﻿using System;
using System.Collections.Generic;

namespace ConsoleGame.Renderer
{
    public class TerminalRenderer
    {
        private List<Framebuffer> frameBuffers;
        private int consoleWidth;
        private int consoleHeight;
        private ConsoleColor currentFg;
        private ConsoleColor currentBg;
        private ConsoleColor defaultFg;
        private ConsoleColor defaultBg;
        private char[] lineBuffer; // Pre-allocated buffer for rendering lines

        public TerminalRenderer()
        {
            frameBuffers = new List<Framebuffer>();
            consoleWidth = Console.WindowWidth;
            consoleHeight = Console.WindowHeight - 1;

            Console.CursorVisible = false;

            defaultFg = Console.ForegroundColor;
            defaultBg = Console.BackgroundColor;
            currentFg = defaultFg;
            currentBg = defaultBg;

            // Allocate the line buffer once
            lineBuffer = new char[consoleWidth];
        }

        public void AddFrameBuffer(Framebuffer fb)
        {
            frameBuffers.Add(fb);
        }

        public void RemoveFrameBuffer(Framebuffer fb)
        {
            frameBuffers.Remove(fb);
        }

        private Chexel GetChexelForPoint(int screenX, int screenY)
        {
            for (int i = frameBuffers.Count - 1; i >= 0; i--)
            {
                Framebuffer fb = frameBuffers[i];
                int fbX = screenX - fb.ViewportX;
                int fbY = screenY - fb.ViewportY;

                if (fbX >= 0 && fbX < fb.Width && fbY >= 0 && fbY < fb.Height)
                {
                    Chexel chexel = fb.GetChexel(fbX, fbY);
                    if (chexel.Char != ' ')
                    {
                        return chexel;
                    }
                }
            }

            return new Chexel(' ', defaultFg, defaultBg);
        }

        public void Render()
        {
            Console.CursorVisible = false;

            //// Sync cached colors with the console's actual state to avoid banding/stripes
            currentFg = Console.ForegroundColor;
            currentBg = Console.BackgroundColor;

            for (int y = 0; y < consoleHeight; y++)
            {
                Console.SetCursorPosition(0, y);

                ConsoleColor? runFg = null;
                ConsoleColor? runBg = null;
                int segmentStart = 0;

                for (int x = 0; x < consoleWidth; x++)
                {
                    Chexel chexel = GetChexelForPoint(x, y);

                    // Always fill the buffer for this position
                    lineBuffer[x] = chexel.Char;

                    // Start a new run if needed
                    if (runFg == null)
                    {
                        runFg = chexel.ForegroundColor;
                        runBg = chexel.BackgroundColor;
                        segmentStart = 0;
                    }
                    else if (chexel.ForegroundColor != runFg || chexel.BackgroundColor != runBg)
                    {
                        // Flush the previous run [segmentStart, x)
                        if (currentFg != runFg.Value)
                        {
                            Console.ForegroundColor = runFg.Value;
                            currentFg = runFg.Value;
                        }
                        if (currentBg != runBg.Value)
                        {
                            Console.BackgroundColor = runBg.Value;
                            currentBg = runBg.Value;
                        }
                        Console.Write(lineBuffer, segmentStart, x - segmentStart);

                        // Begin new run
                        runFg = chexel.ForegroundColor;
                        runBg = chexel.BackgroundColor;
                        segmentStart = x;
                    }
                }

                // Flush the final run for this line
                if (runFg == null)
                {
                    runFg = currentFg;
                    runBg = currentBg;
                }
                if (currentFg != runFg.Value)
                {
                    Console.ForegroundColor = runFg.Value;
                    currentFg = runFg.Value;
                }
                if (currentBg != runBg.Value)
                {
                    Console.BackgroundColor = runBg.Value;
                    currentBg = runBg.Value;
                }
                Console.Write(lineBuffer, segmentStart, consoleWidth - segmentStart);
            }

            Console.ResetColor();
        }
    }
}
