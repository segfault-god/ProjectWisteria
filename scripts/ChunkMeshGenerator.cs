using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Godot;
using static ProjectWisteria.WorldConstants;
using Array = Godot.Collections.Array;

namespace ProjectWisteria
{
    public class ChunkMeshGenerator
    {
        private readonly int[] _baseBlockTriangles =
        {
            0, 1, 2, // 0
            0, 2, 3 // 1
        };

        private readonly int[] _baseBlockTrianglesFlipped =
        {
            0, 1, 3, // 0
            1, 2, 3 // 1
        };

        private readonly List<Vector3> _verts = new();
        private readonly List<Color> _vertsColor = new();
        private readonly List<Vector2> _uvs = new();

        // ReSharper disable once InconsistentNaming
        private readonly List<Vector2> _uv2s = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<int> _tris = new();

        private readonly ShaderMaterial _material;

        private const string MaterialPath = "res://materials/block_array.tres";

        public ChunkMeshGenerator()
        {
            _material = (ResourceLoader.Load(MaterialPath) as ShaderMaterial)!;

            _material.SetShaderParam("texture_albedo", BlockDictionary.Instance.TextureArray);
        }

        public void Generate(out ArrayMesh? mesh, Chunk chunk)
        {
            if (chunk.IsOnlyAir())
            {
                mesh = null;
                return;
            }

            {
                BitArray[]? blockExistsYnZAxisLines = null;
                BitArray[]? blockExistsY0ZAxisLines = null;
                BitArray[]? blockExistsYpZAxisLines = null;

                // Create X and Y faces
                for (var blockY = 0; blockY < ChunkSize; blockY++)
                {
                    if (blockExistsY0ZAxisLines != null) { blockExistsYnZAxisLines = blockExistsY0ZAxisLines; }
                    else
                    {
                        blockExistsYnZAxisLines = new BitArray[ChunkSize + 2];

                        for (var x = 0; x < ChunkSize + 2; x++)
                        {
                            blockExistsYnZAxisLines[x] = GetZAxisLineBlockExists(x - 1, blockY - 1, chunk);
                        }
                    }

                    if (blockExistsYpZAxisLines != null) { blockExistsY0ZAxisLines = blockExistsYpZAxisLines; }
                    else
                    {
                        blockExistsY0ZAxisLines = new BitArray[ChunkSize + 2];

                        for (var x = 0; x < ChunkSize + 2; x++)
                        {
                            blockExistsY0ZAxisLines[x] = GetZAxisLineBlockExists(x - 1, blockY, chunk);
                        }
                    }

                    blockExistsYpZAxisLines = new BitArray[ChunkSize + 2];

                    for (var x = 0; x < ChunkSize + 2; x++)
                    {
                        blockExistsYpZAxisLines[x] = GetZAxisLineBlockExists(x - 1, blockY + 1, chunk);
                    }

                    for (var blockX = 0; blockX < ChunkSize; blockX++)
                    {
                        CreateMergedFacesXy(chunk, blockX, blockY,
                            blockExistsYnZAxisLines, blockExistsY0ZAxisLines, blockExistsYpZAxisLines);
                    }
                }
            }

            {
                BitArray[]? blockExistsYnXAxisLines = null;
                BitArray[]? blockExistsY0XAxisLines = null;
                BitArray[]? blockExistsYpXAxisLines = null;

                // Create Z faces
                for (var blockY = 0; blockY < ChunkSize; blockY++)
                {
                    if (blockExistsY0XAxisLines != null) { blockExistsYnXAxisLines = blockExistsY0XAxisLines; }
                    else
                    {
                        blockExistsYnXAxisLines = new BitArray[ChunkSize + 2];

                        for (var z = 0; z < ChunkSize + 2; z++)
                        {
                            blockExistsYnXAxisLines[z] = GetXAxisLineBlockExists(z - 1, blockY - 1, chunk);
                        }
                    }

                    if (blockExistsYpXAxisLines != null) { blockExistsY0XAxisLines = blockExistsYpXAxisLines; }
                    else
                    {
                        blockExistsY0XAxisLines = new BitArray[ChunkSize + 2];

                        for (var z = 0; z < ChunkSize + 2; z++)
                        {
                            blockExistsY0XAxisLines[z] = GetXAxisLineBlockExists(z - 1, blockY, chunk);
                        }
                    }

                    blockExistsYpXAxisLines = new BitArray[ChunkSize + 2];

                    for (var z = 0; z < ChunkSize + 2; z++)
                    {
                        blockExistsYpXAxisLines[z] = GetXAxisLineBlockExists(z - 1, blockY + 1, chunk);
                    }

                    for (var blockZ = 0; blockZ < ChunkSize; blockZ++)
                    {
                        CreateMergedFaceZ(chunk, blockY, blockZ,
                            blockExistsYnXAxisLines, blockExistsY0XAxisLines, blockExistsYpXAxisLines);
                    }
                }
            }


            mesh = new ArrayMesh();

            if (_verts.Count == 0) { return; }

            var arrays = new Array();
            arrays.Resize((int) Mesh.ArrayType.Max);
            arrays[(int) Mesh.ArrayType.Vertex] = _verts.ToArray();
            arrays[(int) Mesh.ArrayType.Color] = _vertsColor.ToArray();
            arrays[(int) Mesh.ArrayType.TexUv] = _uvs.ToArray();
            arrays[(int) Mesh.ArrayType.TexUv2] = _uv2s.ToArray();
            arrays[(int) Mesh.ArrayType.Normal] = _normals.ToArray();
            arrays[(int) Mesh.ArrayType.Index] = _tris.ToArray();

            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            mesh.SurfaceSetMaterial(0, _material);

            _verts.Clear();
            _vertsColor.Clear();
            _uvs.Clear();
            _uv2s.Clear();
            _normals.Clear();
            _tris.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CreateMergedFacesXy(Chunk chunk, int startX, int startY,
            IReadOnlyList<BitArray> blockYnZAxisLines,
            IReadOnlyList<BitArray> blockY0ZAxisLines,
            IReadOnlyList<BitArray> blockYpZAxisLines)
        {
            var startBlock = BlockType.Air;

            var xpFaceStartZ = -1;
            var xnFaceStartZ = -1;
            var ypFaceStartZ = -1;
            var ynFaceStartZ = -1;

            byte[] xpFaceStartAoLevels = null!;
            byte[] xnFaceStartAoLevels = null!;
            byte[] ypFaceStartAoLevels = null!;
            byte[] ynFaceStartAoLevels = null!;

            var cx = chunk.ChunkColumnX;
            var cy = chunk.ChunkY;
            var cz = chunk.ChunkColumnZ;

            var xpNeighborChunk = chunk.World.GetChunk(cx + 1, cy, cz);
            var xnNeighborChunk = chunk.World.GetChunk(cx - 1, cy, cz);
            var ypNeighborChunk = chunk.World.GetChunk(cx, cy + 1, cz);
            var ynNeighborChunk = chunk.World.GetChunk(cx, cy - 1, cz);

            // Z-axis scanning
            for (var z = 0; z < ChunkSize; z++)
            {
                var block = chunk.GetBlock(startX, startY, z);

                if (startBlock == BlockType.Air && block == BlockType.Air) { continue; }

                var blockChanged = block != startBlock;

                var xpFaceVisible = IsXpFaceVisible(startX, startY, z, chunk, xpNeighborChunk);
                var xnFaceVisible = IsXnFaceVisible(startX, startY, z, chunk, xnNeighborChunk);
                var ypFaceVisible = IsYpFaceVisible(startX, startY, z, chunk, ypNeighborChunk);
                var ynFaceVisible = IsYnFaceVisible(startX, startY, z, chunk, ynNeighborChunk);

                var xpFaceRendered = false;
                var xnFaceRendered = false;
                var ypFaceRendered = false;
                var ynFaceRendered = false;

                if (xpFaceStartZ == -1 && block != BlockType.Air && xpFaceVisible)
                {
                    xpFaceStartZ = z;
                    xpFaceStartAoLevels =
                        GetXpFaceAmbientOcclusionLevels(z,
                            blockYnZAxisLines[startX + 2],
                            blockY0ZAxisLines[startX + 2],
                            blockYpZAxisLines[startX + 2]);
                }

                if (xnFaceStartZ == -1 && block != BlockType.Air && xnFaceVisible)
                {
                    xnFaceStartZ = z;
                    xnFaceStartAoLevels =
                        GetXnFaceAmbientOcclusionLevels(z,
                            blockYnZAxisLines[startX],
                            blockY0ZAxisLines[startX],
                            blockYpZAxisLines[startX]);
                }

                if (ypFaceStartZ == -1 && block != BlockType.Air && ypFaceVisible)
                {
                    ypFaceStartZ = z;
                    ypFaceStartAoLevels =
                        GetYpFaceAmbientOcclusionLevels(z,
                            blockYpZAxisLines[startX],
                            blockYpZAxisLines[startX + 1],
                            blockYpZAxisLines[startX + 2]);
                }

                if (ynFaceStartZ == -1 && block != BlockType.Air && ynFaceVisible)
                {
                    ynFaceStartZ = z;
                    ynFaceStartAoLevels =
                        GetYnFaceAmbientOcclusionLevels(z,
                            blockYnZAxisLines[startX],
                            blockYnZAxisLines[startX + 1],
                            blockYnZAxisLines[startX + 2]);
                }

                if (startBlock != BlockType.Air)
                {
                    if (xpFaceStartZ != -1)
                    {
                        var needRender = false;
                        var aoLevels =
                            GetXpFaceAmbientOcclusionLevels(z,
                                blockYnZAxisLines[startX + 2],
                                blockY0ZAxisLines[startX + 2],
                                blockYpZAxisLines[startX + 2]);

                        var differentAoLevels = !xpFaceStartAoLevels.SequenceEqual(aoLevels);

                        if (blockChanged || !xpFaceVisible || differentAoLevels) { needRender = true; }

                        if (needRender)
                        {
                            AddXpBlockFaceElems(startX, startY, xpFaceStartZ, z - xpFaceStartZ,
                                startBlock, xpFaceStartAoLevels);
                            xpFaceRendered = true;
                        }

                        if (differentAoLevels) { xpFaceStartAoLevels = aoLevels; }
                    }

                    if (xnFaceStartZ != -1)
                    {
                        var needRender = false;
                        var aoLevels =
                            GetXnFaceAmbientOcclusionLevels(z,
                                blockYnZAxisLines[startX],
                                blockY0ZAxisLines[startX],
                                blockYpZAxisLines[startX]);

                        var differentAoLevels = !xnFaceStartAoLevels.SequenceEqual(aoLevels);

                        if (blockChanged || !xnFaceVisible || differentAoLevels) { needRender = true; }

                        if (needRender)
                        {
                            AddXnBlockFaceElems(startX, startY, xnFaceStartZ, z - xnFaceStartZ,
                                startBlock, xnFaceStartAoLevels);
                            xnFaceRendered = true;
                        }

                        if (differentAoLevels) { xnFaceStartAoLevels = aoLevels; }
                    }

                    if (ypFaceStartZ != -1)
                    {
                        var needRender = false;
                        var aoLevels =
                            GetYpFaceAmbientOcclusionLevels(z,
                                blockYpZAxisLines[startX],
                                blockYpZAxisLines[startX + 1],
                                blockYpZAxisLines[startX + 2]);

                        var differentAoLevels = !ypFaceStartAoLevels.SequenceEqual(aoLevels);

                        if (blockChanged || !ypFaceVisible || differentAoLevels) { needRender = true; }

                        if (needRender)
                        {
                            AddYpBlockFaceElems(startX, startY, ypFaceStartZ, z - ypFaceStartZ,
                                startBlock, ypFaceStartAoLevels);
                            ypFaceRendered = true;
                        }

                        if (differentAoLevels) { ypFaceStartAoLevels = aoLevels; }
                    }

                    if (ynFaceStartZ != -1)
                    {
                        var needRender = false;
                        var aoLevels =
                            GetYnFaceAmbientOcclusionLevels(z,
                                blockYnZAxisLines[startX],
                                blockYnZAxisLines[startX + 1],
                                blockYnZAxisLines[startX + 2]);

                        var differentAoLevels = !ynFaceStartAoLevels.SequenceEqual(aoLevels);

                        if (blockChanged || !ynFaceVisible || differentAoLevels) { needRender = true; }

                        if (needRender)
                        {
                            AddYnBlockFaceElems(startX, startY, ynFaceStartZ, z - ynFaceStartZ,
                                startBlock, ynFaceStartAoLevels);
                            ynFaceRendered = true;
                        }

                        if (differentAoLevels) { ynFaceStartAoLevels = aoLevels; }
                    }
                }

                if (xpFaceRendered) { xpFaceStartZ = block == BlockType.Air || !xpFaceVisible ? -1 : z; }

                if (xnFaceRendered) { xnFaceStartZ = block == BlockType.Air || !xnFaceVisible ? -1 : z; }

                if (ypFaceRendered) { ypFaceStartZ = block == BlockType.Air || !ypFaceVisible ? -1 : z; }

                if (ynFaceRendered) { ynFaceStartZ = block == BlockType.Air || !ynFaceVisible ? -1 : z; }

                if (blockChanged) { startBlock = block; }
            }

            if (xpFaceStartZ != -1)
            {
                AddXpBlockFaceElems(startX, startY, xpFaceStartZ,
                    ChunkSize - xpFaceStartZ, startBlock, xpFaceStartAoLevels);
            }

            if (xnFaceStartZ != -1)
            {
                AddXnBlockFaceElems(startX, startY, xnFaceStartZ,
                    ChunkSize - xnFaceStartZ, startBlock, xnFaceStartAoLevels);
            }

            if (ypFaceStartZ != -1)
            {
                AddYpBlockFaceElems(startX, startY, ypFaceStartZ,
                    ChunkSize - ypFaceStartZ, startBlock, ypFaceStartAoLevels);
            }

            if (ynFaceStartZ != -1)
            {
                AddYnBlockFaceElems(startX, startY, ynFaceStartZ,
                    ChunkSize - ynFaceStartZ, startBlock, ynFaceStartAoLevels);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CreateMergedFaceZ(Chunk chunk, int startY, int startZ,
            IReadOnlyList<BitArray> blockYnXAxisLines,
            IReadOnlyList<BitArray> blockY0XAxisLines,
            IReadOnlyList<BitArray> blockYpXAxisLines)
        {
            var startBlock = BlockType.Air;

            var zpFaceStartX = -1;
            var znFaceStartX = -1;

            byte[] zpFaceStartAoLevels = null!;
            byte[] znFaceStartAoLevels = null!;

            var cx = chunk.ChunkColumnX;
            var cy = chunk.ChunkY;
            var cz = chunk.ChunkColumnZ;

            var zpNeighborChunk = chunk.World.GetChunk(cx, cy, cz + 1);
            var znNeighborChunk = chunk.World.GetChunk(cx, cy, cz - 1);

            // X-axis scanning
            for (var x = 0; x < ChunkSize; x++)
            {
                var block = chunk.GetBlock(x, startY, startZ);

                if (startBlock == BlockType.Air && block == BlockType.Air) { continue; }

                var blockChanged = block != startBlock;

                var zpFaceVisible = IsZpFaceVisible(x, startY, startZ, chunk, zpNeighborChunk);
                var znFaceVisible = IsZnFaceVisible(x, startY, startZ, chunk, znNeighborChunk);

                var zpFaceRendered = false;
                var znFaceRendered = false;

                if (zpFaceStartX == -1 && block != BlockType.Air && zpFaceVisible)
                {
                    zpFaceStartX = x;
                    zpFaceStartAoLevels =
                        GetZpFaceAmbientOcclusionLevels(x,
                            blockYnXAxisLines[startZ + 2],
                            blockY0XAxisLines[startZ + 2],
                            blockYpXAxisLines[startZ + 2]);
                }

                if (znFaceStartX == -1 && block != BlockType.Air && znFaceVisible)
                {
                    znFaceStartX = x;
                    znFaceStartAoLevels =
                        GetZnFaceAmbientOcclusionLevels(x,
                            blockYnXAxisLines[startZ],
                            blockY0XAxisLines[startZ],
                            blockYpXAxisLines[startZ]);
                }

                if (startBlock != BlockType.Air)
                {
                    if (zpFaceStartX != -1)
                    {
                        var needRender = false;
                        var aoLevels =
                            GetZpFaceAmbientOcclusionLevels(x,
                                blockYnXAxisLines[startZ + 2],
                                blockY0XAxisLines[startZ + 2],
                                blockYpXAxisLines[startZ + 2]);

                        var differentAoLevels = !zpFaceStartAoLevels.SequenceEqual(aoLevels);

                        if (blockChanged || !zpFaceVisible || differentAoLevels) { needRender = true; }

                        if (needRender)
                        {
                            AddZpBlockFaceElems(zpFaceStartX, startY, startZ, x - zpFaceStartX,
                                startBlock, zpFaceStartAoLevels);
                            zpFaceRendered = true;
                        }

                        if (differentAoLevels) { zpFaceStartAoLevels = aoLevels; }
                    }

                    if (znFaceStartX != -1)
                    {
                        var needRender = false;
                        var aoLevels =
                            GetZnFaceAmbientOcclusionLevels(x,
                                blockYnXAxisLines[startZ],
                                blockY0XAxisLines[startZ],
                                blockYpXAxisLines[startZ]);

                        var differentAoLevels = !znFaceStartAoLevels.SequenceEqual(aoLevels);

                        if (blockChanged || !znFaceVisible || differentAoLevels) { needRender = true; }

                        if (needRender)
                        {
                            AddZnBlockFaceElems(znFaceStartX, startY, startZ, x - znFaceStartX,
                                startBlock, znFaceStartAoLevels);
                            znFaceRendered = true;
                        }

                        if (differentAoLevels) { znFaceStartAoLevels = aoLevels; }
                    }
                }

                if (zpFaceRendered) { zpFaceStartX = block == BlockType.Air || !zpFaceVisible ? -1 : x; }

                if (znFaceRendered) { znFaceStartX = block == BlockType.Air || !znFaceVisible ? -1 : x; }

                if (blockChanged) { startBlock = block; }
            }

            if (zpFaceStartX != -1)
            {
                AddZpBlockFaceElems(zpFaceStartX, startY, startZ,
                    ChunkSize - zpFaceStartX, startBlock, zpFaceStartAoLevels);
            }

            if (znFaceStartX != -1)
            {
                AddZnBlockFaceElems(znFaceStartX, startY, startZ,
                    ChunkSize - znFaceStartX, startBlock, znFaceStartAoLevels);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsXpFaceVisible(int x, int y, int z, Chunk chunk, Chunk? neighborChunk)
        {
            if (x < ChunkSize - 1)
            {
                return chunk.GetBlock(x + 1, y, z) == BlockType.Air;
            }

            if (neighborChunk == null) { return false; }

            return neighborChunk.GetBlock(0, y, z) == BlockType.Air;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsXnFaceVisible(int x, int y, int z, Chunk chunk, Chunk? neighborChunk)
        {
            if (x > 0)
            {
                return chunk.GetBlock(x - 1, y, z) == BlockType.Air;
            }

            if (neighborChunk == null) { return false; }

            return neighborChunk.GetBlock(ChunkSize - 1, y, z) == BlockType.Air;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsZpFaceVisible(int x, int y, int z, Chunk chunk, Chunk? neighborChunk)
        {
            if (z < ChunkSize - 1)
            {
                return chunk.GetBlock(x, y, z + 1) == BlockType.Air;
            }

            if (neighborChunk == null) { return false; }

            return neighborChunk.GetBlock(x, y, 0) == BlockType.Air;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsZnFaceVisible(int x, int y, int z, Chunk chunk, Chunk? neighborChunk)
        {
            if (z > 0)
            {
                return chunk.GetBlock(x, y, z - 1) == BlockType.Air;
            }

            if (neighborChunk == null) { return false; }

            return neighborChunk.GetBlock(x, y, ChunkSize - 1) == BlockType.Air;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsYpFaceVisible(int x, int y, int z, Chunk chunk, Chunk? neighborChunk)
        {
            if (y < ChunkSize - 1)
            {
                return chunk.GetBlock(x, y + 1, z) == BlockType.Air;
            }

            if (neighborChunk == null) { return false; }

            return neighborChunk.GetBlock(x, 0, z) == BlockType.Air;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsYnFaceVisible(int x, int y, int z, Chunk chunk, Chunk? neighborChunk)
        {
            if (y > 0)
            {
                return chunk.GetBlock(x, y - 1, z) == BlockType.Air;
            }

            if (neighborChunk == null) { return false; }

            return neighborChunk.GetBlock(x, ChunkSize - 1, z) == BlockType.Air;
        }

        private void AddXpBlockFaceElems(int x, int y, int z, int len, BlockType block, IReadOnlyList<byte> aoLevels)
        {
            _verts.Add(new Vector3(x + 1, y + 1, z + len));
            _verts.Add(new Vector3(x + 1, y + 1, z));
            _verts.Add(new Vector3(x + 1, y, z));
            _verts.Add(new Vector3(x + 1, y, z + len));

            var flipTriangle = false;
            if (aoLevels[1] == 3 || aoLevels[3] == 3) { flipTriangle = true; }
            else if (aoLevels[0] == 3 || aoLevels[2] == 3) { }
            else { flipTriangle = aoLevels[0] + aoLevels[2] > aoLevels[1] + aoLevels[3]; }

            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[0]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[1]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[2]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[3]));

            _normals.Add(new Vector3(1, 0, 0));
            _normals.Add(new Vector3(1, 0, 0));
            _normals.Add(new Vector3(1, 0, 0));
            _normals.Add(new Vector3(1, 0, 0));

            _uvs.Add(new Vector2(0, 0));
            _uvs.Add(new Vector2(len, 0));
            _uvs.Add(new Vector2(len, 1));
            _uvs.Add(new Vector2(0, 1));

            var textureLayer = BlockDictionary.Instance.Blocks[block].XpTextureIndex;

            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));

            foreach (var triangle in flipTriangle ? _baseBlockTrianglesFlipped : _baseBlockTriangles)
            {
                _tris.Add(triangle + _verts.Count - 4);
            }
        }

        private void AddXnBlockFaceElems(int x, int y, int z, int len, BlockType block, IReadOnlyList<byte> aoLevels)
        {
            _verts.Add(new Vector3(x, y + 1, z));
            _verts.Add(new Vector3(x, y + 1, z + len));
            _verts.Add(new Vector3(x, y, z + len));
            _verts.Add(new Vector3(x, y, z));

            var flipTriangle = false;
            if (aoLevels[1] == 3 || aoLevels[3] == 3) { flipTriangle = true; }
            else if (aoLevels[0] == 3 || aoLevels[2] == 3) { }
            else { flipTriangle = aoLevels[0] + aoLevels[2] > aoLevels[1] + aoLevels[3]; }

            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[0]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[1]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[2]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[3]));

            _normals.Add(new Vector3(1, 0, 0));
            _normals.Add(new Vector3(1, 0, 0));
            _normals.Add(new Vector3(1, 0, 0));
            _normals.Add(new Vector3(1, 0, 0));

            _uvs.Add(new Vector2(0, 0));
            _uvs.Add(new Vector2(len, 0));
            _uvs.Add(new Vector2(len, 1));
            _uvs.Add(new Vector2(0, 1));

            var textureLayer = BlockDictionary.Instance.Blocks[block].XnTextureIndex;

            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));

            foreach (var triangle in flipTriangle ? _baseBlockTrianglesFlipped : _baseBlockTriangles)
            {
                _tris.Add(triangle + _verts.Count - 4);
            }
        }

        private void AddYpBlockFaceElems(int x, int y, int z, int len, BlockType block, IReadOnlyList<byte> aoLevels)
        {
            _verts.Add(new Vector3(x, y + 1, z));
            _verts.Add(new Vector3(x + 1, y + 1, z));
            _verts.Add(new Vector3(x + 1, y + 1, z + len));
            _verts.Add(new Vector3(x, y + 1, z + len));

            var flipTriangle = false;
            if (aoLevels[1] == 3 || aoLevels[3] == 3) { flipTriangle = true; }
            else if (aoLevels[0] == 3 || aoLevels[2] == 3) { }
            else { flipTriangle = aoLevels[0] + aoLevels[2] > aoLevels[1] + aoLevels[3]; }

            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[0]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[1]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[2]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[3]));

            _normals.Add(new Vector3(0, 1, 0));
            _normals.Add(new Vector3(0, 1, 0));
            _normals.Add(new Vector3(0, 1, 0));
            _normals.Add(new Vector3(0, 1, 0));

            _uvs.Add(new Vector2(0, 0));
            _uvs.Add(new Vector2(1, 0));
            _uvs.Add(new Vector2(1, len));
            _uvs.Add(new Vector2(0, len));

            var textureLayer = BlockDictionary.Instance.Blocks[block].YpTextureIndex;

            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));

