namespace ConsoleGame.RayTracing.Objects
{
    public abstract class Hittable
    {
        public abstract bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV);
        public abstract bool TryGetBounds(out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ, out float cx, out float cy, out float cz);
    }
}

