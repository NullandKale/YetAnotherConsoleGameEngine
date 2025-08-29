namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    internal sealed class DefaultBiomeLayering : IBiomeLayering
    {
        public (int, int) ChooseSurfaceBlock(int groundHeight, int localWater, float moisture, in WorldContext ctx)
        {
            if (groundHeight > ctx.Config.SnowLevel) return (WorldGenSettings.Blocks.Snow, 0);
            if (groundHeight <= localWater + WorldGenSettings.Terrain.BeachBuffer) return (WorldGenSettings.Blocks.Sand, 0);
            if (moisture < WorldGenSettings.Biomes.DesertMoistureThreshold) return (WorldGenSettings.Blocks.Sand, 0);
            return (WorldGenSettings.Blocks.Grass, 0);
        }

        public (int, int) ChooseSubsurfaceBlock(int gy, int groundHeight, float moisture, in WorldContext ctx)
        {
            if (groundHeight > ctx.Config.SnowLevel) return (WorldGenSettings.Blocks.Stone, 0);
            if (moisture < WorldGenSettings.Biomes.DesertMoistureThreshold)
            {
                if (gy >= groundHeight - WorldGenSettings.Biomes.DesertSandDepth) return (WorldGenSettings.Blocks.Sand, 0);
                return (WorldGenSettings.Blocks.Stone, 0);
            }
            else
            {
                if (gy >= groundHeight - WorldGenSettings.Terrain.DirtDepth) return (WorldGenSettings.Blocks.Dirt, 0);
                return (WorldGenSettings.Blocks.Stone, 0);
            }
        }
    }
}
