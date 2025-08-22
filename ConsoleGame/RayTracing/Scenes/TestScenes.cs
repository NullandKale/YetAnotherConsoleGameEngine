// File: TestScenes.cs
using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using ConsoleRayTracing;
using ConsoleGame.RayTracing.Objects;

namespace ConsoleGame.RayTracing.Scenes
{
    public static class TestScenes
    {
        public static Scene BuildTestScene()
        {
            Console.WriteLine("[TestScene] Starting build...");
            Stopwatch totalSw = Stopwatch.StartNew();

            Scene s = new Scene();
            s.Ambient = new AmbientLight(new Vec3(1, 1, 1), 0.06f);

            Console.WriteLine("[TestScene] Adding global floor...");
            Func<Vec3, Vec3, float, Material> floor = Checker(new Vec3(0.82f, 0.82f, 0.85f), new Vec3(0.12f, 0.12f, 0.12f), 0.8f);
            s.Objects.Add(new Plane(new Vec3(0.0f, 0.0f, 0.0f), new Vec3(0.0f, 1.0f, 0.0f), floor, 0.02f, 0.00f));

            Console.WriteLine("[TestScene] Preparing materials...");
            Material mirror = new Material(new Vec3(0.98, 0.98, 0.98), 0.0, 0.90, Vec3.Zero);
            Material red = new Material(new Vec3(0.95, 0.15, 0.15), 0.08, 0.02, Vec3.Zero);
            Material green = new Material(new Vec3(0.15, 0.95, 0.20), 0.06, 0.02, Vec3.Zero);
            Material blue = new Material(new Vec3(0.15, 0.25, 0.95), 0.06, 0.02, Vec3.Zero);
            Material gold = new Material(new Vec3(1.00, 0.85, 0.57), 0.25, 0.10, Vec3.Zero);
            Material brass = new Material(new Vec3(0.78, 0.60, 0.20), 0.18, 0.06, Vec3.Zero);
            Material pedestal = new Material(new Vec3(0.85, 0.85, 0.85), 0.00, 0.00, Vec3.Zero);
            Material glassClear = new Material(new Vec3(1.0, 1.0, 1.0), 0.0, 0.02, Vec3.Zero, 1.0, 1.5, new Vec3(1.0, 1.0, 1.0));
            Material glassBlue = new Material(new Vec3(0.9, 0.95, 1.0), 0.0, 0.02, Vec3.Zero, 1.0, 1.52, new Vec3(0.9, 0.95, 1.0));
            Material emissiveSoft = new Material(new Vec3(0.0, 0.0, 0.0), 0.0, 0.0, new Vec3(4.0, 4.0, 4.0));

            Console.WriteLine("[TestScene] Building Cornell box corner...");
            {
                float xL = -9.0f;
                float xR = -3.0f;
                float yB = 0.0f;
                float yT = 5.0f;
                float zF = -0.5f;
                float zB = -6.5f;

                Func<Vec3, Vec3, float, Material> white = Solid(new Vec3(0.82, 0.82, 0.82));
                Func<Vec3, Vec3, float, Material> redWall = Solid(new Vec3(0.80, 0.10, 0.10));
                Func<Vec3, Vec3, float, Material> greenWall = Solid(new Vec3(0.10, 0.80, 0.10));

                s.Objects.Add(new YZRect(yB, yT, zB, zF, xL, redWall, 0.0f, 0.0f));
                s.Objects.Add(new YZRect(yB, yT, zB, zF, xR, greenWall, 0.0f, 0.0f));
                s.Objects.Add(new XZRect(xL, xR, zB, zF, yB, white, 0.0f, 0.0f));
                s.Objects.Add(new XZRect(xL, xR, zB, zF, yT, white, 0.0f, 0.0f));
                s.Objects.Add(new XYRect(xL, xR, yB, yT, zB, white, 0.0f, 0.0f));

                float lx0 = -8.0f;
                float lx1 = -4.8f;
                float lz0 = -4.8f;
                float lz1 = -3.6f;
                float ly = yT - 0.01f;
                s.Objects.Add(new XZRect(lx0, lx1, lz0, lz1, ly, (p, n, u) => emissiveSoft, 0.0f, 0.0f));
                s.Lights.Add(new PointLight(new Vec3(-6.2f, 4.6f, -4.2f), new Vec3(1.0f, 0.95f, 0.9f), 75.0f));
            }
            Console.WriteLine("[TestScene] Cornell box corner added.");

            Console.WriteLine("[TestScene] Building Volume Grid section...");
            {
                Stopwatch vSw = Stopwatch.StartNew();
                int nx = 16;
                int ny = 8;
                int nz = 16;
                (int matId, int metaId)[,,] cells = new (int, int)[nx, ny, nz];

                for (int x = 0; x < nx; x++)
                {
                    for (int z = 0; z < nz; z++)
                    {
                        cells[x, 0, z] = (1, 0);
                    }
                }
                for (int y = 1; y <= 3; y++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        cells[x, y, 0] = (1, 0);
                        cells[x, y, nz - 1] = (1, 0);
                    }
                    for (int z = 0; z < nz; z++)
                    {
                        cells[0, y, z] = (1, 0);
                        cells[nx - 1, y, z] = (1, 0);
                    }
                }
                void Pillar(int cx, int cz, int height, int mat)
                {
                    for (int y = 1; y <= height && y < ny; y++)
                    {
                        cells[cx, y, cz] = (mat, 0);
                    }
                }
                Pillar(4, 4, 4, 2);
                Pillar(11, 4, 3, 3);
                Pillar(4, 11, 5, 4);
                Pillar(11, 11, 4, 5);
                for (int x = 6; x <= 9; x++)
                {
                    for (int z = 6; z <= 9; z++)
                    {
                        bool check = (x + z & 1) == 0;
                        cells[x, 1, z] = (check ? 1 : 4, 0);
                    }
                }
                Vec3 minCorner = new Vec3(3.5, 0.0, -10.0);
                Vec3 voxelSize = new Vec3(0.5, 0.5, 0.5);

                Func<int, int, Material> materialLookup = (id, meta) =>
                {
                    switch (id)
                    {
                        case 1: return new Material(new Vec3(0.82, 0.82, 0.85), 0.0, 0.0, Vec3.Zero);
                        case 2: return red;
                        case 3: return green;
                        case 4: return blue;
                        case 5: return mirror;
                        default: return new Material(new Vec3(0.7, 0.7, 0.7), 0.0, 0.0, Vec3.Zero);
                    }
                };

                //s.Objects.Add(new VolumeGrid(cells, minCorner, voxelSize, materialLookup));

                Vec3 pedC = new Vec3(7.5f, 0.0f, -8.0f);
                s.Objects.Add(new CylinderY(pedC, 0.35f, 0.0f, 1.4f, true, pedestal));
                s.Objects.Add(new Sphere(pedC + new Vec3(0.0f, 1.4f + 0.45f, 0.0f), 0.45f, glassClear));

                s.Lights.Add(new PointLight(new Vec3(7.5f, 3.0f, -9.0f), new Vec3(0.9f, 0.95f, 1.0f), 110.0f));
                vSw.Stop();
                Console.WriteLine("[TestScene] Volume Grid added in {0} ms.", vSw.ElapsedMilliseconds);
            }

