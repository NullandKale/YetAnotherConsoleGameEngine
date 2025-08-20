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
        private double verticalVelocity = 0.0;
        private const double EyeHeight = 1.7;
        private const double GravityAccel = 30.0;
        private const double MaxProbeHeight = 4096.0;
        private const double GroundClearance = 0.10;

        public void InitializeWorld(WorldManager manager, WorldConfig config)
        {
            this.worldManager = manager ?? throw new ArgumentNullException(nameof(manager));
            this.worldConfig = config ?? throw new ArgumentNullException(nameof(config));
        }

        public override void Update(float deltaTimeMS)
        {
            if (worldManager == null)
            {
                return;
            }

            double dt = deltaTimeMS;
            if (dt < 0.0)
            {
                dt = 0.0;
            }
            dt *= 0.001;

            // Stream chunks around the player.
            worldManager.LoadChunksAround(CameraPos);

            // One-time auto placement: drop a ray from above to find the ground and park the camera just above it.
            if (!autoPlaced)
            {
                double probeY = Math.Min(worldConfig.WorldHeight + 16.0, MaxProbeHeight);
                double groundY;
                if (TrySampleGroundY(CameraPos.X, CameraPos.Z, probeY, probeY + 8.0, out groundY))
                {
                    CameraPos = new Vec3(CameraPos.X, groundY + EyeHeight + GroundClearance, CameraPos.Z);
                    verticalVelocity = 0.0;
                    autoPlaced = true;
                }
                else
                {
                    CameraPos = new Vec3(CameraPos.X, worldConfig.WaterLevel + EyeHeight + 4.0, CameraPos.Z);
                    verticalVelocity = 0.0;
                    autoPlaced = true;
                }
                return;
            }

            // Gravity integration (simple Euler).
            verticalVelocity -= GravityAccel * dt;
            double newY = CameraPos.Y + verticalVelocity * dt;

            // Query ground directly under current XZ by casting from high above each frame (robust in caves/overhangs).
            double probeTop = Math.Min(worldConfig.WorldHeight + 16.0, MaxProbeHeight);
            double ground;
            bool hasGround = TrySampleGroundY(CameraPos.X, CameraPos.Z, probeTop, probeTop + 8.0, out ground);

            // If we have a ground, prevent tunneling and stick to surface when descending.
            if (hasGround)
            {
                double targetFloorY = ground + EyeHeight + GroundClearance;
                if (newY <= targetFloorY)
                {
                    newY = targetFloorY;
                    verticalVelocity = 0.0;
                }
            }

            CameraPos = new Vec3(CameraPos.X, newY, CameraPos.Z);
        }

        private bool TrySampleGroundY(double x, double z, double startY, double maxDistance, out double groundY)
        {
            groundY = 0.0;
            Ray r = new Ray(new Vec3(x, startY, z), new Vec3(0.0, -1.0, 0.0));
            HitRecord rec = default;
            bool hit = Hit(r, 0.001f, (float)maxDistance, ref rec);
            if (!hit)
            {
                return false;
            }
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
            scene.Ambient = new AmbientLight(new Vec3(1, 1, 1), 1f);
            scene.BackgroundTop = new Vec3(0.02, 0.02, 0.03);
            scene.BackgroundBottom = new Vec3(0.01, 0.01, 0.01);
            scene.Lights.Add(new PointLight(new Vec3(0.0, 1200.0, 0.0), new Vec3(1.0, 1.0, 1.0), 400.0f));
            scene.Lights.Add(new PointLight(new Vec3(-10.0, 1000.0, -10.0), new Vec3(1.0, 0.95, 0.9), 160.0f));

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