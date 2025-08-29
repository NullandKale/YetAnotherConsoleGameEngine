using ConsoleGame.RayTracing.Objects;
using ConsoleGame.RayTracing;
using System.Runtime.CompilerServices;

public sealed class BVH : Hittable
{
    private const int TargetLeafSize = 4;
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
    private int[] nodeStart;   // start index into leafObjIndex
    private int[] nodeCount;   // number of objects in leaf (0 => internal)
    private int nodeCountUsed;
    private int rootIndex;

    // Packed leaf indices and pre-bound hit delegates
    private int[] leafObjIndex;
    private delegate bool HitFunc(Ray r, float tMin, float tMax, ref HitRecord rec, float screenU, float screenV);
    private HitFunc[] objectHit;

    public BVH(IEnumerable<Hittable> objects)
    {
        List<Hittable> objs = new List<Hittable>();
        List<Item> items = new List<Item>();

        foreach (Hittable h in objects)
        {
            float minX, minY, minZ, maxX, maxY, maxZ, cx, cy, cz;
            if (!h.TryGetBounds(out minX, out minY, out minZ, out maxX, out maxY, out maxZ, out cx, out cy, out cz))
            {
                throw new Exception("Unbounded Hittable");
            }

            Item it;
            it.Index = objs.Count;
            it.MinX = minX; it.MinY = minY; it.MinZ = minZ;
            it.MaxX = maxX; it.MaxY = maxY; it.MaxZ = maxZ;
            it.Cx = cx; it.Cy = cy; it.Cz = cz;

            objs.Add(h);
            items.Add(it);
        }

        int n = objs.Count;
        if (n == 0)
        {
            nodeMinX = nodeMinY = nodeMinZ = nodeMaxX = nodeMaxY = nodeMaxZ = Array.Empty<float>();
            nodeLeft = nodeRight = nodeStart = nodeCount = Array.Empty<int>();
            leafObjIndex = Array.Empty<int>();
            objectHit = Array.Empty<HitFunc>();
            rootIndex = -1;
            nodeCountUsed = 0;
            return;
        }

        objectHit = new HitFunc[n];
        for (int i = 0; i < n; i++)
        {
            objectHit[i] = objs[i].Hit;
        }

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

        leafObjIndex = leafIndices.ToArray();
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

        Span<int> stack = stackalloc int[128];
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
                    int objId = leafObjIndex[start + i];
                    HitRecord tmp = default;
                    if (objectHit[objId](r, tMin, closest, ref tmp, screenU, screenV))
                    {
                        hitAnything = true;
                        closest = tmp.T;
                        best = tmp;
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
    public static bool BoxHitFast(
        float minX, float minY, float minZ,
        float maxX, float maxY, float maxZ,
        in Ray r,
        float tMin, float tMax,
        float invDx, float invDy, float invDz,
        int signX, int signY, int signZ,
        out float tNear, out float tFar)
    {
        // Branchless slab intersection
        float ox = r.Origin.X, oy = r.Origin.Y, oz = r.Origin.Z;
        float tEnterX = (minX - ox) * invDx;
        float tExitX = (maxX - ox) * invDx;
        if (tEnterX > tExitX)
        { // swap if out of order
            float tmp = tEnterX; tEnterX = tExitX; tExitX = tmp;
        }
        // Repeat for Y:
        float tEnterY = (minY - oy) * invDy;
        float tExitY = (maxY - oy) * invDy;
        if (tEnterY > tExitY) { float tmp = tEnterY; tEnterY = tExitY; tExitY = tmp; }
        // Repeat for Z:
        float tEnterZ = (minZ - oz) * invDz;
        float tExitZ = (maxZ - oz) * invDz;
        if (tEnterZ > tExitZ) { float tmp = tEnterZ; tEnterZ = tExitZ; tExitZ = tmp; }

        // Now find overall enter and exit
        float tEnter = MathF.Max(tEnterX, MathF.Max(tEnterY, tEnterZ));
        float tExit = MathF.Min(tExitX, MathF.Min(tExitY, tExitZ));
        // Clamp to initial tMin/tMax bounds:
        if (tEnter < tMin) tEnter = tMin;
        if (tExit > tMax) tExit = tMax;
        tNear = tEnter;
        tFar = tExit;
        return tExit >= tEnter;
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
            for (int b = 0; b < SAH_Bins; b++)
            {
                lminx[b] = lminy[b] = lminz[b] = float.PositiveInfinity;
                lmaxx[b] = lmaxy[b] = lmaxz[b] = float.NegativeInfinity;
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
            float origin = bestAxis == 0 ? arr[start].Cx : bestAxis == 1 ? arr[start].Cy : arr[start].Cz;
            float extent = bestAxis == 0 ? (arr[start + count - 1].Cx - origin) : bestAxis == 1 ? (arr[start + count - 1].Cy - origin) : (arr[start + count - 1].Cz - origin);
            float invExtent = extent != 0.0f ? 1.0f / extent : 0.0f;
            int i0 = start, i1 = start + count - 1;
            while (i0 <= i1)
            {
                float c0 = bestAxis == 0 ? arr[i0].Cx : bestAxis == 1 ? arr[i0].Cy : arr[i0].Cz;
                int b0 = invExtent != 0.0f ? (int)((c0 - origin) * invExtent * (SAH_Bins - 1)) : 0;
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
        throw new NotImplementedException();
    }
}