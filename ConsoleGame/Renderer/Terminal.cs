using ConsoleGame.Entities;
using System;
using System.Diagnostics;
using System.Collections.Generic;

namespace ConsoleGame.Renderer
{
    public class Terminal
    {
        private ANSITerminalRenderer renderer;
        private TerminalInput input;
        private bool isRunning;
        private Stopwatch stopwatch;
        private List<BaseEntity> entities;
        private Framebuffer entityFramebuffer;
        private string debugString = "";

        public event Action<int, int> Resized;

        public Terminal()
        {
            //renderer = new TerminalRenderer(OnResized);
            renderer = new ANSITerminalRenderer(OnResized);
            //renderer = new OpenGLTerminalRenderer(OnResized);
            //renderer = new Win32TerminalRenderer(OnResized);
            input = new TerminalInput();
            isRunning = false;
            stopwatch = new Stopwatch();
            entities = new List<BaseEntity>();

            // Create a framebuffer for entities
            entityFramebuffer = new Framebuffer(Console.WindowWidth, Console.WindowHeight - 1);
            renderer.AddFrameBuffer(entityFramebuffer);
        }

        public void OnResized(int width, int height)
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
            renderer.AddFrameBuffer(fb);
        }

        public void RemoveFrameBuffer(Framebuffer fb)
        {
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

                input.Update();

                while (input.TryGetKey(out ConsoleKeyInfo keyInfo))
                {
                    HandleInput(keyInfo);
                }

                Update(deltaTime);

                DrawEntities();

                renderer.Render();

                double frameMs = stopwatch.Elapsed.TotalMilliseconds;
                double fps = frameMs > 0.0 ? 1000.0 / frameMs : 0.0;
                string hud = $"{debugString} fps: {fps:0.0}  ms: {frameMs:0.00}";
                int hudlen = renderer.consoleWidth - 10;
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
    }
}
