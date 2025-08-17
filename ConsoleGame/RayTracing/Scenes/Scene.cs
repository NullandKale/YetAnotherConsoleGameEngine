using System;
using System.Collections.Generic;
using System.IO;
using ConsoleGame.RayTracing.Objects;

namespace ConsoleGame.RayTracing.Scenes
{
    public class Scene
    {
        public List<Hittable> Objects = new List<Hittable>();
        public List<PointLight> Lights = new List<PointLight>();
        public Vec3 BackgroundTop = new Vec3(0.6, 0.8, 1.0);
        public Vec3 BackgroundBottom = new Vec3(1.0, 1.0, 1.0);
        public AmbientLight Ambient = new AmbientLight(new Vec3(1.0, 1.0, 1.0), 0.075f);
        public float DefaultFovDeg = 30.0f;
        public Vec3 DefaultCameraPos = new Vec3(0.0, 1.0, 0.0);
        public float DefaultYaw = 0.0f;
        public float DefaultPitch = 0.0f;

        private BVH bvh;

        public virtual void RebuildBVH()
        {
            bvh = new BVH(Objects);
        }

        public virtual bool Hit(Ray r, float tMin, float tMax, ref HitRecord outRec)
        {
            if (bvh == null) throw new InvalidOperationException("Scene BVH not built; call RebuildBVH() after populating Objects.");
            return bvh.Hit(r, tMin, tMax, ref outRec);
        }

        public virtual bool Occluded(Ray r, float maxDist)
        {
            if (bvh == null) throw new InvalidOperationException("Scene BVH not built; call RebuildBVH() after populating Objects.");
            HitRecord rec = default;
            return bvh.Hit(r, 0.001f, maxDist, ref rec);
        }
    }
}