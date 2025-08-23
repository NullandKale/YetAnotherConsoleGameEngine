using ConsoleGame.RayTracing.Objects;
using ConsoleRayTracing;

namespace ConsoleGame.RayTracing.Scenes
{
    public static class Scenes
    {
        public static Scene BuildTestScene()
        {
            Scene s = new Scene();
            s.Ambient = new AmbientLight(new Vec3(1, 1, 1), 0.01f);
            Material red = new Material(new Vec3(1.0, 0.0, 0.0), 0.15, 0.0, Vec3.Zero);
            Material green = new Material(new Vec3(0.0, 1.0, 0.0), 0.15, 0.0, Vec3.Zero);
            Material blue = new Material(new Vec3(0.0, 0.0, 1.0), 0.15, 0.0, Vec3.Zero);
            Material mirror = new Material(new Vec3(0.98, 0.98, 0.98), 0.0, 0.9, Vec3.Zero);

            float r = 0.9f;
            s.Objects.Add(new Sphere(new Vec3(-1.2, r, -2.2), r, red));     // top-left of square
            s.Objects.Add(new Sphere(new Vec3(1.2, r, -2.2), r, green));   // top-right
            s.Objects.Add(new Sphere(new Vec3(-1.2, r, -3.6), r, blue));    // bottom-left
            s.Objects.Add(new Sphere(new Vec3(1.2, r, -3.6), r, mirror));  // bottom-right (mirror)

            s.Lights.Add(new PointLight(new Vec3(0.0, 3.2, -2.9), new Vec3(1.0, 1.0, 1.0), 140.0f));
            s.Lights.Add(new PointLight(new Vec3(-2.2, 2.0, -2.4), new Vec3(1.0, 1.0, 1.0), 60.0f));

            s.BackgroundTop = new Vec3(0.05, 0.05, 0.05);
            s.BackgroundBottom = new Vec3(0.05, 0.05, 0.05);

            s.RebuildBVH();
            return s;
        }

