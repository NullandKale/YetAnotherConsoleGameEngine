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
            Console.WriteLine("[Museum] Starting build...");
            Stopwatch totalSw = Stopwatch.StartNew();

            Scene s = new Scene();
            s.Ambient = new AmbientLight(new Vec3(1, 1, 1), 0.06f);

            Console.WriteLine("[Museum] Adding global floor and distant backdrop...");
            Func<Vec3, Vec3, float, Material> floor = Checker(new Vec3(0.82f, 0.82f, 0.85f), new Vec3(0.12f, 0.12f, 0.12f), 0.8f);
            s.Objects.Add(new Plane(new Vec3(0.0f, 0.0f, 0.0f), new Vec3(0.0f, 1.0f, 0.0f), floor, 0.02f, 0.00f));
            s.Objects.Add(new Plane(new Vec3(0.0f, 0.0f, -100.0f), new Vec3(0.0f, 0.0f, 1.0f), (p, n, u) => new Material(new Vec3(0.02, 0.02, 0.03), 0.0, 0.0, Vec3.Zero), 0.0f, 0.0f));

            Console.WriteLine("[Museum] Preparing base materials...");
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

            Console.WriteLine("[Museum] Spacing anchors (no corridor walls)...");
            float cornellW = 6.0f;

            Vec3 cornellAnchorA = new Vec3(-9.0f, 0.0f, -12.0f);
            Vec3 cornellAnchorB = new Vec3(9.0f, 0.0f, -28.0f);
            Vec3 cornellAnchorC = new Vec3(-9.0f, 0.0f, -48.0f);

            Vec3 meshGalleryAnchor = new Vec3(9.0f, 0.0f, -40.0f);
            Vec3 pedestalQuadAnchor = new Vec3(-8.6f, 0.0f, -30.0f);

            Vec3 volumeAnchorA = new Vec3(-9.0f, 0.0f, -72.0f);
            Vec3 volumeAnchorB = new Vec3(9.0f, 0.0f, -88.0f);

            Console.WriteLine("[Museum] Building Cornell rooms...");
            AddCornellBoxRoom(s, cornellAnchorA, cornellW, 5.0f, new Vec3(0.80, 0.10, 0.10), new Vec3(0.10, 0.80, 0.10), new Vec3(0.82, 0.82, 0.82), 65.0f, emissiveSoft);
            AddCornellBoxRoom(s, cornellAnchorB, cornellW, 5.0f, new Vec3(0.70, 0.10, 0.70), new Vec3(0.10, 0.70, 0.70), new Vec3(0.82, 0.82, 0.82), 75.0f, emissiveSoft);
            AddCornellBoxRoom(s, cornellAnchorC, cornellW, 5.0f, new Vec3(0.80, 0.20, 0.10), new Vec3(0.20, 0.80, 0.10), new Vec3(0.90, 0.90, 0.90), 90.0f, emissiveSoft);

            Console.WriteLine("[Museum] Adding mesh gallery (uniform scale=1)...");
            {
                TryAddMeshAutoGround(s, @"assets\cow.obj", gold, 1.0f, meshGalleryAnchor + new Vec3(-2.6f, 0.0f, -0.4f));
                TryAddMeshAutoGround(s, @"assets\stanford-bunny.obj", green, 1.0f, meshGalleryAnchor + new Vec3(0.0f, 0.0f, 0.0f));
                TryAddMeshAutoGround(s, @"assets\teapot.obj", red, 1.0f, meshGalleryAnchor + new Vec3(2.6f, 0.0f, 0.4f));
                TryAddMeshAutoGround(s, @"assets\xyzrgb_dragon.obj", mirror, 1.0f, meshGalleryAnchor + new Vec3(5.2f, 0.0f, -0.8f));
                s.Objects.Add(new Disk(meshGalleryAnchor + new Vec3(2.6f, 0.01f, 0.4f), new Vec3(0.0f, 1.0f, 0.0f), 0.9f, Solid(new Vec3(0.85, 0.85, 0.1)), 0.0f, 0.0f));
            }

            Console.WriteLine("[Museum] Adding pedestal quartet with spheres...");
            {
                float pedH = 1.2f;
                float sphR = 0.35f;
                Vec3 baseC = pedestalQuadAnchor;
                Vec3 dx = new Vec3(1.8f, 0.0f, 0.0f);
                Vec3 dz = new Vec3(0.0f, 0.0f, -1.8f);
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

            Console.WriteLine("[Museum] Adding triangle showcase...");
            {
                Vec3 a = new Vec3(2.2f, 0.0f, -6.0f);
                Vec3 b = new Vec3(2.8f, 1.2f, -6.4f);
                Vec3 c = new Vec3(1.6f, 0.7f, -6.8f);
                s.Objects.Add(new Triangle(a, b, c, brass));
            }

            Console.WriteLine("[Museum] Adding textured demo sphere and endcap wall...");
            {
                string texPath = @"C:\Users\alec\Downloads\IMG_1355.bmp";
                Material textured = new Material(new Vec3(1.0, 1.0, 1.0), 0.05, 0.02, Vec3.Zero);
                textured.DiffuseTexture = new ConsoleGame.Renderer.Texture(texPath);
                textured.TextureWeight = 1.0;
                textured.UVScale = 1.5;
                Vec3 c = new Vec3(-1.6f, 0.6f, -10.0f);
                float r = 0.6f;
                s.Objects.Add(new Sphere(c, r, textured));
                var tex = new ConsoleGame.Renderer.Texture(texPath);
                Material texMat = new Material(new Vec3(1.0, 1.0, 1.0), 0.02, 0.00, Vec3.Zero);
                texMat.DiffuseTexture = tex;
                texMat.TextureWeight = 1.0;
                texMat.UVScale = 0.35;
                Func<Vec3, Vec3, float, Material> texturedPlane = (p, n, u) => texMat;
                s.Objects.Add(new Plane(new Vec3(0.0f, 0.0f, -98.0f), new Vec3(0.0f, 0.0f, 1.0f), texturedPlane, 0.02f, 0.00f));
            }

            Console.WriteLine("[Museum] Building Volume Grid dioramas...");
            {
                BuildVolumeDioramaA(s, volumeAnchorA, red, green, blue, mirror, glassClear, pedestal);
                BuildVolumeDioramaB(s, volumeAnchorB, red, green, blue, gold, pedestal);
            }

            Console.WriteLine("[Museum] Using a SINGLE giant light...");
            s.Lights.Add(new PointLight(new Vec3(0.0f, 12.0f, -50.0f), new Vec3(1.0f, 1.0f, 1.0f), 900.0f));

            s.BackgroundTop = new Vec3(0.06, 0.08, 0.10);
            s.BackgroundBottom = new Vec3(0.01, 0.01, 0.02);
            s.DefaultCameraPos = new Vec3(0.0, 1.7, 2.5);

            Console.WriteLine("[Museum] Rebuilding BVH...");
            Stopwatch bvhSw = Stopwatch.StartNew();
            s.RebuildBVH();
            bvhSw.Stop();
            totalSw.Stop();
            Console.WriteLine("[Museum] BVH rebuilt in {0} ms.", bvhSw.ElapsedMilliseconds);
            Console.WriteLine("[Museum] Build complete in {0} ms.", totalSw.ElapsedMilliseconds);

            return s;
        }

        private static void AddCornellBoxRoom(Scene s, Vec3 anchor, float width, float height, Vec3 leftColor, Vec3 rightColor, Vec3 whiteColor, float lightPower, Material emissive)
        {
            float xL = anchor.X - width;
            float xR = anchor.X - width * 0.0f;
            float yB = anchor.Y + 0.0f;
            float yT = anchor.Y + height;
            float zB = anchor.Z - width;
            float zF = anchor.Z + 0.0f;

            Func<Vec3, Vec3, float, Material> white = Solid(whiteColor);
            Func<Vec3, Vec3, float, Material> leftWall = Solid(leftColor);
            Func<Vec3, Vec3, float, Material> rightWall = Solid(rightColor);

            s.Objects.Add(new YZRect(yB, yT, zB, zF, xL, leftWall, 0.0f, 0.0f));
            s.Objects.Add(new YZRect(yB, yT, zB, zF, xR, rightWall, 0.0f, 0.0f));
            s.Objects.Add(new XZRect(xL, xR, zB, zF, yB, white, 0.0f, 0.0f));
            s.Objects.Add(new XZRect(xL, xR, zB, zF, yT, white, 0.0f, 0.0f));
            s.Objects.Add(new XYRect(xL, xR, yB, yT, zB, white, 0.0f, 0.0f));

            float lx0 = xL + 0.20f * width;
            float lx1 = xR - 0.20f * width;
            float lz0 = zB + 0.35f * width;
            float lz1 = zB + 0.55f * width;
            float ly = yT - 0.01f;

            s.Objects.Add(new XZRect(lx0, lx1, lz0, lz1, ly, (p, n, u) => emissive, 0.0f, 0.0f));
            s.Lights.Add(new PointLight(new Vec3((lx0 + lx1) * 0.5f, yT - 0.2f, (lz0 + lz1) * 0.5f), new Vec3(1.0f, 0.98f, 0.95f), lightPower));

            float cx = (xL + xR) * 0.5f;
            float cz = (zB + zF) * 0.5f;

            Material ped = new Material(new Vec3(0.88, 0.88, 0.88), 0.00, 0.00, Vec3.Zero);
            Material objA = new Material(new Vec3(0.90, 0.20, 0.20), 0.08, 0.02, Vec3.Zero);
            Material objB = new Material(new Vec3(0.20, 0.80, 0.95), 0.10, 0.06, Vec3.Zero);
            Material mirrorish = new Material(new Vec3(0.98, 0.98, 0.98), 0.0, 0.85, Vec3.Zero);
            Material glassish = new Material(new Vec3(1.0, 1.0, 1.0), 0.0, 0.02, Vec3.Zero, 1.0, 1.5, new Vec3(1.0, 1.0, 1.0));

            Vec3 stand0 = new Vec3(cx - 0.8f, yB, cz + 0.2f);
            Vec3 stand1 = new Vec3(cx + 0.8f, yB, cz - 0.2f);

            s.Objects.Add(new Disk(new Vec3(cx, yB + 0.01f, cz), new Vec3(0.0f, 1.0f, 0.0f), width * 0.32f, Solid(new Vec3(0.90f, 0.90f, 0.92f)), 0.0f, 0.0f));

            s.Objects.Add(new CylinderY(stand0, 0.28f, 0.0f, 0.9f, true, ped));
            s.Objects.Add(new Sphere(stand0 + new Vec3(0.0f, 0.9f + 0.35f, 0.0f), 0.35f, mirrorish));

            s.Objects.Add(new CylinderY(stand1, 0.28f, 0.0f, 0.9f, true, ped));
            s.Objects.Add(new Sphere(stand1 + new Vec3(0.0f, 0.9f + 0.32f, 0.0f), 0.32f, glassish));

            s.Objects.Add(new Sphere(new Vec3(cx, yB + 0.35f, cz - 0.9f), 0.35f, objA));
            s.Objects.Add(new Sphere(new Vec3(cx, yB + 0.22f, cz + 0.9f), 0.22f, objB));
        }

        private static void BuildVolumeDioramaA(Scene s, Vec3 minCorner, Material red, Material green, Material blue, Material mirror, Material glassClear, Material pedestal)
        {
            Stopwatch sw = Stopwatch.StartNew();
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
                    bool check = ((x + z) & 1) == 0;
                    cells[x, 1, z] = (check ? 1 : 4, 0);
                }
            }
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
            s.Objects.Add(new VolumeGrid(cells, minCorner, voxelSize, materialLookup));
            Vec3 pedC = minCorner + new Vec3(4.0f, 0.0f, 2.0f);
            s.Objects.Add(new CylinderY(pedC, 0.35f, 0.0f, 1.4f, true, pedestal));
            s.Objects.Add(new Sphere(pedC + new Vec3(0.0f, 1.4f + 0.45f, 0.0f), 0.45f, glassClear));
            s.Lights.Add(new PointLight(minCorner + new Vec3(4.0f, 3.0f, 1.0f), new Vec3(0.9f, 0.95f, 1.0f), 110.0f));
            sw.Stop();
            Console.WriteLine("[Museum] Diorama A added in {0} ms.", sw.ElapsedMilliseconds);
        }

        private static void BuildVolumeDioramaB(Scene s, Vec3 minCorner, Material red, Material green, Material blue, Material gold, Material pedestal)
        {
            Stopwatch sw = Stopwatch.StartNew();
            int nx = 14;
            int ny = 7;
            int nz = 14;
            (int matId, int metaId)[,,] cells = new (int, int)[nx, ny, nz];
            for (int x = 0; x < nx; x++)
            {
                for (int z = 0; z < nz; z++)
                {
                    bool check = ((x + z) & 1) == 0;
                    cells[x, 0, z] = (check ? 6 : 7, 0);
                }
            }
            for (int i = 2; i < nx - 2; i += 3)
            {
                for (int y = 1; y <= 3 && y < ny; y++)
                {
                    cells[i, y, 2] = (2, 0);
                    cells[i, y, nz - 3] = (3, 0);
                }
            }
            Vec3 voxelSize = new Vec3(0.45, 0.45, 0.45);
            Func<int, int, Material> materialLookup = (id, meta) =>
            {
                switch (id)
                {
                    case 2: return red;
                    case 3: return green;
                    case 4: return blue;
                    case 6: return new Material(new Vec3(0.80, 0.80, 0.82), 0.0, 0.0, Vec3.Zero);
                    case 7: return new Material(new Vec3(0.15, 0.15, 0.18), 0.0, 0.0, Vec3.Zero);
                    default: return new Material(new Vec3(0.7, 0.7, 0.7), 0.0, 0.0, Vec3.Zero);
                }
            };
            s.Objects.Add(new VolumeGrid(cells, minCorner, voxelSize, materialLookup));
            Vec3 stand = minCorner + new Vec3(3.0f, 0.0f, 6.0f);
            s.Objects.Add(new CylinderY(stand, 0.30f, 0.0f, 1.1f, true, pedestal));
            TryAddMeshAutoGround(s, @"assets\teapot.obj", gold, 1.0f, stand + new Vec3(0.0f, 1.12f, 0.0f));
            s.Lights.Add(new PointLight(minCorner + new Vec3(2.5f, 2.8f, 7.0f), new Vec3(1.0f, 0.95f, 0.9f), 85.0f));
            sw.Stop();
            Console.WriteLine("[Museum] Diorama B added in {0} ms.", sw.ElapsedMilliseconds);
        }

        private static void TryAddMeshAutoGround(Scene s, string objPath, Material mat, float scale, Vec3 targetPos)
        {
            Console.WriteLine("[Museum] Loading mesh: {0}", objPath);
            Stopwatch sw = Stopwatch.StartNew();
            if (!File.Exists(objPath))
            {
                sw.Stop();
                Console.WriteLine("[Museum] Mesh file missing, skipped: {0} ({1} ms)", objPath, sw.ElapsedMilliseconds);
                return;
            }
            float yTranslate = targetPos.Y + 0.5f + 0.01f;
            Vec3 translate = new Vec3(targetPos.X, yTranslate, targetPos.Z);
            s.Objects.Add(Mesh.FromObj(objPath, mat, scale: 1.0f, translate: translate));
            sw.Stop();
            Console.WriteLine("[Museum] Mesh added (scale=1): {0} ({1} ms)", objPath, sw.ElapsedMilliseconds);
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
                bool check = (((cx + cz) & 1) == 0);
                Vec3 albedo = check ? a : b;
                return new Material(albedo, 0.0, 0.0, Vec3.Zero);
            };
        }
    }
}
