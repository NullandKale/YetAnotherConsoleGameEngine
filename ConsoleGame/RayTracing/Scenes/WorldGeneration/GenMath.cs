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
}
