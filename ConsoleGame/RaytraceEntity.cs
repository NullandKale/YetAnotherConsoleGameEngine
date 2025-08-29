using ConsoleGame.Components;
using ConsoleGame.Entities;
using ConsoleGame.RayTracing.Objects;
using ConsoleGame.RayTracing.Scenes;
using ConsoleGame.RayTracing;
using ConsoleGame.Renderer;
using System;
using System.Collections.Generic;

public partial class RaytraceEntity : BaseComponent
{
    private interface IConsoleRenderer
    {
        void SetCamera(Vec3 pos, float yaw, float pitch);
        void SetFov(float fovDeg);
        void TryFlipAndBlit(Framebuffer fb);
        void Resize(Framebuffer fb, int superSample);
    }

    private sealed class RaytraceWrapper : IConsoleRenderer
    {
        private RaytraceRenderer inner;
        public RaytraceWrapper(RaytraceRenderer inner) { this.inner = inner; }
        public void SetCamera(Vec3 pos, float yaw, float pitch) { inner.SetCamera(pos, yaw, pitch); }
        public void SetFov(float fovDeg) { inner.SetFov(fovDeg); }
        public void TryFlipAndBlit(Framebuffer fb) { inner.TryFlipAndBlit(fb); }
        public void Resize(Framebuffer fb, int superSample) { inner.Resize(fb, superSample); }
    }

    private sealed class VideoWrapper : IConsoleRenderer
    {
        private VideoRenderer inner;
        private readonly Func<Framebuffer, int, VideoRenderer> factory;

        public VideoWrapper(VideoRenderer initial, Func<Framebuffer, int, VideoRenderer> factory)
        {
            this.inner = initial ?? throw new ArgumentNullException(nameof(initial));
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public void SetCamera(Vec3 pos, float yaw, float pitch) { /* no-op for video */ }
        public void SetFov(float fovDeg) { /* no-op for video */ }
        public void TryFlipAndBlit(Framebuffer fb) { inner.TryFlipAndBlit(fb); }

        public void Resize(Framebuffer fb, int superSample)
        {
            inner.Dispose();
            inner = factory(fb, superSample);
        }
    }

    private enum RenderMode { Raytrace, Video }

    private readonly Terminal terminal;
    private Framebuffer fb;
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

    // Default camera (Video) options used when switching via number keys
    private bool camRequestRGBA = false;
    private bool camSingleFrameAdvance = false;
    private float camForcedAspect = 1.77f;

    public RaytraceEntity(Terminal terminal, BaseEntity entity, int superSample)
    {
        if (terminal == null) throw new ArgumentNullException(nameof(terminal));
        this.terminal = terminal;
        this.mode = RenderMode.Raytrace;
        this.sceneBuilders = BuildSceneTable();
        this.sceneIndex = DefaultSceneIndex();
        this.activeScene = GetOrBuildScene(this.sceneIndex);
        this.rtSuperSample = Math.Max(1, superSample);

        this.activeScene.CameraPos = activeScene.DefaultCameraPos;
        this.activeScene.Yaw = activeScene.DefaultYaw;
        this.activeScene.Pitch = activeScene.DefaultPitch;

        this.terminal.AddResizedCallback(OnTerminalResized);
        int initW = Math.Max(1, Console.WindowWidth - 1);
        int initH = Math.Max(1, Console.WindowHeight - 1);
        CreateOrResizeFramebuffer(initW, initH);

        var rt = new RaytraceRenderer(fb, this.activeScene, activeScene.DefaultFovDeg, rtWidth, rtHeight, rtSuperSample);
        this.renderer = new RaytraceWrapper(rt);
        this.renderer.SetFov(activeScene.DefaultFovDeg);
        this.renderer.SetCamera(this.activeScene.CameraPos, this.activeScene.Yaw, this.activeScene.Pitch);
    }

