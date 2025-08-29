using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace ConsoleGame.RayTracing.Objects
{
    public sealed class Triangle : Hittable
    {
        public Vec3 A;
        public Vec3 B;
        public Vec3 C;
        public Material Mat;

        // Cached edges (A->B, A->C) and unit normal for fast hits.
        private readonly float e1x, e1y, e1z;
        private readonly float e2x, e2y, e2z;
        private readonly float nx, ny, nz;

        // SIMD cached 4-wide (xyz0) vectors for SSE path.
        private readonly Vector128<float> A4;
        private readonly Vector128<float> E14;
        private readonly Vector128<float> E24;

        // Cached bounds (expanded slightly) and center.
        private readonly float bMinX, bMinY, bMinZ, bMaxX, bMaxY, bMaxZ, bCx, bCy, bCz;

        private const float EpsDet = 1e-8f;
        private const float BoundEps = 1e-4f;

        public Triangle(Vec3 a, Vec3 b, Vec3 c, Material mat)
        {
            A = a;
            B = b;
            C = c;
            Mat = mat;

            e1x = B.X - A.X; e1y = B.Y - A.Y; e1z = B.Z - A.Z;
            e2x = C.X - A.X; e2y = C.Y - A.Y; e2z = C.Z - A.Z;

            float nnx = e1y * e2z - e1z * e2y;
            float nny = e1z * e2x - e1x * e2z;
            float nnz = e1x * e2y - e1y * e2x;
            float invLen = 1.0f / MathF.Max(1e-20f, MathF.Sqrt(nnx * nnx + nny * nny + nnz * nnz));
            nx = nnx * invLen; ny = nny * invLen; nz = nnz * invLen;

            if (Sse.IsSupported)
            {
                A4 = Vector128.Create(A.X, A.Y, A.Z, 0f);
                E14 = Vector128.Create(e1x, e1y, e1z, 0f);
                E24 = Vector128.Create(e2x, e2y, e2z, 0f);
            }

            // Compute and cache expanded AABB and its center.
            float mnx = MathF.Min(A.X, MathF.Min(B.X, C.X));
            float mny = MathF.Min(A.Y, MathF.Min(B.Y, C.Y));
            float mnz = MathF.Min(A.Z, MathF.Min(B.Z, C.Z));
            float mxx = MathF.Max(A.X, MathF.Max(B.X, C.X));
            float mxy = MathF.Max(A.Y, MathF.Max(B.Y, C.Y));
            float mxz = MathF.Max(A.Z, MathF.Max(B.Z, C.Z));
            bMinX = mnx - BoundEps; bMinY = mny - BoundEps; bMinZ = mnz - BoundEps;
            bMaxX = mxx + BoundEps; bMaxY = mxy + BoundEps; bMaxZ = mxz + BoundEps;
            bCx = 0.5f * (bMinX + bMaxX);
            bCy = 0.5f * (bMinY + bMaxY);
            bCz = 0.5f * (bMinZ + bMaxZ);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            if (Sse.IsSupported && Sse41.IsSupported)
            {
                // dir, origin, O-A
                Vector128<float> D = Vector128.Create(r.Dir.X, r.Dir.Y, r.Dir.Z, 0f);
                Vector128<float> O = Vector128.Create(r.Origin.X, r.Origin.Y, r.Origin.Z, 0f);
                Vector128<float> S = Sse.Subtract(O, A4);

                // h = D x E2 using shuffles (YZX permutation 0xC9)
                const byte YZX = 0xC9;
                Vector128<float> Dyzx = Sse.Shuffle(D, D, YZX);
                Vector128<float> E2yzx = Sse.Shuffle(E24, E24, YZX);
                Vector128<float> h = Sse.Subtract(Sse.Multiply(D, E2yzx), Sse.Multiply(E24, Dyzx));
                h = Sse.Shuffle(h, h, YZX);

                // det = dot(E1, h) (mask 0x71 => xyz·xyz -> x lane)
                float det = Sse41.DotProduct(E14, h, 0x71).ToScalar();
                if (MathF.Abs(det) < EpsDet)
                {
                    return false;
                }
                float invDet = 1.0f / det;

                // u = dot(S, h) * invDet
                float u = Sse41.DotProduct(S, h, 0x71).ToScalar() * invDet;
                if (u < 0.0f || u > 1.0f)
                {
                    return false;
                }

                // q = S x E1
                Vector128<float> Syzx = Sse.Shuffle(S, S, YZX);
                Vector128<float> E1yzx = Sse.Shuffle(E14, E14, YZX);
                Vector128<float> q = Sse.Subtract(Sse.Multiply(S, E1yzx), Sse.Multiply(E14, Syzx));
                q = Sse.Shuffle(q, q, YZX);

                // v = dot(D, q) * invDet
                float v = Sse41.DotProduct(D, q, 0x71).ToScalar() * invDet;
                if (v < 0.0f || (u + v) > 1.0f)
                {
                    return false;
                }

                // t = dot(E2, q) * invDet
                float t = Sse41.DotProduct(E24, q, 0x71).ToScalar() * invDet;
                if (t < tMin || t > tMax)
                {
                    return false;
                }

                rec.T = t;
                rec.P = new Vec3(r.Origin.X + t * r.Dir.X, r.Origin.Y + t * r.Dir.Y, r.Origin.Z + t * r.Dir.Z);
                float ndotd = nx * r.Dir.X + ny * r.Dir.Y + nz * r.Dir.Z;
                rec.N = ndotd < 0.0f ? new Vec3(nx, ny, nz) : new Vec3(-nx, -ny, -nz);
                rec.Mat = Mat;
                rec.U = u;
                rec.V = v;
                return true;
            }

            // Scalar fallback (fully inlined Möller–Trumbore with cached edges)
            float px = r.Dir.Y * e2z - r.Dir.Z * e2y;
            float py = r.Dir.Z * e2x - r.Dir.X * e2z;
            float pz = r.Dir.X * e2y - r.Dir.Y * e2x;

            float detS = e1x * px + e1y * py + e1z * pz;
            if (MathF.Abs(detS) < EpsDet)
            {
                return false;
            }
            float invDetS = 1.0f / detS;

            float sx = r.Origin.X - A.X;
            float sy = r.Origin.Y - A.Y;
            float sz = r.Origin.Z - A.Z;

            float uS = (sx * px + sy * py + sz * pz) * invDetS;
            if (uS < 0.0f || uS > 1.0f)
            {
                return false;
            }

            float qx = sy * e1z - sz * e1y;
            float qy = sz * e1x - sx * e1z;
            float qz = sx * e1y - sy * e1x;

            float vS = (r.Dir.X * qx + r.Dir.Y * qy + r.Dir.Z * qz) * invDetS;
            if (vS < 0.0f || (uS + vS) > 1.0f)
            {
                return false;
            }

            float tS = (e2x * qx + e2y * qy + e2z * qz) * invDetS;
            if (tS < tMin || tS > tMax)
            {
                return false;
            }

            rec.T = tS;
            rec.P = new Vec3(r.Origin.X + tS * r.Dir.X, r.Origin.Y + tS * r.Dir.Y, r.Origin.Z + tS * r.Dir.Z);
            float nd = nx * r.Dir.X + ny * r.Dir.Y + nz * r.Dir.Z;
            rec.N = nd < 0.0f ? new Vec3(nx, ny, nz) : new Vec3(-nx, -ny, -nz);
            rec.Mat = Mat;
            rec.U = uS;
            rec.V = vS;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool TryGetBounds(out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ, out float cx, out float cy, out float cz)
        {
            minX = bMinX; minY = bMinY; minZ = bMinZ;
            maxX = bMaxX; maxY = bMaxY; maxZ = bMaxZ;
            cx = bCx; cy = bCy; cz = bCz;
            return true;
        }
    }
}
