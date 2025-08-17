// File: RaytraceRenderer.cs
using ConsoleGame.Components;
using ConsoleGame.Entities;
using ConsoleGame.RayTracing;
using ConsoleGame.RayTracing.Scenes;
using ConsoleGame.Renderer;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleRayTracing
{
    public partial class RaytraceEntity : BaseComponent
    {
        private readonly Framebuffer fb;
        private readonly RaytraceRenderer renderer;
        private readonly Scene activeScene;
        private readonly Dictionary<int, Scene> sceneCache = new Dictionary<int, Scene>();
        private readonly Func<Scene>[] sceneBuilders;
        private int sceneIndex;

        private Vec3 camPos;
        private float yaw;
        private float pitch;
        private float lastDeltaTime = 1.0f / 60.0f;
        private float sceneSwitchCooldown = 0.0f;

        public RaytraceEntity(BaseEntity entity, Framebuffer framebuffer, int pxW, int pxH, int superSample)
        {
            this.fb = framebuffer;
            this.camPos = new Vec3(0.0, 10.0, 0.0);
            this.yaw = 0.0f;
            this.pitch = 0.0f;

            this.sceneBuilders = BuildSceneTable();
            this.sceneIndex = DefaultSceneIndex();

            Scene initial = GetOrBuildScene(this.sceneIndex);
            this.activeScene = new Scene();
            this.renderer = new RaytraceRenderer(framebuffer, this.activeScene, initial.DefaultFovDeg, pxW, pxH, superSample);
            this.camPos = initial.DefaultCameraPos;
            this.yaw = initial.DefaultYaw;
            this.pitch = initial.DefaultPitch;
            this.renderer.SetCamera(camPos, yaw, pitch);

            SwitchToScene(this.sceneIndex);
        }

        public override void HandleInput(ConsoleKeyInfo keyInfo)
        {
            float moveSpeed = 3.0f;
            float rotSpeed = 1.8f;

            if(keyInfo.Modifiers == ConsoleModifiers.Shift)
            {
                moveSpeed *= 2;
                rotSpeed *= 2;
            }

            float dt = lastDeltaTime;
            if (dt < 0.0) dt = 0.0f;

            if (keyInfo.Key == ConsoleKey.LeftArrow)
            {
                yaw -= rotSpeed * dt;
            }

            if (keyInfo.Key == ConsoleKey.RightArrow)
            {
                yaw += rotSpeed * dt;
            }

            if (keyInfo.Key == ConsoleKey.UpArrow)
            {
                pitch += rotSpeed * dt;
            }

            if (keyInfo.Key == ConsoleKey.DownArrow)
            {
                pitch -= rotSpeed * dt;
            }

            float limit = (MathF.PI * 0.5f) - 0.01f;
            if (pitch > limit)
            {
                pitch = limit;
            }
            if (pitch < -limit)
            {
                pitch = -limit;
            }

            float cy = MathF.Cos(yaw);
            float sy = MathF.Sin(yaw);
            Vec3 forwardXZ = new Vec3(sy, 0.0, -cy);
            Vec3 rightXZ = new Vec3(cy, 0.0, sy);
            Vec3 up = new Vec3(0.0, 1.0, 0.0);

            if (keyInfo.Key == ConsoleKey.W)
            {
                camPos = camPos + forwardXZ * (moveSpeed * dt);
            }

            if (keyInfo.Key == ConsoleKey.S)
            {
                camPos = camPos - forwardXZ * (moveSpeed * dt);
            }

            if (keyInfo.Key == ConsoleKey.A)
            {
                camPos = camPos - rightXZ * (moveSpeed * dt);
            }

            if (keyInfo.Key == ConsoleKey.D)
            {
                camPos = camPos + rightXZ * (moveSpeed * dt);
            }

            if (keyInfo.Key == ConsoleKey.E)
            {
                camPos = camPos + up * (moveSpeed * dt);
            }

            if (keyInfo.Key == ConsoleKey.Q)
            {
                camPos = camPos - up * (moveSpeed * dt);
            }

            if (keyInfo.Key == ConsoleKey.I)
            {
                if (sceneSwitchCooldown <= 0.0f)
                {
                    int count = sceneBuilders.Length;
                    if (count > 0)
                    {
                        sceneIndex = (sceneIndex + 1) % count;
                        SwitchToScene(sceneIndex);
                        sceneSwitchCooldown = 1.0f;
                    }
                }
            }

            if (keyInfo.Key == ConsoleKey.U)
            {
                if (sceneSwitchCooldown <= 0.0f)
                {
                    int count = sceneBuilders.Length;
                    if (count > 0)
                    {
                        sceneIndex = (sceneIndex - 1 + count) % count;
                        SwitchToScene(sceneIndex);
                        sceneSwitchCooldown = 1.0f;
                    }
                }
            }

            renderer.SetCamera(camPos, yaw, pitch);
        }

        public string GetInfoString()
        {
            return $"pos=({camPos.X:0.###},{camPos.Y:0.###},{camPos.Z:0.###}) yaw={yaw:0.###} pitch={pitch:0.###} scene={sceneIndex}";
        }

        public override void Update(double deltaTime)
        {
            lastDeltaTime = (float)deltaTime;
            sceneSwitchCooldown -= (float)deltaTime;
            if (sceneSwitchCooldown < 0.0f)
            {
                sceneSwitchCooldown = 0.0f;
            }
            renderer.SetCamera(camPos, yaw, pitch);
            renderer.TryFlipAndBlit(fb);
            Program.terminal.SetDebugString(GetInfoString());
        }

        private void SwitchToScene(int index)
        {
            Scene src = GetOrBuildScene(index);
            activeScene.Objects.Clear();
            activeScene.Objects.AddRange(src.Objects);
            activeScene.Lights.Clear();
            activeScene.Lights.AddRange(src.Lights);
            activeScene.BackgroundTop = src.BackgroundTop;
            activeScene.BackgroundBottom = src.BackgroundBottom;
            activeScene.Ambient = src.Ambient;
            camPos = src.DefaultCameraPos;
            yaw = src.DefaultYaw;
            pitch = src.DefaultPitch;
            renderer.SetFov(src.DefaultFovDeg);
            renderer.SetCamera(camPos, yaw, pitch);
            renderer.ResetTaaHistory();
            activeScene.RebuildBVH();
        }

        private Scene GetOrBuildScene(int index)
        {
            Scene s;
            if (sceneCache.TryGetValue(index, out s))
            {
                return s;
            }
            if (index < 0 || index >= sceneBuilders.Length)
            {
                s = new Scene();
                sceneCache[index] = s;
                return s;
            }
            s = sceneBuilders[index]();
            sceneCache[index] = s;
            return s;
        }

        private static int DefaultSceneIndex()
        {
            return 0;
        }

        private static Func<Scene>[] BuildSceneTable()
        {
            List<Func<Scene>> list = new List<Func<Scene>>();

            list.Add(() => Scenes.BuildTestScene());
            list.Add(() => Scenes.BuildDemoScene());
            list.Add(() => Scenes.BuildCornellBox());
            list.Add(() => Scenes.BuildMirrorSpheresOnChecker());
            list.Add(() => Scenes.BuildCylindersDisksAndTriangles());
            list.Add(() => Scenes.BuildBoxesShowcase());
            list.Add(() => Scenes.BuildVolumeGridTestScene());
            list.Add(() => MeshScenes.BuildAllMeshesScene());
            list.Add(() => MeshScenes.BuildBunnyScene());
            list.Add(() => MeshScenes.BuildTeapotScene());
            list.Add(() => MeshScenes.BuildCowScene());
            list.Add(() => MeshScenes.BuildDragonScene());
            list.Add(() => VolumeScenes.BuildMinecraftLike("test.bin"));
            
            return list.ToArray();
        }
    }
}
