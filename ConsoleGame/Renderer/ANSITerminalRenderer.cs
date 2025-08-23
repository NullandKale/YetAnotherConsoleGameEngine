using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ConsoleGame.Renderer
{
    public class ANSITerminalRenderer
    {
        private readonly List<Framebuffer> frameBuffers;
        public int consoleWidth;
        public int consoleHeight;
        private readonly ConsoleColor defaultFg;
        private readonly ConsoleColor defaultBg;
        private readonly Action<int, int> onResize;

        private readonly bool isWindows;
        private readonly bool vtEnabled;
        private readonly IntPtr hStdOut;
        private readonly byte[] zeroSeq = new byte[] { 0x1B, (byte)'[', (byte)'0', (byte)'m' };

        private byte[] outBuf = new byte[1 << 20];
        private int outLen = 0;

        private static readonly byte[] s_cubeSrgb = new byte[] { 0, 95, 135, 175, 215, 255 };
        private static readonly double[] s_cubeLinear = new double[6];
        private static readonly byte[] s_graySrgb = new byte[24];
        private static readonly double[] s_grayLinear = new double[24];

        static ANSITerminalRenderer()
        {
            for (int i = 0; i < 6; i++)
            {
                s_cubeLinear[i] = Srgb8ToLinearNoClamp(s_cubeSrgb[i]);
            }
            for (int i = 0; i < 24; i++)
            {
                int v = 8 + 10 * i;
                s_graySrgb[i] = (byte)v;
                s_grayLinear[i] = Srgb8ToLinearNoClamp(v);
            }
        }

        public ANSITerminalRenderer(Action<int, int> onResize)
        {
            this.onResize = onResize;
            frameBuffers = new List<Framebuffer>();
            consoleWidth = Console.WindowWidth;
            consoleHeight = Console.WindowHeight - 1;
            defaultFg = Console.ForegroundColor;
            defaultBg = Console.BackgroundColor;
            Console.CursorVisible = false;

            isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            if (isWindows)
            {
                hStdOut = GetStdHandle(STD_OUTPUT_HANDLE);
                vtEnabled = EnableVirtualTerminalProcessing(hStdOut);
                SetConsoleOutputCP(65001);
            }
            else
            {
                vtEnabled = true;
            }

            AppendAscii("\x1b[?25l");
            Flush();
            outLen = 0;
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
                onResize?.Invoke(consoleWidth, consoleHeight);
                sizeChanged = true;
            }

            EnsureCapacity(64 + consoleWidth * consoleHeight * 16);
            outLen = 0;

            if (sizeChanged)
            {
                AppendAscii("\x1b[2J\x1b[H");
            }

            int currentFgIdx = -1;
            int currentBgIdx = -1;

            for (int y = 0; y < consoleHeight; y++)
            {
                AppendAscii("\x1b[");
                AppendInt(y + 1);
                AppendAscii(";1H");

                for (int x = 0; x < consoleWidth; x++)
                {
                    Chexel c = GetChexelForPoint(x, y);
                    int fgIdx = ChexelToAnsi256(c.ForegroundColor);
                    int bgIdx = ChexelToAnsi256(c.BackgroundColor);

                    if (fgIdx != currentFgIdx && bgIdx != currentBgIdx)
                    {
                        AppendAscii("\x1b[38;5;");
                        AppendInt(fgIdx);
                        AppendAscii(";48;5;");
                        AppendInt(bgIdx);
                        AppendAscii("m");
                        currentFgIdx = fgIdx;
                        currentBgIdx = bgIdx;
                    }
                    else if (fgIdx != currentFgIdx)
                    {
                        AppendAscii("\x1b[38;5;");
                        AppendInt(fgIdx);
                        AppendAscii("m");
                        currentFgIdx = fgIdx;
                    }
                    else if (bgIdx != currentBgIdx)
                    {
                        AppendAscii("\x1b[48;5;");
                        AppendInt(bgIdx);
                        AppendAscii("m");
                        currentBgIdx = bgIdx;
                    }

                    AppendCharUtf8(c.Char);
                }
            }

            AppendBytes(zeroSeq);

            Flush();
            outLen = 0;
        }

        private void EnsureCapacity(int neededAdditional)
        {
            int needed = outLen + neededAdditional;
            if (needed <= outBuf.Length) return;
            int newCap = outBuf.Length;
            while (newCap < needed) newCap <<= 1;
            Array.Resize(ref outBuf, newCap);
        }

        private void AppendAscii(string s)
        {
            int n = s.Length;
            EnsureCapacity(n);
            for (int i = 0; i < n; i++)
            {
                outBuf[outLen++] = (byte)s[i];
            }
        }

        private void AppendBytes(byte[] src)
        {
            EnsureCapacity(src.Length);
            Buffer.BlockCopy(src, 0, outBuf, outLen, src.Length);
            outLen += src.Length;
        }

        private void AppendInt(int v)
        {
            if (v == 0)
            {
                EnsureCapacity(1);
                outBuf[outLen++] = (byte)'0';
                return;
            }
            int tmp = v;
            int digits = 0;
            while (tmp > 0) { tmp /= 10; digits++; }
            EnsureCapacity(digits);
            int pos = outLen + digits - 1;
            int val = v;
            while (val > 0)
            {
                int d = val % 10;
                outBuf[pos--] = (byte)('0' + d);
                val /= 10;
            }
            outLen += digits;
        }

        private void AppendCharUtf8(char ch)
        {
            if (ch <= 0x7F)
            {
                EnsureCapacity(1);
                outBuf[outLen++] = (byte)ch;
            }
            else if (ch <= 0x7FF)
            {
                EnsureCapacity(2);
                outBuf[outLen++] = (byte)(0xC0 | (ch >> 6));
                outBuf[outLen++] = (byte)(0x80 | (ch & 0x3F));
            }
            else
            {
                EnsureCapacity(3);
                outBuf[outLen++] = (byte)(0xE0 | (ch >> 12));
                outBuf[outLen++] = (byte)(0x80 | ((ch >> 6) & 0x3F));
                outBuf[outLen++] = (byte)(0x80 | (ch & 0x3F));
            }
        }

        private void Flush()
        {
            if (outLen <= 0) return;

            if (isWindows)
            {
                WriteFile(hStdOut, outBuf, outLen, out _, IntPtr.Zero);
            }
            else
            {
                using (var stdout = Console.OpenStandardOutput())
                {
                    stdout.Write(outBuf, 0, outLen);
                    stdout.Flush();
                }
            }
        }

        // ===== ANSI 256 mapping (sRGB distance, gray only for near-gray colors) =====

        private static int ChexelToAnsi256(ChexelColor cc)
        {
            double rLin = cc.color_f32.X;
            double gLin = cc.color_f32.Y;
            double bLin = cc.color_f32.Z;
            if (rLin < 0.0) rLin = 0.0; if (rLin > 1.0) rLin = 1.0;
            if (gLin < 0.0) gLin = 0.0; if (gLin > 1.0) gLin = 1.0;
            if (bLin < 0.0) bLin = 0.0; if (bLin > 1.0) bLin = 1.0;

            byte rSrgb = LinearToSrgb8(rLin);
            byte gSrgb = LinearToSrgb8(gLin);
            byte bSrgb = LinearToSrgb8(bLin);

            int ir = ToCubeLevelSrgb(rSrgb);
            int ig = ToCubeLevelSrgb(gSrgb);
            int ib = ToCubeLevelSrgb(bSrgb);
            int idxCube = 16 + 36 * ir + 6 * ig + ib;

            int cubeR = s_cubeSrgb[ir];
            int cubeG = s_cubeSrgb[ig];
            int cubeB = s_cubeSrgb[ib];

            byte ySrgb = LinearToSrgb8(0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin);
            int grayIdx = (int)Math.Round((ySrgb - 8.0) / 10.0);
            if (grayIdx < 0) grayIdx = 0;
            if (grayIdx > 23) grayIdx = 23;
            int grayV = s_graySrgb[grayIdx];
            int idxGray = 232 + grayIdx;

            int drg = Math.Abs(rSrgb - gSrgb);
            int drb = Math.Abs(rSrgb - bSrgb);
            int dgb = Math.Abs(gSrgb - bSrgb);
            int chroma = Math.Max(drg, Math.Max(drb, dgb));

            bool allowGray = chroma <= 18;

            int dCube = Dist2Srgb(rSrgb, gSrgb, bSrgb, cubeR, cubeG, cubeB);
            int dGray = allowGray ? Dist2Srgb(rSrgb, gSrgb, bSrgb, grayV, grayV, grayV) + 64 : int.MaxValue;

            return dGray < dCube ? idxGray : idxCube;
        }

        private static int ToCubeLevelSrgb(byte v)
        {
            if (v < 48) return 0;
            if (v < 114) return 1;
            if (v < 154) return 2;
            if (v < 194) return 3;
            if (v < 234) return 4;
            return 5;
        }

        private static byte LinearToSrgb8(double c)
        {
            if (c < 0.0) c = 0.0;
            if (c > 1.0) c = 1.0;
            double s = c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
            int v = (int)Math.Round(s * 255.0);
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return (byte)v;
        }

        private static double Srgb8ToLinearNoClamp(int v)
        {
            double s = v / 255.0;
            if (s <= 0.04045) return s / 12.92;
            return Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        private static int Dist2Srgb(int r1, int g1, int b1, int r2, int g2, int b2)
        {
            int dr = r1 - r2;
            int dg = g1 - g2;
            int db = b1 - b2;
            return dr * dr + dg * dg + db * db;
        }

        // ===== Legacy helpers kept for reference =====

        private static int ToCubeLevel(int v)
        {
            int level = (int)Math.Round(v / 255.0 * 5.0);
            if (level < 0) level = 0;
            if (level > 5) level = 5;
            return level;
        }

        private static int CubeValue(int level)
        {
            switch (level)
            {
                case 0: return 0;
                case 1: return 95;
                case 2: return 135;
                case 3: return 175;
                case 4: return 215;
                default: return 255;
            }
        }

        private static int ToGrayIndex(int R, int G, int B)
        {
            double avg = (R + G + B) / 3.0;
            int idx = (int)Math.Round((avg - 8.0) / 10.0);
            if (idx < 0) idx = 0;
            if (idx > 23) idx = 23;
            return idx;
        }

        private static int Dist2(int r1, int g1, int b1, int r2, int g2, int b2)
        {
            int dr = r1 - r2;
            int dg = g1 - g2;
            int db = b1 - b2;
            return dr * dr + dg * dg + db * db;
        }

        // ===== Win32 interop =====

        private const int STD_OUTPUT_HANDLE = -11;
        private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
        private const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr hFile, byte[] buffer, int numBytesToWrite, out int numBytesWritten, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleOutputCP(uint wCodePageID);

        private static bool EnableVirtualTerminalProcessing(IntPtr handle)
        {
            try
            {
                if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;
                if (!GetConsoleMode(handle, out uint mode)) return false;
                uint newMode = mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING | DISABLE_NEWLINE_AUTO_RETURN;
                return SetConsoleMode(handle, newMode);
            }
            catch { return false; }
        }
    }
}
