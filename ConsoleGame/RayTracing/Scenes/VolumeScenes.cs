using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using ConsoleGame.RayTracing;  // for Ray, HitRecord

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
        private const float GravityAccel = 12.0f;       // softer gravity
        private const float MaxProbeHeight = 4096.0f;   // safety cap for initial drop
        private const float GroundClearance = 0.10f;    // to avoid z-fighting on ground
        private const float JumpSpeed = 8.5f;           // jump impulse
        private const float LocalProbeDepthMin = 3.0f;  // min downward probe each frame
        private const float LocalProbeStart = 0.6f;     // start probe slightly above camera
        private const float ProbeRadius = 0.35f;        // radius for 5-ray ground fan
        private const float ExtraFallMargin = 1.0f;     // probe deeper than fall distance
        private const float StepUpGuardEpsilon = 0.05f; // prevents upward snap on walls

        // Shift = fly (ignore gravity & ground snapping), faster
        private const float ShiftSpeedMult = 3.0f;
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
            if (worldManager == null) return;

            float dt = deltaTimeMS;
            if (dt < 0.0f) dt = 0.0f;
            dt *= 0.001f;

            // Stream chunks.
            worldManager.LoadChunksAround(CameraPos);

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
                double corrDepth = LocalProbeStart + 1.0;
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
            if (keyInfo.Key == ConsoleKey.W) CameraPos = CameraPos + forwardXZ * (moveSpeed * dt);
            if (keyInfo.Key == ConsoleKey.S) CameraPos = CameraPos - forwardXZ * (moveSpeed * dt);
            if (keyInfo.Key == ConsoleKey.A) CameraPos = CameraPos - rightXZ * (moveSpeed * dt);
            if (keyInfo.Key == ConsoleKey.D) CameraPos = CameraPos + rightXZ * (moveSpeed * dt);

            // Vertical strafe (fly-style up/down). Gravity is ignored in Update while Shift is active.
            if (keyInfo.Key == ConsoleKey.E) CameraPos = CameraPos + up * (moveSpeed * dt);
            if (keyInfo.Key == ConsoleKey.Q) CameraPos = CameraPos - up * (moveSpeed * dt);

            // Jump only in normal (non-Shift-fly) mode and only when grounded.
            if (keyInfo.Key == ConsoleKey.Spacebar && !shiftDown && isGrounded)
            {
                verticalVelocity = JumpSpeed;
                isGrounded = false;
            }
        }

        // --- Outward wall repulsion implementation ---
        // Fires short rays around the camera (eye + torso heights). If a wall is closer than
        // CollisionRadius, accumulate an outward impulse (opposite the ray dir). We integrate a
        // damped lateral velocity so you gently glide off obstacles instead of sticking.
        private void ApplyWallRepulsion(float dt)
        {
            // Exponential damping of existing push velocity
            float damp = MathF.Exp(-RepelDamping * dt);
            repelVel = repelVel * damp;

            // Sample directions (8 around the circle)
            ReadOnlySpan<(double x, double z)> dirs = stackalloc (double, double)[]
            {
                ( 1,  0), (-1,  0), ( 0,  1), ( 0, -1),
                ( 1,  1), ( 1, -1), (-1,  1), (-1, -1),
            };

            // Two heights: eye and torso (helps doorways / ledges)
            double eyeY = CameraPos.Y;
            double torsoY = CameraPos.Y - (EyeHeight * 0.5);

            foreach (var (dx, dz) in dirs)
            {
                // Normalize horizontal dir
                double len = Math.Sqrt(dx * dx + dz * dz);
                double ux = dx / len;
                double uz = dz / len;

                // Eye
                AccumulateRepelAt(new Vec3(CameraPos.X, eyeY, CameraPos.Z), new Vec3(ux, 0.0, uz));
                // Torso
                AccumulateRepelAt(new Vec3(CameraPos.X, torsoY, CameraPos.Z), new Vec3(ux, 0.0, uz));
            }

            // Apply lateral push (Y ignored)
            CameraPos = new Vec3(CameraPos.X + repelVel.X * dt,
                                 CameraPos.Y,
                                 CameraPos.Z + repelVel.Z * dt);
        }

        private void AccumulateRepelAt(Vec3 origin, Vec3 dirXZ)
        {
            Ray r = new Ray(origin, dirXZ); // horizontal ray
            HitRecord rec = default;

            // Short ray up to the collision radius
            if (Hit(r, 0.001f, (float)CollisionRadius, ref rec, 0, 0))
            {
                // Penetration if the surface is within our "personal space"
                float pen = CollisionRadius - rec.T;
                if (pen > 0.0)
                {
                    // Outward impulse opposite the direction we cast
                    Vec3 impulse = (-dirXZ) * (RepelImpulse * pen);

                    // Only lateral; keep Y neutral to avoid vertical pops
                    repelVel = new Vec3(repelVel.X + impulse.X, 0.0, repelVel.Z + impulse.Z);
                }
            }
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

        public void ReloadFromExistingFile(string filename, Vec3 worldMinCorner, Vec3 voxelSize, Func<int, int, Material> materialLookup, int chunkSize = 32)
        {
            if (worldManager == null)
                throw new InvalidOperationException("WorldManager not initialized.");
            worldManager.ReloadFromExistingFile(filename, worldMinCorner, voxelSize, materialLookup, chunkSize);
        }
    }
    public static class VolumeScenes
    {
        public static VolumeScene BuildMinecraftLike(string filename = "")
        {
            WorldConfig cfg = new WorldConfig(
                chunkSize: 32,
                chunksX: 16,
                chunksY: 8,
                chunksZ: 16,
                viewDistanceChunks: 16,
                worldMin: new Vec3(-100, 0, -100),
                voxelSize: new Vec3(1, 1, 1),
                worldSeed: 0
            );

            VolumeScene scene = new VolumeScene();
            scene.DefaultCameraPos = new Vec3(0, 120, 0);
            scene.Ambient = new AmbientLight(new Vec3(1, 1, 1), 0.25f);
            scene.BackgroundTop = new Vec3(0.02, 0.02, 0.03);
            scene.BackgroundBottom = new Vec3(0.01, 0.01, 0.01);
            scene.Lights.Add(new PointLight(new Vec3(0.0, 500.0, 0.0), new Vec3(1.0, 1.0, 1.0), 400000.0f));
            scene.Lights.Add(new PointLight(new Vec3(-10.0, 500.0, -10.0), new Vec3(1.0, 0.95, 0.9), 160000.0f));

            var generator = new WorldGenerator();
            var manager = new WorldManager(scene, generator, cfg, VoxelMaterialPalette.MaterialLookup);
            scene.InitializeWorld(manager, cfg);

            if (!string.IsNullOrEmpty(filename) && File.Exists(filename))
            {
                scene.ReloadFromExistingFile(filename, cfg.WorldMin, cfg.VoxelSize, VoxelMaterialPalette.MaterialLookup, cfg.ChunkSize);
            }
            else if (!string.IsNullOrEmpty(filename))
            {
                manager.GenerateAndSaveWorld(filename);
                scene.ReloadFromExistingFile(filename, cfg.WorldMin, cfg.VoxelSize, VoxelMaterialPalette.MaterialLookup, cfg.ChunkSize);
            }
            else
            {
                scene.ClearLoadedVolumes();
                scene.COC(scene.DefaultCameraPos);
            }

            return scene;
        }
    }
}
