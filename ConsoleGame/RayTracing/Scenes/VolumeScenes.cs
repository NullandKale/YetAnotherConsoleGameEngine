using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using ConsoleGame.RayTracing;
using ConsoleGame.RayTracing.Scenes.WorldGeneration;  // for Ray, HitRecord

namespace ConsoleGame.RayTracing.Scenes
{
    public sealed class VolumeScene : Scene
    {
        private WorldManager worldManager;
        private WorldConfig worldConfig;

        // --- Camera physics state ---
        private bool autoPlaced = false;
        private float verticalVelocity = 0.0f;

        // Tunables
        private const float EyeHeight = 1.7f;           // eyes above feet
        private const float GravityAccel = 9.81f;       // real-world gravity (m/s^2)
        private const float MaxProbeHeight = 4096.0f;   // safety cap for initial drop
        private const float GroundClearance = 0.10f;    // to avoid z-fighting on ground
        private const float JumpSpeed = 4.8f;           // >= 1m jump apex with 9.81 m/s^2
        private const float LocalProbeDepthMin = 3.0f;  // min downward probe each frame
        private const float LocalProbeStart = 0.6f;     // start probe slightly above camera
        private const float ProbeRadius = 0.35f;        // radius for 5-ray ground fan
        private const float ExtraFallMargin = 1.0f;     // probe deeper than fall distance
        private const float StepUpGuardEpsilon = 0.05f; // prevents upward snap on walls

        // Shift = fly (ignore gravity & ground snapping), faster
        private const float ShiftSpeedMult = 30.0f;
        private const float ShiftHoldGraceSeconds = 0.15f; // small timer refreshed by input events
        private float shiftHoldTimer = 0.0f;

        // --- Wall repulsion (outward "force") ---
        private const float CollisionRadius = 0.65f; // horizontal personal-space radius
        private const float RepelImpulse = 30.0f;    // impulse scale per unit penetration
        private const float RepelDamping = 8.0f;     // exponential damping of push velocity
        private Vec3 repelVel = new Vec3(0.0, 0.0, 0.0); // lateral push velocity (Y ignored)

        // Derived/ephemeral
        private bool isGrounded = false;

        public void InitializeWorld(WorldManager manager, WorldConfig config)
        {
            this.worldManager = manager ?? throw new ArgumentNullException(nameof(manager));
            this.worldConfig = config ?? throw new ArgumentNullException(nameof(config));
        }

