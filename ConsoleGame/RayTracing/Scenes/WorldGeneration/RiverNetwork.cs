using System;

namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    internal static class RiverNetwork
    {
        private struct Cell
        {
            public int X, Z, H;
        }

        // Computes river carving depths and river surface water height for a chunk.
        // - ground: input terrain heights (modified in-place if applyCarve is true)
        // - outCarveDepth: per-cell carve depth [0..RiverMaxCarve]
        // - outRiverWaterY: per-cell river water surface height (>= sea)
        public static void ComputeForChunk(int cx, int cz, WorldConfig cfg, int[,] ground, bool applyCarve,
                                           out float[,] outCarveDepth, out int[,] outRiverWaterY)
        {
            int size = cfg.ChunkSize;
            outCarveDepth = new float[size, size];
            outRiverWaterY = new int[size, size];
            int sea = cfg.WaterLevel;

            // Pre-sample a small border of heights to support flow out of the chunk
            int baseX = cx * size, baseZ = cz * size;

            // Downstream indices for each local cell (-1/-1 for none)
            int[,] dnX = new int[size, size];
            int[,] dnZ = new int[size, size];

            // Build flow directions using D8 steepest descent on global heights
            for (int lx = 0; lx < size; lx++)
            {
                int gx = baseX + lx;
                for (int lz = 0; lz < size; lz++)
                {
                    int gz = baseZ + lz;
                    int h0 = ground[lx, lz];
                    int bestDx = 0, bestDz = 0, bestDrop = 0;
                    for (int oz = -1; oz <= 1; oz++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oz == 0) continue;
                            int ng = TerrainNoise.HeightY(gx + ox, gz + oz, cfg);
                            int drop = h0 - ng;
                            if (drop > bestDrop)
                            {
                                bestDrop = drop; bestDx = ox; bestDz = oz;
                            }
                        }
                    }
                    dnX[lx, lz] = bestDrop > 0 ? bestDx : 0;
                    dnZ[lx, lz] = bestDrop > 0 ? bestDz : 0;
                }
            }

            // Flow accumulation: sort cells by height asc, then push accumulation to downstream
            Cell[] cells = new Cell[size * size];
            int idx = 0;
            for (int lx = 0; lx < size; lx++)
                for (int lz = 0; lz < size; lz++)
                    cells[idx++] = new Cell { X = lx, Z = lz, H = ground[lx, lz] };
            Array.Sort(cells, (a, b) => a.H.CompareTo(b.H));

            float[,] accum = new float[size, size];
            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i];
                float a = accum[c.X, c.Z];
                if (a <= 0) a = 1.0f; // at least one unit of flow per cell
                int nx = c.X + dnX[c.X, c.Z];
                int nz = c.Z + dnZ[c.X, c.Z];
                if (nx >= 0 && nx < size && nz >= 0 && nz < size)
                {
                    accum[nx, nz] += a;
                }
            }

            // Carve where accumulation is high
            for (int lx = 0; lx < size; lx++)
            {
                for (int lz = 0; lz < size; lz++)
                {
                    float a = accum[lx, lz];
                    float t = (a - IslandSettings.RiverAccumThreshold) / IslandSettings.RiverAccumThreshold;
                    if (t <= 0)
                    {
                        outCarveDepth[lx, lz] = 0.0f;
                        outRiverWaterY[lx, lz] = sea;
                        continue;
                    }
                    float carve = MathF.Min(IslandSettings.RiverMaxCarve, MathF.Max(0.0f, t) * IslandSettings.RiverMaxCarve);
                    outCarveDepth[lx, lz] = carve;
                    int bedY = ground[lx, lz] - (int)MathF.Floor(carve);
                    int surface = Math.Max(sea, bedY + (int)MathF.Ceiling(IslandSettings.RiverWaterDepth));
                    outRiverWaterY[lx, lz] = surface;
                }
            }

            if (applyCarve)
            {
                for (int lx = 0; lx < size; lx++)
                {
                    for (int lz = 0; lz < size; lz++)
                    {
                        int lower = (int)MathF.Floor(outCarveDepth[lx, lz]);
                        if (lower > 0)
                        {
                            ground[lx, lz] = Math.Max(0, ground[lx, lz] - lower);
                        }
                    }
                }
            }
        }
    }
}
