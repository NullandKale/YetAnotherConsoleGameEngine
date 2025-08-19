using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace ConsoleGame.RayTracing.Scenes
{
    public sealed class VolumeScene : Scene
    {
        private WorldManager worldManager;
        private WorldConfig worldConfig;

        public void InitializeWorld(WorldManager manager, WorldConfig config)
        {
            this.worldManager = manager ?? throw new ArgumentNullException(nameof(manager));
            this.worldConfig = config ?? throw new ArgumentNullException(nameof(config));
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
