// File: TestScenesRandom.cs
using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using ConsoleRayTracing;
using ConsoleGame.RayTracing.Objects;
using ConsoleGame.RayTracing.Objects.BoundedSolids;
using ConsoleGame.RayTracing.Objects.Surfaces;

namespace ConsoleGame.RayTracing.Scenes
{
    public static class TestScenesRandom
    {
        // ----------------------------------------------------------------------
        // PLAN (in-code documentation)
        // ----------------------------------------------------------------------
        // Radial layout with elevated variety:
        //  - A GIANT refractive dragon sits at world center (AddCenterDragon) with color-gelled uplights.
        //  - Around it, each exhibit now has a distinct “hook” (colored lighting, emissive accents, special geometry).
        //  - Each vignette is independently interesting—no two rely on the same trick.
        //
        // Structure:
        //  - Callers pick a ring angle: PlaceXOnRing(center, radius, angleDeg, rng) -> AddX vignette.
        //  - Vignettes use modest, colored point lights plus emissive surfaces for visual spice.
        //
        // Renderer considerations:
        //  - Emissive materials contribute visible glow; s.Lights provide direct light sampling.
        //  - Intensities are tuned to avoid fireflies with a huge glass centerpiece.
        //
        // Usage:
        //  - Call TestScenesRandom.Build(seed). Use seed==-1 for a fresh random each run.

        public static Scene Build(int seed = 1337)
        {
            Console.WriteLine("[Random] Starting build (radial, enhanced exhibits)...");
            Stopwatch totalSw = Stopwatch.StartNew();

            Scene s = new Scene();
            s.Ambient = new AmbientLight(new Vec3(1, 1, 1), 0.055f);
            s.BackgroundTop = new Vec3(0.05, 0.07, 0.10);
            s.BackgroundBottom = new Vec3(0.01, 0.01, 0.02);
            s.DefaultCameraPos = new Vec3(0.0, 2.4, 10.5);

            if (seed == -1)
            {
                seed = Random.Shared.Next();
            }

            Rng rng = new Rng(seed);

            Console.WriteLine("[Random] Adding global floor and backdrop...");
            Func<Vec3, Vec3, float, Material> floor = Checker(new Vec3(0.82f, 0.82f, 0.85f), new Vec3(0.12f, 0.12f, 0.12f), 0.9f);
            s.Add(new Plane(new Vec3(0.0f, 0.0f, 0.0f), new Vec3(0.0f, 1.0f, 0.0f), floor, 0.01f, 0.00f));
            s.Add(new Plane(new Vec3(0.0f, 0.0f, -200.0f), new Vec3(0.0f, 0.0f, 1.0f), Solid(new Vec3(0.02, 0.02, 0.03)), 0.0f, 0.0f));

            Console.WriteLine("[Random] Preparing base materials...");
            Material matWhite = new Material(new Vec3(0.90, 0.90, 0.92), 0.03, 0.00, Vec3.Zero);
            Material matPedestal = new Material(new Vec3(0.86, 0.86, 0.86), 0.00, 0.00, Vec3.Zero);
            Material matMirror = new Material(new Vec3(0.98, 0.98, 0.98), 0.00, 0.90, Vec3.Zero);
            Material matGold = new Material(new Vec3(1.00, 0.85, 0.57), 0.22, 0.10, Vec3.Zero);
            Material matBrass = new Material(new Vec3(0.78, 0.60, 0.20), 0.18, 0.08, Vec3.Zero);
            Material matCopper = new Material(new Vec3(0.96, 0.58, 0.25), 0.20, 0.08, Vec3.Zero);

            Material emissiveWarm = new Material(new Vec3(0.0, 0.0, 0.0), 0.0, 0.0, new Vec3(3.2, 3.0, 2.8));
            Material emissiveCool = new Material(new Vec3(0.0, 0.0, 0.0), 0.0, 0.0, new Vec3(2.9, 3.2, 3.4));
            Material emissiveWhite = new Material(new Vec3(0.0, 0.0, 0.0), 0.0, 0.0, new Vec3(3.1, 3.1, 3.1));

            Vec3 center = new Vec3(0.0f, 0.0f, -26.0f);
            float ringRadius = 22.0f;
            int ringCount = 12;

            Console.WriteLine("[Random] Adding center dragon...");
            AddCenterDragon(s, center, rng);

            Console.WriteLine("[Random] Placing ring of exhibits...");
            float startAngle = -15.0f;
            float stepDeg = 360.0f / ringCount;

            PlaceMiniCornellOnRing(s, center, ringRadius, startAngle + stepDeg * 0 + rng.Range(-5f, 5f), rng);
            PlaceGlassGardenOnRing(s, center, ringRadius, startAngle + stepDeg * 1 + rng.Range(-5f, 5f), rng);
            PlaceMirrorCorridorOnRing(s, center, ringRadius, startAngle + stepDeg * 2 + rng.Range(-5f, 5f), rng);
            PlaceVoxelGrottoOnRing(s, center, ringRadius, startAngle + stepDeg * 3 + rng.Range(-5f, 5f), rng);
            PlacePedestalPlazaOnRing(s, center, ringRadius, startAngle + stepDeg * 4 + rng.Range(-5f, 5f), rng);
            PlaceTriangleFieldOnRing(s, center, ringRadius, startAngle + stepDeg * 5 + rng.Range(-5f, 5f), rng);
            PlaceTexturedPanelOnRing(s, center, ringRadius, startAngle + stepDeg * 6 + rng.Range(-5f, 5f), rng);
            PlaceMeshShowcaseOnRing(s, center, ringRadius, startAngle + stepDeg * 7 + rng.Range(-5f, 5f), rng);
            PlaceLightStageOnRing(s, center, ringRadius, startAngle + stepDeg * 8 + rng.Range(-5f, 5f), rng);
            PlaceMetalRingOnRing(s, center, ringRadius, startAngle + stepDeg * 9 + rng.Range(-5f, 5f), rng);
            PlaceCheckerTerraceOnRing(s, center, ringRadius, startAngle + stepDeg * 10 + rng.Range(-5f, 5f), rng);
            PlaceDiffuseStacksOnRing(s, center, ringRadius, startAngle + stepDeg * 11 + rng.Range(-5f, 5f), rng);

            Console.WriteLine("[Random] Adding global keys/fills...");
            PointLight g0 = new PointLight(center + new Vec3(0.0f, 18.0f, 14.0f), new Vec3(1.0f, 0.98f, 0.95f), 330.0f);
            PointLight g1 = new PointLight(center + new Vec3(-22.0f, 14.0f, -18.0f), new Vec3(0.9f, 0.95f, 1.0f), 240.0f);
            PointLight g2 = new PointLight(center + new Vec3(22.0f, 15.0f, -6.0f), new Vec3(0.95f, 1.0f, 0.95f), 220.0f);
            s.Lights.Add(g0);
            s.Lights.Add(g1);
            s.Lights.Add(g2);
            s.AddEntity(new PulsingLightEntity(g0, 1.0f, 0.20f, 0.35f));
            s.AddEntity(new PulsingLightEntity(g1, 1.0f, 0.18f, 0.50f));
            s.AddEntity(new PulsingLightEntity(g2, 1.0f, 0.15f, 0.65f));

            Console.WriteLine("[Random] Rebuilding BVH...");
            Stopwatch bvhSw = Stopwatch.StartNew();
            s.Update(0.0f);
            bvhSw.Stop();

            totalSw.Stop();
            Console.WriteLine("[Random] BVH rebuilt in {0} ms.", bvhSw.ElapsedMilliseconds);
            Console.WriteLine("[Random] Build complete in {0} ms (radial, enhanced).", totalSw.ElapsedMilliseconds);

            return s;
        }

