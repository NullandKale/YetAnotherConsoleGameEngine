namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    internal sealed class DefaultHydrologyModel : IHydrologyModel
    {
        public int EffectiveSeaLevel(in WorldContext ctx)
        {
            int minSea = Math.Max(2, (int)(ctx.Config.WorldHeight * WorldGenSettings.Water.MinSeaLevelFraction));
            int sea = ctx.Config.WaterLevel;
            if (sea < minSea || sea >= ctx.Config.WorldHeight - 2) sea = minSea;
            return sea;
        }

        public int WaterSurfaceLevelAt(int gx, int gz, int groundHeight, in WorldContext ctx)
        {
            int sea = EffectiveSeaLevel(ctx);
            int table = LakeTableHeight(gx, gz, ctx);
            int water = sea;
            if (groundHeight < table) water = Math.Max(water, table);
            return water;
        }

        public int LakeTableHeight(int x, int z, in WorldContext ctx)
        {
            float n = GenMath.FBM2D(x, z, WorldGenSettings.Water.LakeOctaves, WorldGenSettings.Water.LakeLacunarity, WorldGenSettings.Water.LakeGain, WorldGenSettings.Water.LakeBaseFreq, ctx.Seed + 901) + WorldGenSettings.Water.LakeBias;
            int offset = (int)(n * WorldGenSettings.Water.LakeAmplitude);
            int table = EffectiveSeaLevel(ctx) + offset;
            if (table < 1) table = 1;
            if (table > ctx.Config.WorldHeight - 2) table = ctx.Config.WorldHeight - 2;
            return table;
        }
    }
}
