using System;
using ConsoleGame.RayTracing;

namespace ConsoleGame.Renderer
{
    public struct ChexelColor
    {
        public ConsoleColor color_16;
        public Vec3 color_f32;

        private static readonly Vec3[] s_Palette16 = new Vec3[]
        {
            new Vec3(0.00f,0.00f,0.00f),  // 0 Black
            new Vec3(0.00f,0.00f,0.50f),  // 1 DarkBlue
            new Vec3(0.00f,0.50f,0.00f),  // 2 DarkGreen
            new Vec3(0.00f,0.50f,0.50f),  // 3 DarkCyan
            new Vec3(0.50f,0.00f,0.00f),  // 4 DarkRed
            new Vec3(0.50f,0.00f,0.50f),  // 5 DarkMagenta
            new Vec3(0.50f,0.50f,0.00f),  // 6 DarkYellow
            new Vec3(0.75f,0.75f,0.75f),  // 7 Gray
            new Vec3(0.50f,0.50f,0.50f),  // 8 DarkGray
            new Vec3(0.00f,0.00f,1.00f),  // 9 Blue
            new Vec3(0.00f,1.00f,0.00f),  // 10 Green
            new Vec3(0.00f,1.00f,1.00f),  // 11 Cyan
            new Vec3(1.00f,0.00f,0.00f),  // 12 Red
            new Vec3(1.00f,0.00f,1.00f),  // 13 Magenta
            new Vec3(1.00f,1.00f,0.00f),  // 14 Yellow
            new Vec3(1.00f,1.00f,1.00f)   // 15 White
        };

        public ChexelColor(ConsoleColor color_16)
        {
            this.color_16 = color_16;
            this.color_f32 = PaletteToVec3(color_16);
        }

        public ChexelColor(Vec3 color_f32)
        {
            this.color_f32 = Clamp01(color_f32);
            this.color_16 = NearestConsoleColorFrom(this.color_f32);
        }

        public ChexelColor(ConsoleColor color_16, Vec3 color_f32)
        {
            this.color_16 = color_16;
            this.color_f32 = Clamp01(color_f32);
        }

        public static implicit operator ConsoleColor(ChexelColor c)
        {
            return c.color_16;
        }

        public static implicit operator ChexelColor(ConsoleColor c)
        {
            return new ChexelColor(c);
        }

        public static implicit operator ChexelColor(Vec3 v)
        {
            return new ChexelColor(v);
        }

        private static Vec3 PaletteToVec3(ConsoleColor c)
        {
            int idx = ((int)c) & 0xF;
            return s_Palette16[idx];
        }

        private static ConsoleColor NearestConsoleColorFrom(Vec3 v)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < s_Palette16.Length; i++)
            {
                Vec3 p = s_Palette16[i];
                float dr = (float)(v.X - p.X);
                float dg = (float)(v.Y - p.Y);
                float db = (float)(v.Z - p.Z);
                float d = dr * dr + dg * dg + db * db;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return (ConsoleColor)best;
        }

        private static Vec3 Clamp01(Vec3 c)
        {
            double rx = c.X < 0.0 ? 0.0 : (c.X > 1.0 ? 1.0 : c.X);
            double ry = c.Y < 0.0 ? 0.0 : (c.Y > 1.0 ? 1.0 : c.Y);
            double rz = c.Z < 0.0 ? 0.0 : (c.Z > 1.0 ? 1.0 : c.Z);
            return new Vec3(rx, ry, rz);
        }
    }

    public struct Chexel
    {
        public char Char;
        public ChexelColor ForegroundColor;
        public ChexelColor BackgroundColor;

        public Chexel(char ch, ConsoleColor fgColor, ConsoleColor bgColor)
        {
            Char = ch;
            ForegroundColor = fgColor;
            BackgroundColor = bgColor;
        }

        public Chexel(char ch, Vec3 fgColor, Vec3 bgColor)
        {
            Char = ch;
            ForegroundColor = fgColor;
            BackgroundColor = bgColor;
        }

        public Chexel(char ch, ChexelColor fgColor, ChexelColor bgColor)
        {
            Char = ch;
            ForegroundColor = fgColor;
            BackgroundColor = bgColor;
        }
    }
}
