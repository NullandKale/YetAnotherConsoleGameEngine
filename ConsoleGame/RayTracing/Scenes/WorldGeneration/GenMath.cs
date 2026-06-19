using ConsoleGame.Renderer;

namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    ////////////////////////////////////////////////////////////////////////////////////////////
    // DEFAULT IMPLEMENTATIONS (preserve existing look/feel)
    ////////////////////////////////////////////////////////////////////////////////////////////
    internal static class GenMath
    {
        public static float FBM2D(float x, float z, int octaves, float lacunarity, float gain, float baseFreq, int seed)
        {
            float sum = 0.0f, amp = 1.0f, freq = baseFreq;
            for (int i = 0; i < octaves; i++)
            {
                float n = GradientNoise2D(x * freq, z * freq, seed + i * 131);
                sum += n * amp;
                freq *= lacunarity;
                amp *= gain;
            }
            return 0.5f * sum + 0.5f;
        }

        public static float RidgedFBM2D(float x, float z, int octaves, float lacunarity, float gain, float baseFreq, int seed)
        {
            float sum = 0.0f, amp = 0.5f, freq = baseFreq, weight = 1.0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = GradientNoise2D(x * freq, z * freq, seed + i * 733);
                n = 1.0f - MathF.Abs(n);
                n *= n;
                n *= weight;
                weight = n * gain;
                if (weight > 1.0f) weight = 1.0f;
                sum += n * amp;
                freq *= lacunarity;
                amp *= 0.5f;
            }
            return sum;
        }

        public static float FBM3D(float x, float y, float z, int octaves, float lacunarity, float gain, int seed)
        {
            float sum = 0.0f, amp = 1.0f, freq = WorldGenSettings.Normalization.FBM3DBaseFrequency;
            for (int i = 0; i < octaves; i++)
            {
                float n = GradientNoise3D(x * freq, y * freq, z * freq, seed + i * 197);
                sum += n * amp;
                freq *= lacunarity;
                amp *= gain;
            }
            return 0.5f * sum + 0.5f;
        }

        public static float GradientNoise2D(float x, float z, int seed)
        {
            int x0 = FastFloor(x);
            int z0 = FastFloor(z);
            int x1 = x0 + 1;
            int z1 = z0 + 1;
            float tx = x - x0;
            float tz = z - z0;
            float u = Fade(tx);
            float v = Fade(tz);
            float n00 = Dot(Grad2(x0, z0, seed), tx, tz);
            float n10 = Dot(Grad2(x1, z0, seed), tx - 1.0f, tz);
            float n01 = Dot(Grad2(x0, z1, seed), tx, tz - 1.0f);
            float n11 = Dot(Grad2(x1, z1, seed), tx - 1.0f, tz - 1.0f);
            float ix0 = Lerp(n00, n10, u);
            float ix1 = Lerp(n01, n11, u);
            float val = Lerp(ix0, ix1, v);
            return Clamp(val * WorldGenSettings.Normalization.Perlin2D, -1.0f, 1.0f);
        }

        public static float GradientNoise3D(float x, float y, float z, int seed)
        {
            int x0 = FastFloor(x), y0 = FastFloor(y), z0 = FastFloor(z);
            int x1 = x0 + 1, y1 = y0 + 1, z1 = z0 + 1;
            float tx = x - x0, ty = y - y0, tz = z - z0;
            float u = Fade(tx), v = Fade(ty), w = Fade(tz);
            float n000 = Dot(Grad3(x0, y0, z0, seed), tx, ty, tz);
            float n100 = Dot(Grad3(x1, y0, z0, seed), tx - 1.0f, ty, tz);
            float n010 = Dot(Grad3(x0, y1, z0, seed), tx, ty - 1.0f, tz);
            float n110 = Dot(Grad3(x1, y1, z0, seed), tx - 1.0f, ty - 1.0f, tz);
            float n001 = Dot(Grad3(x0, y0, z1, seed), tx, ty, tz - 1.0f);
            float n101 = Dot(Grad3(x1, y0, z1, seed), tx - 1.0f, ty, tz - 1.0f);
            float n011 = Dot(Grad3(x0, y1, z1, seed), tx, ty - 1.0f, tz - 1.0f);
            float n111 = Dot(Grad3(x1, y1, z1, seed), tx - 1.0f, ty - 1.0f, tz - 1.0f);
            float ix00 = Lerp(n000, n100, u);
            float ix10 = Lerp(n010, n110, u);
            float ix01 = Lerp(n001, n101, u);
            float ix11 = Lerp(n011, n111, u);
            float iy0 = Lerp(ix00, ix10, v);
            float iy1 = Lerp(ix01, ix11, v);
            float val = Lerp(iy0, iy1, w);
            return Clamp(val * WorldGenSettings.Normalization.Perlin3D, -1.0f, 1.0f);
        }

        public static float LocalSlope01(int[,] hCache, int lx, int lz, int size)
        {
            int x0 = Math.Max(0, lx - 1);
            int x1 = Math.Min(size - 1, lx + 1);
            int z0 = Math.Max(0, lz - 1);
            int z1 = Math.Min(size - 1, lz + 1);
            float dx = (hCache[x1, lz] - hCache[x0, lz]) * 0.5f;
            float dz = (hCache[lx, z1] - hCache[lx, z0]) * 0.5f;
            float g = MathF.Sqrt(dx * dx + dz * dz);
            return Saturate(g / WorldGenSettings.Normalization.SlopeNormalize);
        }

        public static int FastFloor(float t) => t >= 0.0f ? (int)t : (int)t - 1;

        public static float Fade(float t) { return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f); }

        public static float[] Grad2(int ix, int iz, int seed)
        {
            int h = FastHash(ix, 0, iz, seed);
            switch (h >> 13 & 7)
            {
                case 0: return new float[] { 1, 0 };
                case 1: return new float[] { -1, 0 };
                case 2: return new float[] { 0, 1 };
                case 3: return new float[] { 0, -1 };
                case 4: return new float[] { WorldGenSettings.Normalization.InvSqrt2, WorldGenSettings.Normalization.InvSqrt2 };
                case 5: return new float[] { -WorldGenSettings.Normalization.InvSqrt2, WorldGenSettings.Normalization.InvSqrt2 };
                case 6: return new float[] { WorldGenSettings.Normalization.InvSqrt2, -WorldGenSettings.Normalization.InvSqrt2 };
                default: return new float[] { -WorldGenSettings.Normalization.InvSqrt2, -WorldGenSettings.Normalization.InvSqrt2 };
            }
        }

        public static float[] Grad3(int ix, int iy, int iz, int seed)
        {
            int h = FastHash(ix, iy, iz, seed);
            switch (h >> 11 & 15)
            {
                case 0: return new float[] { 1, 1, 0 };
                case 1: return new float[] { -1, 1, 0 };
                case 2: return new float[] { 1, -1, 0 };
                case 3: return new float[] { -1, -1, 0 };
                case 4: return new float[] { 1, 0, 1 };
                case 5: return new float[] { -1, 0, 1 };
                case 6: return new float[] { 1, 0, -1 };
                case 7: return new float[] { -1, 0, -1 };
                case 8: return new float[] { 0, 1, 1 };
                case 9: return new float[] { 0, -1, 1 };
                case 10: return new float[] { 0, 1, -1 };
                case 11: return new float[] { 0, -1, -1 };
                case 12: return new float[] { 1, 1, 1 };
                case 13: return new float[] { -1, 1, 1 };
                case 14: return new float[] { 1, -1, 1 };
                default: return new float[] { 1, 1, -1 };
            }
        }

        public static float Dot(float[] g, float x, float z) => g[0] * x + g[1] * z;
        public static float Dot(float[] g, float x, float y, float z) => g[0] * x + g[1] * y + g[2] * z;
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        public static float Clamp(float x, float a, float b) { if (x < a) return a; if (x > b) return b; return x; }
        public static float Saturate(float x) { if (x < 0.0f) return 0.0f; if (x > 1.0f) return 1.0f; return x; }

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Saturate((x - edge0) / (edge1 - edge0));
            return t * t * (3.0f - 2.0f * t);
        }

        public static int FastHash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = WorldGenSettings.Hashing.FnvOffset ^ (uint)seed;
                h ^= (uint)x; h *= WorldGenSettings.Hashing.FnvPrime;
                h ^= (uint)y; h *= WorldGenSettings.Hashing.FnvPrime;
                h ^= (uint)z; h *= WorldGenSettings.Hashing.FnvPrime;
                return (int)h;
            }
        }

        public static float Hash01(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)FastHash(x, y, z, seed);
                return h * (1.0f / 4294967296.0f);
            }
        }
    }
    ////////////////////////////////////////////////////////////////////////////////////////////
    // NOISE-DERIVED FRAMEBUFFER MAPS FOR WORLD GENERATION
    // - Deterministic per (DefaultSeed, mapIndex, sizeX, sizeY)
    // - Values in [0,1] unless documented otherwise
    // - No Parallel.For (single-threaded loops)
    // - Island height guarantees edges below sea level via radial mask
    // - Includes conversion to Chexel with good-looking palettes per map
    ////////////////////////////////////////////////////////////////////////////////////////////
    internal static class NoiseFramebuffers
    {
        ////////////////////////////////////////////////////////////////////////////////////////
        // TUNABLE CONSTANTS
        ////////////////////////////////////////////////////////////////////////////////////////
        public static int DefaultSeed = 1337;

        // Global FBM controls
        private const int FBM_OCTAVES = 6;
        private const float FBM_LAC = 2.0f;
        private const float FBM_GAIN = 0.5f;

        // Height/island shaping
        private const float HEIGHT_BASEFREQ_PER_MIN_DIM = 1.6f;
        private const float HEIGHT_RIDGE_MULT = 3.0f;
        private const int HEIGHT_RIDGE_OCTAVES = 4;
        private const float HEIGHT_RIDGE_GAIN = 0.5f;
        private const float HEIGHT_RIDGE_WEIGHT = 0.55f;
        private const float HEIGHT_BASE_WEIGHT = 0.85f;
        private const float HEIGHT_EXPONENT = 1.15f; // >1 sharpens peaks, <1 flattens
        private const float ISLAND_RADIUS_FRACTION = 0.92f; // 0..1 relative to half-min dimension
        private const float ISLAND_EDGE_POWER = 2.6f; // higher => sharper falloff to ocean
        public const float SEA_LEVEL = 0.45f; // exposed for gameplay queries

        // Domain warp (for organic features)
        private const float WARP_FREQ_PER_MIN_DIM = 0.8f;
        private const float WARP_STRENGTH = 12.5f;

        // Weather (large-scale)
        private const float WEATHER_BASEFREQ_PER_MIN_DIM = 0.35f;
        private const int WEATHER_OCTAVES = 5;
        private const float WEATHER_GAIN = 0.55f;
        private const float WEATHER_WARP_STRENGTH = 18.0f;

        // Moisture / raininess
        private const float MOISTURE_NOISE_FREQ_PER_MIN_DIM = 0.9f;
        private const float COAST_MOISTURE_WEIGHT = 0.55f;
        private const float ALTITUDE_DRYING = 0.65f; // reduces moisture above sea level
        private const float WEATHER_TO_RAININESS = 0.6f;
        private const float MOISTURE_TO_RAININESS = 0.7f;

        // Temperature
        private const float TEMP_NOISE_FREQ_PER_MIN_DIM = 0.7f;
        private const float TEMP_NOISE_AMPLITUDE = 0.18f;
        private const float TEMP_LAPSE_WITH_ALT = 0.55f; // cooler with height above sea level
        private const float TEMP_LAT_GRADIENT_STRENGTH = 0.6f; // vertical gradient top->bottom

        // Rivers
        private const float RIVER_NOISE_FREQ_PER_MIN_DIM = 2.2f;
        private const int RIVER_OCTAVES = 4;
        private const float RIVER_CENTER = 0.5f;
        private const float RIVER_WIDTH = 0.045f; // smaller => thinner rivers
        private const float RIVER_BANK_SOFTNESS = 0.06f; // smoothstep width
        private const float RIVER_ON_LAND_ONLY_SCALE = 4.0f; // how far above sea level rivers can appear

        ////////////////////////////////////////////////////////////////////////////////////////
        // PUBLIC: MAP DISPATCH (returns colored Chexel buffers for easy cycling)
        //  Map Index Legend:
        //    0 = Height (Island)
        //    1 = Ocean/River (water mask)
        //    2 = Weather
        //    3 = Raininess
        //    4 = Temperature
        //    5 = Moisture
        //    6 = Island Mask
        //    7 = White Noise (debug)
        //    8 = Gradient Noise 2D (debug)
        ////////////////////////////////////////////////////////////////////////////////////////
        public static Fast2D<Chexel> GetFrameBuffer(int mapIndex, int sizeX, int sizeY)
        {
            if (sizeX <= 0) throw new ArgumentOutOfRangeException(nameof(sizeX), "sizeX must be > 0.");
            if (sizeY <= 0) throw new ArgumentOutOfRangeException(nameof(sizeY), "sizeY must be > 0.");

            int seed = DefaultSeed + mapIndex * 101; // deterministic variety per index
            switch (mapIndex)
            {
                case 0:
                    {
                        var map = GetFramebufferHeightIsland(seed, sizeX, sizeY);
                        return ToChexel(map, PaletteHeightIsland);
                    }
                case 1:
                    {
                        var map = GetFramebufferOceanRiver(seed, sizeX, sizeY);
                        return ToChexel(map, PaletteOceanRiver);
                    }
                case 2:
                    {
                        var map = GetFramebufferWeather(seed, sizeX, sizeY);
                        return ToChexel(map, PaletteWeather);
                    }
                case 3:
                    {
                        var map = GetFramebufferRaininess(seed, sizeX, sizeY);
                        return ToChexel(map, PaletteRaininess);
                    }
                case 4:
                    {
                        var map = GetFramebufferTemperature(seed, sizeX, sizeY);
                        return ToChexel(map, PaletteTemperature);
                    }
                case 5:
                    {
                        var map = GetFramebufferMoisture(seed, sizeX, sizeY);
                        return ToChexel(map, PaletteMoisture);
                    }
                case 6:
                    {
                        var map = GetFramebufferIslandMask(seed, sizeX, sizeY);
                        return ToChexel(map, PaletteIslandMask);
                    }
                case 7:
                    {
                        var map = GetFramebufferWhite(seed, sizeX, sizeY);
                        return ToChexel(map, PaletteGrayscale);
                    }
                case 8:
                    {
                        var map = GetFramebufferGradient2D(seed, sizeX, sizeY);
                        return ToChexel(map, PaletteGrayscale);
                    }
                default:
                    {
                        var map = GetFramebufferHeightIsland(seed, sizeX, sizeY);
                        return ToChexel(map, PaletteHeightIsland);
                    }
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////
        // PUBLIC MAP GENERATORS (float in [0,1])
        ////////////////////////////////////////////////////////////////////////////////////////

        public static Fast2D<float> GetFramebufferWhite(int seed, int sizeX, int sizeY)
        {
            if (sizeX <= 0) throw new ArgumentOutOfRangeException(nameof(sizeX), "sizeX must be > 0.");
            if (sizeY <= 0) throw new ArgumentOutOfRangeException(nameof(sizeY), "sizeY must be > 0.");
            Fast2D<float> fb = new Fast2D<float>(sizeX, sizeY);
            for (int y = 0; y < sizeY; y++) { for (int x = 0; x < sizeX; x++) fb[x, y] = GenMath.Hash01(x, y, 0, seed); }
            return fb;
        }

        public static Fast2D<float> GetFramebufferGradient2D(int seed, int sizeX, int sizeY)
        {
            if (sizeX <= 0) throw new ArgumentOutOfRangeException(nameof(sizeX), "sizeX must be > 0.");
            if (sizeY <= 0) throw new ArgumentOutOfRangeException(nameof(sizeY), "sizeY must be > 0.");
            Fast2D<float> fb = new Fast2D<float>(sizeX, sizeY);
            float baseFreq = 1.0f / MathF.Max(8.0f, MathF.Min(sizeX, sizeY));
            for (int y = 0; y < sizeY; y++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    float n = GenMath.GradientNoise2D(x * baseFreq, y * baseFreq, seed);
                    fb[x, y] = 0.5f * n + 0.5f;
                }
            }
            return fb;
        }

        // Height map for an island; edges guaranteed below SEA_LEVEL via island mask.
        public static Fast2D<float> GetFramebufferHeightIsland(int seed, int sizeX, int sizeY)
        {
            if (sizeX <= 0) throw new ArgumentOutOfRangeException(nameof(sizeX), "sizeX must be > 0.");
            if (sizeY <= 0) throw new ArgumentOutOfRangeException(nameof(sizeY), "sizeY must be > 0.");
            Fast2D<float> fb = new Fast2D<float>(sizeX, sizeY);
            int minDim = Math.Min(sizeX, sizeY);
            float baseFreq = HEIGHT_BASEFREQ_PER_MIN_DIM / Math.Max(1, minDim);
            float warpFreq = WARP_FREQ_PER_MIN_DIM / Math.Max(1, minDim);
            for (int y = 0; y < sizeY; y++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    float mask = IslandMask(x, y, sizeX, sizeY, ISLAND_RADIUS_FRACTION, ISLAND_EDGE_POWER);
                    float wx = x + WARP_STRENGTH * (GenMath.GradientNoise2D(x * warpFreq, y * warpFreq, seed ^ 0x5A17) * 2.0f - 1.0f);
                    float wy = y + WARP_STRENGTH * (GenMath.GradientNoise2D(x * warpFreq, y * warpFreq, seed ^ 0x9B3F) * 2.0f - 1.0f);
                    float baseH = GenMath.FBM2D(wx, wy, FBM_OCTAVES, FBM_LAC, FBM_GAIN, baseFreq, seed + 11);
                    float ridgH = GenMath.RidgedFBM2D(wx, wy, HEIGHT_RIDGE_OCTAVES, FBM_LAC, HEIGHT_RIDGE_GAIN, baseFreq * HEIGHT_RIDGE_MULT, seed + 23);
                    float h = GenMath.Saturate(baseH * HEIGHT_BASE_WEIGHT + ridgH * HEIGHT_RIDGE_WEIGHT);
                    h = MathF.Pow(h, HEIGHT_EXPONENT);
                    h = GenMath.Saturate(h * mask);
                    fb[x, y] = h;
                }
            }
            return fb;
        }

        // Ocean + river water map; 1 = water (ocean or river), 0 = land.
        public static Fast2D<float> GetFramebufferOceanRiver(int seed, int sizeX, int sizeY)
        {
            if (sizeX <= 0) throw new ArgumentOutOfRangeException(nameof(sizeX), "sizeX must be > 0.");
            if (sizeY <= 0) throw new ArgumentOutOfRangeException(nameof(sizeY), "sizeY must be > 0.");
            Fast2D<float> fb = new Fast2D<float>(sizeX, sizeY);
            int minDim = Math.Min(sizeX, sizeY);
            float baseFreq = HEIGHT_BASEFREQ_PER_MIN_DIM / Math.Max(1, minDim);
            float warpFreq = WARP_FREQ_PER_MIN_DIM / Math.Max(1, minDim);
            float riverFreq = RIVER_NOISE_FREQ_PER_MIN_DIM / Math.Max(1, minDim);
            for (int y = 0; y < sizeY; y++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    float mask = IslandMask(x, y, sizeX, sizeY, ISLAND_RADIUS_FRACTION, ISLAND_EDGE_POWER);
                    float wx = x + WARP_STRENGTH * (GenMath.GradientNoise2D(x * warpFreq, y * warpFreq, seed ^ 0x1177) * 2.0f - 1.0f);
                    float wy = y + WARP_STRENGTH * (GenMath.GradientNoise2D(x * warpFreq, y * warpFreq, seed ^ 0x33BB) * 2.0f - 1.0f);
                    float baseH = GenMath.FBM2D(wx, wy, FBM_OCTAVES, FBM_LAC, FBM_GAIN, baseFreq, seed + 11);
                    float ridgH = GenMath.RidgedFBM2D(wx, wy, HEIGHT_RIDGE_OCTAVES, FBM_LAC, HEIGHT_RIDGE_GAIN, baseFreq * HEIGHT_RIDGE_MULT, seed + 23);
                    float h = MathF.Pow(GenMath.Saturate(baseH * HEIGHT_BASE_WEIGHT + ridgH * HEIGHT_RIDGE_WEIGHT), HEIGHT_EXPONENT) * mask;
                    h = GenMath.Saturate(h);
                    float ocean = h < SEA_LEVEL ? 1.0f : 0.0f;
                    float rn = GenMath.FBM2D(wx, wy, RIVER_OCTAVES, FBM_LAC, FBM_GAIN, riverFreq, seed + 101);
                    float dev = MathF.Abs(rn - RIVER_CENTER);
                    float river = 1.0f - GenMath.SmoothStep(RIVER_WIDTH, RIVER_WIDTH + RIVER_BANK_SOFTNESS, dev);
                    float landMask = GenMath.Saturate((h - SEA_LEVEL) * RIVER_ON_LAND_ONLY_SCALE);
                    float water = MathF.Max(ocean, river * landMask);
                    fb[x, y] = GenMath.Saturate(water);
                }
            }
            return fb;
        }

        // Weather/storminess map (large-scale pressure/cloud proxy), [0,1].
        public static Fast2D<float> GetFramebufferWeather(int seed, int sizeX, int sizeY)
        {
            if (sizeX <= 0) throw new ArgumentOutOfRangeException(nameof(sizeX), "sizeX must be > 0.");
            if (sizeY <= 0) throw new ArgumentOutOfRangeException(nameof(sizeY), "sizeY must be > 0.");
            Fast2D<float> fb = new Fast2D<float>(sizeX, sizeY);
            int minDim = Math.Min(sizeX, sizeY);
            float baseFreq = WEATHER_BASEFREQ_PER_MIN_DIM / Math.Max(1, minDim);
            float warpFreq = WARP_FREQ_PER_MIN_DIM / Math.Max(1, minDim);
            for (int y = 0; y < sizeY; y++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    float wx = x + WEATHER_WARP_STRENGTH * (GenMath.GradientNoise2D(x * warpFreq, y * warpFreq, seed ^ 0xCAFE) * 2.0f - 1.0f);
                    float wy = y + WEATHER_WARP_STRENGTH * (GenMath.GradientNoise2D(x * warpFreq, y * warpFreq, seed ^ 0xBEEF) * 2.0f - 1.0f);
                    float w = GenMath.FBM2D(wx, wy, WEATHER_OCTAVES, FBM_LAC, WEATHER_GAIN, baseFreq, seed + 777);
                    fb[x, y] = GenMath.Saturate(w);
                }
            }
            return fb;
        }

        // Raininess (precipitation potential) combining moisture, proximity to ocean, orographic and weather, [0,1].
        public static Fast2D<float> GetFramebufferRaininess(int seed, int sizeX, int sizeY)
        {
            if (sizeX <= 0) throw new ArgumentOutOfRangeException(nameof(sizeX), "sizeX must be > 0.");
            if (sizeY <= 0) throw new ArgumentOutOfRangeException(nameof(sizeY), "sizeY must be > 0.");
            Fast2D<float> fb = new Fast2D<float>(sizeX, sizeY);
            int minDim = Math.Min(sizeX, sizeY);
            float hFreq = HEIGHT_BASEFREQ_PER_MIN_DIM / Math.Max(1, minDim);
            float warpFreq = WARP_FREQ_PER_MIN_DIM / Math.Max(1, minDim);
            float mFreq = MOISTURE_NOISE_FREQ_PER_MIN_DIM / Math.Max(1, minDim);
            float wFreq = WEATHER_BASEFREQ_PER_MIN_DIM / Math.Max(1, minDim);
            for (int y = 0; y < sizeY; y++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    float mask = IslandMask(x, y, sizeX, sizeY, ISLAND_RADIUS_FRACTION, ISLAND_EDGE_POWER);
                    float wx = x + WARP_STRENGTH * (GenMath.GradientNoise2D(x * warpFreq, y * warpFreq, seed ^ 0x1144) * 2.0f - 1.0f);
                    float wy = y + WARP_STRENGTH * (GenMath.GradientNoise2D(x * warpFreq, y * warpFreq, seed ^ 0x2266) * 2.0f - 1.0f);
                    float baseH = GenMath.FBM2D(wx, wy, FBM_OCTAVES, FBM_LAC, FBM_GAIN, hFreq, seed + 11);
                    float ridgH = GenMath.RidgedFBM2D(wx, wy, HEIGHT_RIDGE_OCTAVES, FBM_LAC, HEIGHT_RIDGE_GAIN, hFreq * HEIGHT_RIDGE_MULT, seed + 23);
                    float h = MathF.Pow(GenMath.Saturate(baseH * HEIGHT_BASE_WEIGHT + ridgH * HEIGHT_RIDGE_WEIGHT), HEIGHT_EXPONENT) * mask;
                    h = GenMath.Saturate(h);
                    float coast = 1.0f - mask;
                    float moistureNoise = GenMath.FBM2D(wx, wy, FBM_OCTAVES - 1, FBM_LAC, FBM_GAIN, mFreq, seed + 333);
                    float moisture = GenMath.Saturate(coast * COAST_MOISTURE_WEIGHT + moistureNoise * (1.0f - COAST_MOISTURE_WEIGHT));
                    float altitudeDry = GenMath.Saturate(1.0f - (h - SEA_LEVEL) * ALTITUDE_DRYING);
                    float weather = GenMath.FBM2D(wx, wy, WEATHER_OCTAVES, FBM_LAC, WEATHER_GAIN, wFreq, seed + 777);
                    float rain = GenMath.Saturate(moisture * MOISTURE_TO_RAININESS * altitudeDry + weather * WEATHER_TO_RAININESS * 0.7f);
                    fb[x, y] = rain;
                }
            }
            return fb;
        }

        // Additional: Temperature map, colder with latitude and altitude, [0,1].
        public static Fast2D<float> GetFramebufferTemperature(int seed, int sizeX, int sizeY)
        {
            if (sizeX <= 0) throw new ArgumentOutOfRangeException(nameof(sizeX), "sizeX must be > 0.");
            if (sizeY <= 0) throw new ArgumentOutOfRangeException(nameof(sizeY), "sizeY must be > 0.");
            Fast2D<float> fb = new Fast2D<float>(sizeX, sizeY);
            int minDim = Math.Min(sizeX, sizeY);
            float tFreq = TEMP_NOISE_FREQ_PER_MIN_DIM / Math.Max(1, minDim);
            float hFreq = HEIGHT_BASEFREQ_PER_MIN_DIM / Math.Max(1, minDim);
            float warpFreq = WARP_FREQ_PER_MIN_DIM / Math.Max(1, minDim);
            for (int y = 0; y < sizeY; y++)
            {
                float lat = (float)y / Math.Max(1, sizeY - 1);
                float latGrad = 1.0f - lat;
                for (int x = 0; x < sizeX; x++)
                {
                    float wx = x + WARP_STRENGTH * (GenMath.GradientNoise2D(x * warpFreq, y * warpFreq, seed ^ 0x7A7A) * 2.0f - 1.0f);
                    float wy = y + WARP_STRENGTH * (GenMath.GradientNoise2D(x * warpFreq, y * warpFreq, seed ^ 0x8B8B) * 2.0f - 1.0f);
                    float tNoise = (GenMath.FBM2D(wx, wy, FBM_OCTAVES - 1, FBM_LAC, FBM_GAIN, tFreq, seed + 444) - 0.5f) * 2.0f * TEMP_NOISE_AMPLITUDE + 0.5f;
                    float baseH = GenMath.FBM2D(wx, wy, FBM_OCTAVES, FBM_LAC, FBM_GAIN, hFreq, seed + 11);
                    float ridgH = GenMath.RidgedFBM2D(wx, wy, HEIGHT_RIDGE_OCTAVES, FBM_LAC, HEIGHT_RIDGE_GAIN, hFreq * HEIGHT_RIDGE_MULT, seed + 23);
                    float h = MathF.Pow(GenMath.Saturate(baseH * HEIGHT_BASE_WEIGHT + ridgH * HEIGHT_RIDGE_WEIGHT), HEIGHT_EXPONENT);
                    float temp = GenMath.Saturate(tNoise * (1.0f - TEMP_LAT_GRADIENT_STRENGTH) + latGrad * TEMP_LAT_GRADIENT_STRENGTH - GenMath.Saturate(h - SEA_LEVEL) * TEMP_LAPSE_WITH_ALT);
                    fb[x, y] = temp;
                }
            }
            return fb;
        }

        // Additional: Moisture (baseline humidity) separate from raininess, [0,1].
        public static Fast2D<float> GetFramebufferMoisture(int seed, int sizeX, int sizeY)
        {
            if (sizeX <= 0) throw new ArgumentOutOfRangeException(nameof(sizeX), "sizeX must be > 0.");
            if (sizeY <= 0) throw new ArgumentOutOfRangeException(nameof(sizeY), "sizeY must be > 0.");
            Fast2D<float> fb = new Fast2D<float>(sizeX, sizeY);
            int minDim = Math.Min(sizeX, sizeY);
            float mFreq = MOISTURE_NOISE_FREQ_PER_MIN_DIM / Math.Max(1, minDim);
            float warpFreq = WARP_FREQ_PER_MIN_DIM / Math.Max(1, minDim);
            for (int y = 0; y < sizeY; y++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    float mask = IslandMask(x, y, sizeX, sizeY, ISLAND_RADIUS_FRACTION, ISLAND_EDGE_POWER);
                    float coastal = 1.0f - mask;
                    float wx = x + WARP_STRENGTH * (GenMath.GradientNoise2D(x * warpFreq, y * warpFreq, seed ^ 0xDD11) * 2.0f - 1.0f);
                    float wy = y + WARP_STRENGTH * (GenMath.GradientNoise2D(x * warpFreq, y * warpFreq, seed ^ 0xEE22) * 2.0f - 1.0f);
                    float n = GenMath.FBM2D(wx, wy, FBM_OCTAVES - 1, FBM_LAC, FBM_GAIN, mFreq, seed + 333);
                    float moisture = GenMath.Saturate(coastal * COAST_MOISTURE_WEIGHT + n * (1.0f - COAST_MOISTURE_WEIGHT));
                    fb[x, y] = moisture;
                }
            }
            return fb;
        }

        // Additional: Island mask only, 1=land core, 0=ocean edge.
        public static Fast2D<float> GetFramebufferIslandMask(int seed, int sizeX, int sizeY)
        {
            if (sizeX <= 0) throw new ArgumentOutOfRangeException(nameof(sizeX), "sizeX must be > 0.");
            if (sizeY <= 0) throw new ArgumentOutOfRangeException(nameof(sizeY), "sizeY must be > 0.");
            Fast2D<float> fb = new Fast2D<float>(sizeX, sizeY);
            for (int y = 0; y < sizeY; y++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    fb[x, y] = IslandMask(x, y, sizeX, sizeY, ISLAND_RADIUS_FRACTION, ISLAND_EDGE_POWER);
                }
            }
            return fb;
        }

        ////////////////////////////////////////////////////////////////////////////////////////
        // CONVERTERS: float map -> Chexel buffer (per-pixel palette)
        ////////////////////////////////////////////////////////////////////////////////////////
        private static Fast2D<Chexel> ToChexel(Fast2D<float> map01, Func<float, Vec3> palette)
        {
            Fast2D<Chexel> outFb = new Fast2D<Chexel>(map01.Width, map01.Height);
            for (int y = 0; y < map01.Height; y++)
            {
                for (int x = 0; x < map01.Width; x++)
                {
                    float v = map01[x, y];
                    if (v < 0.0f) v = 0.0f;
                    if (v > 1.0f) v = 1.0f;
                    Vec3 c = palette(v);
                    outFb[x, y] = new Chexel('▀', c, c); // uniform color; top=bottom
                }
            }
            return outFb;
        }

        ////////////////////////////////////////////////////////////////////////////////////////
        // PALETTES
        ////////////////////////////////////////////////////////////////////////////////////////
        private static Vec3 PaletteGrayscale(float v)
        {
            return new Vec3(v, v, v);
        }

        private static Vec3 PaletteIslandMask(float v)
        {
            Vec3 water = RGB(8, 61, 120);
            Vec3 land = RGB(45, 140, 74);
            return LerpColor(water, land, v);
        }

        private static Vec3 PaletteOceanRiver(float v)
        {
            Vec3 land = RGB(28, 28, 28);
            Vec3 deep = RGB(8, 61, 120);
            Vec3 light = RGB(64, 180, 255);
            Vec3 water = LerpColor(deep, light, GenMath.Saturate(v * 0.85f + 0.15f));
            return LerpColor(land, water, v);
        }

        private static Vec3 PaletteWeather(float v)
        {
            Vec3 sky = RGB(30, 40, 60);
            Vec3 cloud = RGB(235, 235, 240);
            return LerpColor(sky, cloud, v);
        }

        private static Vec3 PaletteRaininess(float v)
        {
            if (v < 0.30f) return LerpColor(RGB(194, 168, 120), RGB(157, 193, 94), v / 0.30f); // dry tan -> light green
            if (v < 0.70f) return LerpColor(RGB(157, 193, 94), RGB(62, 147, 78), (v - 0.30f) / 0.40f); // light green -> lush green
            return LerpColor(RGB(62, 147, 78), RGB(46, 125, 210), (v - 0.70f) / 0.30f); // green -> rainy blue
        }

        private static Vec3 PaletteTemperature(float v)
        {
            if (v < 0.5f) return LerpColor(RGB(44, 111, 216), RGB(247, 244, 160), v / 0.5f); // cold blue -> temperate
            return LerpColor(RGB(247, 244, 160), RGB(255, 94, 0), (v - 0.5f) / 0.5f); // temperate -> hot
        }

        private static Vec3 PaletteMoisture(float v)
        {
            if (v < 0.5f) return LerpColor(RGB(176, 143, 96), RGB(89, 165, 99), v / 0.5f); // dry brown -> green
            return LerpColor(RGB(89, 165, 99), RGB(61, 178, 209), (v - 0.5f) / 0.5f); // green -> cyan wet
        }

        private static Vec3 PaletteHeightIsland(float h)
        {
            if (h < SEA_LEVEL)
            {
                float t = h / MathF.Max(1e-6f, SEA_LEVEL);
                t = MathF.Sqrt(t);
                Vec3 deep = RGB(3, 34, 76);
                Vec3 shallow = RGB(0, 168, 232);
                return LerpColor(deep, shallow, t);
            }
            float tland = GenMath.Saturate((h - SEA_LEVEL) / MathF.Max(1e-6f, 1.0f - SEA_LEVEL));
            if (tland < 0.08f) return LerpColor(RGB(196, 179, 126), RGB(226, 212, 167), tland / 0.08f); // beach -> sand
            if (tland < 0.45f) return LerpColor(RGB(69, 148, 74), RGB(34, 112, 44), (tland - 0.08f) / 0.37f); // light grass -> dark grass
            if (tland < 0.75f) return LerpColor(RGB(110, 130, 60), RGB(130, 130, 130), (tland - 0.45f) / 0.30f); // scrub -> rock
            if (tland < 0.92f) return LerpColor(RGB(96, 96, 96), RGB(220, 220, 220), (tland - 0.75f) / 0.17f * 0.6f); // dark rock -> light rock
            return RGB(255, 255, 255); // snow
        }

        ////////////////////////////////////////////////////////////////////////////////////////
        // HELPERS
        ////////////////////////////////////////////////////////////////////////////////////////
        private static float IslandMask(int x, int y, int sizeX, int sizeY, float radiusFrac, float edgePow)
        {
            float cx = (sizeX - 1) * 0.5f;
            float cy = (sizeY - 1) * 0.5f;
            float rx = (x - cx) / MathF.Max(1e-6f, radiusFrac * cx);
            float ry = (y - cy) / MathF.Max(1e-6f, radiusFrac * cy);
            float r = MathF.Sqrt(rx * rx + ry * ry);
            float m = 1.0f - MathF.Pow(MathF.Max(0.0f, r), edgePow);
            return GenMath.Saturate(m);
        }

        private static Vec3 RGB(byte r, byte g, byte b)
        {
            return new Vec3(r / 255.0, g / 255.0, b / 255.0);
        }

        private static Vec3 LerpColor(Vec3 a, Vec3 b, float t)
        {
            return a * (1.0f - t) + b * t;
        }
    }
}