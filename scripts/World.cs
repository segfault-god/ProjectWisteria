using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using ProjectWisteria.Coord;
using static ProjectWisteria.WorldConstants;

namespace ProjectWisteria
{
    // ReSharper disable once ClassNeverInstantiated.Global
    // ReSharper disable once UnusedType.Global
    public class World : Node
    {
        private readonly Dictionary<ChunkColumnCoord, ChunkColumn> _chunkCols = new();
        private readonly Dictionary<ChunkGlobalCoord, Node> _chunkNodes = new();

        private readonly Queue<ChunkGlobalCoord> _renderUpdateChunkQueue = new();

        private TerrainGenerator _terrainGenerator = null!;
        private ChunkMeshGenerator _chunkMeshGenerator = null!;

        public override void _Ready()
        {
            _terrainGenerator = new TerrainGenerator();

            _chunkMeshGenerator = new ChunkMeshGenerator();

            for (var chunkColX = -WorldSizeX / 2; chunkColX <= (WorldSizeX - 1) / 2; chunkColX++)
            {
                for (var chunkColZ = -WorldSizeZ / 2; chunkColZ <= (WorldSizeZ - 1) / 2; chunkColZ++)
                {
                    var chunkCol = new ChunkColumn(this, chunkColX, chunkColZ);

                    for (var chunkY = -ChunksNegativeInColumn; chunkY < ChunksPositiveInColumn; chunkY++)
                    {
                        var chunkGlobalCoord = new ChunkGlobalCoord(chunkColX, chunkY, chunkColZ);

                        var chunk = chunkCol.GetChunk(chunkY)!;

                        _terrainGenerator.Generate(chunk, chunkGlobalCoord);

                        var chunkNode = new Spatial
                        {
                            Translation = new Vector3(
                                chunkColX * ChunkSize,
                                chunkY * ChunkSize,
                                chunkColZ * ChunkSize
                            ),
                            Name = $"Chunk ({chunkColX}, {chunkColZ}) [{chunkY}]"
                        };

                        AddChild(chunkNode);

                        var chunkMeshNode = new MeshInstance
                        {
                            Name = "BlockMesh"
                        };
                        chunkNode.AddChild(chunkMeshNode);

                        _chunkNodes[chunkGlobalCoord] = chunkNode;
                    }

                    var chunkCoord = new ChunkColumnCoord(chunkColX, chunkColZ);
                    _chunkCols.Add(chunkCoord, chunkCol);
                }
            }
        }

        public override void _Process(float delta)
        {
            UpdateMultipleChunkRender(10);
        }

        public void UpdateChunkRender()
        {
            if (_renderUpdateChunkQueue.Count == 0) { return; }

            var globalCoord = _renderUpdateChunkQueue.Dequeue();

            var chunkCoord = new ChunkColumnCoord(globalCoord.X, globalCoord.Z);

            var chunkCol = _chunkCols[chunkCoord];
            var chunk = chunkCol.GetChunk(globalCoord.Y)!;

            _chunkMeshGenerator.Generate(out var mesh, chunk);

            ((MeshInstance) _chunkNodes[globalCoord].GetChild(0)).Mesh = mesh;

            chunk.IsRenderScheduled = false;
        }

        public void UpdateMultipleChunkRender(int timeLimitMillisec)
        {
            if (_renderUpdateChunkQueue.Count == 0) { return; }

            var stopwatch = new Stopwatch();

            while (_renderUpdateChunkQueue.Count > 0)
            {
                stopwatch.Start();

                UpdateChunkRender();

                stopwatch.Stop();

                if (stopwatch.Elapsed.TotalMilliseconds >= timeLimitMillisec)
                {
                    return;
                }
            }
        }

        public ChunkColumn? GetChunkColumn(int x, int z)
        {
            _chunkCols.TryGetValue(new ChunkColumnCoord(x, z), out var chunkCol);

            return chunkCol;
        }

        public Chunk? GetChunk(int x, int y, int z)
        {
            var chunkCol = GetChunkColumn(x, z);

            return chunkCol?.GetChunk(y);
        }

        public Chunk? GetChunk(ChunkGlobalCoord coord)
        {
            var chunkCol = GetChunkColumn(coord.X, coord.Z);

            return chunkCol?.GetChunk(coord.Y);
        }


        public BlockType GetBlock(int x, int y, int z)
        {
            GetChunkColumnCoordFromGlobalBlockCoord(x, y, z, out var chunkColX, out var chunkColZ);
            GetLocalBlockCoordFromGlobalBlockCoord(x, y, z, out var blockX, out var blockY, out var blockZ);
            GetChunkCoordFromGlobalBlockCoord(x, y, z, out var chunkY);

            var chunk = GetChunkColumn(chunkColX, chunkColZ)!.GetChunk(chunkY);

            var block = chunk.GetBlock(blockX, blockY, blockZ)!;
            return block;
        }

        public void SetBlock(int x, int y, int z, BlockType blockType)
        {
            GetChunkColumnCoordFromGlobalBlockCoord(x, y, z, out var chunkColX, out var chunkColZ);
            GetLocalBlockCoordFromGlobalBlockCoord(x, y, z, out var blockX, out var blockY, out var blockZ);
            GetChunkCoordFromGlobalBlockCoord(x, y, z, out var chunkY);

            var chunk = GetChunkColumn(chunkColX, chunkColZ)!.GetChunk(chunkY);

            chunk.SetBlock(blockX, blockY, blockZ, blockType);
        }

        public void ScheduleUpdateChunk(Chunk chunk)
        {
            var coord = new ChunkGlobalCoord(chunk.ChunkColumnX, chunk.ChunkY, chunk.ChunkColumnZ);

            _renderUpdateChunkQueue.Enqueue(coord);
        }

        public static void GetChunkColumnCoordFromGlobalBlockCoord(int x, int y, int z, out int colX, out int colZ)
        {
            colX = x >> 4;
            colZ = z >> 4;
        }

        public static void GetChunkCoordFromGlobalBlockCoord(int x, int y, int z, out int chunkY)
        {
            chunkY = y >> 4;
        }

        public static void GetLocalBlockCoordFromGlobalBlockCoord(int x, int y, int z,
            out int bx, out int by, out int bz)
        {
            bx = x & 0b1111;
            by = y & 0b1111;
            bz = z & 0b1111;
        }

        public List<AABB> GetAabb(AABB colliderAabb)
        {
            var aabbList = new List<AABB>();

            var x0 = (int) Math.Floor(colliderAabb.Position.x);
            var x1 = (int) Math.Floor(colliderAabb.End.x + 1);

            var y0 = (int) Math.Floor(colliderAabb.Position.y);
            var y1 = (int) Math.Floor(colliderAabb.End.y + 1);

            var z0 = (int) Math.Floor(colliderAabb.Position.z);
            var z1 = (int) Math.Floor(colliderAabb.End.z + 1);

            //GD.Print($"{x0} {x1} / {y0} {y1} / {z0} {z1}");

            for (var x = x0; x < x1; x++)
            {
                for (var y = y0; y < y1; y++)
                {
                    for (var z = z0; z < z1; z++)
                    {
                        if (GetBlock(x, y, z) != BlockType.Air)
                        {
                            var aabb = new AABB(x, y, z, 1, 1, 1);
                            aabbList.Add(aabb);
                        }
                    }
                }
            }

            return aabbList;
        }
    }
}
