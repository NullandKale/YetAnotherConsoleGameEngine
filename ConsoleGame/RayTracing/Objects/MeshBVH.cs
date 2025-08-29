// File: MeshBVH.cs
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Linq;

namespace ConsoleGame.RayTracing.Objects
{
    public sealed class MeshBVH : Hittable
    {
        public static int counter = 0;
        private const int TargetLeafSize = 8;
        private const int SAH_Bins = 16;

        // Flattened node arrays (SoA)
        private float[] nodeMinX;
        private float[] nodeMinY;
        private float[] nodeMinZ;
        private float[] nodeMaxX;
        private float[] nodeMaxY;
        private float[] nodeMaxZ;
        private int[] nodeLeft;
        private int[] nodeRight;
        private int[] nodeStart;   // start index into leafTriIndex
        private int[] nodeCount;   // number of triangles in leaf (0 => internal)
        private int nodeCountUsed;
        private int rootIndex;

        // Triangle data (SoA)
        private float[] ax, ay, az;         // vertex A
        private float[] e1x, e1y, e1z;      // edge1 (B - A)
        private float[] e2x, e2y, e2z;      // edge2 (C - A)
        private float[] nx, ny, nz;         // unit geometric normal
        private Material[] triMat;          // material per triangle

        // Packed leaf triangle indices (triangles referenced by leaves live in this array)
        private int[] leafTriIndex;

