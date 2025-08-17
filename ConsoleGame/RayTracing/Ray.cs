namespace ConsoleGame.RayTracing
{
    public struct Ray
    {
        public Vec3 Origin;
        public Vec3 Dir;

        public Ray(Vec3 o, Vec3 d)
        {
            Origin = o;
            Dir = d.Normalized();
        }

        public Vec3 At(float t)
        {
            return Origin + Dir * t;
        }
    }
}
