using System;
using System.Collections.Generic;
using System.IO;
using ConsoleGame.RayTracing;
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
        public float DefaultFovDeg = 45.0f;
        public Vec3 DefaultCameraPos = new Vec3(0.0, 1.0, 0.0);
        public float DefaultYaw = 0.0f;
        public float DefaultPitch = 0.0f;

        public Vec3 CameraPos = new Vec3(0.0, 1.0, 0.0);
        public float Yaw = 0.0f;
        public float Pitch = 0.0f;

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

        public virtual void ResetCamera()
        {
            CameraPos = DefaultCameraPos;
            Yaw = DefaultYaw;
            Pitch = DefaultPitch;
        }

        public virtual void Update(float deltaTimeMS)
        {
        }

        public virtual void HandleInput(ConsoleKeyInfo keyInfo, float dt)
        {
            float moveSpeed = 3.0f;
            float rotSpeed = 1.8f;

            if (keyInfo.Modifiers == ConsoleModifiers.Shift)
            {
                moveSpeed *= 2;
                rotSpeed *= 2;
            }

            if (dt < 0.0f)
            {
                dt = 0.0f;
            }

            if (keyInfo.Key == ConsoleKey.LeftArrow)
            {
                Yaw -= rotSpeed * dt;
            }

            if (keyInfo.Key == ConsoleKey.RightArrow)
            {
                Yaw += rotSpeed * dt;
            }

            if (keyInfo.Key == ConsoleKey.UpArrow)
            {
                Pitch += rotSpeed * dt;
            }

            if (keyInfo.Key == ConsoleKey.DownArrow)
            {
                Pitch -= rotSpeed * dt;
            }

            float limit = (MathF.PI * 0.5f) - 0.01f;
            if (Pitch > limit)
            {
                Pitch = limit;
            }
            if (Pitch < -limit)
            {
                Pitch = -limit;
            }

            float cy = MathF.Cos(Yaw);
            float sy = MathF.Sin(Yaw);
            Vec3 forwardXZ = new Vec3(sy, 0.0, -cy);
            Vec3 rightXZ = new Vec3(cy, 0.0, sy);
            Vec3 up = new Vec3(0.0, 1.0, 0.0);

            if (keyInfo.Key == ConsoleKey.W)
            {
                CameraPos = CameraPos + forwardXZ * (moveSpeed * dt);
            }

            if (keyInfo.Key == ConsoleKey.S)
            {
                CameraPos = CameraPos - forwardXZ * (moveSpeed * dt);
            }

            if (keyInfo.Key == ConsoleKey.A)
            {
                CameraPos = CameraPos - rightXZ * (moveSpeed * dt);
            }

            if (keyInfo.Key == ConsoleKey.D)
            {
                CameraPos = CameraPos + rightXZ * (moveSpeed * dt);
            }

            if (keyInfo.Key == ConsoleKey.E)
            {
                CameraPos = CameraPos + up * (moveSpeed * dt);
            }

            if (keyInfo.Key == ConsoleKey.Q)
            {
                CameraPos = CameraPos - up * (moveSpeed * dt);
            }
        }

        public virtual void CopyFrom(Scene src)
        {
            Objects.Clear();
            Objects.AddRange(src.Objects);
            Lights.Clear();
            Lights.AddRange(src.Lights);
            BackgroundTop = src.BackgroundTop;
            BackgroundBottom = src.BackgroundBottom;
            Ambient = src.Ambient;
            DefaultFovDeg = src.DefaultFovDeg;
            DefaultCameraPos = src.DefaultCameraPos;
            DefaultYaw = src.DefaultYaw;
            DefaultPitch = src.DefaultPitch;
            ResetCamera();
        }
    }
}