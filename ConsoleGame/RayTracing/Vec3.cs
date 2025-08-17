using System;

namespace ConsoleGame.RayTracing
{
    public struct Vec3
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Vec3(double x, double y, double z)
        {
            X = (float)x;
            Y = (float)y;
            Z = (float)z;
        }

        public static Vec3 Zero => new Vec3(0.0f, 0.0f, 0.0f);

        public static Vec3 operator +(Vec3 a, Vec3 b)
        {
            return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static Vec3 operator -(Vec3 a, Vec3 b)
        {
            return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public static Vec3 operator -(Vec3 a)
        {
            return new Vec3(-a.X, -a.Y, -a.Z);
        }

        public static Vec3 operator *(Vec3 a, Vec3 b)
        {
            return new Vec3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        }

        public static Vec3 operator *(Vec3 a, float s)
        {
            return new Vec3(a.X * s, a.Y * s, a.Z * s);
        }

        public static Vec3 operator *(float s, Vec3 a)
        {
            return new Vec3(a.X * s, a.Y * s, a.Z * s);
        }

        public static Vec3 operator /(Vec3 a, float s)
        {
            return new Vec3(a.X / s, a.Y / s, a.Z / s);
        }

        public float Dot(Vec3 b)
        {
            return X * b.X + Y * b.Y + Z * b.Z;
        }

        public Vec3 Cross(Vec3 b)
        {
            return new Vec3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        }

        public float Length()
        {
            return MathF.Sqrt(Dot(this));
        }

        public Vec3 Normalized()
        {
            float len = Length();
            if (len <= 0.0)
            {
                return this;
            }
            return this / len;
        }

        public Vec3 Saturate()
        {
            return new Vec3(Clamp01(X), Clamp01(Y), Clamp01(Z));
        }

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