        public static Scene BuildVolumeGridTestScene()
        {
            Scene s = new Scene();
            s.Ambient = new AmbientLight(new Vec3(1, 1, 1), 0.01f);

            int nx = 16;
            int ny = 8;
            int nz = 16;
            (int matId, int metaId)[,,] cells = new (int, int)[nx, ny, nz];

            // --- Populate a simple room: floor + low walls ---
            // matId legend:
            // 0=empty, 1=stone/white, 2=red, 3=green, 4=blue, 5=mirror
            for (int x = 0; x < nx; x++)
            {
                for (int z = 0; z < nz; z++)
                {
                    cells[x, 0, z] = (1, 0); // floor layer
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

            // --- Colored pillars to test shading/palette ---
            void Pillar(int cx, int cz, int height, int mat)
            {
                for (int y = 1; y <= height && y < ny; y++)
                {
                    cells[cx, y, cz] = (mat, 0);
                }
            }
            Pillar(4, 4, 4, 2);   // red
            Pillar(11, 4, 3, 3);  // green
            Pillar(4, 11, 5, 4);  // blue
            Pillar(11, 11, 4, 5); // mirror

            // --- A checker dais in the center to exercise normals and occlusion ---
            for (int x = 6; x <= 9; x++)
            {
                for (int z = 6; z <= 9; z++)
                {
                    bool check = (x + z & 1) == 0;
                    cells[x, 1, z] = (check ? 1 : 4, 0);
                }
            }

            // --- Placeholder "game object" spots (metaId!=0): think chests, etc. ---
            cells[2, 1, 2] = (2, 101);
            cells[13, 1, 2] = (3, 102);
            cells[2, 1, 13] = (4, 103);
            cells[13, 1, 13] = (5, 104);

            // --- Grid placement and scale in world space ---
            Vec3 minCorner = new Vec3(-4.0, 0.0, -6.0);
            Vec3 voxelSize = new Vec3(0.5, 0.5, 0.5); // 16*0.5 = 8 units wide, fits camera nicely

            // --- ID -> Material mapping ---
            Func<int, int, Material> materialLookup = (id, meta) =>
            {
                switch (id)
                {
                    case 1: return new Material(new Vec3(0.82, 0.82, 0.85), 0.0, 0.0, Vec3.Zero);  // stone/white
                    case 2: return new Material(new Vec3(0.95, 0.15, 0.15), 0.05, 0.0, Vec3.Zero); // red
                    case 3: return new Material(new Vec3(0.15, 0.95, 0.20), 0.05, 0.0, Vec3.Zero); // green
                    case 4: return new Material(new Vec3(0.15, 0.25, 0.95), 0.05, 0.0, Vec3.Zero); // blue
                    case 5: return new Material(new Vec3(0.98, 0.98, 0.98), 0.0, 0.9, Vec3.Zero);  // mirror
                    default: return new Material(new Vec3(0.7, 0.7, 0.7), 0.0, 0.0, Vec3.Zero);
                }
            };

            // --- Add the volume to the scene ---
            s.Objects.Add(new VolumeGrid(cells, minCorner, voxelSize, materialLookup));

            // --- Pedestals and spheres around the volume + a clear sphere at the exact center on the floor ---
            Material pedestalMat = new Material(new Vec3(0.85, 0.85, 0.85), 0.0, 0.0, Vec3.Zero);
            Material red = new Material(new Vec3(0.95, 0.15, 0.15), 0.05, 0.0, Vec3.Zero);
            Material green = new Material(new Vec3(0.15, 0.95, 0.20), 0.05, 0.0, Vec3.Zero);
            Material blue = new Material(new Vec3(0.15, 0.25, 0.95), 0.05, 0.0, Vec3.Zero);
            Material mirror = new Material(new Vec3(0.98, 0.98, 0.98), 0.0, 0.9, Vec3.Zero);
            Material clear = new Material(new Vec3(1.0, 1.0, 1.0), 0.0, 0.02, Vec3.Zero, 1.0, 1.5, new Vec3(1.0, 1.0, 1.0));

            float pedR = 0.25f;
            float pedH = 1.2f;
            float sphR = 0.35f;

            Vec3 centerXZ = new Vec3(0.0, 0.0, -2.0); // geometric center of the grid in XZ
            Vec3 posL = new Vec3(-1.6, 0.0, -2.0);
            Vec3 posR = new Vec3(1.6, 0.0, -2.0);
            Vec3 posF = new Vec3(0.0, 0.0, -0.8);
            Vec3 posB = new Vec3(0.0, 0.0, -3.2);

            s.Objects.Add(new CylinderY(posL, pedR, 0.0f, pedH, true, pedestalMat));
            s.Objects.Add(new CylinderY(posR, pedR, 0.0f, pedH, true, pedestalMat));
            s.Objects.Add(new CylinderY(posF, pedR, 0.0f, pedH, true, pedestalMat));
            s.Objects.Add(new CylinderY(posB, pedR, 0.0f, pedH, true, pedestalMat));

            s.Objects.Add(new Sphere(posL + new Vec3(0.0, pedH + sphR, 0.0), sphR, mirror));
            s.Objects.Add(new Sphere(posR + new Vec3(0.0, pedH + sphR, 0.0), sphR, red));
            s.Objects.Add(new Sphere(posF + new Vec3(0.0, pedH + sphR, 0.0), sphR, blue));
            s.Objects.Add(new Sphere(posB + new Vec3(0.0, pedH + sphR, 0.0), sphR, green));

            float clearR = 0.5f;
            s.Objects.Add(new Sphere(new Vec3(centerXZ.X, centerXZ.Y + 2, centerXZ.Z), clearR, clear));

            // --- Lighting (two lights to reduce flatness) ---
            s.Lights.Add(new PointLight(new Vec3(0.0, 5.0, -3.0), new Vec3(1.0, 1.0, 1.0), 220.0f));
            s.Lights.Add(new PointLight(new Vec3(-2.5, 3.0, -1.8), new Vec3(1.0, 0.95, 0.9), 90.0f));

            // --- Background ---
            s.BackgroundTop = new Vec3(0.02, 0.02, 0.02);
            s.BackgroundBottom = new Vec3(0.02, 0.02, 0.02);

            s.RebuildBVH();
            return s;
        }


        public static Scene BuildDemoScene()
        {
            Scene s = new Scene();
            s.Ambient = new AmbientLight(new Vec3(1, 1, 1), 0.01f);

            Material red = new Material(new Vec3(0.9, 0.2, 0.2), 0.25, 0.2, Vec3.Zero);
            Material green = new Material(new Vec3(0.2, 0.9, 0.2), 0.25, 0.1, Vec3.Zero);
            Material blue = new Material(new Vec3(0.2, 0.2, 0.9), 0.35, 0.5, Vec3.Zero);
            Material mirror = new Material(new Vec3(0.95, 0.95, 0.95), 0.0, 0.9, Vec3.Zero);
            Material lightMat = new Material(new Vec3(1.0, 1.0, 1.0), 0.0, 0.0, new Vec3(8.0, 8.0, 8.0));

            s.Objects.Add(new Sphere(new Vec3(-1.2, 1.0, 0.0), 1.0f, red));
            s.Objects.Add(new Sphere(new Vec3(1.2, 1.0, -0.5), 1.0f, blue));
            s.Objects.Add(new Sphere(new Vec3(0.0, 0.5, -2.5), 0.5f, mirror));

            s.Objects.Add(new Plane(new Vec3(0.0, 0.0, 0.0), new Vec3(0.0, 1.0, 0.0), Checker(new Vec3(0.8, 0.8, 0.8), new Vec3(0.1, 0.1, 0.1), 0.5f), 0.0f, 0.0f));

            s.Objects.Add(new Sphere(new Vec3(0.0, 5.0, 2.0), 0.5f, lightMat));

            s.Lights.Add(new PointLight(new Vec3(-2.0, 4.0, 3.0), new Vec3(1.0, 0.9, 0.8), 60.0f));
            s.Lights.Add(new PointLight(new Vec3(2.5, 3.5, -1.5), new Vec3(0.8, 0.9, 1.0), 40.0f));

            s.BackgroundTop = new Vec3(0.6, 0.8, 1.0);
            s.BackgroundBottom = new Vec3(0.9, 0.95, 1.0);

            Random rng = new Random(); // change to a fixed seed like new Random(1337) for reproducible placement

            Vec3 HsvToRgb(float h, float sSat, float v)
            {
                float c = v * sSat;
                float hh = h % 1.0f * 6.0f;
                float x = c * (1.0f - MathF.Abs(hh % 2.0f - 1.0f));
                float r = 0.0f, g = 0.0f, b = 0.0f;
                if (hh < 1.0) { r = c; g = x; b = 0.0f; }
                else if (hh < 2.0) { r = x; g = c; b = 0.0f; }
                else if (hh < 3.0) { r = 0.0f; g = c; b = x; }
                else if (hh < 4.0) { r = 0.0f; g = x; b = c; }
                else if (hh < 5.0) { r = x; g = 0.0f; b = c; }
                else { r = c; g = 0.0f; b = x; }
                float m = v - c;
                return new Vec3(r + m, g + m, b + m);
            }

            bool Overlaps(Vec3 c, float r)
            {
                for (int i = 0; i < s.Objects.Count; i++)
                {
                    Sphere sph = s.Objects[i] as Sphere;
                    if (sph != null)
                    {
                        Vec3 d = c - sph.Center;
                        float dist2 = d.Dot(d);
                        float rr = r + sph.Radius + 0.05f;
                        if (dist2 < rr * rr)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            int count = 100;
            int attemptsPerSphere = 32;
            for (int i = 0; i < count; i++)
            {
                bool placed = false;
                for (int attempt = 0; attempt < attemptsPerSphere && !placed; attempt++)
                {
                    float radius = 0.18f + (float)rng.NextDouble() * 0.32f; // [0.18, 0.50]
                    float x = -9.0f + (float)rng.NextDouble() * 9.0f;       // [-3, 3]
                    float z = -9.8f + (float)rng.NextDouble() * 4.6f;       // [-4.8, -0.2]
                    float y = radius;                               // sit on the ground plane (y=0)
                    Vec3 center = new Vec3(x, y, z);

                    if (Overlaps(center, radius))
                    {
                        continue;
                    }

                    float hue = (float)rng.NextDouble();                   // [0,1)
                    float sat = 0.65f + (float)rng.NextDouble() * 0.35f;     // [0.65,1.0]
                    float val = 0.55f + (float)rng.NextDouble() * 0.45f;     // [0.55,1.0]
                    Vec3 rgb = HsvToRgb(hue, sat, val);

                    float spec = 0.10f + (float)rng.NextDouble() * 0.30f;    // some spec
                    float refl = (float)rng.NextDouble() < 0.2f ? 0.6f : 0.05f; // occasional reflective balls

                    Material m = new Material(rgb, spec, refl, Vec3.Zero);
                    s.Objects.Add(new Sphere(center, radius, m));
                    placed = true;
                }
            }

            s.RebuildBVH();
            return s;
        }

        public static Scene BuildCornellBox()
        {
            Scene s = new Scene();
            s.Ambient = new AmbientLight(new Vec3(1, 1, 1), 0.00f);

            Func<Vec3, Vec3, float, Material> white = Solid(new Vec3(0.82, 0.82, 0.82));
            Func<Vec3, Vec3, float, Material> red = Solid(new Vec3(0.80, 0.10, 0.10));
            Func<Vec3, Vec3, float, Material> green = Solid(new Vec3(0.10, 0.80, 0.10));
            Func<Vec3, Vec3, float, Material> lightEmit = Emissive(new Vec3(0.6, 0.6, 0.6));

            float xL = -3.0f;
            float xR = 3.0f;
            float yB = 0.0f;
            float yT = 5.0f;
            float zF = 0.0f;  // open side toward the camera
            float zB = -5.0f; // back wall

            s.Objects.Add(new YZRect(yB, yT, zB, zF, xL, red, 0.0f, 0.0f));    // left wall (red)
            s.Objects.Add(new YZRect(yB, yT, zB, zF, xR, green, 0.0f, 0.0f));  // right wall (green)
            s.Objects.Add(new XZRect(xL, xR, zB, zF, yB, white, 0.0f, 0.0f));  // floor
            s.Objects.Add(new XZRect(xL, xR, zB, zF, yT, white, 0.0f, 0.0f));  // ceiling
            s.Objects.Add(new XYRect(xL, xR, yB, yT, zB, white, 0.0f, 0.0f));  // back wall

            float lx0 = -0.9f;
            float lx1 = 0.9f;
            float lz0 = -3.2f;
            float lz1 = -2.2f;
            float ly = yT - 0.01f;
            s.Objects.Add(new XZRect(lx0, lx1, lz0, lz1, ly, lightEmit, 0.0f, 0.0f)); // emissive panel (visible light)

            s.Objects.Add(new Box(new Vec3(-2.2, 0.0, -4.0), new Vec3(-0.8, 1.0, -2.8), white, 0.0f, 0.0f));
            s.Objects.Add(new Box(new Vec3(0.6, 0.0, -3.3), new Vec3(2.0, 1.8, -2.1), white, 0.0f, 0.0f));

            s.Lights.Add(new PointLight(new Vec3(0.0, 4.6, -2.7), new Vec3(1.0, 1.0, 1.0), 20.0f));

            s.BackgroundTop = new Vec3(0.0, 0.0, 0.0);
            s.BackgroundBottom = new Vec3(0.0, 0.0, 0.0);

            s.RebuildBVH();
            return s;
        }

        public static Scene BuildMirrorSpheresOnChecker()
        {
            Scene s = new Scene();
            s.Ambient = new AmbientLight(new Vec3(1, 1, 1), 0.01f);

            Func<Vec3, Vec3, float, Material> floor = Checker(new Vec3(0.8, 0.8, 0.8), new Vec3(0.15, 0.15, 0.15), 0.6f);
            s.Objects.Add(new XZRect(-8.0f, 8.0f, -8.0f, 4.0f, 0.0f, floor, 0.1f, 0.0f));

            Material gold = new Material(new Vec3(1.0, 0.85, 0.57), 0.25, 0.1, Vec3.Zero);
            Material glassy = new Material(new Vec3(0.9, 0.95, 1.0), 0.0, 0.6, Vec3.Zero);
            Material mirror = new Material(new Vec3(0.98, 0.98, 0.98), 0.0, 0.85, Vec3.Zero);

            s.Objects.Add(new Sphere(new Vec3(-1.2, 1.0, -2.0), 1.0f, gold));
            s.Objects.Add(new Sphere(new Vec3(1.3, 1.0, -2.6), 1.0f, glassy));
            s.Objects.Add(new Sphere(new Vec3(0.0, 0.5, -4.2), 0.5f, mirror));

            s.Lights.Add(new PointLight(new Vec3(-2.5, 3.5, -1.5), new Vec3(1.0, 0.95, 0.9), 90.0f));
            s.Lights.Add(new PointLight(new Vec3(2.0, 2.8, -3.8), new Vec3(0.9, 0.95, 1.0), 70.0f));

            s.BackgroundTop = new Vec3(0.55, 0.75, 1.0);
            s.BackgroundBottom = new Vec3(0.95, 0.98, 1.0);

            s.RebuildBVH();
            return s;
        }

        public static Scene BuildTextureTestScene()
        {
            Scene s = new Scene();
            s.Ambient = new AmbientLight(new Vec3(1, 1, 1), 0.5f);

            ConsoleGame.Renderer.Texture tex = new ConsoleGame.Renderer.Texture(@"C:\Users\alec\Downloads\IMG_1355.bmp");

            Material texMat = new Material(new Vec3(1, 1, 1), 0.0, 0.0, Vec3.Zero);
            texMat.DiffuseTexture = tex;
            texMat.TextureWeight = 1.0;
            texMat.UVScale = 1.0;

            s.Objects.Add(new Box(new Vec3(-0.5, -0.5, -2.5), new Vec3(0.5, 0.5, -1.5), (pos, n, u) => texMat, 0.00f, 0.00f));

            s.BackgroundTop = new Vec3(0.0, 0.0, 0.0);
            s.BackgroundBottom = new Vec3(0.0, 0.0, 0.0);

            s.RebuildBVH();
            return s;
        }


        public static Scene BuildCylindersDisksAndTriangles()
        {
            Scene s = new Scene();
            s.Ambient = new AmbientLight(new Vec3(1, 1, 1), 0.01f);

            Func<Vec3, Vec3, float, Material> floor = Checker(new Vec3(0.75, 0.75, 0.75), new Vec3(0.2, 0.2, 0.2), 0.8f);
            s.Objects.Add(new Plane(new Vec3(0.0, 0.0, 0.0), new Vec3(0.0, 1.0, 0.0), floor, 0.05f, 0.0f));

            Material matteBlue = new Material(new Vec3(0.2, 0.35, 0.9), 0.1, 0.0, Vec3.Zero);
            Material matteRed = new Material(new Vec3(0.9, 0.25, 0.25), 0.1, 0.0, Vec3.Zero);

            s.Objects.Add(new CylinderY(new Vec3(-1.2, 0.0, -3.0), 0.6f, 0.0f, 1.6f, true, matteBlue));
            s.Objects.Add(new Disk(new Vec3(1.6, 0.01, -2.2), new Vec3(0.0, 1.0, 0.0), 0.9f, Solid(new Vec3(0.8, 0.8, 0.1)), 0.0f, 0.0f));

            s.Objects.Add(new Triangle(new Vec3(0.2, 0.0, -3.6), new Vec3(1.3, 1.4, -3.0), new Vec3(-0.7, 0.7, -2.8), matteRed));

            s.Lights.Add(new PointLight(new Vec3(-2.2, 3.2, -2.0), new Vec3(1.0, 0.95, 0.9), 70.0f));
            s.Lights.Add(new PointLight(new Vec3(2.4, 2.2, -4.4), new Vec3(0.9, 0.95, 1.0), 60.0f));

            s.BackgroundTop = new Vec3(0.58, 0.78, 1.0);
            s.BackgroundBottom = new Vec3(0.95, 0.98, 1.0);

            s.RebuildBVH();
            return s;
        }

        public static Scene BuildBoxesShowcase()
        {
            Scene s = new Scene();
            s.Ambient = new AmbientLight(new Vec3(1, 1, 1), 0.01f);

            Func<Vec3, Vec3, float, Material> floor = Checker(new Vec3(0.85, 0.85, 0.85), new Vec3(0.15, 0.15, 0.15), 0.7f);
            s.Objects.Add(new Plane(new Vec3(0.0, 0.0, 0.0), new Vec3(0.0, 1.0, 0.0), floor, 0.05f, 0.0f));

            Func<Vec3, Vec3, float, Material> white = Solid(new Vec3(0.86, 0.86, 0.86));
            s.Objects.Add(new Box(new Vec3(-2.2, 0.0, -3.6), new Vec3(-1.0, 1.2, -2.4), white, 0.1f, 0.0f));
            s.Objects.Add(new Box(new Vec3(-0.6, 0.0, -4.2), new Vec3(0.6, 0.6, -3.0), white, 0.1f, 0.4f));
            s.Objects.Add(new Box(new Vec3(1.0, 0.0, -3.0), new Vec3(2.4, 2.0, -1.8), white, 0.0f, 0.0f));

            s.Lights.Add(new PointLight(new Vec3(-2.0, 3.0, -2.0), new Vec3(1.0, 0.95, 0.9), 70.0f));
            s.Lights.Add(new PointLight(new Vec3(2.0, 2.5, -4.2), new Vec3(0.9, 0.95, 1.0), 50.0f));

            s.BackgroundTop = new Vec3(0.6, 0.8, 1.0);
            s.BackgroundBottom = new Vec3(0.95, 0.98, 1.0);

            s.RebuildBVH();
            return s;
        }

        private static Func<Vec3, Vec3, float, Material> Solid(Vec3 albedo)
        {
            return (pos, n, u) => new Material(albedo, 0.0, 0.0, Vec3.Zero);
        }

        private static Func<Vec3, Vec3, float, Material> Emissive(Vec3 emission)
        {
            return (pos, n, u) => new Material(new Vec3(0.0, 0.0, 0.0), 0.0, 0.0, emission);
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
