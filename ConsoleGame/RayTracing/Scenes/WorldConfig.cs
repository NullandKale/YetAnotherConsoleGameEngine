namespace ConsoleGame.RayTracing.Scenes
{
    public sealed class WorldConfig
    {
        public readonly int ChunkSize;
        public readonly int ChunksX;
        public readonly int ChunksY;
        public readonly int ChunksZ;
        public readonly int ViewDistanceChunks;
        public readonly Vec3 WorldMin;
        public readonly Vec3 VoxelSize;
        public readonly int WorldSeed;
        public readonly int WorldWidth;
        public readonly int WorldHeight;
        public readonly int WorldDepth;
        public readonly int WaterLevel;
        public readonly int SnowLevel;

        public WorldConfig(int chunkSize, int chunksX, int chunksY, int chunksZ, int viewDistanceChunks, Vec3 worldMin, Vec3 voxelSize, int worldSeed)
        {
            ChunkSize = chunkSize;
            ChunksX = chunksX;
            ChunksY = chunksY;
            ChunksZ = chunksZ;
            ViewDistanceChunks = viewDistanceChunks;
            WorldMin = worldMin;
            VoxelSize = voxelSize;
            WorldSeed = worldSeed;
            WorldWidth = ChunksX * ChunkSize;
            WorldHeight = ChunksY * ChunkSize;
            WorldDepth = ChunksZ * ChunkSize;
            WaterLevel = Math.Max(1, WorldHeight / 4);
            SnowLevel = (int)(WorldHeight * 0.8f);
        }
    }
}
