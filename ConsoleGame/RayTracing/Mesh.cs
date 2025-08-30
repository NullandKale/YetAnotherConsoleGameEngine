// File: MeshAndMeshLoader.cs
using System;
using System.IO;
using System.Collections.Generic;
using ConsoleGame.RayTracing.Objects;

namespace ConsoleGame.RayTracing
{
    public sealed class Mesh : Hittable
    {
        public readonly Vec3 BoundsMin;
        public readonly Vec3 BoundsMax;

        private readonly Hittable bvh;

        public Mesh(List<Triangle> triangles, Vec3 min, Vec3 max)
        {
            BoundsMin = min;
            BoundsMax = max;
            bvh = new MeshBVH(triangles);
        }

        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            return bvh.Hit(r, tMin, tMax, ref rec, screenU, screenV);
        }

        [Obsolete("Use MeshLoader.FromObj instead.")]
        public static Mesh FromObj(string path, Material defaultMaterial, float scale = 1.0f, Vec3? translate = null, bool normalize = true, float targetSize = 1.0f)
        {
            return MeshLoader.FromObj(path, defaultMaterial, scale, translate, normalize, targetSize);
        }

        public override bool TryGetBounds(out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ, out float cx, out float cy, out float cz)
        {
            return bvh.TryGetBounds(out minX, out minY, out minZ, out maxX, out maxY, out maxZ, out cx, out cy, out cz);
        }
    }
}
