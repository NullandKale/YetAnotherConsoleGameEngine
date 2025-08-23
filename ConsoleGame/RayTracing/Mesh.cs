// File: ObjMesh.cs
using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using ConsoleGame.RayTracing.Objects;

namespace ConsoleGame.RayTracing
{
    public sealed class Mesh : Hittable
    {
        public readonly Vec3 BoundsMin;
        public readonly Vec3 BoundsMax;

        private readonly Hittable bvh; // local BVH over triangles

        private Mesh(List<Triangle> triangles, Vec3 min, Vec3 max)
        {
            BoundsMin = min;
            BoundsMax = max;
            bvh = new MeshBVH(triangles);
            //bvh = new BVH(triangles);
        }

        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            return bvh.Hit(r, tMin, tMax, ref rec, screenU, screenV);
        }

        public static Mesh FromObj(string path, Material defaultMaterial, float scale = 1.0f, Vec3? translate = null, bool normalize = true, float targetSize = 1.0f)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path");
            if (!File.Exists(path)) throw new FileNotFoundException("OBJ not found", path);

            Vec3 t = translate ?? new Vec3(0.0f, 0.0f, 0.0f);
            CultureInfo ci = CultureInfo.InvariantCulture;

            List<Vec3> positions = new List<Vec3>(1 << 16);
            List<Vec3> normals = new List<Vec3>(1 << 15); // optional, we don't require them
            List<(float u, float v)> uvs = new List<(float, float)>(1 << 15); // optional
            List<(int a, int b, int c)> faces = new List<(int, int, int)>(1 << 17);

            bool haveAny = false;

