using System;

namespace ConsoleGame.RayTracing.Scenes
{
    ////////////////////////////////////////////////////////////////////////////////////////////
    // WORLD GENERATION SETTINGS (One place to tune everything at runtime; no recompilation)
    ////////////////////////////////////////////////////////////////////////////////////////////
    public static class WorldGenSettings
    {
        // Block IDs used throughout generation (kept const so they can be used in switch/case).
        public static class Blocks
        {
            public const int Air = 0;
            public const int Stone = 1;
            public const int Dirt = 2;
            public const int Grass = 3;
            public const int Water = 4;
            public const int Sand = 5;
            public const int Wood = 6;
            public const int Leaves = 7;
            public const int Snow = 8;
            public const int Ore = 9;
            public const int TallGrass = 10;
            public const int Flower = 11;
        }

        // Biome thresholds and ground layering.
        public static class Biomes
        {
            public static float DesertMoistureThreshold = 0.30f;
            public static int DesertSandDepth = 5;
            public static float TreeMinMoisture = 0.35f;
            public static float HighAltitudePineBiasDelta = 10.0f;
        }

        // Terrain rules not tied to biomes.
        public static class Terrain
        {
            public static int DirtDepth = 3;
            public static int UnderwaterSandBuffer = 1;
            public static int BeachBuffer = 2;
            public static float BaseOffsetFraction = 0.20f;  // WorldHeight/5
            public static float ReliefFraction = 0.70f;
        }

        // Vegetation placement and shapes.
        public static class Vegetation
        {
            public static int BorderMargin = 2;
            public static float MaxSlopeForTree = 0.65f;
            public static int DenseForestCell = 8;
            public static int MoistForestCell = 10;
            public static int SparseForestCell = 14;
            public static int BroadleafBaseHeight = 5;
            public static int BroadleafRandomHeight = 3;
            public static int BroadleafRadius = 2;
            public static int BroadleafLayers = 3;
            public static int ConiferBaseHeight = 7;
            public static int ConiferRandomHeight = 4;
            public static int ConiferLayers = 4;
            public static int ConiferBaseRadius = 3;
        }

        // Decorative ground cover probabilities (per grass block), by moisture band.
        public static class Plants
        {
            public static float LushFlowerChance = 0.00010f;
            public static float LushGrassChance = 0.00040f;
            public static float MoistFlowerChance = 0.00004f;
            public static float MoistGrassChance = 0.00030f;
            public static float SemiAridFlowerChance = 0.00001f;
            public static float SemiAridGrassChance = 0.00015f;
        }

        // Cave carving and 3D noise shaping.
        public static class Caves
        {
            public static float ThresholdBase = 0.60f;
            public static float ThresholdDepthScale = 0.08f;
            public static float DepthSmoothMax = 16.0f;
            public static int BoostBelowY = 24;
            public static float CarveDepthFactor = 0.80f;
            public static float FBMInputScaleX = 0.05f;
            public static float FBMInputScaleY = 0.07f;
            public static float FBMInputScaleZ = 0.05f;
            public static int FBMOctaves = 4;
            public static float FBMLacunarity = 2.0f;
            public static float FBMGain = 0.5f;
            public static int FBMSeedOffset = 900;
            public static int SurfaceSafetyShell = 2;
        }

        // Heightmap composition (continents, ridges, detail, and river carve).
        public static class Heightmap
        {
            public static float WarpFrequency = 0.0016f;
            public static float WarpAmplitude = 24.0f;
            public static float ContinentFreq = 0.0010f;
            public static float ContinentExponent = 0.95f;
            public static int RidgedOctaves = 5;
            public static float RidgedLacunarity = 2.0f;
            public static float RidgedGain = 0.5f;
            public static float RidgedBaseFreq = 0.0022f;
            public static int DetailOctaves = 4;
            public static float DetailBaseFreq = 0.009f;
            public static float ExtraNoiseAmp = 0.06f;
            public static float ExtraNoiseBaseFreq = 0.012f;
            public static float RiverCarveStrength = 0.05f;
            public static float OceanBiasStrength = 0.12f;     // ↑ to ensure some land falls below sea
            public static float OceanBiasMaxContinent = 0.50f;

            // Narrower river mask used only for height carving (avoids giant valleys).
            public static float RiverEdgeMinForHeight = 0.003f;
            public static float RiverEdgeMaxForHeight = 0.010f;
            public static float RiverBandPowerForHeight = 4.0f;
        }

        // Rivers and lakes.
        public static class Water
        {
            // River pathing.
            public static float RiverFreq = 0.00085f;
            public static float RiverWarp1 = 23.0f;
            public static float RiverWarp2 = 19.0f;
            public static float RiverInputFreqX1 = 0.0011f;
            public static float RiverInputFreqZ1 = 0.0011f;
            public static float RiverInputFreqX2 = 0.0009f;
            public static float RiverInputFreqZ2 = 0.0013f;
            public static float RiverSmoothEdgeMin = 0.03f;
            public static float RiverSmoothEdgeMax = 0.08f;
            public static float RiverActiveThreshold = 0.92f;  // kept for "near river" heuristics if needed
            public static float RiverBandPower = 6.0f;         // display-only mask shaping
            public static int RiverTargetWidthBlocks = 8;      // ← total target width for carving/sand/water
            public static int RiverBedDepthBase = 3;
            public static int RiverBedDepthRandMask = 1;
            public static int RiverWaterDepth = 2;             // ← fixed water depth inside river core
            // Lakes (flat table water inside basins).
            public static int LakeOctaves = 3;
            public static float LakeBaseFreq = 0.0005f;
            public static float LakeBias = -0.35f;
            public static float LakeAmplitude = 18.0f;
            public static float LakeLacunarity = 2.0f;
            public static float LakeGain = 0.5f;

