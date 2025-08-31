using System;
using ConsoleGame.Threads;

namespace ConsoleGame.RayTracing
{
    public sealed class ToneMapper
    {
        private float toneExposure = 1f;
        private float toneGamma = 2.2f;

        private bool autoExposure = true;
        private float aeKey = 0.18f;
        private float aeSpeed = 0.2f;
        private float aeExposure = 1.0f;
        private float aeMin = 0.10f;
        private float aeMax = 1.50f;

        private float effectiveExposure = 1.0f;

        private float toneSaturation = 2.0f;
        private float toneVibrance = 0.0f;

        public void SetToneMapping(float exposure, float gamma)
        {
            toneExposure = MathF.Max(0.001f, exposure);
            toneGamma = MathF.Max(0.1f, gamma);
        }

        public void SetSaturation(float saturation = 1.20f, float vibrance = 0.0f)
        {
            toneSaturation = MathF.Max(0.0f, saturation);
            toneVibrance = MathF.Max(-1.0f, MathF.Min(1.0f, vibrance));
        }

        public void SetAutoExposure(bool enabled, float key = 0.18f, float speed = 0.2f, float minExposure = 0.10f, float maxExposure = 1.50f)
        {
            autoExposure = enabled;
            aeKey = MathF.Max(1e-4f, key);
            aeSpeed = MathF.Max(0.0f, speed);
            aeMin = MathF.Max(0.001f, minExposure);
            aeMax = MathF.Max(aeMin, maxExposure);
        }

        public float EffectiveExposure
        {
            get { return effectiveExposure; }
        }

        public void UpdateExposure(Fast2D<Vec3> hdrBuffer, Fast2D<bool> optionalSkyMask = null, int sampleStep = 2)
        {
            if (hdrBuffer == null) throw new ArgumentNullException(nameof(hdrBuffer));
            int w = hdrBuffer.Width;
            int h = hdrBuffer.Height;
            if (optionalSkyMask != null && (optionalSkyMask.Width != w || optionalSkyMask.Height != h)) throw new ArgumentException("Sky mask size does not match HDR buffer.");

            if (!autoExposure)
            {
                effectiveExposure = toneExposure * aeExposure;
                return;
            }

            int step = Math.Max(2, sampleStep);
            float logSum = 0.0f;
            int cnt = 0;

            for (int py = 0; py < h; py += step)
            {
                for (int px = 0; px < w; px += step)
                {
                    if (optionalSkyMask != null && optionalSkyMask[px, py]) continue;
                    Vec3 c = hdrBuffer[px, py];
                    float lum = 0.2126f * (float)c.X + 0.7152f * (float)c.Y + 0.0722f * (float)c.Z;
                    if (lum > 0.0f)
                    {
                        logSum += MathF.Log(1e-6f + lum);
                        cnt++;
                    }
                }
            }

            float avgLog = cnt > 0 ? logSum / Math.Max(1, cnt) : 0.0f;
            float avgLum = MathF.Exp(avgLog);
            float targetExp = cnt > 0 ? aeKey / MathF.Max(1e-6f, avgLum) : aeExposure;
            if (targetExp < aeMin) targetExp = aeMin;
            if (targetExp > aeMax) targetExp = aeMax;

            float s = 1.0f - MathF.Exp(-aeSpeed);
            aeExposure = aeExposure + (targetExp - aeExposure) * s;

            effectiveExposure = toneExposure * aeExposure;
        }

        public void UpdateExposure(Fast2D<Vec3> hdrBuffer, FixedThreadFor threadpool, int workerCount, Fast2D<bool> optionalSkyMask = null, int sampleStep = 2)
        {
            if (hdrBuffer == null) throw new ArgumentNullException(nameof(hdrBuffer));
            if (threadpool == null) { UpdateExposure(hdrBuffer, optionalSkyMask, sampleStep); return; }
            int w = hdrBuffer.Width;
            int h = hdrBuffer.Height;
            if (optionalSkyMask != null && (optionalSkyMask.Width != w || optionalSkyMask.Height != h)) throw new ArgumentException("Sky mask size does not match HDR buffer.");

            if (!autoExposure)
            {
                effectiveExposure = toneExposure * aeExposure;
                return;
            }

            int step = Math.Max(2, sampleStep);
            float[] partialLog = new float[Math.Max(1, workerCount)];
            int[] partialCnt = new int[Math.Max(1, workerCount)];

            threadpool.For(0, workerCount, worker =>
            {
                int yStart = worker * h / workerCount;
                int yEnd = (worker + 1) * h / workerCount;
                float lsum = 0.0f;
                int lcnt = 0;
                for (int py = yStart; py < yEnd; py += step)
                {
                    for (int px = 0; px < w; px += step)
                    {
                        if (optionalSkyMask != null && optionalSkyMask[px, py]) continue;
                        Vec3 c = hdrBuffer[px, py];
                        float lum = 0.2126f * (float)c.X + 0.7152f * (float)c.Y + 0.0722f * (float)c.Z;
                        if (lum > 0.0f)
                        {
                            lsum += MathF.Log(1e-6f + lum);
                            lcnt++;
                        }
                    }
                }
                partialLog[worker] = lsum;
                partialCnt[worker] = lcnt;
            });

            float logSum = 0.0f;
            int cnt = 0;
            for (int i = 0; i < Math.Max(1, workerCount); i++)
            {
                logSum += partialLog[i];
                cnt += partialCnt[i];
            }

            float avgLog = cnt > 0 ? logSum / Math.Max(1, cnt) : 0.0f;
            float avgLum = MathF.Exp(avgLog);
            float targetExp = cnt > 0 ? aeKey / MathF.Max(1e-6f, avgLum) : aeExposure;
            if (targetExp < aeMin) targetExp = aeMin;
            if (targetExp > aeMax) targetExp = aeMax;

            float s = 1.0f - MathF.Exp(-aeSpeed);
            aeExposure = aeExposure + (targetExp - aeExposure) * s;

            effectiveExposure = toneExposure * aeExposure;
        }

