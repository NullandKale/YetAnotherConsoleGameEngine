using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using ConsoleGame.RayTracing.Objects;
using ConsoleGame.RayTracing.Scenes.ConsoleGame.RayTracing.Scenes;

namespace ConsoleGame.RayTracing.Scenes
{
    public sealed class VolumeScene : Scene
    {
        // List and dictionary to manage loaded volume chunks for infinite world
        private readonly List<VolumeGrid> volumeGrids = new List<VolumeGrid>();
        private readonly Dictionary<(int, int, int), VolumeGrid> loadedChunkMap = new Dictionary<(int, int, int), VolumeGrid>();

        // Remove all loaded volume chunks from the scene
        public void ClearLoadedVolumes()
        {
            // Remove each VolumeGrid from the scene objects
            foreach (var vg in volumeGrids)
            {
                Objects.Remove(vg);
            }
            volumeGrids.Clear();
            loadedChunkMap.Clear();
        }

        /// <summary>
        /// Center On Coordinate: Load volume chunks around the given world center position.
        /// This allows an “infinite” world by loading/unloading chunks as the center moves.
        /// </summary>
        public void COC(Vec3 center)
        {
            // Use the view distance in chunks (radius around the center chunk to load)
            int cxCenter = (int)Math.Floor(center.X / VolumeScenes.CHUNK_SIZE);
            int czCenter = (int)Math.Floor(center.Z / VolumeScenes.CHUNK_SIZE);

            // Determine the range of chunk indices to load in X and Z around the center
            int minCX = cxCenter - VolumeScenes.VIEW_DISTANCE_CHUNKS;
            int maxCX = cxCenter + VolumeScenes.VIEW_DISTANCE_CHUNKS;
            int minCZ = czCenter - VolumeScenes.VIEW_DISTANCE_CHUNKS;
            int maxCZ = czCenter + VolumeScenes.VIEW_DISTANCE_CHUNKS;

            // Load any new chunks within the range that are not already loaded
            for (int cx = minCX; cx <= maxCX; cx++)
            {
                for (int cz = minCZ; cz <= maxCZ; cz++)
                {
                    for (int cy = 0; cy < VolumeScenes.CHUNKS_Y; cy++)
                    {
                        var chunkKey = (cx, cy, cz);
                        if (loadedChunkMap.ContainsKey(chunkKey))
                            continue; // already loaded

                        // Generate the volume grid for this chunk
                        var cells = new (int, int)[VolumeScenes.CHUNK_SIZE, VolumeScenes.CHUNK_SIZE, VolumeScenes.CHUNK_SIZE];
                        bool anySolid = false;
                        // Compute the world-space coordinates of this chunk's minimum corner
                        int baseX = cx * VolumeScenes.CHUNK_SIZE;
                        int baseY = cy * VolumeScenes.CHUNK_SIZE;
                        int baseZ = cz * VolumeScenes.CHUNK_SIZE;

                        // Fill the chunk's cells using the procedural world generator
                        for (int lx = 0; lx < VolumeScenes.CHUNK_SIZE; lx++)
                        {
                            int gx = baseX + lx;
                            for (int lz = 0; lz < VolumeScenes.CHUNK_SIZE; lz++)
                            {
                                int gz = baseZ + lz;
                                // Compute terrain height at this (gx, gz) column
                                int groundHeight = VolumeScenes.ComputeHeight(gx, gz);
                                for (int ly = 0; ly < VolumeScenes.CHUNK_SIZE; ly++)
                                {
                                    int gy = baseY + ly;
                                    (int mat, int meta) block = (0, 0); // default air
                                    if (gy > groundHeight)
                                    {
                                        // Above ground
                                        if (gy <= VolumeScenes.WATER_LEVEL)
                                        {
                                            // Below or at water level: fill with water
                                            block = (4, 0); // water
                                        }
                                        else
                                        {
                                            block = (0, 0); // air
                                        }
                                    }
                                    else
                                    {
                                        // At or below ground height
                                        if (gy == groundHeight)
                                        {
                                            // Surface block
                                            if (groundHeight > VolumeScenes.SNOW_LEVEL)
                                            {
                                                // High altitude: snowy cap
                                                block = (8, 0); // snow
                                            }
                                            else if (groundHeight <= VolumeScenes.WATER_LEVEL + 2)
                                            {
                                                // Near water or underwater: sand beach or ocean floor
                                                block = (5, 0); // sand
                                            }
                                            else
                                            {
                                                block = (3, 0); // grass
                                            }
                                        }
                                        else
                                        {
                                            // Below the surface
                                            if (groundHeight > VolumeScenes.SNOW_LEVEL)
                                            {
                                                // High altitude (mountain) region: no topsoil, use stone directly
                                                // (Do nothing here, will default to stone later)
                                            }
                                            else if (gy >= groundHeight - 3)
                                            {
                                                // A few blocks below the surface in lower altitudes: topsoil (dirt)
                                                block = (2, 0); // dirt
                                            }
                                            // If not handled above, default to stone or ore below
                                            if (block.Item1 == 0)
                                            {
                                                // Deeper underground: mostly stone with some ores
                                                block = (1, 0); // stone
                                                // Rare chance to place an ore block in stone
                                                if (gy > 0) // avoid ore at bedrock layer
                                                {
                                                    int oreRand = VolumeScenes.FastHash(gx, gy, gz) & 0x3F; // 0–63
                                                    if (oreRand == 0)
                                                    {
                                                        int oreType = VolumeScenes.FastHash(gx + 11, gy + 7, gz + 19) % 3;
                                                        block = (9, oreType); // ore with meta type 0,1,2
                                                    }
                                                }
                                            }
                                        }
                                        // Ensure bedrock at bottom: make y=0 a solid block (stone as bedrock)
                                        if (gy == 0)
                                        {
                                            block = (1, 0); // bedrock (using stone id for simplicity)
                                        }
                                    }

                                    // Apply cave generation: carve out solid blocks to create caves
                                    if (block.Item1 != 0 && block.Item1 != 4) // if solid (not air or water)
                                    {
                                        if (gy > 0) // don't carve the bottom layer
                                        {
                                            double caveNoise = VolumeScenes.CaveNoise(gx, gy, gz);
                                            if (caveNoise > 0.6)
                                            {
                                                // Carve out this block (make it air)
                                                block = (0, 0);
                                            }
                                        }
                                    }

                                    cells[lx, ly, lz] = block;
                                    if (block.Item1 != 0) anySolid = true;
                                }
                            }
                        }

                        // After base terrain fill, add trees in this chunk
                        // Iterate over each column (lx,lz) in the chunk
                        for (int lx = 0; lx < VolumeScenes.CHUNK_SIZE; lx++)
                        {
                            int gx = baseX + lx;
                            for (int lz = 0; lz < VolumeScenes.CHUNK_SIZE; lz++)
                            {
                                int gz = baseZ + lz;
                                // Determine if this column has a surface in this chunk
                                int groundHeight = VolumeScenes.ComputeHeight(gx, gz);
                                int groundChunkY = groundHeight / VolumeScenes.CHUNK_SIZE;
                                if (groundChunkY != cy) continue; // surface is not in this chunk
                                // Only consider tree placement on grass blocks at lower altitudes
                                if (groundHeight > 0 && groundHeight < VolumeScenes.SNOW_LEVEL)
                                {
                                    // Check that the surface block in this column is grass (id 3)
                                    int localGroundY = groundHeight - baseY;
                                    if (localGroundY >= 0 && localGroundY < VolumeScenes.CHUNK_SIZE)
                                    {
                                        if (cells[lx, localGroundY, lz].Item1 == 3) // grass
                                        {
                                            // Use a deterministic random chance for tree placement
                                            int treeChance = VolumeScenes.FastHash(gx, groundHeight, gz) & 0xFF;
                                            if (treeChance < 4) // ~1.5% chance for a tree
                                            {
                                                // Avoid generating trees near chunk borders to prevent half-trees
                                                if (lx < 2 || lx > VolumeScenes.CHUNK_SIZE - 3 || lz < 2 || lz > VolumeScenes.CHUNK_SIZE - 3)
                                                {
                                                    continue; // skip tree at chunk edge
                                                }
                                                // Determine tree height (trunk height) with a bit of variation
                                                int trunkHeight = 5 + (VolumeScenes.FastHash(gx, groundHeight, gz) % 3); // 5-7 blocks tall
                                                // Ensure the whole tree (trunk + leaves) fits in this chunk's vertical span
                                                if (groundHeight + trunkHeight + 2 >= baseY + VolumeScenes.CHUNK_SIZE)
                                                {
                                                    continue; // tree would exceed chunk height, skip
                                                }
                                                // Place trunk blocks
                                                for (int h = 0; h < trunkHeight; h++)
                                                {
                                                    int trunkY = localGroundY + h;
                                                    cells[lx, trunkY, lz] = (6, 0); // wood trunk
                                                }
                                                // Place leaves around the top of the trunk
                                                int topY = localGroundY + trunkHeight - 1;
                                                for (int ly = 0; ly <= 2; ly++)
                                                {
                                                    for (int lxOff = -2; lxOff <= 2; lxOff++)
                                                    {
                                                        for (int lzOff = -2; lzOff <= 2; lzOff++)
                                                        {
                                                            // Compute absolute positions for the leaf block
                                                            int leafX = lx + lxOff;
                                                            int leafY = topY + ly;
                                                            int leafZ = lz + lzOff;
                                                            // Skip if out of chunk bounds
                                                            if (leafX < 0 || leafX >= VolumeScenes.CHUNK_SIZE ||
                                                                leafY < 0 || leafY >= VolumeScenes.CHUNK_SIZE ||
                                                                leafZ < 0 || leafZ >= VolumeScenes.CHUNK_SIZE)
                                                            {
                                                                continue;
                                                            }
                                                            // Avoid overwriting the trunk (center of leaves at ly=0)
                                                            if (lxOff == 0 && lzOff == 0 && ly == 0)
                                                                continue;
                                                            // Make a roughly spherical leaf cluster by skipping corners on top layer
                                                            if (Math.Abs(lxOff) == 2 && Math.Abs(lzOff) == 2 && ly == 2)
                                                                continue;
                                                            // Only place leaves in empty air (do not overwrite existing blocks)
                                                            if (cells[leafX, leafY, leafZ].Item1 == 0)
                                                            {
                                                                cells[leafX, leafY, leafZ] = (7, 0); // leaves
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

                        // If this chunk has any solid blocks, add it to the scene
                        if (!anySolid)
                            continue;
                        Vec3 minCorner = new Vec3(
                            VolumeScenes.WORLD_MIN.X + baseX * VolumeScenes.VOXEL_SIZE.X,
                            VolumeScenes.WORLD_MIN.Y + baseY * VolumeScenes.VOXEL_SIZE.Y,
                            VolumeScenes.WORLD_MIN.Z + baseZ * VolumeScenes.VOXEL_SIZE.Z
                        );
                        var volumeGrid = new VolumeGrid(cells, minCorner, VolumeScenes.VOXEL_SIZE, VoxelMaterialPalette.MaterialLookup);
                        volumeGrids.Add(volumeGrid);
                        loadedChunkMap[chunkKey] = volumeGrid;
                        Objects.Add(volumeGrid);
                    }
                }
            }

            // Unload chunks that are now outside of the view distance
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
                VolumeGrid vg = loadedChunkMap[key];
                Objects.Remove(vg);
                volumeGrids.Remove(vg);
                loadedChunkMap.Remove(key);
            }

            // Rebuild the acceleration structure for ray tracing (BVH) after adding/removing chunks
            RebuildBVH();
        }

        // Load a world from a binary file (in the 'VG01' format) and add chunks to the scene.
        public void ReloadFromExistingFile(string filename, Vec3 worldMinCorner, Vec3 voxelSize, Func<int, int, Material> materialLookup, int chunkSize = 32)
        {
            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("filename is null or empty.", nameof(filename));
            if (!File.Exists(filename))
                throw new FileNotFoundException("World file not found.", filename);

            ClearLoadedVolumes();
            using (var br = new BinaryReader(File.OpenRead(filename)))
            {
                // Read and validate header
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

                // Read all voxel data from file
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

                // Split the worldCells array into VolumeGrid chunks and add to scene
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
                                        if (cell.Item1 != 0) anySolid = true;
                                    }
                                }
                            }
                            if (!anySolid) continue;
                            Vec3 minCorner = new Vec3(
                                worldMinCorner.X + cx * chunkSize * voxelSize.X,
                                worldMinCorner.Y + cy * chunkSize * voxelSize.Y,
                                worldMinCorner.Z + cz * chunkSize * voxelSize.Z
                            );
                            var vg = new VolumeGrid(chunkCells, minCorner, voxelSize, materialLookup);
                            volumeGrids.Add(vg);
                            loadedChunkMap[(cx, cy, cz)] = vg;
                            Objects.Add(vg);
                        }
                    }
                }
            }
            RebuildBVH();
        }
    }

namespace ConsoleGame.RayTracing.Scenes
    {
        /// <summary>
        /// Thread-safe, canonical material palette for voxel blocks.
        /// - Normalizes (id, meta) to a compact, known set so "same block" always maps to the same Material.
        /// - Caches Material instances per (id, meta) to avoid excessive allocations and to keep BVH/instancing stable.
        /// - Encodes intended usage of metas (e.g., ore variants) while clamping unknown inputs to safe defaults.
        /// 
        /// Block IDs used by the generator:
        ///   0 = Air
        ///   1 = Stone
        ///   2 = Dirt
        ///   3 = Grass
        ///   4 = Water
        ///   5 = Sand
        ///   6 = Wood (trunk)
        ///   7 = Leaves
        ///   8 = Snow
        ///   9 = Ore (meta: 0=Coal, 1=Iron, 2=Copper)
        /// 
        /// If you later add new blocks, extend Normalize(...) and optionally Prewarm() to keep materials canonical.
        /// </summary>
        public static class VoxelMaterialPalette
        {
            // Public lookup delegate you can plug in wherever a Func<int,int,Material> is needed.
            public static readonly Func<int, int, Material> MaterialLookup = (id, meta) =>
            {
                var key = Normalize(id, meta);
                return Cache.GetOrAdd(key, CreateMaterial);
            };

            // Internal cache of canonical materials by normalized (id, meta).
            private static readonly ConcurrentDictionary<(int id, int meta), Material> Cache = new ConcurrentDictionary<(int id, int meta), Material>();

            // Optional: eagerly build the most common materials once (avoids first-use hiccups in hot paths).
            static VoxelMaterialPalette()
            {
                Prewarm();
            }

            /// <summary>
            /// Normalize raw (id, meta) into a compact, stable key space.
            /// Unknown IDs collapse to a single "debug" material (id=1 stone) to avoid exploding material counts.
            /// </summary>
            private static (int id, int meta) Normalize(int id, int meta)
            {
                switch (id)
                {
                    case 0: return (0, 0);                                 // Air
                    case 1: return (1, 0);                                 // Stone
                    case 2: return (2, 0);                                 // Dirt
                    case 3: return (3, 0);                                 // Grass
                    case 4: return (4, 0);                                 // Water (opaque/transparent handled by engine material)
                    case 5: return (5, 0);                                 // Sand
                    case 6: return (6, 0);                                 // Wood (trunk)
                    case 7: return (7, 0);                                 // Leaves
                    case 8: return (8, 0);                                 // Snow
                    case 9: return (9, Clamp(meta, 0, 2));                 // Ore variants: 0=Coal, 1=Iron, 2=Copper
                    default: return (1, 0);                                // Fallback to Stone to avoid creating unknown materials
                }
            }

            /// <summary>
            /// Factory creating one canonical Material per normalized key.
            /// NOTE: We intentionally construct using (id, meta) only, matching the engine's Material contract.
            /// Any PBR parameters, textures, IOR, transparency, etc., should be resolved inside Material or downstream by (id,meta).
            /// </summary>
            private static Material CreateMaterial((int id, int meta) key)
            {
                switch (key.id)
                {
                    case 0: return new Material(new Vec3(0.0, 0.0, 0.0), 0.0, 0.0, new Vec3(0.0, 0.0, 0.0));                                  // Air
                    case 1: return new Material(new Vec3(0.50, 0.50, 0.50), 0.04, 0.02, new Vec3(0.0, 0.0, 0.0));                            // Stone
                    case 2: return new Material(new Vec3(0.36, 0.24, 0.14), 0.03, 0.01, new Vec3(0.0, 0.0, 0.0));                            // Dirt
                    case 3: return new Material(new Vec3(0.22, 0.45, 0.18), 0.03, 0.01, new Vec3(0.0, 0.0, 0.0));                            // Grass
                    case 4: return new Material(new Vec3(0.05, 0.12, 0.20), 0.80, 0.02, new Vec3(0.0, 0.0, 0.0));                            // Water (engine handles transparency)
                    case 5: return new Material(new Vec3(0.76, 0.70, 0.50), 0.06, 0.02, new Vec3(0.0, 0.0, 0.0));                            // Sand
                    case 6: return new Material(new Vec3(0.35, 0.20, 0.07), 0.05, 0.02, new Vec3(0.0, 0.0, 0.0));                            // Wood (trunk)
                    case 7: return new Material(new Vec3(0.16, 0.35, 0.12), 0.10, 0.01, new Vec3(0.0, 0.0, 0.0));                            // Leaves
                    case 8: return new Material(new Vec3(0.93, 0.93, 0.95), 0.10, 0.04, new Vec3(0.0, 0.0, 0.0));                            // Snow
                    case 9:
                        {
                            switch (key.meta)
                            {
                                case 0: return new Material(new Vec3(0.20, 0.20, 0.20), 0.04, 0.02, new Vec3(0.0, 0.0, 0.0));                   // Coal ore
                                case 1: return new Material(new Vec3(0.74, 0.56, 0.45), 0.18, 0.04, new Vec3(0.0, 0.0, 0.0));                   // Iron ore
                                default: return new Material(new Vec3(0.72, 0.41, 0.25), 0.20, 0.04, new Vec3(0.0, 0.0, 0.0));                  // Copper ore
                            }
                        }
                    default: return new Material(new Vec3(0.50, 0.50, 0.50), 0.04, 0.02, new Vec3(0.0, 0.0, 0.0));                            // Fallback Stone
                }
            }

            /// <summary>
            /// Prewarm commonly used materials so the first render doesn’t pay cache misses for ubiquitous blocks.
            /// </summary>
            private static void Prewarm()
            {
                // Core terrain
                _ = MaterialLookup(1, 0); // Stone
                _ = MaterialLookup(2, 0); // Dirt
                _ = MaterialLookup(3, 0); // Grass
                _ = MaterialLookup(4, 0); // Water
                _ = MaterialLookup(5, 0); // Sand
                _ = MaterialLookup(6, 0); // Wood (trunk)
                _ = MaterialLookup(7, 0); // Leaves
                _ = MaterialLookup(8, 0); // Snow

                // Ore variants
                _ = MaterialLookup(9, 0); // Coal
                _ = MaterialLookup(9, 1); // Iron
                _ = MaterialLookup(9, 2); // Copper
            }

            private static int Clamp(int v, int lo, int hi)
            {
                if (v < lo) return lo;
                if (v > hi) return hi;
                return v;
            }
        }
    }


    public static class VolumeScenes
    {
        // World generation constants and parameters
        internal const int CHUNK_SIZE = 32;
        internal const int CHUNKS_X = 8;   // initial world size in chunks (X direction)
        internal const int CHUNKS_Y = 8;   // world height in chunks (8 * 32 = 256 blocks high)
        internal const int CHUNKS_Z = 8;   // initial world size in chunks (Z direction)
        internal const int VIEW_DISTANCE_CHUNKS = 8; // radius of chunks to load around center for infinite world
        internal static readonly Vec3 WORLD_MIN = new Vec3(0, 0, 0);
        internal static readonly Vec3 VOXEL_SIZE = new Vec3(1, 1, 1);
        internal const int WORLD_SEED = 12345;
        // Derived dimensions
        private static readonly int WORLD_WIDTH = CHUNKS_X * CHUNK_SIZE;
        private static readonly int WORLD_HEIGHT = CHUNKS_Y * CHUNK_SIZE;
        private static readonly int WORLD_DEPTH = CHUNKS_Z * CHUNK_SIZE;
        // Terrain generation thresholds
        internal static readonly int WATER_LEVEL = Math.Max(1, WORLD_HEIGHT / 4);    // water level (e.g., 1/4 of max height)
        internal static readonly int SNOW_LEVEL = (int)(WORLD_HEIGHT * 0.8f);        // altitude above which snow appears

        // Compute a deterministic height for terrain at given (x, z) using fractal noise
        internal static int ComputeHeight(int x, int z)
        {
            // Use fractal noise (multiple octaves of value noise) for terrain height
            double height = 0;
            // Base frequencies and amplitudes for noise
            double freq = 0.005;    // frequency for first octave
            double amp = WORLD_HEIGHT / 5.0;  // amplitude for first octave
            double persistence = 0.5;  // amplitude multiplier per octave
            double lacunarity = 2.0;   // frequency multiplier per octave
            // Combine several octaves of noise
            for (int octave = 0; octave < 4; octave++)
            {
                double nx = x * freq;
                double nz = z * freq;
                // Use a simple 2D pseudo-noise based on hashed grid interpolation
                double noiseVal = ValueNoise2D(nx, nz);
                height += noiseVal * amp;
                // Prepare next octave
                freq *= lacunarity;
                amp *= persistence;
            }
            // Base height offset (so terrain is not centered around 0)
            height += WORLD_HEIGHT / 4.0;
            // Clamp height to valid range [1, WORLD_HEIGHT-2] to ensure some air above and bedrock below
            int h = (int)height;
            if (h < 1) h = 1;
            if (h > WORLD_HEIGHT - 2) h = WORLD_HEIGHT - 2;
            return h;
        }

        // Simple value noise in 2D for terrain height (returns value roughly in [-1,1])
        private static double ValueNoise2D(double x, double z)
        {
            // Determine grid cell coordinates
            int x0 = (int)Math.Floor(x);
            int z0 = (int)Math.Floor(z);
            int x1 = x0 + 1;
            int z1 = z0 + 1;
            // Compute interpolation weights
            double tx = x - x0;
            double tz = z - z0;
            // Get pseudo-random values at the four corners of the cell
            double v00 = (FastHash(x0, 0, z0) & 0x7FFF) / 32767.0 * 2 - 1;  // map to [-1,1]
            double v10 = (FastHash(x1, 0, z0) & 0x7FFF) / 32767.0 * 2 - 1;
            double v01 = (FastHash(x0, 0, z1) & 0x7FFF) / 32767.0 * 2 - 1;
            double v11 = (FastHash(x1, 0, z1) & 0x7FFF) / 32767.0 * 2 - 1;
            // Bilinear interpolation of these corner values
            double i1 = Lerp(v00, v10, tx);
            double i2 = Lerp(v01, v11, tx);
            return Lerp(i1, i2, tz);
        }

        // 3D noise function for caves (returns value in [0,1]). Higher values indicate open space.
        internal static double CaveNoise(int x, int y, int z)
        {
            // Use a fixed grid size for cave noise interpolation (e.g., 32 for cave feature size)
            int grid = 32;
            // Determine surrounding grid cube indices
            int x0 = (x / grid) * grid;
            int y0 = (y / grid) * grid;
            int z0 = (z / grid) * grid;
            int x1 = x0 + grid;
            int y1 = y0 + grid;
            int z1 = z0 + grid;
            // Fractions within the cube
            double dx = (double)(x - x0) / grid;
            double dy = (double)(y - y0) / grid;
            double dz = (double)(z - z0) / grid;
            // Random values at the 8 cube corners
            double v000 = (FastHash(x0, y0, z0) & 0x7FFF) / 32767.0;
            double v100 = (FastHash(x1, y0, z0) & 0x7FFF) / 32767.0;
            double v010 = (FastHash(x0, y1, z0) & 0x7FFF) / 32767.0;
            double v110 = (FastHash(x1, y1, z0) & 0x7FFF) / 32767.0;
            double v001 = (FastHash(x0, y0, z1) & 0x7FFF) / 32767.0;
            double v101 = (FastHash(x1, y0, z1) & 0x7FFF) / 32767.0;
            double v011 = (FastHash(x0, y1, z1) & 0x7FFF) / 32767.0;
            double v111 = (FastHash(x1, y1, z1) & 0x7FFF) / 32767.0;
            // Trilinear interpolation of corner values
            double i00 = Lerp(v000, v100, dx);
            double i01 = Lerp(v001, v101, dx);
            double i10 = Lerp(v010, v110, dx);
            double i11 = Lerp(v011, v111, dx);
            double j0 = Lerp(i00, i10, dy);
            double j1 = Lerp(i01, i11, dy);
            return Lerp(j0, j1, dz);
        }

        // Linear interpolation helper
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        // Fast deterministic hash function (3D) for noise, incorporating the world seed
        internal static int FastHash(int x, int y, int z)
        {
            unchecked
            {
                uint h = 2166136261u ^ (uint)WORLD_SEED;
                h ^= (uint)x; h *= 16777619u;
                h ^= (uint)y; h *= 16777619u;
                h ^= (uint)z; h *= 16777619u;
                return (int)h;
            }
        }

        /// <summary>
        /// Build a Minecraft-like volume scene. This generates or loads a world with terrain features such as 
        /// mountains, valleys, water, snow, trees, and caves. The world is infinite in the XZ plane, 
        /// with chunks loaded around the camera position.
        /// </summary>
        /// <param name="filename">Path to a world file to load/save. If the file exists (and matches current parameters), it will be loaded. If not, a new world is generated and saved to this file. If null or empty, the world is generated procedurally without saving.</param>
        public static VolumeScene BuildMinecraftLike(string filename)
        {
            // Create a new volume scene and set up default camera and lighting
            VolumeScene scene = new VolumeScene();
            scene.DefaultCameraPos = new Vec3(0, 100, 0);  // start the camera above the world origin
            scene.Ambient = new AmbientLight(new Vec3(1, 1, 1), 0.5f);
            scene.BackgroundTop = new Vec3(0.02, 0.02, 0.03);
            scene.BackgroundBottom = new Vec3(0.01, 0.01, 0.01);
            // Add some light sources
            scene.Lights.Add(new PointLight(new Vec3(0.0, 12.0, 0.0), new Vec3(1.0, 1.0, 1.0), 400.0f));
            scene.Lights.Add(new PointLight(new Vec3(-10.0, 10.0, -10.0), new Vec3(1.0, 0.95, 0.9), 160.0f));

            // If a filename is provided and the file already exists, load that world
            if (!string.IsNullOrEmpty(filename) && File.Exists(filename))
            {
                scene.ReloadFromExistingFile(filename, WORLD_MIN, VOXEL_SIZE, VoxelMaterialPalette.MaterialLookup, CHUNK_SIZE);
            }
            else if (!string.IsNullOrEmpty(filename))
            {
                // No existing file, so generate a new world and save it
                // Define world dimensions in voxels
                int nx = CHUNKS_X * CHUNK_SIZE;
                int ny = CHUNKS_Y * CHUNK_SIZE;
                int nz = CHUNKS_Z * CHUNK_SIZE;
                // Ensure directory exists
                string dir = Path.GetDirectoryName(filename);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                // Write world data to file in binary format ('VG01')
                using (var bw = new BinaryWriter(File.Open(filename, FileMode.Create, FileAccess.Write, FileShare.None)))
                {
                    bw.Write('V'); bw.Write('G'); bw.Write('0'); bw.Write('1');
                    bw.Write(nx); bw.Write(ny); bw.Write(nz);
                    // Iterate over every voxel in the defined world region
                    for (int gx = 0; gx < nx; gx++)
                    {
                        for (int gy = 0; gy < ny; gy++)
                        {
                            for (int gz = 0; gz < nz; gz++)
                            {
                                // Determine block type for (gx, gy, gz)
                                int height = ComputeHeight(gx, gz);
                                (int mat, int meta) block;
                                if (gy > height)
                                {
                                    // Above ground
                                    if (gy <= WATER_LEVEL) block = (4, 0); // water
                                    else block = (0, 0); // air
                                }
                                else
                                {
                                    // At or below ground
                                    if (gy == height)
                                    {
                                        if (height > SNOW_LEVEL) block = (8, 0);      // snow at high alt
                                        else if (height <= WATER_LEVEL + 2) block = (5, 0); // sand near water
                                        else block = (3, 0);  // grass
                                    }
                                    else
                                    {
                                        // below surface
                                        block = (1, 0); // default stone
                                        if (height <= SNOW_LEVEL && gy >= height - 3)
                                        {
                                            block = (2, 0); // topsoil dirt (if not high altitude)
                                        }
                                        // Rare ore deposits in stone
                                        if (block.Item1 == 1 && gy > 0)
                                        {
                                            if ((FastHash(gx, gy, gz) & 0x3F) == 0)
                                            {
                                                int oreType = FastHash(gx + 11, gy + 7, gz + 19) % 3;
                                                block = (9, oreType);
                                            }
                                        }
                                        if (gy == 0)
                                        {
                                            block = (1, 0); // bedrock as stone at bottom
                                        }
                                    }
                                }
                                // Apply caves carving
                                if (block.Item1 != 0 && block.Item1 != 4 && gy > 0)
                                {
                                    if (CaveNoise(gx, gy, gz) > 0.6)
                                    {
                                        block = (0, 0);
                                    }
                                }
                                bw.Write(block.Item1);
                                bw.Write(block.Item2);
                            }
                        }
                    }
                }
                // Load the newly generated world from the file
                scene.ReloadFromExistingFile(filename, WORLD_MIN, VOXEL_SIZE, VoxelMaterialPalette.MaterialLookup, CHUNK_SIZE);
            }
            else
            {
                // No file provided: generate an infinite world on the fly around the origin
                scene.ClearLoadedVolumes();
                // Load initial chunks around the default camera position
                scene.COC(scene.DefaultCameraPos);
            }

            return scene;
        }
    }
}
