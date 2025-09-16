using System;

namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    internal static class StrataMap
    {
        // Returns a Stone meta [0..2] based on altitude and noise to simulate rock variation.
        public static int RockMetaAt(int gx, int gy, int gz, WorldConfig cfg)
        {
            // Banding by altitude
            float hBand = (gy % 24) / 24.0f; // repeat every 24 blocks
            int baseMeta = hBand < 0.33f ? 0 : (hBand < 0.66f ? 1 : 2);

            // Perturb by low-frequency noise
            float n = GenMath.FBM2D(gx * 0.004f, gz * 0.004f, 3, 2.0f, 0.5f, 1.0f, cfg.WorldSeed + 4201);
            if (n < 0.33f) return 0;
            if (n < 0.66f) return 1;
            return baseMeta;
        }
    }
}