        // ----------------------------------------------------------------------
        // CENTER DRAGON AREA (now with color-gelled uplights & reflective plinth)
        // ----------------------------------------------------------------------

        private static void AddCenterDragon(Scene s, Vec3 center, Rng rng)
        {
            s.Add(new Disk(center + new Vec3(0.0f, 0.015f, 0.0f), new Vec3(0.0f, 1.0f, 0.0f), 6.2f, (p, n, u) => new Material(new Vec3(0.96, 0.96, 0.98), 0.0, 0.88, Vec3.Zero), 0.0f, 0.0f));

            string dragonPath = FindExistingPath(new string[] { @"assets\xyzrgb_dragon.obj", @"assets\dragon.obj" });
            if (dragonPath != null)
            {
                Material giantGlass = new Material(new Vec3(1.0, 1.0, 1.0), 0.0, 0.02, Vec3.Zero, 1.0, 1.52, new Vec3(0.95, 0.98, 1.00));
                float giantScale = 20.0f;
                TryAddMeshAutoGround(s, dragonPath, giantGlass, giantScale, center + new Vec3(0.0, -5.5, 0.0));
            }
            else
            {
                Console.WriteLine("[Random] Central dragon asset not found; skipping centerpiece.");
            }

            PointLight a = new PointLight(center + new Vec3(7.0f, 6.5f, 6.0f), new Vec3(0.98f, 0.96f, 1.06f), 260.0f);
            PointLight b = new PointLight(center + new Vec3(-8.0f, 6.0f, -6.5f), new Vec3(1.06f, 0.96f, 0.92f), 230.0f);
            PointLight c = new PointLight(center + new Vec3(0.0f, 3.2f, 0.0f), new Vec3(0.65f, 0.80f, 1.30f), 140.0f);
            PointLight d = new PointLight(center + new Vec3(0.0f, 3.2f, 2.2f), new Vec3(1.20f, 0.75f, 0.70f), 120.0f);
            s.Lights.Add(a);
            s.Lights.Add(b);
            s.Lights.Add(c);
            s.Lights.Add(d);
            s.AddEntity(new OrbitingLightEntity(a, center, 8.0f, 6.5f, 0.25f, 0.0f));
            s.AddEntity(new OrbitingLightEntity(b, center, 8.0f, 6.0f, -0.22f, 1.8f));
            s.AddEntity(new PulsingLightEntity(c, 1.0f, 0.25f, 0.9f));
            s.AddEntity(new PulsingLightEntity(d, 1.0f, 0.22f, 1.4f));
        }

        // ----------------------------------------------------------------------
        // RING PLACERS
        // ----------------------------------------------------------------------

        private static void PlaceMiniCornellOnRing(Scene s, Vec3 center, float radius, float angleDeg, Rng rng)
        {
            Vec3 a = AnchorOnRing(center, radius, angleDeg);
            AddMiniCornell(s, a, rng);
        }

        private static void PlaceGlassGardenOnRing(Scene s, Vec3 center, float radius, float angleDeg, Rng rng)
        {
            Vec3 a = AnchorOnRing(center, radius, angleDeg);
            AddGlassGarden(s, a, rng);
        }

        private static void PlaceMirrorCorridorOnRing(Scene s, Vec3 center, float radius, float angleDeg, Rng rng)
        {
            Vec3 a = AnchorOnRing(center, radius, angleDeg);
            AddMirrorCorridor(s, a, rng);
        }

        private static void PlaceVoxelGrottoOnRing(Scene s, Vec3 center, float radius, float angleDeg, Rng rng)
        {
            Vec3 a = AnchorOnRing(center, radius, angleDeg);
            AddVoxelGrotto(s, a, rng);
        }

        private static void PlacePedestalPlazaOnRing(Scene s, Vec3 center, float radius, float angleDeg, Rng rng)
        {
            Vec3 a = AnchorOnRing(center, radius, angleDeg);
            AddPedestalPlaza(s, a, rng);
        }

        private static void PlaceTriangleFieldOnRing(Scene s, Vec3 center, float radius, float angleDeg, Rng rng)
        {
            Vec3 a = AnchorOnRing(center, radius, angleDeg);
            AddTriangleField(s, a, rng);
        }

        private static void PlaceTexturedPanelOnRing(Scene s, Vec3 center, float radius, float angleDeg, Rng rng)
        {
            Vec3 a = AnchorOnRing(center, radius, angleDeg);
            AddTexturedPanel(s, a, rng);
        }

