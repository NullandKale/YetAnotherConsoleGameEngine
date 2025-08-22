// File: MeshScenes.Local.cs
using System;
using System.IO;
using System.Globalization;
using ConsoleRayTracing;
using ConsoleGame.RayTracing.Objects;

namespace ConsoleGame.RayTracing.Scenes
{
    public static class MeshSwatches
    {
        // Exact 16-color console sRGB anchors (match ConsolePalette order):
        // Black, DarkBlue, DarkGreen, DarkCyan, DarkRed, DarkMagenta, DarkYellow, Gray,
        // DarkGray, Blue, Green, Cyan, Red, Magenta, Yellow, White
        // (Kept here locally so scenes can pick deterministic swatches without peeking into ConsolePalette internals.)
        private static readonly Vec3[] Palette16 = new Vec3[]
        {
            new Vec3(0.00f,0.00f,0.00f),  // Black
            new Vec3(0.00f,0.00f,0.50f),  // DarkBlue
            new Vec3(0.00f,0.50f,0.00f),  // DarkGreen
            new Vec3(0.00f,0.50f,0.50f),  // DarkCyan
            new Vec3(0.50f,0.00f,0.00f),  // DarkRed
            new Vec3(0.50f,0.00f,0.50f),  // DarkMagenta
            new Vec3(0.50f,0.50f,0.00f),  // DarkYellow
            new Vec3(0.75f,0.75f,0.75f),  // Gray
            new Vec3(0.50f,0.50f,0.50f),  // DarkGray
            new Vec3(0.00f,0.00f,1.00f),  // Blue
            new Vec3(0.00f,1.00f,0.00f),  // Green
            new Vec3(0.00f,1.00f,1.00f),  // Cyan
            new Vec3(1.00f,0.00f,0.00f),  // Red
            new Vec3(1.00f,0.00f,1.00f),  // Magenta
            new Vec3(1.00f,1.00f,0.00f),  // Yellow
            new Vec3(1.00f,1.00f,1.00f)   // White
        };

        private static Vec3 FromConsole(ConsoleColor c)
        {
            int idx = (int)c;
            if (idx < 0 || idx >= Palette16.Length) return Palette16[0];
            return Palette16[idx];
        }

        private static Vec3 Scale(ConsoleColor c, float k)
        {
            if (k < 0.0f) k = 0.0f;
            if (k > 1.0f) k = 1.0f;
            Vec3 v = FromConsole(c);
            return new Vec3(v.X * k, v.Y * k, v.Z * k);
        }

        // ---------- Neutrals (great for bones, clay, porcelain, etc.) ----------
        public static readonly Vec3 Black = FromConsole(ConsoleColor.Black);
        public static readonly Vec3 Charcoal = FromConsole(ConsoleColor.DarkGray);
        public static readonly Vec3 Stone = FromConsole(ConsoleColor.Gray);
        public static readonly Vec3 WhiteSoft = Scale(ConsoleColor.White, 0.85f);   // avoids harsh clipping
        public static readonly Vec3 White = FromConsole(ConsoleColor.White);

        // ---------- Metals / warm materials ----------
        public static readonly Vec3 Gold = Scale(ConsoleColor.Yellow, 0.90f);
        public static readonly Vec3 Brass = Scale(ConsoleColor.DarkYellow, 1.00f);
        public static readonly Vec3 Copper = new Vec3(0.80f, 0.45f, 0.25f);      // will quantize near DarkYellow/Red

        // ---------- Gem-ish primaries (bright but softened a bit) ----------
        public static readonly Vec3 Ruby = Scale(ConsoleColor.Red, 0.92f);
        public static readonly Vec3 Emerald = Scale(ConsoleColor.Green, 0.85f);
        public static readonly Vec3 Sapphire = Scale(ConsoleColor.Blue, 0.85f);
        public static readonly Vec3 Amethyst = Scale(ConsoleColor.Magenta, 0.88f);
        public static readonly Vec3 CyanSoft = Scale(ConsoleColor.Cyan, 0.85f);
        public static readonly Vec3 Jade = Scale(ConsoleColor.DarkCyan, 1.00f);

        // ---------- Helpful dark accents ----------
        public static readonly Vec3 OxideRed = FromConsole(ConsoleColor.DarkRed);
        public static readonly Vec3 PineGreen = FromConsole(ConsoleColor.DarkGreen);
        public static readonly Vec3 Navy = FromConsole(ConsoleColor.DarkBlue);
        public static readonly Vec3 Plum = FromConsole(ConsoleColor.DarkMagenta);

        // ---------- Console-aligned primaries (exact anchors) ----------
        public static readonly Vec3 Red = FromConsole(ConsoleColor.Red);
        public static readonly Vec3 Green = FromConsole(ConsoleColor.Green);
        public static readonly Vec3 Blue = FromConsole(ConsoleColor.Blue);
        public static readonly Vec3 Magenta = FromConsole(ConsoleColor.Magenta);
        public static readonly Vec3 Yellow = FromConsole(ConsoleColor.Yellow);
        public static readonly Vec3 Cyan = FromConsole(ConsoleColor.Cyan);

        // ---------- Material helpers ----------
        public static Material Matte(Vec3 albedo, double specular = 0.10, double reflectivity = 0.00)
        {
            return new Material(albedo, specular, reflectivity, Vec3.Zero);
        }

        public static Material Mirror(Vec3 tint, double reflectivity = 0.85)
        {
            return new Material(tint, 0.0, reflectivity, Vec3.Zero);
        }

