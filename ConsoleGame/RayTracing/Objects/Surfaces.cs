using ConsoleGame.RayTracing;
using ConsoleGame.RayTracing.Objects;
using System.Runtime.CompilerServices;

namespace ConsoleGame.RayTracing.Objects.Surfaces
{
    public sealed class Plane : Hittable
    {
        public Vec3 Point;
        public Vec3 Normal;
        public Func<Vec3, Vec3, float, Material> MaterialFunc;
        public float Specular;
        public float Reflectivity;
        private readonly float ndotPoint;
        private readonly Vec3 NormalNeg;
        private const float Eps = 1e-6f;

        public Plane(Vec3 p, Vec3 n, Func<Vec3, Vec3, float, Material> matFunc, float specular, float reflectivity)
        {
            Point = p;
            Normal = n.Normalized();
            MaterialFunc = matFunc;
            Specular = specular;
            Reflectivity = reflectivity;
            ndotPoint = Normal.X * Point.X + Normal.Y * Point.Y + Normal.Z * Point.Z;
            NormalNeg = new Vec3(-Normal.X, -Normal.Y, -Normal.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool TryGetBounds(out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ, out float cx, out float cy, out float cz)
        {
            float B = 1e6f;
            minX = -B; minY = -B; minZ = -B; maxX = B; maxY = B; maxZ = B;
            cx = 0.0f; cy = 0.0f; cz = 0.0f;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            float nx = Normal.X, ny = Normal.Y, nz = Normal.Z;
            float dx = r.Dir.X, dy = r.Dir.Y, dz = r.Dir.Z;
            float ox = r.Origin.X, oy = r.Origin.Y, oz = r.Origin.Z;

            float denom = nx * dx + ny * dy + nz * dz;
            if (denom > -Eps && denom < Eps)
            {
                return false;
            }

            float t = (ndotPoint - (nx * ox + ny * oy + nz * oz)) / denom;
            if (t < tMin || t > tMax)
            {
                return false;
            }

            float px = ox + t * dx;
            float py = oy + t * dy;
            float pz = oz + t * dz;

            rec.T = t;
            rec.P = new Vec3(px, py, pz);
            rec.N = denom < 0.0f ? Normal : NormalNeg;
            Material baseMat = MaterialFunc(rec.P, rec.N, 0.0f);
            baseMat.Specular = Specular;
            baseMat.Reflectivity = Reflectivity;
            rec.Mat = baseMat;
            rec.U = 0.0f;
            rec.V = 0.0f;
            return true;
        }
    }
    public sealed class Disk : Hittable
    {
        public Vec3 Center;
        public Vec3 Normal;
        public float Radius;
        public Func<Vec3, Vec3, float, Material> MaterialFunc;
        public float Specular;
        public float Reflectivity;
        private readonly float ndotCenter;
        private readonly float radius2;

        public Disk(Vec3 center, Vec3 normal, float radius, Func<Vec3, Vec3, float, Material> matFunc, float specular, float reflectivity)
        {
            Center = center;
            Normal = normal.Normalized();
            Radius = radius;
            MaterialFunc = matFunc;
            Specular = specular;
            Reflectivity = reflectivity;
            ndotCenter = Normal.Dot(Center);
            radius2 = radius * radius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool TryGetBounds(out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ, out float cx, out float cy, out float cz)
        {
            Vec3 r = new Vec3(Radius, Radius, Radius);
            Vec3 mn = Center - r;
            Vec3 mx = Center + r;
            minX = mn.X; minY = mn.Y; minZ = mn.Z; maxX = mx.X; maxY = mx.Y; maxZ = mx.Z;
            cx = 0.5f * (minX + maxX); cy = 0.5f * (minY + maxY); cz = 0.5f * (minZ + maxZ);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            float denom = Normal.Dot(r.Dir);
            float adenom = MathF.Abs(denom);
            float safeDenom = MathF.CopySign(MathF.Max(adenom, 1e-8f), denom);
            float t = (ndotCenter - Normal.Dot(r.Origin)) / safeDenom;

            float px = r.Origin.X + t * r.Dir.X;
            float py = r.Origin.Y + t * r.Dir.Y;
            float pz = r.Origin.Z + t * r.Dir.Z;

            float dx = px - Center.X;
            float dz = pz - Center.Z;
            float rr = dx * dx + dz * dz;

            bool ok = adenom >= 1e-6f;
            ok &= t >= tMin & t <= tMax;
            ok &= rr <= radius2;

            if (!ok)
            {
                return false;
            }

            rec.T = t;
            rec.P = new Vec3(px, py, pz);
            rec.N = denom < 0.0f ? Normal : -Normal;
            Material baseMat = MaterialFunc(rec.P, rec.N, 0.0f);
            baseMat.Specular = Specular;
            baseMat.Reflectivity = Reflectivity;
            rec.Mat = baseMat;
            rec.U = 0.0f;
            rec.V = 0.0f;
            return true;
        }
    }

    public sealed class XYRect : Hittable
    {
        public float X0;
        public float X1;
        public float Y0;
        public float Y1;
        public float Z;
        public Func<Vec3, Vec3, float, Material> MaterialFunc;
        public float Specular;
        public float Reflectivity;
        private readonly float invXSpan;
        private readonly float invYSpan;
        private static readonly Vec3 NzPos = new Vec3(0.0f, 0.0f, 1.0f);
        private static readonly Vec3 NzNeg = new Vec3(0.0f, 0.0f, -1.0f);

        public XYRect(float x0, float x1, float y0, float y1, float z, Func<Vec3, Vec3, float, Material> matFunc, float specular, float reflectivity)
        {
            X0 = x0;
            X1 = x1;
            Y0 = y0;
            Y1 = y1;
            Z = z;
            MaterialFunc = matFunc;
            Specular = specular;
            Reflectivity = reflectivity;
            invXSpan = 1.0f / (X1 - X0);
            invYSpan = 1.0f / (Y1 - Y0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool TryGetBounds(out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ, out float cx, out float cy, out float cz)
        {
            const float Eps = 1e-4f;
            minX = X0; minY = Y0; minZ = Z - Eps; maxX = X1; maxY = Y1; maxZ = Z + Eps;
            cx = 0.5f * (minX + maxX); cy = 0.5f * (minY + maxY); cz = 0.5f * (minZ + maxZ);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            float dirZ = r.Dir.Z;
            float adirZ = MathF.Abs(dirZ);
            float safeDirZ = MathF.CopySign(MathF.Max(adirZ, 1e-8f), dirZ);
            float t = (Z - r.Origin.Z) / safeDirZ;

            float px = r.Origin.X + t * r.Dir.X;
            float py = r.Origin.Y + t * r.Dir.Y;

            bool ok = adirZ >= 1e-8f;
            ok &= t >= tMin & t <= tMax;
            ok &= px >= X0 & px <= X1 & py >= Y0 & py <= Y1;

            if (!ok)
            {
                return false;
            }

            rec.T = t;
            rec.P = new Vec3(px, py, Z);
            float nz = MathF.CopySign(1.0f, -dirZ);
            rec.N = new Vec3(0.0f, 0.0f, nz);
            Material baseMat = MaterialFunc(rec.P, rec.N, 0.0f);
            baseMat.Specular = Specular;
            baseMat.Reflectivity = Reflectivity;
            rec.Mat = baseMat;
            rec.U = (px - X0) * invXSpan;
            rec.V = (py - Y0) * invYSpan;
            return true;
        }
    }

    public sealed class XZRect : Hittable
    {
        public float X0;
        public float X1;
        public float Z0;
        public float Z1;
        public float Y;
        public Func<Vec3, Vec3, float, Material> MaterialFunc;
        public float Specular;
        public float Reflectivity;
        private readonly float invXSpan;
        private readonly float invZSpan;
        private static readonly Vec3 NyPos = new Vec3(0.0f, 1.0f, 0.0f);
        private static readonly Vec3 NyNeg = new Vec3(0.0f, -1.0f, 0.0f);

        public XZRect(float x0, float x1, float z0, float z1, float y, Func<Vec3, Vec3, float, Material> matFunc, float specular, float reflectivity)
        {
            X0 = x0;
            X1 = x1;
            Z0 = z0;
            Z1 = z1;
            Y = y;
            MaterialFunc = matFunc;
            Specular = specular;
            Reflectivity = reflectivity;
            invXSpan = 1.0f / (X1 - X0);
            invZSpan = 1.0f / (Z1 - Z0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool TryGetBounds(out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ, out float cx, out float cy, out float cz)
        {
            const float Eps = 1e-4f;
            minX = X0; minY = Y - Eps; minZ = Z0; maxX = X1; maxY = Y + Eps; maxZ = Z1;
            cx = 0.5f * (minX + maxX); cy = 0.5f * (minY + maxY); cz = 0.5f * (minZ + maxZ);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            float dirY = r.Dir.Y;
            float adirY = MathF.Abs(dirY);
            float safeDirY = MathF.CopySign(MathF.Max(adirY, 1e-8f), dirY);
            float t = (Y - r.Origin.Y) / safeDirY;

            float px = r.Origin.X + t * r.Dir.X;
            float pz = r.Origin.Z + t * r.Dir.Z;

            bool ok = adirY >= 1e-8f;
            ok &= t >= tMin & t <= tMax;
            ok &= px >= X0 & px <= X1 & pz >= Z0 & pz <= Z1;

            if (!ok)
            {
                return false;
            }

            rec.T = t;
            rec.P = new Vec3(px, Y, pz);
            float ny = MathF.CopySign(1.0f, -dirY);
            rec.N = new Vec3(0.0f, ny, 0.0f);
            Material baseMat = MaterialFunc(rec.P, rec.N, 0.0f);
            baseMat.Specular = Specular;
            baseMat.Reflectivity = Reflectivity;
            rec.Mat = baseMat;
            rec.U = (px - X0) * invXSpan;
            rec.V = (pz - Z0) * invZSpan;
            return true;
        }
    }

    public sealed class YZRect : Hittable
    {
        public float Y0;
        public float Y1;
        public float Z0;
        public float Z1;
        public float X;
        public Func<Vec3, Vec3, float, Material> MaterialFunc;
        public float Specular;
        public float Reflectivity;
        private readonly float invYSpan;
        private readonly float invZSpan;
        private static readonly Vec3 NxPos = new Vec3(1.0f, 0.0f, 0.0f);
        private static readonly Vec3 NxNeg = new Vec3(-1.0f, 0.0f, 0.0f);

        public YZRect(float y0, float y1, float z0, float z1, float x, Func<Vec3, Vec3, float, Material> matFunc, float specular, float reflectivity)
        {
            Y0 = y0;
            Y1 = y1;
            Z0 = z0;
            Z1 = z1;
            X = x;
            MaterialFunc = matFunc;
            Specular = specular;
            Reflectivity = reflectivity;
            invYSpan = 1.0f / (Y1 - Y0);
            invZSpan = 1.0f / (Z1 - Z0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool TryGetBounds(out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ, out float cx, out float cy, out float cz)
        {
            const float Eps = 1e-4f;
            minX = X - Eps; minY = Y0; minZ = Z0; maxX = X + Eps; maxY = Y1; maxZ = Z1;
            cx = 0.5f * (minX + maxX); cy = 0.5f * (minY + maxY); cz = 0.5f * (minZ + maxZ);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            float dirX = r.Dir.X;
            float adirX = MathF.Abs(dirX);
            float safeDirX = MathF.CopySign(MathF.Max(adirX, 1e-8f), dirX);
            float t = (X - r.Origin.X) / safeDirX;

            float py = r.Origin.Y + t * r.Dir.Y;
            float pz = r.Origin.Z + t * r.Dir.Z;

            bool ok = adirX >= 1e-8f;
            ok &= t >= tMin & t <= tMax;
            ok &= py >= Y0 & py <= Y1 & pz >= Z0 & pz <= Z1;

            if (!ok)
            {
                return false;
            }

            rec.T = t;
            rec.P = new Vec3(X, py, pz);
            float nx = MathF.CopySign(1.0f, -dirX);
            rec.N = new Vec3(nx, 0.0f, 0.0f);
            Material baseMat = MaterialFunc(rec.P, rec.N, 0.0f);
            baseMat.Specular = Specular;
            baseMat.Reflectivity = Reflectivity;
            rec.Mat = baseMat;
            rec.U = (py - Y0) * invYSpan;
            rec.V = (pz - Z0) * invZSpan;
            return true;
        }
    }
}