        private static void PlaceMeshShowcaseOnRing(Scene s, Vec3 center, float radius, float angleDeg, Rng rng)
        {
            Vec3 a = AnchorOnRing(center, radius, angleDeg);
            AddMeshShowcase(s, a, rng);
        }

        private static void PlaceLightStageOnRing(Scene s, Vec3 center, float radius, float angleDeg, Rng rng)
        {
            Vec3 a = AnchorOnRing(center, radius, angleDeg);
            AddLightStage(s, a, rng);
        }

        private static void PlaceMetalRingOnRing(Scene s, Vec3 center, float radius, float angleDeg, Rng rng)
        {
            Vec3 a = AnchorOnRing(center, radius, angleDeg);
            AddMetalRing(s, a, rng);
        }

        private static void PlaceCheckerTerraceOnRing(Scene s, Vec3 center, float radius, float angleDeg, Rng rng)
        {
            Vec3 a = AnchorOnRing(center, radius, angleDeg);
            AddCheckerTerrace(s, a, rng);
        }

        private static void PlaceDiffuseStacksOnRing(Scene s, Vec3 center, float radius, float angleDeg, Rng rng)
        {
            Vec3 a = AnchorOnRing(center, radius, angleDeg);
            AddDiffuseStacks(s, a, rng);
        }

        private static Vec3 AnchorOnRing(Vec3 center, float radius, float angleDeg)
        {
            float t = angleDeg * (MathF.PI / 180.0f);
            return center + new Vec3(MathF.Cos(t) * radius, 0.0f, MathF.Sin(t) * radius);
        }

        // ----------------------------------------------------------------------
        // VIGNETTES (each with a distinct hook)
        // ----------------------------------------------------------------------

        private static void AddMiniCornell(Scene s, Vec3 a, Rng rng)
        {
            float w = 5.2f;
            float h = 4.4f;
            float xL = a.X - w * 0.5f;
            float xR = a.X + w * 0.5f;
            float yB = a.Y + 0.0f;
            float yT = a.Y + h;
            float zB = a.Z - w * 0.5f;
            float zF = a.Z + w * 0.5f;

            Vec3 leftC = new Vec3(0.95, 0.20, 0.20);
            Vec3 rightC = new Vec3(0.20, 0.28, 0.95);
            Vec3 whiteC = new Vec3(0.82, 0.82, 0.82);

            Func<Vec3, Vec3, float, Material> left = Solid(leftC);
            Func<Vec3, Vec3, float, Material> right = Solid(rightC);
            Func<Vec3, Vec3, float, Material> white = Solid(whiteC);

            s.Add(new YZRect(yB, yT, zB, zF, xL, left, 0.0f, 0.0f));
            s.Add(new YZRect(yB, yT, zB, zF, xR, right, 0.0f, 0.0f));
            s.Add(new XZRect(xL, xR, zB, zF, yB, white, 0.0f, 0.0f));
            s.Add(new XZRect(xL, xR, zB, zF, yT, white, 0.0f, 0.0f));
            s.Add(new XYRect(xL, xR, yB, yT, zB, white, 0.0f, 0.0f));

            float lx0 = xL + 0.18f * w;
            float lx1 = xR - 0.18f * w;
            float lz0 = zB + 0.38f * w;
            float lz1 = zB + 0.58f * w;
            float ly = yT - 0.01f;

            Func<Vec3, Vec3, float, Material> ceilingWarm = (p, n, u) => new Material(new Vec3(0, 0, 0), 0.0, 0.0, new Vec3(3.2, 2.6, 2.3));
            Func<Vec3, Vec3, float, Material> ceilingCool = (p, n, u) => new Material(new Vec3(0, 0, 0), 0.0, 0.0, new Vec3(2.4, 2.9, 3.2));
            s.Add(new XZRect(lx0, lx1, lz0, lz1, ly, rng.Bool(0.5f) ? ceilingWarm : ceilingCool, 0.0f, 0.0f));

            Vec3 stand0 = new Vec3(a.X - 0.85f, yB, a.Z + 0.25f);
            Vec3 stand1 = new Vec3(a.X + 0.85f, yB, a.Z - 0.25f);
            Material ped = new Material(new Vec3(0.90, 0.90, 0.92), 0.00, 0.00, Vec3.Zero);

            s.Add(new CylinderY(stand0, 0.26f, 0.0f, 0.9f, true, ped));
            Sphere sp0 = new Sphere(stand0 + new Vec3(0.0f, 1.25f, 0.0f), 0.34f, new Material(new Vec3(0.98, 0.98, 0.98), 0.0, 0.85, Vec3.Zero));
            s.AddEntity(new BobbingSphereEntity(sp0, 0.06f, 1.6f, 0.0f));
            s.Add(new CylinderY(stand1, 0.26f, 0.0f, 0.9f, true, ped));
            Sphere sp1 = new Sphere(stand1 + new Vec3(0.0f, 1.18f, 0.0f), 0.30f, MakeGlass(rng));
            s.AddEntity(new BobbingSphereEntity(sp1, 0.05f, 1.2f, 1.1f));

            s.Add(new XYRect(a.X - 0.55f, a.X + 0.55f, a.Y + 1.9f, a.Y + 2.0f, a.Z + 0.2f, (p, n, u) => new Material(new Vec3(0, 0, 0), 0.0, 0.0, new Vec3(2.8, 1.0, 0.8)), 0.0f, 0.0f));
            s.Add(new XYRect(a.X - 0.55f, a.X + 0.55f, a.Y + 1.9f, a.Y + 2.0f, a.Z - 0.2f, (p, n, u) => new Material(new Vec3(0, 0, 0), 0.0, 0.0, new Vec3(0.9, 2.4, 3.0)), 0.0f, 0.0f));

            PointLight L0 = new PointLight(new Vec3(a.X - 0.8f, yT - 0.35f, a.Z + 0.6f), new Vec3(1.2f, 0.7f, 0.6f), 55.0f);
            PointLight L1 = new PointLight(new Vec3(a.X + 0.8f, yT - 0.35f, a.Z - 0.6f), new Vec3(0.7f, 0.9f, 1.2f), 55.0f);
            s.Lights.Add(L0);
            s.Lights.Add(L1);
            s.AddEntity(new PulsingLightEntity(L0, 1.0f, 0.30f, 0.0f));
            s.AddEntity(new PulsingLightEntity(L1, 1.0f, 0.30f, MathF.PI));
        }

