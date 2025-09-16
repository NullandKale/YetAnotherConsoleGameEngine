using System;

namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    internal static class Layering
    {
        public static int ChooseSurfaceBlock(Biome biome, int heightY, int sea, int snow, float slope01)
        {
            // Beaches and ocean handled by caller; here pick the land surface.
            if (heightY >= snow) return WorldGenSettings.Blocks.Snow;
            if (Math.Abs(heightY - sea) <= IslandSettings.BeachBuffer) return WorldGenSettings.Blocks.Sand;

            // Steep slopes expose rock more often
            if (slope01 > 0.80f) return WorldGenSettings.Blocks.Stone;

            switch (biome)
            {
                case Biome.Desert:
                    return WorldGenSettings.Blocks.Sand;
                case Biome.Alpine:
                    return slope01 > 0.60f ? WorldGenSettings.Blocks.Stone : WorldGenSettings.Blocks.Grass;
                case Biome.Taiga:
                case Biome.Forest:
                case Biome.Plains:
                default:
                    return WorldGenSettings.Blocks.Grass;
            }
        }

        public static int ChooseSubsurfaceBlock(Biome biome, int gy, int groundY, int sea)
        {
            // Underwater or near sea: prefer sand
            if (groundY <= sea + WorldGenSettings.Terrain.UnderwaterSandBuffer)
                return WorldGenSettings.Blocks.Sand;

            // Desert subsurface stays sandy
            if (biome == Biome.Desert)
                return WorldGenSettings.Blocks.Sand;

            // Default dirt below surface, stone deeper
            int depth = groundY - gy;
            if (depth <= IslandSettings.DirtDepth)
                return WorldGenSettings.Blocks.Dirt;
            return WorldGenSettings.Blocks.Stone;
        }
    }
}
