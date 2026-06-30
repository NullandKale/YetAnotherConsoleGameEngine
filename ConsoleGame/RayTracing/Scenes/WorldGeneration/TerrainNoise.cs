using System;

namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    internal static class TerrainNoise
    {
        // Compute island shoreline mask (0=ocean, 1=deep interior) using the same
        // warped domain and coastline jitter as the height function.
        public static float IslandMask01(float gx, float gz, WorldConfig cfg)
        {
            float x = gx, z = gz;
            Warp(ref x, ref z, cfg.WorldSeed);
            float dist = MathF.Sqrt(x * x + z * z);
            float coastJitter = (GenMath.FBM2D(x * IslandSettings.CoastJitterFreq, z * IslandSettings.CoastJitterFreq, 3, 2.0f, 0.5f, 1.0f, cfg.WorldSeed + 333) - 0.5f) * 2.0f * IslandSettings.CoastJitterAmp;
            dist = MathF.Max(0.0f, dist - coastJitter);
            float fadeW = MathF.Max(8.0f, IslandSettings.IslandRadius * IslandSettings.MaskFadeFraction);
            float edgeStart = IslandSettings.IslandRadius - fadeW;
            return 1.0f - GenMath.SmoothStep(edgeStart, IslandSettings.IslandRadius, dist);
        }

        private static void Warp(ref float x, ref float z, int seed)
        {
            // Two-stage domain warp to avoid smooth bowls or single peaks.
            float wx1 = GenMath.FBM2D(x * IslandSettings.Warp1Freq, z * IslandSettings.Warp1Freq, 4, 2.0f, 0.5f, 1.0f, seed + 101);
            float wz1 = GenMath.FBM2D((x + 137f) * IslandSettings.Warp1Freq, (z - 271f) * IslandSettings.Warp1Freq, 4, 2.0f, 0.5f, 1.0f, seed + 103);
            wx1 = (wx1 - 0.5f) * 2.0f; wz1 = (wz1 - 0.5f) * 2.0f;
            x += wx1 * IslandSettings.Warp1Amp;
            z += wz1 * IslandSettings.Warp1Amp;

            float wx2 = GenMath.FBM2D(x * IslandSettings.Warp2Freq, z * IslandSettings.Warp2Freq, 3, 2.0f, 0.5f, 1.0f, seed + 151);
            float wz2 = GenMath.FBM2D((x - 911f) * IslandSettings.Warp2Freq, (z + 643f) * IslandSettings.Warp2Freq, 3, 2.0f, 0.5f, 1.0f, seed + 157);
            wx2 = (wx2 - 0.5f) * 2.0f; wz2 = (wz2 - 0.5f) * 2.0f;
            x += wx2 * IslandSettings.Warp2Amp;
            z += wz2 * IslandSettings.Warp2Amp;
        }

        // 0..1 base height (before mapping to world Y)
        public static float Height01(float gx, float gz, WorldConfig cfg)
        {
            float x = gx, z = gz;
            Warp(ref x, ref z, cfg.WorldSeed);

            // Irregular island mask from warped distance with coastline jitter.
            float dist = MathF.Sqrt(x * x + z * z);
            float coastJitter = (GenMath.FBM2D(x * IslandSettings.CoastJitterFreq, z * IslandSettings.CoastJitterFreq, 3, 2.0f, 0.5f, 1.0f, cfg.WorldSeed + 333) - 0.5f) * 2.0f * IslandSettings.CoastJitterAmp;
            dist = MathF.Max(0.0f, dist - coastJitter);
            float fadeW = MathF.Max(8.0f, IslandSettings.IslandRadius * IslandSettings.MaskFadeFraction);
            float edgeStart = IslandSettings.IslandRadius - fadeW;
            float mask = 1.0f - GenMath.SmoothStep(edgeStart, IslandSettings.IslandRadius, dist);

            // Multi-scale terrain detail (more octaves, mixed types)
            int seed = cfg.WorldSeed;
            float nCont = GenMath.RidgedFBM2D(x * IslandSettings.ContinentFreq, z * IslandSettings.ContinentFreq, IslandSettings.ContinentOctaves, 2.0f, 0.5f, 1.0f, seed + 1001);
            float nMount = GenMath.RidgedFBM2D(x * IslandSettings.MountainFreq, z * IslandSettings.MountainFreq, IslandSettings.MountainOctaves, 2.0f, 0.5f, 1.0f, seed + 1003);
            float d1 = GenMath.FBM2D(x * IslandSettings.Detail1Freq, z * IslandSettings.Detail1Freq, IslandSettings.Detail1Octaves, 2.0f, 0.5f, 1.0f, seed + 1005);
            float d2 = GenMath.FBM2D(x * IslandSettings.Detail2Freq, z * IslandSettings.Detail2Freq, IslandSettings.Detail2Octaves, 2.0f, 0.5f, 1.0f, seed + 1006);

            // Build a mountain mask and combine with detailed plains
            float mountainMask = GenMath.Saturate((nCont * 1.15f + nMount * 1.10f) - 0.90f);
            float plains = d1 * 0.65f + d2 * 0.35f;               // 0..1
            float mountains = MathF.Pow(nMount, 1.35f);           // emphasize ridges
            float baseTerrain = GenMath.Lerp(plains, mountains, mountainMask);

            // Final height field before mask; flatten the very center to avoid towering peaks
            float h01 = baseTerrain;
            float centerDist = MathF.Sqrt(x * x + z * z);
            float centerFlatten = GenMath.Saturate(centerDist / (IslandSettings.IslandRadius * 0.55f));
            // centerFlatten=0 at center -> 0.55x height; ->1 near mid-island -> 1.0x height
            h01 *= GenMath.Lerp(0.55f, 1.00f, centerFlatten);

            // Optional terraces
            if (IslandSettings.TerraceStep > 0.0f)
            {
                float step = IslandSettings.TerraceStep / MathF.Max(1.0f, cfg.WorldHeight);
                float terrJitter = (GenMath.FBM2D(x * 0.01f, z * 0.01f, 2, 2.0f, 0.5f, 1.0f, seed + 707) - 0.5f) * 2.0f * IslandSettings.TerraceJitter * step;
                float q = MathF.Floor((h01 + terrJitter) / step) * step;
                h01 = GenMath.Saturate(q);
            }

            // Apply shoreline mask only as a clamp so interior stays varied.
            h01 = MathF.Min(h01, mask);
            return GenMath.Saturate(h01);
        }

        public static int HeightY(int gx, int gz, WorldConfig cfg)
        {
            int sea = cfg.WaterLevel;
            int oceanFloor = Math.Max(1, sea - IslandSettings.SeaFloorDepth);
            float h01 = Height01(gx, gz, cfg);
            float maxRise = cfg.WorldHeight * IslandSettings.MaxRiseFraction;
            int h = (int)MathF.Round(sea + h01 * maxRise);

            // Outside island: keep seabed slightly undulated
            float dx = gx, dz = gz;
            float radial = GenMath.Saturate(1.0f - MathF.Sqrt(dx * dx + dz * dz) / IslandSettings.IslandRadius);
            if (radial <= 0.0005f)
            {
                float bed = GenMath.FBM2D(gx * 0.0015f, gz * 0.0015f, 3, 2.0f, 0.5f, 1.0f, cfg.WorldSeed + 1303);
                int undulate = (int)MathF.Round((bed - 0.5f) * 6.0f);
                h = oceanFloor + undulate;
            }
            else
            {
                h = Math.Max(h, oceanFloor);
            }

            if (h < 0) h = 0;
            if (h >= cfg.WorldHeight) h = cfg.WorldHeight - 1;
            return h;
        }

        // Local water surface height for inland seas/lakes (>= sea level when active).
        public static int LocalWaterY(int gx, int gz, WorldConfig cfg, int groundY, float slope01)
        {
            int sea = cfg.WaterLevel;
            float mask = IslandMask01(gx, gz, cfg);
            if (mask < IslandSettings.LakeMaskThreshold)
                return sea;

            // Lake candidate level from low-frequency noise
            int seed = cfg.WorldSeed;
            float n1 = GenMath.FBM2D(gx * IslandSettings.LakeFreq1, gz * IslandSettings.LakeFreq1, 5, 2.0f, 0.5f, 1.0f, seed + 8101);
            float n2 = GenMath.FBM2D(gx * IslandSettings.LakeFreq2, gz * IslandSettings.LakeFreq2, 4, 2.0f, 0.5f, 1.0f, seed + 8107);
            float lakeField = 0.65f * n1 + 0.35f * n2; // 0..1
            // Encourage lakes in lower elevations by biasing with (sea - groundY) clamp
            float lowlandBias = Clamp01(1.0f - (groundY - sea) / Math.Max(1.0f, (float)(cfg.SnowLevel - sea)));
            float candidate = sea + IslandSettings.LakeBaseAboveSea + (lakeField * 0.75f + lowlandBias * 0.25f) * IslandSettings.LakeRiseMax;

            // Only accept if the ground is sufficiently below and slope is gentle
            if (slope01 <= IslandSettings.LakeSlopeMax && groundY + IslandSettings.LakeMinDepth < candidate)
            {
                int wy = (int)MathF.Floor(candidate);
                if (wy > sea) return wy;
            }
            return sea;
        }

        // Local slope using neighboring heights sampled on the fly
        public static float Slope01At(int gx, int gz, WorldConfig cfg)
        {
            int hL = HeightY(gx - 1, gz, cfg);
            int hR = HeightY(gx + 1, gz, cfg);
            int hD = HeightY(gx, gz - 1, cfg);
            int hU = HeightY(gx, gz + 1, cfg);
            float dx = (hR - hL) * 0.5f;
            float dz = (hU - hD) * 0.5f;
            float g = MathF.Sqrt(dx * dx + dz * dz);
            return GenMath.Saturate(g / WorldGenSettings.Normalization.SlopeNormalize);
        }

        private static float Clamp01(float x)
        {
            if (x < 0.0f) return 0.0f;
            if (x > 1.0f) return 1.0f;
            return x;
        }
    }
}
