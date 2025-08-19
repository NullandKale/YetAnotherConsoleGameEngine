using System;
using System.Collections.Concurrent;

namespace ConsoleGame.RayTracing.Scenes
{
    public static class VoxelMaterialPalette
    {
        public static readonly Func<int, int, Material> MaterialLookup = (id, meta) =>
        {
            var key = Normalize(id, meta);
            return Cache.GetOrAdd(key, CreateMaterial);
        };

        private static readonly ConcurrentDictionary<(int id, int meta), Material> Cache = new ConcurrentDictionary<(int id, int meta), Material>();

        static VoxelMaterialPalette()
        {
            Prewarm();
        }

        private static (int id, int meta) Normalize(int id, int meta)
        {
            switch (id)
            {
                case 0: return (0, 0);
                case 1: return (1, 0);
                case 2: return (2, 0);
                case 3: return (3, 0);
                case 4: return (4, 0);
                case 5: return (5, 0);
                case 6: return (6, 0);
                case 7: return (7, 0);
                case 8: return (8, 0);
                case 9: return (9, Clamp(meta, 0, 2));
                default: return (1, 0);
            }
        }

        private static Material CreateMaterial((int id, int meta) key)
        {
            switch (key.id)
            {
                case 0: return new Material(new Vec3(0.00, 0.00, 0.00), 0.00, 0.00, new Vec3(0.00, 0.00, 0.00));
                case 1: return new Material(new Vec3(0.35, 0.35, 0.38), 0.00, 0.00, new Vec3(0.00, 0.00, 0.00));
                case 2: return new Material(new Vec3(0.45, 0.33, 0.16), 0.00, 0.00, new Vec3(0.00, 0.00, 0.00));
                case 3: return new Material(new Vec3(0.18, 0.52, 0.20), 0.00, 0.00, new Vec3(0.00, 0.00, 0.00));
                case 4: return new Material(new Vec3(0.07, 0.22, 0.50), 0.00, 0.00, new Vec3(0.00, 0.00, 0.00));
                case 5: return new Material(new Vec3(0.86, 0.76, 0.38), 0.00, 0.00, new Vec3(0.00, 0.00, 0.00));
                case 6: return new Material(new Vec3(0.42, 0.22, 0.08), 0.00, 0.00, new Vec3(0.00, 0.00, 0.00));
                case 7: return new Material(new Vec3(0.12, 0.55, 0.16), 0.00, 0.00, new Vec3(0.00, 0.00, 0.00));
                case 8: return new Material(new Vec3(0.95, 0.95, 0.98), 0.00, 0.00, new Vec3(0.00, 0.00, 0.00));
                case 9:
                    {
                        switch (key.meta)
                        {
                            case 0: return new Material(new Vec3(0.15, 0.15, 0.15), 0.00, 0.00, new Vec3(0.00, 0.00, 0.00));
                            case 1: return new Material(new Vec3(0.72, 0.68, 0.62), 0.00, 0.00, new Vec3(0.00, 0.00, 0.00));
                            default: return new Material(new Vec3(0.82, 0.45, 0.18), 0.00, 0.00, new Vec3(0.00, 0.00, 0.00));
                        }
                    }
                default: return new Material(new Vec3(0.50, 0.50, 0.50), 0.00, 0.00, new Vec3(0.00, 0.00, 0.00));
            }
        }

