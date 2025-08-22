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

        // Orbit state (toggle with Y)
        private bool OrbitMode = false;
        private Vec3 OrbitPivot = new Vec3(0.0, 0.0, 0.0);
        private float OrbitRadius = 4.0f;                     // configurable; no longer overwritten on toggle
        private float OrbitAngle = 0.0f;
        private float OrbitY = 1.0f;
        private float OrbitRadiansPerSecond = 0.5f;           // ~12.6s per revolution
        private const float OrbitPivotAhead = 4.0f;           // center is 2 units ahead of camera when toggled
        private float TimeMS = 0.0f;                          // accumulated simulation time (ms)
        private float LastOrbitToggleMS = -1_000_000.0f;      // last time we toggled (ms)
        private const float OrbitToggleDebounceMS = 200.0f;   // debounce window for Y (ms)

        public virtual void RebuildBVH()
        {
            bvh = new BVH(Objects);
        }

        public virtual bool Hit(Ray r, float tMin, float tMax, ref HitRecord outRec, float screenU, float screenV)
        {
            if (bvh == null) throw new InvalidOperationException("Scene BVH not built; call RebuildBVH() after populating Objects.");
            return bvh.Hit(r, tMin, tMax, ref outRec, screenU, screenV);
        }

        public virtual bool Occluded(Ray r, float maxDist, float screenU, float screenV)
        {
            if (bvh == null) throw new InvalidOperationException("Scene BVH not built; call RebuildBVH() after populating Objects.");
            HitRecord rec = default;
            return bvh.Hit(r, 0.001f, maxDist, ref rec, screenU, screenV);
        }

        public virtual void ResetCamera()
        {
            CameraPos = DefaultCameraPos;
            Yaw = DefaultYaw;
            Pitch = DefaultPitch;
            OrbitMode = false;
            TimeMS = 0.0f;
            LastOrbitToggleMS = -1_000_000.0f;
        }

        public virtual void Update(float deltaTimeMS)
        {
            if (deltaTimeMS > 0.0f)
            {
                TimeMS += deltaTimeMS;
            }

            if (!OrbitMode)
            {
                return;
            }

            float dt = deltaTimeMS * 0.001f;
            if (dt <= 0.0f)
            {
                return;
            }

            OrbitAngle += OrbitRadiansPerSecond * dt;

            float rx = MathF.Cos(OrbitAngle) * OrbitRadius;
            float rz = MathF.Sin(OrbitAngle) * OrbitRadius;

            CameraPos = new Vec3(OrbitPivot.X + rx, OrbitY, OrbitPivot.Z + rz);

            float dx = (float)(OrbitPivot.X - CameraPos.X);
            float dy = (float)(OrbitPivot.Y - CameraPos.Y);
            float dz = (float)(OrbitPivot.Z - CameraPos.Z);
            float horiz = MathF.Max(1e-6f, MathF.Sqrt(dx * dx + dz * dz));

            Yaw = MathF.Atan2(dx, -dz);
            Pitch = MathF.Atan2(dy, horiz);

            float limit = (MathF.PI * 0.5f) - 0.01f;
            if (Pitch > limit)
            {
                Pitch = limit;
            }
            if (Pitch < -limit)
            {
                Pitch = -limit;
            }
        }

        public virtual void HandleInput(ConsoleKeyInfo keyInfo, float dt)
        {
            float moveSpeed = 6.0f;
            float rotSpeed = 2.2f;

            if (keyInfo.Modifiers == ConsoleModifiers.Shift)
            {
                moveSpeed *= 10;
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

            if (keyInfo.Key == ConsoleKey.Y)
            {
                if (TimeMS - LastOrbitToggleMS >= OrbitToggleDebounceMS)
                {
                    LastOrbitToggleMS = TimeMS;
                    if (!OrbitMode)
                    {
                        OrbitPivot = CameraPos + forwardXZ * OrbitPivotAhead;
                        OrbitY = (float)CameraPos.Y;
                        float dx = (float)(CameraPos.X - OrbitPivot.X);
                        float dz = (float)(CameraPos.Z - OrbitPivot.Z);
                        OrbitAngle = MathF.Atan2(dz, dx);
                        CameraPos = new Vec3(OrbitPivot.X + MathF.Cos(OrbitAngle) * OrbitRadius, OrbitY, OrbitPivot.Z + MathF.Sin(OrbitAngle) * OrbitRadius);
                        OrbitMode = true;
                    }
                    else
                    {
                        OrbitMode = false;
                    }
                }
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
