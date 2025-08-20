using ConsoleGame.Components;
using ConsoleGame.Entities;
using ConsoleGame.RayTracing;
using ConsoleGame.RayTracing.Objects;
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
        private interface IConsoleRenderer
        {
            void SetCamera(Vec3 pos, float yaw, float pitch);
            void SetFov(float fovDeg);
            void TryFlipAndBlit(Framebuffer fb);
        }

        private sealed class RaytraceWrapper : IConsoleRenderer
        {
            private readonly RaytraceRenderer inner;
            public RaytraceWrapper(RaytraceRenderer inner) { this.inner = inner; }
            public void SetCamera(Vec3 pos, float yaw, float pitch) { inner.SetCamera(pos, yaw, pitch); }
            public void SetFov(float fovDeg) { inner.SetFov(fovDeg); }
            public void TryFlipAndBlit(Framebuffer fb) { inner.TryFlipAndBlit(fb); }
        }

        private sealed class VideoWrapper : IConsoleRenderer
        {
            private readonly VideoRenderer inner;
            public VideoWrapper(VideoRenderer inner) { this.inner = inner; }
            public void SetCamera(Vec3 pos, float yaw, float pitch) { /* no-op for video */ }
            public void SetFov(float fovDeg) { /* no-op for video */ }
            public void TryFlipAndBlit(Framebuffer fb) { inner.TryFlipAndBlit(fb); }
        }

        private enum RenderMode { Raytrace, Video }
        private readonly Framebuffer fb;
        private IConsoleRenderer renderer;
        private RenderMode mode;
        private readonly Dictionary<int, Scene> sceneCache = new Dictionary<int, Scene>();
        private readonly Func<Scene>[] sceneBuilders;
        private int sceneIndex;

        private Scene activeScene;
        private int rtWidth;
        private int rtHeight;
        private int rtSuperSample;

        private float lastDeltaTime = 1.0f / 60.0f;
        private float sceneSwitchCooldown = 0.0f;

        public RaytraceEntity(BaseEntity entity, Framebuffer framebuffer, int pxW, int pxH, int superSample)
        {
            this.fb = framebuffer;
            this.sceneBuilders = BuildSceneTable();
            this.sceneIndex = DefaultSceneIndex();
            this.activeScene = GetOrBuildScene(this.sceneIndex);
            this.mode = RenderMode.Raytrace;
            this.rtWidth = pxW;
            this.rtHeight = pxH;
            this.rtSuperSample = superSample;
            this.renderer = new RaytraceWrapper(new RaytraceRenderer(framebuffer, this.activeScene, activeScene.DefaultFovDeg, pxW, pxH, superSample));
            this.activeScene.CameraPos = activeScene.DefaultCameraPos;
            this.activeScene.Yaw = activeScene.DefaultYaw;
            this.activeScene.Pitch = activeScene.DefaultPitch;
            this.renderer.SetCamera(this.activeScene.CameraPos, this.activeScene.Yaw, this.activeScene.Pitch);
            SwitchToScene(this.sceneIndex);
        }

        public RaytraceEntity(BaseEntity entity, Framebuffer framebuffer, int cameraIndex, int superSample, bool requestRGBA = false, bool singleFrameAdvance = false, float forcedAspect = 0.0f)
        {
            this.fb = framebuffer;
            this.sceneBuilders = BuildSceneTable();
            this.sceneIndex = DefaultSceneIndex();
            this.activeScene = GetOrBuildScene(this.sceneIndex);
            this.mode = RenderMode.Video;
            this.renderer = new VideoWrapper(new VideoRenderer(framebuffer, cameraIndex, superSample, requestRGBA, singleFrameAdvance, forcedAspect));
            this.activeScene.CameraPos = activeScene.DefaultCameraPos;
            this.activeScene.Yaw = activeScene.DefaultYaw;
            this.activeScene.Pitch = activeScene.DefaultPitch;
            this.renderer.SetCamera(this.activeScene.CameraPos, this.activeScene.Yaw, this.activeScene.Pitch);
            SwitchToScene(this.sceneIndex);
        }

        public RaytraceEntity(BaseEntity entity, Framebuffer framebuffer, string videoFile, int superSample, bool requestRGBA = false, bool singleFrameAdvance = false, bool playAudio = true)
        {
            this.fb = framebuffer;
            this.sceneBuilders = BuildSceneTable();
            this.sceneIndex = DefaultSceneIndex();
            this.activeScene = GetOrBuildScene(this.sceneIndex);
            this.mode = RenderMode.Video;
            this.renderer = new VideoWrapper(new VideoRenderer(framebuffer, videoFile, superSample, requestRGBA, singleFrameAdvance, playAudio));
            this.activeScene.CameraPos = activeScene.DefaultCameraPos;
            this.activeScene.Yaw = activeScene.DefaultYaw;
            this.activeScene.Pitch = activeScene.DefaultPitch;
            this.renderer.SetCamera(this.activeScene.CameraPos, this.activeScene.Yaw, this.activeScene.Pitch);
            SwitchToScene(this.sceneIndex);
        }

        public override void HandleInput(ConsoleKeyInfo keyInfo)
        {
            float dt = lastDeltaTime;
            if (dt < 0.0f)
            {
                dt = 0.0f;
            }

            this.activeScene.HandleInput(keyInfo, dt);

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

            renderer.SetCamera(this.activeScene.CameraPos, this.activeScene.Yaw, this.activeScene.Pitch);
        }

        public string GetInfoString()
        {
            return $"pos=({activeScene.CameraPos.X:0.###},{activeScene.CameraPos.Y:0.###},{activeScene.CameraPos.Z:0.###}) yaw={activeScene.Yaw:0.###} pitch={activeScene.Pitch:0.###} scene={sceneIndex} objs={activeScene.Objects.Count} tris={MeshBVH.counter}   ";
        }

        public override void Update(double deltaTime)
        {
            float dtMS = (float)(deltaTime * 1000.0);
            activeScene.Update(dtMS);
            lastDeltaTime = (float)deltaTime;
            sceneSwitchCooldown -= (float)deltaTime;
            if (sceneSwitchCooldown < 0.0f)
            {
                sceneSwitchCooldown = 0.0f;
            }
            renderer.SetCamera(this.activeScene.CameraPos, this.activeScene.Yaw, this.activeScene.Pitch);
            renderer.TryFlipAndBlit(fb);
            Program.terminal.SetDebugString(GetInfoString());
        }

        private void SwitchToScene(int index)
        {
            Scene src = GetOrBuildScene(index);
            this.activeScene = src;
            if (mode == RenderMode.Raytrace)
            {
                this.renderer = new RaytraceWrapper(new RaytraceRenderer(fb, this.activeScene, src.DefaultFovDeg, rtWidth, rtHeight, rtSuperSample));
            }
            renderer.SetFov(src.DefaultFovDeg);
            renderer.SetCamera(activeScene.CameraPos, activeScene.Yaw, activeScene.Pitch);
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
            return 12;
        }

        private static Func<Scene>[] BuildSceneTable()
        {
            List<Func<Scene>> list = new List<Func<Scene>>();

            list.Add(() => Scenes.BuildTestScene()); // 0
            list.Add(() => Scenes.BuildDemoScene()); // 1
            list.Add(() => Scenes.BuildCornellBox()); // 2
            list.Add(() => Scenes.BuildMirrorSpheresOnChecker()); // 3
            list.Add(() => Scenes.BuildCylindersDisksAndTriangles()); // 4
            list.Add(() => Scenes.BuildBoxesShowcase()); // 5
            list.Add(() => Scenes.BuildVolumeGridTestScene()); // 6
            list.Add(() => MeshScenes.BuildAllMeshesScene()); // 7
            list.Add(() => MeshScenes.BuildBunnyScene()); // 8
            list.Add(() => MeshScenes.BuildTeapotScene()); // 9
            list.Add(() => MeshScenes.BuildCowScene()); // 10
            list.Add(() => MeshScenes.BuildDragonScene()); // 11
            list.Add(() => VolumeScenes.BuildMinecraftLike());

            return list.ToArray();
        }
    }
}