        private static void AddGlassGarden(Scene s, Vec3 a, Rng rng)
        {
            int nx = 4;
            int nz = 3;
            float spacing = 0.95f;
            float rmin = 0.20f, rmax = 0.42f;
            for (int iz = 0; iz < nz; iz++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    float rx = (ix - (nx - 1) * 0.5f) * 1.3f + rng.Range(-0.15f, 0.15f);
                    float rz = (iz - (nz - 1) * 0.5f) * 1.3f + rng.Range(-0.15f, 0.15f);
                    float rr = rng.Range(rmin, rmax);
                    Vec3 c = a + new Vec3(rx * spacing, rr, rz * spacing);
                    if (rng.Bool(0.35f))
                    {
                        s.Add(new CylinderY(a + new Vec3(rx * spacing, 0.0f, rz * spacing), rr * 0.75f, 0.0f, rr * 2.2f, true, MakeGlass(rng)));
                    }
                    else
                    {
                        Sphere gs = new Sphere(c, rr, MakeGlass(rng));
                        s.AddEntity(new BobbingSphereEntity(gs, 0.04f, 1.2f + rng.Range(-0.2f, 0.2f), rx * 1.1f + rz * 0.7f));
                    }
                }
            }

            s.Add(new Disk(a + new Vec3(0.0f, 0.01f, 0.0f), new Vec3(0.0f, 1.0f, 0.0f), 2.2f, (p, n, u) => new Material(new Vec3(0.96, 0.96, 0.98), 0.0, 0.88, Vec3.Zero), 0.0f, 0.0f));

            PointLight l0 = new PointLight(a + new Vec3(-1.2f, 2.4f, 1.0f), new Vec3(1.15f, 0.65f, 0.55f), 80.0f);
            PointLight l1 = new PointLight(a + new Vec3(1.2f, 2.6f, -1.0f), new Vec3(0.55f, 0.95f, 1.25f), 80.0f);
            PointLight l2 = new PointLight(a + new Vec3(0.0f, 3.0f, 0.0f), new Vec3(0.95f, 0.98f, 1.05f), 60.0f);
            s.Lights.Add(l0);
            s.Lights.Add(l1);
            s.Lights.Add(l2);
            s.AddEntity(new OrbitingLightEntity(l0, a, 2.0f, 2.4f, 0.6f, 0.0f));
            s.AddEntity(new OrbitingLightEntity(l1, a, 2.0f, 2.6f, -0.6f, 0.5f));
            s.AddEntity(new PulsingLightEntity(l2, 1.0f, 0.25f, 0.8f));
        }

        private static void AddMirrorCorridor(Scene s, Vec3 a, Rng rng)
        {
            int n = 6;
            float step = 1.8f;
            for (int i = -n; i <= n; i++)
            {
                Vec3 pL = a + new Vec3(-1.6f, 0.0f, i * step);
                Vec3 pR = a + new Vec3(1.6f, 0.0f, i * step);
                s.Add(new CylinderY(pL, 0.22f, 0.0f, 2.1f, true, new Material(new Vec3(0.98, 0.98, 0.98), 0.00, 0.92, Vec3.Zero)));
                s.Add(new CylinderY(pR, 0.22f, 0.0f, 2.1f, true, new Material(new Vec3(0.98, 0.98, 0.98), 0.00, 0.92, Vec3.Zero)));
                PointLight pl = (((i & 1) == 0) ? new PointLight(a + new Vec3(0.0f, 2.9f, i * step), new Vec3(1.15f, 0.75f, 0.65f), 38.0f) : new PointLight(a + new Vec3(0.0f, 2.9f, i * step), new Vec3(0.65f, 0.95f, 1.20f), 38.0f));
                s.Lights.Add(pl);
                s.AddEntity(new PulsingLightEntity(pl, 1.0f, 0.35f, i * 0.7f));
            }
            s.Add(new XZRect(a.X - 2.2f, a.X + 2.2f, a.Z - n * step - 0.4f, a.Z + n * step + 0.4f, a.Y + 0.01f, (p, nrm, u) => new Material(new Vec3(0.96, 0.96, 0.98), 0.0, 0.85, Vec3.Zero), 0.0f, 0.0f));
        }

        private static void AddVoxelGrotto(Scene s, Vec3 a, Rng rng)
        {
            int nx = 12, ny = 6, nz = 12;
            (int matId, int metaId)[,,] cells = new (int, int)[nx, ny, nz];

            for (int x = 0; x < nx; x++)
            {
                for (int z = 0; z < nz; z++)
                {
                    cells[x, 0, z] = (1, 0);
                }
            }

            for (int i = 0; i < 20; i++)
            {
                int cx = rng.Int(1, nx - 2);
                int cz = rng.Int(1, nz - 2);
                int h = rng.Int(2, ny - 1);
                int mat = rng.Choice(new int[] { 2, 3, 4, 5 });
                for (int y = 1; y <= h; y++)
                {
                    cells[cx, y, cz] = (mat, 0);
                }
            }

            for (int x = 3; x < nx - 3; x++)
            {
                for (int z = 3; z < nz - 3; z++)
                {
                    if (((x + z) & 1) == 0) cells[x, 1, z] = (6, 0);
                }
            }

            Vec3 voxelSize = new Vec3(0.45, 0.45, 0.45);
            Func<int, int, Material> lookup = (id, meta) =>
            {
                switch (id)
                {
                    case 1: return new Material(new Vec3(0.80, 0.80, 0.82), 0.0, 0.0, Vec3.Zero);
                    case 2: return new Material(new Vec3(0.90, 0.20, 0.20), 0.10, 0.02, Vec3.Zero);
                    case 3: return new Material(new Vec3(0.20, 0.85, 0.20), 0.08, 0.02, Vec3.Zero);
                    case 4: return new Material(new Vec3(0.20, 0.30, 0.95), 0.08, 0.02, Vec3.Zero);
                    case 5: return new Material(new Vec3(0.98, 0.98, 0.98), 0.0, 0.88, Vec3.Zero);
                    case 6: return new Material(new Vec3(0.85, 0.85, 0.88), 0.0, 0.0, Vec3.Zero);
                    default: return new Material(new Vec3(0.7, 0.7, 0.7), 0.0, 0.0, Vec3.Zero);
                }
            };

            s.Add(new VolumeGrid(cells, a + new Vec3(-2.6f, 0.0f, -2.6f), voxelSize, lookup));

            for (int i = 0; i < 10; i++)
            {
                Vec3 p = a + new Vec3(rng.Range(-2.2f, 2.2f), rng.Range(0.6f, 2.2f), rng.Range(-2.2f, 2.2f));
                Vec3 e = RandSaturated(rng, 0.25f) * 2.0f;
                s.Add(new Sphere(p, 0.08f, new Material(new Vec3(0, 0, 0), 0.0, 0.0, e)));
            }

            PointLight pl = new PointLight(a + new Vec3(0.0f, 2.4f, 0.0f), new Vec3(0.85f, 1.05f, 1.10f), 85.0f);
            s.Lights.Add(pl);
            s.AddEntity(new OrbitingLightEntity(pl, a, 2.2f, 2.4f, 0.7f, 0.0f));
        }