        public override void Update(float deltaTimeMS)
        {
            if (worldManager == null)
            {
                base.Update(deltaTimeMS);
                return;
            }

            float dt = deltaTimeMS;
            if (dt < 0.0f) dt = 0.0f;
            dt *= 0.001f;

            // Stream chunks first so new geometry is present for this frame's BVH
            worldManager.LoadChunksAround(CameraPos);

            // Update scene entities (e.g., day-night cycle) and timekeeping,
            // which also flushes entity geometry into Objects and rebuilds BVH when needed.
            base.Update(deltaTimeMS);

            // If we start this tick embedded in terrain (due to tunneling/edge cases),
            // search for nearby air and push the camera outside before physics.
            ResolveIfEmbedded();

            // One-time auto placement.
            if (!autoPlaced)
            {
                double probeTop = MathF.Min(worldConfig.WorldHeight + 16.0f, MaxProbeHeight);
                if (TrySampleGroundYFan(CameraPos.X, CameraPos.Z, probeTop, probeTop + 8.0f, out double g0))
                    CameraPos = new Vec3(CameraPos.X, g0 + EyeHeight + GroundClearance, CameraPos.Z);
                else
                    CameraPos = new Vec3(CameraPos.X, worldConfig.WaterLevel + EyeHeight + 4.0f, CameraPos.Z);

                verticalVelocity = 0.0f;
                autoPlaced = true;
                return;
            }

            // Count down Shift fly window (refreshed by input with Shift).
            if (shiftHoldTimer > 0.0f)
                shiftHoldTimer = Math.Max(0.0f, shiftHoldTimer - dt);

            bool shiftFlyActive = shiftHoldTimer > 0.0f;

            // --- Gravity & ground snap (disabled while Shift-fly is active) ---
            if (!shiftFlyActive)
            {
                verticalVelocity -= GravityAccel * dt;
                double newY = CameraPos.Y + verticalVelocity * dt;

                // Probe depth must cover fall distance to avoid tunneling.
                double fallDist = Math.Max(0.0, CameraPos.Y - newY);
                double probeDepth = Math.Max(LocalProbeDepthMin, fallDist + ExtraFallMargin);

                // 5-ray ground probe (center + 4 offsets) to catch edges.
                double localProbeStartY = CameraPos.Y + LocalProbeStart;
                bool hasGround = TrySampleGroundYFan(CameraPos.X, CameraPos.Z, localProbeStartY, probeDepth + LocalProbeStart, out double ground);

                isGrounded = false;
                if (hasGround)
                {
                    double floorY = ground + EyeHeight + GroundClearance;
                    bool floorIsAboveHead = floorY > CameraPos.Y + StepUpGuardEpsilon;

                    // Snap only DOWN (never up).
                    if (newY <= floorY && !floorIsAboveHead)
                    {
                        newY = floorY;
                        verticalVelocity = 0.0f;
                        isGrounded = true;
                    }
                    else
                    {
                        isGrounded = (!floorIsAboveHead) && ((CameraPos.Y - floorY) <= StepUpGuardEpsilon);
                    }
                }

                CameraPos = new Vec3(CameraPos.X, newY, CameraPos.Z);

                // Post-step correction: if we ended up very near the floor below, snap cleanly.
                double corrStartY = CameraPos.Y + LocalProbeStart;
                double corrDepth = LocalProbeStart + 1.5; // slightly deeper snap without auto-step
                if (TrySampleGroundYFan(CameraPos.X, CameraPos.Z, corrStartY, corrDepth, out double g2))
                {
                    double f2 = g2 + EyeHeight + GroundClearance;
                    if (f2 <= CameraPos.Y + StepUpGuardEpsilon && (CameraPos.Y - f2) <= 0.5)
                    {
                        CameraPos = new Vec3(CameraPos.X, f2, CameraPos.Z);
                        verticalVelocity = 0.0f;
                        isGrounded = true;
                    }
                }
            }
            else
            {
                // In fly mode, ignore gravity & snap.
                verticalVelocity = 0.0f;
            }

            // --- Outward wall repulsion (works in both walk & fly modes) ---
            ApplyWallRepulsion(dt);

            // World fail-safe.
            if (CameraPos.Y < -10.0)
            {
                CameraPos = new Vec3(CameraPos.X, 200.0, CameraPos.Z);
                verticalVelocity = 0.0f;
                repelVel = new Vec3(0.0, 0.0, 0.0);
            }
        }

        /// <summary>
        /// Shift = fly (ignores gravity) and increases speed while held.
        /// Space = jump (only when grounded). E/Q = vertical strafe.
        /// </summary>
        public override void HandleInput(ConsoleKeyInfo keyInfo, float dt)
        {
            if (dt < 0.0f) dt = 0.0f;

            float moveSpeed = 6.0f;
            float rotSpeed = 2.2f;

            bool shiftDown = (keyInfo.Modifiers & ConsoleModifiers.Shift) != 0;
            if (shiftDown)
            {
                moveSpeed *= ShiftSpeedMult;  // faster while Shift-held
                rotSpeed *= 1.8f;
                shiftHoldTimer = ShiftHoldGraceSeconds; // enable fly for this frame-window
            }

            // Camera rotation
            if (keyInfo.Key == ConsoleKey.LeftArrow) Yaw -= rotSpeed * dt;
            if (keyInfo.Key == ConsoleKey.RightArrow) Yaw += rotSpeed * dt;
            if (keyInfo.Key == ConsoleKey.UpArrow) Pitch += rotSpeed * dt;
            if (keyInfo.Key == ConsoleKey.DownArrow) Pitch -= rotSpeed * dt;

            float limit = (MathF.PI * 0.5f) - 0.01f;
            if (Pitch > limit) Pitch = limit;
            if (Pitch < -limit) Pitch = -limit;

            // Horizontal basis (XZ)
            float cy = MathF.Cos(Yaw);
            float sy = MathF.Sin(Yaw);
            Vec3 forwardXZ = new Vec3(sy, 0.0, -cy);
            Vec3 rightXZ = new Vec3(cy, 0.0, sy);
            Vec3 up = new Vec3(0.0, 1.0, 0.0);

            // Horizontal moves
            if (keyInfo.Key == ConsoleKey.W) AttemptMoveHorizontal(forwardXZ * (moveSpeed * dt));
            if (keyInfo.Key == ConsoleKey.S) AttemptMoveHorizontal(-forwardXZ * (moveSpeed * dt));
            if (keyInfo.Key == ConsoleKey.A) AttemptMoveHorizontal(-rightXZ * (moveSpeed * dt));
            if (keyInfo.Key == ConsoleKey.D) AttemptMoveHorizontal(rightXZ * (moveSpeed * dt));

            // Vertical strafe (fly-style up/down). Gravity is ignored in Update while Shift is active.
            if (keyInfo.Key == ConsoleKey.E) AttemptMoveVertical(moveSpeed * dt);
            if (keyInfo.Key == ConsoleKey.Q) AttemptMoveVertical(-moveSpeed * dt);

            // Jump only in normal (non-Shift-fly) mode and only when grounded.
            if (keyInfo.Key == ConsoleKey.Spacebar && !shiftDown && isGrounded)
            {
                verticalVelocity = JumpSpeed;
                isGrounded = false;
            }
        }

