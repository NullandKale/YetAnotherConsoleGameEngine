using System;

namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    internal static class BiomeMap
    {
        public static Biome Evaluate(int gx, int gz, int heightY, int sea, int snow, float slope01, WorldConfig cfg)
        {
            // Hard overrides first
            if (heightY <= sea - 1)
                return Biome.Ocean;
            if (Math.Abs(heightY - sea) <= IslandSettings.BeachBuffer)
                return Biome.Beach;
            // Climate field: combine moisture and ridged dryness at higher frequency
            int seed = cfg.WorldSeed;
            float m1 = GenMath.FBM2D(gx * 0.0025f, gz * 0.0025f, 5, 2.0f, 0.5f, 1.0f, seed + 5002);
            float d1 = GenMath.RidgedFBM2D(gx * 0.0020f, gz * 0.0020f, 4, 2.0f, 0.5f, 1.0f, seed + 5003);
            // Dryness increases with ridged noise and lower moisture
            float dryness = 0.55f * d1 + 0.45f * (1.0f - m1);
            // Threshold to split Desert vs Forest; adjust to taste
            return (dryness > 0.52f) ? Biome.Desert : Biome.Forest;
        }
    }
}
