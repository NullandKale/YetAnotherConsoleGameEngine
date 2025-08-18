using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ConsoleGame.Renderer
{
    public class Win32TerminalRenderer
    {
        private List<Framebuffer> frameBuffers;
        public int consoleWidth;
        public int consoleHeight;
        private CHAR_INFO[] backBuffer;
        private IntPtr hConsole;

        public Win32TerminalRenderer()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException("Win32TerminalRenderer requires Windows.");
            }

            frameBuffers = new List<Framebuffer>();
            consoleWidth = Console.WindowWidth - 1;
            consoleHeight = Console.WindowHeight - 2;
            backBuffer = new CHAR_INFO[consoleWidth * consoleHeight];

            hConsole = GetStdHandle(STD_OUTPUT_HANDLE);
            if (hConsole == IntPtr.Zero || hConsole == INVALID_HANDLE_VALUE)
            {
                throw new InvalidOperationException("Failed to get console handle.");
            }

            Console.CursorVisible = false;
            SetConsoleCursorPosition(hConsole, new COORD { X = 0, Y = 0 });
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
            return new Chexel(' ', Console.ForegroundColor, Console.BackgroundColor);
        }

        public void Render()
        {
            for (int y = 0; y < consoleHeight; y++)
            {
                for (int x = 0; x < consoleWidth; x++)
                {
                    Chexel c = GetChexelForPoint(x, y);
                    int i = (y * consoleWidth) + x;
                    backBuffer[i].UnicodeChar = c.Char == '\0' ? ' ' : c.Char;
                    backBuffer[i].Attributes = MapAttributes(c.ForegroundColor, c.BackgroundColor);
                }
            }

            COORD bufSize = new COORD { X = (short)consoleWidth, Y = (short)consoleHeight };
            COORD bufCoord = new COORD { X = 0, Y = 0 };
            SMALL_RECT region = new SMALL_RECT { Left = 0, Top = 0, Right = (short)(consoleWidth - 1), Bottom = (short)(consoleHeight - 1) };

            SetConsoleCursorPosition(hConsole, new COORD { X = 0, Y = 0 });

            bool ok = WriteConsoleOutputW(hConsole, backBuffer, bufSize, bufCoord, ref region);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException("WriteConsoleOutputW failed with error " + err);
            }

            SetConsoleCursorPosition(hConsole, new COORD { X = 0, Y = 0 });
        }

        private static ushort MapAttributes(ConsoleColor fg, ConsoleColor bg)
        {
            return (ushort)(((int)fg & 0x0F) | (((int)bg & 0x0F) << 4));
        }

        private const int STD_OUTPUT_HANDLE = -11;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct COORD
        {
            public short X;
            public short Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SMALL_RECT
        {
            public short Left;
            public short Top;
            public short Right;
            public short Bottom;
        }

        [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
        private struct CHAR_INFO
        {
            [FieldOffset(0)]
            public char UnicodeChar;
            [FieldOffset(2)]
            public ushort Attributes;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool WriteConsoleOutputW(IntPtr hConsoleOutput, [In] CHAR_INFO[] lpBuffer, COORD dwBufferSize, COORD dwBufferCoord, ref SMALL_RECT lpWriteRegion);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCursorPosition(IntPtr hConsoleOutput, COORD dwCursorPosition);
    }
}