        // --- Continuous collision for player movement ---
        // Subdivides motion into micro-steps and raycasts at torso/eye heights with a 3-ray fan
        // (center/left/right) to approximate a capsule of radius CollisionRadius. Prevents tunneling
        // at low framerates or high speeds.
        private void AttemptMoveHorizontal(Vec3 delta)
        {
            double maxStep = Math.Max(0.1, CollisionRadius * 0.35);
            double remaining = Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
            if (remaining <= 1e-6) return;

            Vec3 dir = new Vec3(delta.X / remaining, 0.0, delta.Z / remaining);
            Vec3 perp = new Vec3(-dir.Z, 0.0, dir.X); // 90deg in XZ

            while (remaining > 1e-6)
            {
                double step = Math.Min(remaining, maxStep);
                double best = step;
                bool hadBlockNormal = false;
                Vec3 blockNormal = new Vec3(0.0, 0.0, 0.0);

                // Sample at two heights: eye and torso
                double eyeY = CameraPos.Y;
                double torsoY = CameraPos.Y - (EyeHeight * 0.5);

                // Offsets to approximate capsule radius
                double r = CollisionRadius;
                ReadOnlySpan<double> offs = stackalloc double[] { 0.0, r, -r };

                for (int i = 0; i < offs.Length; i++)
                {
                    double o = offs[i];
                    Vec3 offVec = new Vec3(perp.X * o, 0.0, perp.Z * o);

                    // Eye ray
                    LimitStepByRay(new Vec3(CameraPos.X + offVec.X, eyeY, CameraPos.Z + offVec.Z), dir, ref best, ref hadBlockNormal, ref blockNormal);
                    // Torso ray
                    LimitStepByRay(new Vec3(CameraPos.X + offVec.X, torsoY, CameraPos.Z + offVec.Z), dir, ref best, ref hadBlockNormal, ref blockNormal);
                }

                if (best <= 1e-5)
                {
                    // Can't advance; stop horizontal motion this event (no auto step)
                    break;
                }

                // Apply step
                CameraPos = new Vec3(CameraPos.X + dir.X * best,
                                      CameraPos.Y,
                                      CameraPos.Z + dir.Z * best);
                remaining -= best;

                // If blocked before full step, try to slide along the blocking surface
                double residual = step - best;
                if (residual > 1e-6 && hadBlockNormal)
                {
                    Vec3 slide = ProjectOntoPlaneXZ(dir, blockNormal);
                    float len = MathF.Sqrt(slide.X * slide.X + slide.Z * slide.Z);
                    if (len > 1e-6f)
                    {
                        dir = new Vec3(slide.X / len, 0.0, slide.Z / len);
                        remaining += residual; // attempt to use the leftover along slide
                    }
                }
            }
        }

        // Auto-step removed by request

        private void LimitStepByRay(Vec3 origin, Vec3 dir, ref double best, ref bool hadNormal, ref Vec3 blockNormal)
        {
            Ray r = new Ray(origin, dir);
            HitRecord rec = default;
            float tmax = (float)(best + 1e-3);
            if (Hit(r, 0.001f, tmax, ref rec, 0, 0))
            {
                double cand = Math.Max(0.0, rec.T - 0.01);
                if (cand < best)
                {
                    best = cand;
                    hadNormal = true;
                    blockNormal = rec.N;
                }
            }
        }

