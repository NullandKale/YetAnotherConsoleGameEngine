using System.Runtime.CompilerServices;
using ConsoleGame.RayTracing.Objects.Surfaces;

namespace ConsoleGame.RayTracing.Objects.BoundedSolids
{
    public sealed class Sphere : Hittable
    {
        public Vec3 Center;
        public float Radius;
        public Material Mat;

        public Sphere(Vec3 c, float r, Material m)
        {
            Center = c;
            Radius = r;
            Mat = m;
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
            float ox = r.Origin.X - Center.X;
            float oy = r.Origin.Y - Center.Y;
            float oz = r.Origin.Z - Center.Z;
            float dx = r.Dir.X;
            float dy = r.Dir.Y;
            float dz = r.Dir.Z;
            float a = dx * dx + dy * dy + dz * dz;
            float halfB = ox * dx + oy * dy + oz * dz;
            float c = ox * ox + oy * oy + oz * oz - Radius * Radius;
            float disc = halfB * halfB - a * c;
            if (disc < 0.0f)
            {
                return false;
            }
            float s = MathF.Sqrt(disc);
            float invA = 1.0f / a;
            float t = (-halfB - s) * invA;
            if (t < tMin || t > tMax)
            {
                t = (-halfB + s) * invA;
                if (t < tMin || t > tMax)
                {
                    return false;
                }
            }
            float px = r.Origin.X + t * dx;
            float py = r.Origin.Y + t * dy;
            float pz = r.Origin.Z + t * dz;
            float invR = 1.0f / Radius;
            rec.T = t;
            rec.P = new Vec3(px, py, pz);
            rec.N = new Vec3((px - Center.X) * invR, (py - Center.Y) * invR, (pz - Center.Z) * invR);
            rec.Mat = Mat;
            rec.U = 0.0f;
            rec.V = 0.0f;
            return true;
        }
    }

    public sealed class Box : Hittable
    {
        public Vec3 Min;
        public Vec3 Max;
        private readonly Hittable[] faces;

