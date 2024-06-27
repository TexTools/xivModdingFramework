using HelixToolkit.SharpDX.Core;
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.Enums;
using xivModdingFramework.Mods.DataContainers;

namespace xivModdingFramework.Models.FileTypes
{
    internal static class MdlVertexReader
    {
        public static void ReadVertexData(BinaryReader br, MeshData meshData, int lodVertexOffset, int lodIndexOffset)
        {
            var vertexData = new VertexData();

            var info = meshData.VertexDataStructList;
            var vertexCount = meshData.MeshInfo.VertexCount;
            var indexCount = meshData.MeshInfo.IndexCount;
            var block0Offset = meshData.MeshInfo.VertexDataOffset0 + lodVertexOffset;
            var block1Offset = meshData.MeshInfo.VertexDataOffset1 + lodVertexOffset;
            var indexOffset = (meshData.MeshInfo.IndexDataOffset * 2) + lodIndexOffset;

            br.BaseStream.Seek(block0Offset, SeekOrigin.Begin);
            var block0Sorted = info.Where(x => x.DataBlock == 0).OrderBy(x => x.DataOffset);
            for(int i = 0; i < vertexCount; i++)
            {
                foreach(var entry in block0Sorted)
                {
                    ReadData(vertexData, br, entry.DataUsage, entry.DataType, entry.Count);
                }
            }

            if(br.BaseStream.Position != block0Offset + (vertexCount * meshData.MeshInfo.VertexDataEntrySize0))
            {
                throw new InvalidDataException("Vertex Data Size Mismatch. Some part(s) of the vertex data in stream 0 were not read properly.");
            }

            br.BaseStream.Seek(block1Offset, SeekOrigin.Begin);
            var block1Sorted = info.Where(x => x.DataBlock == 1).OrderBy(x => x.DataOffset);
            for (int i = 0; i < vertexCount; i++)
            {
                foreach (var entry in block1Sorted)
                {
                    ReadData(vertexData, br, entry.DataUsage, entry.DataType, entry.Count);
                }
            }

            if (br.BaseStream.Position != block1Offset + (vertexCount * meshData.MeshInfo.VertexDataEntrySize1))
            {
                throw new InvalidDataException("Vertex Data Size Mismatch. Some part(s) of the vertex data in stream 1 were not read properly.");
            }

            br.BaseStream.Seek(indexOffset, SeekOrigin.Begin);
            for (var i = 0; i < indexCount; i++)
            {
                vertexData.Indices.Add(br.ReadUInt16());
            }

            if (br.BaseStream.Position != indexOffset + (indexCount * 2))
            {
                throw new InvalidDataException("Index Size Mismatch. Some part(s) of the index data were not read properly.");
            }

            meshData.VertexData = vertexData;
        }

        public static void ReadData(VertexData data, BinaryReader br, VertexUsageType usage, VertexDataType type, int count)
        {
            if(usage == VertexUsageType.TextureCoordinate)
            {
                var r = ReadDoubleVector(br, type);
                data.TextureCoordinates0.Add(r.Vec0);
                data.TextureCoordinates1.Add(r.Vec1);
            } else if(usage == VertexUsageType.Binormal)
            {
                var r = ReadByteVector(br, type);
                data.BiNormals.Add(r.Vector);
                data.BiNormalHandedness.Add(r.Handedness);
            } else if(usage == VertexUsageType.Tangent)
            {
                var r = ReadByteVector(br, type);
                data.Tangents.Add(r.Vector);
                data.TangentHandedness.Add(r.Handedness);
            } else if(usage == VertexUsageType.Normal)
            {
                data.Normals.Add(ReadVector3(br, type));
            }
            else if (usage == VertexUsageType.Position)
            {
                data.Positions.Add(ReadVector3(br, type));
            } else if(usage == VertexUsageType.Color)
            {
                if (count == 0)
                {
                    data.Colors.Add(ReadColor(br, type));
                } else
                {
                    data.Colors2.Add(ReadColor(br, type));
                }
            } else if(usage == VertexUsageType.BoneWeight)
            {
                data.BoneWeights.Add(ReadFloatArray(br, type));
            } else if(usage == VertexUsageType.BoneIndex)
            {
                data.BoneIndices.Add(ReadByteArray(br, type));
            }
        }


