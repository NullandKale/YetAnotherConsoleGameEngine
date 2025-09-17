using ConsoleGame.RayTracing.Scenes;
using ConsoleGame.Renderer;
using ConsoleGame.Threads;
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
        private int hiW;
        private int hiH;
        private int ss;

        private int fbW;
        private int fbH;

        private Fast2D<Chexel> frameBuffer;

        private long frameCounter = 0;

        private readonly object camLock = new object();
        private Vec3 camPos = new Vec3(0.0, 1.0, 0.0);
        private float yaw = 0.0f;
        private float pitch = 0.0f;

        private const int DiffuseBounces = 1;
        private const int IndirectSamples = 1;
        private const int MaxMirrorBounces = 2;
        private const int MaxRefractions = 2;
        private const float MirrorThreshold = 0.9f;
        private const float Eps = 1e-4f;

        private const float MaxLuminance = 1.0f;
        private const ulong SeedSalt = 0x9E3779B97F4A7C15UL;

        private float taaAlpha = 0.01f;
        private const float MotionTransReset = 0.0025f;
        private const float MotionRotReset = 0.0025f;

        private Fast2D<Ray> rays;
        private Fast2D<Vec3> gAlbedo;
        private Fast2D<Vec3> gNormal;
        private Fast2D<float> gDepth;

        private int procCount;
        private FixedThreadFor threadpool;
        private PixelThreadPool pixelPool;

        private Fast2D<bool> skyMask;

        private TemporalAA taa;
        private ToneMapper toneMapper;

        // Scratch buffers for spatial denoising
        private Fast2D<Vec3> spatialA;
        private Fast2D<Vec3> spatialB;

        private const float Pi = 3.14159265358979323846f;
        private const float InvPi = 1.0f / Pi;
        private const float DiffuseSigmaDeg = 25.0f;

        // Temporal history with per-pixel guides for stability
        private Fast2D<Vec3> taaHistory;
        private bool taaHistoryValid = false;
        private Fast2D<Vec3> prevNormal;
        private Fast2D<float> prevDepth;
        private Fast2D<bool> prevSky;

        public RaytraceRenderer(Framebuffer framebuffer, Scene scene, float fovDeg, int pxW, int pxH, int superSample)
        {
            procCount = Math.Max(1, Environment.ProcessorCount);
            threadpool = new FixedThreadFor(procCount, "Ray Trace Render Threads");
            pixelPool = new PixelThreadPool(procCount, "Ray Trace Pixels");
            this.scene = scene;
            this.fovDeg = fovDeg;
            ss = Math.Max(1, superSample);

            fbW = framebuffer.Width;
            fbH = framebuffer.Height;

            hiW = fbW * ss;
            hiH = fbH * 2 * ss;

            frameBuffer = new Fast2D<Chexel>(fbW, fbH);
            rays = new Fast2D<Ray>(hiW, hiH);
            skyMask = new Fast2D<bool>(hiW, hiH);
            gAlbedo = new Fast2D<Vec3>(hiW, hiH);
            gNormal = new Fast2D<Vec3>(hiW, hiH);
            gDepth = new Fast2D<float>(hiW, hiH);

            taa = new TemporalAA(hiW, hiH, taaAlpha, MotionTransReset, MotionRotReset);
            toneMapper = new ToneMapper();

            spatialA = new Fast2D<Vec3>(hiW, hiH);
            spatialB = new Fast2D<Vec3>(hiW, hiH);

            taaHistory = new Fast2D<Vec3>(hiW, hiH);
            prevNormal = new Fast2D<Vec3>(hiW, hiH);
            prevDepth = new Fast2D<float>(hiW, hiH);
            prevSky = new Fast2D<bool>(hiW, hiH);

            scene.RebuildBVH();
        }

        public void Resize(Framebuffer framebuffer, int superSample)
        {
            if (framebuffer == null) throw new ArgumentNullException(nameof(framebuffer));
            ss = Math.Max(1, superSample);

            fbW = framebuffer.Width;
            fbH = framebuffer.Height;

            hiW = fbW * ss;
            hiH = fbH * 2 * ss;

            frameBuffer = new Fast2D<Chexel>(fbW, fbH);
            rays = new Fast2D<Ray>(hiW, hiH);
            skyMask = new Fast2D<bool>(hiW, hiH);
            gAlbedo = new Fast2D<Vec3>(hiW, hiH);
            gNormal = new Fast2D<Vec3>(hiW, hiH);
            gDepth = new Fast2D<float>(hiW, hiH);

            taa.Resize(hiW, hiH);

            spatialA = new Fast2D<Vec3>(hiW, hiH);
            spatialB = new Fast2D<Vec3>(hiW, hiH);

            taaHistory = new Fast2D<Vec3>(hiW, hiH);
            prevNormal = new Fast2D<Vec3>(hiW, hiH);
            prevDepth = new Fast2D<float>(hiW, hiH);
            prevSky = new Fast2D<bool>(hiW, hiH);
            taaHistoryValid = false;
        }

        public void SetCamera(Vec3 pos, float yaw, float pitch)
        {
            lock (camLock)
            {
                camPos = pos;
                this.yaw = yaw;
                this.pitch = pitch;
            }
        }

        public void SetFov(float fovDeg)
        {
            this.fovDeg = fovDeg;
        }

        Fast2D<Vec3> currentHdr = null;

        public void TryFlipAndBlit(Framebuffer fb)
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

            bool resetHistory = taa.ShouldResetHistory(camPosSnapshot, yawSnapshot, pitchSnapshot) || scene.HasDynamicTextures;

            Camera cam = BuildCamera(camPosSnapshot, yawSnapshot, pitchSnapshot, fovDeg, aspect);

            long frame = Interlocked.Increment(ref frameCounter);

            int frameIdx = unchecked((int)(frame & 0x7fffffff));
            float jitterRotX = RaytraceSampler.Frac((frameIdx + 1) * 0.61803398875f);
            float jitterRotY = RaytraceSampler.Frac((frameIdx + 1) * 0.38196601125f);

            var target = frameBuffer;

            threadpool.For(0, procCount, worker =>
            {
                int yStart = worker * hiH / procCount;
                int yEnd = (worker + 1) * hiH / procCount;
                for (int py = yStart; py < yEnd; py++)
                {
                    for (int px = 0; px < hiW; px++)
                    {
                        rays[px, py] = MakeJitteredRay(camPosSnapshot, yawSnapshot, pitchSnapshot, fovDeg, aspect, px, py, hiW, hiH, jitterRotX, jitterRotY, frameIdx);
                    }
                }
            });

            if (currentHdr == null || currentHdr.Width != hiW || currentHdr.Height != hiH)
            {
                currentHdr = new Fast2D<Vec3>(hiW, hiH);
            }

            pixelPool.For2D(hiW, hiH, (px, py, threadId) =>
            {
                RaytraceSampler.Rng rng = new RaytraceSampler.Rng(RaytraceSampler.PerFrameSeed(px, py, frame, 0, 0, SeedSalt));
                float uCenter = (px + 0.5f) / hiW;
                float vCenter = (py + 0.5f) / hiH;

                bool isSky;
                PrimaryGBuffer gbuf;
                Vec3 cur = TraceFull(scene, rays[px, py], 0, 0, ref rng, uCenter, vCenter, out isSky, out gbuf);
                skyMask[px, py] = isSky;

                currentHdr[px, py] = cur;
                gAlbedo[px, py] = gbuf.Albedo;
                gNormal[px, py] = gbuf.Normal;
                gDepth[px, py] = gbuf.Depth;
            });

            Fast2D<Vec3> blendedHdr = TemporalBlendWithClamp(currentHdr, gNormal, gDepth, skyMask, resetHistory, clampRadius: 1, luminancePad: 0.10f);

            // Edge-aware spatial denoise on temporally blended color
            Fast2D<Vec3> denoisedHdr = ApplyAtrousDenoise(
                blendedHdr, gAlbedo, gNormal, gDepth, skyMask,
                spatialA, spatialB,
                iterations: 3, cPhi: 3.0f, nPhi: 0.35f, zPhi: 2.0f, aPhi: 0.20f);

            int step = Math.Max(2, ss * 2);
            toneMapper.UpdateExposure(denoisedHdr, skyMask, step);

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
                                topSum = topSum + denoisedHdr[x, yTop];
                                botSum = botSum + denoisedHdr[x, yBot];
                            }
                        }
                        float inv = 1.0f / (ss * ss);
                        Vec3 topAvg = new Vec3(topSum.X * inv, topSum.Y * inv, topSum.Z * inv);
                        Vec3 botAvg = new Vec3(botSum.X * inv, botSum.Y * inv, botSum.Z * inv);

                        Vec3 topSDR = toneMapper.MapPixel(topAvg);
                        Vec3 botSDR = toneMapper.MapPixel(botAvg);

                        target[cx, cy] = new Chexel('▀', topSDR, botSDR);
                        fb.SetChexel(cx, cy, target[cx, cy]);
                    }
                }
            });

            taa.CommitCamera(camPosSnapshot, yawSnapshot, pitchSnapshot);
        }

        private static float Luma(Vec3 c)
        {
            return 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
        }

        private Fast2D<Vec3> TemporalBlendWithClamp(
            Fast2D<Vec3> current,
            Fast2D<Vec3> normal,
            Fast2D<float> depth,
            Fast2D<bool> sky,
            bool forceReset,
            int clampRadius = 1,
            float luminancePad = 0.10f)
        {
            int w = current.Width;
            int h = current.Height;
            if (!taaHistoryValid || forceReset || taaHistory == null || taaHistory.Width != w || taaHistory.Height != h)
            {
                taaHistory = new Fast2D<Vec3>(w, h);
                prevNormal = new Fast2D<Vec3>(w, h);
                prevDepth = new Fast2D<float>(w, h);
                prevSky = new Fast2D<bool>(w, h);
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        taaHistory[x, y] = current[x, y];
                        prevNormal[x, y] = normal[x, y];
                        prevDepth[x, y] = depth[x, y];
                        prevSky[x, y] = sky[x, y];
                    }
                }
                taaHistoryValid = true;
                return taaHistory;
            }

            float alpha = MathF.Max(0.0f, MathF.Min(1.0f, taaAlpha));
            int r = Math.Max(0, clampRadius);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Vec3 cur = current[x, y];
                    Vec3 prev = taaHistory[x, y];

                    // Disocclusion / reactive mask using guides
                    bool skyNow = sky[x, y];
                    bool skyPrev = prevSky[x, y];
                    float localAlpha = alpha;
                    if (skyNow != skyPrev)
                    {
                        localAlpha = 1.0f;
                    }
                    else
                    {
                        float zNow = depth[x, y];
                        float zPrev = prevDepth[x, y];
                        Vec3 nNow = normal[x, y].Normalized();
                        Vec3 nPrev = prevNormal[x, y].Normalized();

                        if (!float.IsFinite(zNow) || !float.IsFinite(zPrev))
                        {
                            localAlpha = 1.0f;
                        }
                        else
                        {
                            float dz = MathF.Abs(zNow - zPrev);
                            float rel = dz / MathF.Max(1e-4f, MathF.Min(zNow, zPrev));
                            float ndot = nNow.Dot(nPrev);
                            if (rel > 0.05f || ndot < 0.8f)
                            {
                                localAlpha = 1.0f;
                            }
                        }
                    }

                    // Neighborhood luminance clamp to reduce flashing
                    float minL = float.PositiveInfinity;
                    float maxL = float.NegativeInfinity;
                    for (int oy = -r; oy <= r; oy++)
                    {
                        int sy = y + oy; if (sy < 0) sy = 0; else if (sy >= h) sy = h - 1;
                        for (int ox = -r; ox <= r; ox++)
                        {
                            int sx = x + ox; if (sx < 0) sx = 0; else if (sx >= w) sx = w - 1;
                            if (sky[sx, sy] != sky[x, y]) continue;
                            float l = Luma(current[sx, sy]);
                            if (l < minL) minL = l;
                            if (l > maxL) maxL = l;
                        }
                    }
                    float pad = luminancePad;
                    float range = maxL - minL;
                    float lMin = minL - range * pad;
                    float lMax = maxL + range * pad;
                    float prevL = Luma(prev);
                    if (prevL > lMax)
                    {
                        float s = lMax / MathF.Max(1e-6f, prevL);
                        prev = new Vec3(prev.X * s, prev.Y * s, prev.Z * s);
                    }
                    else if (prevL < lMin)
                    {
                        float s = lMin / MathF.Max(1e-6f, prevL);
                        prev = new Vec3(prev.X * s, prev.Y * s, prev.Z * s);
                    }

                    Vec3 outC = new Vec3(
                        prev.X * (1.0f - localAlpha) + cur.X * localAlpha,
                        prev.Y * (1.0f - localAlpha) + cur.Y * localAlpha,
                        prev.Z * (1.0f - localAlpha) + cur.Z * localAlpha);

                    taaHistory[x, y] = outC;
                }
            }

            // Update guides for next frame
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    prevNormal[x, y] = normal[x, y];
                    prevDepth[x, y] = depth[x, y];
                    prevSky[x, y] = sky[x, y];
                }
            }

            return taaHistory;
        }

        private struct PrimaryGBuffer
        {
            public Vec3 Albedo;
            public Vec3 Normal;
            public float Depth;
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

        private static Ray MakeJitteredRay(Vec3 camPos, float yaw, float pitch, float fovDeg, float aspect, int px, int py, int W, int H, float jitterRotX, float jitterRotY, int frameIdx)
        {
            float jxBase = RaytraceSampler.BlueNoiseSample(px, py, frameIdx, 0);
            float jyBase = RaytraceSampler.BlueNoiseSample(px, py, frameIdx, 1);
            float jx = RaytraceSampler.Frac(jxBase + jitterRotX) - 0.5f;
            float jy = RaytraceSampler.Frac(jyBase + jitterRotY) - 0.5f;

            float u = ((px + 0.5f + jx) / W) * 2.0f - 1.0f;
            float v = 1.0f - ((py + 0.5f + jy) / H) * 2.0f;
            float fovRad = fovDeg * (MathF.PI / 180.0f);
            float halfH = MathF.Tan(0.5f * fovRad);
            float halfW = halfH * aspect;
            Vec3 fwd = ForwardFromYawPitch(yaw, pitch).Normalized();
            Vec3 worldUp = new Vec3(0.0, 1.0, 0.0);
            Vec3 right = fwd.Cross(worldUp).Normalized();
            Vec3 up = right.Cross(fwd).Normalized();
            Vec3 dir = (fwd + right * (u * halfW) + up * (v * halfH)).Normalized();
            return new Ray(camPos, dir);
        }

        private struct PathWorkItem
        {
            public Ray Ray;
            public Vec3 Throughput;
            public int MirrorDepth;
            public int DiffuseDepth;
            public bool IsPrimary;
        }

        private static Vec3 TraceFull(Scene scene, Ray r, int mirrorDepthIgnored, int diffuseDepthIgnored, ref RaytraceSampler.Rng rng, float screenU, float screenV, out bool isSky, out PrimaryGBuffer primary)
        {
            const int MaxStack = 16;
            PathWorkItem[] stack = new PathWorkItem[MaxStack];
            int sp = 0;
            stack[sp++] = new PathWorkItem { Ray = r, Throughput = new Vec3(1, 1, 1), MirrorDepth = 0, DiffuseDepth = 0, IsPrimary = true };
            Vec3 radiance = Vec3.Zero;
            bool primaryHitSomething = false;
            isSky = false;
            bool gbufValid = false;
            primary = new PrimaryGBuffer { Albedo = Vec3.Zero, Normal = Vec3.Zero, Depth = float.MaxValue };
            HitRecord rec = default;
            float sigmaRad = DiffuseSigmaDeg * (MathF.PI / 180.0f);
            while (sp > 0)
            {
                sp--;
                PathWorkItem item = stack[sp];
                Ray currentRay = item.Ray;
                Vec3 beta = item.Throughput;
                int mirrorDepth = item.MirrorDepth;
                int diffuseDepth = item.DiffuseDepth;
                for (; ; )
                {
                    rec = default;
                    if (!scene.Hit(currentRay, 0.001f, float.MaxValue, ref rec, screenU, screenV))
                    {
                        float tbg = 0.5f * (currentRay.Dir.Y + 1.0f);
                        Vec3 sky = Lerp(scene.BackgroundBottom, scene.BackgroundTop, tbg);
                        if (item.IsPrimary && !primaryHitSomething)
                        {
                            isSky = true;
                            if (!gbufValid)
                            {
                                primary = new PrimaryGBuffer { Albedo = Vec3.Zero, Normal = Vec3.Zero, Depth = float.MaxValue };
                                gbufValid = true;
                            }
                        }
                        radiance += new Vec3(beta.X * sky.X, beta.Y * sky.Y, beta.Z * sky.Z);
                        break;
                    }
                    if (item.IsPrimary)
                    {
                        primaryHitSomething = true;
                        isSky = false;
                        if (!gbufValid)
                        {
                            Vec3 baseAlb = SampleAlbedo(rec.Mat, rec.P, rec.N, rec.U, rec.V);
                            primary = new PrimaryGBuffer { Albedo = baseAlb, Normal = rec.N, Depth = rec.T };
                            gbufValid = true;
                        }
                        item.IsPrimary = false;
                    }
                    if (rec.Mat.Emission.X != 0.0 || rec.Mat.Emission.Y != 0.0 || rec.Mat.Emission.Z != 0.0)
                    {
                        Vec3 e = rec.Mat.Emission;
                        radiance += new Vec3(beta.X * e.X, beta.Y * e.Y, beta.Z * e.Z);
                    }
                    Vec3 baseAlbedo = SampleAlbedo(rec.Mat, rec.P, rec.N, rec.U, rec.V);
                    if (rec.Mat.Transparency > 0.0)
                    {
                        if (mirrorDepth >= MaxMirrorBounces)
                        {
                            break;
                        }
                        Vec3 n = rec.N;
                        Vec3 wo = currentRay.Dir;
                        bool frontFace = n.Dot(wo) < 0.0;
                        Vec3 nl = frontFace ? n : n * -1.0f;
                        float etaI = frontFace ? 1.0f : (float)rec.Mat.IndexOfRefraction;
                        float etaT = frontFace ? (float)rec.Mat.IndexOfRefraction : 1.0f;
                        float eta = etaI / etaT;
                        Vec3 reflDir = Reflect(wo, nl).Normalized();
                        bool hasRefract = Refract(wo, nl, eta, out Vec3 refrDir);
                        float cosTheta = MathF.Abs(nl.Dot(wo * -1.0f));
                        float fresnel = FresnelSchlick(cosTheta, etaI, etaT);
                        float R = fresnel;
                        float Tr = (float)Math.Clamp(rec.Mat.Transparency, 0.0, 1.0);
                        float T = hasRefract ? (1.0f - R) * Tr : 0.0f;
                        R = Math.Clamp(R + (float)rec.Mat.Reflectivity * (1.0f - R), 0.0f, 1.0f);
                        int pushed = 0;
                        if (R > 0.0f)
                        {
                            if (sp < MaxStack)
                            {
                                PathWorkItem refl = new PathWorkItem();
                                refl.Ray = new Ray(rec.P + nl * Eps, reflDir);
                                refl.Throughput = new Vec3(beta.X * baseAlbedo.X * R, beta.Y * baseAlbedo.Y * R, beta.Z * baseAlbedo.Z * R);
                                refl.MirrorDepth = mirrorDepth + 1;
                                refl.DiffuseDepth = diffuseDepth;
                                refl.IsPrimary = false;
                                stack[sp++] = refl;
                                pushed++;
                            }
                        }
                        if (T > 0.0f)
                        {
                            if (sp < MaxStack)
                            {
                                PathWorkItem refr = new PathWorkItem();
                                refr.Ray = new Ray(rec.P - nl * Eps, refrDir.Normalized());
                                Vec3 transTint = rec.Mat.TransmissionColor;
                                refr.Throughput = new Vec3(beta.X * transTint.X * T, beta.Y * transTint.Y * T, beta.Z * transTint.Z * T);
                                refr.MirrorDepth = mirrorDepth + 1;
                                refr.DiffuseDepth = diffuseDepth;
                                refr.IsPrimary = false;
                                stack[sp++] = refr;
                                pushed++;
                            }
                        }
                        break;
                    }
                    if ((float)rec.Mat.Reflectivity >= MirrorThreshold)
                    {
                        if (mirrorDepth >= MaxMirrorBounces)
                        {
                            break;
                        }
                        Vec3 reflDir = Reflect(currentRay.Dir, rec.N).Normalized();
                        currentRay = new Ray(rec.P + rec.N * Eps, reflDir);
                        beta = new Vec3(beta.X * baseAlbedo.X, beta.Y * baseAlbedo.Y, beta.Z * baseAlbedo.Z);
                        mirrorDepth++;
                        continue;
                    }
                    if (scene.Ambient.Intensity > 0.0f)
                    {
                        Vec3 a = new Vec3(scene.Ambient.Color.X * scene.Ambient.Intensity, scene.Ambient.Color.Y * scene.Ambient.Intensity, scene.Ambient.Color.Z * scene.Ambient.Intensity);
                        Vec3 amb = new Vec3(a.X * baseAlbedo.X, a.Y * baseAlbedo.Y, a.Z * baseAlbedo.Z);
                        radiance += new Vec3(beta.X * amb.X, beta.Y * amb.Y, beta.Z * amb.Z);
                    }
                    Vec3 woView = (currentRay.Dir * -1.0f).Normalized();
                    for (int i = 0; i < scene.Lights.Count; i++)
                    {
                        PointLight light = scene.Lights[i];
                        Vec3 toL = light.Position - rec.P;
                        float dist2 = toL.Dot(toL);
                        float dist = MathF.Sqrt(dist2);
                        Vec3 ldir = toL / dist;
                        float nDotL = MathF.Max(0.0f, rec.N.Dot(ldir));
                        if (nDotL <= 0.0f)
                        {
                            continue;
                        }
                        Ray shadow = new Ray(rec.P + rec.N * Eps, ldir);
                        Vec3 transToLight = ComputeTransmittanceToLight(scene, shadow, dist - Eps, screenU, screenV);
                        if (transToLight.X <= 1e-6f && transToLight.Y <= 1e-6f && transToLight.Z <= 1e-6f)
                        {
                            continue;
                        }
                        float atten = light.Intensity / dist2;
                        Vec3 fDiffuse = OrenNayarBRDF(baseAlbedo, rec.N, woView, ldir, sigmaRad);
                        Vec3 bsdf = fDiffuse;
                        Vec3 Li = light.Color * atten;
                        Vec3 contrib = (bsdf * (nDotL)) * Li;
                        contrib = new Vec3(contrib.X * transToLight.X, contrib.Y * transToLight.Y, contrib.Z * transToLight.Z);
                        radiance += new Vec3(beta.X * contrib.X, beta.Y * contrib.Y, beta.Z * contrib.Z);
                    }
                    if (diffuseDepth < DiffuseBounces)
                    {
                        Vec3 bounceDir = RaytraceSampler.CosineSampleHemisphere(rec.N, ref rng);
                        float cosNI = MathF.Max(0.0f, rec.N.Dot(bounceDir));
                        Vec3 fON = OrenNayarBRDF(baseAlbedo, rec.N, woView, bounceDir, sigmaRad);
                        float factor = Pi;
                        Vec3 mult = new Vec3(fON.X * factor, fON.Y * factor, fON.Z * factor);
                        currentRay = new Ray(rec.P + rec.N * Eps, bounceDir);
                        beta = new Vec3(beta.X * mult.X, beta.Y * mult.Y, beta.Z * mult.Z);
                        diffuseDepth++;
                        continue;
                    }
                    break;
                }
            }
            return radiance;
        }

        private static Fast2D<Vec3> ApplyAtrousDenoise(
            Fast2D<Vec3> src,
            Fast2D<Vec3> albedo,
            Fast2D<Vec3> normal,
            Fast2D<float> depth,
            Fast2D<bool> sky,
            Fast2D<Vec3> scratchA,
            Fast2D<Vec3> scratchB,
            int iterations = 3,
            float cPhi = 3.0f,
            float nPhi = 0.35f,
            float zPhi = 2.0f,
            float aPhi = 0.20f)
        {
            int w = src.Width;
            int h = src.Height;
            if (albedo.Width != w || albedo.Height != h) return src;
            if (normal.Width != w || normal.Height != h) return src;
            if (depth.Width != w || depth.Height != h) return src;
            if (sky != null && (sky.Width != w || sky.Height != h)) return src;
            if (scratchA == null || scratchA.Width != w || scratchA.Height != h) scratchA = new Fast2D<Vec3>(w, h);
            if (scratchB == null || scratchB.Width != w || scratchB.Height != h) scratchB = new Fast2D<Vec3>(w, h);

            // 5-tap B3-spline kernel
            float[] k = new float[5] { 1f / 16f, 1f / 4f, 3f / 8f, 1f / 4f, 1f / 16f };

            Fast2D<Vec3> cur = src;
            Fast2D<Vec3> dst = scratchA;

            for (int it = 0; it < Math.Max(1, iterations); it++)
            {
                int step = 1 << it;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (sky != null && sky[x, y]) { dst[x, y] = cur[x, y]; continue; }

                        Vec3 c0 = cur[x, y];
                        Vec3 a0 = albedo[x, y];
                        Vec3 n0 = normal[x, y].Normalized();
                        float z0 = depth[x, y];

                        float wsum = 0.0f;
                        Vec3 accum = Vec3.Zero;

                        for (int ky = -2; ky <= 2; ky++)
                        {
                            int sy = y + ky * step;
                            if (sy < 0) sy = 0; else if (sy >= h) sy = h - 1;
                            float wy = k[ky + 2];
                            for (int kx = -2; kx <= 2; kx++)
                            {
                                int sx = x + kx * step;
                                if (sx < 0) sx = 0; else if (sx >= w) sx = w - 1;
                                if (sky != null && sky[sx, sy] != (sky[x, y])) continue;
                                float wx = k[kx + 2];
                                float wBase = wx * wy;

                                Vec3 c = cur[sx, sy];
                                Vec3 a = albedo[sx, sy];
                                Vec3 n = normal[sx, sy].Normalized();
                                float z = depth[sx, sy];

                                float lum0 = 0.2126f * c0.X + 0.7152f * c0.Y + 0.0722f * c0.Z;
                                float lum = 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
                                float dl = MathF.Abs(lum - lum0);
                                float dn = MathF.Max(0.0f, 1.0f - n0.Dot(n));
                                float dz = MathF.Abs(z - z0);
                                float da = MathF.Abs(a.X - a0.X) + MathF.Abs(a.Y - a0.Y) + MathF.Abs(a.Z - a0.Z);

                                float wc = MathF.Exp(-dl / MathF.Max(1e-6f, cPhi));
                                float wn = MathF.Exp(-dn / MathF.Max(1e-6f, nPhi));
                                float wz = MathF.Exp(-dz / MathF.Max(1e-6f, zPhi));
                                float wa = MathF.Exp(-(da) / MathF.Max(1e-6f, aPhi));

                                float wght = wBase * wc * wn * wz * wa;
                                accum = new Vec3(accum.X + c.X * wght, accum.Y + c.Y * wght, accum.Z + c.Z * wght);
                                wsum += wght;
                            }
                        }

                        if (wsum > 1e-8f)
                        {
                            float inv = 1.0f / wsum;
                            dst[x, y] = new Vec3(accum.X * inv, accum.Y * inv, accum.Z * inv);
                        }
                        else
                        {
                            dst[x, y] = c0;
                        }
                    }
                }

                // swap
                var tmp = cur; cur = dst; dst = (tmp == scratchA) ? scratchB : scratchA;
            }

            return cur;
        }

        private static Vec3 SampleAlbedo(Material mat, Vec3 pos, Vec3 normal, float u, float v)
        {
            if (mat.DiffuseTexture == null || mat.TextureWeight <= 0.0)
            {
                return mat.Albedo;
            }
            float tiles = (float)Math.Max(1e-6, mat.UVScale);
            Vec3 tex = mat.DiffuseTexture.SampleBilinear(u * tiles, v * tiles);
            float t = (float)Math.Clamp(mat.TextureWeight, 0.0, 1.0);
            Vec3 outAlbedo = mat.Albedo * (1.0f - t) + tex * t;
            return outAlbedo.Saturate();
        }

        private static bool Refract(Vec3 v, Vec3 n, float eta, out Vec3 refrDir)
        {
            float cosi = -MathF.Max(-1.0f, MathF.Min(1.0f, (float)v.Dot(n)));
            float k = 1.0f - eta * eta * (1.0f - cosi * cosi);
            if (k < 0.0f)
            {
                refrDir = default;
                return false;
            }
            refrDir = (v * eta) + (n * (eta * cosi - MathF.Sqrt(k)));
            return true;
        }

        private static float FresnelSchlick(float cosTheta, float etaI, float etaT)
        {
            float r0 = (etaI - etaT) / (etaI + etaT);
            r0 = r0 * r0;
            return r0 + (1.0f - r0) * MathF.Pow(1.0f - cosTheta, 5.0f);
        }

        private static Vec3 ComputeTransmittanceToLight(Scene scene, Ray shadow, float maxDist, float screenU, float screenV)
        {
            // In large voxel worlds we only need binary occlusion for direct lighting.
            // This avoids edge cases where transmissive handling might mask occluders.
            if (scene is ConsoleGame.RayTracing.Scenes.VolumeScene)
            {
                bool blocked = scene.Occluded(shadow, maxDist, screenU, screenV);
                return blocked ? Vec3.Zero : new Vec3(1.0f, 1.0f, 1.0f);
            }
            float transR = 1.0f;
            float transG = 1.0f;
            float transB = 1.0f;
            HitRecord block = default;
            float tmin = 0.0f + Eps;
            int counter = 0;
            const float cutoff = 1e-6f;
            while (counter < MaxRefractions && scene.Hit(shadow, tmin, maxDist, ref block, screenU, screenV))
            {
                counter++;
                double tr = block.Mat.Transparency;
                if (tr <= 0.0)
                {
                    return Vec3.Zero;
                }
                Vec3 tint = block.Mat.TransmissionColor;
                float trf = (float)tr;
                transR *= tint.X * trf;
                transG *= tint.Y * trf;
                transB *= tint.Z * trf;
                if (transR <= cutoff && transG <= cutoff && transB <= cutoff)
                {
                    return Vec3.Zero;
                }
                float tHit = block.T;
                if (tHit > maxDist)
                {
                    break;
                }
                tmin = tHit + Eps;
            }
            return new Vec3(transR, transG, transB);
        }

        private static Vec3 Reflect(Vec3 v, Vec3 n)
        {
            return v - n * (2.0f * v.Dot(n));
        }

        private static Vec3 Lerp(Vec3 a, Vec3 b, float t)
        {
            return a * (1.0f - t) + b * t;
        }

        private static Vec3 OrenNayarBRDF(Vec3 albedo, Vec3 n, Vec3 wo, Vec3 wi, float sigmaRad)
        {
            float cosThetaI = MathF.Max(0.0f, (float)n.Dot(wi));
            float cosThetaO = MathF.Max(0.0f, (float)n.Dot(wo));
            if (cosThetaI <= 0.0f || cosThetaO <= 0.0f)
            {
                return Vec3.Zero;
            }
            float sinThetaI = MathF.Sqrt(MathF.Max(0.0f, 1.0f - cosThetaI * cosThetaI));
            float sinThetaO = MathF.Sqrt(MathF.Max(0.0f, 1.0f - cosThetaO * cosThetaO));
            Vec3 projI = (wi - n * cosThetaI).Normalized();
            Vec3 projO = (wo - n * cosThetaO).Normalized();
            float cosPhiDiff = MathF.Max(0.0f, (float)projI.Dot(projO));
            float sigma2 = sigmaRad * sigmaRad;
            float A = 1.0f - (sigma2 / (2.0f * (sigma2 + 0.33f)));
            float B = 0.45f * sigma2 / (sigma2 + 0.09f);
            float sinAlpha = MathF.Max(sinThetaI, sinThetaO);
            float tanBeta = MathF.Min(sinThetaI / MathF.Max(1e-6f, cosThetaI), sinThetaO / MathF.Max(1e-6f, cosThetaO));
            float on = (A + B * cosPhiDiff * sinAlpha * tanBeta);
            Vec3 f = albedo * (on * InvPi);
            return f.Saturate();
        }
    }
}