            Console.WriteLine("[TestScene] Adding mesh gallery (cow, bunny, teapot, dragon)...");
            {
                TryAddMeshAutoGround(s, @"assets\cow.obj", gold, 0.80f, new Vec3(-2.8f, 0.0f, -8.6f));
                TryAddMeshAutoGround(s, @"assets\stanford-bunny.obj", green, 8.0f, new Vec3(-0.8f, 0.0f, -7.8f));
                TryAddMeshAutoGround(s, @"assets\teapot.obj", red, 0.60f, new Vec3(1.8f, 0.0f, -8.2f));
                TryAddMeshAutoGround(s, @"assets\xyzrgb_dragon.obj", mirror, 0.12f, new Vec3(4.0f, 0.0f, -9.2f));

                s.Objects.Add(new Disk(new Vec3(1.8f, 0.01f, -8.2f), new Vec3(0.0f, 1.0f, 0.0f), 0.9f, Solid(new Vec3(0.85, 0.85, 0.1)), 0.0f, 0.0f));
                s.Lights.Add(new PointLight(new Vec3(0.2f, 4.2f, -8.2f), new Vec3(1.0f, 0.9f, 0.8f), 85.0f));
            }
            Console.WriteLine("[TestScene] Mesh gallery done.");

            Console.WriteLine("[TestScene] Adding pedestal quartet with spheres...");
            {
                float pedH = 1.2f;
                float sphR = 0.35f;
                Vec3 baseC = new Vec3(4.2f, 0.0f, -2.2f);
                Vec3 dx = new Vec3(1.6f, 0.0f, 0.0f);
                Vec3 dz = new Vec3(0.0f, 0.0f, -1.6f);

                Vec3 p0 = baseC;
                Vec3 p1 = baseC + dx;
                Vec3 p2 = baseC + dz;
                Vec3 p3 = baseC + dx + dz;

                s.Objects.Add(new CylinderY(p0, 0.32f, 0.0f, pedH, true, pedestal));
                s.Objects.Add(new CylinderY(p1, 0.32f, 0.0f, pedH, true, pedestal));
                s.Objects.Add(new CylinderY(p2, 0.32f, 0.0f, pedH, true, pedestal));
                s.Objects.Add(new CylinderY(p3, 0.32f, 0.0f, pedH, true, pedestal));

                s.Objects.Add(new Sphere(p0 + new Vec3(0.0f, pedH + sphR, 0.0f), sphR, mirror));
                s.Objects.Add(new Sphere(p1 + new Vec3(0.0f, pedH + sphR, 0.0f), sphR, glassBlue));
                s.Objects.Add(new Sphere(p2 + new Vec3(0.0f, pedH + sphR, 0.0f), sphR, red));
                s.Objects.Add(new Sphere(p3 + new Vec3(0.0f, pedH + sphR, 0.0f), sphR, blue));
            }
            Console.WriteLine("[TestScene] Pedestal quartet added.");

