using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ConsoleGame.RayTracing;

namespace ConsoleGame.RayTracing.Objects
{
    public sealed unsafe class VolumeGrid : Hittable, IDisposable
    {
        // === Bricked, pinned SoA storage laid out in Z-order (Morton) within each brick ===
        private const int BrickShift = 3; // 8^3 bricks
        private const int BrickSize = 1 << BrickShift; // 8
        private const int BrickMask = BrickSize - 1; // 7
        private const int BrickVolume = BrickSize * BrickSize * BrickSize; // 512

        private readonly int nx;
        private readonly int ny;
        private readonly int nz;
        private readonly int nbx;
        private readonly int nby;
        private readonly int nbz;
        private readonly int brickCount;
        private readonly int capacity;

        private readonly int[] mat;     // SoA: material IDs
        private readonly int[] meta;    // SoA: metadata IDs

        private GCHandle matHandle;
        private GCHandle metaHandle;

        private readonly int* matPtr;
        private readonly int* metaPtr;

        private readonly Vec3 minCorner;
        private readonly Vec3 voxelSize;
        private readonly Func<int, int, Material> materialLookup;

        // Wireframe controls
        private readonly bool wireframe;
        private readonly float wireWidthFrac; // [0..0.5] thickness as fraction of the smaller face dimension
        private readonly float wireMaxDistance; // max distance from ray origin to apply wireframe
        private static readonly Vec3 WireColor = new Vec3(0.0, 0.0, 0.0);
        private static readonly Vec3 WireColorCenter = new Vec3(1.0, 1.0, 1.0);
        private const float CenterWindowHalf = 0.000001f; // only the true center ray flags the center block

        // Cached "center block" (set when the center ray hits this grid)
        private int centerIx = int.MinValue;
        private int centerIy = int.MinValue;
        private int centerIz = int.MinValue;
        private bool centerValid = false;

        private bool disposed = false;

        // Backwards-compatible signature: aoStrength retained but ignored; new wireframe params appended with defaults.
        public VolumeGrid((int, int)[,,] cells, Vec3 minCorner, Vec3 voxelSize, Func<int, int, Material> materialLookup, bool enableWireframe = true, float wireWidthFraction = 0.06f, float wireMaxDistance = 16.0f)
        {
            nx = cells.GetLength(0);
            ny = cells.GetLength(1);
            nz = cells.GetLength(2);

            nbx = (nx + BrickMask) >> BrickShift;
            nby = (ny + BrickMask) >> BrickShift;
            nbz = (nz + BrickMask) >> BrickShift;
            brickCount = nbx * nby * nbz;
            capacity = brickCount * BrickVolume;

            this.mat = new int[capacity];
            this.meta = new int[capacity];

            matHandle = GCHandle.Alloc(mat, GCHandleType.Pinned);
            metaHandle = GCHandle.Alloc(meta, GCHandleType.Pinned);
            matPtr = (int*)matHandle.AddrOfPinnedObject().ToPointer();
            metaPtr = (int*)metaHandle.AddrOfPinnedObject().ToPointer();

            this.minCorner = minCorner;
            this.voxelSize = new Vec3(MathF.Max(1e-6f, voxelSize.X), MathF.Max(1e-6f, voxelSize.Y), MathF.Max(1e-6f, voxelSize.Z));
            this.materialLookup = materialLookup;
            this.wireframe = enableWireframe;
            if (wireWidthFraction < 0.0f) wireWidthFraction = 0.0f; if (wireWidthFraction > 0.5f) wireWidthFraction = 0.5f;
            this.wireWidthFrac = wireWidthFraction;
            if (wireMaxDistance < 0.0f) wireMaxDistance = 0.0f;
            this.wireMaxDistance = wireMaxDistance;

            for (int iz = 0; iz < nz; iz++)
                for (int iy = 0; iy < ny; iy++)
                    for (int ix = 0; ix < nx; ix++)
                    {
                        var c = cells[ix, iy, iz];
                        int idx = IndexOf(ix, iy, iz);
                        matPtr[idx] = c.Item1;
                        metaPtr[idx] = c.Item2;
                    }
        }

