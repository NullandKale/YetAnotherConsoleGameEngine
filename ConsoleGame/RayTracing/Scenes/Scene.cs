using System;
using System.Collections.Generic;
using System.IO;
using ConsoleGame.RayTracing;
using ConsoleGame.RayTracing.Objects;
using ConsoleGame.Renderer;

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

        // If true, the renderer should invalidate/clear TAA history each frame
        // because scene shading depends on dynamic video/camera pixels.
        public bool HasDynamicTextures = false;

        // Orbit state (toggle with Y)
        private bool OrbitMode = false;
        private Vec3 OrbitPivot = new Vec3(0.0, 0.0, 0.0);
        private float OrbitRadius = 4.0f;
        private float OrbitAngle = 0.0f;
        private float OrbitY = 1.0f;
        private float OrbitRadiansPerSecond = 0.5f;
        private const float OrbitPivotAhead = 4.0f;
        private float TimeMS = 0.0f;
        private float LastOrbitToggleMS = -1_000_000.0f;
        private const float OrbitToggleDebounceMS = 200.0f;

        // Mouse interaction state and tuning
        public float MouseYawPitchPerPixel = 0.0035f;         // rad per "unit" from input
        public float MousePanUnitsPerPixel = 0.01f;           // world units per "unit" from input
        public float MouseWheelUnitsPerDelta = 0.0020f;
        public float MouseOrbitYawPerPixel = 0.0075f;         // rad per "unit" from input
        public float MouseOrbitYUnitsPerPixel = 0.01f;        // world units per "unit" from input
        public float MouseOrbitRadiusPerDelta = 0.0040f;

        // Adaptive scaling for coarse (cell-sized) mouse deltas
        public float CoarseSensitivityBoost = 8.0f;           // multiply deltas when coarse input detected
        private bool MouseRotating = false;
        private bool MousePanning = false;
        private int LastMouseX = 0;
        private int LastMouseY = 0;
        private bool CoarseMouse = false;
        private int CoarseDetectCount = 0;
        private const int CoarseDetectThreshold = 6;

        // Game-logic entity layer
        private readonly List<ISceneEntity> Entities = new List<ISceneEntity>();
        private bool GeometryDirty = false;

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
            MouseRotating = false;
            MousePanning = false;
            LastMouseX = 0;
            LastMouseY = 0;
            CoarseMouse = false;
            CoarseDetectCount = 0;
        }

        public virtual void Update(float deltaTimeMS)
        {
            if (deltaTimeMS > 0.0f)
            {
                TimeMS += deltaTimeMS;
            }

            float dt = deltaTimeMS * 0.001f;
            if (dt < 0.0f)
            {
                dt = 0.0f;
            }

            for (int i = 0; i < Entities.Count; i++)
            {
                ISceneEntity e = Entities[i];
                if (e != null && e.Enabled)
                {
                    e.Update(dt, this);
                }
            }

            if (GeometryDirty)
            {
                GeometryDirty = false;
                SyncObjectsFromEntities();
                RebuildBVH();
            }

            if (!OrbitMode)
            {
                return;
            }

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
                        float cy2 = MathF.Cos(Yaw);
                        float sy2 = MathF.Sin(Yaw);
                        Vec3 forwardXZ2 = new Vec3(sy2, 0.0, -cy2);
                        OrbitPivot = CameraPos + forwardXZ2 * OrbitPivotAhead;
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

        public virtual void HandleMouse(ConsoleGame.Renderer.TerminalInput.MouseEvent me, float dt)
        {
            if (dt < 0.0f)
            {
                dt = 0.0f;
            }

            if (me.Down)
            {
                if ((me.Buttons & ConsoleGame.Renderer.TerminalInput.MouseButtons.Right) != 0)
                {
                    MouseRotating = true;
                    LastMouseX = me.X;
                    LastMouseY = me.Y;
                    CoarseDetectCount = 0;
                }
                if ((me.Buttons & ConsoleGame.Renderer.TerminalInput.MouseButtons.Middle) != 0)
                {
                    MousePanning = true;
                    LastMouseX = me.X;
                    LastMouseY = me.Y;
                    CoarseDetectCount = 0;
                }
            }

            if (me.Up)
            {
                if ((me.Buttons & ConsoleGame.Renderer.TerminalInput.MouseButtons.Right) != 0)
                {
                    MouseRotating = false;
                }
                if ((me.Buttons & ConsoleGame.Renderer.TerminalInput.MouseButtons.Middle) != 0)
                {
                    MousePanning = false;
                }
            }

            if (me.Moved)
            {
                int dxPix = me.X - LastMouseX;
                int dyPix = me.Y - LastMouseY;

                int ax = Math.Abs(dxPix);
                int ay = Math.Abs(dyPix);
                if ((ax <= 1 && ay <= 1))
                {
                    if (CoarseDetectCount < CoarseDetectThreshold) CoarseDetectCount++;
                    if (CoarseDetectCount >= CoarseDetectThreshold) CoarseMouse = true;
                }
                else
                {
                    if (CoarseDetectCount > 0) CoarseDetectCount--;
                    if (ax > 2 || ay > 2) CoarseMouse = false;
                }
                float unitScale = CoarseMouse ? CoarseSensitivityBoost : 1.0f;

                if (MouseRotating)
                {
                    if (OrbitMode)
                    {
                        OrbitAngle += (dxPix * unitScale) * MouseOrbitYawPerPixel;
                        OrbitY -= (dyPix * unitScale) * MouseOrbitYUnitsPerPixel;
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
                    else
                    {
                        Yaw += (dxPix * unitScale) * MouseYawPitchPerPixel;
                        Pitch -= (dyPix * unitScale) * MouseYawPitchPerPixel;
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
                }

                if (MousePanning)
                {
                    Vec3 forward, right, camUp;
                    ComputeCameraBasis(out forward, out right, out camUp);
                    Vec3 pan = (-(dxPix * unitScale) * MousePanUnitsPerPixel) * right + ((dyPix * unitScale) * MousePanUnitsPerPixel) * camUp;
                    if (OrbitMode)
                    {
                        OrbitPivot = OrbitPivot + pan;
                        CameraPos = CameraPos + pan;
                    }
                    else
                    {
                        CameraPos = CameraPos + pan;
                    }
                }

                LastMouseX = me.X;
                LastMouseY = me.Y;
            }
            else
            {
                if (!MouseRotating && !MousePanning)
                {
                    LastMouseX = me.X;
                    LastMouseY = me.Y;
                }
            }

            if (me.WheelDelta != 0)
            {
                if (OrbitMode)
                {
                    OrbitRadius -= me.WheelDelta * MouseOrbitRadiusPerDelta;
                    if (OrbitRadius < 0.05f)
                    {
                        OrbitRadius = 0.05f;
                    }
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
                else
                {
                    float cy = MathF.Cos(Yaw);
                    float sy = MathF.Sin(Yaw);
                    float cp = MathF.Cos(Pitch);
                    float sp = MathF.Sin(Pitch);
                    Vec3 forward = new Vec3(sy * cp, sp, -cy * cp);
                    CameraPos = CameraPos + forward * (me.WheelDelta * MouseWheelUnitsPerDelta);
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

        private void ComputeCameraBasis(out Vec3 forward, out Vec3 right, out Vec3 camUp)
        {
            float cy = MathF.Cos(Yaw);
            float sy = MathF.Sin(Yaw);
            float cp = MathF.Cos(Pitch);
            float sp = MathF.Sin(Pitch);
            forward = new Vec3(sy * cp, sp, -cy * cp);
            Vec3 worldUp = new Vec3(0.0, 1.0, 0.0);
            right = Normalize(Cross(forward, worldUp));
            camUp = Normalize(Cross(right, forward));
        }

        private static Vec3 Normalize(Vec3 v)
        {
            float len = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (len <= 1e-12) return new Vec3(0.0, 0.0, 0.0);
            return v * (1.0f / len);
        }

        private static Vec3 Cross(Vec3 a, Vec3 b)
        {
            return new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }

        // ==== Game-logic entity management API ====

        public void AddEntity(ISceneEntity entity)
        {
            if (entity == null) return;
            Entities.Add(entity);
            GeometryDirty = true;
        }

        public bool RemoveEntity(ISceneEntity entity)
        {
            bool removed = Entities.Remove(entity);
            if (removed) GeometryDirty = true;
            return removed;
        }

        public void ClearEntities()
        {
            Entities.Clear();
            GeometryDirty = true;
        }

        public ISceneEntity Add(Hittable h)
        {
            if (h == null) return null;
            StaticHittableEntity e = new StaticHittableEntity(h);
            AddEntity(e);
            return e;
        }

        public void RequestGeometryRebuild()
        {
            GeometryDirty = true;
        }

        public IReadOnlyList<Hittable> Drawables
        {
            get { return Objects; }
        }

        private void SyncObjectsFromEntities()
        {
            Objects.Clear();
            for (int i = 0; i < Entities.Count; i++)
            {
                ISceneEntity e = Entities[i];
                if (e != null && e.Enabled)
                {
                    IEnumerable<Hittable> hs = e.GetHittables();
                    if (hs == null) continue;
                    foreach (Hittable h in hs)
                    {
                        if (h != null) Objects.Add(h);
                    }
                }
            }
        }
    }

    public interface ISceneEntity
    {
        bool Enabled { get; set; }
        void Update(float dt, Scene scene);
        IEnumerable<Hittable> GetHittables();
    }

    public sealed class StaticHittableEntity : ISceneEntity
    {
        private readonly Hittable h;
        public bool Enabled { get; set; } = true;

        public StaticHittableEntity(Hittable hittable)
        {
            h = hittable;
        }

        public void Update(float dt, Scene scene)
        {
        }

        public IEnumerable<Hittable> GetHittables()
        {
            yield return h;
        }
    }
}
