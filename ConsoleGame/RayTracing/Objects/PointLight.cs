namespace ConsoleGame.RayTracing
{
    public sealed class PointLight
    {
        public Vec3 Position;
        public Vec3 Color;
        public float Intensity;

        public PointLight(Vec3 p, Vec3 c, float intensity)
        {
            Position = p;
            Color = c;
            Intensity = intensity;
        }
    }
}
