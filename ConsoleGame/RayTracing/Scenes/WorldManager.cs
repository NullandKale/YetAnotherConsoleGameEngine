using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using ConsoleGame.RayTracing.Objects;

namespace ConsoleGame.RayTracing.Scenes
{
    public sealed class WorldManager : IDisposable
    {
        private readonly Scene scene;
        private readonly WorldGenerator generator;
        private readonly WorldConfig config;
        private readonly Func<int, int, Material> materialLookup;

        private readonly List<VolumeGrid> volumeGrids = new List<VolumeGrid>();
        private readonly Dictionary<(int, int, int), VolumeGrid> loadedChunkMap = new Dictionary<(int, int, int), VolumeGrid>();

        // --- Streaming job system ---
        private readonly ConcurrentQueue<BuildJob> jobQueue = new ConcurrentQueue<BuildJob>();
        private readonly ConcurrentDictionary<(int, int, int), byte> inFlight = new ConcurrentDictionary<(int, int, int), byte>();
        private readonly ConcurrentQueue<((int, int, int) key, VolumeGrid vg)> readyResults = new ConcurrentQueue<((int, int, int), VolumeGrid)>();
        private readonly ManualResetEventSlim jobSignal = new ManualResetEventSlim(false);
        private readonly Thread[] workers;
        private volatile bool stop;

        private enum JobKind { Generate, FromWorldCells }

        private sealed class BuildJob
        {
            public JobKind Kind;
            public int Cx;
            public int Cy;
            public int Cz;
            public (int, int)[,,] WorldCells; // only for FromWorldCells
            public int Nx;
            public int Ny;
            public int Nz;
            public Vec3 WorldMinCorner;
            public Vec3 VoxelSize;
            public int ChunkSize;
            public CountdownEvent Group; // optional barrier for synchronous callers
        }

        public WorldManager(Scene scene, WorldGenerator generator, WorldConfig config, Func<int, int, Material> materialLookup)
        {
            this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
            this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.materialLookup = materialLookup ?? throw new ArgumentNullException(nameof(materialLookup));

            int wc = Math.Max(1, Environment.ProcessorCount);
            workers = new Thread[wc];
            stop = false;
            for (int i = 0; i < wc; i++)
            {
                int id = i;
                workers[i] = new Thread(() => WorkerLoop(id));
                workers[i].IsBackground = true;
                workers[i].Name = "WorldMgr-" + id;
                workers[i].Start();
            }
        }

        public void ClearLoadedVolumes()
        {
            foreach (var vg in volumeGrids)
            {
                scene.Objects.Remove(vg);
            }
            volumeGrids.Clear();
            loadedChunkMap.Clear();
            inFlight.Clear();
            // Do not clear jobQueue; let workers drain or ignore obsolete work by key checks.
        }

        public void LoadChunksAround(Vec3 center)
        {
            int cxCenter = (int)Math.Floor(center.X / config.ChunkSize);
            int czCenter = (int)Math.Floor(center.Z / config.ChunkSize);

            int minCX = cxCenter - config.ViewDistanceChunks;
            int maxCX = cxCenter + config.ViewDistanceChunks;
            int minCZ = czCenter - config.ViewDistanceChunks;
            int maxCZ = czCenter + config.ViewDistanceChunks;

            // Enqueue missing chunks near the camera.
            for (int cx = minCX; cx <= maxCX; cx++)
            {
                for (int cz = minCZ; cz <= maxCZ; cz++)
                {
                    for (int cy = 0; cy < config.ChunksY; cy++)
                    {
                        var key = (cx, cy, cz);
                        if (loadedChunkMap.ContainsKey(key))
                            continue;
                        if (!inFlight.TryAdd(key, 0))
                            continue;

                        EnqueueGenerateJob(cx, cy, cz);
                    }
                }
            }

            // Remove far chunks immediately on the main thread.
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

            // Integrate any finished builds from worker threads.
            bool anyAdded = DrainReadyResults();
            if (anyAdded || toRemove.Count > 0)
            {
                scene.RebuildBVH();
            }
        }

