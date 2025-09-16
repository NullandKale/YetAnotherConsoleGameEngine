using System;

namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{

    ////////////////////////////////////////////////////////////////////////////////////////////
    // EXTENSIBLE PIPELINE INTERFACES
    ////////////////////////////////////////////////////////////////////////////////////////////
    public readonly struct WorldContext
    {
        public readonly WorldConfig Config;
        public readonly int Seed;
        public WorldContext(WorldConfig cfg) { Config = cfg; Seed = cfg.WorldSeed; }
    }

    public interface IHeightModel
    {
        int ComputeHeight(int x, int z, in WorldContext ctx);
    }

    public interface IHydrologyModel
    {
        int EffectiveSeaLevel(in WorldContext ctx);
        int WaterSurfaceLevelAt(int gx, int gz, int groundHeight, in WorldContext ctx);
        float RiverMask01(int x, int z, in WorldContext ctx);
        float RiverMask01ForHeight(int x, int z, in WorldContext ctx);
        float RiverCenterSignal(int x, int z, in WorldContext ctx);
        bool InRiverCoreBand(int x, int z, int targetWidthBlocks, in WorldContext ctx);
        int LakeTableHeight(int x, int z, in WorldContext ctx);
        int RiverBedY(int gx, int gz, int groundHeight, in WorldContext ctx);
        int RiverSurfaceY(int gx, int gz, int groundHeight, int localWater, int riverBedY, in WorldContext ctx);
    }

    public interface IBiomeLayering
    {
        (int, int) ChooseSurfaceBlock(int groundHeight, int localWater, float moisture, in WorldContext ctx);
        (int, int) ChooseSubsurfaceBlock(int gy, int groundHeight, float moisture, in WorldContext ctx);
    }

    public interface ICaveCarver
    {
        bool ShouldCarve(int gx, int gy, int gz, int groundHeight, in WorldContext ctx);
    }

    public interface IOreDistributor
    {
        bool TryOreAt(int gx, int gy, int gz, out (int, int) ore, in WorldContext ctx);
    }

    public interface IFloraPlacer
    {
        void TryPlaceFeaturesInChunk(int cx, int cy, int cz, WorldConfig cfg, int[,] hCache, int[,] wCache, float[,] moistCache, bool[,] riverCoreCache, (int, int)[,,] cells, ref bool anySolid);
    }

    public sealed class WorldGenPipeline
    {
        public readonly IHeightModel Height;
        public readonly IHydrologyModel Hydro;
        public readonly IBiomeLayering Layering;
        public readonly ICaveCarver Caves;
        public readonly IOreDistributor Ores;
        public readonly IFloraPlacer Flora;

        public WorldGenPipeline(IHeightModel height, IHydrologyModel hydro, IBiomeLayering layering, ICaveCarver caves, IOreDistributor ores, IFloraPlacer flora)
        {
            Height = height;
            Hydro = hydro;
            Layering = layering;
            Caves = caves;
            Ores = ores;
            Flora = flora;
        }

        public static WorldGenPipeline CreateDefault()
        {
            // Simplified generator no longer uses the pipeline; return a placeholder.
            return new WorldGenPipeline(height: null, hydro: null, layering: null, caves: null, ores: null, flora: null);
        }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////
    // PUBLIC WORLD GENERATOR (pluggable)
    ////////////////////////////////////////////////////////////////////////////////////////////
    public sealed class WorldGenerator
    {
        private readonly WorldGenPipeline _pipeline;

        public WorldGenerator() : this(WorldGenPipeline.CreateDefault()) { }

        public WorldGenerator(WorldGenPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public void GenerateChunkCells(int cx, int cy, int cz, WorldConfig cfg, (int, int)[,,] cells, out bool anySolid)
        {
            anySolid = false;
            int baseX = cx * cfg.ChunkSize;
            int baseY = cy * cfg.ChunkSize;
            int baseZ = cz * cfg.ChunkSize;
            int size = cfg.ChunkSize;

            // Precompute ground height, slope and biome for each column in this chunk
            int[,] ground = new int[size, size];
            float[,] slope01 = new float[size, size];
            Biome[,] biome = new Biome[size, size];
            int[,] localWater = new int[size, size];
            for (int lx = 0; lx < size; lx++)
            {
                int gx = baseX + lx;
                for (int lz = 0; lz < size; lz++)
                {
                    int gz = baseZ + lz;
                    int h = TerrainNoise.HeightY(gx, gz, cfg);
                    ground[lx, lz] = h;
                }
            }

            int sea = cfg.WaterLevel;
            int snow = cfg.SnowLevel;

            // Rivers: compute carve depths and river surface, then lower ground for channels
            RiverNetwork.ComputeForChunk(cx, cz, cfg, ground, applyCarve: true, out var carveDepth, out var riverWater);

            // Slope from local neighborhood of the precomputed (carved) heights
            for (int lx = 0; lx < size; lx++)
            {
                for (int lz = 0; lz < size; lz++)
                {
                    int x0 = Math.Max(0, lx - 1), x1 = Math.Min(size - 1, lx + 1);
                    int z0 = Math.Max(0, lz - 1), z1 = Math.Min(size - 1, lz + 1);
                    float dx = (ground[x1, lz] - ground[x0, lz]) * 0.5f;
                    float dz = (ground[lx, z1] - ground[lx, z0]) * 0.5f;
                    float g = MathF.Sqrt(dx * dx + dz * dz);
                    slope01[lx, lz] = GenMath.Saturate(g / WorldGenSettings.Normalization.SlopeNormalize);
                }
            }

            // Biome selection per column and local water level
            for (int lx = 0; lx < size; lx++)
            {
                int gx = baseX + lx;
                for (int lz = 0; lz < size; lz++)
                {
                    int gz = baseZ + lz;
                    int gY = ground[lx, lz];
                    float sl = slope01[lx, lz];
                    biome[lx, lz] = BiomeMap.Evaluate(gx, gz, gY, sea, snow, sl, cfg);
                    int inland = TerrainNoise.LocalWaterY(gx, gz, cfg, gY, sl);
                    localWater[lx, lz] = Math.Max(inland, riverWater[lx, lz]);
                    if (localWater[lx, lz] > sea && gY <= localWater[lx, lz])
                        biome[lx, lz] = Biome.Lakes;
                }
            }

            // Fill voxels per column with simple strata
            for (int lx = 0; lx < size; lx++)
            {
                for (int lz = 0; lz < size; lz++)
                {
                    int gY = ground[lx, lz];
                    int wY = localWater[lx, lz];

                    for (int ly = 0; ly < size; ly++)
                    {
                        int gy = baseY + ly;
                        (int, int) block;

                        if (gy > gY)
                        {
                            block = gy <= wY ? (WorldGenSettings.Blocks.Water, 0) : (WorldGenSettings.Blocks.Air, 0);
                        }
                        else if (gy == gY)
                        {
                            // Inland beaches near raised water surfaces
                            if (wY > sea && (wY - gY) <= IslandSettings.BeachBuffer)
                                block = (WorldGenSettings.Blocks.Sand, 0);
                            else
                            {
                                int top = Layering.ChooseSurfaceBlock(biome[lx, lz], gY, sea, snow, slope01[lx, lz]);
                                block = (top, 0);
                            }
                        }
                        else if (gy >= gY - WorldGenSettings.Terrain.DirtDepth)
                        {
                            int sub = Layering.ChooseSubsurfaceBlock(biome[lx, lz], gy, gY, sea);
                            block = (sub, 0);
                        }
                        else
                        {
                            int meta = StrataMap.RockMetaAt(baseX + lx, gy, baseZ + lz, cfg);
                            block = (WorldGenSettings.Blocks.Stone, meta);
                        }

                        cells[lx, ly, lz] = block;
                        if (block.Item1 != 0) anySolid = true;
                    }
                }
            }

            // After base terrain, place trees (only modifies empty space)
            FloraPlacer.PlaceTreesInChunk(cx, cy, cz, cfg, ground, biome, slope01, localWater, cells, ref anySolid);
        }

        public (int, int) GetBlockAt(int gx, int gy, int gz, WorldConfig cfg)
        {
            int sea = cfg.WaterLevel;
            int snow = cfg.SnowLevel;
            int gY = TerrainNoise.HeightY(gx, gz, cfg);
            float slope = TerrainNoise.Slope01At(gx, gz, cfg);
            Biome biome = BiomeMap.Evaluate(gx, gz, gY, sea, snow, slope, cfg);
            int wY = TerrainNoise.LocalWaterY(gx, gz, cfg, gY, slope);
            if (wY > sea && gY <= wY) biome = Biome.Lakes;

            if (gy > gY)
                return gy <= wY ? (WorldGenSettings.Blocks.Water, 0) : (WorldGenSettings.Blocks.Air, 0);

            if (gy == gY)
            {
                if (wY > sea && (wY - gY) <= IslandSettings.BeachBuffer)
                    return (WorldGenSettings.Blocks.Sand, 0);
                int top = Layering.ChooseSurfaceBlock(biome, gY, sea, snow, slope);
                return (top, 0);
            }

            if (gy >= gY - WorldGenSettings.Terrain.DirtDepth)
            {
                int sub = Layering.ChooseSubsurfaceBlock(biome, gy, gY, sea);
                return (sub, 0);
            }

            return (WorldGenSettings.Blocks.Stone, 0);
        }

        private static int ComputeIslandGroundHeight(int gx, int gz, WorldConfig cfg)
        {
            return TerrainNoise.HeightY(gx, gz, cfg);
        }

        private static float Moisture01(int x, int z, in WorldContext ctx)
        {
            float m = GenMath.FBM2D(x * WorldGenSettings.Moisture.CoordScale, z * WorldGenSettings.Moisture.CoordScale, WorldGenSettings.Moisture.Octaves, WorldGenSettings.Moisture.Lacunarity, WorldGenSettings.Moisture.Gain, WorldGenSettings.Moisture.BaseFreq, ctx.Seed + 321);
            return GenMath.Saturate(m);
        }
    }
}