        private void AttemptMoveVertical(double dy)
        {
            if (Math.Abs(dy) <= 1e-6) return;
            double remaining = Math.Abs(dy);
            double stepSign = Math.Sign(dy);
            double maxStep = Math.Max(0.1, EyeHeight * 0.25);
            while (remaining > 1e-6)
            {
                double step = Math.Min(remaining, maxStep);
                Vec3 dir = new Vec3(0.0, stepSign, 0.0);
                Ray r = new Ray(new Vec3(CameraPos.X, CameraPos.Y, CameraPos.Z), dir);
                HitRecord rec = default;
                float tmax = (float)(step + 1e-3);
                if (Hit(r, 0.001f, tmax, ref rec, 0, 0))
                {
                    double cand = Math.Max(0.0, rec.T - 0.01);
                    if (cand <= 1e-5) break;
                    CameraPos = new Vec3(CameraPos.X, CameraPos.Y + cand * stepSign, CameraPos.Z);
                }
                else
                {
                    CameraPos = new Vec3(CameraPos.X, CameraPos.Y + step * stepSign, CameraPos.Z);
                }
                remaining -= step;
            }
        }

        // --- Embedded resolution ---
        private void ResolveIfEmbedded()
        {
            if (!IsLikelyEmbedded()) return;

            // Search for nearest exit to air in 6 axes from eye/torso heights
            Vec3[] dirs = new Vec3[]
            {
                new Vec3( 1, 0,  0), new Vec3(-1, 0,  0),
                new Vec3( 0, 0,  1), new Vec3( 0, 0, -1),
                new Vec3( 0, 1,  0), new Vec3( 0, -1, 0),
            };

            double eyeY = CameraPos.Y;
            double torsoY = CameraPos.Y - (EyeHeight * 0.5);
            float searchR = (float)Math.Max(CollisionRadius * 4.0, (worldConfig != null ? worldConfig.VoxelSize.Y : 1.0) * 4.0);

            bool found = false;
            double bestT = double.PositiveInfinity;
            Vec3 bestDir = new Vec3(0.0, 1.0, 0.0);

            for (int i = 0; i < dirs.Length; i++)
            {
                Vec3 d = dirs[i];
                // Eye
                if (RayDist(new Vec3(CameraPos.X, eyeY, CameraPos.Z), d, 0.0f, searchR, out float tEye))
                {
                    if (tEye < bestT)
                    {
                        bestT = tEye; bestDir = d; found = true;
                    }
                }
                // Torso
                if (RayDist(new Vec3(CameraPos.X, torsoY, CameraPos.Z), d, 0.0f, searchR, out float tTorso))
                {
                    if (tTorso < bestT)
                    {
                        bestT = tTorso; bestDir = d; found = true;
                    }
                }
            }

            if (found && bestT < double.PositiveInfinity)
            {
                double eps = Math.Max(0.02, (worldConfig != null ? worldConfig.VoxelSize.Y : 1.0) * 0.02);
                CameraPos = new Vec3(CameraPos.X + bestDir.X * (bestT + eps),
                                     CameraPos.Y + bestDir.Y * (bestT + eps),
                                     CameraPos.Z + bestDir.Z * (bestT + eps));

                // Resolve any residual small overlaps
                ApplyWallRepulsion(0.0f);
                ApplyWallRepulsion(0.0f);
            }
        }

        private bool IsLikelyEmbedded()
        {
            double eyeY = CameraPos.Y;
            double torsoY = CameraPos.Y - (EyeHeight * 0.5);
            return IsLikelyEmbeddedAtY(eyeY) || IsLikelyEmbeddedAtY(torsoY);
        }

        private bool IsLikelyEmbeddedAtY(double y)
        {
            float probe = (float)Math.Max(0.05, CollisionRadius * 0.25);
            Vec3 o = new Vec3(CameraPos.X, y, CameraPos.Z);
            bool xPos = RayHitWithin(o, new Vec3(1, 0, 0), probe);
            bool xNeg = RayHitWithin(o, new Vec3(-1, 0, 0), probe);
            bool zPos = RayHitWithin(o, new Vec3(0, 0, 1), probe);
            bool zNeg = RayHitWithin(o, new Vec3(0, 0, -1), probe);
            return (xPos && xNeg) && (zPos && zNeg);
        }

        private bool RayHitWithin(Vec3 origin, Vec3 dir, float tMax)
        {
            HitRecord rec = default;
            Ray r = new Ray(origin, dir);
            return Hit(r, 0.0f, tMax, ref rec, 0, 0);
        }

        private bool RayDist(Vec3 origin, Vec3 dir, float tMin, float tMax, out float t)
        {
            HitRecord rec = default;
            Ray r = new Ray(origin, dir);
            bool hit = Hit(r, tMin, tMax, ref rec, 0, 0);
            t = rec.T;
            return hit;
        }

