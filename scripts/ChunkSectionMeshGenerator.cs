using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;
using static ProjectWisteria.WorldConstants;
using Array = Godot.Collections.Array;

namespace ProjectWisteria
{
    public class ChunkSectionMeshGenerator
    {
        private readonly int[] _baseBlockTriangles =
        {
            0, 1, 2, // 0
            0, 2, 3 // 1
        };

        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Color> _vertsColor = new List<Color>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<int> _tris = new List<int>();

        private readonly ShaderMaterial _material;

        private const string MaterialPath = "res://materials/block.tres";

        public ChunkSectionMeshGenerator()
        {
            _material = ResourceLoader.Load(MaterialPath) as ShaderMaterial;
        }

        private bool IsValidBlockPosInChunkSection(int x, int y, int z)
        {
            var invalid = x < 0 || x >= ChunkSectionSize || y < 0 || y >= ChunkSectionSize || z < 0 ||
                          z >= ChunkSectionSize;

            return !invalid;
        }

        public int GetNeighborBlockExists(int blockX, int blockY, int blockZ, ChunkSection section)
        {
            var index = 0;
            var neighborBlocks = 0;

            for (var y = 0; y <= 1; y++)
            {
                for (var z = -1; z <= 1; z++)
                {
                    for (var x = -1; x <= 1; x++)
                    {
                        if (x == 0 && y == 0 && z == 0) { continue; }

                        if (IsValidBlockPosInChunkSection(blockX + x, blockY + y, blockZ + z))
                        {
                            var exists = section.GetBlock(blockX + x, blockY + y, blockZ + z) != BlockType.Air;
                            if (exists)
                            {
                                neighborBlocks |= 1 << (16 - index);
                            }
                            else
                            {
                                neighborBlocks &= ~(1 << (16 - index));
                            }
                        }
                        else
                        {
                            neighborBlocks &= ~(1 << (16 - index));
                        }

                        index++;
                    }
                }
            }

            //GD.Print(Convert.ToString(neighborBlocks, 2));

            return neighborBlocks;
        }

        public int GetAmbientOcclusionLevel(bool side0, bool side1, bool corner)
        {
            if (side0 && side1) { return 0; }

            return 3 - (side0 ? 1 : 0) - (side1 ? 1 : 0) - (corner ? 1 : 0);
        }

