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

        public Terminal()
        {
            //renderer = new TerminalRenderer();
            renderer = new ANSITerminalRenderer();
            //renderer = new Win32TerminalRenderer();
            input = new TerminalInput();
            isRunning = false;
            stopwatch = new Stopwatch();
            entities = new List<BaseEntity>();

            // Create a framebuffer for entities
            entityFramebuffer = new Framebuffer(Console.WindowWidth, Console.WindowHeight - 1);
            renderer.AddFrameBuffer(entityFramebuffer);
        }

        public void SetDebugString(string str)
        {
            if(str != null)
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
            Console.CancelKeyPress += OnCancelKeyPress; // Handle Ctrl+C to stop the loop gracefully

            stopwatch.Start(); // Start the stopwatch to measure delta time

            while (isRunning)
            {
                // Calculate delta time (time elapsed since the last frame)
                double deltaTime = stopwatch.Elapsed.TotalSeconds;
                stopwatch.Restart(); // Reset the stopwatch for the next frame

                // Update input
                input.Update();

                // Process input
                while (input.TryGetKey(out ConsoleKeyInfo keyInfo))
                {
                    HandleInput(keyInfo);
                }

                // Update game logic using delta time
                Update(deltaTime);

                // Draw entities to the entity framebuffer
                DrawEntities();

                // Render the current frame
                renderer.Render();

                // HUD: print current fps and ms per frame on the last console line
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
                //Console.SetCursorPosition(0, renderer.consoleHeight);
                Console.Write(hud);
            }

            stopwatch.Stop(); // Stop the stopwatch
            Console.CancelKeyPress -= OnCancelKeyPress; // Clean up event handler
        }

        public void Stop()
        {
            isRunning = false;
        }

        private void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true; // Prevent the program from terminating immediately
            Stop(); // Stop the render loop gracefully
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
            // Clear the entity framebuffer
            entityFramebuffer.Clear();

            // Draw each entity to the entity framebuffer
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