        private static void AddPedestalPlaza(Scene s, Vec3 a, Rng rng)
        {
            int count = 5;
            float pedH = 1.1f;
            for (int i = 0; i < count; i++)
            {
                float ang = (float)(i * (Math.PI * 2.0) / count);
                Vec3 p = a + new Vec3(MathF.Cos(ang) * 1.6f, 0.0f, MathF.Sin(ang) * 1.6f);
                s.Add(new CylinderY(p, 0.30f, 0.0f, pedH, true, new Material(new Vec3(0.88, 0.88, 0.88), 0.00, 0.00, Vec3.Zero)));
                Material topMat = rng.Choice(new Material[]
                {
                    new Material(new Vec3(0.98,0.98,0.98),0.0,0.90,Vec3.Zero),
                    MakeGlass(rng),
                    new Material(new Vec3(1.00,0.85,0.57),0.22,0.10,Vec3.Zero),
                    new Material(new Vec3(0.78,0.60,0.20),0.18,0.08,Vec3.Zero),
                    new Material(new Vec3(0.20,0.85,0.20),0.08,0.02,Vec3.Zero)
                });
                Sphere orb = new Sphere(p + new Vec3(0.0f, pedH + 0.38f, 0.0f), 0.36f, topMat);
                s.AddEntity(new BobbingSphereEntity(orb, 0.07f, 1.3f, ang));
            }

            s.Add(new Disk(a + new Vec3(0.0f, 0.015f, 0.0f), new Vec3(0.0f, 1.0f, 0.0f), 2.0f, (p, n, u) => new Material(new Vec3(0.96, 0.96, 0.98), 0.0, 0.88, Vec3.Zero), 0.0f, 0.0f));

            PointLight l0 = new PointLight(a + new Vec3(0.0f, 3.0f, 0.0f), new Vec3(1.00f, 0.92f, 0.80f), 55.0f);
            PointLight l1 = new PointLight(a + new Vec3(1.6f, 2.4f, 1.2f), new Vec3(0.65f, 0.95f, 1.25f), 50.0f);
            PointLight l2 = new PointLight(a + new Vec3(-1.6f, 2.4f, -1.2f), new Vec3(1.20f, 0.70f, 0.70f), 50.0f);
            s.Lights.Add(l0);
            s.Lights.Add(l1);
            s.Lights.Add(l2);
            s.AddEntity(new PulsingLightEntity(l0, 1.0f, 0.25f, 0.0f));
            s.AddEntity(new PulsingLightEntity(l1, 1.0f, 0.20f, 0.7f));
            s.AddEntity(new PulsingLightEntity(l2, 1.0f, 0.20f, 1.9f));
        }

        private static void AddTriangleField(Scene s, Vec3 a, Rng rng)
        {
            int n = 14;
            for (int i = 0; i < n; i++)
            {
                Vec3 p0 = a + new Vec3(rng.Range(-2.0f, 2.0f), rng.Range(0.0f, 0.8f), rng.Range(-2.0f, 2.0f));
                Vec3 p1 = a + new Vec3(rng.Range(-2.0f, 2.0f), rng.Range(0.0f, 1.0f), rng.Range(-2.0f, 2.0f));
                Vec3 p2 = a + new Vec3(rng.Range(-2.0f, 2.0f), rng.Range(0.0f, 1.2f), rng.Range(-2.0f, 2.0f));
                Material m = new Material(RandSaturated(rng, 0.15f), rng.Range(0.06f, 0.12f), rng.Range(0.02f, 0.08f), Vec3.Zero);
                s.Add(new Triangle(p0, p1, p2, m));
            }

            s.Add(new Disk(a + new Vec3(0.0f, 0.012f, 0.0f), new Vec3(0.0f, 1.0f, 0.0f), 2.2f, (p, n, u) => new Material(new Vec3(0.90, 0.90, 0.94), 0.0, 0.82, Vec3.Zero), 0.0f, 0.0f));

            PointLight l0 = new PointLight(a + new Vec3(0.0f, 2.6f, -1.2f), new Vec3(1.15f, 0.80f, 0.75f), 60.0f);
            PointLight l1 = new PointLight(a + new Vec3(0.0f, 2.6f, 1.2f), new Vec3(0.70f, 0.95f, 1.25f), 60.0f);
            s.Lights.Add(l0);
            s.Lights.Add(l1);
            s.AddEntity(new OrbitingLightEntity(l0, a, 2.4f, 2.6f, 0.5f, 0.0f));
            s.AddEntity(new OrbitingLightEntity(l1, a, 2.4f, 2.6f, -0.5f, 0.9f));
        }