    public RaytraceEntity(Terminal terminal, BaseEntity entity, int cameraIndex, int superSample, bool requestRGBA = false, bool singleFrameAdvance = false, float forcedAspect = 0.0f)
    {
        if (terminal == null) throw new ArgumentNullException(nameof(terminal));
        this.terminal = terminal;
        this.mode = RenderMode.Video;
        this.sceneBuilders = BuildSceneTable();
        this.sceneIndex = DefaultSceneIndex();
        this.activeScene = GetOrBuildScene(this.sceneIndex);
        this.rtSuperSample = Math.Max(1, superSample);

        this.camRequestRGBA = requestRGBA;
        this.camSingleFrameAdvance = singleFrameAdvance;
        this.camForcedAspect = forcedAspect;

        this.activeScene.CameraPos = activeScene.DefaultCameraPos;
        this.activeScene.Yaw = activeScene.DefaultYaw;
        this.activeScene.Pitch = activeScene.DefaultPitch;

        this.terminal.AddResizedCallback(OnTerminalResized);
        int initW = Math.Max(1, Console.WindowWidth - 1);
        int initH = Math.Max(1, Console.WindowHeight - 1);
        CreateOrResizeFramebuffer(initW, initH);

        Func<Framebuffer, int, VideoRenderer> factory = (newFb, ss) => new VideoRenderer(newFb, cameraIndex, ss, requestRGBA, singleFrameAdvance, forcedAspect);
        var vr = factory(fb, rtSuperSample);
        this.renderer = new VideoWrapper(vr, factory);
        this.renderer.SetCamera(this.activeScene.CameraPos, this.activeScene.Yaw, this.activeScene.Pitch);
    }

    public RaytraceEntity(Terminal terminal, BaseEntity entity, string videoFile, int superSample, bool requestRGBA = false, bool singleFrameAdvance = false, bool playAudio = true)
    {
        if (terminal == null) throw new ArgumentNullException(nameof(terminal));
        this.terminal = terminal;
        this.mode = RenderMode.Video;
        this.sceneBuilders = BuildSceneTable();
        this.sceneIndex = DefaultSceneIndex();
        this.activeScene = GetOrBuildScene(this.sceneIndex);
        this.rtSuperSample = Math.Max(1, superSample);

        this.activeScene.CameraPos = activeScene.DefaultCameraPos;
        this.activeScene.Yaw = activeScene.DefaultYaw;
        this.activeScene.Pitch = activeScene.DefaultPitch;

        this.terminal.AddResizedCallback(OnTerminalResized);
        int initW = Math.Max(1, Console.WindowWidth - 1);
        int initH = Math.Max(1, Console.WindowHeight - 1);
        CreateOrResizeFramebuffer(initW, initH);

        Func<Framebuffer, int, VideoRenderer> factory = (newFb, ss) => new VideoRenderer(newFb, videoFile, ss, requestRGBA, singleFrameAdvance, playAudio);
        var vr = factory(fb, rtSuperSample);
        this.renderer = new VideoWrapper(vr, factory);
        this.renderer.SetCamera(this.activeScene.CameraPos, this.activeScene.Yaw, this.activeScene.Pitch);
    }

    public override void HandleMouse(TerminalInput.MouseEvent me, float dt)
    {
        activeScene.HandleMouse(me, dt);
    }