        public Vec3 MapPixel(Vec3 hdrLinear)
        {
            Vec3 sdr = ToneMapAndEncode(hdrLinear, effectiveExposure, toneGamma);
            return sdr;
        }

        public Fast2D<Vec3> MapBuffer(Fast2D<Vec3> hdrBuffer)
        {
            if (hdrBuffer == null) throw new ArgumentNullException(nameof(hdrBuffer));
            int w = hdrBuffer.Width;
            int h = hdrBuffer.Height;
            Fast2D<Vec3> outBuf = new Fast2D<Vec3>(w, h);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    outBuf[x, y] = ToneMapAndEncode(hdrBuffer[x, y], effectiveExposure, toneGamma);
                }
            }

            return outBuf;
        }

        public Fast2D<Vec3> MapBuffer(Fast2D<Vec3> hdrBuffer, FixedThreadFor threadpool, int workerCount)
        {
            if (hdrBuffer == null) throw new ArgumentNullException(nameof(hdrBuffer));
            if (threadpool == null) return MapBuffer(hdrBuffer);

            int w = hdrBuffer.Width;
            int h = hdrBuffer.Height;
            Fast2D<Vec3> outBuf = new Fast2D<Vec3>(w, h);

            threadpool.For(0, workerCount, worker =>
            {
                int yStart = worker * h / workerCount;
                int yEnd = (worker + 1) * h / workerCount;
                for (int y = yStart; y < yEnd; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        outBuf[x, y] = ToneMapAndEncode(hdrBuffer[x, y], effectiveExposure, toneGamma);
                    }
                }
            });

            return outBuf;
        }

        private Vec3 ToneMapAndEncode(Vec3 hdr, float exposure, float gamma)
        {
            float r = MathF.Max(0.0f, (float)hdr.X) * exposure;
            float g = MathF.Max(0.0f, (float)hdr.Y) * exposure;
            float b = MathF.Max(0.0f, (float)hdr.Z) * exposure;

            r = ACESFilm(r);
            g = ACESFilm(g);
            b = ACESFilm(b);

            float invGamma = 1.0f / MathF.Max(0.1f, gamma);
            float sr = MathF.Pow(Saturate01(r), invGamma);
            float sg = MathF.Pow(Saturate01(g), invGamma);
            float sb = MathF.Pow(Saturate01(b), invGamma);

            Vec3 outSRGB = ApplySaturation(new Vec3(sr, sg, sb));
            return outSRGB;
        }

        private Vec3 ApplySaturation(Vec3 srgb01)
        {
            float r = Saturate01((float)srgb01.X);
            float g = Saturate01((float)srgb01.Y);
            float b = Saturate01((float)srgb01.Z);
            float y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            float maxc = MathF.Max(r, MathF.Max(g, b));
            float minc = MathF.Min(r, MathF.Min(g, b));
            float chroma = maxc - minc;
            float vibFactor = 1.0f + toneVibrance * (1.0f - chroma);
            float f = toneSaturation * vibFactor;
            float rr = y + (r - y) * f;
            float gg = y + (g - y) * f;
            float bb = y + (b - y) * f;
            return new Vec3(Saturate01(rr), Saturate01(gg), Saturate01(bb));
        }

        private static float Saturate01(float v)
        {
            if (v < 0.0f) return 0.0f;
            if (v > 1.0f) return 1.0f;
            return v;
        }

        private static float ACESFilm(float x)
        {
            float a = 2.51f;
            float b = 0.03f;
            float c = 2.43f;
            float d = 0.59f;
            float e = 0.14f;
            float num = x * (a * x + b);
            float den = x * (c * x + d) + e;
            float y = den > 0.0f ? num / den : 0.0f;
            if (y < 0.0f) y = 0.0f;
            if (y > 1.0f) y = 1.0f;
            return y;
        }
    }
}
