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

        // Constructor flags kept for API compatibility; dithering is no longer used.
        private readonly bool useDither;
        private readonly float ditherStrength;

        public VideoRenderer(Framebuffer framebuffer, string videoFile, int superSample = 1, bool requestRGBA = false, bool singleFrameAdvance = false, bool playAudio = true, bool enableDither = false, float ditherStrength = 0.0f)
        {
            if (framebuffer == null) throw new ArgumentNullException(nameof(framebuffer));
            fbW = framebuffer.Width;
            fbH = framebuffer.Height;
            ss = Math.Max(1, superSample);
            hiW = fbW * ss;
            hiH = fbH * 2 * ss;

            useRGBA = requestRGBA;
            useDither = false;
            this.ditherStrength = 0.0f;

            reader = new AsyncFfmpegVideoReader(videoFile, singleFrameAdvance: singleFrameAdvance, useRGBA: useRGBA, playAudio: playAudio);

            frameBuffer = new Chexel[fbW, fbH];
            threadpool = new FixedThreadFor(Environment.ProcessorCount, "Video Render Threads");
        }

        public VideoRenderer(Framebuffer framebuffer, int cameraIndex, int superSample = 1, bool requestRGBA = false, bool singleFrameAdvance = false, float forcedAspect = 0.0f, bool enableDither = false, float ditherStrength = 0.0f)
        {
            if (framebuffer == null) throw new ArgumentNullException(nameof(framebuffer));
            fbW = framebuffer.Width;
            fbH = framebuffer.Height;
            ss = Math.Max(1, superSample);
            hiW = fbW * ss;
            hiH = fbH * 2 * ss;

            useRGBA = requestRGBA;
            useDither = false;
            this.ditherStrength = 0.0f;

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

                                Vec3 cTop = SampleSourceLanczos(ptr, srcW, srcH, bpp, sxTop, syTop);
                                Vec3 cBot = SampleSourceLanczos(ptr, srcW, srcH, bpp, sxBot, syBot);

                                topSum = topSum + cTop;
                                botSum = botSum + cBot;
                            }
                        }

                        float inv = 1.0f / (ss * ss);
                        Vec3 topAvg = new Vec3(topSum.X * inv, topSum.Y * inv, topSum.Z * inv).Saturate();
                        Vec3 botAvg = new Vec3(botSum.X * inv, botSum.Y * inv, botSum.Z * inv).Saturate();

                        target[cx, cy] = new Chexel('▀', topAvg, botAvg);
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

        // --- Lanczos3 sampling (separable) ---

        private const int LanczosA = 3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Sinc(float x)
        {
            x = MathF.Abs(x);
            if (x < 1e-6f) return 1.0f;
            float pix = MathF.PI * x;
            return MathF.Sin(pix) / pix;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float LanczosKernel(float x, int a)
        {
            x = MathF.Abs(x);
            if (x >= a) return 0.0f;
            return Sinc(x) * Sinc(x / a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        private static Vec3 SampleSourceLanczos(IntPtr basePtr, int w, int h, int bpp, float x, float y)
        {
            if (w <= 0 || h <= 0) return Vec3.Zero;

            int x0 = (int)MathF.Floor(x);
            int y0 = (int)MathF.Floor(y);

            int ixStart = x0 - (LanczosA - 1);
            int ixEnd = x0 + LanczosA;
            int iyStart = y0 - (LanczosA - 1);
            int iyEnd = y0 + LanczosA;

            float[] wx = new float[2 * LanczosA];
            float[] wy = new float[2 * LanczosA];

            float sumWx = 0.0f;
            float sumWy = 0.0f;

            for (int i = 0, ix = ixStart; ix <= ixEnd; ix++, i++)
            {
                float k = LanczosKernel(x - ix, LanczosA);
                wx[i] = k;
                sumWx += k;
            }
            for (int j = 0, iy = iyStart; iy <= iyEnd; iy++, j++)
            {
                float k = LanczosKernel(y - iy, LanczosA);
                wy[j] = k;
                sumWy += k;
            }

            if (sumWx <= 0.0f || sumWy <= 0.0f) return SampleSourceBilinear(basePtr, w, h, bpp, x, y);

            float invWx = 1.0f / sumWx;
            float invWy = 1.0f / sumWy;

            for (int i = 0; i < wx.Length; i++) wx[i] *= invWx;
            for (int j = 0; j < wy.Length; j++) wy[j] *= invWy;

            float rAcc = 0.0f, gAcc = 0.0f, bAcc = 0.0f;

            for (int j = 0, iy = iyStart; iy <= iyEnd; iy++, j++)
            {
                int sy = Clamp(iy, 0, h - 1);
                float wyj = wy[j];
                for (int i = 0, ix = ixStart; ix <= ixEnd; ix++, i++)
                {
                    int sx = Clamp(ix, 0, w - 1);
                    float r, g, b; LoadPixel(basePtr, w, bpp, sx, sy, out r, out g, out b);
                    float wxy = wx[i] * wyj;
                    rAcc += r * wxy;
                    gAcc += g * wxy;
                    bAcc += b * wxy;
                }
            }

            return new Vec3(Clamp01(rAcc), Clamp01(gAcc), Clamp01(bAcc));
        }

        // Fallback bilinear (used for degenerate cases)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vec3 SampleSourceBilinear(IntPtr basePtr, int w, int h, int bpp, float x, float y)
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
