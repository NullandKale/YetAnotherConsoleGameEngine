using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleGame.Renderer
{
    public class ANSITerminalRenderer
    {
        private List<Framebuffer> frameBuffers;
        public int consoleWidth;
        public int consoleHeight;
        private ConsoleColor defaultFg;
        private ConsoleColor defaultBg;
        private bool vtEnabled;

        public ANSITerminalRenderer()
        {
            frameBuffers = new List<Framebuffer>();
            consoleWidth = Console.WindowWidth;
            consoleHeight = Console.WindowHeight - 1;
            defaultFg = Console.ForegroundColor;
            defaultBg = Console.BackgroundColor;
            Console.CursorVisible = false;
            vtEnabled = EnableVirtualTerminalProcessing();
            if (!vtEnabled)
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            Console.Write("\x1b[?25l");
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
            bool sizeChanged = false;
            if (Console.WindowWidth != consoleWidth || Console.WindowHeight - 1 != consoleHeight)
            {
                consoleWidth = Console.WindowWidth;
                consoleHeight = Console.WindowHeight - 1;
                sizeChanged = true;
            }

            StringBuilder sb = new StringBuilder(consoleWidth * consoleHeight * 8);

            if (sizeChanged)
            {
                sb.Append("\x1b[2J\x1b[H");
            }

            int currentFgCode = -1;
            int currentBgCode = -1;

            for (int y = 0; y < consoleHeight; y++)
            {
                sb.Append("\x1b[");
                sb.Append(y + 1);
                sb.Append(";1H");

                for (int x = 0; x < consoleWidth; x++)
                {
                    Chexel c = GetChexelForPoint(x, y);
                    int fg = MapConsoleColorToAnsiFg(c.ForegroundColor);
                    int bg = MapConsoleColorToAnsiBg(c.BackgroundColor);

                    if (fg != currentFgCode && bg != currentBgCode)
                    {
                        sb.Append("\x1b[");
                        sb.Append(fg);
                        sb.Append(';');
                        sb.Append(bg);
                        sb.Append('m');
                        currentFgCode = fg;
                        currentBgCode = bg;
                    }
                    else if (fg != currentFgCode)
                    {
                        sb.Append("\x1b[");
                        sb.Append(fg);
                        sb.Append('m');
                        currentFgCode = fg;
                    }
                    else if (bg != currentBgCode)
                    {
                        sb.Append("\x1b[");
                        sb.Append(bg);
                        sb.Append('m');
                        currentBgCode = bg;
                    }

                    sb.Append(c.Char);
                }
            }

            sb.Append("\x1b[0m");

            Console.Out.Write(sb.ToString());
            Console.Out.Flush();
        }

        private static int MapConsoleColorToAnsiFg(ConsoleColor color)
        {
            switch (color)
            {
                case ConsoleColor.Black: return 30;
                case ConsoleColor.DarkBlue: return 34;
                case ConsoleColor.DarkGreen: return 32;
                case ConsoleColor.DarkCyan: return 36;
                case ConsoleColor.DarkRed: return 31;
                case ConsoleColor.DarkMagenta: return 35;
                case ConsoleColor.DarkYellow: return 33;
                case ConsoleColor.Gray: return 37;
                case ConsoleColor.DarkGray: return 90;
                case ConsoleColor.Blue: return 94;
                case ConsoleColor.Green: return 92;
                case ConsoleColor.Cyan: return 96;
                case ConsoleColor.Red: return 91;
                case ConsoleColor.Magenta: return 95;
                case ConsoleColor.Yellow: return 93;
                case ConsoleColor.White: return 97;
                default: return 39;
            }
        }

        private static int MapConsoleColorToAnsiBg(ConsoleColor color)
        {
            switch (color)
            {
                case ConsoleColor.Black: return 40;
                case ConsoleColor.DarkBlue: return 44;
                case ConsoleColor.DarkGreen: return 42;
                case ConsoleColor.DarkCyan: return 46;
                case ConsoleColor.DarkRed: return 41;
                case ConsoleColor.DarkMagenta: return 45;
                case ConsoleColor.DarkYellow: return 43;
                case ConsoleColor.Gray: return 47;
                case ConsoleColor.DarkGray: return 100;
                case ConsoleColor.Blue: return 104;
                case ConsoleColor.Green: return 102;
                case ConsoleColor.Cyan: return 106;
                case ConsoleColor.Red: return 101;
                case ConsoleColor.Magenta: return 105;
                case ConsoleColor.Yellow: return 103;
                case ConsoleColor.White: return 107;
                default: return 49;
            }
        }

        private static bool EnableVirtualTerminalProcessing()
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return true;
                }
                IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
                if (handle == INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
                {
                    return false;
                }
                if (!GetConsoleMode(handle, out uint mode))
                {
                    return false;
                }
                uint newMode = mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING | DISABLE_NEWLINE_AUTO_RETURN;
                if (!SetConsoleMode(handle, newMode))
                {
                    return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private const int STD_OUTPUT_HANDLE = -11;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
        private const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
    }
}