        public static float[] ReadFloatArray(BinaryReader br, VertexDataType dataType)
        {
            byte[] byteValues = dataType == VertexDataType.UByte8 ? new byte[8] : new byte[4];

            if (dataType == VertexDataType.UByte8)
            {
                // Silly low => High format
                byteValues[0] = br.ReadByte();
                byteValues[4] = br.ReadByte();
                byteValues[1] = br.ReadByte();
                byteValues[5] = br.ReadByte();
                byteValues[2] = br.ReadByte();
                byteValues[6] = br.ReadByte();
                byteValues[3] = br.ReadByte();
                byteValues[7] = br.ReadByte();
            }
            else
            {
                for (int z = 0; z < byteValues.Length; z++)
                {
                    byteValues[z] = br.ReadByte();
                }
            }

            var floatValues = new float[byteValues.Length];
            for (int z = 0; z < floatValues.Length; z++)
            {
                floatValues[z] = byteValues[z] / 255f;
            }

            return floatValues;
        }

        public static byte[] ReadByteArray(BinaryReader br, VertexDataType dataType)
        {
            byte[] byteValues = dataType == VertexDataType.UByte8 ? new byte[8] : new byte[4];

            if (dataType == VertexDataType.UByte8)
            {
                // Silly low => High format
                byteValues[0] = br.ReadByte();
                byteValues[4] = br.ReadByte();
                byteValues[1] = br.ReadByte();
                byteValues[5] = br.ReadByte();
                byteValues[2] = br.ReadByte();
                byteValues[6] = br.ReadByte();
                byteValues[3] = br.ReadByte();
                byteValues[7] = br.ReadByte();
            }
            else
            {
                for (int z = 0; z < byteValues.Length; z++)
                {
                    byteValues[z] = br.ReadByte();
                }
            }

            return byteValues;
        }

        public static Vector3 ReadVector3(BinaryReader br, VertexDataType dataType)
        {
            Vector3 positionVector;
            // Position data is either stored in half-floats or singles
            if (dataType == VertexDataType.Half4)
            {
                var x = new SharpDX.Half(br.ReadUInt16());
                var y = new SharpDX.Half(br.ReadUInt16());
                var z = new SharpDX.Half(br.ReadUInt16());
                var w = new SharpDX.Half(br.ReadUInt16());

                positionVector = new Vector3(x, y, z);
            }
            else
            {
                var x = br.ReadSingle();
                var y = br.ReadSingle();
                var z = br.ReadSingle();

                positionVector = new Vector3(x, y, z);
            }

            if (float.IsNaN(positionVector.X) || float.IsInfinity(positionVector.X)
                || float.IsNaN(positionVector.Y) || float.IsInfinity(positionVector.Y)
                || float.IsNaN(positionVector.Z) || float.IsInfinity(positionVector.Z))
            {
                positionVector = new Vector3();
            }
            return positionVector;
        }

        public static (Vector3 Vector, byte Handedness) ReadByteVector(BinaryReader br, VertexDataType dataType)
        {
            var x = br.ReadByte() * 2 / 255f - 1f;
            var y = br.ReadByte() * 2 / 255f - 1f;
            var z = br.ReadByte() * 2 / 255f - 1f;
            var w = br.ReadByte();

            return (new Vector3(x, y, z), w);
        }

        public static Color ReadColor(BinaryReader br, VertexDataType dataType)
        {
            var r = br.ReadByte();
            var g = br.ReadByte();
            var b = br.ReadByte();
            var a = br.ReadByte();

            return new Color(r, g, b, a);
        }

        public static (Vector2 Vec0, Vector2 Vec1) ReadDoubleVector(BinaryReader br, VertexDataType dataType)
        {

            Vector2 tcVector0;
            Vector2 tcVector1;
            // Normal data is either stored in half-floats or singles
            if (dataType == VertexDataType.Half4)
            {
                var x = new SharpDX.Half(br.ReadUInt16());
                var y = new SharpDX.Half(br.ReadUInt16());
                var x1 = new SharpDX.Half(br.ReadUInt16());
                var y1 = new SharpDX.Half(br.ReadUInt16());

                tcVector0 = new Vector2(x, y);
                tcVector1 = new Vector2(x1, y1);
            }
            else if (dataType == VertexDataType.Half2)
            {
                var x = new SharpDX.Half(br.ReadUInt16());
                var y = new SharpDX.Half(br.ReadUInt16());

                tcVector0 = new Vector2(x, y);
                tcVector1 = Vector2.Zero;
            }
            else if (dataType == VertexDataType.Float2)
            {
                var x = br.ReadSingle();
                var y = br.ReadSingle();

                tcVector0 = new Vector2(x, y);
                tcVector1 = Vector2.Zero;
            }
            else if (dataType == VertexDataType.Float4)
            {
                var x = br.ReadSingle();
                var y = br.ReadSingle();
                var x1 = br.ReadSingle();
                var y1 = br.ReadSingle();

                tcVector0 = new Vector2(x, y);
                tcVector1 = new Vector2(x1, y1);
            }
            else
            {
                tcVector0 = Vector2.Zero;
                tcVector1 = Vector2.Zero;
            }

            return (tcVector0, tcVector1);
        }
    }
}
