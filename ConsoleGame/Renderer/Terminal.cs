// File: Terminal.cs
using ConsoleGame.Entities;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ConsoleGame.Threads;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ConsoleGame.Renderer
{
    public interface ITerminalRenderer
    {
        int consoleWidth { get; }
        void AddFrameBuffer(Framebuffer fb);
        void RemoveFrameBuffer(Framebuffer fb);
        void Render();
    }

    public class Terminal
    {
        private ITerminalRenderer renderer;
        private TerminalInput input;
        private bool isRunning;
        private Stopwatch stopwatch;
        private List<BaseEntity> entities;
        private Framebuffer entityFramebuffer;
        private string debugString = "";
        private readonly List<Framebuffer> externalFramebuffers = new List<Framebuffer>();
        private int rendererIndex = 1;
        private string rendererName = "";

        private readonly long resizeDebounceTicks = TimeSpan.TicksPerMillisecond * 125;
        private int pendingResizeW = -1;
        private int pendingResizeH = -1;
        private long lastResizeEventTick = 0;
        private bool resizePending = false;

        private bool oem4Latched = false;
        private bool oem6Latched = false;

        public event Action<int, int> Resized;

        public Terminal()
        {
            input = new TerminalInput();
            isRunning = false;
            stopwatch = new Stopwatch();
            entities = new List<BaseEntity>();
            entityFramebuffer = new Framebuffer(Console.WindowWidth, Console.WindowHeight - 1);
            renderer = CreateRendererByIndex(rendererIndex, OnResized, out rendererName);
            renderer.AddFrameBuffer(entityFramebuffer);
        }

        public void OnResized(int width, int height)
        {
            pendingResizeW = width;
            pendingResizeH = height;
            lastResizeEventTick = DateTime.UtcNow.Ticks;
            resizePending = true;
        }

        private void ProcessDebouncedResize()
        {
            if (!resizePending) return;
            long now = DateTime.UtcNow.Ticks;
            if (now - lastResizeEventTick < resizeDebounceTicks) return;
            resizePending = false;
            if (pendingResizeW > 0 && pendingResizeH > 0) ApplyResize(pendingResizeW, pendingResizeH);
        }

        private void ApplyResize(int width, int height)
        {
            Framebuffer old = entityFramebuffer;
            entityFramebuffer = new Framebuffer(width, height);
            renderer.RemoveFrameBuffer(old);
            renderer.AddFrameBuffer(entityFramebuffer);
            Resized?.Invoke(width, height);
        }

        public void AddResizedCallback(Action<int, int> callback)
        {
            if (callback != null) Resized += callback;
        }

        public void RemoveResizedCallback(Action<int, int> callback)
        {
            if (callback != null) Resized -= callback;
        }

        public void SetDebugString(string str)
        {
            if (str != null)
            {
                debugString = str;
            }
        }

        public void AddFrameBuffer(Framebuffer fb)
        {
            if (fb == null) return;
            externalFramebuffers.Add(fb);
            renderer.AddFrameBuffer(fb);
        }

        public void RemoveFrameBuffer(Framebuffer fb)
        {
            if (fb == null) return;
            externalFramebuffers.Remove(fb);
            renderer.RemoveFrameBuffer(fb);
        }

        public void AddEntity(BaseEntity entity)
        {
            entities.Add(entity);
        }

        public void RemoveEntity(BaseEntity entity)
        {
            entities.Remove(entity);
        }

        public void Start()
        {
            if (isRunning)
                return;

            isRunning = true;
            Console.CancelKeyPress += OnCancelKeyPress;

            stopwatch.Start();

            while (isRunning)
            {
                double deltaTime = stopwatch.Elapsed.TotalSeconds;
                stopwatch.Restart();

                UpdateSwitchKeyLatches();

                input.Update();

                while (input.TryGetMouseEvent(out TerminalInput.MouseEvent mouseEvent))
                {
                    HandleMouse(mouseEvent, (float)deltaTime);
                }

                while (input.TryGetKey(out ConsoleKeyInfo keyInfo))
                {
                    HandleInput(keyInfo);
                }

                ProcessDebouncedResize();

                Update(deltaTime);

                DrawEntities();

                renderer.Render();

                double frameMs = stopwatch.Elapsed.TotalMilliseconds;
                double fps = frameMs > 0.0 ? 1000.0 / frameMs : 0.0;
                string hud = $"{debugString} renderer: {rendererName}  fps: {fps:0.0}  ms: {frameMs:0.00}";
                int hudlen = renderer != null ? renderer.consoleWidth - 10 : Console.WindowWidth - 10;
                if (hud.Length < hudlen)
                {
                    hud = hud.PadRight(hudlen);
                }
                else if (hud.Length > hudlen)
                {
                    hud = hud.Substring(0, hudlen);
                }
                Console.Write(hud);
            }

            stopwatch.Stop();
            Console.CancelKeyPress -= OnCancelKeyPress;

            DisposeRendererIfNeeded(renderer);
            renderer = null;
            BringConsoleToFrontIfWindows();
        }

        public void Stop()
        {
            isRunning = false;
        }

        private void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            Stop();
        }

        private void HandleMouse(TerminalInput.MouseEvent me, float dt)
        {
            foreach (var entity in entities)
            {
                entity.HandleMouse(me, dt);
            }
        }


        private void HandleInput(ConsoleKeyInfo keyInfo)
        {
            foreach (var entity in entities)
            {
                entity.HandleInput(keyInfo);
            }

            if (keyInfo.Key == ConsoleKey.Escape)
            {
                Stop();
            }

            if (keyInfo.Key == ConsoleKey.Oem4)
            {
                if (!oem4Latched)
                {
                    oem4Latched = true;
                    CycleRenderer(-1);
                }
            }

            if (keyInfo.Key == ConsoleKey.Oem6)
            {
                if (!oem6Latched)
                {
                    oem6Latched = true;
                    CycleRenderer(1);
                }
            }
        }

        private void UpdateSwitchKeyLatches()
        {
            if (!input.IsKeyDown(ConsoleKey.Oem4))
            {
                oem4Latched = false;
            }
            if (!input.IsKeyDown(ConsoleKey.Oem6))
            {
                oem6Latched = false;
            }
        }

        private void CycleRenderer(int dir)
        {
            int count = GetRendererCount();
            if (count <= 0) return;
            int next = ((rendererIndex + dir) % count + count) % count;
            if (next == rendererIndex) return;

            ITerminalRenderer old = renderer;

            if (old != null)
            {
                old.RemoveFrameBuffer(entityFramebuffer);
                for (int i = 0; i < externalFramebuffers.Count; i++)
                {
                    old.RemoveFrameBuffer(externalFramebuffers[i]);
                }
                DisposeRendererIfNeeded(old);
            }

            rendererIndex = next;
            Console.Clear();

            renderer = CreateRendererByIndex(rendererIndex, OnResized, out rendererName);

            BringConsoleToFrontIfWindows();

            renderer.AddFrameBuffer(entityFramebuffer);
            for (int i = 0; i < externalFramebuffers.Count; i++)
            {
                renderer.AddFrameBuffer(externalFramebuffers[i]);
            }
        }

        private static void DisposeRendererIfNeeded(ITerminalRenderer r)
        {
            if (r is IDisposable d)
            {
                try { d.Dispose(); } catch { }
            }
        }

        private void Update(double deltaTime)
        {
            foreach (var entity in entities)
            {
                entity.Update(deltaTime);
            }
        }

        private void DrawEntities()
        {
            entityFramebuffer.Clear();

            foreach (var entity in entities)
            {
                if (entity.X >= 0 && entity.X < entityFramebuffer.Width &&
                    entity.Y >= 0 && entity.Y < entityFramebuffer.Height)
                {
                    entityFramebuffer.SetChexel(entity.X, entity.Y, entity.Chexel);
                }
            }
        }

        private static int GetRendererCount()
        {
            return 4;
        }

        private static ITerminalRenderer CreateRendererByIndex(int index, Action<int, int> onResized, out string name)
        {
            int count = GetRendererCount();
            int i = ((index % count) + count) % count;
            switch (i)
            {
                case 0:
                    name = "OpenGL";
                    return new OpenGLTerminalRenderer(onResized);
                case 1:
                    name = "ANSI";
                    return new ANSITerminalRenderer(onResized);
                case 2:
                    name = "Win32";
                    return new Win32TerminalRenderer(onResized);
                case 3:
                    name = "Terminal";
                    return new TerminalRenderer(onResized);
                default:
                    name = "OpenGL";
                    return new OpenGLTerminalRenderer(onResized);
            }
        }

        [SupportedOSPlatform("windows")]
        private static void BringConsoleToFrontIfWindows()
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
                IntPtr hWnd = GetConsoleWindow();
                if (hWnd == IntPtr.Zero) return;
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
            }
            catch { }
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;
    }
}