    public override void HandleInput(ConsoleKeyInfo keyInfo)
    {
        float dt = lastDeltaTime;
        if (dt < 0.0f) dt = 0.0f;

        activeScene.HandleInput(keyInfo, dt);

        // Number-row and numpad -> switch to camera mode with index mapping: '1'->0, ... '9'->8, '0'->9
        int camIdx = MapKeyToCameraIndex(keyInfo.Key);
        if (camIdx >= 0)
        {
            SwitchToCamera(camIdx);
            return;
        }

        if (keyInfo.Key == ConsoleKey.I)
        {
            if (sceneSwitchCooldown <= 0.0f)
            {
                EnsureRaytraceMode();
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
                EnsureRaytraceMode();
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
        if (sceneSwitchCooldown < 0.0f) sceneSwitchCooldown = 0.0f;

        renderer.SetCamera(this.activeScene.CameraPos, this.activeScene.Yaw, this.activeScene.Pitch);
        renderer.TryFlipAndBlit(fb);
        Program.terminal.SetDebugString(GetInfoString());
    }

    private void SwitchToScene(int index)
    {
        if (mode == RenderMode.Raytrace)
        {
            Scene src = GetOrBuildScene(index);
            this.activeScene = src;
            var rt = new RaytraceRenderer(fb, this.activeScene, src.DefaultFovDeg, rtWidth, rtHeight, rtSuperSample);
            this.renderer = new RaytraceWrapper(rt);
            renderer.SetFov(src.DefaultFovDeg);
            renderer.SetCamera(activeScene.CameraPos, activeScene.Yaw, activeScene.Pitch);
            activeScene.RebuildBVH();
        }
    }

    private void SwitchToCamera(int cameraIndex)
    {
        mode = RenderMode.Video;
        Func<Framebuffer, int, VideoRenderer> factory = (newFb, ss) => new VideoRenderer(newFb, cameraIndex, ss, camRequestRGBA, camSingleFrameAdvance, camForcedAspect);
        var vr = factory(fb, rtSuperSample);
        this.renderer = new VideoWrapper(vr, factory);
        renderer.SetCamera(this.activeScene.CameraPos, this.activeScene.Yaw, this.activeScene.Pitch);
    }

    private void EnsureRaytraceMode()
    {
        if (mode != RenderMode.Raytrace)
        {
            mode = RenderMode.Raytrace;
            var rt = new RaytraceRenderer(fb, this.activeScene, activeScene.DefaultFovDeg, rtWidth, rtHeight, rtSuperSample);
            this.renderer = new RaytraceWrapper(rt);
            renderer.SetFov(activeScene.DefaultFovDeg);
            renderer.SetCamera(activeScene.CameraPos, this.activeScene.Yaw, this.activeScene.Pitch);
        }
    }

    private static int MapKeyToCameraIndex(ConsoleKey key)
    {
        if (key == ConsoleKey.D1 || key == ConsoleKey.NumPad1) return 0;
        if (key == ConsoleKey.D2 || key == ConsoleKey.NumPad2) return 1;
        if (key == ConsoleKey.D3 || key == ConsoleKey.NumPad3) return 2;
        if (key == ConsoleKey.D4 || key == ConsoleKey.NumPad4) return 3;
        if (key == ConsoleKey.D5 || key == ConsoleKey.NumPad5) return 4;
        if (key == ConsoleKey.D6 || key == ConsoleKey.NumPad6) return 5;
        if (key == ConsoleKey.D7 || key == ConsoleKey.NumPad7) return 6;
        if (key == ConsoleKey.D8 || key == ConsoleKey.NumPad8) return 7;
        if (key == ConsoleKey.D9 || key == ConsoleKey.NumPad9) return 8;
        if (key == ConsoleKey.D0 || key == ConsoleKey.NumPad0) return 9;
        return -1;
    }

    private void OnTerminalResized(int width, int height)
    {
        int w = Math.Max(1, width - 1);
        int h = Math.Max(1, height - 1);
        CreateOrResizeFramebuffer(w, h);
        renderer.Resize(fb, rtSuperSample);
        renderer.SetCamera(activeScene.CameraPos, activeScene.Yaw, activeScene.Pitch);
        renderer.SetFov(activeScene.DefaultFovDeg);
    }

    private void CreateOrResizeFramebuffer(int width, int height)
    {
        if (fb != null) Program.terminal.RemoveFrameBuffer(fb);
        fb = new Framebuffer(width, height, 0, 0);
        Program.terminal.AddFrameBuffer(fb);
        rtWidth = width * rtSuperSample;
        rtHeight = height * rtSuperSample;
    }

    private Scene GetOrBuildScene(int index)
    {
        if (sceneCache.TryGetValue(index, out Scene s)) return s;
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

    private static int DefaultSceneIndex() { return 0; }

    private Func<Scene>[] BuildSceneTable()
    {
        if (mode != RenderMode.Raytrace) return Array.Empty<Func<Scene>>();

        List<Func<Scene>> list =
        [
            () => TestScenes.BuildTestScene(),
            () => Scenes.BuildTextureTestScene(),
            () => Scenes.BuildTestScene(),
            () => Scenes.BuildDemoScene(),
            () => Scenes.BuildCornellBox(),
            () => Scenes.BuildMirrorSpheresOnChecker(),
            () => Scenes.BuildCylindersDisksAndTriangles(),
            () => Scenes.BuildBoxesShowcase(),
            () => Scenes.BuildVolumeGridTestScene(),
            () => MeshScenes.BuildAllMeshesScene(),
            () => MeshScenes.BuildBunnyScene(),
            () => MeshScenes.BuildTeapotScene(),
            () => MeshScenes.BuildCowScene(),
            () => MeshScenes.BuildDragonScene(),
            () => VolumeScenes.BuildMinecraftLike(),
        ];

        return list.ToArray();
    }
}
