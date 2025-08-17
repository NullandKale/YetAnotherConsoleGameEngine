namespace ConsoleGame.RayTracing
{
    public sealed class Camera
    {
        public Vec3 Origin;
        public Vec3 Forward;
        public Vec3 Right;
        public Vec3 Up;
        public float FovYRad;
        public float Aspect;

        public Camera(Vec3 eye, Vec3 lookAt, Vec3 up, float fovDeg, float aspect)
        {
            Origin = eye;
            Forward = (lookAt - eye).Normalized();
            Right = Forward.Cross(up).Normalized();
            Up = Right.Cross(Forward).Normalized();
            FovYRad = fovDeg * MathF.PI / 180.0f;
            Aspect = aspect;
        }

        public Ray MakeRay(int px, int py, int width, int height)
        {
            float ndcX = (px + 0.5f) / width * 2.0f - 1.0f;
            float ndcY = 1.0f - (py + 0.5f) / height * 2.0f;
            float tanHalf = MathF.Tan(FovYRad * 0.5f);
            float camX = ndcX * tanHalf * Aspect;
            float camY = ndcY * tanHalf;
            Vec3 dir = (Forward + Right * camX + Up * camY).Normalized();
            return new Ray(Origin, dir);
        }
    }
}