            using (var sr = new StreamReader(path))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] tok = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (tok.Length == 0) continue;

                    if (tok[0] == "v" && tok.Length >= 4)
                    {
                        float x = float.Parse(tok[1], ci);
                        float y = float.Parse(tok[2], ci);
                        float z = float.Parse(tok[3], ci);
                        positions.Add(new Vec3(x, y, z));
                        if (!haveAny) haveAny = true;
                    }
                    else if (tok[0] == "vn" && tok.Length >= 4)
                    {
                        float x = float.Parse(tok[1], ci);
                        float y = float.Parse(tok[2], ci);
                        float z = float.Parse(tok[3], ci);
                        normals.Add(new Vec3(x, y, z).Normalized());
                    }
                    else if (tok[0] == "vt" && tok.Length >= 3)
                    {
                        float u = float.Parse(tok[1], ci);
                        float v = float.Parse(tok[2], ci);
                        uvs.Add((u, v));
                    }
                    else if (tok[0] == "f" && tok.Length >= 4)
                    {
                        int faceVerts = tok.Length - 1;
                        int[] vIdx = new int[faceVerts];

                        for (int i = 0; i < faceVerts; i++)
                        {
                            string[] parts = tok[i + 1].Split('/');
                            int vi = ParseIndex(parts[0], positions.Count);
                            vIdx[i] = vi;
                        }

                        for (int i = 2; i < faceVerts; i++)
                        {
                            faces.Add((vIdx[0], vIdx[i - 1], vIdx[i]));
                        }
                    }
                    // ignore: mtllib/usemtl/o/g/s — keep loader minimal and robust
                }
            }

            if (!haveAny || faces.Count == 0) throw new InvalidDataException("OBJ had no triangles.");

            Vec3[] pos = positions.ToArray();

            if (normalize)
            {
                NormalizeToCanonical(ref pos, targetSize);
            }

            if (scale != 1.0f || t.X != 0.0f || t.Y != 0.0f || t.Z != 0.0f)
            {
                for (int i = 0; i < pos.Length; i++)
                {
                    pos[i] = new Vec3(pos[i].X * scale + t.X, pos[i].Y * scale + t.Y, pos[i].Z * scale + t.Z);
                }
            }

            float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;

            List<Triangle> tris = new List<Triangle>(faces.Count);
            for (int i = 0; i < faces.Count; i++)
            {
                Vec3 a = pos[faces[i].a];
                Vec3 b = pos[faces[i].b];
                Vec3 c = pos[faces[i].c];
                tris.Add(new Triangle(a, b, c, defaultMaterial));

                if (a.X < minX) minX = a.X; if (a.Y < minY) minY = a.Y; if (a.Z < minZ) minZ = a.Z;
                if (b.X < minX) minX = b.X; if (b.Y < minY) minY = b.Y; if (b.Z < minZ) minZ = b.Z;
                if (c.X < minX) minX = c.X; if (c.Y < minY) minY = c.Y; if (c.Z < minZ) minZ = c.Z;

                if (a.X > maxX) maxX = a.X; if (a.Y > maxY) maxY = a.Y; if (a.Z > maxZ) maxZ = a.Z;
                if (b.X > maxX) maxX = b.X; if (b.Y > maxY) maxY = b.Y; if (b.Z > maxZ) maxZ = b.Z;
                if (c.X > maxX) maxX = c.X; if (c.Y > maxY) maxY = c.Y; if (c.Z > maxZ) maxZ = c.Z;
            }

            Vec3 mn = new Vec3(minX, minY, minZ);
            Vec3 mx = new Vec3(maxX, maxY, maxZ);
            return new Mesh(tris, mn, mx);
        }

        private static int ParseIndex(string token, int count)
        {
            if (string.IsNullOrEmpty(token)) return 0;
            int idx = int.Parse(token, CultureInfo.InvariantCulture);
            if (idx > 0) return idx - 1;
            return count + idx;
        }

        private static void NormalizeToCanonical(ref Vec3[] pos, float targetSize)
        {
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;

            for (int i = 0; i < pos.Length; i++)
            {
                Vec3 p = pos[i];
                if (p.X < minX) minX = p.X; if (p.Y < minY) minY = p.Y; if (p.Z < minZ) minZ = p.Z;
                if (p.X > maxX) maxX = p.X; if (p.Y > maxY) maxY = p.Y; if (p.Z > maxZ) maxZ = p.Z;
            }

            float sx = maxX - minX;
            float sy = maxY - minY;
            float sz = maxZ - minZ;

            int upAxis = 1;
            if (sz >= sy && sz >= sx) upAxis = 2;
            else if (sx >= sy && sx >= sz) upAxis = 0;

            for (int i = 0; i < pos.Length; i++)
            {
                pos[i] = RotateToYUp(pos[i], upAxis);
            }

            float rMinX = float.PositiveInfinity, rMinY = float.PositiveInfinity, rMinZ = float.PositiveInfinity;
            float rMaxX = float.NegativeInfinity, rMaxY = float.NegativeInfinity, rMaxZ = float.NegativeInfinity;

            for (int i = 0; i < pos.Length; i++)
            {
                Vec3 p = pos[i];
                if (p.X < rMinX) rMinX = p.X; if (p.Y < rMinY) rMinY = p.Y; if (p.Z < rMinZ) rMinZ = p.Z;
                if (p.X > rMaxX) rMaxX = p.X; if (p.Y > rMaxY) rMaxY = p.Y; if (p.Z > rMaxZ) rMaxZ = p.Z;
            }

            float rx = rMaxX - rMinX;
            float ry = rMaxY - rMinY;
            float rz = rMaxZ - rMinZ;
            float maxExtent = rx;
            if (ry > maxExtent) maxExtent = ry;
            if (rz > maxExtent) maxExtent = rz;
            if (maxExtent <= 0.0f) maxExtent = 1.0f;

            float s = targetSize / maxExtent;

            float cx = (rMinX + rMaxX) * 0.5f;
            float cy = (rMinY + rMaxY) * 0.5f;
            float cz = (rMinZ + rMaxZ) * 0.5f;

            for (int i = 0; i < pos.Length; i++)
            {
                float x = (pos[i].X - cx) * s;
                float y = (pos[i].Y - cy) * s;
                float z = (pos[i].Z - cz) * s;
                pos[i] = new Vec3(x, y, z);
            }
        }

        private static Vec3 RotateToYUp(Vec3 p, int upAxis)
        {
            if (upAxis == 1)
            {
                return p;
            }
            if (upAxis == 2)
            {
                float x = p.X;
                float y = p.Z;
                float z = -p.Y;
                return new Vec3(x, y, z);
            }
            float nx = -p.Y;
            float ny = p.X;
            float nz = p.Z;
            return new Vec3(nx, ny, nz);
        }
    }
}