        public Box(Vec3 min, Vec3 max, Func<Vec3, Vec3, float, Material> matFunc, float specular, float reflectivity)
        {
            Min = min;
            Max = max;
            faces = new Hittable[6];
            faces[0] = new XYRect(min.X, max.X, min.Y, max.Y, max.Z, matFunc, specular, reflectivity);
            faces[1] = new XYRect(min.X, max.X, min.Y, max.Y, min.Z, matFunc, specular, reflectivity);
            faces[2] = new XZRect(min.X, max.X, min.Z, max.Z, max.Y, matFunc, specular, reflectivity);
            faces[3] = new XZRect(min.X, max.X, min.Z, max.Z, min.Y, matFunc, specular, reflectivity);
            faces[4] = new YZRect(min.Y, max.Y, min.Z, max.Z, max.X, matFunc, specular, reflectivity);
            faces[5] = new YZRect(min.Y, max.Y, min.Z, max.Z, min.X, matFunc, specular, reflectivity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool TryGetBounds(out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ, out float cx, out float cy, out float cz)
        {
            minX = Min.X; minY = Min.Y; minZ = Min.Z; maxX = Max.X; maxY = Max.Y; maxZ = Max.Z;
            cx = 0.5f * (minX + maxX); cy = 0.5f * (minY + maxY); cz = 0.5f * (minZ + maxZ);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            bool hitAnything = false;
            float closest = tMax;
            HitRecord temp = default;
            for (int i = 0; i < 6; i++)
            {
                if (faces[i].Hit(r, tMin, closest, ref temp, screenU, screenV))
                {
                    hitAnything = true;
                    closest = temp.T;
                    rec = temp;
                }
            }
            return hitAnything;
        }
    }

    public sealed class CylinderY : Hittable
    {
        public Vec3 Center;
        public float Radius;
        public float YMin;
        public float YMax;
        public bool Capped;
        public Material Mat;
        private readonly float radius2;

        public CylinderY(Vec3 center, float radius, float yMin, float yMax, bool capped, Material mat)
        {
            Center = center;
            Radius = radius;
            YMin = MathF.Min(yMin, yMax);
            YMax = MathF.Max(yMin, yMax);
            Capped = capped;
            Mat = mat;
            radius2 = radius * radius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool TryGetBounds(out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ, out float cx, out float cy, out float cz)
        {
            minX = Center.X - Radius; minY = YMin; minZ = Center.Z - Radius; maxX = Center.X + Radius; maxY = YMax; maxZ = Center.Z + Radius;
            cx = 0.5f * (minX + maxX); cy = 0.5f * (minY + maxY); cz = 0.5f * (minZ + maxZ);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            float ox = r.Origin.X - Center.X;
            float oy = r.Origin.Y;
            float oz = r.Origin.Z - Center.Z;
            float dx = r.Dir.X;
            float dy = r.Dir.Y;
            float dz = r.Dir.Z;
            float a = dx * dx + dz * dz;
            float hitT = float.MaxValue;
            Vec3 hitN = Vec3.Zero;
            bool hit = false;
            if (a > 1e-12f)
            {
                float halfB = ox * dx + oz * dz;
                float c = ox * ox + oz * oz - radius2;
                float disc = halfB * halfB - a * c;
                if (disc >= 0.0f)
                {
                    float s = MathF.Sqrt(disc);
                    float invA = 1.0f / a;
                    float t1 = (-halfB - s) * invA;
                    if (t1 > tMin && t1 < tMax)
                    {
                        float y1 = oy + t1 * dy;
                        if (y1 >= YMin && y1 <= YMax)
                        {
                            hitT = t1;
                            float nx = (ox + t1 * dx) / Radius;
                            float nz = (oz + t1 * dz) / Radius;
                            hitN = new Vec3(nx, 0.0f, nz);
                            hit = true;
                        }
                    }
                    if (!hit)
                    {
                        float t2 = (-halfB + s) * invA;
                        if (t2 > tMin && t2 < tMax)
                        {
                            float y2 = oy + t2 * dy;
                            if (y2 >= YMin && y2 <= YMax)
                            {
                                hitT = t2;
                                float nx = (ox + t2 * dx) / Radius;
                                float nz = (oz + t2 * dz) / Radius;
                                hitN = new Vec3(nx, 0.0f, nz);
                                hit = true;
                            }
                        }
                    }
                }
            }
            if (Capped && MathF.Abs(dy) > 1e-8f)
            {
                float tTop = (YMax - oy) / dy;
                if (tTop > tMin && tTop < tMax)
                {
                    float rx = ox + tTop * dx;
                    float rz = oz + tTop * dz;
                    if (rx * rx + rz * rz <= radius2)
                    {
                        if (tTop < hitT)
                        {
                            hitT = tTop;
                            hitN = new Vec3(0.0f, 1.0f, 0.0f);
                            hit = true;
                        }
                    }
                }
                float tBot = (YMin - oy) / dy;
                if (tBot > tMin && tBot < tMax)
                {
                    float rx = ox + tBot * dx;
                    float rz = oz + tBot * dz;
                    if (rx * rx + rz * rz <= radius2)
                    {
                        if (tBot < hitT)
                        {
                            hitT = tBot;
                            hitN = new Vec3(0.0f, -1.0f, 0.0f);
                            hit = true;
                        }
                    }
                }
            }
            if (!hit)
            {
                return false;
            }
            float px = r.Origin.X + hitT * dx;
            float py = r.Origin.Y + hitT * dy;
            float pz = r.Origin.Z + hitT * dz;
            rec.T = hitT;
            rec.P = new Vec3(px, py, pz);
            rec.N = hitN.Dot(r.Dir) < 0.0f ? hitN : -hitN;
            rec.Mat = Mat;
            rec.U = 0.0f;
            rec.V = 0.0f;
            return true;
        }
    }
}
