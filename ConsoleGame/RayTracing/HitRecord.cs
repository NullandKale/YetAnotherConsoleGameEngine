namespace ConsoleGame.RayTracing
{
    public struct HitRecord
    {
        public float T;
        public Vec3 P;
        public Vec3 N;
        public Material Mat;
        public float U;
        public float V;
    }

}