            foreach (var triangle in flipTriangle ? _baseBlockTrianglesFlipped : _baseBlockTriangles)
            {
                _tris.Add(triangle + _verts.Count - 4);
            }
        }

        private void AddYnBlockFaceElems(int x, int y, int z, int len, BlockType block, IReadOnlyList<byte> aoLevels)
        {
            _verts.Add(new Vector3(x, y, z + len));
            _verts.Add(new Vector3(x + 1, y, z + len));
            _verts.Add(new Vector3(x + 1, y, z));
            _verts.Add(new Vector3(x, y, z));

            var flipTriangle = false;
            if (aoLevels[1] == 3 || aoLevels[3] == 3) { flipTriangle = true; }
            else if (aoLevels[0] == 3 || aoLevels[2] == 3) { }
            else { flipTriangle = aoLevels[0] + aoLevels[2] > aoLevels[1] + aoLevels[3]; }

            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[0]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[1]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[2]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[3]));

            _normals.Add(new Vector3(0, -1, 0));
            _normals.Add(new Vector3(0, -1, 0));
            _normals.Add(new Vector3(0, -1, 0));
            _normals.Add(new Vector3(0, -1, 0));

            _uvs.Add(new Vector2(0, 0));
            _uvs.Add(new Vector2(1, 0));
            _uvs.Add(new Vector2(1, len));
            _uvs.Add(new Vector2(0, len));

            var textureLayer = BlockDictionary.Instance.Blocks[block].YnTextureIndex;

            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));

            foreach (var triangle in flipTriangle ? _baseBlockTrianglesFlipped : _baseBlockTriangles)
            {
                _tris.Add(triangle + _verts.Count - 4);
            }
        }

        private void AddZpBlockFaceElems(int x, int y, int z, int len, BlockType block, IReadOnlyList<byte> aoLevels)
        {
            _verts.Add(new Vector3(x, y + 1, z + 1));
            _verts.Add(new Vector3(x + len, y + 1, z + 1));
            _verts.Add(new Vector3(x + len, y, z + 1));
            _verts.Add(new Vector3(x, y, z + 1));

            var flipTriangle = false;
            if (aoLevels[1] == 3 || aoLevels[3] == 3) { flipTriangle = true; }
            else if (aoLevels[0] == 3 || aoLevels[2] == 3) { }
            else { flipTriangle = aoLevels[0] + aoLevels[2] > aoLevels[1] + aoLevels[3]; }

            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[0]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[1]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[2]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[3]));

            _normals.Add(new Vector3(0, 0, 1));
            _normals.Add(new Vector3(0, 0, 1));
            _normals.Add(new Vector3(0, 0, 1));
            _normals.Add(new Vector3(0, 0, 1));

            _uvs.Add(new Vector2(0, 0));
            _uvs.Add(new Vector2(len, 0));
            _uvs.Add(new Vector2(len, 1));
            _uvs.Add(new Vector2(0, 1));

            var textureLayer = BlockDictionary.Instance.Blocks[block].ZpTextureIndex;

            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));

            foreach (var triangle in flipTriangle ? _baseBlockTrianglesFlipped : _baseBlockTriangles)
            {
                _tris.Add(triangle + _verts.Count - 4);
            }
        }

        private void AddZnBlockFaceElems(int x, int y, int z, int len, BlockType block, IReadOnlyList<byte> aoLevels)
        {
            _verts.Add(new Vector3(x + len, y + 1, z));
            _verts.Add(new Vector3(x, y + 1, z));
            _verts.Add(new Vector3(x, y, z));
            _verts.Add(new Vector3(x + len, y, z));

            var flipTriangle = false;
            if (aoLevels[1] == 3 || aoLevels[3] == 3) { flipTriangle = true; }
            else if (aoLevels[0] == 3 || aoLevels[2] == 3) { }
            else { flipTriangle = aoLevels[0] + aoLevels[2] > aoLevels[1] + aoLevels[3]; }

            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[0]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[1]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[2]));
            _vertsColor.Add(GetAmbientOcclusionColor(aoLevels[3]));

            _normals.Add(new Vector3(0, 0, -1));
            _normals.Add(new Vector3(0, 0, -1));
            _normals.Add(new Vector3(0, 0, -1));
            _normals.Add(new Vector3(0, 0, -1));

            _uvs.Add(new Vector2(0, 0));
            _uvs.Add(new Vector2(len, 0));
            _uvs.Add(new Vector2(len, 1));
            _uvs.Add(new Vector2(0, 1));

            var textureLayer = BlockDictionary.Instance.Blocks[block].ZnTextureIndex;

            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));
            _uv2s.Add(new Vector2(textureLayer, 0));

            foreach (var triangle in flipTriangle ? _baseBlockTrianglesFlipped : _baseBlockTriangles)
            {
                _tris.Add(triangle + _verts.Count - 4);
            }
        }

        public static BitArray GetZAxisLineBlockExists(int blockX, int blockY, Chunk chunk)
        {
            var results = new BitArray(ChunkSize + 2, false);

            var cxOffset = 0;
            var cyOffset = 0;

            var bx = blockX;
            var by = blockY;

            switch (blockX)
            {
                case -1:
                    cxOffset = -1;
                    bx = ChunkSize - 1;
                    break;
                case ChunkSize:
                    cxOffset = 1;
                    bx = 0;
                    break;
            }

            switch (blockY)
            {
                case -1:
                    cyOffset = -1;
                    by = ChunkSize - 1;
                    break;
                case ChunkSize:
                    cyOffset = 1;
                    by = 0;
                    break;
            }

            var cx = chunk.ChunkColumnX;
            var cy = chunk.ChunkY;
            var cz = chunk.ChunkColumnZ;

            var chunkA = chunk.World.GetChunk(cx + cxOffset, cy + cyOffset, cz - 1);
            var chunkB = chunk.World.GetChunk(cx + cxOffset, cy + cyOffset, cz);
            var chunkC = chunk.World.GetChunk(cx + cxOffset, cy + cyOffset, cz + 1);

            if (chunkA != null)
            {
                results[0] = chunkA.GetBlock(bx, by, ChunkSize - 1) != BlockType.Air;
            }

            if (chunkC != null)
            {
                results[ChunkSize + 1] = chunkC.GetBlock(bx, by, 0) != BlockType.Air;
            }

            if (chunkB != null)
            {
                for (var z = 0; z < ChunkSize; z++)
                {
                    results[z + 1] = chunkB.GetBlock(bx, by, z) != BlockType.Air;
                }
            }

            return results;
        }

        public static BitArray GetXAxisLineBlockExists(int blockZ, int blockY, Chunk chunk)
        {
            var results = new BitArray(ChunkSize + 2, false);

            var czOffset = 0;
            var cyOffset = 0;

            var bz = blockZ;
            var by = blockY;

            switch (blockZ)
            {
                case -1:
                    czOffset = -1;
                    bz = ChunkSize - 1;
                    break;
                case ChunkSize:
                    czOffset = 1;
                    bz = 0;
                    break;
            }

            switch (blockY)
            {
                case -1:
                    cyOffset = -1;
                    by = ChunkSize - 1;
                    break;
                case ChunkSize:
                    cyOffset = 1;
                    by = 0;
                    break;
            }

            var cx = chunk.ChunkColumnX;
            var cy = chunk.ChunkY;
            var cz = chunk.ChunkColumnZ;

            var chunkA = chunk.World.GetChunk(cx - 1, cy + cyOffset, cz + czOffset);
            var chunkB = chunk.World.GetChunk(cx, cy + cyOffset, cz + czOffset);
            var chunkC = chunk.World.GetChunk(cx + 1, cy + cyOffset, cz + czOffset);

            if (chunkA != null)
            {
                results[0] = chunkA.GetBlock(ChunkSize - 1, by, bz) != BlockType.Air;
            }

            if (chunkC != null)
            {
                results[ChunkSize + 1] = chunkC.GetBlock(0, by, bz) != BlockType.Air;
            }

            if (chunkB != null)
            {
                for (var x = 0; x < ChunkSize; x++)
                {
                    results[x + 1] = chunkB.GetBlock(x, by, bz) != BlockType.Air;
                }
            }

            return results;
        }

        private static byte GetAmbientOcclusionLevel(bool side0, bool side1, bool corner)
        {
            if (side0 && side1) { return 3; }

            return (byte) ((side0 ? 1 : 0) + (side1 ? 1 : 0) + (corner ? 1 : 0));
        }

        private static Color GetAmbientOcclusionColor(int level)
        {
            var color = level switch
            {
                3 => new Color(.2f, .2f, .2f),
                2 => new Color(.35f, .35f, .35f),
                1 => new Color(.5f, .5f, .5f),
                0 => new Color(1, 1, 1),
                _ => throw new ArgumentOutOfRangeException()
            };

            return color;
        }

        private static byte[] GetXpFaceAmbientOcclusionLevels(int z,
            BitArray neighborXpYn, BitArray neighborXpY0, BitArray neighborXpYp)
        {
            var aoLevels = new[]
            {
                GetAmbientOcclusionLevel(
                    neighborXpY0[z + 2], neighborXpYp[z + 1], neighborXpYp[z + 2]),
                GetAmbientOcclusionLevel(
                    neighborXpY0[z], neighborXpYp[z + 1], neighborXpYp[z]),
                GetAmbientOcclusionLevel(
                    neighborXpY0[z], neighborXpYn[z + 1], neighborXpYn[z]),
                GetAmbientOcclusionLevel(
                    neighborXpY0[z + 2], neighborXpYn[z + 1], neighborXpYn[z + 2])
            };

            return aoLevels;
        }

        private static byte[] GetXnFaceAmbientOcclusionLevels(int z,
            BitArray neighborXnYn, BitArray neighborXnY0, BitArray neighborXnYp)
        {
            var aoLevels = new[]
            {
                GetAmbientOcclusionLevel(
                    neighborXnY0[z], neighborXnYp[z + 1], neighborXnYp[z]),
                GetAmbientOcclusionLevel(
                    neighborXnY0[z + 2], neighborXnYp[z + 1], neighborXnYp[z + 2]),
                GetAmbientOcclusionLevel(
                    neighborXnY0[z + 2], neighborXnYn[z + 1], neighborXnYn[z + 2]),
                GetAmbientOcclusionLevel(
                    neighborXnY0[z], neighborXnYn[z + 1], neighborXnYn[z])
            };

            return aoLevels;
        }

        private static byte[] GetYpFaceAmbientOcclusionLevels(int z,
            BitArray neighborXnYp, BitArray neighborX0Yp, BitArray neighborXpYp)
        {
            var aoLevels = new[]
            {
                GetAmbientOcclusionLevel(
                    neighborXnYp[z + 1], neighborX0Yp[z], neighborXnYp[z]),
                GetAmbientOcclusionLevel(
                    neighborXpYp[z + 1], neighborX0Yp[z], neighborXpYp[z]),
                GetAmbientOcclusionLevel(
                    neighborXpYp[z + 1], neighborX0Yp[z + 2], neighborXpYp[z + 2]),
                GetAmbientOcclusionLevel(
                    neighborXnYp[z + 1], neighborX0Yp[z + 2], neighborXnYp[z + 2])
            };

            return aoLevels;
        }

        private static byte[] GetYnFaceAmbientOcclusionLevels(int z,
            BitArray neighborXnYn, BitArray neighborX0Yn, BitArray neighborXpYn)
        {
            var aoLevels = new[]
            {
                GetAmbientOcclusionLevel(
                    neighborXnYn[z + 1], neighborX0Yn[z + 2], neighborXnYn[z + 2]),
                GetAmbientOcclusionLevel(
                    neighborXpYn[z + 1], neighborX0Yn[z + 2], neighborXpYn[z + 2]),
                GetAmbientOcclusionLevel(
                    neighborXpYn[z + 1], neighborX0Yn[z], neighborXpYn[z]),
                GetAmbientOcclusionLevel(
                    neighborXnYn[z + 1], neighborX0Yn[z], neighborXnYn[z])
            };

            return aoLevels;
        }

        private static byte[] GetZpFaceAmbientOcclusionLevels(int x,
            BitArray neighborZpYn, BitArray neighborZpY0, BitArray neighborZpYp)
        {
            var aoLevels = new[]
            {
                GetAmbientOcclusionLevel(
                    neighborZpY0[x], neighborZpYp[x + 1], neighborZpYp[x]),
                GetAmbientOcclusionLevel(
                    neighborZpY0[x + 2], neighborZpYp[x + 1], neighborZpYp[x + 2]),
                GetAmbientOcclusionLevel(
                    neighborZpY0[x + 2], neighborZpYn[x + 1], neighborZpYn[x + 2]),
                GetAmbientOcclusionLevel(
                    neighborZpY0[x], neighborZpYn[x + 1], neighborZpYn[x])
            };

            return aoLevels;
        }


        private static byte[] GetZnFaceAmbientOcclusionLevels(int x,
            BitArray neighborZnYn, BitArray neighborZnY0, BitArray neighborZnYp)
        {
            var aoLevels = new[]
            {
                GetAmbientOcclusionLevel(
                    neighborZnY0[x + 2], neighborZnYp[x + 1], neighborZnYp[x + 2]),
                GetAmbientOcclusionLevel(
                    neighborZnY0[x], neighborZnYp[x + 1], neighborZnYp[x]),
                GetAmbientOcclusionLevel(
                    neighborZnY0[x], neighborZnYn[x + 1], neighborZnYn[x]),
                GetAmbientOcclusionLevel(
                    neighborZnY0[x + 2], neighborZnYn[x + 1], neighborZnYn[x + 2])
            };

            return aoLevels;
        }
    }
}