            // Enforce a sane flat sea level if cfg.WaterLevel is unusable.
            public static float MinSeaLevelFraction = 0.30f; // 30% of world height
        }

        // Moisture field (drives biomes and vegetation density).
        public static class Moisture
        {
            public static float CoordScale = 0.5f;
            public static int Octaves = 4;
            public static float Lacunarity = 2.0f;
            public static float Gain = 0.5f;
            public static float BaseFreq = 0.0012f;
        }

        // Blue-noise placement (tree candidate jitter).
        public static class BlueNoise
        {
            public static int JitterSeedOffsetX = 12345;
            public static int JitterSeedOffsetZ = 54321;
        }

        // Ore distribution.
        public static class Ore
        {
            public static int ChanceMask = 0x3F; // ~1/64
            public static int TypeModulo = 3;
            public static int TypeHashOffsetX = 11;
            public static int TypeHashOffsetY = 7;
            public static int TypeHashOffsetZ = 19;
        }

        // Normalization constants and math helpers shared by noise.
        public static class Normalization
        {
            public const float InvSqrt2 = 0.70710678118f;
            public const float Perlin2D = 1.41421356237f;
            public const float Perlin3D = 1.15470053838f;
            public static float SlopeNormalize = 6.0f;
            public static float FBM3DBaseFrequency = 0.035f;
        }

        // Hashing constants (FNV-1a 32-bit).
        public static class Hashing
        {
            public const uint FnvOffset = 2166136261u;
            public const uint FnvPrime = 16777619u;
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
            float[,] moistCache = new float[size, size];
            float[,] riverMaskCache = new float[size, size];
            bool[,] riverCoreCache = new bool[size, size];
            int[,] riverBedYCache = new int[size, size];
            int[,] riverSurfYCache = new int[size, size]; // per-column river water surface inside core

