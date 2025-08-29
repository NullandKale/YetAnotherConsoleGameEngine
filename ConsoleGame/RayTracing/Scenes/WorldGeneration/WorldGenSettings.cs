namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    ////////////////////////////////////////////////////////////////////////////////////////////
    // WORLD GENERATION SETTINGS (tunable at runtime)
    ////////////////////////////////////////////////////////////////////////////////////////////
    public static class WorldGenSettings
    {
        public static class Blocks
        {
            public const int Air = 0;
            public const int Stone = 1;
            public const int Dirt = 2;
            public const int Grass = 3;
            public const int Water = 4;
            public const int Sand = 5;
            public const int Wood = 6;
            public const int Leaves = 7;
            public const int Snow = 8;
            public const int Ore = 9;
            public const int TallGrass = 10;
            public const int Flower = 11;
        }

        public static class Biomes
        {
            public static float DesertMoistureThreshold = 0.30f;
            public static int DesertSandDepth = 5;
            public static float TreeMinMoisture = 0.35f;
            public static float HighAltitudePineBiasDelta = 10.0f;
        }

        public static class Terrain
        {
            public static int DirtDepth = 3;
            public static int UnderwaterSandBuffer = 1;
            public static int BeachBuffer = 2;
            public static float BaseOffsetFraction = 0.20f;
            public static float ReliefFraction = 0.70f;
        }

        public static class Vegetation
        {
            public static int BorderMargin = 2;
            public static float MaxSlopeForTree = 0.65f;
            public static int DenseForestCell = 8;
            public static int MoistForestCell = 10;
            public static int SparseForestCell = 14;
            public static int BroadleafBaseHeight = 5;
            public static int BroadleafRandomHeight = 3;
            public static int BroadleafRadius = 2;
            public static int BroadleafLayers = 3;
            public static int ConiferBaseHeight = 7;
            public static int ConiferRandomHeight = 4;
            public static int ConiferLayers = 4;
            public static int ConiferBaseRadius = 3;
        }

        public static class Plants
        {
            public static float LushFlowerChance = 0.00010f;
            public static float LushGrassChance = 0.00040f;
            public static float MoistFlowerChance = 0.00004f;
            public static float MoistGrassChance = 0.00030f;
            public static float SemiAridFlowerChance = 0.00001f;
            public static float SemiAridGrassChance = 0.00015f;
        }

        public static class Caves
        {
            public static float ThresholdBase = 0.60f;
            public static float ThresholdDepthScale = 0.08f;
            public static float DepthSmoothMax = 16.0f;
            public static int BoostBelowY = 24;
            public static float CarveDepthFactor = 0.80f;
            public static float FBMInputScaleX = 0.05f;
            public static float FBMInputScaleY = 0.07f;
            public static float FBMInputScaleZ = 0.05f;
            public static int FBMOctaves = 4;
            public static float FBMLacunarity = 2.0f;
            public static float FBMGain = 0.5f;
            public static int FBMSeedOffset = 900;
            public static int SurfaceSafetyShell = 2;
        }

        public static class Heightmap
        {
            public static float WarpFrequency = 0.0016f;
            public static float WarpAmplitude = 24.0f;
            public static float ContinentFreq = 0.0010f;
            public static float ContinentExponent = 0.95f;
            public static int RidgedOctaves = 5;
            public static float RidgedLacunarity = 2.0f;
            public static float RidgedGain = 0.5f;
            public static float RidgedBaseFreq = 0.0022f;
            public static int DetailOctaves = 4;
            public static float DetailBaseFreq = 0.009f;
            public static float ExtraNoiseAmp = 0.06f;
            public static float ExtraNoiseBaseFreq = 0.012f;
            public static float RiverCarveStrength = 0.05f;
            public static float OceanBiasStrength = 0.12f;
            public static float OceanBiasMaxContinent = 0.50f;
            public static float RiverEdgeMinForHeight = 0.003f;
            public static float RiverEdgeMaxForHeight = 0.010f;
            public static float RiverBandPowerForHeight = 4.0f;
        }

        public static class Water
        {
            public static float RiverFreq = 0.00085f;
            public static float RiverWarp1 = 23.0f;
            public static float RiverWarp2 = 19.0f;
            public static float RiverInputFreqX1 = 0.0011f;
            public static float RiverInputFreqZ1 = 0.0011f;
            public static float RiverInputFreqX2 = 0.0009f;
            public static float RiverInputFreqZ2 = 0.0013f;
            public static float RiverSmoothEdgeMin = 0.03f;
            public static float RiverSmoothEdgeMax = 0.08f;
            public static float RiverActiveThreshold = 0.92f;
            public static float RiverBandPower = 6.0f;
            public static int RiverTargetWidthBlocks = 8;
            public static int RiverBedDepthBase = 3;
            public static int RiverBedDepthRandMask = 1;
            public static int RiverWaterDepth = 2;
            public static int LakeOctaves = 3;
            public static float LakeBaseFreq = 0.0005f;
            public static float LakeBias = -0.35f;
            public static float LakeAmplitude = 18.0f;
            public static float LakeLacunarity = 2.0f;
            public static float LakeGain = 0.5f;
            public static float MinSeaLevelFraction = 0.30f;
        }

        public static class Moisture
        {
            public static float CoordScale = 0.5f;
            public static int Octaves = 4;
            public static float Lacunarity = 2.0f;
            public static float Gain = 0.5f;
            public static float BaseFreq = 0.0012f;
        }

        public static class BlueNoise
        {
            public static int JitterSeedOffsetX = 12345;
            public static int JitterSeedOffsetZ = 54321;
        }

        public static class Ore
        {
            public static int ChanceMask = 0x3F;
            public static int TypeModulo = 3;
            public static int TypeHashOffsetX = 11;
            public static int TypeHashOffsetY = 7;
            public static int TypeHashOffsetZ = 19;
        }

        public static class Normalization
        {
            public const float InvSqrt2 = 0.70710678118f;
            public const float Perlin2D = 1.41421356237f;
            public const float Perlin3D = 1.15470053838f;
            public static float SlopeNormalize = 6.0f;
            public static float FBM3DBaseFrequency = 0.035f;
        }

        public static class Hashing
        {
            public const uint FnvOffset = 2166136261u;
            public const uint FnvPrime = 16777619u;
        }
    }
}