        public Vec3 BoundsMin { get { return minCorner; } }
        public Vec3 BoundsMax { get { return new Vec3(minCorner.X + nx * voxelSize.X, minCorner.Y + ny * voxelSize.Y, minCorner.Z + nz * voxelSize.Z); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            float minX = (float)minCorner.X; float minY = (float)minCorner.Y; float minZ = (float)minCorner.Z;
            float sizeX = (float)voxelSize.X; float sizeY = (float)voxelSize.Y; float sizeZ = (float)voxelSize.Z;
            float maxX = minX + nx * sizeX; float maxY = minY + ny * sizeY; float maxZ = minZ + nz * sizeZ;

            int enterAxis;
            float tEnter, tExit;
            if (!RayAabb(r, new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ), out tEnter, out tExit, out enterAxis)) return false;
            float t = tEnter; if (t < tMin) t = tMin; if (t > tMax || t > tExit) return false;

            const float eps = 1e-6f;
            t += eps;

            float ox = (float)r.Origin.X; float oy = (float)r.Origin.Y; float oz = (float)r.Origin.Z;
            float dx = (float)r.Dir.X; float dy = (float)r.Dir.Y; float dz = (float)r.Dir.Z;

            float px = ox + dx * t; float py = oy + dy * t; float pz = oz + dz * t;

            int ix = (int)MathF.Floor((px - minX) / sizeX); if (ix < 0) ix = 0; else if (ix >= nx) ix = nx - 1;
            int iy = (int)MathF.Floor((py - minY) / sizeY); if (iy < 0) iy = 0; else if (iy >= ny) iy = ny - 1;
            int iz = (int)MathF.Floor((pz - minZ) / sizeZ); if (iz < 0) iz = 0; else if (iz >= nz) iz = nz - 1;

            int stepX = dx > 0.0f ? 1 : dx < 0.0f ? -1 : 0;
            int stepY = dy > 0.0f ? 1 : dy < 0.0f ? -1 : 0;
            int stepZ = dz > 0.0f ? 1 : dz < 0.0f ? -1 : 0;

            float invDx = stepX == 0 ? 0.0f : 1.0f / dx;
            float invDy = stepY == 0 ? 0.0f : 1.0f / dy;
            float invDz = stepZ == 0 ? 0.0f : 1.0f / dz;

            float nextVx = minX + (stepX > 0 ? (ix + 1) * sizeX : ix * sizeX);
            float nextVy = minY + (stepY > 0 ? (iy + 1) * sizeY : iy * sizeY);
            float nextVz = minZ + (stepZ > 0 ? (iz + 1) * sizeZ : iz * sizeZ);

            float tMaxX = stepX == 0 ? float.PositiveInfinity : (nextVx - ox) * invDx;
            float tMaxY = stepY == 0 ? float.PositiveInfinity : (nextVy - oy) * invDy;
            float tMaxZ = stepZ == 0 ? float.PositiveInfinity : (nextVz - oz) * invDz;

            float tDeltaX = stepX == 0 ? float.PositiveInfinity : MathF.Abs(sizeX * invDx);
            float tDeltaY = stepY == 0 ? float.PositiveInfinity : MathF.Abs(sizeY * invDy);
            float tDeltaZ = stepZ == 0 ? float.PositiveInfinity : MathF.Abs(sizeZ * invDz);

            int lastAxis = enterAxis < 0 ? (tMaxX <= tMaxY && tMaxX <= tMaxZ ? 0 : tMaxY <= tMaxZ ? 1 : 2) : enterAxis;

            var lookup = materialLookup;
            bool wf = wireframe;
            float wireMax2 = wireMaxDistance <= 0.0f ? -1.0f : wireMaxDistance * wireMaxDistance;
            float dirLen2 = dx * dx + dy * dy + dz * dz;

            while (t <= tExit && t <= tMax)
            {
                if ((uint)ix < (uint)nx && (uint)iy < (uint)ny && (uint)iz < (uint)nz)
                {
                    int idx = IndexOf(ix, iy, iz);
                    int matId = matPtr[idx];
                    if (matId > 0)
                    {
                        int metaId = metaPtr[idx];

                        int normalAxis = lastAxis;
                        float hitT = MathF.Max(t, tMin);
                        if (normalAxis < 0)
                        {
                            if (tMaxX <= tMaxY && tMaxX <= tMaxZ) { normalAxis = 0; hitT = MathF.Max(tMaxX, tMin); }
                            else if (tMaxY <= tMaxZ) { normalAxis = 1; hitT = MathF.Max(tMaxY, tMin); }
                            else { normalAxis = 2; hitT = MathF.Max(tMaxZ, tMin); }
                        }

                        Vec3 n = FaceNormalFromAxis(normalAxis, stepX, stepY, stepZ);
                        Vec3 hitPoint = r.At(hitT);

                        bool withinWireRange = false;
                        if (wf && wireMax2 >= 0.0f)
                        {
                            float dist2 = hitT * hitT * dirLen2;
                            withinWireRange = dist2 <= wireMax2;
                        }

                        bool isCenterBlock = false;
                        if (wf)
                        {
                            bool isCenterRay = IsCenterUV(screenU, screenV);
                            if (isCenterRay)
                            {
                                centerIx = ix; centerIy = iy; centerIz = iz; centerValid = true;
                            }
                            isCenterBlock = centerValid && ix == centerIx && iy == centerIy && iz == centerIz;
                        }

                        Material m = lookup(matId, metaId);
                        if (wf && withinWireRange && IsWireOnFace(hitPoint, ix, iy, iz, normalAxis))
                        {
                            m.Albedo = isCenterBlock ? WireColorCenter : WireColor;
                        }

                        rec.T = hitT;
                        rec.P = hitPoint;
                        rec.N = n;
                        rec.Mat = m;
                        rec.U = 0.0f;
                        rec.V = 0.0f;
                        return true;
                    }
                }

                if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
                {
                    ix += stepX;
                    t = tMaxX;
                    tMaxX += tDeltaX;
                    lastAxis = 0;
                }
                else if (tMaxY <= tMaxZ)
                {
                    iy += stepY;
                    t = tMaxY;
                    tMaxY += tDeltaY;
                    lastAxis = 1;
                }
                else
                {
                    iz += stepZ;
                    t = tMaxZ;
                    tMaxZ += tDeltaZ;
                    lastAxis = 2;
                }

                if ((uint)ix >= (uint)nx || (uint)iy >= (uint)ny || (uint)iz >= (uint)nz) break;
            }

            return false;
        }

