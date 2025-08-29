namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    internal sealed class DefaultOreDistributor : IOreDistributor
    {
        public bool TryOreAt(int gx, int gy, int gz, out (int, int) ore, in WorldContext ctx)
        {
            ore = default;
            if (gy <= 0) return false;
            if ((GenMath.FastHash(gx, gy, gz, ctx.Seed) & WorldGenSettings.Ore.ChanceMask) != 0) return false;
            int oreType = GenMath.FastHash(gx + WorldGenSettings.Ore.TypeHashOffsetX, gy + WorldGenSettings.Ore.TypeHashOffsetY, gz + WorldGenSettings.Ore.TypeHashOffsetZ, ctx.Seed) % WorldGenSettings.Ore.TypeModulo;
            ore = (WorldGenSettings.Blocks.Ore, oreType);
            return true;
        }
    }
}
