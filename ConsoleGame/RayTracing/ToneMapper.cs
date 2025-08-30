using System;
using ConsoleGame.Threads;

namespace ConsoleGame.RayTracing
{
    public sealed class ToneMapper
    {
        // Tone mapping parameters
        private float toneExposure = 1f; // Multiplies scene brightness before the curve; increase to make the image brighter, decrease to make it darker (too high risks highlight clipping before the curve).
        private float toneGamma = 2.2f; // Output gamma encoding; higher values darken midtones, lower values brighten midtones (does not change linear light energy, only display mapping).

        // Auto-exposure parameters
        private bool autoExposure = true; // Enables adaptive exposure; true lets exposure adjust from scene luminance, false keeps it fixed at toneExposure * aeExposure.
        private float aeKey = 0.18f; // Target “middle gray” after mapping; lowering darkens globally, raising brightens (targetExp = aeKey / avgLum).
        private float aeSpeed = 0.2f; // Adaptation speed per update; higher reacts faster (snappier), lower reacts slower (smoother).
        private float aeExposure = 1.0f; // Current auto-exposure level; normally driven by auto-exposure, can be set manually to override.
        private float aeMin = 0.10f; // Lower clamp on auto-exposure; raising prevents overly dark adaptation.
        private float aeMax = 1.50f; // Upper clamp on auto-exposure; lowering prevents overly bright adaptation.

        // The currently effective exposure (toneExposure * aeExposure)
        private float effectiveExposure = 1.0f;

        /// <summary>Configure manual exposure and gamma for tone mapping.</summary>
        public void SetToneMapping(float exposure, float gamma)
        {
            toneExposure = MathF.Max(0.001f, exposure);
            toneGamma = MathF.Max(0.1f, gamma);
        }

        /// <summary>Enable/disable auto-exposure and its parameters.</summary>
        public void SetAutoExposure(bool enabled, float key = 0.18f, float speed = 0.2f, float minExposure = 0.10f, float maxExposure = 1.50f)
        {
            autoExposure = enabled;
            aeKey = MathF.Max(1e-4f, key);
            aeSpeed = MathF.Max(0.0f, speed);
            aeMin = MathF.Max(0.001f, minExposure);
            aeMax = MathF.Max(aeMin, maxExposure);
        }

        /// <summary>Get the current effective exposure (toneExposure * auto-exposure level).</summary>
        public float EffectiveExposure
        {
            get { return effectiveExposure; }
        }

        /// <summary>
        /// Update exposure using log-average luminance sampling (single-threaded).
        /// Call this each frame before mapping to adapt exposure (if autoExposure is enabled).
        /// </summary>
        public void UpdateExposure(Vec3[,] hdrBuffer, bool[,] optionalSkyMask = null, int sampleStep = 2)
        {
            if (hdrBuffer == null) throw new ArgumentNullException(nameof(hdrBuffer));
            int w = hdrBuffer.GetLength(0);
            int h = hdrBuffer.GetLength(1);
            if (optionalSkyMask != null && (optionalSkyMask.GetLength(0) != w || optionalSkyMask.GetLength(1) != h)) throw new ArgumentException("Sky mask size does not match HDR buffer.");

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

        /// <summary>
        /// Update exposure using log-average luminance sampling (parallel over rows) with your FixedThreadFor.
        /// workerCount should match the same value you use elsewhere (e.g., procCount).
        /// </summary>
        public void UpdateExposure(Vec3[,] hdrBuffer, FixedThreadFor threadpool, int workerCount, bool[,] optionalSkyMask = null, int sampleStep = 2)
        {
            if (hdrBuffer == null) throw new ArgumentNullException(nameof(hdrBuffer));
            if (threadpool == null) { UpdateExposure(hdrBuffer, optionalSkyMask, sampleStep); return; }
            int w = hdrBuffer.GetLength(0);
            int h = hdrBuffer.GetLength(1);
            if (optionalSkyMask != null && (optionalSkyMask.GetLength(0) != w || optionalSkyMask.GetLength(1) != h)) throw new ArgumentException("Sky mask size does not match HDR buffer.");

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

        /// <summary>
        /// Tone-map a single HDR color (Vec3 in linear HDR) to SDR (linear 0-1 range), using current exposure & gamma.
        /// </summary>
        public Vec3 MapPixel(Vec3 hdrLinear)
        {
            Vec3 sdr = ToneMapAndEncode(hdrLinear, effectiveExposure, toneGamma);
            return sdr;
        }

        /// <summary>
        /// Tone-map an entire HDR buffer to an SDR buffer (single-threaded).
        /// </summary>
        public Vec3[,] MapBuffer(Vec3[,] hdrBuffer)
        {
            if (hdrBuffer == null) throw new ArgumentNullException(nameof(hdrBuffer));
            int w = hdrBuffer.GetLength(0);
            int h = hdrBuffer.GetLength(1);
            Vec3[,] outBuf = new Vec3[w, h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    outBuf[x, y] = ToneMapAndEncode(hdrBuffer[x, y], effectiveExposure, toneGamma);
                }
            }

            return outBuf;
        }

        /// <summary>
        /// Tone-map an entire HDR buffer to an SDR buffer in parallel using your FixedThreadFor.
        /// workerCount should match the same value you use elsewhere (e.g., procCount).
        /// </summary>
        public Vec3[,] MapBuffer(Vec3[,] hdrBuffer, FixedThreadFor threadpool, int workerCount)
        {
            if (hdrBuffer == null) throw new ArgumentNullException(nameof(hdrBuffer));
            if (threadpool == null) return MapBuffer(hdrBuffer);

            int w = hdrBuffer.GetLength(0);
            int h = hdrBuffer.GetLength(1);
            Vec3[,] outBuf = new Vec3[w, h];

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

        /// <summary>
        /// Internal helper: ACES filmic tonemap + gamma encode.
        /// Returns SDR linear color with components clamped to [0,1] after gamma encode.
        /// </summary>
        private static Vec3 ToneMapAndEncode(Vec3 hdr, float exposure, float gamma)
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

            return new Vec3(Saturate01(sr), Saturate01(sg), Saturate01(sb));
        }

        /// <summary>Clamp value to [0,1].</summary>
        private static float Saturate01(float v)
        {
            if (v < 0.0f) return 0.0f;
            if (v > 1.0f) return 1.0f;
            return v;
        }

        /// <summary>
        /// ACES filmic tone mapping curve (Krzysztof Narkowicz fit).
        /// </summary>
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
