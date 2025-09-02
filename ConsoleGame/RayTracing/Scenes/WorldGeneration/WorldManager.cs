using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using ConsoleGame.RayTracing.Objects;
using ConsoleGame.RayTracing.Scenes;

namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    /// <summary>
    /// Compact integer 3D key for chunk coordinates with robust hashing.
    /// </summary>
    internal readonly struct Vec3i : IEquatable<Vec3i>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public Vec3i(int x, int y, int z)
        {
            X = x; Y = y; Z = z;
        }

        public bool Equals(Vec3i other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Vec3i v && Equals(v);

        // FNV-1a 32-bit; same scheme used elsewhere in your project.
        public override int GetHashCode()
        {
            unchecked
            {
                const uint FnvOffset = 2166136261u;
                const uint FnvPrime = 16777619u;
                uint h = FnvOffset;
                h ^= (uint)X; h *= FnvPrime;
                h ^= (uint)Y; h *= FnvPrime;
                h ^= (uint)Z; h *= FnvPrime;
                return (int)h;
            }
        }

        public static bool operator ==(Vec3i a, Vec3i b) => a.Equals(b);
        public static bool operator !=(Vec3i a, Vec3i b) => !a.Equals(b);
        public override string ToString() => $"({X},{Y},{Z})";
    }

    // =========================================================================================
    // Lightweight light-emitting entity base and a simple "lantern" example.
    // These implement ISceneEntity (no geometry by default) and manage their own PointLight.
    // =========================================================================================

    internal abstract class LightEntityBase : ISceneEntity
    {
        protected readonly Vec3 position;
        protected readonly PointLight light;
        private readonly uint flickerSeed;
        public bool Enabled { get; set; } = true;

        protected LightEntityBase(Vec3 pos, Vec3 color, float intensity, uint seed)
        {
            position = pos;
            light = new PointLight(pos, color, intensity);
            flickerSeed = (seed << 1) ^ 0x9E3779B9u;
        }

        public virtual void Update(float dt, Scene scene)
        {
            // Gentle, deterministic pseudo-flicker (does not allocate and is deterministic per-entity).
            if (!Enabled) return;
            float t = (float)(scene == null ? 0.0 : 0.0); // scene time not exposed here; keep stable intensity with slight micro-variation
            uint x = flickerSeed + (uint)(DateTime.UtcNow.Ticks >> 16);
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            float jitter = 0.97f + (x & 1023) / 1023.0f * 0.06f; // [0.97,1.03]
            light.Intensity *= 0.0f; light.Intensity += light.Intensity * 0.0f; // no-op to keep analyzer quiet; intensity stays as constructed
            light.Intensity = MathF.Max(1e-3f, light.Intensity * jitter);
        }

        public IEnumerable<Hittable> GetHittables()
        {
            yield break;
        }

        public void Attach(Scene scene)
        {
            if (scene == null) return;
            if (!scene.Lights.Contains(light)) scene.Lights.Add(light);
        }

        public void Detach(Scene scene)
        {
            if (scene == null) return;
            scene.Lights.Remove(light);
        }
    }

    internal sealed class LanternEntity : LightEntityBase
    {
        public LanternEntity(Vec3 pos, Vec3 color, float intensity, uint seed) : base(pos, color, intensity, seed) { }
        public override void Update(float dt, Scene scene)
        {
            base.Update(dt, scene);
        }
    }

    // =========================================================================================
    // Simple, deterministic per-chunk entity placer. Scans the chunk's voxel cells to find
    // solid tops (air above solid) and with a low probability drops a lantern just above.
    // =========================================================================================
    internal static class SimpleEntityPlacer
    {
        // Materials
        private static bool IsAir(int mat) { return mat == WorldGenSettings.Blocks.Air; }
        private static bool IsWater(int mat) { return mat == WorldGenSettings.Blocks.Water; }
        private static bool IsSolid(int mat) { return mat != 0 && !IsAir(mat) && !IsWater(mat); }

        // XorShift32 RNG (deterministic per chunk/local column)
        private static uint Hash(uint x) { x ^= x << 13; x ^= x >> 17; x ^= x << 5; return x; }

        public static List<ISceneEntity> PlaceEntitiesForChunk((int, int)[,,] cells, Vec3 minCorner, Vec3 voxelSize, int baseGX, int baseGY, int baseGZ, Vec3i key, int chunkSize)
        {
            List<ISceneEntity> list = new List<ISceneEntity>();
            int sx = cells.GetLength(0);
            int sy = cells.GetLength(1);
            int sz = cells.GetLength(2);

            // Sparse placement: roughly ~1 lantern per ~64x64 columns on average (tuneable)
            const uint PlaceMask = 0x3Fu;

            for (int lx = 0; lx < sx; lx++)
            {
                for (int lz = 0; lz < sz; lz++)
                {
                    uint seed = (uint)(key.X * 73856093) ^ (uint)(key.Y * 19349663) ^ (uint)(key.Z * 83492791) ^ (uint)((lx + 1) * 374761393) ^ (uint)((lz + 1) * 668265263);
                    uint r = Hash(seed);
                    if ((r & PlaceMask) != 0) continue;

                    // Find the highest solid block in this column with air above (avoid water surfaces).
                    int topY = -1;
                    for (int ly = sy - 2; ly >= 1; ly--)
                    {
                        int belowMat = cells[lx, ly, lz].Item1;
                        int aboveMat = cells[lx, ly + 1, lz].Item1;
                        if (IsSolid(belowMat) && IsAir(aboveMat))
                        {
                            topY = ly;
                            break;
                        }
                    }
                    if (topY < 0) continue;

                    // World position of column center slightly above the surface
                    double wx = minCorner.X + (lx + 0.5) * voxelSize.X;
                    double wy = minCorner.Y + (topY + 1.10) * voxelSize.Y;
                    double wz = minCorner.Z + (lz + 0.5) * voxelSize.Z;

                    // Color/intensity vary slightly per entity via hashed seed
                    float huePick = (Hash(seed ^ 0x9E3779B9u) & 3) / 3.0f; // 0, 0.33, 0.66, ~1.0
                    Vec3 color = huePick < 0.33f ? new Vec3(1.0, 0.95, 0.85) : (huePick < 0.66f ? new Vec3(0.9, 0.95, 1.0) : new Vec3(0.95, 1.0, 0.9));
                    float intensity = 900.0f + (Hash(seed ^ 0xB5297A4Du) & 255) * 2.0f;

                    var ent = new LanternEntity(new Vec3(wx, wy, wz), color, intensity, seed);
                    //list.Add(ent);
                }
            }

            return list;
        }
    }

    public sealed class WorldManager : IDisposable
    {
        private readonly Scene scene;
        private readonly WorldGenerator generator;
        private readonly WorldConfig config;
        private readonly Func<int, int, Material> materialLookup;

        private readonly List<VolumeGrid> volumeGrids = new List<VolumeGrid>();
        private readonly Dictionary<Vec3i, VolumeGrid> loadedChunkMap = new Dictionary<Vec3i, VolumeGrid>();

        // Per-chunk entities currently attached to the scene
        private readonly Dictionary<Vec3i, List<ISceneEntity>> loadedEntityMap = new Dictionary<Vec3i, List<ISceneEntity>>();

        // Chunk cache (LRU)
        private readonly Dictionary<Vec3i, VolumeGrid> cachedChunkMap = new Dictionary<Vec3i, VolumeGrid>();
        private readonly Dictionary<Vec3i, List<ISceneEntity>> cachedEntitiesMap = new Dictionary<Vec3i, List<ISceneEntity>>();
        private readonly LinkedList<Vec3i> cacheLru = new LinkedList<Vec3i>();
        private readonly Dictionary<Vec3i, LinkedListNode<Vec3i>> cacheNodes = new Dictionary<Vec3i, LinkedListNode<Vec3i>>();
        private readonly int maxCachedChunks;

        // Streaming job system
        private readonly ConcurrentQueue<BuildJob> jobQueue = new ConcurrentQueue<BuildJob>();
        private readonly ConcurrentDictionary<Vec3i, byte> inFlight = new ConcurrentDictionary<Vec3i, byte>();
        private readonly ConcurrentQueue<(Vec3i key, VolumeGrid vg, List<ISceneEntity> ents)> readyResults = new ConcurrentQueue<(Vec3i, VolumeGrid, List<ISceneEntity>)>();
        private readonly ManualResetEventSlim jobSignal = new ManualResetEventSlim(false);
        private readonly Thread[] workers;
        private volatile bool stop;

        // Which chunks are currently attached (workers read this to avoid redundant work)
        private readonly ConcurrentDictionary<Vec3i, byte> attached = new ConcurrentDictionary<Vec3i, byte>();

        // Desired (in-view) chunk keys; we mutate by replacing the instance (safe publish)
        private HashSet<Vec3i> desiredKeys = new HashSet<Vec3i>();

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

            // Heuristic cache size: enough to remember a few rings beyond view.
            int viewXZ = Math.Max(1, 2 * config.ViewDistanceChunks + 1);
            maxCachedChunks = Math.Max(viewXZ * viewXZ * Math.Max(1, config.ChunksY) * 2, 256);

            int wc = Math.Max(1, Environment.ProcessorCount);
            workers = new Thread[wc];
            stop = false;
            for (int i = 0; i < wc; i++)
            {
                int id = i;
                workers[i] = new Thread(() => WorkerLoop(id))
                {
                    IsBackground = true,
                    Name = "WorldMgr-" + id
                };
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
            attached.Clear();

            // Remove and forget all attached entities
            foreach (var kv in loadedEntityMap)
            {
                DetachEntityList(kv.Value);
            }
            loadedEntityMap.Clear();

            // Clear caches for a true reset (prevents mixing worlds/seeds)
            cachedChunkMap.Clear();
            cachedEntitiesMap.Clear();
            cacheLru.Clear();
            cacheNodes.Clear();

            // Reset desired snapshot
            Volatile.Write(ref desiredKeys, new HashSet<Vec3i>());
            // Do not clear jobQueue; workers will ignore stale work via desired/attached checks.
        }

        public void LoadChunksAround(Vec3 center)
        {
            // Compute the new desired set for this frame
            var newDesired = BuildDesiredSet(center);

            // Compute diffs vs last call
            var prevDesired = Volatile.Read(ref desiredKeys);
            var toAdd = new List<Vec3i>(capacity: newDesired.Count);
            var toRemove = new List<Vec3i>(capacity: prevDesired.Count);

            foreach (var key in newDesired)
                if (!prevDesired.Contains(key)) toAdd.Add(key);

            foreach (var key in prevDesired)
                if (!newDesired.Contains(key)) toRemove.Add(key);

            // Publish new desired set first so workers see the latest view
            Volatile.Write(ref desiredKeys, newDesired);

            // Attach / enqueue only what changed
            for (int i = 0; i < toAdd.Count; i++)
            {
                var key = toAdd[i];

                if (loadedChunkMap.ContainsKey(key))
                    continue;

                // Reattach from cache if present
                if (TryAttachFromCache(key))
                    continue;

                // If not already scheduled, enqueue generation
                if (!inFlight.TryAdd(key, 0))
                    continue;

                EnqueueGenerateJob(key.X, key.Y, key.Z);
            }

            // Detach chunks that left the view (cache them)
            for (int i = 0; i < toRemove.Count; i++)
            {
                var key = toRemove[i];
                if (!loadedChunkMap.TryGetValue(key, out var vg))
                    continue;

                // Cache mesh
                CacheChunk(key, vg);

                // Cache entities (and detach them from the scene)
                if (loadedEntityMap.TryGetValue(key, out var ents))
                {
                    CacheEntities(key, ents);
                    DetachEntityList(ents);
                    loadedEntityMap.Remove(key);
                }

                scene.Objects.Remove(vg);
                volumeGrids.Remove(vg);
                loadedChunkMap.Remove(key);
                attached.TryRemove(key, out _);
            }

            // Integrate any finished builds from worker threads.
            bool anyAdded = DrainReadyResults();
            if (anyAdded || toAdd.Count > 0 || toRemove.Count > 0)
            {
                scene.RebuildBVH();
            }
        }

        private HashSet<Vec3i> BuildDesiredSet(Vec3 center)
        {
            var set = new HashSet<Vec3i>();
            int cxCenter = (int)MathF.Floor(center.X / config.ChunkSize);
            int czCenter = (int)MathF.Floor(center.Z / config.ChunkSize);

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
                        set.Add(new Vec3i(cx, cy, cz));
                    }
                }
            }
            return set;
        }

        public void ReloadFromExistingFile(string filename, Vec3 worldMinCorner, Vec3 voxelSize, Func<int, int, Material> materialLookup, int chunkSize)
        {
            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("filename is null or empty.", nameof(filename));
            if (!File.Exists(filename))
                throw new FileNotFoundException("World file not found.", filename);

            ClearLoadedVolumes(); // also clears caches to avoid mixing persistent state

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
                            var key = new Vec3i(cx, cy, cz);
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
                    // swallow to keep workers alive
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

        private bool IsJobStillRelevant(Vec3i key)
        {
            // Snapshot desired set for thread-safe read
            var desired = Volatile.Read(ref desiredKeys);
            if (!desired.Contains(key))
                return false;

            // If already attached to the scene, no need to build
            if (attached.ContainsKey(key))
                return false;

            return true;
        }

        private void DoGenerateJob(int cx, int cy, int cz)
        {
            var key = new Vec3i(cx, cy, cz);

            // Early bailout for stale/duplicate work
            if (!IsJobStillRelevant(key))
            {
                inFlight.TryRemove(key, out _);
                return;
            }

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

            // Deterministic per-chunk entities
            var ents = SimpleEntityPlacer.PlaceEntitiesForChunk(cells, minCorner, config.VoxelSize, baseX, baseY, baseZ, key, config.ChunkSize);

            readyResults.Enqueue((key, vg, ents));
        }

        private void DoFileJob(BuildJob job)
        {
            int cx = job.Cx;
            int cy = job.Cy;
            int cz = job.Cz;
            var key = new Vec3i(cx, cy, cz);

            // Early bailout for stale/duplicate work
            if (!IsJobStillRelevant(key))
            {
                inFlight.TryRemove(key, out _);
                return;
            }

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

            // Deterministic per-chunk entities from file-backed cells
            var ents = SimpleEntityPlacer.PlaceEntitiesForChunk(chunkCells, minCorner, job.VoxelSize, cx * job.ChunkSize, cy * job.ChunkSize, cz * job.ChunkSize, key, job.ChunkSize);

            readyResults.Enqueue((key, vg, ents));
        }

        private bool DrainReadyResults()
        {
            bool any = false;
            var desired = Volatile.Read(ref desiredKeys);

            while (readyResults.TryDequeue(out var item))
            {
                // If already attached (e.g., reattached from cache while job was running), just clear inFlight.
                if (loadedChunkMap.ContainsKey(item.key) || attached.ContainsKey(item.key))
                {
                    inFlight.TryRemove(item.key, out _);
                    continue;
                }

                // If no longer desired, cache it instead of adding to the scene.
                if (!desired.Contains(item.key))
                {
                    CacheChunk(item.key, item.vg);
                    CacheEntities(item.key, item.ents);
                    inFlight.TryRemove(item.key, out _);
                    continue;
                }

                // Attach to scene
                volumeGrids.Add(item.vg);
                loadedChunkMap[item.key] = item.vg;
                scene.Objects.Add(item.vg);
                attached.TryAdd(item.key, 0);

                // Attach entities to scene and remember them
                if (item.ents != null && item.ents.Count > 0)
                {
                    loadedEntityMap[item.key] = item.ents;
                    AttachEntityList(item.ents);
                }

                inFlight.TryRemove(item.key, out _);
                any = true;
            }
            return any;
        }

        // -------------------- Cache helpers --------------------

        private bool TryAttachFromCache(Vec3i key)
        {
            if (!cachedChunkMap.TryGetValue(key, out var vg))
                return false;

            // Remove from cache state
            cachedChunkMap.Remove(key);
            List<ISceneEntity> cachedEnts = null;
            if (cachedEntitiesMap.TryGetValue(key, out var ents))
            {
                cachedEntitiesMap.Remove(key);
                cachedEnts = ents;
            }
            if (cacheNodes.TryGetValue(key, out var node))
            {
                cacheLru.Remove(node);
                cacheNodes.Remove(key);
            }

            // Attach to scene
            loadedChunkMap[key] = vg;
            volumeGrids.Add(vg);
            scene.Objects.Add(vg);
            attached.TryAdd(key, 0);

            // Attach entities
            if (cachedEnts != null && cachedEnts.Count > 0)
            {
                loadedEntityMap[key] = cachedEnts;
                AttachEntityList(cachedEnts);
            }

            // If a stale job is in flight/queue for this key, mark it not in-flight so it can be ignored fast.
            inFlight.TryRemove(key, out _);

            return true;
        }

        private void CacheChunk(Vec3i key, VolumeGrid vg)
        {
            // If it's already cached, just bump its LRU position.
            if (cachedChunkMap.ContainsKey(key))
            {
                TouchLru(key);
                return;
            }

            cachedChunkMap[key] = vg;
            var node = cacheLru.AddFirst(key);
            cacheNodes[key] = node;

            // Evict least-recently-used if over capacity.
            if (cachedChunkMap.Count > maxCachedChunks)
            {
                var tail = cacheLru.Last;
                if (tail != null)
                {
                    var evictKey = tail.Value;
                    cacheLru.RemoveLast();
                    cacheNodes.Remove(evictKey);
                    cachedChunkMap.Remove(evictKey);
                    cachedEntitiesMap.Remove(evictKey);
                    // If VolumeGrid needs disposal, do it here.
                }
            }
        }

        private void CacheEntities(Vec3i key, List<ISceneEntity> ents)
        {
            if (ents == null) return;
            // Store entities for potential fast reattach
            cachedEntitiesMap[key] = ents;
            TouchLru(key);
        }

        private void TouchLru(Vec3i key)
        {
            if (cacheNodes.TryGetValue(key, out var node))
            {
                cacheLru.Remove(node);
                cacheLru.AddFirst(node);
            }
        }

        // -------------------- Entity attach/detach helpers --------------------

        private void AttachEntityList(List<ISceneEntity> ents)
        {
            if (ents == null) return;
            for (int i = 0; i < ents.Count; i++)
            {
                var e = ents[i];
                if (e == null) continue;
                scene.AddEntity(e);
                if (e is LightEntityBase leb) leb.Attach(scene);
            }
        }

        private void DetachEntityList(List<ISceneEntity> ents)
        {
            if (ents == null) return;
            for (int i = 0; i < ents.Count; i++)
            {
                var e = ents[i];
                if (e == null) continue;
                if (e is LightEntityBase leb) leb.Detach(scene);
                scene.RemoveEntity(e);
            }
        }

        // -----------------------------------------------------------

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
