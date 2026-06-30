namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    internal static class IslandSettings
    {
        // Island shape
        public static float IslandRadius = 10000.0f;     // ~10k x 10k island
        public static float MaskFadeFraction = 0.18f;    // fraction of radius over which shore fades to sea
        public static float MaxRiseFraction = 0.45f;     // fraction of world height above sea (lowered)
        public static int SeaFloorDepth = 12;            // blocks below sea level for ocean floor baseline
        public static int BeachBuffer = 2;               // height window around sea level considered beach

        // Strata
        public static int DirtDepth = 3;                 // blocks of dirt under surface

        // Shoreline jitter (irregular coasts)
        public static float CoastJitterFreq = 0.00022f;
        public static float CoastJitterAmp = 600.0f;     // subtract from distance to push coasts in/out

        // Domain warping (two stages)
        public static float Warp1Freq = 0.00025f;
        public static float Warp1Amp = 350.0f;
        public static float Warp2Freq = 0.0012f;
        public static float Warp2Amp = 90.0f;

        // Continental and mountain structure
        public static float ContinentFreq = 0.00045f;
        public static int ContinentOctaves = 6;
        public static float MountainFreq = 0.0011f;
        public static int MountainOctaves = 5;

        // Detail layers
        public static float Detail1Freq = 0.0025f;
        public static int Detail1Octaves = 6;
        public static float Detail2Freq = 0.0060f;
        public static int Detail2Octaves = 5;

        // Inland lakes/seas
        public static float LakeFreq1 = 0.0008f;
        public static float LakeFreq2 = 0.0016f;
        public static float LakeRiseMax = 60.0f;     // max height above sea for inland water (boosted)
        public static float LakeBaseAboveSea = 8.0f;  // baseline above sea level for candidate lakes
        public static float LakeSlopeMax = 0.60f;     // allow lakes on broader flats/valleys
        public static float LakeMaskThreshold = 0.05f;// must be inside island (more permissive)
        public static float LakeMinDepth = 1.0f;      // minimum depth to accept water

        // Terrace effect (small, optional)
        public static float TerraceStep = 0.0f;          // 0 disables terraces; try 1.5f for steps
        public static float TerraceJitter = 0.15f;       // jitter strength for step edges

        // Rivers
        public static float RiverAccumThreshold = 50.0f; // cells needed before carving begins
        public static float RiverMaxCarve = 3.5f;        // max blocks carved below terrain
        public static float RiverWaterDepth = 2.0f;      // water thickness above carved bed
        public static float RiverBankSand = 1.5f;        // sand band above river surface
    }
}
