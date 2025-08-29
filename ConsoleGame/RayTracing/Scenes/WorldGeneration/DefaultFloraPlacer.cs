namespace ConsoleGame.RayTracing.Scenes.WorldGeneration
{
    internal sealed class DefaultFloraPlacer : IFloraPlacer
    {
        public void TryPlaceFeaturesInChunk(int cx, int cy, int cz, WorldConfig cfg, int[,] hCache, int[,] wCache, float[,] moistCache, bool[,] riverCoreCache, (int, int)[,,] cells, ref bool anySolid)
        {
            int baseX = cx * cfg.ChunkSize;
            int baseY = cy * cfg.ChunkSize;
            int baseZ = cz * cfg.ChunkSize;
            int size = cfg.ChunkSize;
            for (int lx = 0; lx < size; lx++)
            {
                int gx = baseX + lx;
                for (int lz = 0; lz < size; lz++)
                {
                    int gz = baseZ + lz;
                    int groundHeight = hCache[lx, lz];
                    int localWater = wCache[lx, lz];
                    bool nearRiver = riverCoreCache[lx, lz];
                    if (groundHeight / size != cy) continue;
                    if (!(groundHeight > 0 && groundHeight < cfg.SnowLevel && groundHeight > localWater && !nearRiver)) continue;
                    int localGroundY = groundHeight - baseY;
                    if (localGroundY < 0 || localGroundY >= size) continue;
                    if (cells[lx, localGroundY, lz].Item1 != WorldGenSettings.Blocks.Grass) continue;
                    float moist = moistCache[lx, lz];
                    if (moist <= WorldGenSettings.Biomes.TreeMinMoisture) continue;
                    int cellSize = moist > 0.8f ? WorldGenSettings.Vegetation.DenseForestCell : moist > 0.6f ? WorldGenSettings.Vegetation.MoistForestCell : WorldGenSettings.Vegetation.SparseForestCell;
                    if (!BlueNoiseTreeCandidate(gx, gz, cellSize, cfg.WorldSeed)) continue;
                    float slope = GenMath.LocalSlope01(hCache, lx, lz, size);
                    if (slope >= WorldGenSettings.Vegetation.MaxSlopeForTree) continue;
                    if (lx < WorldGenSettings.Vegetation.BorderMargin || lx > size - 1 - WorldGenSettings.Vegetation.BorderMargin || lz < WorldGenSettings.Vegetation.BorderMargin || lz > size - 1 - WorldGenSettings.Vegetation.BorderMargin) continue;
                    int typeSel;
                    if (groundHeight > cfg.SnowLevel - (int)WorldGenSettings.Biomes.HighAltitudePineBiasDelta) typeSel = 1;
                    else if (moist > 0.8f) typeSel = 0;
                    else typeSel = GenMath.FastHash(gx, groundHeight, gz, cfg.WorldSeed) >> 4 & 1;
                    if (typeSel == 0)
                    {
                        int trunkHeight = WorldGenSettings.Vegetation.BroadleafBaseHeight + GenMath.FastHash(gx, groundHeight, gz, cfg.WorldSeed) % WorldGenSettings.Vegetation.BroadleafRandomHeight;
                        if (groundHeight + trunkHeight + WorldGenSettings.Vegetation.BroadleafLayers >= baseY + size) continue;
                        for (int h = 0; h < trunkHeight; h++)
                        {
                            int trunkY = localGroundY + h;
                            cells[lx, trunkY, lz] = (WorldGenSettings.Blocks.Wood, 0);
                        }
                        int topY = localGroundY + trunkHeight - 1;
                        for (int ly = 0; ly < WorldGenSettings.Vegetation.BroadleafLayers; ly++)
                        {
                            for (int lxOff = -WorldGenSettings.Vegetation.BroadleafRadius; lxOff <= WorldGenSettings.Vegetation.BroadleafRadius; lxOff++)
                            {
                                for (int lzOff = -WorldGenSettings.Vegetation.BroadleafRadius; lzOff <= WorldGenSettings.Vegetation.BroadleafRadius; lzOff++)
                                {
                                    int leafX = lx + lxOff;
                                    int leafY = topY + ly;
                                    int leafZ = lz + lzOff;
                                    if (leafX < 0 || leafX >= size || leafY < 0 || leafY >= size || leafZ < 0 || leafZ >= size) continue;
                                    if (lxOff == 0 && lzOff == 0 && ly == 0) continue;
                                    if (MathF.Abs(lxOff) == WorldGenSettings.Vegetation.BroadleafRadius && MathF.Abs(lzOff) == WorldGenSettings.Vegetation.BroadleafRadius && ly == WorldGenSettings.Vegetation.BroadleafLayers - 1) continue;
                                    if (cells[leafX, leafY, leafZ].Item1 == WorldGenSettings.Blocks.Air) cells[leafX, leafY, leafZ] = (WorldGenSettings.Blocks.Leaves, 0);
                                }
                            }
                        }
                        anySolid = true;
                    }
                    else
                    {
                        int trunkHeight = WorldGenSettings.Vegetation.ConiferBaseHeight + GenMath.FastHash(gx + 31, groundHeight, gz - 17, cfg.WorldSeed) % WorldGenSettings.Vegetation.ConiferRandomHeight;
                        if (groundHeight + trunkHeight + WorldGenSettings.Vegetation.ConiferLayers >= baseY + size) continue;
                        for (int h = 0; h < trunkHeight; h++)
                        {
                            int trunkY = localGroundY + h;
                            cells[lx, trunkY, lz] = (WorldGenSettings.Blocks.Wood, 0);
                        }
                        for (int ly = 0; ly < WorldGenSettings.Vegetation.ConiferLayers; ly++)
                        {
                            int radius = Math.Max(0, WorldGenSettings.Vegetation.ConiferBaseRadius - ly);
                            int y = localGroundY + trunkHeight - 1 + ly;
                            for (int rx = -radius; rx <= radius; rx++)
                            {
                                for (int rz = -radius; rz <= radius; rz++)
                                {
                                    if (MathF.Abs(rx) + MathF.Abs(rz) > radius + (ly == WorldGenSettings.Vegetation.ConiferLayers - 1 ? 0 : 1)) continue;
                                    int px = lx + rx;
                                    int pz = lz + rz;
                                    if (px < 0 || px >= size || pz < 0 || pz >= size || y < 0 || y >= size) continue;
                                    if (cells[px, y, pz].Item1 == WorldGenSettings.Blocks.Air) cells[px, y, pz] = (WorldGenSettings.Blocks.Leaves, 0);
                                }
                            }
                        }
                        anySolid = true;
                    }
                }
            }
        }

        private static bool BlueNoiseTreeCandidate(int gx, int gz, int cell, int seed)
        {
            int cx = (int)MathF.Floor((float)gx / cell);
            int cz = (int)MathF.Floor((float)gz / cell);
            int jx = Math.Abs(GenMath.FastHash(cx, 0, cz, seed + WorldGenSettings.BlueNoise.JitterSeedOffsetX)) % cell;
            int jz = Math.Abs(GenMath.FastHash(cx, 1, cz, seed + WorldGenSettings.BlueNoise.JitterSeedOffsetZ)) % cell;
            int px = cx * cell + jx;
            int pz = cz * cell + jz;
            return gx == px && gz == pz;
        }
    }
}
