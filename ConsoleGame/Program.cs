using ConsoleGame.Renderer;
using ConsoleGame.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using ConsoleGame.Components;

namespace ConsoleRayTracing
{
    public static class Program
    {
        public static Terminal terminal;

        private static void Main(string[] args)
        {
            Console.CursorVisible = false;
            
            terminal = new Terminal();

            int cellsW = Console.WindowWidth - 1;
            int cellsH = Console.WindowHeight - 1;

            int superSample = 2;
            if (args != null && args.Length > 0)
            {
                int parsed;
                if (int.TryParse(args[0], out parsed) && parsed > 0)
                {
                    superSample = parsed;
                }
            }

            int pxW = cellsW * superSample;
            int pxH = cellsH * superSample;

            Framebuffer rayFb = new Framebuffer(cellsW, cellsH, 0, 0);
            terminal.AddFrameBuffer(rayFb);

            BaseEntity rt = new BaseEntity(0, 0, new Chexel());
            rt.AddComponent(new RaytraceEntity(rt, rayFb, pxW, pxH, superSample));
            //rt.AddComponent(new RaytraceEntity(rt, rayFb, 1, superSample, false, false, 1.77f));
            //rt.AddComponent(new RaytraceEntity(rt, rayFb, pxW, pxH, superSample));
            terminal.AddEntity(rt);

            terminal.Start();

            Console.ResetColor();
            Console.CursorVisible = true;
        }

       
    }
}