        public void Generate(out ArrayMesh mesh, ChunkSection section)
        {
            if (section.IsOnlyAirs())
            {
                mesh = null;
                return;
            }

            for (byte blockY = 0; blockY < ChunkSectionSize; blockY++)
            {
                for (byte blockZ = 0; blockZ < ChunkSectionSize; blockZ++)
                {
                    for (byte blockX = 0; blockX < ChunkSectionSize; blockX++)
                    {
                        if (section.GetBlock(blockX, blockY, blockZ) == BlockType.Air) { continue; }

                        var neighbor = GetNeighborBlockExists(blockX, blockY, blockZ, section);

                        if (IsXpFaceVisible(blockX, blockY, blockZ, section))
                        {
                            AddXpBlockFaceElems(blockX, blockY, blockZ);
                        }

                        if (IsXnFaceVisible(blockX, blockY, blockZ, section))
                        {
                            AddXnBlockFaceElems(blockX, blockY, blockZ);
                        }

                        if (IsYpFaceVisible(blockX, blockY, blockZ, section))
                        {
                            var aoLevels = CalculateYpBlockFaceAoLevel(neighbor);

                            AddYpBlockFaceElems(blockX, blockY, blockZ, aoLevels);
                        }

                        if (IsYnFaceVisible(blockX, blockY, blockZ, section))
                        {
                            AddYnBlockFaceElems(blockX, blockY, blockZ);
                        }

                        if (IsZpFaceVisible(blockX, blockY, blockZ, section))
                        {
                            AddZpBlockFaceElems(blockX, blockY, blockZ);
                        }

                        if (IsZnFaceVisible(blockX, blockY, blockZ, section))
                        {
                            AddZnBlockFaceElems(blockX, blockY, blockZ);
                        }
                    }
                }
            }

            mesh = new ArrayMesh();

            var arrays = new Array();
            arrays.Resize((int) Mesh.ArrayType.Max);
            arrays[(int) Mesh.ArrayType.Vertex] = _verts.ToArray();
            arrays[(int) Mesh.ArrayType.Color] = _vertsColor.ToArray();
            arrays[(int) Mesh.ArrayType.TexUv] = _uvs.ToArray();
            arrays[(int) Mesh.ArrayType.Normal] = _normals.ToArray();
            arrays[(int) Mesh.ArrayType.Index] = _tris.ToArray();

            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            mesh.SurfaceSetMaterial(0, _material);

            _verts.Clear();
            _vertsColor.Clear();
            _uvs.Clear();
            _normals.Clear();
            _tris.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsXpFaceVisible(byte x, byte y, byte z, ChunkSection section)
        {
            if (x < ChunkSectionSize - 1)
            {
                return section.GetBlock((byte) (x + 1), y, z) == BlockType.Air;
            }

            if (section.XpNeighbor == null) { return true; }

            return section.XpNeighbor.GetBlock((byte) 0, y, z) == BlockType.Air;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsXnFaceVisible(byte x, byte y, byte z, ChunkSection section)
        {
            if (x > 0)
            {
                return section.GetBlock((byte) (x - 1), y, z) == BlockType.Air;
            }

            if (section.XnNeighbor == null) { return true; }

            return section.XnNeighbor.GetBlock((byte) (ChunkSectionSize - 1), y, z) == BlockType.Air;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsZpFaceVisible(byte x, byte y, byte z, ChunkSection section)
        {
            if (z < ChunkSectionSize - 1)
            {
                return section.GetBlock(x, y, (byte) (z + 1)) == BlockType.Air;
            }

            if (section.ZpNeighbor == null) { return true; }

            return section.ZpNeighbor.GetBlock(x, y, (byte) 0) == BlockType.Air;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsZnFaceVisible(byte x, byte y, byte z, ChunkSection section)
        {
            if (z > 0)
            {
                return section.GetBlock(x, y, (byte) (z - 1)) == BlockType.Air;
            }

            if (section.ZnNeighbor == null) { return true; }

            return section.ZnNeighbor.GetBlock(x, y, (byte) (ChunkSectionSize - 1)) == BlockType.Air;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsYpFaceVisible(byte x, byte y, byte z, ChunkSection section)
        {
            if (y < ChunkSectionSize - 1)
            {
                return section.GetBlock(x, (byte) (y + 1), z) == BlockType.Air;
            }

            if (section.YpNeighbor == null) { return true; }

            return section.YpNeighbor.GetBlock(x, (byte) 0, z) == BlockType.Air;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsYnFaceVisible(byte x, byte y, byte z, ChunkSection section)
        {
            if (y > 0)
            {
                return section.GetBlock(x, (byte) (y - 1), z) == BlockType.Air;
            }

            if (section.YnNeighbor == null) { return true; }

            return section.YnNeighbor.GetBlock(x, (byte) (ChunkSectionSize - 1), z) == BlockType.Air;
        }

        private void AddXpBlockFaceElems(byte x, byte y, byte z)
        {
            _verts.Add(new Vector3(x + 1, y + 1, z + 1));
            _verts.Add(new Vector3(x + 1, y + 1, z));
            _verts.Add(new Vector3(x + 1, y, z));
            _verts.Add(new Vector3(x + 1, y, z + 1));

            _vertsColor.Add(new Color(1, 1, 1, 0.3f));
            _vertsColor.Add(new Color(1, 1, 1, 0.3f));
            _vertsColor.Add(new Color(1, 1, 1, 0.3f));
            _vertsColor.Add(new Color(1, 1, 1, 0.3f));

            _normals.Add(new Vector3(1, 0, 0));
            _normals.Add(new Vector3(1, 0, 0));
            _normals.Add(new Vector3(1, 0, 0));
            _normals.Add(new Vector3(1, 0, 0));

            _uvs.Add(new Vector2(0, 0));
            _uvs.Add(new Vector2(1, 0));
            _uvs.Add(new Vector2(1, 1));
            _uvs.Add(new Vector2(0, 1));

            foreach (var triangle in _baseBlockTriangles)
            {
                _tris.Add(triangle + _verts.Count - 4);
            }
        }

        private void AddXnBlockFaceElems(byte x, byte y, byte z)
        {
            _verts.Add(new Vector3(x, y + 1, z));
            _verts.Add(new Vector3(x, y + 1, z + 1));
            _verts.Add(new Vector3(x, y, z + 1));
            _verts.Add(new Vector3(x, y, z));

            _vertsColor.Add(new Color(1, 1, 1, 0.3f));
            _vertsColor.Add(new Color(1, 1, 1, 0.3f));
            _vertsColor.Add(new Color(1, 1, 1, 0.3f));
            _vertsColor.Add(new Color(1, 1, 1, 0.3f));

            _normals.Add(new Vector3(1, 0, 0));
            _normals.Add(new Vector3(1, 0, 0));
            _normals.Add(new Vector3(1, 0, 0));
            _normals.Add(new Vector3(1, 0, 0));

            _uvs.Add(new Vector2(0, 0));
            _uvs.Add(new Vector2(1, 0));
            _uvs.Add(new Vector2(1, 1));
            _uvs.Add(new Vector2(0, 1));

            foreach (var triangle in _baseBlockTriangles)
            {
                _tris.Add(triangle + _verts.Count - 4);
            }
        }

        private void AddYpBlockFaceElems(byte x, byte y, byte z, List<int> levels)
        {
            _verts.Add(new Vector3(x, y + 1, z));
            _verts.Add(new Vector3(x + 1, y + 1, z));
            _verts.Add(new Vector3(x + 1, y + 1, z + 1));
            _verts.Add(new Vector3(x, y + 1, z + 1));

            for (var i = 0; i < 4; i++)
            {
                Color color;
                switch (levels[i])
                {
                    case 3:
                        color = new Color(1, 1, 1);
                        break;
                    case 2:
                        color = new Color(.5f, .5f, .5f);
                        break;
                    case 1:
                        color = new Color(.3f, .3f, .3f);
                        break;
                    case 0:
                        color = new Color(.15f, .15f, .15f);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                _vertsColor.Add(color);
            }

            _normals.Add(new Vector3(0, 1, 0));
            _normals.Add(new Vector3(0, 1, 0));
            _normals.Add(new Vector3(0, 1, 0));
            _normals.Add(new Vector3(0, 1, 0));

            _uvs.Add(new Vector2(0, 0));
            _uvs.Add(new Vector2(1, 0));
            _uvs.Add(new Vector2(1, 1));
            _uvs.Add(new Vector2(0, 1));

            foreach (var triangle in _baseBlockTriangles)
            {
                _tris.Add(triangle + _verts.Count - 4);
            }
        }

        private void AddYnBlockFaceElems(byte x, byte y, byte z)
        {
            _verts.Add(new Vector3(x, y, z + 1));
            _verts.Add(new Vector3(x + 1, y, z + 1));
            _verts.Add(new Vector3(x + 1, y, z));
            _verts.Add(new Vector3(x, y, z));

            _vertsColor.Add(new Color(1, 1, 1, 0.3f));
            _vertsColor.Add(new Color(1, 1, 1, 0.3f));
            _vertsColor.Add(new Color(1, 1, 1, 0.3f));
            _vertsColor.Add(new Color(1, 1, 1, 0.3f));

            _normals.Add(new Vector3(0, -1, 0));
            _normals.Add(new Vector3(0, -1, 0));
            _normals.Add(new Vector3(0, -1, 0));
            _normals.Add(new Vector3(0, -1, 0));

            _uvs.Add(new Vector2(0, 0));
            _uvs.Add(new Vector2(1, 0));
            _uvs.Add(new Vector2(1, 1));
            _uvs.Add(new Vector2(0, 1));

            foreach (var triangle in _baseBlockTriangles)
            {
                _tris.Add(triangle + _verts.Count - 4);
            }
        }

        private void AddZpBlockFaceElems(byte x, byte y, byte z)
        {
            _verts.Add(new Vector3(x, y + 1, z + 1));
            _verts.Add(new Vector3(x + 1, y + 1, z + 1));
            _verts.Add(new Vector3(x + 1, y, z + 1));
            _verts.Add(new Vector3(x, y, z + 1));

            _vertsColor.Add(new Color(1, 1, 1, 0.3f));
            _vertsColor.Add(new Color(1, 1, 1, 0.3f));
            _vertsColor.Add(new Color(1, 1, 1, 0.3f));
            _vertsColor.Add(new Color(1, 1, 1, 0.3f));

            _normals.Add(new Vector3(0, 0, 1));
            _normals.Add(new Vector3(0, 0, 1));
            _normals.Add(new Vector3(0, 0, 1));
            _normals.Add(new Vector3(0, 0, 1));

            _uvs.Add(new Vector2(0, 0));
            _uvs.Add(new Vector2(1, 0));
            _uvs.Add(new Vector2(1, 1));
            _uvs.Add(new Vector2(0, 1));

            foreach (var triangle in _baseBlockTriangles)
            {
                _tris.Add(triangle + _verts.Count - 4);
            }
        }

        private void AddZnBlockFaceElems(byte x, byte y, byte z)
        {
            _verts.Add(new Vector3(x + 1, y + 1, z));
            _verts.Add(new Vector3(x, y + 1, z));
            _verts.Add(new Vector3(x, y, z));
            _verts.Add(new Vector3(x + 1, y, z));

            _vertsColor.Add(new Color(1, 1, 1, 0.3f));
            _vertsColor.Add(new Color(1, 1, 1, 0.3f));
            _vertsColor.Add(new Color(1, 1, 1, 0.3f));
            _vertsColor.Add(new Color(1, 1, 1, 0.3f));

            _normals.Add(new Vector3(0, 0, -1));
            _normals.Add(new Vector3(0, 0, -1));
            _normals.Add(new Vector3(0, 0, -1));
            _normals.Add(new Vector3(0, 0, -1));

            _uvs.Add(new Vector2(0, 0));
            _uvs.Add(new Vector2(1, 0));
            _uvs.Add(new Vector2(1, 1));
            _uvs.Add(new Vector2(0, 1));

            foreach (var triangle in _baseBlockTriangles)
            {
                _tris.Add(triangle + _verts.Count - 4);
            }
        }

        private List<int> CalculateYpBlockFaceAoLevel(int neighbor)
        {
            var ac = ((neighbor >> 8) & 1) == 1;
            var as0 = ((neighbor >> 7) & 1) == 1;
            var as1 = ((neighbor >> 5) & 1) == 1;

            var bc = ((neighbor >> 6) & 1) == 1;
            var bs0 = ((neighbor >> 7) & 1) == 1;
            var bs1 = ((neighbor >> 3) & 1) == 1;

            var cc = ((neighbor >> 0) & 1) == 1;
            var cs0 = ((neighbor >> 1) & 1) == 1;
            var cs1 = ((neighbor >> 3) & 1) == 1;

            var dc = ((neighbor >> 2) & 1) == 1;
            var ds0 = ((neighbor >> 1) & 1) == 1;
            var ds1 = ((neighbor >> 5) & 1) == 1;

            var levels = new List<int>
            {
                GetAmbientOcclusionLevel(as0, as1, ac),
                GetAmbientOcclusionLevel(bs0, bs1, bc),
                GetAmbientOcclusionLevel(cs0, cs1, cc),
                GetAmbientOcclusionLevel(ds0, ds1, dc)
            };

            return levels;
        }
    }
}
