namespace ConsoleGame.RayTracing
{
    public sealed class TemporalAA
    {
        private Vec3[,] history;
        private bool historyValid;

        private float taaAlpha;
        private float motionTransReset;
        private float motionRotReset;

        private float lastCamX = float.NaN;
        private float lastCamY = float.NaN;
        private float lastCamZ = float.NaN;
        private float lastYaw = float.NaN;
        private float lastPitch = float.NaN;

        private int width;
        private int height;

        public TemporalAA(int width, int height, float taaAlpha = 0.05f, float motionTransReset = 0.0025f, float motionRotReset = 0.0025f)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Invalid TAA buffer size.");
            this.width = width;
            this.height = height;
            this.taaAlpha = MathF.Max(0.0f, MathF.Min(1.0f, taaAlpha));
            this.motionTransReset = MathF.Max(0.0f, motionTransReset);
            this.motionRotReset = MathF.Max(0.0f, motionRotReset);
            history = new Vec3[width, height];
            historyValid = false;
        }

        public void Resize(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Invalid TAA buffer size.");
            this.width = width;
            this.height = height;
            history = new Vec3[width, height];
            historyValid = false;
            lastCamX = float.NaN;
            lastCamY = float.NaN;
            lastCamZ = float.NaN;
            lastYaw = float.NaN;
            lastPitch = float.NaN;
        }

        public void SetAlpha(float alpha)
        {
            taaAlpha = MathF.Max(0.0f, MathF.Min(1.0f, alpha));
        }

        public void SetMotionResetThresholds(float translation, float rotation)
        {
            motionTransReset = MathF.Max(0.0f, translation);
            motionRotReset = MathF.Max(0.0f, rotation);
        }

        public bool ShouldResetHistory(Vec3 cam, float yaw, float pitch)
        {
            float dx = cam.X - lastCamX;
            float dy = cam.Y - lastCamY;
            float dz = cam.Z - lastCamZ;
            float trans = float.IsNaN(dx) ? 0.0f : MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            float dyaw = float.IsNaN(lastYaw) ? 0.0f : MathF.Abs(yaw - lastYaw);
            float dpitch = float.IsNaN(lastPitch) ? 0.0f : MathF.Abs(pitch - lastPitch);
            return trans > motionTransReset || dyaw > motionRotReset || dpitch > motionRotReset;
        }

        public void CommitCamera(Vec3 cam, float yaw, float pitch)
        {
            lastCamX = cam.X;
            lastCamY = cam.Y;
            lastCamZ = cam.Z;
            lastYaw = yaw;
            lastPitch = pitch;
        }

        public void Reset()
        {
            historyValid = false;
        }

        public Vec3[,] BlendIntoHistory(Vec3[,] current, bool forceReset = false, float? overrideAlpha = null)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (current.GetLength(0) != width || current.GetLength(1) != height) throw new ArgumentException("Current buffer size does not match TAA history.");

            float alpha = forceReset || !historyValid ? 1.0f : (overrideAlpha.HasValue ? MathF.Max(0.0f, MathF.Min(1.0f, overrideAlpha.Value)) : taaAlpha);
            float ia = 1.0f - alpha;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vec3 prev = history[x, y];
                    Vec3 cur = current[x, y];
                    history[x, y] = new Vec3(prev.X * ia + cur.X * alpha, prev.Y * ia + cur.Y * alpha, prev.Z * ia + cur.Z * alpha);
                }
            }

            historyValid = true;
            return history;
        }

        public Vec3[,] GetHistory()
        {
            return history;
        }

        public bool HistoryValid
        {
            get { return historyValid; }
        }
    }
}
