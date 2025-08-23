using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ConsoleGame.RayTracing;

namespace ConsoleGame.RayTracing.Objects
{
    public abstract class Hittable
    {
        public abstract bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV);
    }

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

    public sealed class Plane : Hittable
    {
        public Vec3 Point;
        public Vec3 Normal;
        public Func<Vec3, Vec3, float, Material> MaterialFunc;
        public float Specular;
        public float Reflectivity;
        private readonly float ndotPoint;

        public Plane(Vec3 p, Vec3 n, Func<Vec3, Vec3, float, Material> matFunc, float specular, float reflectivity)
        {
            Point = p;
            Normal = n.Normalized();
            MaterialFunc = matFunc;
            Specular = specular;
            Reflectivity = reflectivity;
            ndotPoint = Normal.Dot(Point);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            float denom = Normal.Dot(r.Dir);
            if (MathF.Abs(denom) < 1e-6f)
            {
                return false;
            }
            float t = (ndotPoint - Normal.Dot(r.Origin)) / denom;
            if (t < tMin || t > tMax)
            {
                return false;
            }
            float px = r.Origin.X + t * r.Dir.X;
            float py = r.Origin.Y + t * r.Dir.Y;
            float pz = r.Origin.Z + t * r.Dir.Z;
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
        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            float denom = Normal.Dot(r.Dir);
            if (MathF.Abs(denom) < 1e-6f)
            {
                return false;
            }
            float t = (ndotCenter - Normal.Dot(r.Origin)) / denom;
            if (t < tMin || t > tMax)
            {
                return false;
            }
            float px = r.Origin.X + t * r.Dir.X;
            float pz = r.Origin.Z + t * r.Dir.Z;
            float dx = px - Center.X;
            float dz = pz - Center.Z;
            if (dx * dx + dz * dz > radius2)
            {
                return false;
            }
            rec.T = t;
            rec.P = new Vec3(px, r.Origin.Y + t * r.Dir.Y, pz);
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
        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            float dirZ = r.Dir.Z;
            if (MathF.Abs(dirZ) < 1e-8f)
            {
                return false;
            }
            float t = (Z - r.Origin.Z) / dirZ;
            if (t < tMin || t > tMax)
            {
                return false;
            }
            float px = r.Origin.X + t * r.Dir.X;
            float py = r.Origin.Y + t * r.Dir.Y;
            if (px < X0 || px > X1 || py < Y0 || py > Y1)
            {
                return false;
            }
            rec.T = t;
            rec.P = new Vec3(px, py, Z);
            rec.N = dirZ < 0.0f ? NzPos : NzNeg;
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
        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            float dirY = r.Dir.Y;
            if (MathF.Abs(dirY) < 1e-8f)
            {
                return false;
            }
            float t = (Y - r.Origin.Y) / dirY;
            if (t < tMin || t > tMax)
            {
                return false;
            }
            float px = r.Origin.X + t * r.Dir.X;
            float pz = r.Origin.Z + t * r.Dir.Z;
            if (px < X0 || px > X1 || pz < Z0 || pz > Z1)
            {
                return false;
            }
            rec.T = t;
            rec.P = new Vec3(px, Y, pz);
            rec.N = dirY < 0.0f ? NyPos : NyNeg;
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
        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            float dirX = r.Dir.X;
            if (MathF.Abs(dirX) < 1e-8f)
            {
                return false;
            }
            float t = (X - r.Origin.X) / dirX;
            if (t < tMin || t > tMax)
            {
                return false;
            }
            float py = r.Origin.Y + t * r.Dir.Y;
            float pz = r.Origin.Z + t * r.Dir.Z;
            if (py < Y0 || py > Y1 || pz < Z0 || pz > Z1)
            {
                return false;
            }
            rec.T = t;
            rec.P = new Vec3(X, py, pz);
            rec.N = dirX < 0.0f ? NxPos : NxNeg;
            Material baseMat = MaterialFunc(rec.P, rec.N, 0.0f);
            baseMat.Specular = Specular;
            baseMat.Reflectivity = Reflectivity;
            rec.Mat = baseMat;
            rec.U = (py - Y0) * invYSpan;
            rec.V = (pz - Z0) * invZSpan;
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
