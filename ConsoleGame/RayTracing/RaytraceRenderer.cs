using ConsoleGame.RayTracing.Scenes;
using ConsoleGame.Renderer;
using ConsoleRayTracing;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleGame.RayTracing
{
    public sealed class RaytraceRenderer
    {
        private readonly Scene scene;
        private float fovDeg;
        private readonly int hiW;
        private readonly int hiH;
        private readonly int ss;

        private readonly int fbW;
        private readonly int fbH;

        private readonly Chexel[][,] buffers = new Chexel[2][,];
        private int frontIndex = 0;
        private int backIndex = 1;

        private readonly AutoResetEvent evtFrameReady = new AutoResetEvent(false);
        private readonly AutoResetEvent evtFlipDone = new AutoResetEvent(false);
        private volatile bool runRenderLoop = true;
        private Thread renderThread;
        private long frameCounter = 0;

        private readonly object camLock = new object();
        private Vec3 camPos = new Vec3(0.0, 1.0, 0.0);
        private float yaw = 0.0f;
        private float pitch = 0.0f;

        private const int DiffuseBounces = 1;
        private const int IndirectSamples = 1;
        private const int MaxMirrorBounces = 2;
        private const float MirrorThreshold = 0.9f;
        private const float Eps = 1e-4f;

        private const float MaxLuminance = 1.0f;
        private const float PaletteHysteresis = 0.02f;
        private const ulong SeedSalt = 0x9E3779B97F4A7C15UL;

        private readonly TaaAccumulator taa;

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

        private readonly Vec3[,] accumBaseTop;
        private readonly Vec3[,] accumBaseBot;

        private readonly Vec3[,] rawBaseTop;
        private readonly Vec3[,] rawBaseBot;
        private readonly Vec3[,] addTop;
        private readonly Vec3[,] addBot;
        private readonly bool[,] unstableTop;
        private readonly bool[,] unstableBot;
        private readonly bool[,] topHitValid;
        private readonly bool[,] botHitValid;
        private readonly Vec3[,] topHitPos;
        private readonly Vec3[,] botHitPos;

        private bool prevCamValid = false;
        private Vec3 prevCamPos;
        private float prevYaw = 0.0f;
        private float prevPitch = 0.0f;
        private float prevFovDeg = 0.0f;
        private float prevAspect = 1.0f;

        public RaytraceRenderer(Framebuffer framebuffer, Scene scene, float fovDeg, int pxW, int pxH, int superSample)
        {
            this.scene = scene;
            this.fovDeg = fovDeg;
            hiW = pxW;
            hiH = pxH;
            ss = Math.Max(1, superSample);

            fbW = framebuffer.Width;
            fbH = framebuffer.Height;

            buffers[0] = new Chexel[fbW, fbH];
            buffers[1] = new Chexel[fbW, fbH];

            accumBaseTop = new Vec3[fbW, fbH];
            accumBaseBot = new Vec3[fbW, fbH];

            rawBaseTop = new Vec3[fbW, fbH];
            rawBaseBot = new Vec3[fbW, fbH];
            addTop = new Vec3[fbW, fbH];
            addBot = new Vec3[fbW, fbH];
            unstableTop = new bool[fbW, fbH];
            unstableBot = new bool[fbW, fbH];
            topHitValid = new bool[fbW, fbH];
            botHitValid = new bool[fbW, fbH];
            topHitPos = new Vec3[fbW, fbH];
            botHitPos = new Vec3[fbW, fbH];

            taa = new TaaAccumulator(true, fbW, fbH, ss, 0.65f);

            scene.RebuildBVH();

            renderThread = new Thread(RenderLoop);
            renderThread.IsBackground = true;
            renderThread.Name = "Raytrace-RenderLoop";
            renderThread.Start();
        }

        public void SetCamera(Vec3 pos, float yaw, float pitch)
        {
            lock (camLock)
            {
                camPos = pos;
                this.yaw = yaw;
                this.pitch = pitch;
                taa.NotifyCamera(pos.X, pos.Y, pos.Z, yaw, pitch);
            }
        }

        public void SetFov(float fovDeg)
        {
            this.fovDeg = fovDeg;
        }

        public void TryFlipAndBlit(Framebuffer fb)
        {
            if (evtFrameReady.WaitOne(0))
            {
                int newFront = backIndex;
                int newBack = frontIndex;
                frontIndex = newFront;
                backIndex = newBack;
                evtFlipDone.Set();
            }

            Chexel[,] front = buffers[frontIndex];
            for (int cy = 0; cy < fbH; cy++)
            {
                for (int cx = 0; cx < fbW; cx++)
                {
                    fb.SetChexel(cx, cy, front[cx, cy]);
                }
            }
        }

        private void RenderLoop()
        {
            try
            {
                while (runRenderLoop)
                {
                    float aspect = hiW / (float)hiH;

                    Vec3 camPosSnapshot;
                    float yawSnapshot;
                    float pitchSnapshot;
                    lock (camLock)
                    {
                        camPosSnapshot = camPos;
                        yawSnapshot = yaw;
                        pitchSnapshot = pitch;
                    }

                    Camera cam = BuildCamera(camPosSnapshot, yawSnapshot, pitchSnapshot, fovDeg, aspect);

                    int procCount = Math.Max(1, Environment.ProcessorCount);
                    long frame = Interlocked.Increment(ref frameCounter);

                    Chexel[,] target = buffers[backIndex];
                    Chexel[,] prev = buffers[frontIndex];

                    int jx, jy;
                    taa.GetJitter(out jx, out jy);

                    Parallel.For(0, procCount, worker =>
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

                                Vec3 baseTopSum = Vec3.Zero;
                                Vec3 baseBotSum = Vec3.Zero;
                                Vec3 addTopSum = Vec3.Zero;
                                Vec3 addBotSum = Vec3.Zero;
                                bool firstUnstableTop = false;
                                bool firstUnstableBot = false;

                                Rng rng = new Rng(PerFrameSeed(cx, cy, frame, jx, jy));

                                for (int syi = 0; syi < ss; syi++)
                                {
                                    int syTop = ss > 1 ? (syi + jy) % ss : syi;
                                    int yTopPx = yTopPx0 + syTop;

                                    int syBot = ss > 1 ? (syi + jy) % ss : syi;
                                    int yBotPx = yBotPx0 + syBot;

                                    for (int sxi = 0; sxi < ss; sxi++)
                                    {
                                        int sx = ss > 1 ? (sxi + jx) % ss : sxi;
                                        int xPx = xPx0 + sx;

                                        bool uTop = false;
                                        Vec3 bTop, aTop;
                                        FirstHitAndAdd(scene, cam.MakeRay(xPx, yTopPx, hiW, hiH), ref rng, out bTop, out aTop, ref uTop);
                                        if (uTop) firstUnstableTop = true;
                                        baseTopSum = baseTopSum + bTop;
                                        addTopSum = addTopSum + aTop;

                                        bool uBot = false;
                                        Vec3 bBot, aBot;
                                        FirstHitAndAdd(scene, cam.MakeRay(xPx, yBotPx, hiW, hiH), ref rng, out bBot, out aBot, ref uBot);
                                        if (uBot) firstUnstableBot = true;
                                        baseBotSum = baseBotSum + bBot;
                                        addBotSum = addBotSum + aBot;
                                    }
                                }

                                float inv = 1.0f / (ss * ss);
                                Vec3 baseTopAvg = new Vec3(baseTopSum.X * inv, baseTopSum.Y * inv, baseTopSum.Z * inv);
                                Vec3 baseBotAvg = new Vec3(baseBotSum.X * inv, baseBotSum.Y * inv, baseBotSum.Z * inv);
                                Vec3 addTopAvg = new Vec3(addTopSum.X * inv, addTopSum.Y * inv, addTopSum.Z * inv);
                                Vec3 addBotAvg = new Vec3(addBotSum.X * inv, addBotSum.Y * inv, addBotSum.Z * inv);

                                baseTopAvg = ClampLuminance(baseTopAvg, MaxLuminance).Saturate();
                                baseBotAvg = ClampLuminance(baseBotAvg, MaxLuminance).Saturate();
                                addTopAvg = ClampLuminance(addTopAvg, MaxLuminance).Saturate();
                                addBotAvg = ClampLuminance(addBotAvg, MaxLuminance).Saturate();

                                rawBaseTop[cx, cy] = baseTopAvg;
                                rawBaseBot[cx, cy] = baseBotAvg;
                                addTop[cx, cy] = addTopAvg;
                                addBot[cx, cy] = addBotAvg;
                                unstableTop[cx, cy] = firstUnstableTop;
                                unstableBot[cx, cy] = firstUnstableBot;

                                int xCenter = xPx0 + ss / 2;
                                int yCenterTop = yTopPx0 + ss / 2;
                                int yCenterBot = yBotPx0 + ss / 2;

                                Vec3 hp;
                                if (TryPrimaryHitPosition(scene, cam.MakeRay(xCenter, yCenterTop, hiW, hiH), out hp))
                                {
                                    topHitValid[cx, cy] = true;
                                    topHitPos[cx, cy] = hp;
                                }
                                else
                                {
                                    topHitValid[cx, cy] = false;
                                }

                                if (TryPrimaryHitPosition(scene, cam.MakeRay(xCenter, yCenterBot, hiW, hiH), out hp))
                                {
                                    botHitValid[cx, cy] = true;
                                    botHitPos[cx, cy] = hp;
                                }
                                else
                                {
                                    botHitValid[cx, cy] = false;
                                }
                            }
                        }
                    });

                    for (int cy = 0; cy < fbH; cy++)
                    {
                        for (int cx = 0; cx < fbW; cx++)
                        {
                            bool havePrevTop = false;
                            bool havePrevBot = false;
                            float pTopR = 0.0f, pTopG = 0.0f, pTopB = 0.0f;
                            float pBotR = 0.0f, pBotG = 0.0f, pBotB = 0.0f;

                            if (prevCamValid)
                            {
                                if (topHitValid[cx, cy])
                                {
                                    float uPrev, vPrev;
                                    if (ProjectToUV(topHitPos[cx, cy], prevCamPos, prevYaw, prevPitch, prevFovDeg, prevAspect, out uPrev, out vPrev))
                                    {
                                        float xPrev = uPrev * (fbW - 1);
                                        float yPrev = vPrev * (fbH - 1);
                                        taa.SamplePrevTop(xPrev, yPrev, out pTopR, out pTopG, out pTopB);
                                        Vec3 prevBase = new Vec3(pTopR, pTopG, pTopB);
                                        Vec3 minN, maxN;
                                        NeighborhoodMinMax(rawBaseTop, cx, cy, out minN, out maxN);
                                        Vec3 clipped = ClipAABB(minN, maxN, prevBase);
                                        pTopR = clipped.X; pTopG = clipped.Y; pTopB = clipped.Z;
                                        havePrevTop = true;
                                    }
                                }
                                if (botHitValid[cx, cy])
                                {
                                    float uPrev, vPrev;
                                    if (ProjectToUV(botHitPos[cx, cy], prevCamPos, prevYaw, prevPitch, prevFovDeg, prevAspect, out uPrev, out vPrev))
                                    {
                                        float xPrev = uPrev * (fbW - 1);
                                        float yPrev = vPrev * (fbH - 1);
                                        taa.SamplePrevBot(xPrev, yPrev, out pBotR, out pBotG, out pBotB);
                                        Vec3 prevBase = new Vec3(pBotR, pBotG, pBotB);
                                        Vec3 minN, maxN;
                                        NeighborhoodMinMax(rawBaseBot, cx, cy, out minN, out maxN);
                                        Vec3 clipped = ClipAABB(minN, maxN, prevBase);
                                        pBotR = clipped.X; pBotG = clipped.Y; pBotB = clipped.Z;
                                        havePrevBot = true;
                                    }
                                }
                            }

                            float alphaTop = unstableTop[cx, cy] ? 1.0f : taa.Alpha;
                            float alphaBot = unstableBot[cx, cy] ? 1.0f : taa.Alpha;

                            float outBaseTopR, outBaseTopG, outBaseTopB, outBaseBotR, outBaseBotG, outBaseBotB;
                            taa.AccumulateReprojected(cx, cy, rawBaseTop[cx, cy].X, rawBaseTop[cx, cy].Y, rawBaseTop[cx, cy].Z, rawBaseBot[cx, cy].X, rawBaseBot[cx, cy].Y, rawBaseBot[cx, cy].Z, havePrevTop, pTopR, pTopG, pTopB, havePrevBot, pBotR, pBotG, pBotB, alphaTop, alphaBot, out outBaseTopR, out outBaseTopG, out outBaseTopB, out outBaseBotR, out outBaseBotG, out outBaseBotB);

                            Vec3 topSRGB = new Vec3(outBaseTopR + addTop[cx, cy].X, outBaseTopG + addTop[cx, cy].Y, outBaseTopB + addTop[cx, cy].Z).Saturate();
                            Vec3 botSRGB = new Vec3(outBaseBotR + addBot[cx, cy].X, outBaseBotG + addBot[cx, cy].Y, outBaseBotB + addBot[cx, cy].Z).Saturate();

                            accumBaseTop[cx, cy] = topSRGB;
                            accumBaseBot[cx, cy] = botSRGB;
                        }
                    }

                    for (int cy = 0; cy < fbH; cy++)
                    {
                        for (int cx = 0; cx < fbW; cx++)
                        {
                            ConsoleColor prevFg = prev[cx, cy].ForegroundColor;
                            ConsoleColor prevBg = prev[cx, cy].BackgroundColor;
                            ConsoleColor fg = NearestWithHysteresis(accumBaseTop[cx, cy], prevFg, PaletteHysteresis);
                            ConsoleColor bg = NearestWithHysteresis(accumBaseBot[cx, cy], prevBg, PaletteHysteresis);
                            target[cx, cy] = new Chexel('▀', fg, bg);
                        }
                    }

                    taa.EndFrame();

                    prevCamPos = camPosSnapshot;
                    prevYaw = yawSnapshot;
                    prevPitch = pitchSnapshot;
                    prevFovDeg = fovDeg;
                    prevAspect = aspect;
                    prevCamValid = true;

                    evtFrameReady.Set();
                    evtFlipDone.WaitOne();
                }
            }
            catch
            {
            }
        }

        public void SetTaaEnabled(bool enabled)
        {
            taa.SetEnabled(enabled);
        }

        public void SetTaaAlpha(float alpha)
        {
            taa.SetAlpha(alpha);
        }

        public void ResetTaaHistory()
        {
            taa.ResetHistory();
        }

        private static Camera BuildCamera(Vec3 pos, float yaw, float pitch, float fovDeg, float aspect)
        {
            Vec3 fwd = ForwardFromYawPitch(yaw, pitch);
            return new Camera(pos, pos + fwd, new Vec3(0.0, 1.0, 0.0), fovDeg, aspect);
        }

        private static Vec3 ForwardFromYawPitch(float yaw, float pitch)
        {
            float cp = MathF.Cos(pitch);
            return new Vec3(MathF.Sin(yaw) * cp, MathF.Sin(pitch), -MathF.Cos(yaw) * cp);
        }

        private static void FirstHitAndAdd(Scene scene, Ray r, ref Rng rng, out Vec3 baseCol, out Vec3 addCol, ref bool unstableFirst)
        {
            baseCol = Vec3.Zero;
            addCol = Vec3.Zero;

            HitRecord rec = default;
            if (!scene.Hit(r, 0.001f, float.MaxValue, ref rec))
            {
                unstableFirst = true;
                float tbg = 0.5f * (r.Dir.Y + 1.0f);
                baseCol = Lerp(scene.BackgroundBottom, scene.BackgroundTop, tbg);
                return;
            }

            if ((float)rec.Mat.Reflectivity >= MirrorThreshold)
            {
                unstableFirst = true;
                baseCol = rec.Mat.Emission;
                Vec3 reflDir = Reflect(r.Dir, rec.N).Normalized();
                Ray reflRay = new Ray(rec.P + rec.N * Eps, reflDir);
                Vec3 reflCol = TraceFull(scene, reflRay, 0, 0, ref rng);
                addCol += new Vec3(reflCol.X * rec.Mat.Albedo.X, reflCol.Y * rec.Mat.Albedo.Y, reflCol.Z * rec.Mat.Albedo.Z);
                return;
            }

            baseCol = rec.Mat.Emission;

            if (scene.Ambient.Intensity > 0.0f)
            {
                Vec3 a = new Vec3(scene.Ambient.Color.X * scene.Ambient.Intensity, scene.Ambient.Color.Y * scene.Ambient.Intensity, scene.Ambient.Color.Z * scene.Ambient.Intensity);
                baseCol += new Vec3(a.X * rec.Mat.Albedo.X, a.Y * rec.Mat.Albedo.Y, a.Z * rec.Mat.Albedo.Z);
            }

            for (int i = 0; i < scene.Lights.Count; i++)
            {
                PointLight light = scene.Lights[i];
                Vec3 toL = light.Position - rec.P;
                float dist2 = toL.Dot(toL);
                float dist = MathF.Sqrt(dist2);
                Vec3 ldir = toL / dist;

                float nDotL = MathF.Max(0.0f, rec.N.Dot(ldir));
                if (nDotL <= 0.0) continue;

                Ray shadow = new Ray(rec.P + rec.N * Eps, ldir);
                if (scene.Occluded(shadow, dist - Eps)) continue;

                float atten = light.Intensity / dist2;
                Vec3 lambert = rec.Mat.Albedo * (nDotL * atten) * light.Color;
                baseCol += lambert;
            }

            if (DiffuseBounces > 0)
            {
                Vec3 indirect = Vec3.Zero;
                for (int s = 0; s < IndirectSamples; s++)
                {
                    Vec3 bounceDir = CosineSampleHemisphere(rec.N, ref rng);
                    Ray bounce = new Ray(rec.P + rec.N * Eps, bounceDir);
                    Vec3 Li = TraceFull(scene, bounce, 0, 1, ref rng);
                    indirect += new Vec3(Li.X * rec.Mat.Albedo.X, Li.Y * rec.Mat.Albedo.Y, Li.Z * rec.Mat.Albedo.Z);
                }
                float invSpp = 1.0f / IndirectSamples;
                addCol += new Vec3(indirect.X * invSpp, indirect.Y * invSpp, indirect.Z * invSpp);
            }
        }

        private static Vec3 TraceFull(Scene scene, Ray r, int mirrorDepth, int diffuseDepth, ref Rng rng)
        {
            HitRecord rec = default;
            if (!scene.Hit(r, 0.001f, float.MaxValue, ref rec))
            {
                float tbg = 0.5f * (r.Dir.Y + 1.0f);
                return Lerp(scene.BackgroundBottom, scene.BackgroundTop, tbg);
            }

            Vec3 color = rec.Mat.Emission;

            if ((float)rec.Mat.Reflectivity >= MirrorThreshold)
            {
                if (mirrorDepth >= MaxMirrorBounces) return color;
                Vec3 reflDir = Reflect(r.Dir, rec.N).Normalized();
                Ray reflRay = new Ray(rec.P + rec.N * Eps, reflDir);
                Vec3 reflCol = TraceFull(scene, reflRay, mirrorDepth + 1, diffuseDepth, ref rng);
                color += new Vec3(reflCol.X * rec.Mat.Albedo.X, reflCol.Y * rec.Mat.Albedo.Y, reflCol.Z * rec.Mat.Albedo.Z);
                return color;
            }

            if (scene.Ambient.Intensity > 0.0f)
            {
                Vec3 a = new Vec3(scene.Ambient.Color.X * scene.Ambient.Intensity, scene.Ambient.Color.Y * scene.Ambient.Intensity, scene.Ambient.Color.Z * scene.Ambient.Intensity);
                color += new Vec3(a.X * rec.Mat.Albedo.X, a.Y * rec.Mat.Albedo.Y, a.Z * rec.Mat.Albedo.Z);
            }

            for (int i = 0; i < scene.Lights.Count; i++)
            {
                PointLight light = scene.Lights[i];
                Vec3 toL = light.Position - rec.P;
                float dist2 = toL.Dot(toL);
                float dist = MathF.Sqrt(dist2);
                Vec3 ldir = toL / dist;

                float nDotL = MathF.Max(0.0f, rec.N.Dot(ldir));
                if (nDotL <= 0.0) continue;

                Ray shadow = new Ray(rec.P + rec.N * Eps, ldir);
                if (scene.Occluded(shadow, dist - Eps)) continue;

                float atten = light.Intensity / dist2;
                Vec3 lambert = rec.Mat.Albedo * (nDotL * atten) * light.Color;
                color += lambert;
            }

            if (diffuseDepth < DiffuseBounces)
            {
                Vec3 indirect = Vec3.Zero;
                for (int s = 0; s < IndirectSamples; s++)
                {
                    Vec3 bounceDir = CosineSampleHemisphere(rec.N, ref rng);
                    Ray bounce = new Ray(rec.P + rec.N * Eps, bounceDir);
                    Vec3 Li = TraceFull(scene, bounce, mirrorDepth, diffuseDepth + 1, ref rng);
                    indirect += new Vec3(Li.X * rec.Mat.Albedo.X, Li.Y * rec.Mat.Albedo.Y, Li.Z * rec.Mat.Albedo.Z);
                }
                float invSpp = 1.0f / IndirectSamples;
                color += new Vec3(indirect.X * invSpp, indirect.Y * invSpp, indirect.Z * invSpp);
            }

            return color;
        }

        private static bool TryPrimaryHitPosition(Scene scene, Ray r, out Vec3 hitPos)
        {
            HitRecord rec = default;
            if (!scene.Hit(r, 0.001f, float.MaxValue, ref rec))
            {
                hitPos = default;
                return false;
            }
            hitPos = rec.P;
            return true;
        }

        private static bool ProjectToUV(Vec3 worldPos, Vec3 camPos, float yaw, float pitch, float fovDeg, float aspect, out float u, out float v)
        {
            Vec3 fwd = ForwardFromYawPitch(yaw, pitch);
            Vec3 right = fwd.Cross(new Vec3(0.0, 1.0, 0.0)).Normalized();
            if (right.Dot(right) < 1e-8f) right = new Vec3(1.0, 0.0, 0.0);
            Vec3 up = right.Cross(fwd).Normalized();

            Vec3 rel = worldPos - camPos;
            float cz = rel.Dot(fwd);
            if (cz <= 1e-4f)
            {
                u = 0.0f;
                v = 0.0f;
                return false;
            }
            float cx = rel.Dot(right);
            float cy = rel.Dot(up);

            float tanHalf = MathF.Tan(0.5f * fovDeg * (MathF.PI / 180.0f));
            float ix = cx / cz;
            float iy = cy / cz;

            u = 0.5f + 0.5f * (ix / (tanHalf * aspect));
            v = 0.5f - 0.5f * (iy / tanHalf);

            if (u < -0.5f || u > 1.5f || v < -0.5f || v > 1.5f) return false;
            return true;
        }

        private static Vec3 CosineSampleHemisphere(Vec3 n, ref Rng rng)
        {
            float u1 = rng.NextUnit();
            float u2 = rng.NextUnit();
            float r = MathF.Sqrt(u1);
            float theta = 2.0f * MathF.PI * u2;
            float x = r * MathF.Cos(theta);
            float y = r * MathF.Sin(theta);
            float z = MathF.Sqrt(MathF.Max(0.0f, 1.0f - u1));

            Vec3 w = n;
            Vec3 a = MathF.Abs(w.X) > 0.1 ? new Vec3(0.0, 1.0, 0.0) : new Vec3(1.0, 0.0, 0.0);
            Vec3 v = w.Cross(a).Normalized();
            Vec3 u = v.Cross(w);

            Vec3 dir = (u * x + v * y + w * z).Normalized();
            return dir;
        }

        private static Vec3 Reflect(Vec3 v, Vec3 n)
        {
            return v - n * (2.0f * v.Dot(n));
        }

        private static Vec3 Lerp(Vec3 a, Vec3 b, float t)
        {
            return a * (1.0f - t) + b * t;
        }

        private static ulong PerFrameSeed(int x, int y, long frame, int jx, int jy)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                h ^= (ulong)(x) * 0x9E3779B97F4A7C15UL; h = SplitMix64(h);
                h ^= (ulong)(y) * 0xC2B2AE3D27D4EB4FUL; h = SplitMix64(h);
                h ^= (ulong)frame * 0x165667B19E3779F9UL; h = SplitMix64(h);
                h ^= ((ulong)(byte)jx << 8) ^ (ulong)(byte)jy; h = SplitMix64(h);
                h ^= SeedSalt; h = SplitMix64(h);
                return h;
            }
        }

        private static ulong SplitMix64(ulong z)
        {
            unchecked
            {
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        private static Vec3 ClampLuminance(Vec3 c, float maxY)
        {
            float y = 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
            if (y <= maxY) return c;
            float s = maxY / MathF.Max(y, 1e-6f);
            return new Vec3(c.X * s, c.Y * s, c.Z * s);
        }

        private static ConsoleColor NearestWithHysteresis(Vec3 srgb, ConsoleColor prev, float hysteresis)
        {
            ConsoleColor cand = ConsolePalette.NearestColor(srgb);
            if (cand == prev) return prev;
            Vec3 prevRGB = FromConsole(prev);
            Vec3 candRGB = FromConsole(cand);
            float dPrev2 = Dist2(srgb, prevRGB);
            float dCand2 = Dist2(srgb, candRGB);
            float h2 = hysteresis * hysteresis;
            if (dCand2 + h2 >= dPrev2) return prev;
            return cand;
        }

        private static Vec3 FromConsole(ConsoleColor c)
        {
            int idx = (int)c;
            if (idx < 0 || idx >= Palette16.Length) return Palette16[0];
            return Palette16[idx];
        }

        private static float Dist2(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        private static void NeighborhoodMinMax(Vec3[,] src, int cx, int cy, out Vec3 minC, out Vec3 maxC)
        {
            int w = src.GetLength(0);
            int h = src.GetLength(1);
            float minR = 1e9f, minG = 1e9f, minB = 1e9f;
            float maxR = -1e9f, maxG = -1e9f, maxB = -1e9f;
            for (int dy = -1; dy <= 1; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= h) continue;
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= w) continue;
                    Vec3 c = src[x, y];
                    if (c.X < minR) minR = c.X; if (c.Y < minG) minG = c.Y; if (c.Z < minB) minB = c.Z;
                    if (c.X > maxR) maxR = c.X; if (c.Y > maxG) maxG = c.Y; if (c.Z > maxB) maxB = c.Z;
                }
            }
            minC = new Vec3(minR, minG, minB);
            maxC = new Vec3(maxR, maxG, maxB);
        }

        private static Vec3 ClipAABB(Vec3 minC, Vec3 maxC, Vec3 v)
        {
            float r = v.X < minC.X ? minC.X : (v.X > maxC.X ? maxC.X : v.X);
            float g = v.Y < minC.Y ? minC.Y : (v.Y > maxC.Y ? maxC.Y : v.Y);
            float b = v.Z < minC.Z ? minC.Z : (v.Z > maxC.Z ? maxC.Z : v.Z);
            return new Vec3(r, g, b);
        }
    }
}
