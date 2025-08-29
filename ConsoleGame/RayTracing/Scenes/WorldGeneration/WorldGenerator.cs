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
            var height = new DefaultHeightModel();
            var hydro = new DefaultHydrologyModel();
            var layering = new DefaultBiomeLayering();
            var caves = new DefaultCaveCarver();
            var ores = new DefaultOreDistributor();
            var flora = new DefaultFloraPlacer();
            return new WorldGenPipeline(height, hydro, layering, caves, ores, flora);
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
            var ctx = new WorldContext(cfg);
            int baseX = cx * cfg.ChunkSize;
            int baseY = cy * cfg.ChunkSize;
            int baseZ = cz * cfg.ChunkSize;
            int size = cfg.ChunkSize;

            int[,] hCache = new int[size, size];
            int[,] wCache = new int[size, size];
            float[,] moistCache = new float[size, size];
            float[,] riverMaskCache = new float[size, size];
            bool[,] riverCoreCache = new bool[size, size];
            int[,] riverBedYCache = new int[size, size];
            int[,] riverSurfYCache = new int[size, size];

            for (int lx = 0; lx < size; lx++)
            {
                int gx = baseX + lx;
                for (int lz = 0; lz < size; lz++)
                {
                    int gz = baseZ + lz;
                    int h = _pipeline.Height.ComputeHeight(gx, gz, ctx);
                    float rMask = _pipeline.Hydro.RiverMask01(gx, gz, ctx);
                    int riverBedY = _pipeline.Hydro.RiverBedY(gx, gz, h, ctx);
                    int w = _pipeline.Hydro.WaterSurfaceLevelAt(gx, gz, h, ctx);
                    bool inCore = _pipeline.Hydro.InRiverCoreBand(gx, gz, WorldGenSettings.Water.RiverTargetWidthBlocks, ctx);
                    int coreSurf = _pipeline.Hydro.RiverSurfaceY(gx, gz, h, w, riverBedY, ctx);
                    hCache[lx, lz] = h;
                    wCache[lx, lz] = w;
                    moistCache[lx, lz] = Moisture01(gx, gz, ctx);
                    riverMaskCache[lx, lz] = rMask;
                    riverCoreCache[lx, lz] = inCore;
                    riverBedYCache[lx, lz] = riverBedY;
                    riverSurfYCache[lx, lz] = coreSurf;
                }
            }

            for (int lx = 0; lx < size; lx++)
            {
                int gx = baseX + lx;
                for (int lz = 0; lz < size; lz++)
                {
                    int gz = baseZ + lz;
                    int groundHeight = hCache[lx, lz];
                    int localWater = wCache[lx, lz];
                    bool riverCore = riverCoreCache[lx, lz];
                    int riverBedY = riverBedYCache[lx, lz];
                    int riverSurfY = riverSurfYCache[lx, lz];
                    bool riverHasWater = riverCore && riverSurfY > riverBedY;

                    for (int ly = 0; ly < size; ly++)
                    {
                        int gy = baseY + ly;
                        (int mat, int meta) block = (0, 0);

                        if (gy > groundHeight)
                        {
                            block = gy <= localWater ? (WorldGenSettings.Blocks.Water, 0) : (WorldGenSettings.Blocks.Air, 0);
                        }
                        else
                        {
                            if (riverCore)
                            {
                                if (gy == riverBedY) { cells[lx, ly, lz] = (WorldGenSettings.Blocks.Sand, 0); if (cells[lx, ly, lz].Item1 != 0) anySolid = true; continue; }
                                if (gy > riverBedY && gy <= groundHeight)
                                {
                                    if (riverHasWater && gy <= riverSurfY) { cells[lx, ly, lz] = (WorldGenSettings.Blocks.Water, 0); if (cells[lx, ly, lz].Item1 != 0) anySolid = true; continue; }
                                    else { cells[lx, ly, lz] = (WorldGenSettings.Blocks.Air, 0); continue; }
                                }
                            }

                            if (gy == groundHeight)
                            {
                                block = _pipeline.Layering.ChooseSurfaceBlock(groundHeight, localWater, moistCache[lx, lz], ctx);
                            }
                            else
                            {
                                block = _pipeline.Layering.ChooseSubsurfaceBlock(gy, groundHeight, moistCache[lx, lz], ctx);
                                if (block.Item1 == WorldGenSettings.Blocks.Stone)
                                {
                                    if (_pipeline.Ores.TryOreAt(gx, gy, gz, out var ore, ctx)) block = ore;
                                    if (gy == 0) block = (WorldGenSettings.Blocks.Stone, 0);
                                }
                            }
                        }

                        if (block.Item1 != 0 && block.Item1 != WorldGenSettings.Blocks.Water)
                        {
                            if (_pipeline.Caves.ShouldCarve(gx, gy, gz, groundHeight, ctx)) block = (WorldGenSettings.Blocks.Air, 0);
                        }

                        if ((block.Item1 == WorldGenSettings.Blocks.Dirt || block.Item1 == WorldGenSettings.Blocks.Grass) && gy <= localWater + WorldGenSettings.Terrain.UnderwaterSandBuffer)
                        {
                            block = (WorldGenSettings.Blocks.Sand, 0);
                        }

                        cells[lx, ly, lz] = block;
                        if (block.Item1 != 0) anySolid = true;
                    }
                }
            }

            _pipeline.Flora.TryPlaceFeaturesInChunk(cx, cy, cz, cfg, hCache, wCache, moistCache, riverCoreCache, cells, ref anySolid);
        }

        public (int, int) GetBlockAt(int gx, int gy, int gz, WorldConfig cfg)
        {
            var ctx = new WorldContext(cfg);
            int height = _pipeline.Height.ComputeHeight(gx, gz, ctx);
            int localWater = _pipeline.Hydro.WaterSurfaceLevelAt(gx, gz, height, ctx);
            float moisture = Moisture01(gx, gz, ctx);
            bool riverCore = _pipeline.Hydro.InRiverCoreBand(gx, gz, WorldGenSettings.Water.RiverTargetWidthBlocks, ctx);
            int riverBedY = _pipeline.Hydro.RiverBedY(gx, gz, height, ctx);
            int riverSurfY = _pipeline.Hydro.RiverSurfaceY(gx, gz, height, localWater, riverBedY, ctx);
            bool riverHasWater = riverCore && riverSurfY > riverBedY;

            (int mat, int meta) block;
            if (gy > height)
            {
                block = gy <= localWater ? (WorldGenSettings.Blocks.Water, 0) : (WorldGenSettings.Blocks.Air, 0);
            }
            else
            {
                if (riverCore)
                {
                    if (gy == riverBedY) return (WorldGenSettings.Blocks.Sand, 0);
                    if (gy > riverBedY && gy <= height)
                    {
                        if (riverHasWater && gy <= riverSurfY) return (WorldGenSettings.Blocks.Water, 0);
                        return (WorldGenSettings.Blocks.Air, 0);
                    }
                }

                if (gy == height)
                {
                    block = _pipeline.Layering.ChooseSurfaceBlock(height, localWater, moisture, ctx);
                }
                else
                {
                    block = _pipeline.Layering.ChooseSubsurfaceBlock(gy, height, moisture, ctx);
                    if (block.Item1 == WorldGenSettings.Blocks.Stone && gy > 0)
                    {
                        if (_pipeline.Ores.TryOreAt(gx, gy, gz, out var ore, ctx)) block = ore;
                    }
                    if (gy == 0) block = (WorldGenSettings.Blocks.Stone, 0);
                }
            }

            if (block.Item1 != 0 && block.Item1 != WorldGenSettings.Blocks.Water && _pipeline.Caves.ShouldCarve(gx, gy, gz, height, ctx)) block = (WorldGenSettings.Blocks.Air, 0);
            if ((block.Item1 == WorldGenSettings.Blocks.Dirt || block.Item1 == WorldGenSettings.Blocks.Grass) && gy <= localWater + WorldGenSettings.Terrain.UnderwaterSandBuffer) block = (WorldGenSettings.Blocks.Sand, 0);
            return block;
        }

        private static float Moisture01(int x, int z, in WorldContext ctx)
        {
            float m = GenMath.FBM2D(x * WorldGenSettings.Moisture.CoordScale, z * WorldGenSettings.Moisture.CoordScale, WorldGenSettings.Moisture.Octaves, WorldGenSettings.Moisture.Lacunarity, WorldGenSettings.Moisture.Gain, WorldGenSettings.Moisture.BaseFreq, ctx.Seed + 321);
            return GenMath.Saturate(m);
        }
    }
}
