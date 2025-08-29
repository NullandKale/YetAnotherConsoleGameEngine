namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    internal sealed class DefaultHeightModel : IHeightModel
    {
        public int ComputeHeight(int x, int z, in WorldContext ctx)
        {
            float wx = x;
            float wz = z;
            float warpFreq = WorldGenSettings.Heightmap.WarpFrequency;
            float warpAmp = WorldGenSettings.Heightmap.WarpAmplitude;
            float wx1 = wx + warpAmp * GenMath.GradientNoise2D(wx * warpFreq, wz * warpFreq, ctx.Seed + 101);
            float wz1 = wz + warpAmp * GenMath.GradientNoise2D(wx * warpFreq * 1.07f, wz * warpFreq * 0.93f, ctx.Seed - 77);
            float cont = 0.5f * GenMath.GradientNoise2D(wx1 * WorldGenSettings.Heightmap.ContinentFreq, wz1 * WorldGenSettings.Heightmap.ContinentFreq, ctx.Seed + 1) + 0.5f;
            cont = MathF.Pow(GenMath.Saturate(cont), WorldGenSettings.Heightmap.ContinentExponent);
            float ridged = GenMath.RidgedFBM2D(wx1, wz1, WorldGenSettings.Heightmap.RidgedOctaves, WorldGenSettings.Heightmap.RidgedLacunarity, WorldGenSettings.Heightmap.RidgedGain, WorldGenSettings.Heightmap.RidgedBaseFreq, ctx.Seed + 200);
            ridged = GenMath.Saturate(ridged);
            float detail = GenMath.FBM2D(wx1, wz1, WorldGenSettings.Heightmap.DetailOctaves, 2.0f, 0.5f, WorldGenSettings.Heightmap.DetailBaseFreq, ctx.Seed + 300) * 0.5f + 0.5f;
            float riverMask = DefaultHydrologyModel.RiverMask01ForHeightStatic(x, z, ctx);
            float h01 = 0.42f * cont + 0.40f * ridged + 0.18f * detail;
            h01 = MathF.Max(0.0f, h01 - WorldGenSettings.Heightmap.RiverCarveStrength * riverMask);
            h01 += WorldGenSettings.Heightmap.ExtraNoiseAmp * (GenMath.FBM2D(wx1, wz1, 2, 2.0f, 0.5f, WorldGenSettings.Heightmap.ExtraNoiseBaseFreq, ctx.Seed + 444) - 0.5f);
            float oceanBias = WorldGenSettings.Heightmap.OceanBiasStrength * (1.0f - GenMath.SmoothStep(0.0f, WorldGenSettings.Heightmap.OceanBiasMaxContinent, cont));
            h01 = MathF.Max(0.0f, h01 - oceanBias);
            h01 = GenMath.Saturate(h01);
            h01 = GenMath.SmoothStep(0.0f, 1.0f, h01);
            int baseOffset = (int)(ctx.Config.WorldHeight * WorldGenSettings.Terrain.BaseOffsetFraction);
            int maxRelief = (int)(ctx.Config.WorldHeight * WorldGenSettings.Terrain.ReliefFraction);
            int h = baseOffset + (int)(h01 * maxRelief);
            if (h < 1) h = 1;
            if (h > ctx.Config.WorldHeight - 2) h = ctx.Config.WorldHeight - 2;
            return h;
        }
    }
}