        public static Material Emissive(Vec3 emission)
        {
            return new Material(new Vec3(0.0f, 0.0f, 0.0f), 0.0, 0.0, emission);
        }
    }


    public static class MeshScenes
    {
        public static Scene BuildCowScene()
        {
            Scene s = NewBaseScene();
            Material cowMat = MeshSwatches.Matte(MeshSwatches.Gold, 0.08, 0.00);
            AddMeshAutoGround(s, @"assets\cow.obj", cowMat, scale: 0.85f, targetPos: new Vec3(0.0f, 0.0f, -3.2f));
            s.RebuildBVH();
            return s;
        }

        public static Scene BuildBunnyScene()
        {
            Scene s = NewBaseScene();
            Material bunnyMat = MeshSwatches.Matte(MeshSwatches.Emerald, 0.12, 0.00);
            AddMeshAutoGround(s, @"assets\stanford-bunny.obj", bunnyMat, scale: 8f, targetPos: new Vec3(0.0f, 0.0f, -2.8f));
            s.RebuildBVH();
            return s;
        }

        public static Scene BuildTeapotScene()
        {
            Scene s = NewBaseScene();
            Material teapotMat = MeshSwatches.Matte(MeshSwatches.Ruby, 0.30, 0.06);
            AddMeshAutoGround(s, @"assets\teapot.obj", teapotMat, scale: 0.60f, targetPos: new Vec3(0.0f, 0.0f, -3.0f));
            s.RebuildBVH();
            return s;
        }

        public static Scene BuildDragonScene()
        {
            Scene s = NewBaseScene();
            s.DefaultCameraPos = new Vec3(0, 10, 0);
            Material dragonMat = MeshSwatches.Mirror(MeshSwatches.Sapphire, 0.70);
            AddMeshAutoGround(s, @"assets\xyzrgb_dragon.obj", dragonMat, scale: 0.12f, targetPos: new Vec3(0.0f, 0.0f, -3.6f));
            s.RebuildBVH();
            return s;
        }

        public static Scene BuildAllMeshesScene()
        {
            Scene s = NewBaseScene();
            Material cowMat = MeshSwatches.Matte(MeshSwatches.Copper, 0.10, 0.00);
            Material bunnyMat = MeshSwatches.Matte(MeshSwatches.Jade, 0.12, 0.00);
            Material teapotMat = MeshSwatches.Matte(MeshSwatches.Gold, 0.28, 0.06);
            Material dragonMat = MeshSwatches.Mirror(MeshSwatches.Amethyst, 0.65);
            AddMeshAutoGround(s, @"assets\cow.obj", cowMat, scale: 0.80f, targetPos: new Vec3(-3.2f, 0.0f, -4.0f));
            AddMeshAutoGround(s, @"assets\stanford-bunny.obj", bunnyMat, scale: 8f, targetPos: new Vec3(-1.0f, 0.0f, -3.0f));
            AddMeshAutoGround(s, @"assets\teapot.obj", teapotMat, scale: 0.60f, targetPos: new Vec3(1.6f, 0.0f, -3.2f));
            AddMeshAutoGround(s, @"assets\xyzrgb_dragon.obj", dragonMat, scale: 0.12f, targetPos: new Vec3(3.2f, 0.0f, -4.6f));
            s.RebuildBVH();
            return s;
        }

        private static Scene NewBaseScene()
        {
            MeshBVH.counter = 0;
            Scene s = new Scene();
            s.Ambient = new AmbientLight(new Vec3(1, 1, 1), 0.15f);
            s.Objects.Add(new Plane(new Vec3(0.0, 0.0, 0.0), new Vec3(0.0, 1.0, 0.0), FloorMat(new Vec3(1, 1, 1)), 0.01f, 0.00f));
            s.Lights.Add(new PointLight(new Vec3(0.0, 30.6, -4.2), new Vec3(1.0, 0.95, 0.88), 110.0f));
            s.Lights.Add(new PointLight(new Vec3(0.0, 30.0, 4.2), new Vec3(0.85, 0.90, 1.0), 85.0f));
            s.BackgroundTop = new Vec3(0.0, 0.0, 0.0);
            s.BackgroundBottom = new Vec3(0.0, 0.0, 0.0);
            return s;
        }

        private static void AddMeshAutoGround(Scene s, string objPath, Material mat, float scale, Vec3 targetPos)
        {
            Vec3 mn, mx;
            if (!TryReadObjBounds(objPath, out mn, out mx))
            {
                throw new FileNotFoundException("OBJ not found or empty", objPath);
            }
            float minY = mn.Y;
            float yTranslate = targetPos.Y - minY * scale + 0.01f;
            Vec3 translate = new Vec3(targetPos.X, yTranslate, targetPos.Z);
            s.Objects.Add(Mesh.FromObj(objPath, mat, scale: scale, translate: translate));
        }

        private static bool TryReadObjBounds(string path, out Vec3 min, out Vec3 max)
        {
            min = new Vec3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            max = new Vec3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            if (!File.Exists(path))
            {
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
                        if (x < min.X) min.X = x; if (y < min.Y) min.Y = y; if (z < min.Z) min.Z = z;
                        if (x > max.X) max.X = x; if (y > max.Y) max.Y = y; if (z > max.Z) max.Z = z;
                    }
                }
            }
            if (min.X == float.PositiveInfinity) return false;
            return true;
        }

        private static Func<Vec3, Vec3, float, Material> FloorMat(Vec3 albedo)
        {
            return (pos, n, u) => new Material(albedo, 0.00, 0.00, Vec3.Zero);
        }
    }
}
