using System;
using System.Collections.Generic;
using System.IO;
using ConsoleGame.RayTracing.Objects;

namespace ConsoleGame.RayTracing.Scenes
{
    public sealed class VolumeScene : Scene
    {
        private readonly List<VolumeGrid> volumeGrids = new List<VolumeGrid>();

        public void BuildOrLoadLargeWorld(string filename, Vec3 worldMinCorner, Vec3 voxelSize, Func<int, int, Material> materialLookup, int chunksX = 8, int chunksY = 4, int chunksZ = 8, int chunkSize = 32)
        {
            if (materialLookup == null) throw new ArgumentNullException(nameof(materialLookup));
            if (chunksX <= 0 || chunksY <= 0 || chunksZ <= 0) throw new ArgumentOutOfRangeException("Chunk counts must be > 0.");
            if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be > 0.");

            ClearLoadedVolumes();

            int nx = chunksX * chunkSize;
            int ny = chunksY * chunkSize;
            int nz = chunksZ * chunkSize;

            Func<int, int, int, (int mat, int meta)> generator = (gx, gy, gz) =>
            {
                float fx = gx * 0.06f;
                float fz = gz * 0.06f;
                int baseH = Math.Max(1, ny / 4);
                int amp = Math.Max(1, ny / 5);
                int h = Clamp((int)(baseH + amp * (MathF.Sin(fx) + MathF.Cos(fz) + MathF.Sin(0.33f * fx + 0.77f * fz))), 1, ny - 1);

                int waterLevel = ny / 5;
                bool beach = h <= waterLevel + 2;
                if (gy > h)
                {
                    if (gy <= waterLevel) return (4, 0);
                    return (0, 0);
                }

                if (gy == h)
                {
                    if (beach) return (5, 0);
                    return (3, 0);
                }

                if (gy >= h - 3) return (2, 0);

                int oreChance = FastHash3D(gx, gy, gz) & 63;
                if (oreChance == 0) return (5, 200 + FastHash3D(gx + 11, gy + 7, gz + 19) % 3);

                return (1, 0);
            };

            if (!string.IsNullOrWhiteSpace(filename))
            {
                if (File.Exists(filename))
                {
                    LoadWorldFromBinary(filename, worldMinCorner, voxelSize, materialLookup, chunkSize);
                }
                else
                {
                    string dir = Path.GetDirectoryName(filename);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    SaveWorldBinaryFromGenerator(filename, nx, ny, nz, generator);
                    LoadWorldFromBinary(filename, worldMinCorner, voxelSize, materialLookup, chunkSize);
                }
            }
            else
            {
                AddProceduralChunks(worldMinCorner, voxelSize, materialLookup, chunksX, chunksY, chunksZ, chunkSize, generator);
            }

            RebuildBVH();
        }

        public void ReloadFromExistingFile(string filename, Vec3 worldMinCorner, Vec3 voxelSize, Func<int, int, Material> materialLookup, int chunkSize = 32)
        {
            if (string.IsNullOrWhiteSpace(filename)) throw new ArgumentException("filename is null or empty.", nameof(filename));
            if (!File.Exists(filename)) throw new FileNotFoundException("World file not found.", filename);
            if (materialLookup == null) throw new ArgumentNullException(nameof(materialLookup));

            ClearLoadedVolumes();
            LoadWorldFromBinary(filename, worldMinCorner, voxelSize, materialLookup, chunkSize);
            RebuildBVH();
        }

        public void ClearLoadedVolumes()
        {
            for (int i = 0; i < volumeGrids.Count; i++)
            {
                Objects.Remove(volumeGrids[i]);
            }
            volumeGrids.Clear();
        }

        private void AddProceduralChunks(Vec3 worldMinCorner, Vec3 voxelSize, Func<int, int, Material> materialLookup, int chunksX, int chunksY, int chunksZ, int chunkSize, Func<int, int, int, (int mat, int meta)> generator)
        {
            for (int cx = 0; cx < chunksX; cx++)
            {
                for (int cy = 0; cy < chunksY; cy++)
                {
                    for (int cz = 0; cz < chunksZ; cz++)
                    {
                        var cells = new (int, int)[chunkSize, chunkSize, chunkSize];
                        bool anySolid = false;

                        int gx0 = cx * chunkSize;
                        int gy0 = cy * chunkSize;
                        int gz0 = cz * chunkSize;

                        for (int x = 0; x < chunkSize; x++)
                        {
                            int gx = gx0 + x;
                            for (int y = 0; y < chunkSize; y++)
                            {
                                int gy = gy0 + y;
                                for (int z = 0; z < chunkSize; z++)
                                {
                                    int gz = gz0 + z;
                                    var v = generator(gx, gy, gz);
                                    cells[x, y, z] = (v.mat, v.meta);
                                    if (v.mat > 0) anySolid = true;
                                }
                            }
                        }

                        if (!anySolid) continue;

                        Vec3 minCorner = new Vec3(worldMinCorner.X + gx0 * voxelSize.X, worldMinCorner.Y + gy0 * voxelSize.Y, worldMinCorner.Z + gz0 * voxelSize.Z);
                        var vg = new VolumeGrid(cells, minCorner, voxelSize, materialLookup);
                        volumeGrids.Add(vg);
                        Objects.Add(vg);
                    }
                }
            }
        }

