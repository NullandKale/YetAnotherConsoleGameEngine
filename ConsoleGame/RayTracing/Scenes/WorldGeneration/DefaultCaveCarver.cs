namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    internal sealed class DefaultCaveCarver : ICaveCarver
    {
        public bool ShouldCarve(int gx, int gy, int gz, int groundHeight, in WorldContext ctx)
        {
            if (gy <= 0) return false;
            if (gy > groundHeight - WorldGenSettings.Caves.SurfaceSafetyShell) return false;
            float depth = groundHeight - gy;
            float th = WorldGenSettings.Caves.ThresholdBase + WorldGenSettings.Caves.ThresholdDepthScale * GenMath.SmoothStep(0.0f, WorldGenSettings.Caves.DepthSmoothMax, depth);
            float n = GenMath.FBM3D(gx * WorldGenSettings.Caves.FBMInputScaleX, gy * WorldGenSettings.Caves.FBMInputScaleY, gz * WorldGenSettings.Caves.FBMInputScaleZ, WorldGenSettings.Caves.FBMOctaves, WorldGenSettings.Caves.FBMLacunarity, WorldGenSettings.Caves.FBMGain, ctx.Seed + WorldGenSettings.Caves.FBMSeedOffset);
            if (gy < WorldGenSettings.Caves.BoostBelowY) n *= WorldGenSettings.Caves.CarveDepthFactor;
            return n > th;
        }
    }
}
