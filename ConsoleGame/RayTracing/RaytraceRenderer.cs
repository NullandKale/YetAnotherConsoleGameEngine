using ConsoleGame.RayTracing.Scenes;
using ConsoleGame.Renderer;
using ConsoleGame.Threads;
using ConsoleRayTracing;
using System;
using System.Runtime.CompilerServices;
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

        private Chexel[,] frameBuffer;

        private long frameCounter = 0;

        private readonly object camLock = new object();
        private Vec3 camPos = new Vec3(0.0, 1.0, 0.0);
        private float yaw = 0.0f;
        private float pitch = 0.0f;

        private const int DiffuseBounces = 1;
        private const int IndirectSamples = 1;
        private const int MaxMirrorBounces = 2;
        private const int MaxRefractions = 1;
        private const float MirrorThreshold = 0.9f;
        private const float Eps = 1e-4f;

        private const float MaxLuminance = 1.0f;
        private const ulong SeedSalt = 0x9E3779B97F4A7C15UL;

        private float taaAlpha = 0.05f;
        private const float MotionTransReset = 0.0025f;
        private const float MotionRotReset = 0.0025f;

        private Ray[,] rays;

        private int procCount;
        private FixedThreadFor threadpool;
        private PixelThreadPool pixelPool;

        private bool[,] skyMask;

        private TemporalAA taa;
        private ToneMapper toneMapper;

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

            frameBuffer = new Chexel[fbW, fbH];
            rays = new Ray[hiW, hiH];
            skyMask = new bool[hiW, hiH];

            taa = new TemporalAA(hiW, hiH, taaAlpha, MotionTransReset, MotionRotReset);
            toneMapper = new ToneMapper();

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

            frameBuffer = new Chexel[fbW, fbH];
            rays = new Ray[hiW, hiH];
            skyMask = new bool[hiW, hiH];

            taa.Resize(hiW, hiH);
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

        Vec3[,] currentHdr = null;

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

            bool resetHistory = taa.ShouldResetHistory(camPosSnapshot, yawSnapshot, pitchSnapshot);

            Camera cam = BuildCamera(camPosSnapshot, yawSnapshot, pitchSnapshot, fovDeg, aspect);

            long frame = Interlocked.Increment(ref frameCounter);

            int frameIdx = unchecked((int)(frame & 0x7fffffff));
            float jitterX = Halton(frameIdx + 1, 2) - 0.5f;
            float jitterY = Halton(frameIdx + 1, 3) - 0.5f;

            Chexel[,] target = frameBuffer;

            threadpool.For(0, procCount, worker =>
            {
                int yStart = worker * hiH / procCount;
                int yEnd = (worker + 1) * hiH / procCount;
                for (int py = yStart; py < yEnd; py++)
                {
                    for (int px = 0; px < hiW; px++)
                    {
                        rays[px, py] = MakeJitteredRay(camPosSnapshot, yawSnapshot, pitchSnapshot, fovDeg, aspect, px, py, hiW, hiH, jitterX, jitterY);
                    }
                }
            });

            if (currentHdr == null || currentHdr.Length != hiH * hiW)
            {
                currentHdr = new Vec3[hiW, hiH];
            }

            pixelPool.For2D(hiW, hiH, (px, py, threadId) =>
            {
                Rng rng = new Rng(PerFrameSeed(px, py, frame, 0, 0));
                float uCenter = (px + 0.5f) / hiW;
                float vCenter = (py + 0.5f) / hiH;

                HitRecord temp = default;
                bool isSky = !scene.Hit(rays[px, py], 0.001f, float.MaxValue, ref temp, uCenter, vCenter);
                skyMask[px, py] = isSky;

                Vec3 cur;
                if (isSky)
                {
                    float tbg = 0.5f * (rays[px, py].Dir.Y + 1.0f);
                    cur = Lerp(scene.BackgroundBottom, scene.BackgroundTop, tbg);
                }
                else
                {
                    cur = TraceFull(scene, rays[px, py], 0, 0, ref rng, uCenter, vCenter);
                }

                currentHdr[px, py] = cur;
            });

            Vec3[,] blendedHdr = taa.BlendIntoHistory(currentHdr, resetHistory);

            int step = Math.Max(2, ss * 2);
            toneMapper.UpdateExposure(blendedHdr, skyMask, step);

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
                                topSum = topSum + blendedHdr[x, yTop];
                                botSum = botSum + blendedHdr[x, yBot];
                            }
                        }
                        float inv = 1.0f / (ss * ss);
                        Vec3 topAvg = new Vec3(topSum.X * inv, topSum.Y * inv, topSum.Z * inv);
                        Vec3 botAvg = new Vec3(botSum.X * inv, botSum.Y * inv, botSum.Z * inv);

                        Vec3 topSDR = toneMapper.MapPixel(topAvg);
                        Vec3 botSDR = toneMapper.MapPixel(botAvg);

                        target[cx, cy] = new Chexel('▀', topSDR, botSDR);
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

            taa.CommitCamera(camPosSnapshot, yawSnapshot, pitchSnapshot);
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

        private static Ray MakeJitteredRay(Vec3 camPos, float yaw, float pitch, float fovDeg, float aspect, int px, int py, int W, int H, float jitterX, float jitterY)
        {
            float u = ((px + 0.5f + jitterX) / W) * 2.0f - 1.0f;
            float v = 1.0f - ((py + 0.5f + jitterY) / H) * 2.0f;
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

        private static float Halton(int index, int b)
        {
            float f = 1.0f;
            float r = 0.0f;
            while (index > 0)
            {
                f /= b;
                r += f * (index % b);
                index /= b;
            }
            return r;
        }

        private static Vec3 TraceFull(Scene scene, Ray r, int mirrorDepth, int diffuseDepth, ref Rng rng, float screenU, float screenV)
        {
            HitRecord rec = default;
            if (!scene.Hit(r, 0.001f, float.MaxValue, ref rec, screenU, screenV))
            {
                float tbg = 0.5f * (r.Dir.Y + 1.0f);
                return Lerp(scene.BackgroundBottom, scene.BackgroundTop, tbg);
            }
            Vec3 color = rec.Mat.Emission;

            Vec3 baseAlbedo = SampleAlbedo(rec.Mat, rec.P, rec.N, rec.U, rec.V);

            if (rec.Mat.Transparency > 0.0)
            {
                if (mirrorDepth >= MaxMirrorBounces) return color;
                Vec3 n = rec.N;
                Vec3 wo = r.Dir;
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

                Vec3 accum = Vec3.Zero;

                if (R > 0.0f)
                {
                    Ray reflRay = new Ray(rec.P + nl * Eps, reflDir);
                    Vec3 rc = TraceFull(scene, reflRay, mirrorDepth + 1, diffuseDepth, ref rng, screenU, screenV);
                    accum += new Vec3(rc.X * baseAlbedo.X, rc.Y * baseAlbedo.Y, rc.Z * baseAlbedo.Z) * R;
                }

                if (T > 0.0f)
                {
                    Ray refrRay = new Ray(rec.P - nl * Eps, refrDir.Normalized());
                    Vec3 tc = TraceFull(scene, refrRay, mirrorDepth + 1, diffuseDepth, ref rng, screenU, screenV);
                    Vec3 transTint = rec.Mat.TransmissionColor;
                    accum += tc * transTint * T;
                }

                color += accum;
                return color;
            }

            if ((float)rec.Mat.Reflectivity >= MirrorThreshold)
            {
                if (mirrorDepth >= MaxMirrorBounces) return color;
                Vec3 reflDir = Reflect(r.Dir, rec.N).Normalized();
                Ray reflRay = new Ray(rec.P + rec.N * Eps, reflDir);
                Vec3 reflCol = TraceFull(scene, reflRay, mirrorDepth + 1, diffuseDepth, ref rng, screenU, screenV);
                color += new Vec3(reflCol.X * baseAlbedo.X, reflCol.Y * baseAlbedo.Y, reflCol.Z * baseAlbedo.Z);
                return color;
            }

            if (scene.Ambient.Intensity > 0.0f)
            {
                Vec3 a = new Vec3(scene.Ambient.Color.X * scene.Ambient.Intensity, scene.Ambient.Color.Y * scene.Ambient.Intensity, scene.Ambient.Color.Z * scene.Ambient.Intensity);
                color += new Vec3(a.X * baseAlbedo.X, a.Y * baseAlbedo.Y, a.Z * baseAlbedo.Z);
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

                Vec3 transToLight = ComputeTransmittanceToLight(scene, shadow, dist - Eps, screenU, screenV);
                if (transToLight.X <= 1e-6 && transToLight.Y <= 1e-6 && transToLight.Z <= 1e-6) continue;

                float atten = light.Intensity / dist2;
                Vec3 lambert = baseAlbedo * (nDotL * atten) * light.Color;
                color += lambert * transToLight;
            }

            if (diffuseDepth < DiffuseBounces)
            {
                Vec3 indirect = Vec3.Zero;
                for (int s = 0; s < IndirectSamples; s++)
                {
                    Vec3 bounceDir = CosineSampleHemisphere(rec.N, ref rng);
                    Ray bounce = new Ray(rec.P + rec.N * Eps, bounceDir);
                    Vec3 Li = TraceFull(scene, bounce, mirrorDepth, diffuseDepth + 1, ref rng, screenU, screenV);
                    indirect += new Vec3(Li.X * baseAlbedo.X, Li.Y * baseAlbedo.Y, Li.Z * baseAlbedo.Z);
                }
                float invSpp = 1.0f / IndirectSamples;
                color += new Vec3(indirect.X * invSpp, indirect.Y * invSpp, indirect.Z * invSpp);
            }

            return color;
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
            Vec3 trans = new Vec3(1.0, 1.0, 1.0);
            float tTraveled = 0.0f;
            HitRecord block = default;
            float tmin = 0.0f + Eps;
            int counter = 0;
            while (counter < MaxRefractions && scene.Hit(shadow, tmin, maxDist, ref block, screenU, screenV))
            {
                counter++;
                Vec3 hitVec = block.P - shadow.Origin;
                float tHit = MathF.Sqrt((float)hitVec.Dot(hitVec));
                if (tHit > maxDist) break;

                double tr = block.Mat.Transparency;
                if (tr <= 0.0)
                {
                    return new Vec3(0.0, 0.0, 0.0);
                }

                Vec3 tint = block.Mat.TransmissionColor;
                trans = new Vec3(trans.X * tint.X * (float)tr, trans.Y * tint.Y * (float)tr, trans.Z * tint.Z * (float)tr);

                if (trans.X <= 1e-6 && trans.Y <= 1e-6 && trans.Z <= 1e-6) return new Vec3(0.0, 0.0, 0.0);

                tTraveled = tHit + Eps;
                tmin = tTraveled;
                shadow = new Ray(shadow.Origin, shadow.Dir);
            }
            return trans;
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

        private static Vec3 ClampLuminance(Vec3 c, float maxY)
        {
            float y = 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
            if (y <= maxY) return c;
            float s = maxY / MathF.Max(y, 1e-6f);
            return new Vec3(c.X * s, c.Y * s, c.Z * s);
        }
    }
}
