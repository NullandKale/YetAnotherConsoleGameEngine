// File: TaaAccumulator.cs
using System;

namespace ConsoleGame.RayTracing
{
    public sealed class TaaAccumulator
    {
        private readonly int width;
        private readonly int height;
        private readonly int ss;
        private readonly int jitterPeriod;
        private float alpha;
        private bool enabled = true;
        private bool historyValid = false;
        private int jitterPhase = 0;
        private float[,,] prevTop;
        private float[,,] prevBot;
        private float[,,] nextTop;
        private float[,,] nextBot;
        private double lastCamX = double.NaN;
        private double lastCamY = double.NaN;
        private double lastCamZ = double.NaN;
        private float lastYaw = float.NaN;
        private float lastPitch = float.NaN;

        public TaaAccumulator(bool enabled, int width, int height, int superSample, float alpha = 0.12f)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "width must be > 0.");
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "height must be > 0.");
            if (superSample <= 0) throw new ArgumentOutOfRangeException(nameof(superSample), "superSample must be > 0.");
            this.enabled = enabled;
            this.width = width;
            this.height = height;
            ss = superSample;
            jitterPeriod = Math.Max(1, ss * ss);
            this.alpha = Clamp01(alpha);
            prevTop = new float[width, height, 3];
            prevBot = new float[width, height, 3];
            nextTop = new float[width, height, 3];
            nextBot = new float[width, height, 3];
            historyValid = false;
        }

        public void SetEnabled(bool value)
        {
            enabled = value;
        }

        public void SetAlpha(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            alpha = Clamp01(value);
        }

        public void ResetHistory()
        {
            historyValid = false;
        }

        public void NotifyCamera(double x, double y, double z, float yaw, float pitch)
        {
            bool moved = double.IsNaN(lastCamX) || (x - lastCamX) * (x - lastCamX) + (y - lastCamY) * (y - lastCamY) + (z - lastCamZ) * (z - lastCamZ) > 1e-6 || MathF.Abs(yaw - lastYaw) > 1e-4f || MathF.Abs(pitch - lastPitch) > 1e-4f;
            if (moved)
            {
                historyValid = false;
            }
            lastCamX = x;
            lastCamY = y;
            lastCamZ = z;
            lastYaw = yaw;
            lastPitch = pitch;
        }

        public void GetJitter(out int jx, out int jy)
        {
            if (ss <= 1)
            {
                jx = 0;
                jy = 0;
                return;
            }
            jx = jitterPhase % ss;
            jy = jitterPhase / ss % ss;
        }

        public void Accumulate(int cx, int cy, float topR, float topG, float topB, float botR, float botG, float botB, out float outTopR, out float outTopG, out float outTopB, out float outBotR, out float outBotG, out float outBotB)
        {
            if ((uint)cx >= (uint)width || (uint)cy >= (uint)height) throw new ArgumentOutOfRangeException("cell index out of range");
            float tr = topR;
            float tg = topG;
            float tb = topB;
            float br = botR;
            float bg = botG;
            float bb = botB;
            if (enabled && historyValid)
            {
                float pr = prevTop[cx, cy, 0];
                float pg = prevTop[cx, cy, 1];
                float pb = prevTop[cx, cy, 2];
                float qr = prevBot[cx, cy, 0];
                float qg = prevBot[cx, cy, 1];
                float qb = prevBot[cx, cy, 2];
                float a = alpha;
                float ia = 1.0f - a;
                tr = pr * ia + tr * a;
                tg = pg * ia + tg * a;
                tb = pb * ia + tb * a;
                br = qr * ia + br * a;
                bg = qg * ia + bg * a;
                bb = qb * ia + bb * a;
            }
            nextTop[cx, cy, 0] = tr;
            nextTop[cx, cy, 1] = tg;
            nextTop[cx, cy, 2] = tb;
            nextBot[cx, cy, 0] = br;
            nextBot[cx, cy, 1] = bg;
            nextBot[cx, cy, 2] = bb;
            outTopR = tr;
            outTopG = tg;
            outTopB = tb;
            outBotR = br;
            outBotG = bg;
            outBotB = bb;
        }

        public void EndFrame()
        {
            float[,,] tmp = prevTop;
            prevTop = nextTop;
            nextTop = tmp;
            tmp = prevBot;
            prevBot = nextBot;
            nextBot = tmp;
            historyValid = true;
            if (ss > 1)
            {
                jitterPhase++;
                if (jitterPhase >= jitterPeriod)
                {
                    jitterPhase = 0;
                }
            }
        }

        private static float Clamp01(float v)
        {
            if (v < 0.0f) return 0.0f;
            if (v > 1.0f) return 1.0f;
            return v;
        }
    }
}