        // === Brick/Z-order addressing ===
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int IndexOf(int ix, int iy, int iz)
        {
            int bx = ix >> BrickShift; int by = iy >> BrickShift; int bz = iz >> BrickShift;
            int lx = ix & BrickMask; int ly = iy & BrickMask; int lz = iz & BrickMask;
            int brickLinear = ((bz * nby) + by) * nbx + bx;
            int localMorton = Morton3_3bits(lx, ly, lz);
            return brickLinear * BrickVolume + localMorton;
        }

        // Interleave 3 low bits of x,y,z: [x2 y2 z2 x1 y1 z1 x0 y0 z0]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Morton3_3bits(int x, int y, int z)
        {
            int m = ((x & 1) << 0) | ((y & 1) << 1) | ((z & 1) << 2)
                  | ((x & 2) << 2) | ((y & 2) << 3) | ((z & 2) << 4)
                  | ((x & 4) << 4) | ((y & 4) << 5) | ((z & 4) << 6);
            return m;
        }

        // === Wireframe helper ===
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsWireOnFace(Vec3 p, int ix, int iy, int iz, int axis)
        {
            double x0 = minCorner.X + ix * voxelSize.X; double x1 = x0 + voxelSize.X;
            double y0 = minCorner.Y + iy * voxelSize.Y; double y1 = y0 + voxelSize.Y;
            double z0 = minCorner.Z + iz * voxelSize.Z; double z1 = z0 + voxelSize.Z;

            if (axis == 0)
            {
                double dy = EdgeDistance((double)p.Y, y0, y1);
                double dz = EdgeDistance((double)p.Z, z0, z1);
                double w = wireWidthFrac * Math.Min(voxelSize.Y, voxelSize.Z);
                return dy <= w || dz <= w;
            }
            else if (axis == 1)
            {
                double dx = EdgeDistance((double)p.X, x0, x1);
                double dz = EdgeDistance((double)p.Z, z0, z1);
                double w = wireWidthFrac * Math.Min(voxelSize.X, voxelSize.Z);
                return dx <= w || dz <= w;
            }
            else
            {
                double dx = EdgeDistance((double)p.X, x0, x1);
                double dy = EdgeDistance((double)p.Y, y0, y1);
                double w = wireWidthFrac * Math.Min(voxelSize.X, voxelSize.Y);
                return dx <= w || dy <= w;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCenterUV(float u, float v)
        {
            return MathF.Abs(u - 0.5f) <= CenterWindowHalf && MathF.Abs(v - 0.5f) <= CenterWindowHalf;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double EdgeDistance(double v, double v0, double v1)
        {
            double a = v - v0; double b = v1 - v;
            if (a < 0.0) a = 0.0; if (b < 0.0) b = 0.0;
            return Math.Min(a, b);
        }

        // === Existing helpers ===

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vec3 FaceNormalFromAxis(int axis, int stepX, int stepY, int stepZ)
        {
            if (axis == 0) return new Vec3(stepX > 0 ? -1.0 : 1.0, 0.0, 0.0);
            if (axis == 1) return new Vec3(0.0, stepY > 0 ? -1.0 : 1.0, 0.0);
            if (axis == 2) return new Vec3(0.0, 0.0, stepZ > 0 ? -1.0 : 1.0);
            return new Vec3(0.0, 0.0, 0.0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ClampToGrid(int i, int n)
        {
            if (i < 0) return 0;
            if (i >= n) return n - 1;
            return i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool RayAabb(Ray r, Vec3 bmin, Vec3 bmax, out float tEnter, out float tExit, out int enterAxis)
        {
            tEnter = float.NegativeInfinity;
            tExit = float.PositiveInfinity;
            enterAxis = -1;

            if (!Slab((float)r.Origin.X, (float)r.Dir.X, (float)bmin.X, (float)bmax.X, ref tEnter, ref tExit, 0, ref enterAxis)) return false;
            if (!Slab((float)r.Origin.Y, (float)r.Dir.Y, (float)bmin.Y, (float)bmax.Y, ref tEnter, ref tExit, 1, ref enterAxis)) return false;
            if (!Slab((float)r.Origin.Z, (float)r.Dir.Z, (float)bmin.Z, (float)bmax.Z, ref tEnter, ref tExit, 2, ref enterAxis)) return false;

            return tExit >= MathF.Max(0.0f, tEnter);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Slab(float ro, float rd, float min, float max, ref float tEnter, ref float tExit, int axis, ref int enterAxis)
        {
            const float eps = 1e-12f;
            if (MathF.Abs(rd) < eps)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool NearlyEqual(float a, float b, float eps)
        {
            float diff = MathF.Abs(a - b);
            float scale = MathF.Max(1.0f, MathF.Max(MathF.Abs(a), MathF.Abs(b)));
            return diff <= eps * scale;
        }

        private static int AxisOfNextCrossing(float tCurrent, float tMaxX, float tMaxY, float tMaxZ)
        {
            float tNext = MathF.Min(tMaxX, MathF.Min(tMaxY, tMaxZ));
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
            double ax = stepX ? MathF.Abs(dir.X) : -1.0;
            double ay = stepY ? MathF.Abs(dir.Y) : -1.0;
            double az = stepZ ? MathF.Abs(dir.Z) : -1.0;
            if (ax >= ay && ax >= az) return 0;
            if (ay >= ax && ay >= az) return 1;
            return 2;
        }

        public override bool TryGetBounds(out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ, out float cx, out float cy, out float cz)
        {
            if (nx <= 0 || ny <= 0 || nz <= 0)
            {
                minX = minY = minZ = maxX = maxY = maxZ = cx = cy = cz = 0.0f;
                return false;
            }
            minX = (float)minCorner.X;
            minY = (float)minCorner.Y;
            minZ = (float)minCorner.Z;
            maxX = (float)(minCorner.X + nx * voxelSize.X);
            maxY = (float)(minCorner.Y + ny * voxelSize.Y);
            maxZ = (float)(minCorner.Z + nz * voxelSize.Z);
            cx = 0.5f * (minX + maxX);
            cy = 0.5f * (minY + maxY);
            cz = 0.5f * (minZ + maxZ);
            return true;
        }

        // === Disposal for pinned buffers ===
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (matHandle.IsAllocated) matHandle.Free();
            if (metaHandle.IsAllocated) metaHandle.Free();
        }

        ~VolumeGrid()
        {
            Dispose();
        }
    }
}
