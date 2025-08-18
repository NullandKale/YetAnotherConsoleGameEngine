// File: BVH.cs
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace ConsoleGame.RayTracing.Objects
{
    public sealed class BVH : Hittable
    {
        private const int TargetLeafSize = 4;
        private const int SAH_Bins = 16;
        private readonly Node root;

        public BVH(IEnumerable<Hittable> objects)
        {
            List<Item> items = new List<Item>();
            foreach (Hittable h in objects)
            {
                AABB box;
                Vec3 centroid;
                bool bounded = TryComputeBounds(h, out box, out centroid);
                if (bounded)
                {
                    Item it = new Item();
                    it.Obj = h;
                    it.Box = box;
                    it.Centroid = centroid;
                    items.Add(it);
                }
                else
                {
                    throw new Exception("Unbounded Hittable");
                }
            }
            if (items.Count > 0)
            {
                Item[] arr = items.ToArray();
                root = Build(arr, 0, arr.Length);
            }
            else
            {
                root = null;
            }
        }


        private sealed class Node
        {
            public AABB Box;
            public Node Left;
            public Node Right;
            public Hittable[] Leaf;
            public int LeafCount;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool IsLeaf()
            {
                return Leaf != null;
            }
        }

        private struct Item
        {
            public Hittable Obj;
            public AABB Box;
            public Vec3 Centroid;
        }

        private struct AABB
        {
            public Vec3 Min;
            public Vec3 Max;

            public AABB(Vec3 min, Vec3 max)
            {
                Min = min;
                Max = max;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static AABB Surround(AABB a, AABB b)
            {
                Vec3 mn = new Vec3(MathF.Min(a.Min.X, b.Min.X), MathF.Min(a.Min.Y, b.Min.Y), MathF.Min(a.Min.Z, b.Min.Z));
                Vec3 mx = new Vec3(MathF.Max(a.Max.X, b.Max.X), MathF.Max(a.Max.Y, b.Max.Y), MathF.Max(a.Max.Z, b.Max.Z));
                return new AABB(mn, mx);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float SurfaceArea(in AABB b)
            {
                float dx = b.Max.X - b.Min.X;
                float dy = b.Max.Y - b.Min.Y;
                float dz = b.Max.Z - b.Min.Z;
                return 2.0f * (dx * dy + dx * dz + dy * dz);
            }

            // Williams et al. style slab test with SIMD acceleration; returns tNear/tFar for child ordering.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool HitFast(in Ray r, float tMin, float tMax, float invDx, float invDy, float invDz, int signX, int signY, int signZ, out float tNear, out float tFar)
            {
                if (Avx.IsSupported)
                {
                    Vector256<float> vMin = Vector256.Create(Min.X, Min.Y, Min.Z, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f);
                    Vector256<float> vMax = Vector256.Create(Max.X, Max.Y, Max.Z, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f);
                    Vector256<float> vO = Vector256.Create(r.Origin.X, r.Origin.Y, r.Origin.Z, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f);
                    Vector256<float> vInv = Vector256.Create(invDx, invDy, invDz, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f);
                    Vector256<float> vSign = Vector256.Create((float)signX, (float)signY, (float)signZ, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f);
                    Vector256<float> vZero = Vector256<float>.Zero;

                    Vector256<float> eqZero = Avx.CompareEqual(vSign, vZero);
                    Vector256<float> low = Avx.Or(Avx.And(eqZero, vMin), Avx.AndNot(eqZero, vMax));
                    Vector256<float> high = Avx.Or(Avx.And(eqZero, vMax), Avx.AndNot(eqZero, vMin));

                    Vector256<float> vTMin = Avx.Multiply(Avx.Subtract(low, vO), vInv);
                    Vector256<float> vTMax = Avx.Multiply(Avx.Subtract(high, vO), vInv);

                    float txmin = vTMin.GetElement(0);
                    float tymin = vTMin.GetElement(1);
                    float tzmin = vTMin.GetElement(2);

                    float txmax = vTMax.GetElement(0);
                    float tymax = vTMax.GetElement(1);
                    float tzmax = vTMax.GetElement(2);

                    float t0 = MathF.Max(txmin, MathF.Max(tymin, tzmin));
                    float t1 = MathF.Min(txmax, MathF.Min(tymax, tzmax));

                    if (t0 > tMin)
                    {
                        tMin = t0;
                    }

                    if (t1 < tMax)
                    {
                        tMax = t1;
                    }

                    tNear = tMin;
                    tFar = tMax;
                    return tMax >= tMin;
                }

                if (Sse.IsSupported)
                {
                    Vector128<float> vMin = Vector128.Create(Min.X, Min.Y, Min.Z, 0.0f);
                    Vector128<float> vMax = Vector128.Create(Max.X, Max.Y, Max.Z, 0.0f);
                    Vector128<float> vO = Vector128.Create(r.Origin.X, r.Origin.Y, r.Origin.Z, 0.0f);
                    Vector128<float> vInv = Vector128.Create(invDx, invDy, invDz, 1.0f);
                    Vector128<float> vSign = Vector128.Create((float)signX, (float)signY, (float)signZ, 0.0f);
                    Vector128<float> vZero = Vector128<float>.Zero;

                    Vector128<float> eqZero = Sse.CompareEqual(vSign, vZero);
                    Vector128<float> low = Sse.Or(Sse.And(eqZero, vMin), Sse.AndNot(eqZero, vMax));
                    Vector128<float> high = Sse.Or(Sse.And(eqZero, vMax), Sse.AndNot(eqZero, vMin));

                    Vector128<float> vTMin = Sse.Multiply(Sse.Subtract(low, vO), vInv);
                    Vector128<float> vTMax = Sse.Multiply(Sse.Subtract(high, vO), vInv);

                    float txmin = vTMin.GetElement(0);
                    float tymin = vTMin.GetElement(1);
                    float tzmin = vTMin.GetElement(2);

                    float txmax = vTMax.GetElement(0);
                    float tymax = vTMax.GetElement(1);
                    float tzmax = vTMax.GetElement(2);

                    float t0 = MathF.Max(txmin, MathF.Max(tymin, tzmin));
                    float t1 = MathF.Min(txmax, MathF.Min(tymax, tzmax));

                    if (t0 > tMin)
                    {
                        tMin = t0;
                    }

                    if (t1 < tMax)
                    {
                        tMax = t1;
                    }

                    tNear = tMin;
                    tFar = tMax;
                    return tMax >= tMin;
                }

                float ox = r.Origin.X;
                float oy = r.Origin.Y;
                float oz = r.Origin.Z;

                float txminS = ((signX == 0 ? Min.X : Max.X) - ox) * invDx;
                float txmaxS = ((signX == 0 ? Max.X : Min.X) - ox) * invDx;

                float tyminS = ((signY == 0 ? Min.Y : Max.Y) - oy) * invDy;
                float tymaxS = ((signY == 0 ? Max.Y : Min.Y) - oy) * invDy;

                float tzminS = ((signZ == 0 ? Min.Z : Max.Z) - oz) * invDz;
                float tzmaxS = ((signZ == 0 ? Max.Z : Min.Z) - oz) * invDz;

                float t0S = MathF.Max(txminS, tyminS);
                t0S = MathF.Max(t0S, tzminS);
                if (t0S > tMin)
                {
                    tMin = t0S;
                }

                float t1S = MathF.Min(txmaxS, tymaxS);
                t1S = MathF.Min(t1S, tzmaxS);
                if (t1S < tMax)
                {
                    tMax = t1S;
                }

                tNear = tMin;
                tFar = tMax;
                return tMax >= tMin;
            }
        }


        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec)
        {
            if (root == null)
            {
                return false;
            }

            float invDx = 1.0f / r.Dir.X;
            float invDy = 1.0f / r.Dir.Y;
            float invDz = 1.0f / r.Dir.Z;

            int signX = invDx < 0.0f ? 1 : 0;
            int signY = invDy < 0.0f ? 1 : 0;
            int signZ = invDz < 0.0f ? 1 : 0;

            Node[] stackArray = System.Buffers.ArrayPool<Node>.Shared.Rent(128);
            Span<Node> stack = stackArray;
            int sp = 0;
            stack[sp++] = root;

            bool hitAnything = false;
            float closest = tMax;
            HitRecord best = default;

            try
            {
                while (sp > 0)
                {
                    Node node = stack[--sp];

                    float nodeNear, nodeFar;
                    if (!node.Box.HitFast(r, tMin, closest, invDx, invDy, invDz, signX, signY, signZ, out nodeNear, out nodeFar))
                    {
                        continue;
                    }

                    if (node.IsLeaf())
                    {
                        for (int i = 0; i < node.LeafCount; i++)
                        {
                            HitRecord tmp = default;
                            if (node.Leaf[i].Hit(r, tMin, closest, ref tmp))
                            {
                                hitAnything = true;
                                closest = tmp.T;
                                best = tmp;
                            }
                        }
                        continue;
                    }

                    float lNear = 0.0f, lFar = 0.0f, rNear = 0.0f, rFar = 0.0f;
                    bool hitL = node.Left != null && node.Left.Box.HitFast(r, tMin, closest, invDx, invDy, invDz, signX, signY, signZ, out lNear, out lFar);
                    bool hitR = node.Right != null && node.Right.Box.HitFast(r, tMin, closest, invDx, invDy, invDz, signX, signY, signZ, out rNear, out rFar);

                    if (hitL & hitR)
                    {
                        if (lNear < rNear)
                        {
                            if (sp + 2 > stack.Length)
                            {
                                Node[] newArray = System.Buffers.ArrayPool<Node>.Shared.Rent(stack.Length << 1);
                                System.Array.Copy(stackArray, newArray, sp);
                                System.Buffers.ArrayPool<Node>.Shared.Return(stackArray);
                                stackArray = newArray;
                                stack = newArray;
                            }
                            stack[sp++] = node.Right;
                            stack[sp++] = node.Left;
                        }
                        else
                        {
                            if (sp + 2 > stack.Length)
                            {
                                Node[] newArray = System.Buffers.ArrayPool<Node>.Shared.Rent(stack.Length << 1);
                                System.Array.Copy(stackArray, newArray, sp);
                                System.Buffers.ArrayPool<Node>.Shared.Return(stackArray);
                                stackArray = newArray;
                                stack = newArray;
                            }
                            stack[sp++] = node.Left;
                            stack[sp++] = node.Right;
                        }
                    }
                    else if (hitL)
                    {
                        if (sp + 1 > stack.Length)
                        {
                            Node[] newArray = System.Buffers.ArrayPool<Node>.Shared.Rent(stack.Length << 1);
                            System.Array.Copy(stackArray, newArray, sp);
                            System.Buffers.ArrayPool<Node>.Shared.Return(stackArray);
                            stackArray = newArray;
                            stack = newArray;
                        }
                        stack[sp++] = node.Left;
                    }
                    else if (hitR)
                    {
                        if (sp + 1 > stack.Length)
                        {
                            Node[] newArray = System.Buffers.ArrayPool<Node>.Shared.Rent(stack.Length << 1);
                            System.Array.Copy(stackArray, newArray, sp);
                            System.Buffers.ArrayPool<Node>.Shared.Return(stackArray);
                            stackArray = newArray;
                            stack = newArray;
                        }
                        stack[sp++] = node.Right;
                    }
                }
            }
            finally
            {
                System.Buffers.ArrayPool<Node>.Shared.Return(stackArray);
            }

            if (hitAnything)
            {
                rec = best;
            }
            return hitAnything;
        }


        private static Node Build(Item[] arr, int start, int count)
        {
            if (count <= 0)
            {
                return null;
            }

            if (count <= TargetLeafSize)
            {
                Node leaf = new Node();
                Hittable[] objs = new Hittable[count];
                AABB box = arr[start].Box;
                for (int i = 0; i < count; i++)
                {
                    objs[i] = arr[start + i].Obj;
                    box = AABB.Surround(box, arr[start + i].Box);
                }
                leaf.Box = box;
                leaf.Leaf = objs;
                leaf.LeafCount = count;
                leaf.Left = null;
                leaf.Right = null;
                return leaf;
            }

            AABB nodeBox = arr[start].Box;
            Vec3 cMin = arr[start].Centroid;
            Vec3 cMax = arr[start].Centroid;
            for (int i = start + 1; i < start + count; i++)
            {
                nodeBox = AABB.Surround(nodeBox, arr[i].Box);
                cMin = new Vec3(MathF.Min(cMin.X, arr[i].Centroid.X), MathF.Min(cMin.Y, arr[i].Centroid.Y), MathF.Min(cMin.Z, arr[i].Centroid.Z));
                cMax = new Vec3(MathF.Max(cMax.X, arr[i].Centroid.X), MathF.Max(cMax.Y, arr[i].Centroid.Y), MathF.Max(cMax.Z, arr[i].Centroid.Z));
            }

            Vec3 cExt = new Vec3(cMax.X - cMin.X, cMax.Y - cMin.Y, cMax.Z - cMin.Z);
            int axis = 0;
            if (cExt.Y > cExt.X && cExt.Y >= cExt.Z)
            {
                axis = 1;
            }
            else if (cExt.Z > cExt.X && cExt.Z >= cExt.Y)
            {
                axis = 2;
            }

            int splitIndex = -1;
            float bestCost = float.PositiveInfinity;
            int bestAxis = axis;

            for (int ax = 0; ax < 3; ax++)
            {
                float extent = ax == 0 ? cExt.X : ax == 1 ? cExt.Y : cExt.Z;
                if (!(extent > 0.0f))
                {
                    continue;
                }

                int[] counts = new int[SAH_Bins];
                AABB[] bounds = new AABB[SAH_Bins];
                for (int b = 0; b < SAH_Bins; b++)
                {
                    bounds[b].Min = new Vec3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                    bounds[b].Max = new Vec3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                }

                float cmin = ax == 0 ? cMin.X : ax == 1 ? cMin.Y : cMin.Z;
                float invExtent = 1.0f / extent;
                for (int i = start; i < start + count; i++)
                {
                    float c = ax == 0 ? arr[i].Centroid.X : ax == 1 ? arr[i].Centroid.Y : arr[i].Centroid.Z;
                    int b = (int)((c - cmin) * invExtent * (SAH_Bins - 1));
                    if (b < 0) b = 0; if (b >= SAH_Bins) b = SAH_Bins - 1;
                    counts[b]++;
                    bounds[b] = AABB.Surround(bounds[b], arr[i].Box);
                }

                int[] leftCount = new int[SAH_Bins];
                int[] rightCount = new int[SAH_Bins];
                AABB[] leftBounds = new AABB[SAH_Bins];
                AABB[] rightBounds = new AABB[SAH_Bins];

                AABB acc = new AABB(new Vec3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity), new Vec3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity));
                int csum = 0;
                for (int b = 0; b < SAH_Bins; b++)
                {
                    acc = AABB.Surround(acc, bounds[b]);
                    csum += counts[b];
                    leftBounds[b] = acc;
                    leftCount[b] = csum;
                }

                acc = new AABB(new Vec3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity), new Vec3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity));
                csum = 0;
                for (int b = SAH_Bins - 1; b >= 0; b--)
                {
                    acc = AABB.Surround(acc, bounds[b]);
                    csum += counts[b];
                    rightBounds[b] = acc;
                    rightCount[b] = csum;
                }

                for (int b = 0; b < SAH_Bins - 1; b++)
                {
                    int lc = leftCount[b];
                    int rc = rightCount[b + 1];
                    if (lc == 0 || rc == 0)
                    {
                        continue;
                    }
                    float cost = lc * AABB.SurfaceArea(leftBounds[b]) + rc * AABB.SurfaceArea(rightBounds[b + 1]);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestAxis = ax;
                        splitIndex = b;
                    }
                }
            }

            int mid;
            if (splitIndex < 0)
            {
                Array.Sort(arr, start, count, new CentroidComparer(bestAxis));
                mid = start + (count >> 1);
            }
            else
            {
                float cmin = bestAxis == 0 ? cMin.X : bestAxis == 1 ? cMin.Y : cMin.Z;
                float cext = bestAxis == 0 ? cExt.X : bestAxis == 1 ? cExt.Y : cExt.Z;
                float invExtent = 1.0f / cext;
                int i0 = start;
                int i1 = start + count - 1;
                while (i0 <= i1)
                {
                    float c0 = bestAxis == 0 ? arr[i0].Centroid.X : bestAxis == 1 ? arr[i0].Centroid.Y : arr[i0].Centroid.Z;
                    int b0 = (int)((c0 - cmin) * invExtent * (SAH_Bins - 1));
                    if (b0 <= splitIndex)
                    {
                        i0++;
                    }
                    else
                    {
                        Item tmp = arr[i0];
                        arr[i0] = arr[i1];
                        arr[i1] = tmp;
                        i1--;
                    }
                }
                mid = i0;
                if (mid == start || mid == start + count)
                {
                    Array.Sort(arr, start, count, new CentroidComparer(bestAxis));
                    mid = start + (count >> 1);
                }
            }

            Node node = new Node();
            node.Left = Build(arr, start, mid - start);
            node.Right = Build(arr, mid, start + count - mid);

            if (node.Left != null && node.Right != null)
            {
                node.Box = AABB.Surround(node.Left.Box, node.Right.Box);
            }
            else if (node.Left != null)
            {
                node.Box = node.Left.Box;
            }
            else
            {
                node.Box = node.Right.Box;
            }

            node.Leaf = null;
            node.LeafCount = 0;
            return node;
        }

        private sealed class CentroidComparer : IComparer<Item>
        {
            private readonly int axis;

            public CentroidComparer(int axis)
            {
                this.axis = axis;
            }

            public int Compare(Item a, Item b)
            {
                float ca = axis == 0 ? a.Centroid.X : axis == 1 ? a.Centroid.Y : a.Centroid.Z;
                float cb = axis == 0 ? b.Centroid.X : axis == 1 ? b.Centroid.Y : b.Centroid.Z;
                if (ca < cb)
                {
                    return -1;
                }
                if (ca > cb)
                {
                    return 1;
                }
                return 0;
            }
        }

        private static bool TryComputeBounds(Hittable h, out AABB box, out Vec3 centroid)
        {
            const float Eps = 1e-4f;

            Sphere sp = h as Sphere;
            if (sp != null)
            {
                Vec3 r = new Vec3(sp.Radius, sp.Radius, sp.Radius);
                Vec3 mn = sp.Center - r;
                Vec3 mx = sp.Center + r;
                box = new AABB(mn, mx);
                centroid = (mn + mx) * 0.5f;
                return true;
            }

            Box bx = h as Box;
            if (bx != null)
            {
                box = new AABB(bx.Min, bx.Max);
                centroid = (bx.Min + bx.Max) * 0.5f;
                return true;
            }

            Mesh mesh = h as Mesh;
            if (mesh != null)
            {
                box = new AABB(mesh.BoundsMin, mesh.BoundsMax);
                centroid = (mesh.BoundsMin + mesh.BoundsMax) * 0.5f;
                return true;
            }

            XYRect rxy = h as XYRect;
            if (rxy != null)
            {
                Vec3 mn = new Vec3(rxy.X0, rxy.Y0, rxy.Z - Eps);
                Vec3 mx = new Vec3(rxy.X1, rxy.Y1, rxy.Z + Eps);
                box = new AABB(mn, mx);
                centroid = (mn + mx) * 0.5f;
                return true;
            }

            XZRect rxz = h as XZRect;
            if (rxz != null)
            {
                Vec3 mn = new Vec3(rxz.X0, rxz.Y - Eps, rxz.Z0);
                Vec3 mx = new Vec3(rxz.X1, rxz.Y + Eps, rxz.Z1);
                box = new AABB(mn, mx);
                centroid = (mn + mx) * 0.5f;
                return true;
            }

            YZRect ryz = h as YZRect;
            if (ryz != null)
            {
                Vec3 mn = new Vec3(ryz.X - Eps, ryz.Y0, ryz.Z0);
                Vec3 mx = new Vec3(ryz.X + Eps, ryz.Y1, ryz.Z1);
                box = new AABB(mn, mx);
                centroid = (mn + mx) * 0.5f;
                return true;
            }

            Disk dk = h as Disk;
            if (dk != null)
            {
                Vec3 r = new Vec3(dk.Radius, dk.Radius, dk.Radius);
                Vec3 mn = dk.Center - r;
                Vec3 mx = dk.Center + r;
                box = new AABB(mn, mx);
                centroid = (mn + mx) * 0.5f;
                return true;
            }

            Triangle tr = h as Triangle;
            if (tr != null)
            {
                float minX = MathF.Min(tr.A.X, MathF.Min(tr.B.X, tr.C.X));
                float minY = MathF.Min(tr.A.Y, MathF.Min(tr.B.Y, tr.C.Y));
                float minZ = MathF.Min(tr.A.Z, MathF.Min(tr.B.Z, tr.C.Z));
                float maxX = MathF.Max(tr.A.X, MathF.Max(tr.B.X, tr.C.X));
                float maxY = MathF.Max(tr.A.Y, MathF.Max(tr.B.Y, tr.C.Y));
                float maxZ = MathF.Max(tr.A.Z, MathF.Max(tr.B.Z, tr.C.Z));
                Vec3 mn = new Vec3(minX - Eps, minY - Eps, minZ - Eps);
                Vec3 mx = new Vec3(maxX + Eps, maxY + Eps, maxZ + Eps);
                box = new AABB(mn, mx);
                centroid = (mn + mx) * 0.5f;
                return true;
            }

            CylinderY cy = h as CylinderY;
            if (cy != null)
            {
                Vec3 mn = new Vec3(cy.Center.X - cy.Radius, cy.YMin, cy.Center.Z - cy.Radius);
                Vec3 mx = new Vec3(cy.Center.X + cy.Radius, cy.YMax, cy.Center.Z + cy.Radius);
                box = new AABB(mn, mx);
                centroid = (mn + mx) * 0.5f;
                return true;
            }

            VolumeGrid vg = h as VolumeGrid;
            if (vg != null)
            {
                box = new AABB(vg.BoundsMin, vg.BoundsMax);
                centroid = (vg.BoundsMin + vg.BoundsMax) * 0.5f;
                return true;
            }

            Plane pl = h as Plane;
            if (pl != null)
            {
                float B = 1e6f;
                box = new AABB(new Vec3(-B, -B, -B), new Vec3(B, B, B));
                centroid = Vec3.Zero;
                return true;
            }

            box = default;
            centroid = default;
            return false;
        }
    }
}
