using System;
using System.Collections.Generic;
using ConsoleGame.RayTracing;
using ConsoleGame.RayTracing.Objects;

namespace ConsoleGame.RayTracing.Scenes
{
    // Lightweight scene entity that animates a day-night cycle by adjusting
    // ambient light, background gradient, and driving a sun/moon PointLight.
    internal sealed class DayNightEntity : ISceneEntity
    {
        public bool Enabled { get; set; } = true;

        private float time;
        private readonly float cycleSeconds;
        private readonly float sunRadius;
        private readonly Vec3 daySkyTop;
        private readonly Vec3 daySkyBottom;
        private readonly Vec3 nightSkyTop;
        private readonly Vec3 nightSkyBottom;
        private readonly float dayAmbient;
        private readonly float nightAmbient;

        private PointLight sun;
        private PointLight moon;

        public DayNightEntity(
            float cycleSeconds = 120.0f,
            float sunRadius = 2000.0f,
            Vec3? daySkyTop = null, Vec3? daySkyBottom = null,
            Vec3? nightSkyTop = null, Vec3? nightSkyBottom = null)
        {
            this.cycleSeconds = Math.Max(1.0f, cycleSeconds);
            this.sunRadius = sunRadius;
            this.daySkyTop = daySkyTop ?? new Vec3(0.30, 0.55, 0.95);
            this.daySkyBottom = daySkyBottom ?? new Vec3(0.80, 0.90, 1.00);
            this.nightSkyTop = nightSkyTop ?? new Vec3(0.02, 0.03, 0.06);
            this.nightSkyBottom = nightSkyBottom ?? new Vec3(0.00, 0.00, 0.00);
        }

        public void Update(float dt, Scene scene)
        {
            if (!Enabled || scene == null) return;

            // time 0..1 over a cycle
            time += Math.Max(0.0f, dt);
            float t01 = (time % cycleSeconds) / cycleSeconds;

            // Sun angle: t01=0 at sunrise, 0.5 sunset by default
            float theta = (t01 * 2.0f * MathF.PI) - MathF.PI * 0.5f; // -90deg .. 270deg
            float sx = MathF.Cos(theta);
            float sy = MathF.Sin(theta); // height: >0 above horizon
            float sz = 0.25f;            // slight tilt for nicer visuals
            float norm = MathF.Sqrt(sx * sx + sy * sy + sz * sz);
            sx /= norm; sy /= norm; sz /= norm;

            // Place sun/moon on a large circle around origin
            Vec3 sunPos = new Vec3(sx * sunRadius, Math.Max(50.0, sy * sunRadius), sz * sunRadius);
            Vec3 moonPos = new Vec3(-sunPos.X, Math.Max(50.0, -sunPos.Y), -sunPos.Z);

            // Ensure lights exist
            if (sun == null)
            {
                sun = new PointLight(sunPos, new Vec3(1.00, 0.96, 0.88), 0.0f);
                scene.Lights.Add(sun);
            }
            if (moon == null)
            {
                moon = new PointLight(moonPos, new Vec3(0.65, 0.70, 0.90), 0.0f);
                scene.Lights.Add(moon);
            }

            // Lighting intensities based on elevation
            float sunN = MathF.Max(0.0f, sy);              // above horizon
            float moonN = MathF.Max(0.0f, -sy);            // opposite

            // Smooth daylight curve for softer sunrise/sunset
            float sunI = sunN * sunN;                      // quadratic falloff
            float moonI = MathF.Sqrt(moonN) * 0.10f;       // dimmer moonlight

            sun.Position = sunPos;
            sun.Intensity = 300000.0f * sunI;
            moon.Position = moonPos;
            moon.Intensity = 8000.0f * moonI;

            // Background blend
            float skyBlend = Clamp01(sunI * 1.5f);  // brighten the sky more aggressively
            scene.BackgroundTop = Lerp(nightSkyTop, daySkyTop, skyBlend);
            scene.BackgroundBottom = Lerp(nightSkyBottom, daySkyBottom, skyBlend);

        }

        public IEnumerable<Hittable> GetHittables()
        {
            yield break;
        }

        private static Vec3 Lerp(Vec3 a, Vec3 b, float t)
        {
            return a * (1.0f - t) + b * t;
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private static float Clamp01(float x)
        {
            if (x < 0.0f) return 0.0f;
            if (x > 1.0f) return 1.0f;
            return x;
        }
    }
}
