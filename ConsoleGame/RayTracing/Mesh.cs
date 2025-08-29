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

        private readonly Hittable bvh;

        private Mesh(List<Triangle> triangles, Vec3 min, Vec3 max)
        {
            BoundsMin = min;
            BoundsMax = max;
            bvh = new MeshBVH(triangles);
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
            List<(int a, int b, int c)> faces = new List<(int, int, int)>(1 << 17);

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
                }
            }

            if (positions.Count == 0 || faces.Count == 0) throw new InvalidDataException("OBJ had no triangles.");

            Vec3[] pos = positions.ToArray();

            if (normalize)
            {
                NormalizeAndPruneLargestComponent(ref pos, ref faces, targetSize);
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

        private static void NormalizeAndPruneLargestComponent(ref Vec3[] pos, ref List<(int a, int b, int c)> faces, float targetSize)
        {
            int vCount = pos.Length;
            int fCount = faces.Count;

            int[] parent = new int[vCount];
            int[] rank = new int[vCount];
            for (int i = 0; i < vCount; i++) { parent[i] = i; rank[i] = 0; }

            int Find(int x) { while (x != parent[x]) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int x, int y) { int rx = Find(x), ry = Find(y); if (rx == ry) return; if (rank[rx] < rank[ry]) parent[rx] = ry; else if (rank[rx] > rank[ry]) parent[ry] = rx; else { parent[ry] = rx; rank[rx]++; } }

            for (int i = 0; i < fCount; i++)
            {
                var (a, b, c) = faces[i];
                Union(a, b); Union(b, c);
            }

            Dictionary<int, List<int>> compToFaces = new Dictionary<int, List<int>>(128);
            for (int i = 0; i < fCount; i++)
            {
                int ra = Find(faces[i].a);
                if (!compToFaces.TryGetValue(ra, out var list)) { list = new List<int>(64); compToFaces[ra] = list; }
                list.Add(i);
            }

            int bestRoot = -1;
            int bestCount = -1;
            foreach (var kv in compToFaces)
            {
                int cnt = kv.Value.Count;
                if (cnt > bestCount) { bestCount = cnt; bestRoot = kv.Key; }
            }

            if (bestRoot == -1) return;

            HashSet<int> usedVerts = new HashSet<int>();
            List<(int a, int b, int c)> newFaces = new List<(int a, int b, int c)>(bestCount);
            var faceIdx = compToFaces[bestRoot];
            for (int i = 0; i < faceIdx.Count; i++)
            {
                var f = faces[faceIdx[i]];
                newFaces.Add(f);
                usedVerts.Add(f.a); usedVerts.Add(f.b); usedVerts.Add(f.c);
            }

            Dictionary<int, int> remap = new Dictionary<int, int>(usedVerts.Count);
            Vec3[] newPos = new Vec3[usedVerts.Count];
            int cursor = 0;
            foreach (int ov in usedVerts)
            {
                remap[ov] = cursor;
                newPos[cursor] = pos[ov];
                cursor++;
            }

            for (int i = 0; i < newFaces.Count; i++)
            {
                var f = newFaces[i];
                newFaces[i] = (remap[f.a], remap[f.b], remap[f.c]);
            }

            pos = newPos;
            faces = newFaces;

            float cx = 0.0f, cy = 0.0f, cz = 0.0f;
            int triCount = faces.Count;
            for (int i = 0; i < triCount; i++)
            {
                Vec3 A = pos[faces[i].a];
                Vec3 B = pos[faces[i].b];
                Vec3 C = pos[faces[i].c];
                cx += (A.X + B.X + C.X) * (1.0f / 3.0f);
                cy += (A.Y + B.Y + C.Y) * (1.0f / 3.0f);
                cz += (A.Z + B.Z + C.Z) * (1.0f / 3.0f);
            }
            float invT = triCount > 0 ? 1.0f / triCount : 1.0f / MathF.Max(1, pos.Length);
            cx *= invT; cy *= invT; cz *= invT;

            float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;

            for (int i = 0; i < pos.Length; i++)
            {
                float x = pos[i].X - cx;
                float y = pos[i].Y - cy;
                float z = pos[i].Z - cz;
                pos[i] = new Vec3(x, y, z);
                if (x < minX) minX = x; if (y < minY) minY = y; if (z < minZ) minZ = z;
                if (x > maxX) maxX = x; if (y > maxY) maxY = y; if (z > maxZ) maxZ = z;
            }

            float rx = maxX - minX;
            float ry = maxY - minY;
            float rz = maxZ - minZ;
            float maxExtent = rx; if (ry > maxExtent) maxExtent = ry; if (rz > maxExtent) maxExtent = rz;
            if (maxExtent <= 0.0f) maxExtent = 1.0f;

            float s = targetSize / maxExtent;
            for (int i = 0; i < pos.Length; i++)
            {
                pos[i] = new Vec3(pos[i].X * s, pos[i].Y * s, pos[i].Z * s);
            }
        }

        public override bool TryGetBounds(out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ, out float cx, out float cy, out float cz)
        {
            return bvh.TryGetBounds(out minX, out minY, out minZ, out maxX, out maxY, out maxZ, out cx, out cy, out cz);
        }
    }
}
