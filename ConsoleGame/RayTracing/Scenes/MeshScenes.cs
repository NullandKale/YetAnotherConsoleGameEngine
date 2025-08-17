// File: MeshScenes.Local.cs
using System;
using System.IO;
using System.Globalization;
using ConsoleRayTracing;
using ConsoleGame.RayTracing.Objects;

namespace ConsoleGame.RayTracing.Scenes
{
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
