using System;
using System.Collections.Generic;
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

        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            Vec3 oc = r.Origin - Center;
            float a = r.Dir.Dot(r.Dir);
            float b = 2.0f * oc.Dot(r.Dir);
            float c = oc.Dot(oc) - Radius * Radius;
            float disc = b * b - 4.0f * a * c;
            if (disc < 0.0f)
            {
                return false;
            }
            float s = MathF.Sqrt(disc);
            float t = (-b - s) / (2.0f * a);
            if (t < tMin || t > tMax)
            {
                t = (-b + s) / (2.0f * a);
                if (t < tMin || t > tMax)
                {
                    return false;
                }
            }
            rec.T = t;
            rec.P = r.At(t);
            rec.N = (rec.P - Center) / Radius;
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

        public Plane(Vec3 p, Vec3 n, Func<Vec3, Vec3, float, Material> matFunc, float specular, float reflectivity)
        {
            Point = p;
            Normal = n.Normalized();
            MaterialFunc = matFunc;
            Specular = specular;
            Reflectivity = reflectivity;
        }

        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            float denom = Normal.Dot(r.Dir);
            if (MathF.Abs(denom) < 1e-6)
            {
                return false;
            }
            float t = (Point - r.Origin).Dot(Normal) / denom;
            if (t < tMin || t > tMax)
            {
                return false;
            }
            rec.T = t;
            rec.P = r.At(t);
            rec.N = denom < 0.0f ? Normal : -Normal;
            Material baseMat = MaterialFunc(rec.P, rec.N, 0.0f);
            rec.Mat = new Material(baseMat.Albedo, Specular, Reflectivity, baseMat.Emission);
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

        public Disk(Vec3 center, Vec3 normal, float radius, Func<Vec3, Vec3, float, Material> matFunc, float specular, float reflectivity)
        {
            Center = center;
            Normal = normal.Normalized();
            Radius = radius;
            MaterialFunc = matFunc;
            Specular = specular;
            Reflectivity = reflectivity;
        }

        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            float denom = Normal.Dot(r.Dir);
            if (MathF.Abs(denom) < 1e-6)
            {
                return false;
            }
            float t = (Center - r.Origin).Dot(Normal) / denom;
            if (t < tMin || t > tMax)
            {
                return false;
            }
            Vec3 p = r.At(t);
            Vec3 d = p - Center;
            if (d.Dot(d) > Radius * Radius)
            {
                return false;
            }
            rec.T = t;
            rec.P = p;
            rec.N = denom < 0.0f ? Normal : -Normal;
            Material baseMat = MaterialFunc(rec.P, rec.N, 0.0f);
            rec.Mat = new Material(baseMat.Albedo, Specular, Reflectivity, baseMat.Emission);
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
        }

        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            if (MathF.Abs(r.Dir.Z) < 1e-8)
            {
                return false;
            }
            float t = (Z - r.Origin.Z) / r.Dir.Z;
            if (t < tMin || t > tMax)
            {
                return false;
            }
            float x = r.Origin.X + t * r.Dir.X;
            float y = r.Origin.Y + t * r.Dir.Y;
            if (x < X0 || x > X1 || y < Y0 || y > Y1)
            {
                return false;
            }
            rec.T = t;
            rec.P = r.At(t);
            Vec3 n = new Vec3(0.0f, 0.0f, 1.0f);
            rec.N = r.Dir.Z < 0.0f ? n : -n;
            Material baseMat = MaterialFunc(rec.P, rec.N, 0.0f);
            rec.Mat = new Material(baseMat.Albedo, Specular, Reflectivity, baseMat.Emission);
            rec.U = (x - X0) / (X1 - X0);
            rec.V = (y - Y0) / (Y1 - Y0);
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
        }

        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            if (MathF.Abs(r.Dir.Y) < 1e-8)
            {
                return false;
            }
            float t = (Y - r.Origin.Y) / r.Dir.Y;
            if (t < tMin || t > tMax)
            {
                return false;
            }
            float x = r.Origin.X + t * r.Dir.X;
            float z = r.Origin.Z + t * r.Dir.Z;
            if (x < X0 || x > X1 || z < Z0 || z > Z1)
            {
                return false;
            }
            rec.T = t;
            rec.P = r.At(t);
            Vec3 n = new Vec3(0.0f, 1.0f, 0.0f);
            rec.N = r.Dir.Y < 0.0f ? n : -n;
            Material baseMat = MaterialFunc(rec.P, rec.N, 0.0f);
            rec.Mat = new Material(baseMat.Albedo, Specular, Reflectivity, baseMat.Emission);
            rec.U = (x - X0) / (X1 - X0);
            rec.V = (z - Z0) / (Z1 - Z0);
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
        }

        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            if (MathF.Abs(r.Dir.X) < 1e-8)
            {
                return false;
            }
            float t = (X - r.Origin.X) / r.Dir.X;
            if (t < tMin || t > tMax)
            {
                return false;
            }
            float y = r.Origin.Y + t * r.Dir.Y;
            float z = r.Origin.Z + t * r.Dir.Z;
            if (y < Y0 || y > Y1 || z < Z0 || z > Z1)
            {
                return false;
            }
            rec.T = t;
            rec.P = r.At(t);
            Vec3 n = new Vec3(1.0f, 0.0f, 0.0f);
            rec.N = r.Dir.X < 0.0f ? n : -n;
            Material baseMat = MaterialFunc(rec.P, rec.N, 0.0f);
            rec.Mat = new Material(baseMat.Albedo, Specular, Reflectivity, baseMat.Emission);
            rec.U = (y - Y0) / (Y1 - Y0);
            rec.V = (z - Z0) / (Z1 - Z0);
            return true;
        }
    }

    public sealed class Box : Hittable
    {
        public Vec3 Min;
        public Vec3 Max;
        private List<Hittable> faces;

        public Box(Vec3 min, Vec3 max, Func<Vec3, Vec3, float, Material> matFunc, float specular, float reflectivity)
        {
            Min = min;
            Max = max;
            faces = new List<Hittable>(6);
            faces.Add(new XYRect(min.X, max.X, min.Y, max.Y, max.Z, matFunc, specular, reflectivity));
            faces.Add(new XYRect(min.X, max.X, min.Y, max.Y, min.Z, matFunc, specular, reflectivity));
            faces.Add(new XZRect(min.X, max.X, min.Z, max.Z, max.Y, matFunc, specular, reflectivity));
            faces.Add(new XZRect(min.X, max.X, min.Z, max.Z, min.Y, matFunc, specular, reflectivity));
            faces.Add(new YZRect(min.Y, max.Y, min.Z, max.Z, max.X, matFunc, specular, reflectivity));
            faces.Add(new YZRect(min.Y, max.Y, min.Z, max.Z, min.X, matFunc, specular, reflectivity));
        }

        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            bool hitAnything = false;
            float closest = tMax;
            HitRecord temp = default;
            for (int i = 0; i < faces.Count; i++)
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

        public CylinderY(Vec3 center, float radius, float yMin, float yMax, bool capped, Material mat)
        {
            Center = center;
            Radius = radius;
            YMin = MathF.Min(yMin, yMax);
            YMax = MathF.Max(yMin, yMax);
            Capped = capped;
            Mat = mat;
        }

        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            float ox = r.Origin.X - Center.X;
            float oz = r.Origin.Z - Center.Z;
            float dx = r.Dir.X;
            float dz = r.Dir.Z;
            float a = dx * dx + dz * dz;
            float hitT = float.MaxValue;
            Vec3 hitN = Vec3.Zero;
            bool hit = false;
            if (a > 1e-12)
            {
                float b = 2.0f * (ox * dx + oz * dz);
                float c = ox * ox + oz * oz - Radius * Radius;
                float disc = b * b - 4.0f * a * c;
                if (disc >= 0.0f)
                {
                    float s = MathF.Sqrt(disc);
                    float t1 = (-b - s) / (2.0f * a);
                    float t2 = (-b + s) / (2.0f * a);
                    if (t1 > tMin && t1 < tMax)
                    {
                        float y = r.Origin.Y + t1 * r.Dir.Y;
                        if (y >= YMin && y <= YMax)
                        {
                            hitT = t1;
                            Vec3 p = r.At(t1);
                            hitN = new Vec3((p.X - Center.X) / Radius, 0.0f, (p.Z - Center.Z) / Radius).Normalized();
                            hit = true;
                        }
                    }
                    if (!hit && t2 > tMin && t2 < tMax)
                    {
                        float y = r.Origin.Y + t2 * r.Dir.Y;
                        if (y >= YMin && y <= YMax)
                        {
                            hitT = t2;
                            Vec3 p = r.At(t2);
                            hitN = new Vec3((p.X - Center.X) / Radius, 0.0f, (p.Z - Center.Z) / Radius).Normalized();
                            hit = true;
                        }
                    }
                }
            }
            if (Capped)
            {
                if (MathF.Abs(r.Dir.Y) > 1e-8)
                {
                    float tTop = (YMax - r.Origin.Y) / r.Dir.Y;
                    if (tTop > tMin && tTop < tMax)
                    {
                        Vec3 p = r.At(tTop);
                        float dxp = p.X - Center.X;
                        float dzp = p.Z - Center.Z;
                        if (dxp * dxp + dzp * dzp <= Radius * Radius)
                        {
                            if (tTop < hitT)
                            {
                                hitT = tTop;
                                hitN = new Vec3(0.0f, 1.0f, 0.0f);
                                hit = true;
                            }
                        }
                    }
                    float tBot = (YMin - r.Origin.Y) / r.Dir.Y;
                    if (tBot > tMin && tBot < tMax)
                    {
                        Vec3 p = r.At(tBot);
                        float dxp = p.X - Center.X;
                        float dzp = p.Z - Center.Z;
                        if (dxp * dxp + dzp * dzp <= Radius * Radius)
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
            }
            if (!hit)
            {
                return false;
            }
            rec.T = hitT;
            rec.P = r.At(hitT);
            rec.N = hitN.Dot(r.Dir) < 0.0f ? hitN : -hitN;
            rec.Mat = Mat;
            rec.U = 0.0f;
            rec.V = 0.0f;
            return true;
        }
    }
}