        private void SaveWorldBinaryFromGenerator(string path, int nx, int ny, int nz, Func<int, int, int, (int mat, int meta)> generator)
        {
            using (var bw = new BinaryWriter(File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None)))
            {
                bw.Write('V');
                bw.Write('G');
                bw.Write('0');
                bw.Write('1');
                bw.Write(nx);
                bw.Write(ny);
                bw.Write(nz);
                for (int ix = 0; ix < nx; ix++)
                {
                    for (int iy = 0; iy < ny; iy++)
                    {
                        for (int iz = 0; iz < nz; iz++)
                        {
                            var v = generator(ix, iy, iz);
                            bw.Write(v.mat);
                            bw.Write(v.meta);
                        }
                    }
                }
            }
        }

        public void LoadWorldFromBinary(string path, Vec3 worldMinCorner, Vec3 voxelSize, Func<int, int, Material> materialLookup, int chunkSize)
        {
            using (var br = new BinaryReader(File.OpenRead(path)))
            {
                char c0 = br.ReadChar();
                char c1 = br.ReadChar();
                char c2 = br.ReadChar();
                char c3 = br.ReadChar();
                if (c0 != 'V' || c1 != 'G' || c2 != '0' || c3 != '1') throw new InvalidDataException("Unsupported world file header. Expected 'VG01'.");

                int nx = br.ReadInt32();
                int ny = br.ReadInt32();
                int nz = br.ReadInt32();
                if (nx <= 0 || ny <= 0 || nz <= 0) throw new InvalidDataException("Invalid world dimensions.");

                var worldCells = new (int, int)[nx, ny, nz];

                for (int ix = 0; ix < nx; ix++)
                {
                    for (int iy = 0; iy < ny; iy++)
                    {
                        for (int iz = 0; iz < nz; iz++)
                        {
                            int mat = br.ReadInt32();
                            int meta = br.ReadInt32();
                            worldCells[ix, iy, iz] = (mat, meta);
                        }
                    }
                }

                AddGridsFromCells(worldCells, worldMinCorner, voxelSize, materialLookup, chunkSize);
            }
        }

        private void AddGridsFromCells((int, int)[,,] worldCells, Vec3 worldMinCorner, Vec3 voxelSize, Func<int, int, Material> materialLookup, int chunkSize)
        {
            int nx = worldCells.GetLength(0);
            int ny = worldCells.GetLength(1);
            int nz = worldCells.GetLength(2);

            for (int cx = 0; cx < nx; cx += chunkSize)
            {
                for (int cy = 0; cy < ny; cy += chunkSize)
                {
                    for (int cz = 0; cz < nz; cz += chunkSize)
                    {
                        int sx = Math.Min(chunkSize, nx - cx);
                        int sy = Math.Min(chunkSize, ny - cy);
                        int sz = Math.Min(chunkSize, nz - cz);

                        var chunk = new (int, int)[sx, sy, sz];
                        bool anySolid = false;

                        for (int x = 0; x < sx; x++)
                        {
                            int gx = cx + x;
                            for (int y = 0; y < sy; y++)
                            {
                                int gy = cy + y;
                                for (int z = 0; z < sz; z++)
                                {
                                    int gz = cz + z;
                                    var cell = worldCells[gx, gy, gz];
                                    chunk[x, y, z] = cell;
                                    if (cell.Item1 > 0) anySolid = true;
                                }
                            }
                        }

                        if (!anySolid) continue;

                        Vec3 minCorner = new Vec3(worldMinCorner.X + cx * voxelSize.X, worldMinCorner.Y + cy * voxelSize.Y, worldMinCorner.Z + cz * voxelSize.Z);
                        var vg = new VolumeGrid(chunk, minCorner, voxelSize, materialLookup);

                        volumeGrids.Add(vg);
                        Objects.Add(vg);
                    }
                }
            }
        }

        private static int FastHash3D(int x, int y, int z)
        {
            uint h = 2166136261u;
            unchecked
            {
                h ^= (uint)x; h *= 16777619u;
                h ^= (uint)y; h *= 16777619u;
                h ^= (uint)z; h *= 16777619u;
            }
            return (int)h;
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }

    public static class VolumeScenes
    {
        public static VolumeScene BuildMinecraftLike(string filename, Vec3 worldMinCorner, Vec3 voxelSize, Func<int, int, Material> materialLookup, int chunksX = 8, int chunksY = 4, int chunksZ = 8, int chunkSize = 32)
        {
            VolumeScene s = new VolumeScene();
            s.DefaultCameraPos = new Vec3(0, 100, 0);
            s.Ambient = new AmbientLight(new Vec3(1, 1, 1), 0.5f);

            s.BackgroundTop = new Vec3(0.02, 0.02, 0.03);
            s.BackgroundBottom = new Vec3(0.01, 0.01, 0.01);

            s.Lights.Add(new PointLight(new Vec3(0.0, 12.0, 0.0), new Vec3(1.0, 1.0, 1.0), 400.0f));
            s.Lights.Add(new PointLight(new Vec3(-10.0, 10.0, -10.0), new Vec3(1.0, 0.95, 0.9), 160.0f));

            s.BuildOrLoadLargeWorld(filename, worldMinCorner, voxelSize, materialLookup, chunksX, chunksY, chunksZ, chunkSize);

            return s;
        }
    }
}
