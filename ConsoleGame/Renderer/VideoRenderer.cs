using ConsoleGame.Renderer;
using ConsoleGame.Threads;
using ConsoleRayTracing;
using NullEngine.Video;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace ConsoleGame.RayTracing
{
    public sealed class VideoRenderer : IDisposable
    {
        private readonly IFrameReader reader;
        private readonly bool useRGBA;
        private readonly int ss;

        private readonly int fbW;
        private readonly int fbH;
        private readonly int hiW;
        private readonly int hiH;

        private readonly Chexel[,] frameBuffer;
        private readonly FixedThreadFor threadpool;

        private readonly bool useDither;
        private readonly float ditherStrength;

        private static readonly Vec3[] Palette16 = new Vec3[]
        {
            new Vec3(0.00f,0.00f,0.00f),
            new Vec3(0.00f,0.00f,0.50f),
            new Vec3(0.00f,0.50f,0.00f),
            new Vec3(0.00f,0.50f,0.50f),
            new Vec3(0.50f,0.00f,0.00f),
            new Vec3(0.50f,0.00f,0.50f),
            new Vec3(0.50f,0.50f,0.00f),
            new Vec3(0.75f,0.75f,0.75f),
            new Vec3(0.50f,0.50f,0.50f),
            new Vec3(0.00f,0.00f,1.00f),
            new Vec3(0.00f,1.00f,0.00f),
            new Vec3(0.00f,1.00f,1.00f),
            new Vec3(1.00f,0.00f,0.00f),
            new Vec3(1.00f,0.00f,1.00f),
            new Vec3(1.00f,1.00f,0.00f),
            new Vec3(1.00f,1.00f,1.00f)
        };

        private static readonly float[,] Bayer8 =
        {
            { 0f/64f, 48f/64f, 12f/64f, 60f/64f, 3f/64f, 51f/64f, 15f/64f, 63f/64f },
            { 32f/64f, 16f/64f, 44f/64f, 28f/64f, 35f/64f, 19f/64f, 47f/64f, 31f/64f },
            { 8f/64f, 56f/64f, 4f/64f, 52f/64f, 11f/64f, 59f/64f, 7f/64f, 55f/64f },
            { 40f/64f, 24f/64f, 36f/64f, 20f/64f, 43f/64f, 27f/64f, 39f/64f, 23f/64f },
            { 2f/64f, 50f/64f, 14f/64f, 62f/64f, 1f/64f, 49f/64f, 13f/64f, 61f/64f },
            { 34f/64f, 18f/64f, 46f/64f, 30f/64f, 33f/64f, 17f/64f, 45f/64f, 29f/64f },
            { 10f/64f, 58f/64f, 6f/64f, 54f/64f, 9f/64f, 57f/64f, 5f/64f, 53f/64f },
            { 42f/64f, 26f/64f, 38f/64f, 22f/64f, 41f/64f, 25f/64f, 37f/64f, 21f/64f }
        };

        public VideoRenderer(Framebuffer framebuffer, string videoFile, int superSample = 1, bool requestRGBA = false, bool singleFrameAdvance = false, bool playAudio = true, bool enableDither = true, float ditherStrength = 0.12f)
        {
            if (framebuffer == null) throw new ArgumentNullException(nameof(framebuffer));
            fbW = framebuffer.Width;
            fbH = framebuffer.Height;
            ss = Math.Max(1, superSample);
            hiW = fbW * ss;
            hiH = fbH * 2 * ss;

            useRGBA = requestRGBA;
            useDither = enableDither;
            this.ditherStrength = Clamp01(ditherStrength);

            reader = new AsyncFfmpegVideoReader(videoFile, singleFrameAdvance: singleFrameAdvance, useRGBA: useRGBA, playAudio: playAudio);

            frameBuffer = new Chexel[fbW, fbH];
            threadpool = new FixedThreadFor(Environment.ProcessorCount, "Video Render Threads");
        }

        public VideoRenderer(Framebuffer framebuffer, int cameraIndex, int superSample = 1, bool requestRGBA = false, bool singleFrameAdvance = false, float forcedAspect = 0.0f, bool enableDither = true, float ditherStrength = 0.12f)
        {
            if (framebuffer == null) throw new ArgumentNullException(nameof(framebuffer));
            fbW = framebuffer.Width;
            fbH = framebuffer.Height;
            ss = Math.Max(1, superSample);
            hiW = fbW * ss;
            hiH = fbH * 2 * ss;

            useRGBA = requestRGBA;
            useDither = enableDither;
            this.ditherStrength = Clamp01(ditherStrength);

            reader = forcedAspect > 0.0f ? new AsyncCameraReader(cameraIndex, forcedAspect, singleFrameAdvance, useRGBA) : new AsyncCameraReader(cameraIndex, singleFrameAdvance, useRGBA);

            frameBuffer = new Chexel[fbW, fbH];
            threadpool = new FixedThreadFor(Environment.ProcessorCount, "Video Render Threads");
        }

        public void TryFlipAndBlit(Framebuffer fb)
        {
            if (fb == null) throw new ArgumentNullException(nameof(fb));
            int srcW = reader.Width;
            int srcH = reader.Height;
            int bpp = useRGBA ? 4 : 3;

            float scaleX = hiW / (float)srcW;
            float scaleY = hiH / (float)srcH;
            float scale = scaleX < scaleY ? scaleX : scaleY;
            float dstW = srcW * scale;
            float dstH = srcH * scale;
            float offX = 0.5f * (hiW - dstW);
            float offY = 0.5f * (hiH - dstH);

            IntPtr ptr = reader.GetCurrentFramePtr();

            int procCount = Math.Max(1, Environment.ProcessorCount);
            Chexel[,] target = frameBuffer;

            threadpool.For(0, procCount, worker =>
            {
                int yStart = worker * fbH / procCount;
                int yEnd = (worker + 1) * fbH / procCount;

                for (int cy = yStart; cy < yEnd; cy++)
                {
                    int yTopPx0 = cy * 2 * ss;
                    int yBotPx0 = (cy * 2 + 1) * ss;

                    for (int cx = 0; cx < fbW; cx++)
                    {
                        int xPx0 = cx * ss;

                        Vec3 topSum = Vec3.Zero;
                        Vec3 botSum = Vec3.Zero;

                        for (int sy = 0; sy < ss; sy++)
                        {
                            int yTop = yTopPx0 + sy;
                            int yBot = yBotPx0 + sy;
                            for (int sx = 0; sx < ss; sx++)
                            {
                                int x = xPx0 + sx;

                                float sxTop = (x + 0.5f - offX) / scale;
                                float syTop = (yTop + 0.5f - offY) / scale;
                                float sxBot = (x + 0.5f - offX) / scale;
                                float syBot = (yBot + 0.5f - offY) / scale;

                                Vec3 cTop = SampleSource(ptr, srcW, srcH, bpp, sxTop, syTop);
                                Vec3 cBot = SampleSource(ptr, srcW, srcH, bpp, sxBot, syBot);

                                topSum = topSum + cTop;
                                botSum = botSum + cBot;
                            }
                        }

                        float inv = 1.0f / (ss * ss);
                        Vec3 topAvg = new Vec3(topSum.X * inv, topSum.Y * inv, topSum.Z * inv).Saturate();
                        Vec3 botAvg = new Vec3(botSum.X * inv, botSum.Y * inv, botSum.Z * inv).Saturate();

                        if (useDither)
                        {
                            topAvg = ApplyOrderedDither(topAvg, cx, cy, true, ditherStrength);
                            botAvg = ApplyOrderedDither(botAvg, cx, cy, false, ditherStrength);
                        }

                        ConsoleColor fg = NearestPalette(topAvg);
                        ConsoleColor bg = NearestPalette(botAvg);
                        target[cx, cy] = new Chexel('▀', fg, bg);
                    }
                }
            });

            threadpool.For(0, procCount, worker =>
            {
                int yStart = worker * fbH / procCount;
                int yEnd = (worker + 1) * fbH / procCount;

                for (int cy = yStart; cy < yEnd; cy++)
                {
                    for (int cx = 0; cx < fbW; cx++)
                    {
                        fb.SetChexel(cx, cy, target[cx, cy]);
                    }
                }
            });
        }

        public void Dispose()
        {
            reader?.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ConsoleColor NearestPalette(Vec3 srgb)
        {
            float r = srgb.X, g = srgb.Y, b = srgb.Z;
            int bestIdx = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < Palette16.Length; i++)
            {
                Vec3 p = Palette16[i];
                float dr = r - p.X;
                float dg = g - p.Y;
                float db = b - p.Z;
                float d = dr * dr + dg * dg + db * db;
                if (d < bestD)
                {
                    bestD = d;
                    bestIdx = i;
                }
            }
            return (ConsoleColor)bestIdx;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vec3 ApplyOrderedDither(Vec3 c, int cellX, int cellY, bool topHalf, float strength)
        {
            int dx = cellX & 7;
            int dy = ((cellY << 1) | (topHalf ? 0 : 1)) & 7;
            float t = Bayer8[dy, dx] - 0.5f;
            float s = strength;
            float r = Clamp01(c.X + t * s);
            float g = Clamp01(c.Y + t * s);
            float b = Clamp01(c.Z + t * s);
            return new Vec3(r, g, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vec3 SampleSource(IntPtr basePtr, int w, int h, int bpp, float x, float y)
        {
            if (x < 0.0f || y < 0.0f || x > (w - 1) || y > (h - 1)) return Vec3.Zero;

            int x0 = (int)MathF.Floor(x);
            int y0 = (int)MathF.Floor(y);
            int x1 = x0 + 1; if (x1 >= w) x1 = w - 1;
            int y1 = y0 + 1; if (y1 >= h) y1 = h - 1;

            float tx = x - x0;
            float ty = y - y0;

            float r00, g00, b00; LoadPixel(basePtr, w, bpp, x0, y0, out r00, out g00, out b00);
            float r10, g10, b10; LoadPixel(basePtr, w, bpp, x1, y0, out r10, out g10, out b10);
            float r01, g01, b01; LoadPixel(basePtr, w, bpp, x0, y1, out r01, out g01, out b01);
            float r11, g11, b11; LoadPixel(basePtr, w, bpp, x1, y1, out r11, out g11, out b11);

            float r0 = r00 * (1.0f - tx) + r10 * tx;
            float g0 = g00 * (1.0f - tx) + g10 * tx;
            float b0 = b00 * (1.0f - tx) + b10 * tx;

            float r1 = r01 * (1.0f - tx) + r11 * tx;
            float g1 = g01 * (1.0f - tx) + g11 * tx;
            float b1 = b01 * (1.0f - tx) + b11 * tx;

            return new Vec3(r0 * (1.0f - ty) + r1 * ty, g0 * (1.0f - ty) + g1 * ty, b0 * (1.0f - ty) + b1 * ty).Saturate();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void LoadPixel(IntPtr basePtr, int w, int bpp, int x, int y, out float r, out float g, out float b)
        {
            int offset = (y * w + x) * bpp;
            byte bB = Marshal.ReadByte(basePtr, offset + 0);
            byte bG = Marshal.ReadByte(basePtr, offset + 1);
            byte bR = Marshal.ReadByte(basePtr, offset + 2);
            r = bR / 255.0f;
            g = bG / 255.0f;
            b = bB / 255.0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Clamp01(float v)
        {
            if (v < 0.0f) return 0.0f;
            if (v > 1.0f) return 1.0f;
            return v;
        }
    }
}
