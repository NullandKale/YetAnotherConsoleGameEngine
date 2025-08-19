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

            float nextVx = minCorner.X + (stepX > 0 ? (ix + 1) * (float)voxelSize.X : ix * (float)voxelSize.X);
            float nextVy = minCorner.Y + (stepY > 0 ? (iy + 1) * (float)voxelSize.Y : iy * (float)voxelSize.Y);
            float nextVz = minCorner.Z + (stepZ > 0 ? (iz + 1) * (float)voxelSize.Z : iz * (float)voxelSize.Z);

            float tMaxX = stepX == 0 ? float.PositiveInfinity : (nextVx - (float)r.Origin.X) / (float)r.Dir.X;
            float tMaxY = stepY == 0 ? float.PositiveInfinity : (nextVy - (float)r.Origin.Y) / (float)r.Dir.Y;
            float tMaxZ = stepZ == 0 ? float.PositiveInfinity : (nextVz - (float)r.Origin.Z) / (float)r.Dir.Z;

            float tDeltaX = stepX == 0 ? float.PositiveInfinity : Math.Abs((float)voxelSize.X / (float)r.Dir.X);
            float tDeltaY = stepY == 0 ? float.PositiveInfinity : Math.Abs((float)voxelSize.Y / (float)r.Dir.Y);
            float tDeltaZ = stepZ == 0 ? float.PositiveInfinity : Math.Abs((float)voxelSize.Z / (float)r.Dir.Z);

            int lastAxis = enterAxis;
            if (lastAxis < 0)
            {
                lastAxis = AxisOfNextCrossing(t, tMaxX, tMaxY, tMaxZ);
            }

            const float eps = 1e-6f;

            while (t <= tExit && t <= tMax)
            {
                if (ix >= 0 && ix < nx && iy >= 0 && iy < ny && iz >= 0 && iz < nz)
                {
                    var cell = cells[ix, iy, iz];
                    int matId = cell.Item1;
                    int metaId = cell.Item2;
                    if (matId > 0)
                    {
                        int normalAxis;
                        float hitT;
                        if (lastAxis >= 0)
                        {
                            normalAxis = lastAxis;
                            hitT = Math.Max(t, tMin);
                        }
                        else
                        {
                            int axisNext = AxisOfNextCrossing(t, tMaxX, tMaxY, tMaxZ);
                            if (axisNext < 0) return false;
                            float tNext = axisNext == 0 ? tMaxX : axisNext == 1 ? tMaxY : tMaxZ;
                            normalAxis = axisNext;
                            hitT = Math.Max(tNext, tMin);
                        }

                        Vec3 n = FaceNormalFromAxis(normalAxis, stepX, stepY, stepZ);
                        Material m = materialLookup(matId, metaId);
                        rec.T = hitT;
                        rec.P = r.At(rec.T);
                        rec.N = n;
                        rec.Mat = m;
                        rec.U = 0.0f;
                        rec.V = 0.0f;
                        return true;
                    }
                }

                float tNextCross = Math.Min(tMaxX, Math.Min(tMaxY, tMaxZ));
                if (tNextCross > tExit || tNextCross > tMax) break;

                bool stepAlongX = stepX != 0 && NearlyEqual(tMaxX, tNextCross, eps);
                bool stepAlongY = stepY != 0 && NearlyEqual(tMaxY, tNextCross, eps);
                bool stepAlongZ = stepZ != 0 && NearlyEqual(tMaxZ, tNextCross, eps);

                int chosenAxis = SelectNormalAxis(stepAlongX, stepAlongY, stepAlongZ, r.Dir);
                if (stepAlongX)
                {
                    ix += stepX;
                    tMaxX += tDeltaX;
                }
                if (stepAlongY)
                {
                    iy += stepY;
                    tMaxY += tDeltaY;
                }
                if (stepAlongZ)
                {
                    iz += stepZ;
                    tMaxZ += tDeltaZ;
                }

                t = tNextCross;
                lastAxis = chosenAxis;

                if (ix < 0 || ix >= nx || iy < 0 || iy >= ny || iz < 0 || iz >= nz) break;
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

            if (!Slab((float)r.Origin.X, (float)r.Dir.X, (float)bmin.X, (float)bmax.X, ref tEnter, ref tExit, 0, ref enterAxis)) return false;
            if (!Slab((float)r.Origin.Y, (float)r.Dir.Y, (float)bmin.Y, (float)bmax.Y, ref tEnter, ref tExit, 1, ref enterAxis)) return false;
            if (!Slab((float)r.Origin.Z, (float)r.Dir.Z, (float)bmin.Z, (float)bmax.Z, ref tEnter, ref tExit, 2, ref enterAxis)) return false;

            return tExit >= Math.Max(0.0, tEnter);
        }

        private static bool Slab(float ro, float rd, float min, float max, ref float tEnter, ref float tExit, int axis, ref int enterAxis)
        {
            const float eps = 1e-12f;
            if (Math.Abs(rd) < eps)
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

        private static bool NearlyEqual(float a, float b, float eps)
        {
            float diff = Math.Abs(a - b);
            float scale = Math.Max(1.0f, Math.Max(Math.Abs(a), Math.Abs(b)));
            return diff <= eps * scale;
        }

        private static int AxisOfNextCrossing(float tCurrent, float tMaxX, float tMaxY, float tMaxZ)
        {
            float tNext = Math.Min(tMaxX, Math.Min(tMaxY, tMaxZ));
            if (float.IsInfinity(tNext)) return -1;
            if (NearlyEqual(tMaxX, tNext, 1e-6f)) return 0;
            if (NearlyEqual(tMaxY, tNext, 1e-6f)) return 1;
            return 2;
        }

        private static int SelectNormalAxis(bool stepX, bool stepY, bool stepZ, Vec3 dir)
        {
            if (stepX && !stepY && !stepZ) return 0;
            if (!stepX && stepY && !stepZ) return 1;
            if (!stepX && !stepY && stepZ) return 2;
            double ax = stepX ? Math.Abs(dir.X) : -1.0;
            double ay = stepY ? Math.Abs(dir.Y) : -1.0;
            double az = stepZ ? Math.Abs(dir.Z) : -1.0;
            if (ax >= ay && ax >= az) return 0;
            if (ay >= ax && ay >= az) return 1;
            return 2;
        }
    }
}