        private static void Prewarm()
        {
            _ = MaterialLookup(1, 0);
            _ = MaterialLookup(2, 0);
            _ = MaterialLookup(3, 0);
            _ = MaterialLookup(4, 0);
            _ = MaterialLookup(5, 0);
            _ = MaterialLookup(6, 0);
            _ = MaterialLookup(7, 0);
            _ = MaterialLookup(8, 0);
            _ = MaterialLookup(9, 0);
            _ = MaterialLookup(9, 1);
            _ = MaterialLookup(9, 2);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo)
                return lo;
            if (v > hi)
                return hi;
            return v;
        }
    }

    public sealed class WorldGenerator
    {
        public void GenerateChunkCells(int cx, int cy, int cz, WorldConfig cfg, (int, int)[,,] cells, out bool anySolid)
        {
            anySolid = false;

            int baseX = cx * cfg.ChunkSize;
            int baseY = cy * cfg.ChunkSize;
            int baseZ = cz * cfg.ChunkSize;

            int size = cfg.ChunkSize;
            int[,] hCache = new int[size, size];
            int[,] wCache = new int[size, size];
            double[,] moistCache = new double[size, size];

            for (int lx = 0; lx < size; lx++)
            {
                int gx = baseX + lx;
                for (int lz = 0; lz < size; lz++)
                {
                    int gz = baseZ + lz;
                    int h = ComputeHeight(gx, gz, cfg);
                    int w = WaterSurfaceLevelAt(gx, gz, h, cfg);
                    hCache[lx, lz] = h;
                    wCache[lx, lz] = w;
                    moistCache[lx, lz] = Moisture01(gx, gz, cfg.WorldSeed);
                }
            }

            for (int lx = 0; lx < size; lx++)
            {
                int gx = baseX + lx;
                for (int lz = 0; lz < size; lz++)
                {
                    int gz = baseZ + lz;
                    int groundHeight = hCache[lx, lz];
                    int localWater = wCache[lx, lz];
                    for (int ly = 0; ly < size; ly++)
                    {
                        int gy = baseY + ly;
                        (int mat, int meta) block = (0, 0);

                        if (gy > groundHeight)
                        {
                            if (gy <= localWater)
                            {
                                block = (4, 0);
                            }
                            else
                            {
                                block = (0, 0);
                            }
                        }
                        else
                        {
                            if (gy == groundHeight)
                            {
                                if (groundHeight > cfg.SnowLevel)
                                {
                                    block = (8, 0);
                                }
                                else if (groundHeight <= localWater + 2)
                                {
                                    block = (5, 0);
                                }
                                else
                                {
                                    block = (3, 0);
                                }
                            }
                            else
                            {
                                if (groundHeight > cfg.SnowLevel)
                                {
                                }
                                else if (gy >= groundHeight - 3)
                                {
                                    block = (2, 0);
                                }
                                if (block.Item1 == 0)
                                {
                                    block = (1, 0);
                                    if (gy > 0)
                                    {
                                        int oreRand = FastHash(gx, gy, gz, cfg.WorldSeed) & 0x3F;
                                        if (oreRand == 0)
                                        {
                                            int oreType = FastHash(gx + 11, gy + 7, gz + 19, cfg.WorldSeed) % 3;
                                            block = (9, oreType);
                                        }
                                    }
                                }
                                if (gy == 0)
                                {
                                    block = (1, 0);
                                }
                            }
                        }

                        if (block.Item1 != 0 && block.Item1 != 4)
                        {
                            if (gy > 0)
                            {
                                double depth = groundHeight - gy;
                                double th = 0.62 + 0.06 * SmoothStep(0.0, 16.0, depth);
                                double caveNoise = CaveCarveNoise(gx, gy, gz, cfg.WorldSeed);
                                if (caveNoise > th)
                                {
                                    block = (0, 0);
                                }
                            }
                        }

                        if (block.Item1 == 2 || block.Item1 == 3)
                        {
                            if (gy <= localWater + 1)
                            {
                                block = (5, 0);
                            }
                        }

                        cells[lx, ly, lz] = block;
                        if (block.Item1 != 0)
                            anySolid = true;
                    }
                }
            }

            for (int lx = 0; lx < size; lx++)
            {
                int gx = baseX + lx;
                for (int lz = 0; lz < size; lz++)
                {
                    int gz = baseZ + lz;
                    int groundHeight = hCache[lx, lz];
                    int localWater = wCache[lx, lz];
                    int groundChunkY = groundHeight / size;
                    if (groundChunkY != cy)
                        continue;
                    if (groundHeight > 0 && groundHeight < cfg.SnowLevel && groundHeight > localWater + 0)
                    {
                        int localGroundY = groundHeight - baseY;
                        if (localGroundY >= 0 && localGroundY < size)
                        {
                            if (cells[lx, localGroundY, lz].Item1 == 3)
                            {
                                if (BlueNoiseTreeCandidate(gx, gz, 11, cfg.WorldSeed))
                                {
                                    double slope = LocalSlope01(hCache, lx, lz, size);
                                    double moist = moistCache[lx, lz];
                                    if (slope < 0.55 && moist > 0.35)
                                    {
                                        if (lx < 2 || lx > size - 3 || lz < 2 || lz > size - 3)
                                        {
                                            continue;
                                        }
                                        int typeSel = (FastHash(gx, groundHeight, gz, cfg.WorldSeed) >> 4) & 1;
                                        if (typeSel == 0)
                                        {
                                            int trunkHeight = 5 + (FastHash(gx, groundHeight, gz, cfg.WorldSeed) % 3);
                                            if (groundHeight + trunkHeight + 2 >= baseY + size)
                                            {
                                                continue;
                                            }
                                            for (int h = 0; h < trunkHeight; h++)
                                            {
                                                int trunkY = localGroundY + h;
                                                cells[lx, trunkY, lz] = (6, 0);
                                            }
                                            int topY = localGroundY + trunkHeight - 1;
                                            for (int ly = 0; ly <= 2; ly++)
                                            {
                                                for (int lxOff = -2; lxOff <= 2; lxOff++)
                                                {
                                                    for (int lzOff = -2; lzOff <= 2; lzOff++)
                                                    {
                                                        int leafX = lx + lxOff;
                                                        int leafY = topY + ly;
                                                        int leafZ = lz + lzOff;
                                                        if (leafX < 0 || leafX >= size || leafY < 0 || leafY >= size || leafZ < 0 || leafZ >= size)
                                                        {
                                                            continue;
                                                        }
                                                        if (lxOff == 0 && lzOff == 0 && ly == 0)
                                                            continue;
                                                        if (Math.Abs(lxOff) == 2 && Math.Abs(lzOff) == 2 && ly == 2)
                                                            continue;
                                                        if (cells[leafX, leafY, leafZ].Item1 == 0)
                                                        {
                                                            cells[leafX, leafY, leafZ] = (7, 0);
                                                        }
                                                    }
                                                }
                                            }
                                            anySolid = true;
                                        }
                                        else
                                        {
                                            int trunkHeight = 7 + (FastHash(gx + 31, groundHeight, gz - 17, cfg.WorldSeed) % 4);
                                            if (groundHeight + trunkHeight + 3 >= baseY + size)
                                            {
                                                continue;
                                            }
                                            for (int h = 0; h < trunkHeight; h++)
                                            {
                                                int trunkY = localGroundY + h;
                                                cells[lx, trunkY, lz] = (6, 0);
                                            }
                                            int layers = 4;
                                            for (int ly = 0; ly < layers; ly++)
                                            {
                                                int radius = Math.Max(0, 3 - ly);
                                                int y = localGroundY + trunkHeight - 1 + ly;
                                                for (int rx = -radius; rx <= radius; rx++)
                                                {
                                                    for (int rz = -radius; rz <= radius; rz++)
                                                    {
                                                        if (Math.Abs(rx) + Math.Abs(rz) > radius + ((ly == layers - 1) ? 0 : 1))
                                                            continue;
                                                        int px = lx + rx;
                                                        int pz = lz + rz;
                                                        if (px < 0 || px >= size || pz < 0 || pz >= size || y < 0 || y >= size)
                                                            continue;
                                                        if (cells[px, y, pz].Item1 == 0)
                                                        {
                                                            cells[px, y, pz] = (7, 0);
                                                        }
                                                    }
                                                }
                                            }
                                            anySolid = true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public (int, int) GetBlockAt(int gx, int gy, int gz, WorldConfig cfg)
        {
            int height = ComputeHeight(gx, gz, cfg);
            int localWater = WaterSurfaceLevelAt(gx, gz, height, cfg);
            (int mat, int meta) block;
            if (gy > height)
            {
                if (gy <= localWater)
                {
                    block = (4, 0);
                }
                else
                {
                    block = (0, 0);
                }
            }
            else
            {
                if (gy == height)
                {
                    if (height > cfg.SnowLevel)
                    {
                        block = (8, 0);
                    }
                    else if (height <= localWater + 2)
                    {
                        block = (5, 0);
                    }
                    else
                    {
                        block = (3, 0);
                    }
                }
                else
                {
                    block = (1, 0);
                    if (height <= cfg.SnowLevel && gy >= height - 3)
                    {
                        block = (2, 0);
                    }
                    if (block.Item1 == 1 && gy > 0)
                    {
                        if ((FastHash(gx, gy, gz, cfg.WorldSeed) & 0x3F) == 0)
                        {
                            int oreType = FastHash(gx + 11, gy + 7, gz + 19, cfg.WorldSeed) % 3;
                            block = (9, oreType);
                        }
                    }
                    if (gy == 0)
                    {
                        block = (1, 0);
                    }
                }
            }
            if (block.Item1 != 0 && block.Item1 != 4 && gy > 0)
            {
                double depth = height - gy;
                double th = 0.62 + 0.06 * SmoothStep(0.0, 16.0, depth);
                if (CaveCarveNoise(gx, gy, gz, cfg.WorldSeed) > th)
                {
                    block = (0, 0);
                }
            }
            if ((block.Item1 == 2 || block.Item1 == 3) && gy <= localWater + 1)
            {
                block = (5, 0);
            }
            return block;
        }

        private static int ComputeHeight(int x, int z, WorldConfig cfg)
        {
            double wx = x;
            double wz = z;

            double warpFreq = 0.0012;
            double warpAmp = 18.0;
            double wx1 = wx + warpAmp * GradientNoise2D(wx * warpFreq, wz * warpFreq, cfg.WorldSeed + 101);
            double wz1 = wz + warpAmp * GradientNoise2D(wx * warpFreq * 1.07, wz * warpFreq * 0.93, cfg.WorldSeed - 77);

            double cont = 0.5 * GradientNoise2D(wx1 * 0.0007, wz1 * 0.0007, cfg.WorldSeed + 1) + 0.5;
            cont = Math.Pow(Saturate(cont), 1.2);

            double ridged = RidgedFBM2D(wx1, wz1, 5, 2.0, 0.5, 0.0018, cfg.WorldSeed + 200);
            ridged = Saturate(ridged);

            double detail = FBM2D(wx1, wz1, 4, 2.0, 0.5, 0.006, cfg.WorldSeed + 300) * 0.5 + 0.5;

            double riverMask = RiverMask01(x, z, cfg.WorldSeed);
            double carve = riverMask;
            double h01 = 0.55 * cont + 0.40 * (ridged * cont) + 0.15 * detail;
            h01 = Math.Max(0.0, h01 - 0.08 * carve);
            h01 = Math.Pow(Saturate(h01), 1.05);

            int baseOffset = cfg.WorldHeight / 5;
            int maxRelief = (int)(cfg.WorldHeight * 0.70);
            int h = baseOffset + (int)(h01 * maxRelief);

            if (h < 1)
                h = 1;
            if (h > cfg.WorldHeight - 2)
                h = cfg.WorldHeight - 2;
            return h;
        }

        private static int WaterSurfaceLevelAt(int gx, int gz, int groundHeight, WorldConfig cfg)
        {
            int sea = cfg.WaterLevel;
            int table = LakeTableHeight(gx, gz, cfg);
            int riverBedDepth = 2 + (FastHash(gx, 17, gz, cfg.WorldSeed) & 1);
            double r = RiverMask01(gx, gz, cfg.WorldSeed);
            int riverSurface = groundHeight - riverBedDepth + 1;
            if (riverSurface < 1)
                riverSurface = 1;
            if (r > 0.60)
                riverSurface = 0;
            int water = sea;
            if (groundHeight < table)
                water = Math.Max(water, table);
            if (riverSurface > 0)
                water = Math.Max(water, riverSurface);
            return water;
        }

        private static double RiverMask01(int x, int z, int seed)
        {
            double freq = 0.00085;
            double wx = x + 23.0 * GradientNoise2D(x * 0.0011, z * 0.0011, seed + 777);
            double wz = z + 19.0 * GradientNoise2D(x * 0.0009, z * 0.0013, seed - 333);
            double v = Math.Abs(GradientNoise2D(wx * freq, wz * freq, seed + 555));
            double m = 1.0 - SmoothStep(0.02, 0.10, v);
            return m;
        }

        private static int LakeTableHeight(int x, int z, WorldConfig cfg)
        {
            double n = FBM2D(x, z, 3, 2.0, 0.5, 0.0005, cfg.WorldSeed + 901) - 0.55;
            int offset = (int)(n * 12.0);
            int table = cfg.WaterLevel + offset;
            if (table < 1)
                table = 1;
            if (table > cfg.WorldHeight - 2)
                table = cfg.WorldHeight - 2;
            return table;
        }

        private static double Moisture01(int x, int z, int seed)
        {
            double m = FBM2D(x * 0.5, z * 0.5, 4, 2.0, 0.5, 0.0012, seed + 321);
            return Saturate(m);
        }

        private static double LocalSlope01(int[,] hCache, int lx, int lz, int size)
        {
            int x0 = Math.Max(0, lx - 1);
            int x1 = Math.Min(size - 1, lx + 1);
            int z0 = Math.Max(0, lz - 1);
            int z1 = Math.Min(size - 1, lz + 1);
            double dx = (hCache[x1, lz] - hCache[x0, lz]) * 0.5;
            double dz = (hCache[lx, z1] - hCache[lx, z0]) * 0.5;
            double g = Math.Sqrt(dx * dx + dz * dz);
            return Saturate(g / 6.0);
        }

        private static double CaveCarveNoise(int x, int y, int z, int seed)
        {
            double n = FBM3D(x * 0.05, y * 0.07, z * 0.05, 4, 2.0, 0.5, seed + 900);
            double r = 0.5 * (1.0 - n);
            if (y < 24)
                r *= 0.75;
            return r;
        }

        private static bool BlueNoiseTreeCandidate(int gx, int gz, int cell, int seed)
        {
            int cx = (int)Math.Floor((double)gx / cell);
            int cz = (int)Math.Floor((double)gz / cell);
            int jx = Math.Abs(FastHash(cx, 0, cz, seed + 12345)) % cell;
            int jz = Math.Abs(FastHash(cx, 1, cz, seed + 54321)) % cell;
            int px = cx * cell + jx;
            int pz = cz * cell + jz;
            if (gx == px && gz == pz)
                return true;
            return false;
        }

        private static double FBM2D(double x, double z, int octaves, double lacunarity, double gain, double baseFreq, int seed)
        {
            double sum = 0.0;
            double amp = 1.0;
            double freq = baseFreq;
            for (int i = 0; i < octaves; i++)
            {
                double n = GradientNoise2D(x * freq, z * freq, seed + i * 131);
                sum += n * amp;
                freq *= lacunarity;
                amp *= gain;
            }
            sum = 0.5 * sum + 0.5;
            return sum;
        }

        private static double RidgedFBM2D(double x, double z, int octaves, double lacunarity, double gain, double baseFreq, int seed)
        {
            double sum = 0.0;
            double amp = 0.5;
            double freq = baseFreq;
            double weight = 1.0;
            for (int i = 0; i < octaves; i++)
            {
                double n = GradientNoise2D(x * freq, z * freq, seed + i * 733);
                n = 1.0 - Math.Abs(n);
                n *= n;
                n *= weight;
                weight = n * gain;
                if (weight > 1.0)
                    weight = 1.0;
                sum += n * amp;
                freq *= lacunarity;
                amp *= 0.5;
            }
            return sum;
        }

        private static double FBM3D(double x, double y, double z, int octaves, double lacunarity, double gain, int seed)
        {
            double sum = 0.0;
            double amp = 1.0;
            double freq = 0.035;
            for (int i = 0; i < octaves; i++)
            {
                double n = GradientNoise3D(x * freq, y * freq, z * freq, seed + i * 197);
                sum += n * amp;
                freq *= lacunarity;
                amp *= gain;
            }
            sum = 0.5 * sum + 0.5;
            return sum;
        }

        private static double GradientNoise2D(double x, double z, int seed)
        {
            int x0 = FastFloor(x);
            int z0 = FastFloor(z);
            int x1 = x0 + 1;
            int z1 = z0 + 1;

            double tx = x - x0;
            double tz = z - z0;

            double u = Fade(tx);
            double v = Fade(tz);

            double n00 = Dot(Grad2(x0, z0, seed), tx, tz);
            double n10 = Dot(Grad2(x1, z0, seed), tx - 1.0, tz);
            double n01 = Dot(Grad2(x0, z1, seed), tx, tz - 1.0);
            double n11 = Dot(Grad2(x1, z1, seed), tx - 1.0, tz - 1.0);

            double ix0 = Lerp(n00, n10, u);
            double ix1 = Lerp(n01, n11, u);
            double val = Lerp(ix0, ix1, v);

            return Clamp(val * 1.41421356237, -1.0, 1.0);
        }

        private static double GradientNoise3D(double x, double y, double z, int seed)
        {
            int x0 = FastFloor(x);
            int y0 = FastFloor(y);
            int z0 = FastFloor(z);
            int x1 = x0 + 1;
            int y1 = y0 + 1;
            int z1 = z0 + 1;

            double tx = x - x0;
            double ty = y - y0;
            double tz = z - z0;

            double u = Fade(tx);
            double v = Fade(ty);
            double w = Fade(tz);

            double n000 = Dot(Grad3(x0, y0, z0, seed), tx, ty, tz);
            double n100 = Dot(Grad3(x1, y0, z0, seed), tx - 1.0, ty, tz);
            double n010 = Dot(Grad3(x0, y1, z0, seed), tx, ty - 1.0, tz);
            double n110 = Dot(Grad3(x1, y1, z0, seed), tx - 1.0, ty - 1.0, tz);
            double n001 = Dot(Grad3(x0, y0, z1, seed), tx, ty, tz - 1.0);
            double n101 = Dot(Grad3(x1, y0, z1, seed), tx - 1.0, ty, tz - 1.0);
            double n011 = Dot(Grad3(x0, y1, z1, seed), tx, ty - 1.0, tz - 1.0);
            double n111 = Dot(Grad3(x1, y1, z1, seed), tx - 1.0, ty - 1.0, tz - 1.0);

            double ix00 = Lerp(n000, n100, u);
            double ix10 = Lerp(n010, n110, u);
            double ix01 = Lerp(n001, n101, u);
            double ix11 = Lerp(n011, n111, u);

            double iy0 = Lerp(ix00, ix10, v);
            double iy1 = Lerp(ix01, ix11, v);

            double val = Lerp(iy0, iy1, w);

            return Clamp(val * 1.15470053838, -1.0, 1.0);
        }

        private static double Fade(double t)
        {
            return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
        }

        private static double[] Grad2(int ix, int iz, int seed)
        {
            int h = FastHash(ix, 0, iz, seed);
            switch ((h >> 13) & 7)
            {
                case 0: return new double[] { 1, 0 };
                case 1: return new double[] { -1, 0 };
                case 2: return new double[] { 0, 1 };
                case 3: return new double[] { 0, -1 };
                case 4: return new double[] { 0.70710678118, 0.70710678118 };
                case 5: return new double[] { -0.70710678118, 0.70710678118 };
                case 6: return new double[] { 0.70710678118, -0.70710678118 };
                default: return new double[] { -0.70710678118, -0.70710678118 };
            }
        }

        private static double[] Grad3(int ix, int iy, int iz, int seed)
        {
            int h = FastHash(ix, iy, iz, seed);
            switch ((h >> 11) & 15)
            {
                case 0: return new double[] { 1, 1, 0 };
                case 1: return new double[] { -1, 1, 0 };
                case 2: return new double[] { 1, -1, 0 };
                case 3: return new double[] { -1, -1, 0 };
                case 4: return new double[] { 1, 0, 1 };
                case 5: return new double[] { -1, 0, 1 };
                case 6: return new double[] { 1, 0, -1 };
                case 7: return new double[] { -1, 0, -1 };
                case 8: return new double[] { 0, 1, 1 };
                case 9: return new double[] { 0, -1, 1 };
                case 10: return new double[] { 0, 1, -1 };
                case 11: return new double[] { 0, -1, -1 };
                case 12: return new double[] { 1, 1, 1 };
                case 13: return new double[] { -1, 1, 1 };
                case 14: return new double[] { 1, -1, 1 };
                default: return new double[] { 1, 1, -1 };
            }
        }

        private static double Dot(double[] g, double x, double z)
        {
            return g[0] * x + g[1] * z;
        }

        private static double Dot(double[] g, double x, double y, double z)
        {
            return g[0] * x + g[1] * y + g[2] * z;
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }

        private static int FastFloor(double t)
        {
            if (t >= 0.0)
                return (int)t;
            return (int)t - 1;
        }

        private static double Clamp(double x, double a, double b)
        {
            if (x < a)
                return a;
            if (x > b)
                return b;
            return x;
        }

        private static double Saturate(double x)
        {
            if (x < 0.0)
                return 0.0;
            if (x > 1.0)
                return 1.0;
            return x;
        }

        private static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = Saturate((x - edge0) / (edge1 - edge0));
            return t * t * (3.0 - 2.0 * t);
        }

        private static int FastHash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = 2166136261u ^ (uint)seed;
                h ^= (uint)x; h *= 16777619u;
                h ^= (uint)y; h *= 16777619u;
                h ^= (uint)z; h *= 16777619u;
                return (int)h;
            }
        }
    }
}