        private static void AddMeshShowcase(Scene s, Vec3 a, Rng rng)
        {
            TryAddMeshAutoGround(s, @"assets\cow.obj", new Material(new Vec3(1.00, 0.85, 0.57), 0.22, 0.10, Vec3.Zero), 1.0f, a + new Vec3(-2.4f, 0.0f, 0.2f));
            TryAddMeshAutoGround(s, @"assets\stanford-bunny.obj", new Material(new Vec3(0.20, 0.85, 0.20), 0.08, 0.02, Vec3.Zero), 1.0f, a + new Vec3(0.0f, 0.0f, 0.0f));
            TryAddMeshAutoGround(s, @"assets\teapot.obj", new Material(new Vec3(0.95, 0.15, 0.15), 0.10, 0.02, Vec3.Zero), 1.0f, a + new Vec3(2.4f, 0.0f, -0.2f));
            TryAddMeshAutoGround(s, @"assets\xyzrgb_dragon.obj", new Material(new Vec3(0.98, 0.98, 0.98), 0.0, 0.90, Vec3.Zero), 1.0f, a + new Vec3(4.8f, 0.0f, -0.6f));

            s.Add(new Disk(a + new Vec3(0.0f, 0.012f, 0.0f), new Vec3(0.0f, 1.0f, 0.0f), 3.0f, (p, n, u) => new Material(new Vec3(0.90, 0.90, 0.94), 0.0, 0.82, Vec3.Zero), 0.0f, 0.0f));

            PointLight l0 = new PointLight(a + new Vec3(-1.6f, 2.4f, 1.2f), new Vec3(1.25f, 0.65f, 0.60f), 60.0f);
            PointLight l1 = new PointLight(a + new Vec3(1.6f, 2.4f, -1.2f), new Vec3(0.60f, 0.95f, 1.25f), 60.0f);
            PointLight l2 = new PointLight(a + new Vec3(0.0f, 3.0f, 0.0f), new Vec3(0.95f, 1.00f, 0.98f), 70.0f);
            s.Lights.Add(l0);
            s.Lights.Add(l1);
            s.Lights.Add(l2);
            s.AddEntity(new OrbitingLightEntity(l2, a, 2.6f, 3.0f, 0.5f, 0.0f));
            s.AddEntity(new PulsingLightEntity(l0, 1.0f, 0.25f, 0.0f));
            s.AddEntity(new PulsingLightEntity(l1, 1.0f, 0.25f, 1.0f));
        }

        private static void AddLightStage(Scene s, Vec3 a, Rng rng)
        {
            s.Add(new Disk(a + new Vec3(0.0f, 0.02f, 0.0f), new Vec3(0.0f, 1.0f, 0.0f), 1.8f, (p, n, u) => new Material(new Vec3(0.85, 0.85, 0.90), 0.0, 0.0, Vec3.Zero), 0.0f, 0.0f));
            s.Add(new Disk(a + new Vec3(0.0f, 2.4f, 0.0f), new Vec3(0.0f, -1.0f, 0.0f), 0.7f, (p, n, u) => new Material(new Vec3(0, 0, 0), 0.0, 0.0, new Vec3(3.0, 3.0, 3.2)), 0.0f, 0.0f));

            Sphere focus = new Sphere(a + new Vec3(0.0f, 0.6f, 0.0f), 0.45f, MakeGlass(rng));
            s.AddEntity(new BobbingSphereEntity(focus, 0.10f, 1.8f, 0.0f));

            s.Add(new Sphere(a + new Vec3(0.9f, 0.12f, 0.0f), 0.10f, new Material(new Vec3(0, 0, 0), 0.0, 0.0, new Vec3(2.8, 0.9, 0.7))));
            s.Add(new Sphere(a + new Vec3(-0.9f, 0.12f, 0.0f), 0.10f, new Material(new Vec3(0, 0, 0), 0.0, 0.0, new Vec3(0.7, 2.6, 3.0))));
            s.Add(new Sphere(a + new Vec3(0.0f, 0.12f, 0.9f), 0.10f, new Material(new Vec3(0, 0, 0), 0.0, 0.0, new Vec3(2.6, 2.6, 0.9))));

            PointLight a0 = new PointLight(a + new Vec3(1.2f, 2.0f, 0.8f), new Vec3(1.0f, 0.95f, 0.9f), 55.0f);
            PointLight a1 = new PointLight(a + new Vec3(-1.2f, 2.0f, -0.8f), new Vec3(0.9f, 0.95f, 1.0f), 55.0f);
            s.Lights.Add(a0);
            s.Lights.Add(a1);
            s.AddEntity(new OrbitingLightEntity(a0, a, 1.6f, 2.0f, 0.9f, 0.0f));
            s.AddEntity(new OrbitingLightEntity(a1, a, 1.6f, 2.0f, -0.9f, 0.6f));
        }