            Console.WriteLine("[TestScene] Adding triangle showcase...");
            {
                Vec3 a = new Vec3(2.2f, 0.0f, -2.0f);
                Vec3 b = new Vec3(2.8f, 1.2f, -2.4f);
                Vec3 c = new Vec3(1.6f, 0.7f, -2.8f);
                s.Objects.Add(new Triangle(a, b, c, brass));
            }
            Console.WriteLine("[TestScene] Triangle added.");

            Console.WriteLine("[TestScene] Generating randomized clutter (BVH stress)...");
            {
                Stopwatch clutterSw = Stopwatch.StartNew();
                Random rng = new Random(1337);
                List<(Vec3 c, float r)> placed = new List<(Vec3 c, float r)>();

                bool CanPlace(Vec3 c, float r)
                {
                    float minSep = 0.06f;
                    for (int i = 0; i < placed.Count; i++)
                    {
                        Vec3 d = c - placed[i].c;
                        float rr = r + placed[i].r + minSep;
                        if (d.Dot(d) < rr * rr)
                        {
                            return false;
                        }
                    }
                    return true;
                }

                Vec3 regionMin = new Vec3(6.0f, 0.0f, -14.0f);
                Vec3 regionMax = new Vec3(12.0f, 0.0f, -10.0f);

                int target = 60;
                int attempts = 0;
                while (placed.Count < target && attempts < target * 40)
                {
                    attempts++;
                    float t = (float)rng.NextDouble();
                    bool makeSphere = t < 0.40f;
                    bool makeCylinder = t >= 0.40f && t < 0.75f;
                    float x = (float)(regionMin.X + rng.NextDouble() * (regionMax.X - regionMin.X));
                    float z = (float)(regionMax.Z + rng.NextDouble() * (regionMin.Z - regionMax.Z));
                    if (makeSphere)
                    {
                        float r = 0.18f + (float)rng.NextDouble() * 0.35f;
                        Vec3 c = new Vec3(x, r, z);
                        if (!CanPlace(c, r))
                        {
                            continue;
                        }
                        Material m = RandomMat(rng);
                        s.Objects.Add(new Sphere(c, r, m));
                        placed.Add((c, r));
                    }
                    else if (makeCylinder)
                    {
                        float rad = 0.16f + (float)rng.NextDouble() * 0.30f;
                        float h = 0.6f + (float)rng.NextDouble() * 1.2f;
                        Vec3 c = new Vec3(x, 0.0f, z);
                        if (!CanPlace(c + new Vec3(0, h * 0.5f, 0), rad))
                        {
                            continue;
                        }
                        Material m = RandomMat(rng);
                        s.Objects.Add(new CylinderY(c, rad, 0.0f, h, true, m));
                        placed.Add((c + new Vec3(0, h * 0.5f, 0), rad));
                    }
                    else
                    {
                        float sx = 0.3f + (float)rng.NextDouble() * 0.6f;
                        float sy = 0.3f + (float)rng.NextDouble() * 0.9f;
                        float sz = 0.3f + (float)rng.NextDouble() * 0.6f;
                        Vec3 mn = new Vec3(x - sx * 0.5f, 0.0f, z - sz * 0.5f);
                        Vec3 mx = new Vec3(x + sx * 0.5f, sy, z + sz * 0.5f);
                        Vec3 center = new Vec3(x, sy * 0.5f, z);
                        float rad = MathF.Sqrt((sx * sx + sy * sy + sz * sz)) * 0.5f;
                        if (!CanPlace(center, rad))
                        {
                            continue;
                        }
                        Material m = RandomMat(rng);
                        s.Objects.Add(new Box(mn, mx, (p, n, u) => new Material(m.Albedo, 0.02, m.Reflectivity, Vec3.Zero), 0.02f, (float)m.Reflectivity));
                        placed.Add((center, rad));
                    }
                    if (placed.Count % 10 == 0)
                    {
                        Console.WriteLine("[TestScene] Randomized clutter progress: {0}/{1} placed...", placed.Count, target);
                    }
                }

                for (int i = 0; i < 6; i++)
                {
                    float cx = (float)(regionMin.X + rng.NextDouble() * (regionMax.X - regionMin.X));
                    float cz = (float)(regionMax.Z + rng.NextDouble() * (regionMin.Z - regionMax.Z));
                    s.Objects.Add(new Disk(new Vec3(cx, 0.01f, cz), new Vec3(0.0f, 1.0f, 0.0f), 0.5f + 0.4f * (float)rng.NextDouble(), Solid(new Vec3(0.85f, 0.85f, 0.18f)), 0.0f, 0.0f));
                }
                clutterSw.Stop();
                Console.WriteLine("[TestScene] Randomized clutter complete: {0} objects, {1} ms.", placed.Count, clutterSw.ElapsedMilliseconds);
            }

