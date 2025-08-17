namespace ConsoleGame.RayTracing
{
    public struct AmbientLight
    {
        public Vec3 Color;
        public float Intensity;

        public AmbientLight(Vec3 color, float intensity)
        {
            Color = color;
            Intensity = intensity;
        }
    }
}