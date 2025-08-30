// File: MeshAndMeshLoader.cs
using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using ConsoleGame.RayTracing.Objects;

namespace ConsoleGame.RayTracing
{
    public static class MeshLoader
    {
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
                NormalizeAllUsedVertices(ref pos, faces, targetSize);
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

        private static void NormalizeAllUsedVertices(ref Vec3[] pos, List<(int a, int b, int c)> faces, float targetSize)
        {
            HashSet<int> used = new HashSet<int>();
            for (int i = 0; i < faces.Count; i++)
            {
                used.Add(faces[i].a);
                used.Add(faces[i].b);
                used.Add(faces[i].c);
            }

            float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;

            foreach (int vi in used)
            {
                Vec3 p = pos[vi];
                if (p.X < minX) minX = p.X; if (p.Y < minY) minY = p.Y; if (p.Z < minZ) minZ = p.Z;
                if (p.X > maxX) maxX = p.X; if (p.Y > maxY) maxY = p.Y; if (p.Z > maxZ) maxZ = p.Z;
            }

            if (float.IsInfinity(minX) || float.IsInfinity(minY) || float.IsInfinity(minZ) || float.IsInfinity(maxX) || float.IsInfinity(maxY) || float.IsInfinity(maxZ)) return;

            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;
            float cz = (minZ + maxZ) * 0.5f;

            float rx = maxX - minX;
            float ry = maxY - minY;
            float rz = maxZ - minZ;
            float maxExtent = rx; if (ry > maxExtent) maxExtent = ry; if (rz > maxExtent) maxExtent = rz;
            if (maxExtent <= 0.0f) maxExtent = 1.0f;

            float s = targetSize / maxExtent;

            for (int i = 0; i < pos.Length; i++)
            {
                float x = (pos[i].X - cx) * s;
                float y = (pos[i].Y - cy) * s;
                float z = (pos[i].Z - cz) * s;
                pos[i] = new Vec3(x, y, z);
            }
        }
    }
}