            Console.WriteLine("[TestScene] Adding colored spot lights...");
            s.Lights.Add(new PointLight(new Vec3(-6.5f, 3.6f, -2.2f), new Vec3(1.0f, 0.25f, 0.20f), 60.0f));
            s.Lights.Add(new PointLight(new Vec3(3.6f, 4.0f, -3.4f), new Vec3(0.20f, 0.9f, 0.25f), 70.0f));
            s.Lights.Add(new PointLight(new Vec3(6.8f, 4.6f, -7.4f), new Vec3(0.25f, 0.55f, 1.0f), 95.0f));
            s.Lights.Add(new PointLight(new Vec3(0.0f, 6.0f, -6.0f), new Vec3(1.0f, 1.0f, 1.0f), 120.0f));

            s.BackgroundTop = new Vec3(0.06, 0.08, 0.10);
            s.BackgroundBottom = new Vec3(0.01, 0.01, 0.02);

            s.DefaultCameraPos = new Vec3(0.0, 2.4, 2.4);

            Console.WriteLine("[TestScene] Rebuilding BVH...");
            Stopwatch bvhSw = Stopwatch.StartNew();
            s.RebuildBVH();
            bvhSw.Stop();
            totalSw.Stop();
            Console.WriteLine("[TestScene] BVH rebuilt in {0} ms.", bvhSw.ElapsedMilliseconds);
            Console.WriteLine("[TestScene] Build complete in {0} ms.", totalSw.ElapsedMilliseconds);