        // --- Penetration resolution (no bounce) ---
        // Push out of overlap along the surface normal. No accumulated outward velocity; allows sliding.
        private void ApplyWallRepulsion(float dt)
        {
            const float eps = 0.01f;
            ReadOnlySpan<(double x, double z)> dirs = stackalloc (double, double)[]
            {
                ( 1,  0), (-1,  0), ( 0,  1), ( 0, -1),
                ( 1,  1), ( 1, -1), (-1,  1), (-1, -1),
            };

            double eyeY = CameraPos.Y;
            double torsoY = CameraPos.Y - (EyeHeight * 0.5);

            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var (dx, dz) in dirs)
                {
                    double len = Math.Sqrt(dx * dx + dz * dz);
                    Vec3 dirXZ = new Vec3(dx / len, 0.0, dz / len);
                    ResolvePenetrationAt(new Vec3(CameraPos.X, eyeY, CameraPos.Z), dirXZ, eps);
                    ResolvePenetrationAt(new Vec3(CameraPos.X, torsoY, CameraPos.Z), dirXZ, eps);
                }
            }
        }

        private void ResolvePenetrationAt(Vec3 origin, Vec3 dirXZ, float eps)
        {
            Ray r = new Ray(origin, dirXZ);
            HitRecord rec = default;
            if (Hit(r, 0.001f, (float)CollisionRadius, ref rec, 0, 0))
            {
                float pen = CollisionRadius - rec.T;
                if (pen > 0.0f)
                {
                    Vec3 n = rec.N;
                    Vec3 nXZ = new Vec3(n.X, 0.0, n.Z);
                    float nl = MathF.Sqrt(nXZ.X * nXZ.X + nXZ.Z * nXZ.Z);
                    if (nl > 1e-6f)
                    {
                        nXZ = new Vec3(nXZ.X / nl, 0.0, nXZ.Z / nl);
                        float push = pen + eps;
                        CameraPos = new Vec3(CameraPos.X + nXZ.X * push,
                                             CameraPos.Y,
                                             CameraPos.Z + nXZ.Z * push);
                    }
                }
            }
        }

        private static Vec3 ProjectOntoPlaneXZ(Vec3 v, Vec3 n)
        {
            Vec3 nXZ = new Vec3(n.X, 0.0, n.Z);
            float nl = MathF.Sqrt(nXZ.X * nXZ.X + nXZ.Z * nXZ.Z);
            if (nl <= 1e-6f) return new Vec3(0.0, 0.0, 0.0);
            nXZ = new Vec3(nXZ.X / nl, 0.0, nXZ.Z / nl);
            float dot = (float)(v.X * nXZ.X + v.Z * nXZ.Z);
            return new Vec3(v.X - dot * nXZ.X, 0.0, v.Z - dot * nXZ.Z);
        }

        // --- Ground ray helpers ---
        // Picks the highest ground that would not place the camera above its current head position.
        private bool TrySampleGroundYFan(double x, double z, double startY, double maxDistance, out double groundY)
        {
            groundY = double.NegativeInfinity;
            bool anyHit = false;
            bool anyAcceptable = false;
            double bestAcceptable = double.NegativeInfinity;

            ReadOnlySpan<(double ox, double oz)> offs = stackalloc (double, double)[]
            {
                (0.0, 0.0),
                ( ProbeRadius, 0.0),
                (-ProbeRadius, 0.0),
                (0.0,  ProbeRadius),
                (0.0, -ProbeRadius),
            };

            foreach (var (ox, oz) in offs)
            {
                if (TrySampleGroundY(x + ox, z + oz, startY, maxDistance, out double y))
                {
                    anyHit = true;

                    double floorCandidate = y + EyeHeight + GroundClearance;
                    if (floorCandidate <= CameraPos.Y + StepUpGuardEpsilon)
                    {
                        if (!anyAcceptable || y > bestAcceptable) bestAcceptable = y;
                        anyAcceptable = true;
                    }

                    if (!anyAcceptable && (double.IsNegativeInfinity(groundY) || y > groundY)) groundY = y;
                }
            }

            if (anyAcceptable)
            {
                groundY = bestAcceptable;
                return true;
            }

            return anyHit && !double.IsNegativeInfinity(groundY);
        }

        private bool TrySampleGroundY(double x, double z, double startY, double maxDistance, out double groundY)
        {
            groundY = 0.0;
            Ray r = new Ray(new Vec3(x, startY, z), new Vec3(0.0, -1.0, 0.0));
            HitRecord rec = default;
            bool hit = Hit(r, 0.00001f, (float)maxDistance, ref rec, 0, 0);
            if (!hit) return false;
            Vec3 p = r.At(rec.T);
            groundY = p.Y;
            return true;
        }

        public void ClearLoadedVolumes()
        {
            if (worldManager == null)
                throw new InvalidOperationException("WorldManager not initialized.");
            worldManager.ClearLoadedVolumes();
        }

        public void COC(Vec3 center)
        {
            if (worldManager == null)
                throw new InvalidOperationException("WorldManager not initialized.");
            worldManager.LoadChunksAround(center);
        }

        // Place camera at ground surface for given XZ before rendering begins.
        public void PlaceCameraOnSurfaceXZ(double x, double z)
        {
            if (worldConfig == null) return;
            double probeTop = MathF.Min(worldConfig.WorldHeight + 16.0f, MaxProbeHeight);
            if (TrySampleGroundYFan(x, z, probeTop, probeTop + 8.0f, out double g0))
                CameraPos = new Vec3(x, g0 + EyeHeight + GroundClearance, z);
            else
                CameraPos = new Vec3(x, worldConfig.WaterLevel + EyeHeight + 4.0f, z);
            verticalVelocity = 0.0f;
            isGrounded = true;
            autoPlaced = true;
        }

        public void ReloadFromExistingFile(string filename, Vec3 worldMinCorner, Vec3 voxelSize, Func<int, int, Material> materialLookup, int chunkSize = 32)
        {
            if (worldManager == null)
                throw new InvalidOperationException("WorldManager not initialized.");
            worldManager.ReloadFromExistingFile(filename, worldMinCorner, voxelSize, materialLookup, chunkSize);
        }
    }
    public static class VolumeScenes
    {
        public static VolumeScene BuildMinecraftLike(string filename = "assets/Worlds/island.vg")
        {
            int worldSize = 1024;
            int worldHeight = 256;
            int chunkSize = 32;

            int chunksX = worldSize / chunkSize;
            int chunksY = worldHeight / chunkSize;
            int chunksZ = worldSize / chunkSize;

            WorldConfig cfg = new WorldConfig(
                chunkSize: chunkSize,
                chunksX: chunksX,
                chunksY: chunksY,
                chunksZ: chunksZ,
                // Increase view distance so sun/moon shadow rays
                // can actually find occluders within the loaded BVH.
                // With 32-voxel chunks, 8 => ~256 units radius.
                viewDistanceChunks: 8,
                worldMin: new Vec3(-worldSize / 2, 0, -worldSize / 2),
                voxelSize: new Vec3(1, 1, 1),
                worldSeed: 0
            );

            VolumeScene scene = new VolumeScene();
            scene.DefaultCameraPos = new Vec3(0, 120, 0);
            scene.Ambient = new AmbientLight(new Vec3(1, 1, 1), 0.0f);
            scene.BackgroundTop = new Vec3(0.30, 0.55, 0.95);
            scene.BackgroundBottom = new Vec3(0.80, 0.90, 1.00);
            // Day-night controller will manage sun/moon lights and sky
            scene.AddEntity(new DayNightEntity(
                cycleSeconds: 120.0f,
                sunRadius: 2000.0f
            ));

            var generator = new WorldGenerator();
            var manager = new WorldManager(scene, generator, cfg, VoxelMaterialPalette.MaterialLookup);
            scene.InitializeWorld(manager, cfg);

            if (!string.IsNullOrEmpty(filename))
            {
                manager.GenerateAndSaveWorld(filename);
                scene.ReloadFromExistingFile(filename, cfg.WorldMin, cfg.VoxelSize, VoxelMaterialPalette.MaterialLookup, cfg.ChunkSize);
                // Ensure the entire world is fully attached before camera/control
                manager.EnsureAllChunksLoaded();
                // Place camera on surface at default XZ (BVH is valid now)
                scene.PlaceCameraOnSurfaceXZ(scene.DefaultCameraPos.X, scene.DefaultCameraPos.Z);
            }
            else
            {
                scene.ClearLoadedVolumes();
                // Synchronously generate and attach the entire world before control
                manager.EnsureAllChunksLoaded();
                // Place camera after world load
                scene.PlaceCameraOnSurfaceXZ(scene.DefaultCameraPos.X, scene.DefaultCameraPos.Z);
            }

            return scene;
        }

    }
}
