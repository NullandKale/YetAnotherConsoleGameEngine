using System.Runtime.CompilerServices;

namespace ConsoleGame.RayTracing
{
    public static class RaytraceSampler
    {
        private const int BlueTileSize = 8;

        private static readonly byte[,] BlueNoise8x8 = new byte[BlueTileSize, BlueTileSize]
        {
            {  0, 32,  8, 40,  2, 34, 10, 42 },
            { 48, 16, 56, 24, 50, 18, 58, 26 },
            { 12, 44,  4, 36, 14, 46,  6, 38 },
            { 60, 28, 52, 20, 62, 30, 54, 22 },
            {  3, 35, 11, 43,  1, 33,  9, 41 },
            { 51, 19, 59, 27, 49, 17, 57, 25 },
            { 15, 47,  7, 39, 13, 45,  5, 37 },
            { 63, 31, 55, 23, 61, 29, 53, 21 }
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Frac(float v)
        {
            return v - MathF.Floor(v);
        }

        public static float BlueNoiseSample(int x, int y, int frameIdx, int channel)
        {
            int ix = x & (BlueTileSize - 1);
            int iy = y & (BlueTileSize - 1);
            float baseVal = (BlueNoise8x8[iy, ix] + 0.5f) * (1.0f / (BlueTileSize * BlueTileSize));
            float rot = Frac((frameIdx + 1) * (channel == 0 ? 0.7548776662466927f : 0.5698402909980532f));
            return Frac(baseVal + rot);
        }

        public struct Rng
        {
            private ulong state;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Rng(ulong seed)
            {
                state = seed != 0 ? seed : 0x9E3779B97F4A7C15UL;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float NextUnit()
            {
                state = SplitMix64(state);
                uint m24 = (uint)(state >> 40);
                return (m24 + 0.5f) * (1.0f / 16777216.0f);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong PerFrameSeed(int x, int y, long frame, int jx, int jy, ulong salt)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                h ^= (ulong)(x) * 0x9E3779B97F4A7C15UL; h = SplitMix64(h);
                h ^= (ulong)(y) * 0xC2B2AE3D27D4EB4FUL; h = SplitMix64(h);
                h ^= (ulong)frame * 0x165667B19E3779F9UL; h = SplitMix64(h);
                h ^= ((ulong)(byte)jx << 8) ^ (ulong)(byte)jy; h = SplitMix64(h);
                h ^= salt; h = SplitMix64(h);
                return h;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong SplitMix64(ulong z)
        {
            unchecked
            {
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 CosineSampleHemisphere(Vec3 n, ref Rng rng)
        {
            float u1 = rng.NextUnit();
            float u2 = rng.NextUnit();
            float r = MathF.Sqrt(u1);
            float phi = 6.2831853071795864769f * u2;
            var sc = MathF.SinCos(phi);
            float x = r * sc.Cos;
            float y = r * sc.Sin;
            float z = MathF.Sqrt(1.0f - u1);

            Vec3 w = n;
            float wz = (float)w.Z;
            if (wz < -0.999999f)
            {
                Vec3 u = new Vec3(0.0, -1.0, 0.0);
                Vec3 v = new Vec3(-1.0, 0.0, 0.0);
                Vec3 dir = u * x + v * y + w * z;
                return dir;
            }

            float a = 1.0f / (1.0f + wz);
            float b = (float)(-w.X * w.Y) * a;
            Vec3 uAxis = new Vec3(1.0 - (w.X * w.X) * a, b, -w.X);
            Vec3 vAxis = new Vec3(b, 1.0 - (w.Y * w.Y) * a, -w.Y);

            Vec3 outDir = uAxis * x + vAxis * y + w * z;
            return outDir;
        }
    }
}