            return s;

            Material RandomMat(Random rng)
            {
                double p = rng.NextDouble();
                if (p < 0.10) return mirror;
                if (p < 0.20) return glassClear;
                if (p < 0.35) return red;
                if (p < 0.50) return green;
                if (p < 0.65) return blue;
                if (p < 0.80) return gold;
                return new Material(new Vec3(0.70 + 0.30 * rng.NextDouble(), 0.70 + 0.30 * rng.NextDouble(), 0.70 + 0.30 * rng.NextDouble()), 0.05 + 0.20 * rng.NextDouble(), 0.02 * rng.NextDouble(), Vec3.Zero);
            }
        }

        private static void TryAddMeshAutoGround(Scene s, string objPath, Material mat, float scale, Vec3 targetPos)
        {
            Console.WriteLine("[TestScene] Loading mesh: {0}", objPath);
            Stopwatch sw = Stopwatch.StartNew();
            Vec3 mn, mx;
            if (!TryReadObjBounds(objPath, out mn, out mx))
            {
                sw.Stop();
                Console.WriteLine("[TestScene] Mesh not found or empty, skipped: {0} ({1} ms)", objPath, sw.ElapsedMilliseconds);
                return;
            }
            float minY = mn.Y;
            float yTranslate = targetPos.Y - minY * scale + 0.01f;
            Vec3 translate = new Vec3(targetPos.X, yTranslate, targetPos.Z);
            s.Objects.Add(Mesh.FromObj(objPath, mat, scale: scale, translate: translate));
            sw.Stop();
            Console.WriteLine("[TestScene] Mesh added: {0} ({1} ms)", objPath, sw.ElapsedMilliseconds);
        }

        private static bool TryReadObjBounds(string path, out Vec3 min, out Vec3 max)
        {
            Console.WriteLine("[TestScene] Reading OBJ bounds: {0}", path);
            Stopwatch sw = Stopwatch.StartNew();
            min = new Vec3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            max = new Vec3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            if (!File.Exists(path))
            {
                sw.Stop();
                Console.WriteLine("[TestScene] OBJ missing: {0} ({1} ms)", path, sw.ElapsedMilliseconds);
                return false;
            }
            NumberFormatInfo nfi = CultureInfo.InvariantCulture.NumberFormat;
            using (var sr = new StreamReader(path))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.Length < 2) continue;
                    if (line[0] == 'v' && line[1] == ' ')
                    {
                        string[] t = line.Substring(2).Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        if (t.Length < 3) continue;
                        float x = float.Parse(t[0], nfi);
                        float y = float.Parse(t[1], nfi);
                        float z = float.Parse(t[2], nfi);
                        if (x < min.X) min.X = x;
                        if (y < min.Y) min.Y = y;
                        if (z < min.Z) min.Z = z;
                        if (x > max.X) max.X = x;
                        if (y > max.Y) max.Y = y;
                        if (z > max.Z) max.Z = z;
                    }
                }
            }
            sw.Stop();
            if (min.X == float.PositiveInfinity)
            {
                Console.WriteLine("[TestScene] OBJ empty: {0} ({1} ms)", path, sw.ElapsedMilliseconds);
                return false;
            }
            Console.WriteLine("[TestScene] OBJ bounds OK: {0} ({1} ms)", path, sw.ElapsedMilliseconds);
            return true;
        }

        private static Func<Vec3, Vec3, float, Material> Solid(Vec3 albedo)
        {
            return (pos, n, u) => new Material(albedo, 0.0, 0.0, Vec3.Zero);
        }

        private static Func<Vec3, Vec3, float, Material> Checker(Vec3 a, Vec3 b, float scale)
        {
            return (pos, n, u) =>
            {
                int cx = (int)MathF.Floor(pos.X / scale);
                int cz = (int)MathF.Floor(pos.Z / scale);
                bool check = (cx + cz & 1) == 0;
                Vec3 albedo = check ? a : b;
                return new Material(albedo, 0.0, 0.0, Vec3.Zero);
            };
        }
    }
}