        public void ReloadFromExistingFile(string filename, Vec3 worldMinCorner, Vec3 voxelSize, Func<int, int, Material> materialLookup, int chunkSize)
        {
            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("filename is null or empty.", nameof(filename));
            if (!File.Exists(filename))
                throw new FileNotFoundException("World file not found.", filename);

            ClearLoadedVolumes();

            (int, int)[,,] worldCells;
            int nx, ny, nz;
            using (var br = new BinaryReader(File.OpenRead(filename)))
            {
                char c0 = br.ReadChar();
                char c1 = br.ReadChar();
                char c2 = br.ReadChar();
                char c3 = br.ReadChar();
                if (c0 != 'V' || c1 != 'G' || c2 != '0' || c3 != '1')
                    throw new InvalidDataException("Unsupported world file header. Expected 'VG01'.");

                nx = br.ReadInt32();
                ny = br.ReadInt32();
                nz = br.ReadInt32();
                if (nx <= 0 || ny <= 0 || nz <= 0)
                    throw new InvalidDataException("Invalid world dimensions.");

                worldCells = new (int, int)[nx, ny, nz];
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
            }

            int chunkCountX = (nx + chunkSize - 1) / chunkSize;
            int chunkCountY = (ny + chunkSize - 1) / chunkSize;
            int chunkCountZ = (nz + chunkSize - 1) / chunkSize;

            int total = chunkCountX * chunkCountY * chunkCountZ;
            using (var group = new CountdownEvent(total))
            {
                for (int cx = 0; cx < chunkCountX; cx++)
                {
                    for (int cy = 0; cy < chunkCountY; cy++)
                    {
                        for (int cz = 0; cz < chunkCountZ; cz++)
                        {
                            var key = (cx, cy, cz);
                            inFlight.TryAdd(key, 0);
                            EnqueueFileJob(cx, cy, cz, worldCells, nx, ny, nz, worldMinCorner, voxelSize, chunkSize, group);
                        }
                    }
                }

                // Wait for all chunks from file to finish building, integrating as they arrive.
                while (group.CurrentCount > 0)
                {
                    DrainReadyResults();
                    Thread.Sleep(1);
                }
            }

            // Final drain and BVH rebuild.
            bool anyAdded = DrainReadyResults();
            if (anyAdded)
            {
                scene.RebuildBVH();
            }
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

        private void EnqueueGenerateJob(int cx, int cy, int cz)
        {
            jobQueue.Enqueue(new BuildJob
            {
                Kind = JobKind.Generate,
                Cx = cx,
                Cy = cy,
                Cz = cz
            });
            jobSignal.Set();
        }

        private void EnqueueFileJob(int cx, int cy, int cz, (int, int)[,,] worldCells, int nx, int ny, int nz, Vec3 worldMinCorner, Vec3 voxelSize, int chunkSize, CountdownEvent group)
        {
            jobQueue.Enqueue(new BuildJob
            {
                Kind = JobKind.FromWorldCells,
                Cx = cx,
                Cy = cy,
                Cz = cz,
                WorldCells = worldCells,
                Nx = nx,
                Ny = ny,
                Nz = nz,
                WorldMinCorner = worldMinCorner,
                VoxelSize = voxelSize,
                ChunkSize = chunkSize,
                Group = group
            });
            jobSignal.Set();
        }

        private void WorkerLoop(int workerId)
        {
            while (!Volatile.Read(ref stop))
            {
                if (!jobQueue.TryDequeue(out var job))
                {
                    jobSignal.Wait(1);
                    jobSignal.Reset();
                    continue;
                }

                try
                {
                    if (job.Kind == JobKind.Generate)
                    {
                        DoGenerateJob(job.Cx, job.Cy, job.Cz);
                    }
                    else
                    {
                        DoFileJob(job);
                    }
                }
                catch
                {
                }
                finally
                {
                    if (job.Group != null)
                    {
                        try { job.Group.Signal(); } catch { }
                    }
                }
            }
        }

        private void DoGenerateJob(int cx, int cy, int cz)
        {
            var key = (cx, cy, cz);

            var cells = new (int, int)[config.ChunkSize, config.ChunkSize, config.ChunkSize];
            bool anySolid;
            generator.GenerateChunkCells(cx, cy, cz, config, cells, out anySolid);
            if (!anySolid)
            {
                inFlight.TryRemove(key, out _);
                return;
            }

            int baseX = cx * config.ChunkSize;
            int baseY = cy * config.ChunkSize;
            int baseZ = cz * config.ChunkSize;
            Vec3 minCorner = new Vec3(
                config.WorldMin.X + baseX * config.VoxelSize.X,
                config.WorldMin.Y + baseY * config.VoxelSize.Y,
                config.WorldMin.Z + baseZ * config.VoxelSize.Z
            );

            var vg = new VolumeGrid(cells, minCorner, config.VoxelSize, materialLookup);
            readyResults.Enqueue((key, vg));
        }

        private void DoFileJob(BuildJob job)
        {
            int cx = job.Cx;
            int cy = job.Cy;
            int cz = job.Cz;
            var key = (cx, cy, cz);

            int sx = Math.Min(job.ChunkSize, job.Nx - cx * job.ChunkSize);
            int sy = Math.Min(job.ChunkSize, job.Ny - cy * job.ChunkSize);
            int sz = Math.Min(job.ChunkSize, job.Nz - cz * job.ChunkSize);

            var chunkCells = new (int, int)[sx, sy, sz];
            bool anySolid = false;
            for (int x = 0; x < sx; x++)
            {
                int gx = cx * job.ChunkSize + x;
                for (int y = 0; y < sy; y++)
                {
                    int gy = cy * job.ChunkSize + y;
                    for (int z = 0; z < sz; z++)
                    {
                        int gz = cz * job.ChunkSize + z;
                        var cell = job.WorldCells[gx, gy, gz];
                        chunkCells[x, y, z] = cell;
                        if (cell.Item1 != 0)
                            anySolid = true;
                    }
                }
            }
            if (!anySolid)
            {
                inFlight.TryRemove(key, out _);
                return;
            }

            Vec3 minCorner = new Vec3(
                job.WorldMinCorner.X + cx * job.ChunkSize * job.VoxelSize.X,
                job.WorldMinCorner.Y + cy * job.ChunkSize * job.VoxelSize.Y,
                job.WorldMinCorner.Z + cz * job.ChunkSize * job.VoxelSize.Z
            );

            var vg = new VolumeGrid(chunkCells, minCorner, job.VoxelSize, materialLookup);
            readyResults.Enqueue((key, vg));
        }

        private bool DrainReadyResults()
        {
            bool any = false;
            while (readyResults.TryDequeue(out var item))
            {
                if (!loadedChunkMap.ContainsKey(item.key))
                {
                    volumeGrids.Add(item.vg);
                    loadedChunkMap[item.key] = item.vg;
                    scene.Objects.Add(item.vg);
                    any = true;
                }
                inFlight.TryRemove(item.key, out _);
            }
            return any;
        }

        public void Dispose()
        {
            if (!stop)
            {
                stop = true;
                jobSignal.Set();
                for (int i = 0; i < workers.Length; i++)
                {
                    try { workers[i]?.Join(); } catch { }
                }
            }
            jobSignal.Dispose();
        }
    }
}
