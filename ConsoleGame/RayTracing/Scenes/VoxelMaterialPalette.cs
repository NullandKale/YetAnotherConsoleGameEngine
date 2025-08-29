using System.Collections.Concurrent;
using ConsoleGame.RayTracing.Scenes.WorldGeneration;

namespace ConsoleGame.RayTracing.Scenes
{
    public static class VoxelMaterialPalette
    {
        // Fixed 16-color palette (indexable); keep exactly in one place.
        private static readonly Vec3[] Palette16 = new Vec3[]
        {
            new Vec3(0.00,0.00,0.00),  //  0: black
            new Vec3(0.00,0.00,0.50),  //  1: dark blue
            new Vec3(0.00,0.50,0.00),  //  2: dark green
            new Vec3(0.00,0.50,0.50),  //  3: teal
            new Vec3(0.50,0.00,0.00),  //  4: dark red
            new Vec3(0.50,0.00,0.50),  //  5: purple
            new Vec3(0.50,0.50,0.00),  //  6: olive
            new Vec3(0.75,0.75,0.75),  //  7: light gray
            new Vec3(0.50,0.50,0.50),  //  8: gray
            new Vec3(0.00,0.00,1.00),  //  9: blue (water)
            new Vec3(0.00,1.00,0.00),  // 10: green
            new Vec3(0.00,1.00,1.00),  // 11: cyan
            new Vec3(1.00,0.00,0.00),  // 12: red
            new Vec3(1.00,0.00,1.00),  // 13: magenta
            new Vec3(1.00,1.00,0.00),  // 14: yellow
            new Vec3(1.00,1.00,1.00)   // 15: white
        };

        private static Material PalMat(int i)
        {
            var c = Palette16[i];
            return new Material(c, 0.05, 0.00, new Vec3(0.00, 0.00, 0.00));
        }

        public static readonly Func<int, int, Material> MaterialLookup = (id, meta) =>
        {
            var key = Normalize(id, meta);
            return Cache.GetOrAdd(key, CreateMaterial);
        };

        private static readonly ConcurrentDictionary<(int id, int meta), Material> Cache = new ConcurrentDictionary<(int id, int meta), Material>();

        static VoxelMaterialPalette()
        {
            Prewarm();
        }

        private static (int id, int meta) Normalize(int id, int meta)
        {
            switch (id)
            {
                case WorldGenSettings.Blocks.Air: return (0, 0);
                case WorldGenSettings.Blocks.Stone: return (1, 0);
                case WorldGenSettings.Blocks.Dirt: return (2, 0);
                case WorldGenSettings.Blocks.Grass: return (3, 0);
                case WorldGenSettings.Blocks.Water: return (4, 0);
                case WorldGenSettings.Blocks.Sand: return (5, 0);
                case WorldGenSettings.Blocks.Wood: return (6, 0);
                case WorldGenSettings.Blocks.Leaves: return (7, 0);
                case WorldGenSettings.Blocks.Snow: return (8, 0);
                case WorldGenSettings.Blocks.Ore: return (9, Clamp(meta, 0, 2));
                case WorldGenSettings.Blocks.TallGrass: return (10, 0);
                case WorldGenSettings.Blocks.Flower: return (11, 0);
                default: return (1, 0);
            }
        }

        private static Material CreateMaterial((int id, int meta) key)
        {
            switch (key.id)
            {
                case 0: return PalMat(0);
                case 1: return PalMat(8);
                case 2: return PalMat(6);
                case 3: return PalMat(10);
                case 4: return PalMat(9);   // water
                case 5: return PalMat(14);
                case 6: return PalMat(4);
                case 7: return PalMat(2);
                case 8: return PalMat(15);
                case 9:
                    switch (key.meta)
                    {
                        case 0: return PalMat(0);
                        case 1: return PalMat(7);
                        default: return PalMat(14);
                    }
                case 10: return PalMat(10);
                case 11: return PalMat(12);
                default: return PalMat(7);
            }
        }

        private static void Prewarm()
        {
            _ = MaterialLookup(WorldGenSettings.Blocks.Stone, 0);
            _ = MaterialLookup(WorldGenSettings.Blocks.Dirt, 0);
            _ = MaterialLookup(WorldGenSettings.Blocks.Grass, 0);
            _ = MaterialLookup(WorldGenSettings.Blocks.Water, 0);
            _ = MaterialLookup(WorldGenSettings.Blocks.Sand, 0);
            _ = MaterialLookup(WorldGenSettings.Blocks.Wood, 0);
            _ = MaterialLookup(WorldGenSettings.Blocks.Leaves, 0);
            _ = MaterialLookup(WorldGenSettings.Blocks.Snow, 0);
            _ = MaterialLookup(WorldGenSettings.Blocks.Ore, 0);
            _ = MaterialLookup(WorldGenSettings.Blocks.Ore, 1);
            _ = MaterialLookup(WorldGenSettings.Blocks.Ore, 2);
            _ = MaterialLookup(WorldGenSettings.Blocks.TallGrass, 0);
            _ = MaterialLookup(WorldGenSettings.Blocks.Flower, 0);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
