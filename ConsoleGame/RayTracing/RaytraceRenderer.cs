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

        private const int DiffuseBounces = 2;
        private const int IndirectSamples = 2;
        private const int MaxMirrorBounces = 2;
        private const float MirrorThreshold = 0.9f;
        private const float Eps = 1e-4f;

        private const float MaxLuminance = 1.0f;
        private const ulong SeedSalt = 0x9E3779B97F4A7C15UL;

        private Vec3[,] history;
        private bool historyValid = false;
        private float taaAlpha = 0.05f;
        private const float MotionTransReset = 0.0025f;
        private const float MotionRotReset = 0.0025f;

        private float lastCamX = float.NaN;
        private float lastCamY = float.NaN;
        private float lastCamZ = float.NaN;
        private float lastYaw = float.NaN;
        private float lastPitch = float.NaN;

        private Ray[,] rays; // hi-res ray buffer generated in its own phase

        private FixedThreadFor threadpool;

        public RaytraceRenderer(Framebuffer framebuffer, Scene scene, float fovDeg, int pxW, int pxH, int superSample)
        {
            threadpool = new FixedThreadFor(Environment.ProcessorCount, "Render Threads");
            this.scene = scene;
            this.fovDeg = fovDeg;
            ss = Math.Max(1, superSample);

            fbW = framebuffer.Width;
            fbH = framebuffer.Height;

            hiW = fbW * ss;
            hiH = fbH * 2 * ss;

            frameBuffer = new Chexel[fbW, fbH];
            history = new Vec3[hiW, hiH];
            rays = new Ray[hiW, hiH];

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
            history = new Vec3[hiW, hiH];
            rays = new Ray[hiW, hiH];

            historyValid = false;
            lastCamX = float.NaN;
            lastCamY = float.NaN;
            lastCamZ = float.NaN;
            lastYaw = float.NaN;
            lastPitch = float.NaN;
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

            bool resetHistory = ShouldResetHistory(camPosSnapshot, yawSnapshot, pitchSnapshot);
            float blendAlpha = resetHistory || !historyValid ? 1.0f : taaAlpha;

            Camera cam = BuildCamera(camPosSnapshot, yawSnapshot, pitchSnapshot, fovDeg, aspect);

            int procCount = Math.Max(1, Environment.ProcessorCount);
            long frame = Interlocked.Increment(ref frameCounter);

            // Per-frame TAA jitter using Halton(2,3), in pixel units [-0.5, 0.5)
            int frameIdx = unchecked((int)(frame & 0x7fffffff));
            float jitterX = Halton(frameIdx + 1, 2) - 0.5f;
            float jitterY = Halton(frameIdx + 1, 3) - 0.5f;

            Chexel[,] target = frameBuffer;

            // Phase 0: generate all hi-res rays in parallel (with camera jitter)
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

            // Phase 1: trace using prebuilt rays and EMA into single history buffer (basic TAA)
            threadpool.For(0, procCount, worker =>
            {
                int yStart = worker * hiH / procCount;
                int yEnd = (worker + 1) * hiH / procCount;
                for (int py = yStart; py < yEnd; py++)
                {
                    for (int px = 0; px < hiW; px++)
                    {
                        Rng rng = new Rng(PerFrameSeed(px, py, frame, 0, 0));
                        float uCenter = (px + 0.5f) / hiW;
                        float vCenter = (py + 0.5f) / hiH;
                        Vec3 cur = TraceFull(scene, rays[px, py], 0, 0, ref rng, uCenter, vCenter);
                        cur = ClampLuminance(cur, MaxLuminance).Saturate();
                        Vec3 prev = history[px, py];
                        float ia = 1.0f - blendAlpha;
                        Vec3 blended = new Vec3(prev.X * ia + cur.X * blendAlpha, prev.Y * ia + cur.Y * blendAlpha, prev.Z * ia + cur.Z * blendAlpha).Saturate();
                        history[px, py] = blended;
                    }
                }
            });

            // Phase 2: downsample history into console cells and write high-precision colors
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
                                topSum = topSum + history[x, yTop];
                                botSum = botSum + history[x, yBot];
                            }
                        }
                        float inv = 1.0f / (ss * ss);
                        Vec3 topAvg = new Vec3(topSum.X * inv, topSum.Y * inv, topSum.Z * inv).Saturate();
                        Vec3 botAvg = new Vec3(botSum.X * inv, botSum.Y * inv, botSum.Z * inv).Saturate();

                        // Store full-precision colors in Chexel; terminal will quantize as needed.
                        target[cx, cy] = new Chexel('▀', topAvg, botAvg);
                    }
                }
            });

            // Phase 3: blit to framebuffer
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

            historyValid = true;
            lastCamX = camPosSnapshot.X;
            lastCamY = camPosSnapshot.Y;
            lastCamZ = camPosSnapshot.Z;
            lastYaw = yawSnapshot;
            lastPitch = pitchSnapshot;
        }

        private bool ShouldResetHistory(Vec3 cam, float y, float p)
        {
            float dx = cam.X - lastCamX;
            float dy = cam.Y - lastCamY;
            float dz = cam.Z - lastCamZ;
            float trans = float.IsNaN(dx) ? 0.0f : MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            float dyaw = float.IsNaN(lastYaw) ? 0.0f : MathF.Abs(y - lastYaw);
            float dpitch = float.IsNaN(lastPitch) ? 0.0f : MathF.Abs(p - lastPitch);
            return trans > MotionTransReset || dyaw > MotionRotReset || dpitch > MotionRotReset;
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
            if ((float)rec.Mat.Reflectivity >= MirrorThreshold)
            {
                if (mirrorDepth >= MaxMirrorBounces) return color;
                Vec3 reflDir = Reflect(r.Dir, rec.N).Normalized();
                Ray reflRay = new Ray(rec.P + rec.N * Eps, reflDir);
                Vec3 reflCol = TraceFull(scene, reflRay, mirrorDepth + 1, diffuseDepth, ref rng, screenU, screenV);
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
                if (scene.Occluded(shadow, dist - Eps, screenU, screenV)) continue;
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
                    Vec3 Li = TraceFull(scene, bounce, mirrorDepth, diffuseDepth + 1, ref rng, screenU, screenV);
                    indirect += new Vec3(Li.X * rec.Mat.Albedo.X, Li.Y * rec.Mat.Albedo.Y, Li.Z * rec.Mat.Albedo.Z);
                }
                float invSpp = 1.0f / IndirectSamples;
                color += new Vec3(indirect.X * invSpp, indirect.Y * invSpp, indirect.Z * invSpp);
            }
            return color;
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
