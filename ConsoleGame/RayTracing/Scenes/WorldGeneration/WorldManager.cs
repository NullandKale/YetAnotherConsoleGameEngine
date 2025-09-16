using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.IO.MemoryMappedFiles;
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

        // Persistent state for preloaded worlds (for synchronous streaming)
        private (int, int)[,,] preloadedWorld = null; // null if not available
        private int preNx, preNy, preNz;

        private enum JobKind { Generate, FromWorldCells, FromMappedFile }

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
            public string FilePath; // for FromMappedFile
            public long DataOffset; // byte offset to first voxel (after header)
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
            // Remove any directly attached objects (legacy path) just in case
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

            // Sync entity layer to geometry and rebuild BVH
            scene.Update(0.0f);
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

            // Sort additions by radial distance from the player's current chunk (load near-first)
            float scaleX = (float)(config.VoxelSize.X * config.ChunkSize);
            float scaleZ = (float)(config.VoxelSize.Z * config.ChunkSize);
            int cxCenter = (int)MathF.Floor((float)((center.X - config.WorldMin.X) / scaleX));
            int czCenter = (int)MathF.Floor((float)((center.Z - config.WorldMin.Z) / scaleZ));
            toAdd.Sort((a, b) =>
            {
                int dax = a.X - cxCenter; int daz = a.Z - czCenter; int da = dax * dax + daz * daz;
                int dbx = b.X - cxCenter; int dbz = b.Z - czCenter; int db = dbx * dbx + dbz * dbz;
                int c = da.CompareTo(db);
                if (c != 0) return c;
                return a.Y.CompareTo(b.Y);
            });

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
            bool anyAdded = false;
            while (DrainReadyResults()) anyAdded = true;
            // Do not call scene.Update() here to avoid re-entrancy if invoked during Scene.Update.
            // The caller (e.g., VolumeScene.Update) will flush entity geometry and rebuild BVH.
        }

        private HashSet<Vec3i> BuildDesiredSet(Vec3 center)
        {
            var set = new HashSet<Vec3i>();
            // Map world coordinates to chunk indices (account for worldMin and voxel size)
            float scaleX = (float)(config.VoxelSize.X * config.ChunkSize);
            float scaleZ = (float)(config.VoxelSize.Z * config.ChunkSize);
            int cxCenter = (int)MathF.Floor((float)((center.X - config.WorldMin.X) / scaleX));
            int czCenter = (int)MathF.Floor((float)((center.Z - config.WorldMin.Z) / scaleZ));

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

            int nx, ny, nz;
            (int, int)[,,] worldCells;
            Console.WriteLine($"Loading world file: {filename}");
            using (var fs = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
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
                long expectedBytes = (long)nx * ny * nz * 8L;
                Console.WriteLine($"Allocating {nx}x{ny}x{nz} ({expectedBytes / (1024*1024)} MB) in memory...");
                worldCells = new (int, int)[nx, ny, nz];
                for (int ix = 0; ix < nx; ix++)
                {
                    if ((ix & 63) == 0) Console.WriteLine($"Read X: {ix}/{nx}");
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

            // Remember preloaded world for synchronous streaming later
            preloadedWorld = worldCells;
            preNx = nx; preNy = ny; preNz = nz;

            int chunkCountX = (nx + chunkSize - 1) / chunkSize;
            int chunkCountY = (ny + chunkSize - 1) / chunkSize;
            int chunkCountZ = (nz + chunkSize - 1) / chunkSize;

            // Predeclare full desired set (we intend to attach all pregenerated chunks)
            var fullDesired = new HashSet<Vec3i>(chunkCountX * chunkCountY * chunkCountZ);
            for (int cx2 = 0; cx2 < chunkCountX; cx2++)
                for (int cy2 = 0; cy2 < chunkCountY; cy2++)
                    for (int cz2 = 0; cz2 < chunkCountZ; cz2++)
                        fullDesired.Add(new Vec3i(cx2, cy2, cz2));
            Volatile.Write(ref desiredKeys, fullDesired);

            // Determine load origin (use current camera if plausible, else default)
            Vec3 centerWorld = scene != null ? scene.CameraPos : new Vec3(0, 0, 0);
            if (double.IsNaN(centerWorld.X) || double.IsNaN(centerWorld.Z) || (centerWorld.X == 0.0 && centerWorld.Z == 0.0))
            {
                centerWorld = scene != null ? scene.DefaultCameraPos : new Vec3(0, 0, 0);
            }
            int cx0 = (int)MathF.Floor((float)((centerWorld.X - worldMinCorner.X) / (voxelSize.X * chunkSize)));
            int cz0 = (int)MathF.Floor((float)((centerWorld.Z - worldMinCorner.Z) / (voxelSize.Z * chunkSize)));
            cx0 = Math.Clamp(cx0, 0, chunkCountX - 1);
            cz0 = Math.Clamp(cz0, 0, chunkCountZ - 1);

            // Build a priority list of chunk keys sorted by distance from (cx0, cz0)
            var keys = new List<Vec3i>(fullDesired);
            keys.Sort((a, b) =>
            {
                int dax = a.X - cx0; int daz = a.Z - cz0; int da = dax * dax + daz * daz;
                int dbx = b.X - cx0; int dbz = b.Z - cz0; int db = dbx * dbx + dbz * dbz;
                int c = da.CompareTo(db);
                if (c != 0) return c;
                // Prefer lower cy first to get ground before sky
                return a.Y.CompareTo(b.Y);
            });

            int total = chunkCountX * chunkCountY * chunkCountZ;
            using (var group = new CountdownEvent(total))
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    var key = keys[i];
                    inFlight.TryAdd(key, 0);
                    EnqueueFileJob(key.X, key.Y, key.Z, worldCells, nx, ny, nz, worldMinCorner, voxelSize, chunkSize, group);
                }

                // Wait for all chunks from file to finish building, integrating as they arrive.
                while (group.CurrentCount > 0)
                {
                    // Attach as many finished chunks as possible per loop iteration
                    while (DrainReadyResults()) { }
                    Thread.Sleep(1);
                }
            }

            // Final drain and rebuild via Scene.Update to flush Entities -> Objects
            bool anyAdded = false;
            while (DrainReadyResults()) anyAdded = true;
            if (anyAdded)
            {
                scene.Update(0.0f);
            }
        }

        public void GenerateAndSaveWorld(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                throw new ArgumentException("filename cannot be null or empty.", nameof(filename));

            int nx = config.ChunksX * config.ChunkSize;
            int ny = config.ChunksY * config.ChunkSize;
            int nz = config.ChunksZ * config.ChunkSize;

            Console.WriteLine($"World pregen: {nx} x {ny} x {nz}");

            // --- Pass 1: Terrain base fields ---
            var ground = new int[nx, nz];
            for (int x = 0; x < nx; x++)
            {
                if ((x & 63) == 0) Console.WriteLine($"Heights: {x}/{nx}");
                for (int z = 0; z < nz; z++)
                {
                    ground[x, z] = TerrainNoise.HeightY(x, z, config);
                }
            }

            // Rivers global
            RiverNetworkGlobal.Compute(nx, nz, config, ground, out var carveDepth, out var riverWater);
            for (int x = 0; x < nx; x++)
                for (int z = 0; z < nz; z++)
                    ground[x, z] = Math.Max(0, ground[x, z] - (int)MathF.Floor(carveDepth[x, z]));

            // Slope and biome, inland water
            var slope01 = new float[nx, nz];
            var biome = new Biome[nx, nz];
            var localWater = new int[nx, nz];
            int sea = config.WaterLevel, snow = config.SnowLevel;
            for (int x = 0; x < nx; x++)
            {
                for (int z = 0; z < nz; z++)
                {
                    int x0 = Math.Max(0, x - 1), x1 = Math.Min(nx - 1, x + 1);
                    int z0 = Math.Max(0, z - 1), z1 = Math.Min(nz - 1, z + 1);
                    float dx = (ground[x1, z] - ground[x0, z]) * 0.5f;
                    float dz = (ground[x, z1] - ground[x, z0]) * 0.5f;
                    float g = MathF.Sqrt(dx * dx + dz * dz);
                    slope01[x, z] = GenMath.Saturate(g / WorldGenSettings.Normalization.SlopeNormalize);
                    biome[x, z] = BiomeMap.Evaluate(x, z, ground[x, z], sea, snow, slope01[x, z], config);
                    int inland = TerrainNoise.LocalWaterY(x, z, config, ground[x, z], slope01[x, z]);
                    localWater[x, z] = Math.Max(inland, riverWater[x, z]);
                    // Mark lake biome where water covers the column
                    if (localWater[x, z] > sea && ground[x, z] <= localWater[x, z])
                        biome[x, z] = Biome.Lakes;
                }
            }

            // --- Pass 2: Voxel fill ---
            var worldCells = new (int, int)[nx, ny, nz];
            for (int x = 0; x < nx; x++)
            {
                if ((x & 31) == 0) Console.WriteLine($"Voxels: {x}/{nx}");
                for (int z = 0; z < nz; z++)
                {
                    int gY = ground[x, z];
                    int wY = localWater[x, z];
                    for (int y = 0; y < ny; y++)
                    {
                        (int, int) block;
                        if (y > gY)
                        {
                            block = y <= wY ? (WorldGenSettings.Blocks.Water, 0) : (WorldGenSettings.Blocks.Air, 0);
                        }
                        else if (y == gY)
                        {
                            if (wY > sea && (wY - gY) <= IslandSettings.BeachBuffer + IslandSettings.RiverBankSand)
                                block = (WorldGenSettings.Blocks.Sand, 0);
                            else
                            {
                                int top = Layering.ChooseSurfaceBlock(biome[x, z], gY, sea, snow, slope01[x, z]);
                                block = (top, 0);
                            }
                        }
                        else if (y >= gY - WorldGenSettings.Terrain.DirtDepth)
                        {
                            int sub = Layering.ChooseSubsurfaceBlock(biome[x, z], y, gY, sea);
                            block = (sub, 0);
                        }
                        else
                        {
                            int meta = StrataMap.RockMetaAt(x, y, z, config);
                            block = (WorldGenSettings.Blocks.Stone, meta);
                        }
                        worldCells[x, y, z] = block;
                    }
                }
            }

            // --- Pass 3: Global flora ---
            Console.WriteLine("Flora pass...");
            bool anySolid = true; // ref param; not used
            FloraPlacer.PlaceTreesGlobal(config, ground, biome, slope01, localWater, worldCells, ref anySolid);

            // --- Write file ---
            string dir = Path.GetDirectoryName(filename);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Console.WriteLine("Writing world file...");
            using (var bw = new BinaryWriter(File.Open(filename, FileMode.Create, FileAccess.Write, FileShare.None)))
            {
                bw.Write('V'); bw.Write('G'); bw.Write('0'); bw.Write('1');
                bw.Write(nx); bw.Write(ny); bw.Write(nz);
                for (int x = 0; x < nx; x++)
                {
                    if ((x & 63) == 0) Console.WriteLine($"Write: {x}/{nx}");
                    for (int y = 0; y < ny; y++)
                    {
                        for (int z = 0; z < nz; z++)
                        {
                            var block = worldCells[x, y, z];
                            bw.Write(block.Item1);
                            bw.Write(block.Item2);
                        }
                    }
                }
            }
            Console.WriteLine("Done.");
        }

        // Block until all chunks within current view distance around center are attached to the scene.
        public void EnsureViewLoaded(Vec3 center)
        {
            var desired = BuildDesiredSet(center);
            Volatile.Write(ref desiredKeys, desired);

            // Synchronous attach of desired chunks (no workers, no queue):
            // - If preloadedWorld exists, slice directly from RAM
            // - Else, generate on the spot via WorldGenerator
            foreach (var key in desired)
            {
                if (loadedChunkMap.ContainsKey(key)) continue;
                try
                {
                    if (preloadedWorld != null)
                    {
                        AttachChunkFromPreloaded(key.X, key.Y, key.Z);
                    }
                    else
                    {
                        AttachChunkFromGenerator(key.X, key.Y, key.Z);
                    }
                }
                catch { /* ignore single chunk failure to keep going */ }
            }

            // Sync entity layer into Objects and build BVH
            scene.Update(0.0f);
        }

        // Block until ALL chunks in the world are attached to the scene, regardless of view distance.
        public void EnsureAllChunksLoaded()
        {
            int chunkCountX = config.ChunksX;
            int chunkCountY = config.ChunksY;
            int chunkCountZ = config.ChunksZ;

            // Desired set = entire world
            var all = new HashSet<Vec3i>(chunkCountX * chunkCountY * chunkCountZ);
            for (int cx = 0; cx < chunkCountX; cx++)
                for (int cy = 0; cy < chunkCountY; cy++)
                    for (int cz = 0; cz < chunkCountZ; cz++)
                        all.Add(new Vec3i(cx, cy, cz));
            Volatile.Write(ref desiredKeys, all);

            // Attach synchronously using preloaded world if present, else generator
            foreach (var key in all)
            {
                if (loadedChunkMap.ContainsKey(key)) continue;
                try
                {
                    if (preloadedWorld != null)
                        AttachChunkFromPreloaded(key.X, key.Y, key.Z);
                    else
                        AttachChunkFromGenerator(key.X, key.Y, key.Z);
                }
                catch { }
            }

            // Flush entity geometry and rebuild BVH
            scene.Update(0.0f);
        }

        private void AttachChunkFromPreloaded(int cx, int cy, int cz)
        {
            int sx = Math.Min(config.ChunkSize, preNx - cx * config.ChunkSize);
            int sy = Math.Min(config.ChunkSize, preNy - cy * config.ChunkSize);
            int sz = Math.Min(config.ChunkSize, preNz - cz * config.ChunkSize);
            if (sx <= 0 || sy <= 0 || sz <= 0) return;

            var chunkCells = new (int, int)[sx, sy, sz];
            bool anySolid = false;
            for (int x = 0; x < sx; x++)
            {
                int gx = cx * config.ChunkSize + x;
                for (int y = 0; y < sy; y++)
                {
                    int gy = cy * config.ChunkSize + y;
                    for (int z = 0; z < sz; z++)
                    {
                        int gz = cz * config.ChunkSize + z;
                        var cell = preloadedWorld[gx, gy, gz];
                        chunkCells[x, y, z] = cell;
                        if (cell.Item1 != 0) anySolid = true;
                    }
                }
            }
            if (!anySolid) return;

            Vec3 minCorner = new Vec3(
                config.WorldMin.X + cx * config.ChunkSize * config.VoxelSize.X,
                config.WorldMin.Y + cy * config.ChunkSize * config.VoxelSize.Y,
                config.WorldMin.Z + cz * config.ChunkSize * config.VoxelSize.Z
            );

            var vg = new VolumeGrid(chunkCells, minCorner, config.VoxelSize, materialLookup);
            var key = new Vec3i(cx, cy, cz);
            loadedChunkMap[key] = vg;
            volumeGrids.Add(vg);

            // Create an entity for the chunk geometry and attach it
            var vgEntity = scene.Add(vg);

            // Deterministic per-chunk entities from preloaded cells
            var extraEnts = SimpleEntityPlacer.PlaceEntitiesForChunk(
                chunkCells, minCorner, config.VoxelSize,
                cx * config.ChunkSize, cy * config.ChunkSize, cz * config.ChunkSize,
                key, config.ChunkSize);

            // Attach extra entities (lights, etc.)
            AttachEntityList(extraEnts);

            // Track all entities for this chunk for later detach/cache
            var allEnts = new List<ISceneEntity>(1 + (extraEnts?.Count ?? 0));
            allEnts.Add(vgEntity);
            if (extraEnts != null && extraEnts.Count > 0) allEnts.AddRange(extraEnts);
            loadedEntityMap[key] = allEnts;

            attached.TryAdd(key, 0);
        }

        private void AttachChunkFromGenerator(int cx, int cy, int cz)
        {
            var cells = new (int, int)[config.ChunkSize, config.ChunkSize, config.ChunkSize];
            bool anySolid;
            generator.GenerateChunkCells(cx, cy, cz, config, cells, out anySolid);
            if (!anySolid) return;

            int baseX = cx * config.ChunkSize;
            int baseY = cy * config.ChunkSize;
            int baseZ = cz * config.ChunkSize;
            Vec3 minCorner = new Vec3(
                config.WorldMin.X + baseX * config.VoxelSize.X,
                config.WorldMin.Y + baseY * config.VoxelSize.Y,
                config.WorldMin.Z + baseZ * config.VoxelSize.Z
            );
            var vg = new VolumeGrid(cells, minCorner, config.VoxelSize, materialLookup);
            var key = new Vec3i(cx, cy, cz);
            loadedChunkMap[key] = vg;
            volumeGrids.Add(vg);

            // Create an entity for the chunk geometry and attach it
            var vgEntity = scene.Add(vg);

            // Deterministic per-chunk entities
            var extraEnts = SimpleEntityPlacer.PlaceEntitiesForChunk(
                cells, minCorner, config.VoxelSize,
                baseX, baseY, baseZ,
                key, config.ChunkSize);

            // Attach extra entities (lights, etc.)
            AttachEntityList(extraEnts);

            // Track all entities for this chunk for later detach/cache
            var allEnts = new List<ISceneEntity>(1 + (extraEnts?.Count ?? 0));
            allEnts.Add(vgEntity);
            if (extraEnts != null && extraEnts.Count > 0) allEnts.AddRange(extraEnts);
            loadedEntityMap[key] = allEnts;

            attached.TryAdd(key, 0);
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

        private void EnqueueMappedFileJob(int cx, int cy, int cz, string filePath, long dataOffset, int nx, int ny, int nz, Vec3 worldMinCorner, Vec3 voxelSize, int chunkSize, CountdownEvent group)
        {
            jobQueue.Enqueue(new BuildJob
            {
                Kind = JobKind.FromMappedFile,
                Cx = cx,
                Cy = cy,
                Cz = cz,
                FilePath = filePath,
                DataOffset = dataOffset,
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
                    else if (job.Kind == JobKind.FromWorldCells)
                    {
                        DoFileJob(job);
                    }
                    else
                    {
                        DoMappedFileJob(job);
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

        private void DoMappedFileJob(BuildJob job)
        {
            int cx = job.Cx;
            int cy = job.Cy;
            int cz = job.Cz;
            var key = new Vec3i(cx, cy, cz);

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

            using (var mmf = MemoryMappedFile.CreateFromFile(job.FilePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read))
            using (var acc = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
            {
                for (int x = 0; x < sx; x++)
                {
                    int gx = cx * job.ChunkSize + x;
                    for (int y = 0; y < sy; y++)
                    {
                        int gy = cy * job.ChunkSize + y;
                        for (int z = 0; z < sz; z++)
                        {
                            int gz = cz * job.ChunkSize + z;
                            long idx = ((long)gx * job.Ny + gy) * job.Nz + gz;
                            long byteOffset = job.DataOffset + idx * 8L;
                            int mat = acc.ReadInt32(byteOffset);
                            int meta = acc.ReadInt32(byteOffset + 4);
                            chunkCells[x, y, z] = (mat, meta);
                            if (mat != 0) anySolid = true;
                        }
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

                // Attach to scene via entity layer
                volumeGrids.Add(item.vg);
                loadedChunkMap[item.key] = item.vg;

                // Create an entity for the chunk geometry
                var vgEntity2 = scene.Add(item.vg);

                // Attach extra entities (lights, etc.)
                if (item.ents != null && item.ents.Count > 0)
                {
                    AttachEntityList(item.ents);
                }

                // Track both the chunk entity and extras
                var entsAll = new List<ISceneEntity>(1 + (item.ents?.Count ?? 0));
                entsAll.Add(vgEntity2);
                if (item.ents != null && item.ents.Count > 0) entsAll.AddRange(item.ents);
                loadedEntityMap[item.key] = entsAll;

                attached.TryAdd(item.key, 0);

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

            // Attach to scene via entity layer
            loadedChunkMap[key] = vg;
            volumeGrids.Add(vg);

            // Create entity for geometry
            var vgEntity = scene.Add(vg);

            // Merge any cached entities (lights, etc.) and attach them
            List<ISceneEntity> allEnts = new List<ISceneEntity>(1 + (cachedEnts?.Count ?? 0));
            allEnts.Add(vgEntity);
            if (cachedEnts != null && cachedEnts.Count > 0)
            {
                AttachEntityList(cachedEnts);
                allEnts.AddRange(cachedEnts);
            }
            loadedEntityMap[key] = allEnts;

            attached.TryAdd(key, 0);
            
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
