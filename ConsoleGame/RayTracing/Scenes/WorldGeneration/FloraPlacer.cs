using System;

namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    internal static class FloraPlacer
    {
        private static uint Hash(int x, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)GenMath.FastHash(x, 0, z, seed);
                // xorshift mix
                h ^= h << 13; h ^= h >> 17; h ^= h << 5;
                return h;
            }
        }

        public static void PlaceTreesInChunk(int cx, int cy, int cz, WorldConfig cfg, int[,] ground, Biome[,] biome, float[,] slope01, int[,] localWater, (int, int)[,,] cells, ref bool anySolid)
        {
            int size = cfg.ChunkSize;
            int baseX = cx * size;
            int baseY = cy * size;
            int baseZ = cz * size;
            int sea = cfg.WaterLevel;
            int snow = cfg.SnowLevel;

            for (int lx = 0; lx < size; lx++)
            {
                int gx = baseX + lx;
                for (int lz = 0; lz < size; lz++)
                {
                    int gz = baseZ + lz;
                    int gY = ground[lx, lz];
                    int wY = localWater[lx, lz];
                    float slope = slope01[lx, lz];
                    Biome b = biome[lx, lz];

                    // Only if the surface is in this chunk
                    int lyTop = gY - baseY;
                    if (lyTop < 0 || lyTop >= size) continue;

                    // Basic suitability: gentle slope, above water, below snow
                    if (gY <= wY) continue;
                    if (gY >= snow - 2) continue;
                    if (slope > 0.45f) continue;

                    // Density by biome
                    float density;
                    density = (b == Biome.Forest) ? 0.03f : 0.0f; // trees only in forest
                    if (density <= 0.0f) continue;

                    // Deterministic RNG
                    uint h = Hash(gx, gz, cfg.WorldSeed + 90001);
                    float r = (h & 0xFFFF) / 65535.0f;
                    if (r > density) continue;

                    // Tree parameters
                    bool conifer = (b == Biome.Taiga) || ((h >> 16 & 3) == 0);
                    int trunkBase = lyTop + 1;
                    int trunkH = conifer ? 6 + (int)(h >> 2 & 7) : 4 + (int)(h >> 3 & 5);
                    int canopyR = conifer ? 2 : 2 + (int)(h >> 6 & 1);

                    // Ensure canopy fits within this chunk vertically; if not, shorten trunk.
                    int desiredTop = trunkBase + trunkH - (conifer ? 2 : 1) + (conifer ? 2 : 2);
                    if (desiredTop > size - 1)
                    {
                        int over = desiredTop - (size - 1);
                        trunkH = Math.Max(3, trunkH - over);
                    }

                    // Place trunk
                    for (int t = 0; t < trunkH; t++)
                    {
                        int ly = trunkBase + t;
                        if (ly < 0 || ly >= size) continue;
                        if (cells[lx, ly, lz].Item1 == WorldGenSettings.Blocks.Air || cells[lx, ly, lz].Item1 == WorldGenSettings.Blocks.TallGrass)
                        {
                            cells[lx, ly, lz] = (WorldGenSettings.Blocks.Wood, 0);
                            anySolid = true;
                        }
                    }

                    // Place canopy
                    int canopyBase = trunkBase + trunkH - (conifer ? 2 : 1);
                    bool anyLeaves = false;
                    for (int dy = - (conifer ? 0 : 1); dy <= (conifer ? 2 : 2); dy++)
                    {
                        int ly = canopyBase + dy;
                        if (ly < 0 || ly >= size) continue;
                        int radius = conifer ? Math.Max(1, canopyR - Math.Abs(dy)) : canopyR - (dy == 2 ? 1 : 0);
                        for (int rx = -radius; rx <= radius; rx++)
                        {
                            int lx2 = lx + rx;
                            if (lx2 < 0 || lx2 >= size) continue;
                            for (int rz = -radius; rz <= radius; rz++)
                            {
                                int lz2 = lz + rz;
                                if (lz2 < 0 || lz2 >= size) continue;
                                // Roundish canopy using Chebyshev radius for fuller shape
                                if (Math.Max(Math.Abs(rx), Math.Abs(rz)) > radius) continue;
                                if (cells[lx2, ly, lz2].Item1 == WorldGenSettings.Blocks.Air || cells[lx2, ly, lz2].Item1 == WorldGenSettings.Blocks.TallGrass)
                                {
                                    cells[lx2, ly, lz2] = (WorldGenSettings.Blocks.Leaves, 0);
                                    anySolid = true;
                                    anyLeaves = true;
                                }
                            }
                        }
                    }

                    // Fallback: if no leaves placed (e.g., canopy clipped), add a small crown at trunk top.
                    if (!anyLeaves)
                    {
                        int ly = trunkBase + trunkH - 1;
                        if (ly >= 0 && ly < size)
                        {
                            for (int rx = -1; rx <= 1; rx++)
                            {
                                int lx2 = lx + rx; if (lx2 < 0 || lx2 >= size) continue;
                                for (int rz = -1; rz <= 1; rz++)
                                {
                                    int lz2 = lz + rz; if (lz2 < 0 || lz2 >= size) continue;
                                    if (cells[lx2, ly, lz2].Item1 == WorldGenSettings.Blocks.Air)
                                    {
                                        cells[lx2, ly, lz2] = (WorldGenSettings.Blocks.Leaves, 0);
                                        anySolid = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // Global pass tree placer: scans entire world arrays to place trees deterministically.
        public static void PlaceTreesGlobal(WorldConfig cfg, int[,] ground, Biome[,] biome, float[,] slope01, int[,] localWater, (int, int)[,,] cells, ref bool anySolid)
        {
            int nx = ground.GetLength(0);
            int nz = ground.GetLength(1);
            int ny = cfg.WorldHeight;
            int sea = cfg.WaterLevel;
            int snow = cfg.SnowLevel;

            for (int gx = 0; gx < nx; gx++)
            {
                if ((gx & 63) == 0) Console.WriteLine($"Flora: {gx}/{nx}");
                for (int gz = 0; gz < nz; gz++)
                {
                    int gY = ground[gx, gz];
                    int wY = localWater[gx, gz];
                    float slope = slope01[gx, gz];
                    Biome b = biome[gx, gz];

                    if (gY <= wY || gY >= snow - 2) continue;

                    float density = b == Biome.Forest ? 0.03f : 0.0f;
                    if (density <= 0.0f) continue;

                    uint h = Hash(gx, gz, cfg.WorldSeed + 90001);
                    float r = (h & 0xFFFF) / 65535.0f;
                    if (r > density) continue;

                    bool conifer = (b == Biome.Taiga) || ((h >> 16 & 3) == 0);
                    int trunkBase = gY + 1;
                    int trunkH = conifer ? 6 + (int)(h >> 2 & 7) : 4 + (int)(h >> 3 & 5);
                    int canopyR = conifer ? 2 : 2 + (int)(h >> 6 & 1);
                    int desiredTop = trunkBase + trunkH + 2;
                    if (desiredTop >= ny) trunkH = Math.Max(3, ny - trunkBase - 2);

                    for (int t = 0; t < trunkH; t++)
                    {
                        int y = trunkBase + t; if (y < 0 || y >= ny) break;
                        if (cells[gx, y, gz].Item1 == WorldGenSettings.Blocks.Air || cells[gx, y, gz].Item1 == WorldGenSettings.Blocks.TallGrass)
                        { cells[gx, y, gz] = (WorldGenSettings.Blocks.Wood, 0); anySolid = true; }
                    }

                    int canopyBase = trunkBase + trunkH - (conifer ? 2 : 1);
                    bool anyLeaves = false;
                    for (int dy = -(conifer ? 0 : 1); dy <= (conifer ? 2 : 2); dy++)
                    {
                        int y = canopyBase + dy; if (y < 0 || y >= ny) continue;
                        int radius = conifer ? Math.Max(1, canopyR - Math.Abs(dy)) : canopyR - (dy == 2 ? 1 : 0);
                        for (int rx = -radius; rx <= radius; rx++)
                        {
                            int x2 = gx + rx; if (x2 < 0 || x2 >= nx) continue;
                            for (int rz = -radius; rz <= radius; rz++)
                            {
                                int z2 = gz + rz; if (z2 < 0 || z2 >= nz) continue;
                                if (Math.Max(Math.Abs(rx), Math.Abs(rz)) > radius) continue;
                                if (cells[x2, y, z2].Item1 == WorldGenSettings.Blocks.Air || cells[x2, y, z2].Item1 == WorldGenSettings.Blocks.TallGrass)
                                { cells[x2, y, z2] = (WorldGenSettings.Blocks.Leaves, 0); anySolid = true; anyLeaves = true; }
                            }
                        }
                    }
                    if (!anyLeaves)
                    {
                        int y = trunkBase + trunkH - 1; if (y >= 0 && y < ny)
                        {
                            for (int rx = -1; rx <= 1; rx++)
                            {
                                int x2 = gx + rx; if (x2 < 0 || x2 >= nx) continue;
                                for (int rz = -1; rz <= 1; rz++)
                                {
                                    int z2 = gz + rz; if (z2 < 0 || z2 >= nz) continue;
                                    if (cells[x2, y, z2].Item1 == WorldGenSettings.Blocks.Air)
                                    { cells[x2, y, z2] = (WorldGenSettings.Blocks.Leaves, 0); anySolid = true; }
                                }
                            }
                        }
                    }
                }
                // After trees for this x, also sprinkle desert features sparsely
                for (int gz = 0; gz < nz; gz++)
                {
                    if (biome[gx, gz] != Biome.Desert) continue;
                    int gY = ground[gx, gz];
                    int wY = localWater[gx, gz];
                    if (gY <= wY) continue;
                    if (slope01[gx, gz] > 0.25f) continue; // flatter for desert props
                    uint h = Hash(gx * 73856093 ^ gz * 19349663, gz * 83492791 ^ gx * 297121507, cfg.WorldSeed + 1234567);
                    float r = (h & 0xFFFF) / 65535.0f;
                    // 70% chance to do nothing
                    if (r < 0.70f) continue;
                    if (r < 0.85f)
                    {
                        // Small cactus (wood column)
                        int height = 2 + (int)((h >> 16) & 3); // 2..5
                        for (int t = 1; t <= height; t++)
                        {
                            int y = gY + t; if (y >= ny) break;
                            if (cells[gx, y, gz].Item1 == WorldGenSettings.Blocks.Air)
                            { cells[gx, y, gz] = (WorldGenSettings.Blocks.Wood, 0); anySolid = true; }
                        }
                    }
                    else
                    {
                        // Rock: small stone pile radius 1
                        int y = gY + 1; if (y >= ny) continue;
                        for (int rx = -1; rx <= 1; rx++)
                        {
                            int x2 = gx + rx; if (x2 < 0 || x2 >= nx) continue;
                            for (int rz = -1; rz <= 1; rz++)
                            {
                                int z2 = gz + rz; if (z2 < 0 || z2 >= nz) continue;
                                if (Math.Abs(rx) + Math.Abs(rz) > 1) continue;
                                if (cells[x2, y, z2].Item1 == WorldGenSettings.Blocks.Air)
                                { cells[x2, y, z2] = (WorldGenSettings.Blocks.Stone, 1); anySolid = true; }
                            }
                        }
                    }
                }
            }
        }
    }
}
