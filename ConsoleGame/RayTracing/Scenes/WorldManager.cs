using ConsoleGame.RayTracing.Objects;

namespace ConsoleGame.RayTracing.Scenes
{
    public sealed class WorldManager
    {
        private readonly Scene scene;
        private readonly WorldGenerator generator;
        private readonly WorldConfig config;
        private readonly Func<int, int, Material> materialLookup;

        private readonly List<VolumeGrid> volumeGrids = new List<VolumeGrid>();
        private readonly Dictionary<(int, int, int), VolumeGrid> loadedChunkMap = new Dictionary<(int, int, int), VolumeGrid>();

        public WorldManager(Scene scene, WorldGenerator generator, WorldConfig config, Func<int, int, Material> materialLookup)
        {
            this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
            this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.materialLookup = materialLookup ?? throw new ArgumentNullException(nameof(materialLookup));
        }

        public void ClearLoadedVolumes()
        {
            foreach (var vg in volumeGrids)
            {
                scene.Objects.Remove(vg);
            }
            volumeGrids.Clear();
            loadedChunkMap.Clear();
        }

        public void LoadChunksAround(Vec3 center)
        {
            int cxCenter = (int)Math.Floor(center.X / config.ChunkSize);
            int czCenter = (int)Math.Floor(center.Z / config.ChunkSize);

            int minCX = cxCenter - config.ViewDistanceChunks;
            int maxCX = cxCenter + config.ViewDistanceChunks;
            int minCZ = czCenter - config.ViewDistanceChunks;
            int maxCZ = czCenter + config.ViewDistanceChunks;

            for (int cx = minCX; cx <= maxCX; cx++)
            {
                for (int cz = minCZ; cz <= maxCZ; cz++)
                {
                    for (int cy = 0; cy < config.ChunksY; cy++)
                    {
                        var key = (cx, cy, cz);
                        if (loadedChunkMap.ContainsKey(key))
                            continue;

                        var cells = new (int, int)[config.ChunkSize, config.ChunkSize, config.ChunkSize];
                        bool anySolid;
                        generator.GenerateChunkCells(cx, cy, cz, config, cells, out anySolid);
                        if (!anySolid)
                            continue;

                        int baseX = cx * config.ChunkSize;
                        int baseY = cy * config.ChunkSize;
                        int baseZ = cz * config.ChunkSize;

                        Vec3 minCorner = new Vec3(
                            config.WorldMin.X + baseX * config.VoxelSize.X,
                            config.WorldMin.Y + baseY * config.VoxelSize.Y,
                            config.WorldMin.Z + baseZ * config.VoxelSize.Z
                        );

                        var vg = new VolumeGrid(cells, minCorner, config.VoxelSize, materialLookup);
                        volumeGrids.Add(vg);
                        loadedChunkMap[key] = vg;
                        scene.Objects.Add(vg);
                    }
                }
            }

            var toRemove = new List<(int, int, int)>();
            foreach (var key in loadedChunkMap.Keys)
            {
                int cx = key.Item1, cy = key.Item2, cz = key.Item3;
                if (cx < minCX || cx > maxCX || cz < minCZ || cz > maxCZ)
                {
                    toRemove.Add(key);
                }
            }
            foreach (var key in toRemove)
            {
                var vg = loadedChunkMap[key];
                scene.Objects.Remove(vg);
                volumeGrids.Remove(vg);
                loadedChunkMap.Remove(key);
            }

            scene.RebuildBVH();
        }

        public void ReloadFromExistingFile(string filename, Vec3 worldMinCorner, Vec3 voxelSize, Func<int, int, Material> materialLookup, int chunkSize)
        {
            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("filename is null or empty.", nameof(filename));
            if (!File.Exists(filename))
                throw new FileNotFoundException("World file not found.", filename);

            ClearLoadedVolumes();
            using (var br = new BinaryReader(File.OpenRead(filename)))
            {
                char c0 = br.ReadChar();
                char c1 = br.ReadChar();
                char c2 = br.ReadChar();
                char c3 = br.ReadChar();
                if (c0 != 'V' || c1 != 'G' || c2 != '0' || c3 != '1')
                    throw new InvalidDataException("Unsupported world file header. Expected 'VG01'.");

                int nx = br.ReadInt32();
                int ny = br.ReadInt32();
                int nz = br.ReadInt32();
                if (nx <= 0 || ny <= 0 || nz <= 0)
                    throw new InvalidDataException("Invalid world dimensions.");

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

                int chunkCountX = (nx + chunkSize - 1) / chunkSize;
                int chunkCountY = (ny + chunkSize - 1) / chunkSize;
                int chunkCountZ = (nz + chunkSize - 1) / chunkSize;
                for (int cx = 0; cx < chunkCountX; cx++)
                {
                    for (int cy = 0; cy < chunkCountY; cy++)
                    {
                        for (int cz = 0; cz < chunkCountZ; cz++)
                        {
                            int sx = Math.Min(chunkSize, nx - cx * chunkSize);
                            int sy = Math.Min(chunkSize, ny - cy * chunkSize);
                            int sz = Math.Min(chunkSize, nz - cz * chunkSize);

                            var chunkCells = new (int, int)[sx, sy, sz];
                            bool anySolid = false;
                            for (int x = 0; x < sx; x++)
                            {
                                int gx = cx * chunkSize + x;
                                for (int y = 0; y < sy; y++)
                                {
                                    int gy = cy * chunkSize + y;
                                    for (int z = 0; z < sz; z++)
                                    {
                                        int gz = cz * chunkSize + z;
                                        var cell = worldCells[gx, gy, gz];
                                        chunkCells[x, y, z] = cell;
                                        if (cell.Item1 != 0)
                                            anySolid = true;
                                    }
                                }
                            }
                            if (!anySolid)
                                continue;

                            Vec3 minCorner = new Vec3(
                                worldMinCorner.X + cx * chunkSize * voxelSize.X,
                                worldMinCorner.Y + cy * chunkSize * voxelSize.Y,
                                worldMinCorner.Z + cz * chunkSize * voxelSize.Z
                            );

                            var vg = new VolumeGrid(chunkCells, minCorner, voxelSize, materialLookup);
                            volumeGrids.Add(vg);
                            loadedChunkMap[(cx, cy, cz)] = vg;
                            scene.Objects.Add(vg);
                        }
                    }
                }
            }
            scene.RebuildBVH();
        }

        public void GenerateAndSaveWorld(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                throw new ArgumentException("filename cannot be null or empty.", nameof(filename));

            int nx = config.ChunksX * config.ChunkSize;
            int ny = config.ChunksY * config.ChunkSize;
            int nz = config.ChunksZ * config.ChunkSize;

            string dir = Path.GetDirectoryName(filename);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (var bw = new BinaryWriter(File.Open(filename, FileMode.Create, FileAccess.Write, FileShare.None)))
            {
                bw.Write('V'); bw.Write('G'); bw.Write('0'); bw.Write('1');
                bw.Write(nx); bw.Write(ny); bw.Write(nz);
                for (int gx = 0; gx < nx; gx++)
                {
                    for (int gy = 0; gy < ny; gy++)
                    {
                        for (int gz = 0; gz < nz; gz++)
                        {
                            var block = generator.GetBlockAt(gx, gy, gz, config);
                            bw.Write(block.Item1);
                            bw.Write(block.Item2);
                        }
                    }
                }
            }
        }
    }
}