        public MeshBVH(IEnumerable<Triangle> objects)
        {
            counter = counter + objects.Count();
            List<Triangle> tris = new List<Triangle>();
            List<Item> items = new List<Item>();

            foreach (Triangle t in objects)
            {
                Item it;
                if (!TryComputeBounds(t, out it.MinX, out it.MinY, out it.MinZ, out it.MaxX, out it.MaxY, out it.MaxZ))
                {
                    throw new Exception("Unbounded triangle in MeshBVH.");
                }
                it.Index = tris.Count;
                it.Cx = 0.5f * (it.MinX + it.MaxX);
                it.Cy = 0.5f * (it.MinY + it.MaxY);
                it.Cz = 0.5f * (it.MinZ + it.MaxZ);
                tris.Add(t);
                items.Add(it);
            }

            int n = tris.Count;
            if (n == 0)
            {
                nodeMinX = nodeMinY = nodeMinZ = nodeMaxX = nodeMaxY = nodeMaxZ = Array.Empty<float>();
                nodeLeft = nodeRight = nodeStart = nodeCount = Array.Empty<int>();
                ax = ay = az = e1x = e1y = e1z = e2x = e2y = e2z = nx = ny = nz = Array.Empty<float>();
                triMat = Array.Empty<Material>();
                leafTriIndex = Array.Empty<int>();
                rootIndex = -1;
                nodeCountUsed = 0;
                return;
            }

            // Initialize triangle SoA arrays
            ax = new float[n]; ay = new float[n]; az = new float[n];
            e1x = new float[n]; e1y = new float[n]; e1z = new float[n];
            e2x = new float[n]; e2y = new float[n]; e2z = new float[n];
            nx = new float[n]; ny = new float[n]; nz = new float[n];
            triMat = new Material[n];

            for (int i = 0; i < n; i++)
            {
                Triangle t = tris[i];
                ax[i] = t.A.X; ay[i] = t.A.Y; az[i] = t.A.Z;

                float lx = t.B.X - t.A.X; float ly = t.B.Y - t.A.Y; float lz = t.B.Z - t.A.Z;
                float mx = t.C.X - t.A.X; float my = t.C.Y - t.A.Y; float mz = t.C.Z - t.A.Z;

                e1x[i] = lx; e1y[i] = ly; e1z[i] = lz;
                e2x[i] = mx; e2y[i] = my; e2z[i] = mz;

                float nnx = ly * mz - lz * my;
                float nny = lz * mx - lx * mz;
                float nnz = lx * my - ly * mx;
                float invLen = 1.0f / MathF.Max(1e-20f, MathF.Sqrt(nnx * nnx + nny * nny + nnz * nnz));
                nx[i] = nnx * invLen; ny[i] = nny * invLen; nz[i] = nnz * invLen;

                triMat[i] = t.Mat;
            }

            // Build BVH into a temporary AoS node list, while accumulating leaf indices; flatten at the end.
            List<NodeTmp> nodes = new List<NodeTmp>(2 * n);
            List<int> leafIndices = new List<int>(n);

            Item[] arr = items.ToArray();
            rootIndex = BuildRecursive(nodes, leafIndices, arr, 0, arr.Length);

            nodeCountUsed = nodes.Count;
            nodeMinX = new float[nodeCountUsed];
            nodeMinY = new float[nodeCountUsed];
            nodeMinZ = new float[nodeCountUsed];
            nodeMaxX = new float[nodeCountUsed];
            nodeMaxY = new float[nodeCountUsed];
            nodeMaxZ = new float[nodeCountUsed];
            nodeLeft = new int[nodeCountUsed];
            nodeRight = new int[nodeCountUsed];
            nodeStart = new int[nodeCountUsed];
            nodeCount = new int[nodeCountUsed];

            for (int i = 0; i < nodeCountUsed; i++)
            {
                NodeTmp nd = nodes[i];
                nodeMinX[i] = nd.MinX; nodeMinY[i] = nd.MinY; nodeMinZ[i] = nd.MinZ;
                nodeMaxX[i] = nd.MaxX; nodeMaxY[i] = nd.MaxY; nodeMaxZ[i] = nd.MaxZ;
                nodeLeft[i] = nd.Left; nodeRight[i] = nd.Right; nodeStart[i] = nd.Start; nodeCount[i] = nd.Count;
            }

            leafTriIndex = leafIndices.ToArray();
        }

        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV)
        {
            if (rootIndex < 0)
            {
                return false;
            }

            float invDx = 1.0f / r.Dir.X;
            float invDy = 1.0f / r.Dir.Y;
            float invDz = 1.0f / r.Dir.Z;
            int signX = invDx < 0.0f ? 1 : 0;
            int signY = invDy < 0.0f ? 1 : 0;
            int signZ = invDz < 0.0f ? 1 : 0;

            bool hitAnything = false;
            float closest = tMax;
            HitRecord best = default;

            Span<int> stack = stackalloc int[64];
            int sp = 0;
            stack[sp++] = rootIndex;

            while (sp > 0)
            {
                int ni = stack[--sp];

                float nMinX = nodeMinX[ni], nMinY = nodeMinY[ni], nMinZ = nodeMinZ[ni];
                float nMaxX = nodeMaxX[ni], nMaxY = nodeMaxY[ni], nMaxZ = nodeMaxZ[ni];

                float tNear, tFar;
                if (!BoxHitFast(nMinX, nMinY, nMinZ, nMaxX, nMaxY, nMaxZ, r, tMin, closest, invDx, invDy, invDz, signX, signY, signZ, out tNear, out tFar))
                {
                    continue;
                }

                int cnt = nodeCount[ni];
                if (cnt > 0)
                {
                    int start = nodeStart[ni];
                    for (int i = 0; i < cnt; i++)
                    {
                        int tri = leafTriIndex[start + i];
                        float tHit, u, v;
                        if (TriHit(tri, r, tMin, closest, out tHit, out u, out v))
                        {
                            closest = tHit;
                            hitAnything = true;
                            best.T = tHit;
                            best.P = new Vec3(r.Origin.X + tHit * r.Dir.X, r.Origin.Y + tHit * r.Dir.Y, r.Origin.Z + tHit * r.Dir.Z);
                            float ndotd = nx[tri] * r.Dir.X + ny[tri] * r.Dir.Y + nz[tri] * r.Dir.Z;
                            best.N = ndotd < 0.0f ? new Vec3(nx[tri], ny[tri], nz[tri]) : new Vec3(-nx[tri], -ny[tri], -nz[tri]);
                            best.Mat = triMat[tri];
                            best.U = u;
                            best.V = v;
                        }
                    }
                }
                else
                {
                    int l = nodeLeft[ni], rr = nodeRight[ni];

                    float lNear = 0.0f, lFar = 0.0f;
                    float rNear = 0.0f, rFar = 0.0f;

                    bool hitL = false, hitR = false;

                    if (l >= 0)
                    {
                        hitL = BoxHitFast(nodeMinX[l], nodeMinY[l], nodeMinZ[l], nodeMaxX[l], nodeMaxY[l], nodeMaxZ[l], r, tMin, closest, invDx, invDy, invDz, signX, signY, signZ, out lNear, out lFar);
                    }
                    if (rr >= 0)
                    {
                        hitR = BoxHitFast(nodeMinX[rr], nodeMinY[rr], nodeMinZ[rr], nodeMaxX[rr], nodeMaxY[rr], nodeMaxZ[rr], r, tMin, closest, invDx, invDy, invDz, signX, signY, signZ, out rNear, out rFar);
                    }

                    if (hitL & hitR)
                    {
                        if (lNear < rNear)
                        {
                            stack[sp++] = rr;
                            stack[sp++] = l;
                        }
                        else
                        {
                            stack[sp++] = l;
                            stack[sp++] = rr;
                        }
                    }
                    else if (hitL)
                    {
                        stack[sp++] = l;
                    }
                    else if (hitR)
                    {
                        stack[sp++] = rr;
                    }
                }
            }

            if (hitAnything)
            {
                rec = best;
            }
            return hitAnything;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TriHit(int i, in Ray r, float tMin, float tMax, out float t, out float u, out float v)
        {
            // Möller–Trumbore, optimized: hoist loads, defer division, sign-normalize det, and prune against |det|-scaled ranges.
            float dirx = r.Dir.X, diry = r.Dir.Y, dirz = r.Dir.Z;

            float e1x_i = e1x[i], e1y_i = e1y[i], e1z_i = e1z[i];
            float e2x_i = e2x[i], e2y_i = e2y[i], e2z_i = e2z[i];
            float ax_i = ax[i], ay_i = ay[i], az_i = az[i];

            float px = diry * e2z_i - dirz * e2y_i;
            float py = dirz * e2x_i - dirx * e2z_i;
            float pz = dirx * e2y_i - diry * e2x_i;

            float det = e1x_i * px + e1y_i * py + e1z_i * pz;
            const float Eps = 1e-8f;
            if (det > -Eps && det < Eps)
            {
                t = 0f; u = 0f; v = 0f;
                return false;
            }

            float sx = r.Origin.X - ax_i;
            float sy = r.Origin.Y - ay_i;
            float sz = r.Origin.Z - az_i;

            float uNum = sx * px + sy * py + sz * pz;

            float sgn = det > 0.0f ? 1.0f : -1.0f;
            float detAbs = det * sgn;
            float uNumS = uNum * sgn;
            if (uNumS < 0.0f || uNumS > detAbs)
            {
                t = 0f; u = 0f; v = 0f;
                return false;
            }

            float qx = sy * e1z_i - sz * e1y_i;
            float qy = sz * e1x_i - sx * e1z_i;
            float qz = sx * e1y_i - sy * e1x_i;

            float vNum = dirx * qx + diry * qy + dirz * qz;
            float vNumS = vNum * sgn;
            float uvSumS = uNumS + vNumS;
            if (vNumS < 0.0f || uvSumS > detAbs)
            {
                t = 0f; u = 0f; v = 0f;
                return false;
            }

            float tNum = e2x_i * qx + e2y_i * qy + e2z_i * qz;
            float tNumS = tNum * sgn;

            float tMinScaled = tMin * detAbs;
            float tMaxScaled = tMax * detAbs;
            if (tNumS < tMinScaled || tNumS > tMaxScaled)
            {
                t = 0f; u = 0f; v = 0f;
                return false;
            }

            float invDet = 1.0f / det;
            t = tNum * invDet;
            u = uNum * invDet;
            v = vNum * invDet;
            return true;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool BoxHitFast(float minX, float minY, float minZ, float maxX, float maxY, float maxZ, in Ray r, float tMin, float tMax, float invDx, float invDy, float invDz, int signX, int signY, int signZ, out float tNear, out float tFar)
        {
            float ox = r.Origin.X, oy = r.Origin.Y, oz = r.Origin.Z;

            float txEnter = ((signX == 0 ? minX : maxX) - ox) * invDx;
            float txExit = ((signX == 0 ? maxX : minX) - ox) * invDx;
            if (txEnter > tMin) tMin = txEnter;
            if (txExit < tMax) tMax = txExit;
            if (tMax < tMin) { tNear = tMin; tFar = tMax; return false; }

            float tyEnter = ((signY == 0 ? minY : maxY) - oy) * invDy;
            float tyExit = ((signY == 0 ? maxY : minY) - oy) * invDy;
            if (tyEnter > tMin) tMin = tyEnter;
            if (tyExit < tMax) tMax = tyExit;
            if (tMax < tMin) { tNear = tMin; tFar = tMax; return false; }

            float tzEnter = ((signZ == 0 ? minZ : maxZ) - oz) * invDz;
            float tzExit = ((signZ == 0 ? maxZ : minZ) - oz) * invDz;
            if (tzEnter > tMin) tMin = tzEnter;
            if (tzExit < tMax) tMax = tzExit;

            tNear = tMin;
            tFar = tMax;
            return tMax >= tMin;
        }


        private struct NodeTmp
        {
            public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
            public int Left, Right;
            public int Start, Count;
        }

        private struct Item
        {
            public int Index;
            public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
            public float Cx, Cy, Cz;
        }

        private static bool TryComputeBounds(Triangle tr, out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ)
        {
            const float Eps = 1e-4f;
            float ax = tr.A.X, ay = tr.A.Y, az = tr.A.Z;
            float bx = tr.B.X, by = tr.B.Y, bz = tr.B.Z;
            float cx = tr.C.X, cy = tr.C.Y, cz = tr.C.Z;

            minX = MathF.Min(ax, MathF.Min(bx, cx)) - Eps;
            minY = MathF.Min(ay, MathF.Min(by, cy)) - Eps;
            minZ = MathF.Min(az, MathF.Min(bz, cz)) - Eps;
            maxX = MathF.Max(ax, MathF.Max(bx, cx)) + Eps;
            maxY = MathF.Max(ay, MathF.Max(by, cy)) + Eps;
            maxZ = MathF.Max(az, MathF.Max(bz, cz)) + Eps;
            return true;
        }

        private static void Surround(ref float minX, ref float minY, ref float minZ, ref float maxX, ref float maxY, ref float maxZ, float oMinX, float oMinY, float oMinZ, float oMaxX, float oMaxY, float oMaxZ)
        {
            if (oMinX < minX) minX = oMinX; if (oMinY < minY) minY = oMinY; if (oMinZ < minZ) minZ = oMinZ;
            if (oMaxX > maxX) maxX = oMaxX; if (oMaxY > maxY) maxY = oMaxY; if (oMaxZ > maxZ) maxZ = oMaxZ;
        }

        private int BuildRecursive(List<NodeTmp> nodes, List<int> leafIndices, Item[] arr, int start, int count)
        {
            if (count <= 0)
            {
                return -1;
            }

            if (count <= TargetLeafSize)
            {
                NodeTmp leaf = new NodeTmp();
                float mnx = arr[start].MinX, mny = arr[start].MinY, mnz = arr[start].MinZ;
                float mxx = arr[start].MaxX, mxy = arr[start].MaxY, mxz = arr[start].MaxZ;
                for (int i = 1; i < count; i++)
                {
                    Surround(ref mnx, ref mny, ref mnz, ref mxx, ref mxy, ref mxz, arr[start + i].MinX, arr[start + i].MinY, arr[start + i].MinZ, arr[start + i].MaxX, arr[start + i].MaxY, arr[start + i].MaxZ);
                }
                int baseIndex = leafIndices.Count;
                for (int i = 0; i < count; i++)
                {
                    leafIndices.Add(arr[start + i].Index);
                }
                leaf.MinX = mnx; leaf.MinY = mny; leaf.MinZ = mnz; leaf.MaxX = mxx; leaf.MaxY = mxy; leaf.MaxZ = mxz;
                leaf.Left = -1; leaf.Right = -1; leaf.Start = baseIndex; leaf.Count = count;
                int idx = nodes.Count;
                nodes.Add(leaf);
                return idx;
            }

            float cminx = arr[start].Cx, cminy = arr[start].Cy, cminz = arr[start].Cz;
            float cmaxx = cminx, cmaxy = cminy, cmaxz = cminz;
            for (int i = start + 1; i < start + count; i++)
            {
                float cx = arr[i].Cx, cy = arr[i].Cy, cz = arr[i].Cz;
                if (cx < cminx) cminx = cx; if (cy < cminy) cminy = cy; if (cz < cminz) cminz = cz;
                if (cx > cmaxx) cmaxx = cx; if (cy > cmaxy) cmaxy = cy; if (cz > cmaxz) cmaxz = cz;
            }

            float extX = cmaxx - cminx, extY = cmaxy - cminy, extZ = cmaxz - cminz;
            int axis = 0;
            if (extY > extX && extY >= extZ) axis = 1; else if (extZ > extX && extZ >= extY) axis = 2;

            int splitBin = -1;
            int bestAxis = axis;
            float bestCost = float.PositiveInfinity;

            for (int ax = 0; ax < 3; ax++)
            {
                float extent = ax == 0 ? extX : ax == 1 ? extY : extZ;
                if (!(extent > 0.0f))
                {
                    continue;
                }

                float origin = ax == 0 ? cminx : ax == 1 ? cminy : cminz;
                float invExtent = 1.0f / extent;

                int[] counts = new int[SAH_Bins];
                float[] lminx = new float[SAH_Bins], lminy = new float[SAH_Bins], lminz = new float[SAH_Bins];
                float[] lmaxx = new float[SAH_Bins], lmaxy = new float[SAH_Bins], lmaxz = new float[SAH_Bins];
                float[] rminx = new float[SAH_Bins], rminy = new float[SAH_Bins], rminz = new float[SAH_Bins];
                float[] rmaxx = new float[SAH_Bins], rmaxy = new float[SAH_Bins], rmaxz = new float[SAH_Bins];
                for (int b = 0; b < SAH_Bins; b++)
                {
                    lminx[b] = lminy[b] = lminz[b] = float.PositiveInfinity;
                    lmaxx[b] = lmaxy[b] = lmaxz[b] = float.NegativeInfinity;
                    rminx[b] = rminy[b] = rminz[b] = float.PositiveInfinity;
                    rmaxx[b] = rmaxy[b] = rmaxz[b] = float.NegativeInfinity;
                    counts[b] = 0;
                }

                for (int i = start; i < start + count; i++)
                {
                    float c = ax == 0 ? arr[i].Cx : ax == 1 ? arr[i].Cy : arr[i].Cz;
                    int b = (int)((c - origin) * invExtent * (SAH_Bins - 1));
                    if (b < 0) b = 0; if (b >= SAH_Bins) b = SAH_Bins - 1;
                    counts[b]++;
                    Surround(ref lminx[b], ref lminy[b], ref lminz[b], ref lmaxx[b], ref lmaxy[b], ref lmaxz[b], arr[i].MinX, arr[i].MinY, arr[i].MinZ, arr[i].MaxX, arr[i].MaxY, arr[i].MaxZ);
                }

                int[] leftCount = new int[SAH_Bins];
                int[] rightCount = new int[SAH_Bins];
                float[] leftArea = new float[SAH_Bins];
                float[] rightArea = new float[SAH_Bins];

                float curLminx = float.PositiveInfinity, curLminy = float.PositiveInfinity, curLminz = float.PositiveInfinity;
                float curLmaxx = float.NegativeInfinity, curLmaxy = float.NegativeInfinity, curLmaxz = float.NegativeInfinity;
                int acc = 0;
                for (int b = 0; b < SAH_Bins; b++)
                {
                    if (counts[b] > 0)
                    {
                        Surround(ref curLminx, ref curLminy, ref curLminz, ref curLmaxx, ref curLmaxy, ref curLmaxz, lminx[b], lminy[b], lminz[b], lmaxx[b], lmaxy[b], lmaxz[b]);
                    }
                    acc += counts[b];
                    leftCount[b] = acc;
                    leftArea[b] = SurfaceArea(curLminx, curLminy, curLminz, curLmaxx, curLmaxy, curLmaxz);
                }

                float curRminx = float.PositiveInfinity, curRminy = float.PositiveInfinity, curRminz = float.PositiveInfinity;
                float curRmaxx = float.NegativeInfinity, curRmaxy = float.NegativeInfinity, curRmaxz = float.NegativeInfinity;
                acc = 0;
                for (int b = SAH_Bins - 1; b >= 0; b--)
                {
                    if (counts[b] > 0)
                    {
                        Surround(ref curRminx, ref curRminy, ref curRminz, ref curRmaxx, ref curRmaxy, ref curRmaxz, lminx[b], lminy[b], lminz[b], lmaxx[b], lmaxy[b], lmaxz[b]);
                    }
                    acc += counts[b];
                    rightCount[b] = acc;
                    rightArea[b] = SurfaceArea(curRminx, curRminy, curRminz, curRmaxx, curRmaxy, curRmaxz);
                }

                for (int b = 0; b < SAH_Bins - 1; b++)
                {
                    int lc = leftCount[b];
                    int rc = rightCount[b + 1];
                    if (lc == 0 || rc == 0) continue;
                    float cost = leftArea[b] * lc + rightArea[b + 1] * rc;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestAxis = ax;
                        splitBin = b;
                    }
                }
            }

            int mid;
            if (splitBin < 0)
            {
                Comparison<Item> cmp = bestAxis == 0
                    ? (Comparison<Item>)((a, b) => a.Cx.CompareTo(b.Cx))
                    : bestAxis == 1
                        ? (Comparison<Item>)((a, b) => a.Cy.CompareTo(b.Cy))
                        : (a, b) => a.Cz.CompareTo(b.Cz);
                Array.Sort(arr, start, count, Comparer<Item>.Create(cmp));
                mid = start + (count >> 1);
            }
            else
            {
                float origin = bestAxis == 0 ? cminx : bestAxis == 1 ? cminy : cminz;
                float extent = bestAxis == 0 ? extX : bestAxis == 1 ? extY : extZ;
                float invExtent = 1.0f / extent;
                int i0 = start, i1 = start + count - 1;
                while (i0 <= i1)
                {
                    float c0 = bestAxis == 0 ? arr[i0].Cx : bestAxis == 1 ? arr[i0].Cy : arr[i0].Cz;
                    int b0 = (int)((c0 - origin) * invExtent * (SAH_Bins - 1));
                    if (b0 <= splitBin)
                    {
                        i0++;
                    }
                    else
                    {
                        Item tmp = arr[i0]; arr[i0] = arr[i1]; arr[i1] = tmp; i1--;
                    }
                }
                mid = i0;
                if (mid == start || mid == start + count)
                {
                    Comparison<Item> cmp = bestAxis == 0
                        ? (Comparison<Item>)((a, b) => a.Cx.CompareTo(b.Cx))
                        : bestAxis == 1
                            ? (Comparison<Item>)((a, b) => a.Cy.CompareTo(b.Cy))
                            : (a, b) => a.Cz.CompareTo(b.Cz);
                    Array.Sort(arr, start, count, Comparer<Item>.Create(cmp));
                    mid = start + (count >> 1);
                }
            }

            int myIndex = nodes.Count;
            nodes.Add(default);

            int leftIndex = BuildRecursive(nodes, leafIndices, arr, start, mid - start);
            int rightIndex = BuildRecursive(nodes, leafIndices, arr, mid, start + count - mid);

            NodeTmp cur = new NodeTmp();
            cur.Left = leftIndex;
            cur.Right = rightIndex;

            if (leftIndex >= 0 && rightIndex >= 0)
            {
                NodeTmp L = nodes[leftIndex];
                NodeTmp R = nodes[rightIndex];
                cur.MinX = MathF.Min(L.MinX, R.MinX);
                cur.MinY = MathF.Min(L.MinY, R.MinY);
                cur.MinZ = MathF.Min(L.MinZ, R.MinZ);
                cur.MaxX = MathF.Max(L.MaxX, R.MaxX);
                cur.MaxY = MathF.Max(L.MaxY, R.MaxY);
                cur.MaxZ = MathF.Max(L.MaxZ, R.MaxZ);
            }
            else if (leftIndex >= 0)
            {
                NodeTmp L = nodes[leftIndex];
                cur.MinX = L.MinX; cur.MinY = L.MinY; cur.MinZ = L.MinZ; cur.MaxX = L.MaxX; cur.MaxY = L.MaxY; cur.MaxZ = L.MaxZ;
            }
            else
            {
                NodeTmp R = nodes[rightIndex];
                cur.MinX = R.MinX; cur.MinY = R.MinY; cur.MinZ = R.MinZ; cur.MaxX = R.MaxX; cur.MaxY = R.MaxY; cur.MaxZ = R.MaxZ;
            }

            cur.Start = 0; cur.Count = 0;
            nodes[myIndex] = cur;
            return myIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float SurfaceArea(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            float dx = maxX - minX, dy = maxY - minY, dz = maxZ - minZ;
            return 2.0f * (dx * dy + dx * dz + dy * dz);
        }

        public override bool TryGetBounds(out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ, out float cx, out float cy, out float cz)
        {
            if (rootIndex < 0)
            {
                minX = minY = minZ = maxX = maxY = maxZ = cx = cy = cz = 0.0f;
                return false;
            }
            minX = nodeMinX[rootIndex];
            minY = nodeMinY[rootIndex];
            minZ = nodeMinZ[rootIndex];
            maxX = nodeMaxX[rootIndex];
            maxY = nodeMaxY[rootIndex];
            maxZ = nodeMaxZ[rootIndex];
            cx = 0.5f * (minX + maxX);
            cy = 0.5f * (minY + maxY);
            cz = 0.5f * (minZ + maxZ);
            return true;
        }
    }
}
