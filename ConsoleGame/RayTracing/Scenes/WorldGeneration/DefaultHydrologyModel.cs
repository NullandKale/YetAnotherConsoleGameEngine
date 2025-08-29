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

        public float RiverMask01(int x, int z, in WorldContext ctx)
        {
            float freq = WorldGenSettings.Water.RiverFreq;
            float wx = x + WorldGenSettings.Water.RiverWarp1 * GenMath.GradientNoise2D(x * WorldGenSettings.Water.RiverInputFreqX1, z * WorldGenSettings.Water.RiverInputFreqZ1, ctx.Seed + 777);
            float wz = z + WorldGenSettings.Water.RiverWarp2 * GenMath.GradientNoise2D(x * WorldGenSettings.Water.RiverInputFreqX2, z * WorldGenSettings.Water.RiverInputFreqZ2, ctx.Seed - 333);
            float v = MathF.Abs(GenMath.GradientNoise2D(wx * freq, wz * freq, ctx.Seed + 555));
            float m = 1.0f - GenMath.SmoothStep(WorldGenSettings.Water.RiverSmoothEdgeMin, WorldGenSettings.Water.RiverSmoothEdgeMax, v);
            m = MathF.Pow(GenMath.Saturate(m), WorldGenSettings.Water.RiverBandPower);
            return m;
        }

        public float RiverMask01ForHeight(int x, int z, in WorldContext ctx) { return RiverMask01ForHeightStatic(x, z, ctx); }

        internal static float RiverMask01ForHeightStatic(int x, int z, in WorldContext ctx)
        {
            float freq = WorldGenSettings.Water.RiverFreq;
            float wx = x + WorldGenSettings.Water.RiverWarp1 * GenMath.GradientNoise2D(x * WorldGenSettings.Water.RiverInputFreqX1, z * WorldGenSettings.Water.RiverInputFreqZ1, ctx.Seed + 777);
            float wz = z + WorldGenSettings.Water.RiverWarp2 * GenMath.GradientNoise2D(x * WorldGenSettings.Water.RiverInputFreqX2, z * WorldGenSettings.Water.RiverInputFreqZ2, ctx.Seed - 333);
            float v = MathF.Abs(GenMath.GradientNoise2D(wx * freq, wz * freq, ctx.Seed + 555));
            float m = 1.0f - GenMath.SmoothStep(WorldGenSettings.Heightmap.RiverEdgeMinForHeight, WorldGenSettings.Heightmap.RiverEdgeMaxForHeight, v);
            m = MathF.Pow(GenMath.Saturate(m), WorldGenSettings.Heightmap.RiverBandPowerForHeight);
            return m;
        }

        public float RiverCenterSignal(int x, int z, in WorldContext ctx)
        {
            float freq = WorldGenSettings.Water.RiverFreq;
            float wx = x + WorldGenSettings.Water.RiverWarp1 * GenMath.GradientNoise2D(x * WorldGenSettings.Water.RiverInputFreqX1, z * WorldGenSettings.Water.RiverInputFreqZ1, ctx.Seed + 777);
            float wz = z + WorldGenSettings.Water.RiverWarp2 * GenMath.GradientNoise2D(x * WorldGenSettings.Water.RiverInputFreqX2, z * WorldGenSettings.Water.RiverInputFreqZ2, ctx.Seed - 333);
            return GenMath.GradientNoise2D(wx * freq, wz * freq, ctx.Seed + 555);
        }

        public bool InRiverCoreBand(int x, int z, int targetWidthBlocks, in WorldContext ctx)
        {
            if (targetWidthBlocks <= 1) return MathF.Abs(RiverCenterSignal(x, z, ctx)) < 1e-3f;
            float s = RiverCenterSignal(x, z, ctx);
            float sx1 = RiverCenterSignal(x + 1, z, ctx);
            float sx0 = RiverCenterSignal(x - 1, z, ctx);
            float sz1 = RiverCenterSignal(x, z + 1, ctx);
            float sz0 = RiverCenterSignal(x, z - 1, ctx);
            float gx = 0.5f * (sx1 - sx0);
            float gz = 0.5f * (sz1 - sz0);
            float g = MathF.Max(1e-4f, MathF.Sqrt(gx * gx + gz * gz));
            float halfW = 0.5f * targetWidthBlocks;
            return MathF.Abs(s) <= g * halfW;
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

        public int RiverBedY(int gx, int gz, int groundHeight, in WorldContext ctx)
        {
            int bedDepth = WorldGenSettings.Water.RiverBedDepthBase + (GenMath.FastHash(gx, 17, gz, ctx.Seed) & WorldGenSettings.Water.RiverBedDepthRandMask);
            return Math.Max(1, groundHeight - bedDepth);
        }

        public int RiverSurfaceY(int gx, int gz, int groundHeight, int localWater, int riverBedY, in WorldContext ctx)
        {
            return Math.Min(groundHeight, Math.Max(localWater, riverBedY + WorldGenSettings.Water.RiverWaterDepth));
        }
    }
}
