// File: TaaAccumulator.cs
using ConsoleGame.RayTracing.Scenes;
using ConsoleGame.Renderer;
using ConsoleRayTracing;
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

        private float motionHistoryScale = 1.0f;

        private const float MotionDecayK = 6.0f;
        private const float HardResetMotion = 2.0f;
        private const float ColorDeltaLow = 0.02f;
        private const float ColorDeltaHigh = 0.40f;

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

        public float Alpha
        {
            get { return alpha; }
        }

        public void ResetHistory()
        {
            historyValid = false;
        }

        public void NotifyCamera(double x, double y, double z, float yaw, float pitch)
        {
            double dx = x - lastCamX;
            double dy = y - lastCamY;
            double dz = z - lastCamZ;
            float dyaw = float.IsNaN(lastYaw) ? 0.0f : MathF.Abs(yaw - lastYaw);
            float dpitch = float.IsNaN(lastPitch) ? 0.0f : MathF.Abs(pitch - lastPitch);
            double trans = double.IsNaN(dx) ? 0.0 : Math.Sqrt(dx * dx + dy * dy + dz * dz);
            float rot = MathF.Sqrt(dyaw * dyaw + dpitch * dpitch);
            float motion = (float)trans + 0.5f * rot;
            motionHistoryScale = Clamp01(MathF.Exp(-MotionDecayK * motion));
            if (motion > HardResetMotion)
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

        public void AccumulateReprojected(int cx, int cy, float baseTopR, float baseTopG, float baseTopB, float baseBotR, float baseBotG, float baseBotB, bool usePrevTop, float prevTopR, float prevTopG, float prevTopB, bool usePrevBot, float prevBotR, float prevBotG, float prevBotB, float alphaTop, float alphaBot, out float outTopR, out float outTopG, out float outTopB, out float outBotR, out float outBotG, out float outBotB)
        {
            if ((uint)cx >= (uint)width || (uint)cy >= (uint)height) throw new ArgumentOutOfRangeException("cell index out of range");
            float tr = baseTopR;
            float tg = baseTopG;
            float tb = baseTopB;
            float br = baseBotR;
            float bg = baseBotG;
            float bb = baseBotB;
            if (enabled && historyValid)
            {
                float pr = usePrevTop ? prevTopR : prevTop[cx, cy, 0];
                float pg = usePrevTop ? prevTopG : prevTop[cx, cy, 1];
                float pb = usePrevTop ? prevTopB : prevTop[cx, cy, 2];
                float qr = usePrevBot ? prevBotR : prevBot[cx, cy, 0];
                float qg = usePrevBot ? prevBotG : prevBot[cx, cy, 1];
                float qb = usePrevBot ? prevBotB : prevBot[cx, cy, 2];

                float dTop = MathF.Sqrt((tr - pr) * (tr - pr) + (tg - pg) * (tg - pg) + (tb - pb) * (tb - pb));
                float dBot = MathF.Sqrt((br - qr) * (br - qr) + (bg - qg) * (bg - qg) + (bb - qb) * (bb - qb));
                float colorKeepTop = 1.0f - Smoothstep(ColorDeltaLow, ColorDeltaHigh, dTop);
                float colorKeepBot = 1.0f - Smoothstep(ColorDeltaLow, ColorDeltaHigh, dBot);

                float histKeepTop = motionHistoryScale * colorKeepTop;
                float histKeepBot = motionHistoryScale * colorKeepBot;

                float aBaseT = Clamp01(alphaTop);
                float aBaseB = Clamp01(alphaBot);

                float aT = 1.0f - (1.0f - aBaseT) * histKeepTop;
                float aB = 1.0f - (1.0f - aBaseB) * histKeepBot;

                float iaT = 1.0f - aT;
                float iaB = 1.0f - aB;

                tr = pr * iaT + tr * aT;
                tg = pg * iaT + tg * aT;
                tb = pb * iaT + tb * aT;
                br = qr * iaB + br * aB;
                bg = qg * iaB + bg * aB;
                bb = qb * iaB + bb * aB;
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

        public void SamplePrevTop(float x, float y, out float r, out float g, out float b)
        {
            SampleBilinear(prevTop, x, y, out r, out g, out b);
        }

        public void SamplePrevBot(float x, float y, out float r, out float g, out float b)
        {
            SampleBilinear(prevBot, x, y, out r, out g, out b);
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

        private void SampleBilinear(float[,,] src, float x, float y, out float r, out float g, out float b)
        {
            float xf = x;
            float yf = y;
            if (xf < 0.0f) xf = 0.0f;
            if (yf < 0.0f) yf = 0.0f;
            if (xf > width - 1) xf = width - 1;
            if (yf > height - 1) yf = height - 1;

            int x0 = (int)MathF.Floor(xf);
            int y0 = (int)MathF.Floor(yf);
            int x1 = x0 + 1; if (x1 >= width) x1 = width - 1;
            int y1 = y0 + 1; if (y1 >= height) y1 = height - 1;

            float tx = xf - x0;
            float ty = yf - y0;

            float r00 = src[x0, y0, 0], g00 = src[x0, y0, 1], b00 = src[x0, y0, 2];
            float r10 = src[x1, y0, 0], g10 = src[x1, y0, 1], b10 = src[x1, y0, 2];
            float r01 = src[x0, y1, 0], g01 = src[x0, y1, 1], b01 = src[x0, y1, 2];
            float r11 = src[x1, y1, 0], g11 = src[x1, y1, 1], b11 = src[x1, y1, 2];

            float r0 = r00 * (1.0f - tx) + r10 * tx, g0 = g00 * (1.0f - tx) + g10 * tx, b0 = b00 * (1.0f - tx) + b10 * tx;
            float r1 = r01 * (1.0f - tx) + r11 * tx, g1 = g01 * (1.0f - tx) + g11 * tx, b1 = b01 * (1.0f - tx) + b11 * tx;

            r = r0 * (1.0f - ty) + r1 * ty;
            g = g0 * (1.0f - ty) + g1 * ty;
            b = b0 * (1.0f - ty) + b1 * ty;
        }

        private static float Clamp01(float v)
        {
            if (v < 0.0f) return 0.0f;
            if (v > 1.0f) return 1.0f;
            return v;
        }

        private static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Clamp01((x - edge0) / MathF.Max(1e-6f, edge1 - edge0));
            return t * t * (3.0f - 2.0f * t);
        }
    }
}

