// File: BVH.cs
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ConsoleGame.RayTracing.Objects
{
    public sealed class BVH : Hittable
    {
        private sealed class Node
        {
            public AABB Box;
            public Node Left;
            public Node Right;
            public Hittable Leaf;
            public int LeafId;

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

            public static AABB Surround(AABB a, AABB b)
            {
                Vec3 mn = new Vec3(MathF.Min(a.Min.X, b.Min.X), MathF.Min(a.Min.Y, b.Min.Y), MathF.Min(a.Min.Z, b.Min.Z));
                Vec3 mx = new Vec3(MathF.Max(a.Max.X, b.Max.X), MathF.Max(a.Max.Y, b.Max.Y), MathF.Max(a.Max.Z, b.Max.Z));
                return new AABB(mn, mx);
            }

            public bool Hit(Ray r, float tMin, float tMax)
            {
                float invDx = 1.0f / r.Dir.X;
                float t0x = (Min.X - r.Origin.X) * invDx;
                float t1x = (Max.X - r.Origin.X) * invDx;
                if (invDx < 0.0f)
                {
                    float tmp = t0x;
                    t0x = t1x;
                    t1x = tmp;
                }
                if (t0x > tMin)
                {
                    tMin = t0x;
                }
                if (t1x < tMax)
                {
                    tMax = t1x;
                }
                if (tMax <= tMin)
                {
                    return false;
                }

                float invDy = 1.0f / r.Dir.Y;
                float t0y = (Min.Y - r.Origin.Y) * invDy;
                float t1y = (Max.Y - r.Origin.Y) * invDy;
                if (invDy < 0.0f)
                {
                    float tmp = t0y;
                    t0y = t1y;
                    t1y = tmp;
                }
                if (t0y > tMin)
                {
                    tMin = t0y;
                }
                if (t1y < tMax)
                {
                    tMax = t1y;
                }
                if (tMax <= tMin)
                {
                    return false;
                }

                float invDz = 1.0f / r.Dir.Z;
                float t0z = (Min.Z - r.Origin.Z) * invDz;
                float t1z = (Max.Z - r.Origin.Z) * invDz;
                if (invDz < 0.0f)
                {
                    float tmp = t0z;
                    t0z = t1z;
                    t1z = tmp;
                }
                if (t0z > tMin)
                {
                    tMin = t0z;
                }
                if (t1z < tMax)
                {
                    tMax = t1z;
                }
                if (tMax <= tMin)
                {
                    return false;
                }

                return true;
            }
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

        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord rec)
        {
            bool hitAnything = false;
            float closest = tMax;
            HitRecord tmp = default;

            if (root != null)
            {
                if (HitNode(root, r, tMin, closest, 0, ref tmp))
                {
                    hitAnything = true;
                    closest = tmp.T;
                    rec = tmp;
                }
            }

            return hitAnything;
        }

        private static Node Build(Item[] arr, int start, int count)
        {
            if (count <= 0)
            {
                return null;
            }

            if (count == 1)
            {
                Node leaf = new Node();
                leaf.Leaf = arr[start].Obj;
                leaf.Left = null;
                leaf.Right = null;
                leaf.Box = arr[start].Box;
                leaf.LeafId = RuntimeHelpers.GetHashCode(arr[start].Obj);
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

            Array.Sort(arr, start, count, new CentroidComparer(axis));
            int mid = start + (count >> 1);

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
            node.LeafId = 0;
            return node;
        }

        private static bool HitNode(Node node, Ray r, float tMin, float tMax, int depth, ref HitRecord rec)
        {
            if (!node.Box.Hit(r, tMin, tMax))
            {
                return false;
            }

            if (node.IsLeaf())
            {
                return node.Leaf.Hit(r, tMin, tMax, ref rec);
            }

            HitRecord leftRec = default;
            HitRecord rightRec = default;
            bool hitLeft = false;
            bool hitRight = false;

            if (node.Left != null)
            {
                hitLeft = HitNode(node.Left, r, tMin, tMax, depth + 1, ref leftRec);
                if (hitLeft)
                {
                    tMax = leftRec.T;
                }
            }

            if (node.Right != null)
            {
                hitRight = HitNode(node.Right, r, tMin, tMax, depth + 1, ref rightRec);
            }

            if (hitLeft && hitRight)
            {
                if (rightRec.T < leftRec.T)
                {
                    rec = rightRec;
                }
                else
                {
                    rec = leftRec;
                }
                return true;
            }

            if (hitLeft)
            {
                rec = leftRec;
                return true;
            }

            if (hitRight)
            {
                rec = rightRec;
                return true;
            }

            return false;
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
