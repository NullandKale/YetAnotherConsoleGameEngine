using System;

namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    internal static class RiverNetworkGlobal
    {
        public static void Compute(int nx, int nz, WorldConfig cfg, int[,] ground, out float[,] carveDepth, out int[,] riverWaterY)
        {
            carveDepth = new float[nx, nz];
            riverWaterY = new int[nx, nz];
            int sea = cfg.WaterLevel;

            // Steepest-descent direction (D8) for each cell
            sbyte[,] dirX = new sbyte[nx, nz];
            sbyte[,] dirZ = new sbyte[nx, nz];

            for (int x = 0; x < nx; x++)
            {
                for (int z = 0; z < nz; z++)
                {
                    int h0 = ground[x, z];
                    int bestDrop = 0; sbyte bx = 0, bz = 0;
                    for (int oz = -1; oz <= 1; oz++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oz == 0) continue;
                            int nx2 = x + ox, nz2 = z + oz;
                            if (nx2 < 0 || nx2 >= nx || nz2 < 0 || nz2 >= nz) continue;
                            int h1 = ground[nx2, nz2];
                            int drop = h0 - h1;
                            if (drop > bestDrop)
                            {
                                bestDrop = drop; bx = (sbyte)ox; bz = (sbyte)oz;
                            }
                        }
                    }
                    dirX[x, z] = bx; dirZ[x, z] = bz;
                }
            }

            // Accumulation: process cells in ascending height order
            var order = new (int x, int z, int h)[nx * nz];
            int k = 0;
            for (int x = 0; x < nx; x++)
                for (int z = 0; z < nz; z++)
                    order[k++] = (x, z, ground[x, z]);
            Array.Sort(order, (a, b) => a.h.CompareTo(b.h));

            float[,] accum = new float[nx, nz];
            for (int i = 0; i < order.Length; i++)
            {
                var c = order[i];
                float a = accum[c.x, c.z];
                if (a <= 0) a = 1.0f;
                int nx2 = c.x + dirX[c.x, c.z];
                int nz2 = c.z + dirZ[c.x, c.z];
                if (dirX[c.x, c.z] != 0 || dirZ[c.x, c.z] != 0)
                {
                    if (nx2 >= 0 && nx2 < nx && nz2 >= 0 && nz2 < nz)
                        accum[nx2, nz2] += a;
                }
            }

            for (int x = 0; x < nx; x++)
            {
                for (int z = 0; z < nz; z++)
                {
                    float a = accum[x, z];
                    float t = (a - IslandSettings.RiverAccumThreshold) / IslandSettings.RiverAccumThreshold;
                    if (t <= 0)
                    {
                        carveDepth[x, z] = 0.0f;
                        riverWaterY[x, z] = sea;
                        continue;
                    }
                    float carve = MathF.Min(IslandSettings.RiverMaxCarve, MathF.Max(0.0f, t) * IslandSettings.RiverMaxCarve);
                    carveDepth[x, z] = carve;
                    int bedY = ground[x, z] - (int)MathF.Floor(carve);
                    int surface = Math.Max(sea, bedY + (int)MathF.Ceiling(IslandSettings.RiverWaterDepth));
                    riverWaterY[x, z] = surface;
                }
            }
        }
    }
}