        private static void AddMetalRing(Scene s, Vec3 a, Rng rng)
        {
            int count = 10;
            float R = 1.9f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)(i * (Math.PI * 2.0) / count) + rng.Range(-0.02f, 0.02f);
                Vec3 c = a + new Vec3(MathF.Cos(t) * R, 0.5f, MathF.Sin(t) * R);
                Material m = rng.Choice(new Material[]
                {
                    new Material(new Vec3(1.00,0.85,0.57),0.22,0.10,Vec3.Zero),
                    new Material(new Vec3(0.78,0.60,0.20),0.18,0.08,Vec3.Zero),
                    new Material(new Vec3(0.96,0.58,0.25),0.20,0.08,Vec3.Zero),
                    new Material(new Vec3(0.98,0.98,0.98),0.00,0.90,Vec3.Zero)
                });
                s.Add(new Sphere(c, 0.32f, m));
            }

            Sphere centerGlass = new Sphere(a + new Vec3(0.0f, 0.62f, 0.0f), 0.38f, MakeGlass(rng));
            s.AddEntity(new BobbingSphereEntity(centerGlass, 0.08f, 1.0f, 0.3f));
            s.Add(new Sphere(a + new Vec3(0.0f, 0.62f, 0.0f), 0.12f, new Material(new Vec3(0, 0, 0), 0.0, 0.0, new Vec3(2.6, 1.0, 0.8))));

            PointLight l0 = new PointLight(a + new Vec3(0.0f, 2.4f, 0.0f), new Vec3(1.0f, 0.98f, 0.95f), 70.0f);
            PointLight l1 = new PointLight(a + new Vec3(1.6f, 1.8f, 0.0f), new Vec3(0.65f, 0.95f, 1.25f), 55.0f);
            PointLight l2 = new PointLight(a + new Vec3(-1.6f, 1.8f, 0.0f), new Vec3(1.20f, 0.75f, 0.70f), 55.0f);
            s.Lights.Add(l0);
            s.Lights.Add(l1);
            s.Lights.Add(l2);
            s.AddEntity(new PulsingLightEntity(l0, 1.0f, 0.20f, 0.0f));
            s.AddEntity(new PulsingLightEntity(l1, 1.0f, 0.18f, 0.6f));
            s.AddEntity(new PulsingLightEntity(l2, 1.0f, 0.18f, 1.2f));
        }

        private static void AddCheckerTerrace(Scene s, Vec3 a, Rng rng)
        {
            float w = 3.2f;
            float d = 0.9f;
            Func<Vec3, Vec3, float, Material> white = Solid(new Vec3(0.88, 0.88, 0.90));
            for (int i = 0; i < 4; i++)
            {
                float y = a.Y + i * 0.3f;
                s.Add(new XZRect(a.X - w, a.X + w, a.Z - d * (i + 1), a.Z + d * (i + 1), y, white, 0.0f, 0.0f));
                float z0 = a.Z - d * (i + 1);
                float z1 = a.Z + d * (i + 1);
                s.Add(new XZRect(a.X - w, a.X + w, z0, z0 + 0.08f, y + 0.001f, (p, n, u) => new Material(new Vec3(0, 0, 0), 0.0, 0.0, new Vec3(2.8, 0.9, 0.7)), 0.0f, 0.0f));
                s.Add(new XZRect(a.X - w, a.X + w, z1 - 0.08f, z1, y + 0.001f, (p, n, u) => new Material(new Vec3(0, 0, 0), 0.0, 0.0, new Vec3(0.7, 2.7, 3.2)), 0.0f, 0.0f));
            }
            Sphere top = new Sphere(a + new Vec3(0.0f, 0.3f * 4 + 0.35f, 0.0f), 0.35f, rng.Bool(0.5f) ? new Material(new Vec3(0.98, 0.98, 0.98), 0.00, 0.92, Vec3.Zero) : MakeGlass(rng));
            s.AddEntity(new BobbingSphereEntity(top, 0.07f, 1.1f, 0.0f));

            PointLight L = new PointLight(a + new Vec3(0.0f, 2.0f, 0.0f), new Vec3(0.95f, 1.0f, 0.95f), 55.0f);
            s.Lights.Add(L);
            s.AddEntity(new PulsingLightEntity(L, 1.0f, 0.22f, 0.4f));
        }

        private static void AddDiffuseStacks(Scene s, Vec3 a, Rng rng)
        {
            int stacks = 3;
            for (int i = 0; i < stacks; i++)
            {
                Vec3 p = a + new Vec3((i - 1) * 1.4f, 0.0f, (i - 1) * 0.3f);
                float h = rng.Range(0.9f, 1.6f);
                float r = rng.Range(0.25f, 0.35f);
                Material body = new Material(RandSaturated(rng, 0.10f), rng.Range(0.06f, 0.12f), rng.Range(0.02f, 0.06f), Vec3.Zero);
                s.Add(new CylinderY(p, r, 0.0f, h, true, body));
                s.Add(new Sphere(p + new Vec3(0.0f, h + r * 0.8f, 0.0f), r * 0.8f, body));
                s.Add(new XYRect(p.X - 0.25f, p.X + 0.25f, 1.65f, 1.75f, p.Z - 0.30f, (pos, nrm, u) => new Material(new Vec3(0, 0, 0), 0.0, 0.0, new Vec3(0.9, 2.5, 3.0)), 0.0f, 0.0f));
            }
            PointLight fill = new PointLight(a + new Vec3(0.0f, 2.2f, 0.0f), new Vec3(1.00f, 0.98f, 0.95f), 60.0f);
            s.Lights.Add(fill);
            s.AddEntity(new PulsingLightEntity(fill, 1.0f, 0.20f, 0.0f));
        }

        // ----------------------------------------------------------------------
        // HELPERS
        // ----------------------------------------------------------------------

        private static Material MakeGlass(Rng rng)
        {
            double ior = rng.Range(1.30f, 1.70f);
            Vec3 tint = new Vec3(
                rng.Range(0.85f, 1.00f),
                rng.Range(0.85f, 1.00f),
                rng.Range(0.85f, 1.00f)
            );
            return new Material(new Vec3(1.0, 1.0, 1.0), 0.0, 0.02, Vec3.Zero, 1.0, ior, tint);
        }

        private static void TryAddMeshAutoGround(Scene s, string objPath, Material mat, float scale, Vec3 targetPos)
        {
            if (!File.Exists(objPath))
            {
                Console.WriteLine("[Random] Mesh missing, skipped: {0}", objPath);
                return;
            }
            float yTranslate = targetPos.Y + 0.5f * MathF.Max(0.1f, scale) + 0.01f;
            Vec3 translate = new Vec3(targetPos.X, yTranslate, targetPos.Z);
            s.Add(Mesh.FromObj(objPath, mat, scale: scale, translate: translate));
            Console.WriteLine("[Random] Mesh added: {0} (scale={1})", objPath, scale);
        }

        private static string FindExistingPath(string[] candidates)
        {
            if (candidates == null || candidates.Length == 0) return null;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i])) return candidates[i];
            }
            return null;
        }

        private static Vec3 RandSaturated(Rng rng, float desat = 0.0f)
        {
            float h = rng.Range(0.0f, 1.0f);
            float s = 0.75f - desat;
            float v = 0.95f;
            return HsvToRgb(h, s, v);
        }

        private static Vec3 HsvToRgb(float h, float s, float v)
        {
            float hh = (h % 1.0f) * 6.0f;
            int i = (int)MathF.Floor(hh);
            float f = hh - i;
            float p = v * (1.0f - s);
            float q = v * (1.0f - s * f);
            float t = v * (1.0f - s * (1.0f - f));
            switch (i)
            {
                case 0: return new Vec3(v, t, p);
                case 1: return new Vec3(q, v, p);
                case 2: return new Vec3(p, v, t);
                case 3: return new Vec3(p, q, v);
                case 4: return new Vec3(t, p, v);
                default: return new Vec3(v, p, q);
            }
        }

        private static void Shuffle<T>(IList<T> list, Rng rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Int(0, i);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        private static Func<Vec3, Vec3, float, Material> Solid(Vec3 albedo)
        {
            return (pos, n, u) => new Material(albedo, 0.0, 0.0, Vec3.Zero);
        }

        private static Func<Vec3, Vec3, float, Material> Checker(Vec3 a, Vec3 b, float scale)
        {
            return (pos, n, u) =>
            {
                int cx = (int)MathF.Floor((float)(pos.X / scale));
                int cz = (int)MathF.Floor((float)(pos.Z / scale));
                bool check = (((cx + cz) & 1) == 0);
                Vec3 albedo = check ? a : b;
                return new Material(albedo, 0.0, 0.0, Vec3.Zero);
            };
        }

        private sealed class Rng
        {
            private readonly Random r;
            public Rng(int seed) { r = new Random(seed); }
            public float Range(float a, float b) { return a + (float)r.NextDouble() * (b - a); }
            public int Int(int a, int bInclusive) { return a + r.Next(bInclusive - a + 1); }
            public bool Bool(float pTrue = 0.5f) { return r.NextDouble() < pTrue; }
            public T Choice<T>(IList<T> list) { if (list == null || list.Count == 0) throw new ArgumentException(); return list[r.Next(list.Count)]; }
        }
    }

    // ----------------------------------------------------------------------
    // Dynamic entities used by this scene
    // ----------------------------------------------------------------------

    public sealed class BobbingSphereEntity : ISceneEntity
    {
        private readonly Sphere sphere;
        private readonly float baseY;
        private readonly float amplitude;
        private readonly float speed;
        private readonly float phase;
        private float t;
        public bool Enabled { get; set; } = true;

        public BobbingSphereEntity(Sphere sphere, float amplitude, float speed, float phase)
        {
            this.sphere = sphere;
            this.baseY = (float)sphere.Center.Y;
            this.amplitude = amplitude;
            this.speed = speed;
            this.phase = phase;
            this.t = 0.0f;
        }

        public void Update(float dt, Scene scene)
        {
            t += dt;
            float y = baseY + amplitude * MathF.Sin(speed * t + phase);
            sphere.Center = new Vec3(sphere.Center.X, y, sphere.Center.Z);
            scene.RequestGeometryRebuild();
        }

        public IEnumerable<Hittable> GetHittables()
        {
            yield return sphere;
        }
    }

    public sealed class OrbitingLightEntity : ISceneEntity
    {
        private readonly PointLight light;
        private readonly Vec3 pivot;
        private readonly float radius;
        private readonly float height;
        private readonly float speed;
        private float angle;
        private readonly float phase;
        public bool Enabled { get; set; } = true;

        public OrbitingLightEntity(PointLight light, Vec3 pivot, float radius, float height, float speed, float phase)
        {
            this.light = light;
            this.pivot = pivot;
            this.radius = radius;
            this.height = height;
            this.speed = speed;
            this.phase = phase;
            this.angle = 0.0f;
        }

        public void Update(float dt, Scene scene)
        {
            angle += speed * dt;
            float a = angle + phase;
            float x = (float)(pivot.X + radius * MathF.Cos(a));
            float z = (float)(pivot.Z + radius * MathF.Sin(a));
            light.Position = new Vec3(x, height, z);
        }

        public IEnumerable<Hittable> GetHittables()
        {
            yield break;
        }
    }

    public sealed class PulsingLightEntity : ISceneEntity
    {
        private readonly PointLight light;
        private readonly float initialIntensity;
        private readonly float minMult;
        private readonly float maxMult;
        private readonly float speed;
        private float t;
        public bool Enabled { get; set; } = true;

        // baseScale = 1.0 for around-baseline pulsing; ampFraction in [0,1) for +/- amplitude relative to base.
        public PulsingLightEntity(PointLight light, float baseScale, float ampFraction, float speed)
        {
            if (light == null) throw new ArgumentNullException(nameof(light));
            if (ampFraction < 0.0f) ampFraction = 0.0f;
            this.light = light;
            this.initialIntensity = light.Intensity;
            float bs = MathF.Max(0.0f, baseScale);
            this.minMult = MathF.Max(0.0f, bs * (1.0f - ampFraction));
            this.maxMult = bs * (1.0f + ampFraction);
            this.speed = speed;
            this.t = 0.0f;
        }

        public void Update(float dt, Scene scene)
        {
            if (!Enabled) return;
            if (dt < 0.0f) dt = 0.0f;
            t += dt;
            float s = 0.5f + 0.5f * MathF.Sin(speed * t);
            float mult = minMult + (maxMult - minMult) * s;
            float clamped = MathF.Max(0.0f, mult);
            light.Intensity = initialIntensity * clamped;
        }

        public IEnumerable<Hittable> GetHittables()
        {
            yield break;
        }
    }

    public sealed class UVWobbleEntity : ISceneEntity
    {
        private Material material;
        private readonly double baseScale;
        private readonly double amp;
        private readonly float speed;
        private float t;
        public bool Enabled { get; set; } = true;

        public UVWobbleEntity(Material material, double baseScale, double amplitude, float speed)
        {
            this.material = material;
            this.baseScale = baseScale;
            this.amp = amplitude;
            this.speed = speed;
            this.t = 0.0f;
        }

        public void Update(float dt, Scene scene)
        {
            t += dt;
            double s = baseScale + amp * MathF.Sin(speed * t);
            material.UVScale = s;
        }

        public IEnumerable<Hittable> GetHittables()
        {
            yield break;
        }
    }
}