            for (int lx = 0; lx < size; lx++)
            {
                int gx = baseX + lx;
                for (int lz = 0; lz < size; lz++)
                {
                    int gz = baseZ + lz;

                    int h = ComputeHeight(gx, gz, cfg);
                    float rMask = RiverMask01(gx, gz, cfg.WorldSeed);

                    int bedDepth = WorldGenSettings.Water.RiverBedDepthBase + (FastHash(gx, 17, gz, cfg.WorldSeed) & WorldGenSettings.Water.RiverBedDepthRandMask);
                    int riverBedY = Math.Max(1, h - bedDepth);

                    int w = WaterSurfaceLevelAt(gx, gz, h, cfg);
                    bool inCore = InRiverCoreBand(gx, gz, cfg.WorldSeed, WorldGenSettings.Water.RiverTargetWidthBlocks);

                    int coreSurf = Math.Min(h, Math.Max(w, riverBedY + WorldGenSettings.Water.RiverWaterDepth));

                    hCache[lx, lz] = h;
                    wCache[lx, lz] = w;
                    moistCache[lx, lz] = Moisture01(gx, gz, cfg.WorldSeed);
                    riverMaskCache[lx, lz] = rMask;
                    riverCoreCache[lx, lz] = inCore;
                    riverBedYCache[lx, lz] = riverBedY;
                    riverSurfYCache[lx, lz] = coreSurf;
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
                    bool riverCore = riverCoreCache[lx, lz];
                    int riverBedY = riverBedYCache[lx, lz];
                    int riverSurfY = riverSurfYCache[lx, lz];
                    bool riverHasWater = riverCore && (riverSurfY > riverBedY);

                    for (int ly = 0; ly < size; ly++)
                    {
                        int gy = baseY + ly;
                        (int mat, int meta) block = (0, 0);

                        if (gy > groundHeight)
                        {
                            block = (gy <= localWater) ? (WorldGenSettings.Blocks.Water, 0) : (WorldGenSettings.Blocks.Air, 0);
                        }
                        else
                        {
                            if (gy == groundHeight)
                            {
                                if (groundHeight > cfg.SnowLevel)
                                {
                                    block = (WorldGenSettings.Blocks.Snow, 0);
                                }
                                else if (groundHeight <= localWater + WorldGenSettings.Terrain.BeachBuffer)
                                {
                                    block = (WorldGenSettings.Blocks.Sand, 0);
                                }
                                else if (moistCache[lx, lz] < WorldGenSettings.Biomes.DesertMoistureThreshold)
                                {
                                    block = (WorldGenSettings.Blocks.Sand, 0);
                                }
                                else
                                {
                                    block = (WorldGenSettings.Blocks.Grass, 0);
                                }
                            }
                            else
                            {
                                if (groundHeight > cfg.SnowLevel)
                                {
                                }
                                else
                                {
                                    if (moistCache[lx, lz] < WorldGenSettings.Biomes.DesertMoistureThreshold)
                                    {
                                        if (gy >= groundHeight - WorldGenSettings.Biomes.DesertSandDepth)
                                            block = (WorldGenSettings.Blocks.Sand, 0);
                                    }
                                    else
                                    {
                                        if (gy >= groundHeight - WorldGenSettings.Terrain.DirtDepth)
                                            block = (WorldGenSettings.Blocks.Dirt, 0);
                                    }
                                }

                                if (block.Item1 == 0)
                                {
                                    block = (WorldGenSettings.Blocks.Stone, 0);
                                    if (gy > 0)
                                    {
                                        int oreRand = FastHash(gx, gy, gz, cfg.WorldSeed) & WorldGenSettings.Ore.ChanceMask;
                                        if (oreRand == 0)
                                        {
                                            int oreType = FastHash(gx + WorldGenSettings.Ore.TypeHashOffsetX, gy + WorldGenSettings.Ore.TypeHashOffsetY, gz + WorldGenSettings.Ore.TypeHashOffsetZ, cfg.WorldSeed) % WorldGenSettings.Ore.TypeModulo;
                                            block = (WorldGenSettings.Blocks.Ore, oreType);
                                        }
                                    }
                                    if (gy == 0)
                                        block = (WorldGenSettings.Blocks.Stone, 0);
                                }
                            }
                        }

                        if (riverCore)
                        {
                            if (gy == riverBedY)
                            {
                                block = (WorldGenSettings.Blocks.Sand, 0);
                            }
                            else if (gy > riverBedY && gy <= groundHeight)
                            {
                                if (riverHasWater && gy <= riverSurfY)
                                    block = (WorldGenSettings.Blocks.Water, 0);
                                else
                                    block = (WorldGenSettings.Blocks.Air, 0);
                            }
                        }

                        if (block.Item1 != 0 && block.Item1 != WorldGenSettings.Blocks.Water)
                        {
                            if (gy > 0 && gy <= groundHeight - WorldGenSettings.Caves.SurfaceSafetyShell)
                            {
                                float depth = groundHeight - gy;
                                float th = WorldGenSettings.Caves.ThresholdBase
                                          + WorldGenSettings.Caves.ThresholdDepthScale
                                          * SmoothStep(0.0f, WorldGenSettings.Caves.DepthSmoothMax, depth);
                                float carveField = CaveCarveNoise(gx, gy, gz, cfg.WorldSeed);
                                if (carveField > th)
                                    block = (WorldGenSettings.Blocks.Air, 0);
                            }
                        }

                        if ((block.Item1 == WorldGenSettings.Blocks.Dirt || block.Item1 == WorldGenSettings.Blocks.Grass) &&
                            gy <= localWater + WorldGenSettings.Terrain.UnderwaterSandBuffer)
                        {
                            block = (WorldGenSettings.Blocks.Sand, 0);
                        }

                        cells[lx, ly, lz] = block;
                        if (block.Item1 != 0) anySolid = true;
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
                    bool nearRiver = riverCoreCache[lx, lz];

                    if (groundHeight / size != cy) continue;
                    if (!(groundHeight > 0 && groundHeight < cfg.SnowLevel && groundHeight > localWater && !nearRiver))
                        continue;

                    int localGroundY = groundHeight - baseY;
                    if (localGroundY < 0 || localGroundY >= size) continue;

                    if (cells[lx, localGroundY, lz].Item1 == WorldGenSettings.Blocks.Grass)
                    {
                        float moist = moistCache[lx, lz];
                        if (moist > WorldGenSettings.Biomes.TreeMinMoisture)
                        {
                            int cellSize = (moist > 0.8) ? WorldGenSettings.Vegetation.DenseForestCell
                                         : ((moist > 0.6) ? WorldGenSettings.Vegetation.MoistForestCell
                                                          : WorldGenSettings.Vegetation.SparseForestCell);
                            if (BlueNoiseTreeCandidate(gx, gz, cellSize, cfg.WorldSeed))
                            {
                                float slope = LocalSlope01(hCache, lx, lz, size);
                                if (slope < WorldGenSettings.Vegetation.MaxSlopeForTree)
                                {
                                    if (lx < WorldGenSettings.Vegetation.BorderMargin || lx > size - 1 - WorldGenSettings.Vegetation.BorderMargin ||
                                        lz < WorldGenSettings.Vegetation.BorderMargin || lz > size - 1 - WorldGenSettings.Vegetation.BorderMargin)
                                        continue;

                                    int typeSel;
                                    if (groundHeight > cfg.SnowLevel - (int)WorldGenSettings.Biomes.HighAltitudePineBiasDelta)
                                        typeSel = 1;
                                    else if (moist > 0.8)
                                        typeSel = 0;
                                    else
                                        typeSel = (FastHash(gx, groundHeight, gz, cfg.WorldSeed) >> 4) & 1;

                                    if (typeSel == 0)
                                    {
                                        int trunkHeight = WorldGenSettings.Vegetation.BroadleafBaseHeight
                                                        + (FastHash(gx, groundHeight, gz, cfg.WorldSeed) % WorldGenSettings.Vegetation.BroadleafRandomHeight);
                                        if (groundHeight + trunkHeight + WorldGenSettings.Vegetation.BroadleafLayers >= baseY + size)
                                            continue;
                                        for (int h = 0; h < trunkHeight; h++)
                                        {
                                            int trunkY = localGroundY + h;
                                            cells[lx, trunkY, lz] = (WorldGenSettings.Blocks.Wood, 0);
                                        }
                                        int topY = localGroundY + trunkHeight - 1;
                                        for (int ly = 0; ly < WorldGenSettings.Vegetation.BroadleafLayers; ly++)
                                        {
                                            for (int lxOff = -WorldGenSettings.Vegetation.BroadleafRadius; lxOff <= WorldGenSettings.Vegetation.BroadleafRadius; lxOff++)
                                            {
                                                for (int lzOff = -WorldGenSettings.Vegetation.BroadleafRadius; lzOff <= WorldGenSettings.Vegetation.BroadleafRadius; lzOff++)
                                                {
                                                    int leafX = lx + lxOff;
                                                    int leafY = topY + ly;
                                                    int leafZ = lz + lzOff;
                                                    if (leafX < 0 || leafX >= size || leafY < 0 || leafY >= size || leafZ < 0 || leafZ >= size)
                                                        continue;
                                                    if (lxOff == 0 && lzOff == 0 && ly == 0) continue;
                                                    if (MathF.Abs(lxOff) == WorldGenSettings.Vegetation.BroadleafRadius &&
                                                        MathF.Abs(lzOff) == WorldGenSettings.Vegetation.BroadleafRadius &&
                                                        ly == WorldGenSettings.Vegetation.BroadleafLayers - 1)
                                                        continue;
                                                    if (cells[leafX, leafY, leafZ].Item1 == WorldGenSettings.Blocks.Air)
                                                        cells[leafX, leafY, leafZ] = (WorldGenSettings.Blocks.Leaves, 0);
                                                }
                                            }
                                        }
                                        anySolid = true;
                                    }
                                    else
                                    {
                                        int trunkHeight = WorldGenSettings.Vegetation.ConiferBaseHeight
                                                        + (FastHash(gx + 31, groundHeight, gz - 17, cfg.WorldSeed) % WorldGenSettings.Vegetation.ConiferRandomHeight);
                                        if (groundHeight + trunkHeight + WorldGenSettings.Vegetation.ConiferLayers >= baseY + size)
                                            continue;
                                        for (int h = 0; h < trunkHeight; h++)
                                        {
                                            int trunkY = localGroundY + h;
                                            cells[lx, trunkY, lz] = (WorldGenSettings.Blocks.Wood, 0);
                                        }
                                        for (int ly = 0; ly < WorldGenSettings.Vegetation.ConiferLayers; ly++)
                                        {
                                            int radius = Math.Max(0, WorldGenSettings.Vegetation.ConiferBaseRadius - ly);
                                            int y = localGroundY + trunkHeight - 1 + ly;
                                            for (int rx = -radius; rx <= radius; rx++)
                                            {
                                                for (int rz = -radius; rz <= radius; rz++)
                                                {
                                                    if (MathF.Abs(rx) + MathF.Abs(rz) > radius + ((ly == WorldGenSettings.Vegetation.ConiferLayers - 1) ? 0 : 1))
                                                        continue;
                                                    int px = lx + rx;
                                                    int pz = lz + rz;
                                                    if (px < 0 || px >= size || pz < 0 || pz >= size || y < 0 || y >= size)
                                                        continue;
                                                    if (cells[px, y, pz].Item1 == WorldGenSettings.Blocks.Air)
                                                        cells[px, y, pz] = (WorldGenSettings.Blocks.Leaves, 0);
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

        public (int, int) GetBlockAt(int gx, int gy, int gz, WorldConfig cfg)
        {
            int height = ComputeHeight(gx, gz, cfg);
            int localWater = WaterSurfaceLevelAt(gx, gz, height, cfg);
            float moisture = Moisture01(gx, gz, cfg.WorldSeed);
            bool riverCore = InRiverCoreBand(gx, gz, cfg.WorldSeed, WorldGenSettings.Water.RiverTargetWidthBlocks);

            int bedDepth = WorldGenSettings.Water.RiverBedDepthBase +
                           (FastHash(gx, 17, gz, cfg.WorldSeed) & WorldGenSettings.Water.RiverBedDepthRandMask);
            int riverBedY = Math.Max(1, height - bedDepth);
            int riverSurfY = Math.Min(height, Math.Max(localWater, riverBedY + WorldGenSettings.Water.RiverWaterDepth));
            bool riverHasWater = riverCore && (riverSurfY > riverBedY);

            (int mat, int meta) block;
            if (gy > height)
            {
                block = (gy <= localWater) ? (WorldGenSettings.Blocks.Water, 0) : (WorldGenSettings.Blocks.Air, 0);
            }
            else
            {
                if (riverCore)
                {
                    if (gy == riverBedY) return (WorldGenSettings.Blocks.Sand, 0);
                    if (gy > riverBedY && gy <= height)
                    {
                        if (riverHasWater && gy <= riverSurfY) return (WorldGenSettings.Blocks.Water, 0);
                        return (WorldGenSettings.Blocks.Air, 0);
                    }
                }

                if (gy == height)
                {
                    if (height > cfg.SnowLevel) block = (WorldGenSettings.Blocks.Snow, 0);
                    else if (height <= localWater + WorldGenSettings.Terrain.BeachBuffer) block = (WorldGenSettings.Blocks.Sand, 0);
                    else if (moisture < WorldGenSettings.Biomes.DesertMoistureThreshold) block = (WorldGenSettings.Blocks.Sand, 0);
                    else block = (WorldGenSettings.Blocks.Grass, 0);
                }
                else
                {
                    block = (WorldGenSettings.Blocks.Stone, 0);
                    if (height <= cfg.SnowLevel)
                    {
                        if (moisture < WorldGenSettings.Biomes.DesertMoistureThreshold)
                        {
                            if (gy >= height - WorldGenSettings.Biomes.DesertSandDepth) block = (WorldGenSettings.Blocks.Sand, 0);
                        }
                        else
                        {
                            if (gy >= height - WorldGenSettings.Terrain.DirtDepth) block = (WorldGenSettings.Blocks.Dirt, 0);
                        }
                    }
                    if (block.Item1 == WorldGenSettings.Blocks.Stone && gy > 0)
                    {
                        if ((FastHash(gx, gy, gz, cfg.WorldSeed) & WorldGenSettings.Ore.ChanceMask) == 0)
                        {
                            int oreType = FastHash(gx + WorldGenSettings.Ore.TypeHashOffsetX,
                                                   gy + WorldGenSettings.Ore.TypeHashOffsetY,
                                                   gz + WorldGenSettings.Ore.TypeHashOffsetZ,
                                                   cfg.WorldSeed) % WorldGenSettings.Ore.TypeModulo;
                            block = (WorldGenSettings.Blocks.Ore, oreType);
                        }
                    }
                    if (gy == 0) block = (WorldGenSettings.Blocks.Stone, 0);
                }
            }

            if (block.Item1 != 0 && block.Item1 != WorldGenSettings.Blocks.Water &&
                gy > 0 && gy <= height - WorldGenSettings.Caves.SurfaceSafetyShell)
            {
                float depth = height - gy;
                float th = WorldGenSettings.Caves.ThresholdBase +
                            WorldGenSettings.Caves.ThresholdDepthScale *
                            SmoothStep(0.0f, WorldGenSettings.Caves.DepthSmoothMax, depth);
                if (CaveCarveNoise(gx, gy, gz, cfg.WorldSeed) > th)
                    block = (WorldGenSettings.Blocks.Air, 0);
            }

            if ((block.Item1 == WorldGenSettings.Blocks.Dirt || block.Item1 == WorldGenSettings.Blocks.Grass) &&
                gy <= localWater + WorldGenSettings.Terrain.UnderwaterSandBuffer)
            {
                block = (WorldGenSettings.Blocks.Sand, 0);
            }
            return block;
        }

        private static int ComputeHeight(int x, int z, WorldConfig cfg)
        {
            float wx = x;
            float wz = z;
            float warpFreq = WorldGenSettings.Heightmap.WarpFrequency;
            float warpAmp = WorldGenSettings.Heightmap.WarpAmplitude;
            float wx1 = wx + warpAmp * GradientNoise2D(wx * warpFreq, wz * warpFreq, cfg.WorldSeed + 101);
            float wz1 = wz + warpAmp * GradientNoise2D(wx * warpFreq * 1.07f, wz * warpFreq * 0.93f, cfg.WorldSeed - 77);

            float cont = 0.5f * GradientNoise2D(wx1 * WorldGenSettings.Heightmap.ContinentFreq, wz1 * WorldGenSettings.Heightmap.ContinentFreq, cfg.WorldSeed + 1) + 0.5f;
            cont = MathF.Pow(Saturate(cont), WorldGenSettings.Heightmap.ContinentExponent);

            float ridged = RidgedFBM2D(wx1, wz1, WorldGenSettings.Heightmap.RidgedOctaves, WorldGenSettings.Heightmap.RidgedLacunarity, WorldGenSettings.Heightmap.RidgedGain, WorldGenSettings.Heightmap.RidgedBaseFreq, cfg.WorldSeed + 200);
            ridged = Saturate(ridged);

            float detail = FBM2D(wx1, wz1, WorldGenSettings.Heightmap.DetailOctaves, 2.0f, 0.5f, WorldGenSettings.Heightmap.DetailBaseFreq, cfg.WorldSeed + 300) * 0.5f + 0.5f;

            float riverMask = RiverMask01ForHeight(x, z, cfg.WorldSeed);
            float h01 = 0.42f * cont + 0.40f * ridged + 0.18f * detail;
            h01 = MathF.Max(0.0f, h01 - WorldGenSettings.Heightmap.RiverCarveStrength * riverMask);
            h01 += WorldGenSettings.Heightmap.ExtraNoiseAmp * (FBM2D(wx1, wz1, 2, 2.0f, 0.5f, WorldGenSettings.Heightmap.ExtraNoiseBaseFreq, cfg.WorldSeed + 444) - 0.5f);

            float oceanBias = WorldGenSettings.Heightmap.OceanBiasStrength * (1.0f - SmoothStep(0.0f, WorldGenSettings.Heightmap.OceanBiasMaxContinent, cont));
            h01 = MathF.Max(0.0f, h01 - oceanBias);

            h01 = Saturate(h01);
            h01 = SmoothStep(0.0f, 1.0f, h01);

            int baseOffset = (int)(cfg.WorldHeight * WorldGenSettings.Terrain.BaseOffsetFraction);
            int maxRelief = (int)(cfg.WorldHeight * WorldGenSettings.Terrain.ReliefFraction);
            int h = baseOffset + (int)(h01 * maxRelief);

            if (h < 1) h = 1;
            if (h > cfg.WorldHeight - 2) h = cfg.WorldHeight - 2;
            return h;
        }

        private static int WaterSurfaceLevelAt(int gx, int gz, int groundHeight, WorldConfig cfg)
        {
            int sea = EffectiveSeaLevel(cfg);
            int table = LakeTableHeight(gx, gz, cfg);
            int water = sea;
            if (groundHeight < table) water = Math.Max(water, table);
            return water;
        }

        private static int EffectiveSeaLevel(WorldConfig cfg)
        {
            int minSea = Math.Max(2, (int)(cfg.WorldHeight * WorldGenSettings.Water.MinSeaLevelFraction));
            int sea = cfg.WaterLevel;
            if (sea < minSea || sea >= cfg.WorldHeight - 2)
                sea = minSea;
            return sea;
        }

        private static float RiverMask01(int x, int z, int seed)
        {
            float freq = WorldGenSettings.Water.RiverFreq;
            float wx = x + WorldGenSettings.Water.RiverWarp1 * GradientNoise2D(x * WorldGenSettings.Water.RiverInputFreqX1, z * WorldGenSettings.Water.RiverInputFreqZ1, seed + 777);
            float wz = z + WorldGenSettings.Water.RiverWarp2 * GradientNoise2D(x * WorldGenSettings.Water.RiverInputFreqX2, z * WorldGenSettings.Water.RiverInputFreqZ2, seed - 333);
            float v = MathF.Abs(GradientNoise2D(wx * freq, wz * freq, seed + 555));
            float m = 1.0f - SmoothStep(WorldGenSettings.Water.RiverSmoothEdgeMin, WorldGenSettings.Water.RiverSmoothEdgeMax, v);
            m = MathF.Pow(Saturate(m), WorldGenSettings.Water.RiverBandPower);
            return m;
        }

        private static float RiverMask01ForHeight(int x, int z, int seed)
        {
            float freq = WorldGenSettings.Water.RiverFreq;
            float wx = x + WorldGenSettings.Water.RiverWarp1 * GradientNoise2D(x * WorldGenSettings.Water.RiverInputFreqX1, z * WorldGenSettings.Water.RiverInputFreqZ1, seed + 777);
            float wz = z + WorldGenSettings.Water.RiverWarp2 * GradientNoise2D(x * WorldGenSettings.Water.RiverInputFreqX2, z * WorldGenSettings.Water.RiverInputFreqZ2, seed - 333);
            float v = MathF.Abs(GradientNoise2D(wx * freq, wz * freq, seed + 555));
            float m = 1.0f - SmoothStep(WorldGenSettings.Heightmap.RiverEdgeMinForHeight, WorldGenSettings.Heightmap.RiverEdgeMaxForHeight, v);
            m = MathF.Pow(Saturate(m), WorldGenSettings.Heightmap.RiverBandPowerForHeight);
            return m;
        }

        private static float RiverCenterSignal(int x, int z, int seed)
        {
            float freq = WorldGenSettings.Water.RiverFreq;
            float wx = x + WorldGenSettings.Water.RiverWarp1 * GradientNoise2D(x * WorldGenSettings.Water.RiverInputFreqX1, z * WorldGenSettings.Water.RiverInputFreqZ1, seed + 777);
            float wz = z + WorldGenSettings.Water.RiverWarp2 * GradientNoise2D(x * WorldGenSettings.Water.RiverInputFreqX2, z * WorldGenSettings.Water.RiverInputFreqZ2, seed - 333);
            return GradientNoise2D(wx * freq, wz * freq, seed + 555);
        }

        private static bool InRiverCoreBand(int x, int z, int seed, int targetWidthBlocks)
        {
            if (targetWidthBlocks <= 1) return MathF.Abs(RiverCenterSignal(x, z, seed)) < 1e-3f;
            float s = RiverCenterSignal(x, z, seed);
            float sx1 = RiverCenterSignal(x + 1, z, seed);
            float sx0 = RiverCenterSignal(x - 1, z, seed);
            float sz1 = RiverCenterSignal(x, z + 1, seed);
            float sz0 = RiverCenterSignal(x, z - 1, seed);
            float gx = 0.5f * (sx1 - sx0);
            float gz = 0.5f * (sz1 - sz0);
            float g = MathF.Max(1e-4f, MathF.Sqrt(gx * gx + gz * gz));
            float halfW = 0.5f * targetWidthBlocks;
            return MathF.Abs(s) <= g * halfW;
        }

        private static int LakeTableHeight(int x, int z, WorldConfig cfg)
        {
            float n = FBM2D(x, z, WorldGenSettings.Water.LakeOctaves, WorldGenSettings.Water.LakeLacunarity, WorldGenSettings.Water.LakeGain, WorldGenSettings.Water.LakeBaseFreq, cfg.WorldSeed + 901) + WorldGenSettings.Water.LakeBias;
            int offset = (int)(n * WorldGenSettings.Water.LakeAmplitude);
            int table = EffectiveSeaLevel(cfg) + offset;
            if (table < 1) table = 1;
            if (table > cfg.WorldHeight - 2) table = cfg.WorldHeight - 2;
            return table;
        }

        private static float Moisture01(int x, int z, int seed)
        {
            float m = FBM2D(x * WorldGenSettings.Moisture.CoordScale, z * WorldGenSettings.Moisture.CoordScale, WorldGenSettings.Moisture.Octaves, WorldGenSettings.Moisture.Lacunarity, WorldGenSettings.Moisture.Gain, WorldGenSettings.Moisture.BaseFreq, seed + 321);
            return Saturate(m);
        }

        private static float LocalSlope01(int[,] hCache, int lx, int lz, int size)
        {
            int x0 = Math.Max(0, lx - 1);
            int x1 = Math.Min(size - 1, lx + 1);
            int z0 = Math.Max(0, lz - 1);
            int z1 = Math.Min(size - 1, lz + 1);
            float dx = (hCache[x1, lz] - hCache[x0, lz]) * 0.5f;
            float dz = (hCache[lx, z1] - hCache[lx, z0]) * 0.5f;
            float g = MathF.Sqrt(dx * dx + dz * dz);
            return Saturate(g / WorldGenSettings.Normalization.SlopeNormalize);
        }

        private static float CaveCarveNoise(int x, int y, int z, int seed)
        {
            float n = FBM3D(
                x * WorldGenSettings.Caves.FBMInputScaleX,
                y * WorldGenSettings.Caves.FBMInputScaleY,
                z * WorldGenSettings.Caves.FBMInputScaleZ,
                WorldGenSettings.Caves.FBMOctaves,
                WorldGenSettings.Caves.FBMLacunarity,
                WorldGenSettings.Caves.FBMGain,
                seed + WorldGenSettings.Caves.FBMSeedOffset);
            if (y < WorldGenSettings.Caves.BoostBelowY)
                n *= WorldGenSettings.Caves.CarveDepthFactor;
            return n; // 0..1, higher => carve
        }

        private static bool BlueNoiseTreeCandidate(int gx, int gz, int cell, int seed)
        {
            int cx = (int)MathF.Floor((float)gx / cell);
            int cz = (int)MathF.Floor((float)gz / cell);
            int jx = Math.Abs(FastHash(cx, 0, cz, seed + WorldGenSettings.BlueNoise.JitterSeedOffsetX)) % cell;
            int jz = Math.Abs(FastHash(cx, 1, cz, seed + WorldGenSettings.BlueNoise.JitterSeedOffsetZ)) % cell;
            int px = cx * cell + jx;
            int pz = cz * cell + jz;
            return (gx == px && gz == pz);
        }

        private static float FBM2D(float x, float z, int octaves, float lacunarity, float gain, float baseFreq, int seed)
        {
            float sum = 0.0f, amp = 1.0f, freq = baseFreq;
            for (int i = 0; i < octaves; i++)
            {
                float n = GradientNoise2D(x * freq, z * freq, seed + i * 131);
                sum += n * amp;
                freq *= lacunarity;
                amp *= gain;
            }
            return 0.5f * sum + 0.5f;
        }

        private static float RidgedFBM2D(float x, float z, int octaves, float lacunarity, float gain, float baseFreq, int seed)
        {
            float sum = 0.0f, amp = 0.5f, freq = baseFreq, weight = 1.0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = GradientNoise2D(x * freq, z * freq, seed + i * 733);
                n = 1.0f - MathF.Abs(n);
                n *= n;
                n *= weight;
                weight = n * gain;
                if (weight > 1.0) weight = 1.0f;
                sum += n * amp;
                freq *= lacunarity;
                amp *= 0.5f;
            }
            return sum;
        }

        private static float FBM3D(float x, float y, float z, int octaves, float lacunarity, float gain, int seed)
        {
            float sum = 0.0f, amp = 1.0f, freq = WorldGenSettings.Normalization.FBM3DBaseFrequency;
            for (int i = 0; i < octaves; i++)
            {
                float n = GradientNoise3D(x * freq, y * freq, z * freq, seed + i * 197);
                sum += n * amp;
                freq *= lacunarity;
                amp *= gain;
            }
            return 0.5f * sum + 0.5f;
        }

        private static float GradientNoise2D(float x, float z, int seed)
        {
            int x0 = FastFloor(x);
            int z0 = FastFloor(z);
            int x1 = x0 + 1;
            int z1 = z0 + 1;

            float tx = x - x0;
            float tz = z - z0;

            float u = Fade(tx);
            float v = Fade(tz);

            float n00 = Dot(Grad2(x0, z0, seed), tx, tz);
            float n10 = Dot(Grad2(x1, z0, seed), tx - 1.0f, tz);
            float n01 = Dot(Grad2(x0, z1, seed), tx, tz - 1.0f);
            float n11 = Dot(Grad2(x1, z1, seed), tx - 1.0f, tz - 1.0f);

            float ix0 = Lerp(n00, n10, u);
            float ix1 = Lerp(n01, n11, u);
            float val = Lerp(ix0, ix1, v);

            return Clamp(val * WorldGenSettings.Normalization.Perlin2D, -1.0f, 1.0f);
        }

        private static float GradientNoise3D(float x, float y, float z, int seed)
        {
            int x0 = FastFloor(x), y0 = FastFloor(y), z0 = FastFloor(z);
            int x1 = x0 + 1, y1 = y0 + 1, z1 = z0 + 1;

            float tx = x - x0, ty = y - y0, tz = z - z0;
            float u = Fade(tx), v = Fade(ty), w = Fade(tz);

            float n000 = Dot(Grad3(x0, y0, z0, seed), tx, ty, tz);
            float n100 = Dot(Grad3(x1, y0, z0, seed), tx - 1.0f, ty, tz);
            float n010 = Dot(Grad3(x0, y1, z0, seed), tx, ty - 1.0f, tz);
            float n110 = Dot(Grad3(x1, y1, z0, seed), tx - 1.0f, ty - 1.0f, tz);
            float n001 = Dot(Grad3(x0, y0, z1, seed), tx, ty, tz - 1.0f);
            float n101 = Dot(Grad3(x1, y0, z1, seed), tx - 1.0f, ty, tz - 1.0f);
            float n011 = Dot(Grad3(x0, y1, z1, seed), tx, ty - 1.0f, tz - 1.0f);
            float n111 = Dot(Grad3(x1, y1, z1, seed), tx - 1.0f, ty - 1.0f, tz - 1.0f);

            float ix00 = Lerp(n000, n100, u);
            float ix10 = Lerp(n010, n110, u);
            float ix01 = Lerp(n001, n101, u);
            float ix11 = Lerp(n011, n111, u);

            float iy0 = Lerp(ix00, ix10, v);
            float iy1 = Lerp(ix01, ix11, v);

            float val = Lerp(iy0, iy1, w);

            return Clamp(val * WorldGenSettings.Normalization.Perlin3D, -1.0f, 1.0f);
        }

        private static float Fade(float t)
        {
            return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
        }

        private static float[] Grad2(int ix, int iz, int seed)
        {
            int h = FastHash(ix, 0, iz, seed);
            switch ((h >> 13) & 7)
            {
                case 0: return new float[] { 1, 0 };
                case 1: return new float[] { -1, 0 };
                case 2: return new float[] { 0, 1 };
                case 3: return new float[] { 0, -1 };
                case 4: return new float[] { WorldGenSettings.Normalization.InvSqrt2, WorldGenSettings.Normalization.InvSqrt2 };
                case 5: return new float[] { -WorldGenSettings.Normalization.InvSqrt2, WorldGenSettings.Normalization.InvSqrt2 };
                case 6: return new float[] { WorldGenSettings.Normalization.InvSqrt2, -WorldGenSettings.Normalization.InvSqrt2 };
                default: return new float[] { -WorldGenSettings.Normalization.InvSqrt2, -WorldGenSettings.Normalization.InvSqrt2 };
            }
        }

        private static float[] Grad3(int ix, int iy, int iz, int seed)
        {
            int h = FastHash(ix, iy, iz, seed);
            switch ((h >> 11) & 15)
            {
                case 0: return new float[] { 1, 1, 0 };
                case 1: return new float[] { -1, 1, 0 };
                case 2: return new float[] { 1, -1, 0 };
                case 3: return new float[] { -1, -1, 0 };
                case 4: return new float[] { 1, 0, 1 };
                case 5: return new float[] { -1, 0, 1 };
                case 6: return new float[] { 1, 0, -1 };
                case 7: return new float[] { -1, 0, -1 };
                case 8: return new float[] { 0, 1, 1 };
                case 9: return new float[] { 0, -1, 1 };
                case 10: return new float[] { 0, 1, -1 };
                case 11: return new float[] { 0, -1, -1 };
                case 12: return new float[] { 1, 1, 1 };
                case 13: return new float[] { -1, 1, 1 };
                case 14: return new float[] { 1, -1, 1 };
                default: return new float[] { 1, 1, -1 };
            }
        }

        private static float Dot(float[] g, float x, float z) => g[0] * x + g[1] * z;
        private static float Dot(float[] g, float x, float y, float z) => g[0] * x + g[1] * y + g[2] * z;
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static int FastFloor(float t) => (t >= 0.0) ? (int)t : (int)t - 1;

        private static float Clamp(float x, float a, float b)
        {
            if (x < a) return a;
            if (x > b) return b;
            return x;
        }

        private static float Saturate(float x)
        {
            if (x < 0.0) return 0.0f;
            if (x > 1.0) return 1.0f;
            return x;
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Saturate((x - edge0) / (edge1 - edge0));
            return t * t * (3.0f - 2.0f * t);
        }

        private static int FastHash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = WorldGenSettings.Hashing.FnvOffset ^ (uint)seed;
                h ^= (uint)x; h *= WorldGenSettings.Hashing.FnvPrime;
                h ^= (uint)y; h *= WorldGenSettings.Hashing.FnvPrime;
                h ^= (uint)z; h *= WorldGenSettings.Hashing.FnvPrime;
                return (int)h;
            }
        }

        private static float Hash01(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)FastHash(x, y, z, seed);
                return h * (1.0f / 4294967296.0f);
            }
        }
    }
}
