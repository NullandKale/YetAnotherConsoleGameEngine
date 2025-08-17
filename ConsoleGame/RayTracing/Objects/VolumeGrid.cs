using System;
using ConsoleGame.RayTracing;

namespace ConsoleGame.RayTracing.Objects
{
    public sealed class VolumeGrid : Hittable
    {
        private readonly (int matId, int metaId)[,,] cells;
        private readonly int nx;
        private readonly int ny;
        private readonly int nz;
        private readonly Vec3 minCorner;
        private readonly Vec3 voxelSize;
        private readonly Func<int, int, Material> materialLookup;

        public VolumeGrid((int, int)[,,] cells, Vec3 minCorner, Vec3 voxelSize, Func<int, int, Material> materialLookup)
        {
            this.cells = cells;
            nx = cells.GetLength(0);
            ny = cells.GetLength(1);
            nz = cells.GetLength(2);
            this.minCorner = minCorner;
            this.voxelSize = new Vec3(Math.Max(1e-6, voxelSize.X), Math.Max(1e-6, voxelSize.Y), Math.Max(1e-6, voxelSize.Z));
            this.materialLookup = materialLookup;
        }
        public Vec3 BoundsMin { get { return minCorner; } }
        public Vec3 BoundsMax { get { return new Vec3(minCorner.X + nx * voxelSize.X, minCorner.Y + ny * voxelSize.Y, minCorner.Z + nz * voxelSize.Z); } }


        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec)
        {
            Vec3 maxCorner = new Vec3(minCorner.X + nx * voxelSize.X, minCorner.Y + ny * voxelSize.Y, minCorner.Z + nz * voxelSize.Z);
            int enterAxis = -1;
            float tEnter, tExit;
            if (!RayAabb(r, minCorner, maxCorner, out tEnter, out tExit, out enterAxis)) return false;
            float t = Math.Max(tEnter, tMin);
            if (t > tMax || t > tExit) return false;

            Vec3 p = r.At(t + 1e-6f);
            int ix = ClampToGrid((int)Math.Floor((p.X - minCorner.X) / voxelSize.X), nx);
            int iy = ClampToGrid((int)Math.Floor((p.Y - minCorner.Y) / voxelSize.Y), ny);
            int iz = ClampToGrid((int)Math.Floor((p.Z - minCorner.Z) / voxelSize.Z), nz);

            int stepX = r.Dir.X > 0.0 ? 1 : r.Dir.X < 0.0 ? -1 : 0;
            int stepY = r.Dir.Y > 0.0 ? 1 : r.Dir.Y < 0.0 ? -1 : 0;
            int stepZ = r.Dir.Z > 0.0 ? 1 : r.Dir.Z < 0.0 ? -1 : 0;

            float nextVx = minCorner.X + (stepX > 0 ? (ix + 1) * voxelSize.X : ix * voxelSize.X);
            float nextVy = minCorner.Y + (stepY > 0 ? (iy + 1) * voxelSize.Y : iy * voxelSize.Y);
            float nextVz = minCorner.Z + (stepZ > 0 ? (iz + 1) * voxelSize.Z : iz * voxelSize.Z);

            float tMaxX = stepX == 0 ? float.PositiveInfinity : (nextVx - r.Origin.X) / r.Dir.X;
            float tMaxY = stepY == 0 ? float.PositiveInfinity : (nextVy - r.Origin.Y) / r.Dir.Y;
            float tMaxZ = stepZ == 0 ? float.PositiveInfinity : (nextVz - r.Origin.Z) / r.Dir.Z;

            float tDeltaX = stepX == 0 ? float.PositiveInfinity : Math.Abs(voxelSize.X / r.Dir.X);
            float tDeltaY = stepY == 0 ? float.PositiveInfinity : Math.Abs(voxelSize.Y / r.Dir.Y);
            float tDeltaZ = stepZ == 0 ? float.PositiveInfinity : Math.Abs(voxelSize.Z / r.Dir.Z);

            int lastAxis = enterAxis;

            while (t <= tExit && t <= tMax)
            {
                if (ix >= 0 && ix < nx && iy >= 0 && iy < ny && iz >= 0 && iz < nz)
                {
                    var cell = cells[ix, iy, iz];
                    int matId = cell.Item1;
                    int metaId = cell.Item2; // placeholder for linking to world objects (e.g., chests)
                    if (matId > 0)
                    {
                        Vec3 n = FaceNormalFromAxis(lastAxis, stepX, stepY, stepZ);
                        Material m = materialLookup(matId, metaId);
                        rec.T = Math.Max(t, tMin);
                        rec.P = r.At(rec.T);
                        rec.N = n;
                        rec.Mat = m;
                        rec.U = 0.0f;
                        rec.V = 0.0f;
                        return true;
                    }
                }
                if (tMaxX < tMaxY && tMaxX < tMaxZ)
                {
                    ix += stepX;
                    t = tMaxX;
                    tMaxX += tDeltaX;
                    lastAxis = 0;
                    if (ix < 0 || ix >= nx) break;
                }
                else if (tMaxY < tMaxZ)
                {
                    iy += stepY;
                    t = tMaxY;
                    tMaxY += tDeltaY;
                    lastAxis = 1;
                    if (iy < 0 || iy >= ny) break;
                }
                else
                {
                    iz += stepZ;
                    t = tMaxZ;
                    tMaxZ += tDeltaZ;
                    lastAxis = 2;
                    if (iz < 0 || iz >= nz) break;
                }
            }

            return false;
        }

        private static Vec3 FaceNormalFromAxis(int axis, int stepX, int stepY, int stepZ)
        {
            if (axis == 0) return new Vec3(stepX > 0 ? -1.0 : 1.0, 0.0, 0.0);
            if (axis == 1) return new Vec3(0.0, stepY > 0 ? -1.0 : 1.0, 0.0);
            if (axis == 2) return new Vec3(0.0, 0.0, stepZ > 0 ? -1.0 : 1.0);
            return new Vec3(0.0, 0.0, 0.0);
        }

        private static int ClampToGrid(int i, int n)
        {
            if (i < 0) return 0;
            if (i >= n) return n - 1;
            return i;
        }

        private static bool RayAabb(Ray r, Vec3 bmin, Vec3 bmax, out float tEnter, out float tExit, out int enterAxis)
        {
            tEnter = float.NegativeInfinity;
            tExit = float.PositiveInfinity;
            enterAxis = -1;

            if (!Slab(r.Origin.X, r.Dir.X, bmin.X, bmax.X, ref tEnter, ref tExit, 0, ref enterAxis)) return false;
            if (!Slab(r.Origin.Y, r.Dir.Y, bmin.Y, bmax.Y, ref tEnter, ref tExit, 1, ref enterAxis)) return false;
            if (!Slab(r.Origin.Z, r.Dir.Z, bmin.Z, bmax.Z, ref tEnter, ref tExit, 2, ref enterAxis)) return false;

            return tExit >= Math.Max(0.0, tEnter);
        }

        private static bool Slab(float ro, float rd, float min, float max, ref float tEnter, ref float tExit, int axis, ref int enterAxis)
        {
            if (Math.Abs(rd) < 1e-12)
            {
                if (ro < min || ro > max) return false;
                return true;
            }
            float inv = 1.0f / rd;
            float t0 = (min - ro) * inv;
            float t1 = (max - ro) * inv;
            if (t0 > t1)
            {
                float tmp = t0; t0 = t1; t1 = tmp;
            }
            if (t0 > tEnter)
            {
                tEnter = t0;
                enterAxis = axis;
            }
            if (t1 < tExit) tExit = t1;
            return tExit >= tEnter;
        }
    }
}
