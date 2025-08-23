using System;
using System.Runtime.CompilerServices;

namespace ConsoleGame.RayTracing
{
    public struct Vec3
    {
        public float X;
        public float Y;
        public float Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec3(double x, double y, double z)
        {
            X = (float)x;
            Y = (float)y;
            Z = (float)z;
        }

        public static Vec3 Zero => new Vec3(0.0f, 0.0f, 0.0f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 operator +(Vec3 a, Vec3 b)
        {
            return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 operator -(Vec3 a, Vec3 b)
        {
            return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 operator -(Vec3 a)
        {
            return new Vec3(-a.X, -a.Y, -a.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 operator *(Vec3 a, Vec3 b)
        {
            return new Vec3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 operator *(Vec3 a, float s)
        {
            return new Vec3(a.X * s, a.Y * s, a.Z * s);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 operator *(float s, Vec3 a)
        {
            return new Vec3(a.X * s, a.Y * s, a.Z * s);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 operator /(Vec3 a, float s)
        {
            float inv = 1.0f / s;
            return new Vec3(a.X * inv, a.Y * inv, a.Z * inv);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly float Dot(Vec3 b)
        {
            return X * b.X + Y * b.Y + Z * b.Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly Vec3 Cross(Vec3 b)
        {
            return new Vec3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly float LengthSquared()
        {
            return X * X + Y * Y + Z * Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly float Length()
        {
            return MathF.Sqrt(X * X + Y * Y + Z * Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly Vec3 Normalized()
        {
            float lenSq = X * X + Y * Y + Z * Z;
            if (lenSq <= 0.0f)
            {
                return this;
            }
            float invLen = 1.0f / MathF.Sqrt(lenSq);
            return new Vec3(X * invLen, Y * invLen, Z * invLen);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly Vec3 Saturate()
        {
            return new Vec3(Clamp01(X), Clamp01(Y), Clamp01(Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp01(float v)
        {
            if (v < 0.0f)
            {
                return 0.0f;
            }
            if (v > 1.0f)
            {
                return 1.0f;
            }
            return v;
        }
    }
}
