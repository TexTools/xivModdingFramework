// xivModdingFramework
// Copyright © 2018 Rafael Gonzalez - All Rights Reserved
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SharpDX;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.Enums;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;
using BoundingBox = xivModdingFramework.Models.DataContainers.BoundingBox;

namespace xivModdingFramework.Models.FileTypes
{
    public class Mdl
    {
        private const string MdlExtension = ".mdl";
        private readonly DirectoryInfo _gameDirectory;
        private readonly XivDataFile _dataFile;

        public Mdl(DirectoryInfo gameDirectory, XivDataFile dataFile)
        {
            _gameDirectory = gameDirectory;
            _dataFile = dataFile;
        }

        public XivMdl GetMdlData(IItemModel itemModel, XivRace xivRace)
        {

            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);

            var itemType = ItemType.GetItemType(itemModel);

            var mdlPath = GetMdlPath(itemModel, xivRace, itemType);

            var offset = index.GetDataOffset(HashGenerator.GetHash(mdlPath.Folder), HashGenerator.GetHash(mdlPath.File),
                _dataFile);

            var mdlData = dat.GetType3Data(offset, _dataFile);

            var xivMdl = new XivMdl {MdlPath = mdlPath};

            using (var br = new BinaryReader(new MemoryStream(mdlData.Data)))
            {
                // We skip the Vertex Data Structures for now
                // This is done so that we can get the correct number of meshes per LoD first
                br.BaseStream.Seek(64 + 136 * mdlData.MeshCount + 4, SeekOrigin.Begin);

                var mdlPathData = new MdlPathData()
                {
                    PathCount     = br.ReadInt32(),
                    PathBlockSize = br.ReadInt32(),
                    AttributeList = new List<string>(),
                    BoneList      = new List<string>(),
                    MaterialList  = new List<string>()
                };

                // Get the entire path string block to parse later
                // This will be done when we obtain the path counts for each type
                var pathBlock = br.ReadBytes(mdlPathData.PathBlockSize);

                var mdlModelData = new MdlModelData()
                {
                    Unknown0            = br.ReadInt32(),
                    MeshCount           = br.ReadInt16(),
                    AttributeCount      = br.ReadInt16(),
                    MeshPartCount       = br.ReadInt16(),
                    MaterialCount       = br.ReadInt16(),
                    BoneCount           = br.ReadInt16(),
                    BoneListCount       = br.ReadInt16(),
                    MeshHiderInfoCount  = br.ReadInt16(),
                    MeshHiderDataCount  = br.ReadInt16(),
                    MeshHiderIndexCount = br.ReadInt16(),
                    Unknown1            = br.ReadInt16(),
                    Unknown2            = br.ReadInt16(),
                    Unknown3            = br.ReadInt16(),
                    Unknown4            = br.ReadInt16(),
                    Unknown5            = br.ReadInt16(),
                    Unknown6            = br.ReadInt16(),
                    Unknown7            = br.ReadInt16(),
                    Unknown8            = br.ReadInt16(),
                    Unknown9            = br.ReadInt16(),
                    Unknown10           = br.ReadInt16(),
                    Unknown11           = br.ReadInt16(),
                    Unknown12           = br.ReadInt16(),
                    Unknown13           = br.ReadInt16(),
                    Unknown14           = br.ReadInt16(),
                    Unknown15           = br.ReadInt16(),
                    Unknown16           = br.ReadInt16(),
                    Unknown17           = br.ReadInt16()
                };

                // Finished reading all MdlModelData
                // Adding to xivMdl
                xivMdl.ModelData = mdlModelData;

                // Now that we have the path counts wee can parse the path strings
                using (var br1 = new BinaryReader(new MemoryStream(pathBlock)))
                {
                    // Attribute Paths
                    for (var i = 0; i < mdlModelData.AttributeCount; i++)
                    {
                        // Because we don't know the length of the string, we read the data until we reach a 0 value
                        // That 0 value is the space between strings
                        byte a;
                        var atrName = new List<byte>();
                        while ((a = br1.ReadByte()) != 0)
                        {
                            atrName.Add(a);
                        }

                        // Read the string from the byte array and remove null terminators
                        var atr = Encoding.ASCII.GetString(atrName.ToArray()).Replace("\0", "");

                        // Add the attribute to the list
                        mdlPathData.AttributeList.Add(atr);
                    }

                    // Bone Paths
                    for (var i = 0; i < mdlModelData.BoneCount; i++)
                    {
                        byte a;
                        var boneName = new List<byte>();
                        while ((a = br1.ReadByte()) != 0)
                        {
                            boneName.Add(a);
                        }

                        var bone = Encoding.ASCII.GetString(boneName.ToArray()).Replace("\0", "");

                        mdlPathData.BoneList.Add(bone);
                    }

                    // Material Paths
                    for (var i = 0; i < mdlModelData.MaterialCount; i++)
                    {
                        byte a;
                        var materialName = new List<byte>();
                        while ((a = br1.ReadByte()) != 0)
                        {
                            materialName.Add(a);
                        }

                        var bone = Encoding.ASCII.GetString(materialName.ToArray()).Replace("\0", "");

                        mdlPathData.MaterialList.Add(bone);
                    }
                }

                // Finished reading all Path Data
                // Adding to xivMdl
                xivMdl.PathData = mdlPathData;

                // Currently Unknown Data
                var unkData0 = new UnknownData0
                {
                    Unknown = br.ReadBytes(mdlModelData.Unknown2 * 32)
                };

                // Finished reading all UnknownData0
                // Adding to xivMdl
                xivMdl.UnkData0 = unkData0;

                // We add each LoD to the list
                // Note: There is always 3 LoD
                xivMdl.LoDList = new List<LevelOfDetail>();
                for (var i = 0; i < 3; i++)
                {
                    var lod = new LevelOfDetail
                    {
                        MeshOffset       = br.ReadInt16(),
                        MeshCount        = br.ReadInt16(),
                        Unknown0         = br.ReadInt32(),
                        Unknown1         = br.ReadInt32(),
                        Unknown2         = br.ReadInt32(),
                        Unknown3         = br.ReadInt32(),
                        Unknown4         = br.ReadInt32(),
                        Unknown5         = br.ReadInt32(),
                        Unknown6         = br.ReadInt32(),
                        IndexDataStart   = br.ReadInt32(),
                        Unknown7         = br.ReadInt32(),
                        Unknown8         = br.ReadInt32(),
                        VertexDataSize   = br.ReadInt32(),
                        IndexDataSize    = br.ReadInt32(),
                        VertexDataOffset = br.ReadInt32(),
                        IndexDataOffset  = br.ReadInt32(),
                        MeshDataList     = new List<MeshData>()
                    };
                    // Finished reading LoD

                    //Adding to xivMdl
                    xivMdl.LoDList.Add(lod);
                }

                // Now that we have the LoD data, we can go back and read the Vertex Data Structures
                // First we save our current position
                var savePosition = br.BaseStream.Position;

                // for each mesh in each lod
                for (var i = 0; i < xivMdl.LoDList.Count; i++)
                {
                    for (var j = 0; j < xivMdl.LoDList[i].MeshCount; j++)
                    {
                        xivMdl.LoDList[i].MeshDataList.Add(new MeshData());
                        xivMdl.LoDList[i].MeshDataList[j].VertexDataStructList = new List<VertexDataStruct>();

                        // LoD Index * Vertex Data Structure size + Header
                        br.BaseStream.Seek(i * 136 + 68, SeekOrigin.Begin);

                        // If the first byte is 255, we reached the end of the Vertex Data Structs
                        var dataBlockNum = br.ReadByte();
                        while (dataBlockNum != 255)
                        {
                            var vertexDataStruct = new VertexDataStruct
                            {
                                DataBlock  = dataBlockNum,
                                DataOffset = br.ReadByte(),
                                DataType   = VertexTypeDictionary[br.ReadByte()],
                                DataUsage  = VertexUsageDictionary[br.ReadByte()]
                            };

                            xivMdl.LoDList[i].MeshDataList[j].VertexDataStructList.Add(vertexDataStruct);

                            // padding between Vertex Data Structs
                            br.ReadBytes(4);

                            dataBlockNum = br.ReadByte();
                        }
                    }
                }

                // Now that we finished reading the Vertex Data Structures, we can go back to our saved position
                br.BaseStream.Seek(savePosition, SeekOrigin.Begin);

                // Mesh Data Information
                foreach (var lod in xivMdl.LoDList)
                {
                    for (var i = 0; i < lod.MeshCount; i++)
                    {
                        var meshDataInfo = new MeshDataInfo
                        {
                            VertexCount          = br.ReadInt32(),
                            IndexCount           = br.ReadInt32(),
                            MaterialIndex        = br.ReadInt16(),
                            MeshPartIndex        = br.ReadInt16(),
                            MeshPartCount        = br.ReadInt16(),
                            BoneListIndex        = br.ReadInt16(),
                            IndexDataOffset      = br.ReadInt32(),
                            VertexDataOffset0    = br.ReadInt32(),
                            VertexDataOffset1    = br.ReadInt32(),
                            VertexDataOffset2    = br.ReadInt32(),
                            VertexDataEntrySize0 = br.ReadByte(),
                            VertexDataEntrySize1 = br.ReadByte(),
                            VertexDataEntrySize2 = br.ReadByte(),
                            VertexDataBlockCount = br.ReadByte()
                        };

                        lod.MeshDataList[i].MeshInfo = meshDataInfo;

                        var materialString = xivMdl.PathData.MaterialList[meshDataInfo.MaterialIndex];
                        var typeChar = materialString[4].ToString() + materialString[9].ToString();

                        if (typeChar.Equals("cb"))
                        {
                            lod.MeshDataList[i].IsBody = true;
                        }
                    }
                }

                // Data block for attributes
                // Currently unknown usage
                var attributeDataBlock = new AttributeDataBlock
                {
                    Unknown = br.ReadBytes(xivMdl.ModelData.AttributeCount * 4)
                };
                xivMdl.AttrDataBlock = attributeDataBlock;

                // Unknown data block
                var unkData1 = new UnknownData1
                {
                    Unknown = br.ReadBytes(xivMdl.ModelData.Unknown3 * 20)
                };
                xivMdl.UnkData1 = unkData1;

                // Mesh Parts
                foreach (var lod in xivMdl.LoDList)
                {
                    foreach (var meshData in lod.MeshDataList)
                    {
                        meshData.MeshPartList = new List<MeshPart>();

                        for (var i = 0; i < meshData.MeshInfo.MeshPartCount; i++)
                        {
                            var meshPart = new MeshPart
                            {
                                IndexOffset     = br.ReadInt32(),
                                IndexCount      = br.ReadInt32(),
                                AttributeIndex  = br.ReadInt32(),
                                BoneStartOffset = br.ReadInt16(),
                                BoneCount       = br.ReadInt16()
                            };

                            meshData.MeshPartList.Add(meshPart);
                        }
                    }
                }

                // Unknown data block
                var unkData2 = new UnknownData2
                {
                    Unknown = br.ReadBytes(xivMdl.ModelData.Unknown9 * 12)
                };
                xivMdl.UnkData2 = unkData2;

                // Data block for materials
                // Currently unknown usage
                var matDataBlock = new MaterialDataBlock
                {
                    Unknown = br.ReadBytes(xivMdl.ModelData.MaterialCount * 4)
                };
                xivMdl.MatDataBlock = matDataBlock;

                // Data block for bones
                // Currently unknown usage
                var boneDataBlock = new BoneDataBlock
                {
                    Unknown = br.ReadBytes(xivMdl.ModelData.BoneCount * 4)
                };
                xivMdl.BonDataBlock = boneDataBlock;

                // Bone Lists
                xivMdl.BoneIndexMeshList = new List<BoneIndexMesh>();
                for (var i = 0; i < xivMdl.ModelData.BoneListCount; i++)
                {
                    var boneIndexMesh = new BoneIndexMesh
                    {
                        BoneIndices = new List<short>(64)
                    };

                    for (var j = 0; j < 64; j++)
                    {
                        boneIndexMesh.BoneIndices.Add(br.ReadInt16());
                    }

                    boneIndexMesh.BoneIndexCount = br.ReadInt32();

                    xivMdl.BoneIndexMeshList.Add(boneIndexMesh);
                }

                var hiderDataLists = new MeshHiderData
                {
                    HiderInfoList     = new List<MeshHiderData.HiderInfo>(),
                    HiderDataInfoList = new List<MeshHiderData.HiderIndexInfo>(),
                    HiderDataList     = new List<MeshHiderData.HiderData>()
                };

                var totalPartCount = 0;
                // Hider Info
                for (var i = 0; i < xivMdl.ModelData.MeshHiderInfoCount; i++)
                {
                    var hiderInfo = new MeshHiderData.HiderInfo
                    {
                        Unknown         = br.ReadInt32(),
                        HiderIndexParts = new List<MeshHiderData.HiderIndexPart>()
                    };

                    for (var j = 0; j < xivMdl.LoDList.Count; j++)
                    {
                        var hiderIndexPart = new MeshHiderData.HiderIndexPart
                        {
                            DataInfoIndex = br.ReadInt16(),
                            PartCount     = br.ReadInt16()
                        };
                        hiderInfo.HiderIndexParts.Add(hiderIndexPart);
                        totalPartCount += hiderIndexPart.PartCount;
                    }

                    hiderDataLists.HiderInfoList.Add(hiderInfo);
                }

                // Hider Index Info
                for (var i = 0; i < xivMdl.ModelData.MeshHiderDataCount; i++)
                {
                    var hiderIndexInfo = new MeshHiderData.HiderIndexInfo
                    {
                        IndexDataOffset = br.ReadInt32(),
                        IndexCount      = br.ReadInt32(),
                        DataIndexOffset = br.ReadInt32()
                    };

                    hiderDataLists.HiderDataInfoList.Add(hiderIndexInfo);
                }

                // Hider data
                for (var i = 0; i < xivMdl.ModelData.MeshHiderIndexCount; i++)
                {
                    var hiderData = new MeshHiderData.HiderData
                    {
                        ReferenceIndexOffset = br.ReadInt16(),
                        HideIndex            = br.ReadInt16()
                    };

                    hiderDataLists.HiderDataList.Add(hiderData);
                }

                xivMdl.MeshHideData = hiderDataLists;

                // Bone index for Parts
                var boneIndexPart = new BoneIndexPart
                {
                    BoneIndexCount = br.ReadInt32(),
                    BoneIndexList  = new List<short>()
                };

                for (var i = 0; i < boneIndexPart.BoneIndexCount / 2; i++)
                {
                    boneIndexPart.BoneIndexList.Add(br.ReadInt16());
                }

                xivMdl.BonIndexPart = boneIndexPart;

                // Padding
                xivMdl.PaddingSize = br.ReadByte();
                xivMdl.PaddedBytes = br.ReadBytes(xivMdl.PaddingSize);

                // Bounding box
                var boundingBox = new BoundingBox
                {
                    PointList = new List<Vector4>()
                };

                for (var i = 0; i < 8; i++)
                {
                    boundingBox.PointList.Add(new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                }

                xivMdl.BoundBox = boundingBox;

                // Bone Transform Data
                xivMdl.BoneTransformDataList = new List<BoneTransformData>();
                for (var i = 0; i < xivMdl.ModelData.BoneCount; i++)
                {
                    var boneTransformData = new BoneTransformData
                    {
                        Transform0 = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                        Transform1 = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle())
                    };

                    xivMdl.BoneTransformDataList.Add(boneTransformData);
                }

                foreach (var lod in xivMdl.LoDList)
                {
                    foreach (var meshData in lod.MeshDataList)
                    {
                        var vertexData = new VertexData
                        {
                            Positions = new List<Vector3>(),
                            BoneWeights = new List<float[]>(),
                            BoneIndices = new List<byte[]>(),
                            Normals = new List<Vector3>(),
                            BiNormals = new List<Vector3>(),
                            Colors = new List<Byte4>(),
                            TextureCoordinates0 = new List<Vector2>(),
                            TextureCoordinates1 = new List<Vector2>(),
                            Indices = new List<int>()
                        };

                        #region Positions
                        // Get the Vertex Data Structure for positions
                        var posDataSturct = (from vertexDataStruct in meshData.VertexDataStructList
                                             where vertexDataStruct.DataUsage == VertexUsageType.Position
                                             select vertexDataStruct).FirstOrDefault();

                        int vertexDataOffset;
                        int vertexDataSize;

                        // Determine which data block the position data is in
                        // This always seems to be in the first data block
                        switch (posDataSturct.DataBlock)
                        {
                            case 0:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset0;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize0;
                                break;
                            case 1:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset1;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize1;

                                break;
                            default:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset2;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize2;
                                break;
                        }

                        for (var i = 0; i < meshData.MeshInfo.VertexCount; i++)
                        {
                            // Get the offset for the position data for each vertex
                            var positionOffset = lod.VertexDataOffset + vertexDataOffset + posDataSturct.DataOffset + vertexDataSize * i;

                            // Go to the Data Block
                            br.BaseStream.Seek(positionOffset, SeekOrigin.Begin);

                            Vector3 positionVector;
                            // Position data is either stored in half-floats or singles
                            if (posDataSturct.DataType == VertexDataType.Half4)
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
                            vertexData.Positions.Add(positionVector);
                        }
                        #endregion


                        #region BoneWeights

                        // Get the Vertex Data Structure for bone weights
                        var bwDataSturct = (from vertexDataStruct in meshData.VertexDataStructList
                            where vertexDataStruct.DataUsage == VertexUsageType.BoneWeight
                            select vertexDataStruct).FirstOrDefault();

                        // Determine which data block the bone weight data is in
                        // This always seems to be in the first data block
                        switch (bwDataSturct.DataBlock)
                        {
                            case 0:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset0;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize0;
                                break;
                            case 1:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset1;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize1;

                                break;
                            default:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset2;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize2;
                                break;
                        }

                        // There is always one set of bone weights per vertex
                        for (var i = 0; i < meshData.MeshInfo.VertexCount; i++)
                        {
                            var bwOffset = lod.VertexDataOffset + vertexDataOffset + bwDataSturct.DataOffset + vertexDataSize * i;

                            br.BaseStream.Seek(bwOffset, SeekOrigin.Begin);

                            var bw0 = br.ReadByte() / 255f;
                            var bw1 = br.ReadByte() / 255f;
                            var bw2 = br.ReadByte() / 255f;
                            var bw3 = br.ReadByte() / 255f;

                            vertexData.BoneWeights.Add(new []{bw0, bw1, bw2, bw3});
                        }
                        #endregion


                        #region BoneIndices

                        // Get the Vertex Data Structure for bone indices
                        var biDataSturct = (from vertexDataStruct in meshData.VertexDataStructList
                            where vertexDataStruct.DataUsage == VertexUsageType.BoneIndex
                            select vertexDataStruct).FirstOrDefault();

                        // Determine which data block the bone index data is in
                        // This always seems to be in the first data block
                        switch (biDataSturct.DataBlock)
                        {
                            case 0:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset0;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize0;
                                break;
                            case 1:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset1;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize1;

                                break;
                            default:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset2;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize2;
                                break;
                        }

                        // There is always one set of bone indices per vertex
                        for (var i = 0; i < meshData.MeshInfo.VertexCount; i++)
                        {
                            var biOffset = lod.VertexDataOffset + vertexDataOffset + biDataSturct.DataOffset + vertexDataSize * i;

                            br.BaseStream.Seek(biOffset, SeekOrigin.Begin);

                            var bi0 = br.ReadByte();
                            var bi1 = br.ReadByte();
                            var bi2 = br.ReadByte();
                            var bi3 = br.ReadByte();

                            vertexData.BoneIndices.Add(new []{bi0, bi1, bi2, bi3});
                        }
                        #endregion


                        #region Normals

                        // Get the Vertex Data Structure for Normals
                        var normDataSturct = (from vertexDataStruct in meshData.VertexDataStructList
                            where vertexDataStruct.DataUsage == VertexUsageType.Normal
                            select vertexDataStruct).FirstOrDefault();

                        // Determine which data block the normal data is in
                        // This always seems to be in the second data block
                        switch (normDataSturct.DataBlock)
                        {
                            case 0:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset0;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize0;
                                break;
                            case 1:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset1;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize1;

                                break;
                            default:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset2;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize2;
                                break;
                        }

                        // There is always one set of normals per vertex
                        for (var i = 0; i < meshData.MeshInfo.VertexCount; i++)
                        {
                            var normOffset = lod.VertexDataOffset + vertexDataOffset + normDataSturct.DataOffset + vertexDataSize * i;

                            br.BaseStream.Seek(normOffset, SeekOrigin.Begin);

                            Vector3 normalVector;
                            // Normal data is either stored in half-floats or singles
                            if (normDataSturct.DataType == VertexDataType.Half4)
                            {
                                var x = new SharpDX.Half(br.ReadUInt16());
                                var y = new SharpDX.Half(br.ReadUInt16());
                                var z = new SharpDX.Half(br.ReadUInt16());
                                var w = new SharpDX.Half(br.ReadUInt16());

                                normalVector = new Vector3(x, y, z);
                            }
                            else
                            {
                                var x = br.ReadSingle();
                                var y = br.ReadSingle();
                                var z = br.ReadSingle();

                                normalVector = new Vector3(x, y, z);
                            }

                            vertexData.Normals.Add(normalVector);
                        }
                        #endregion


                        #region BiNormals

                        // Get the Vertex Data Structure for BiNormals
                        var biNormDataSturct = (from vertexDataStruct in meshData.VertexDataStructList
                            where vertexDataStruct.DataUsage == VertexUsageType.Binormal
                            select vertexDataStruct).FirstOrDefault();

                        // Determine which data block the binormal data is in
                        // This always seems to be in the second data block
                        switch (biNormDataSturct.DataBlock)
                        {
                            case 0:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset0;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize0;
                                break;
                            case 1:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset1;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize1;

                                break;
                            default:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset2;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize2;
                                break;
                        }

                        // There is always one set of normals per vertex
                        for (var i = 0; i < meshData.MeshInfo.VertexCount; i++)
                        {
                            var biNormOffset = lod.VertexDataOffset + vertexDataOffset + biNormDataSturct.DataOffset + vertexDataSize * i;

                            br.BaseStream.Seek(biNormOffset, SeekOrigin.Begin);

                            var x = br.ReadByte() * 2 / 255f - 1f;
                            var y = br.ReadByte() * 2 / 255f - 1f;
                            var z = br.ReadByte() * 2 / 255f - 1f;
                            var w = br.ReadByte() * 2 / 255f - 1f;

                            vertexData.BiNormals.Add(new Vector3(x, y, z));
                        }
                        #endregion


                        #region VertexColor

                        // Get the Vertex Data Structure for colors
                        var colorDataSturct = (from vertexDataStruct in meshData.VertexDataStructList
                            where vertexDataStruct.DataUsage == VertexUsageType.Color
                            select vertexDataStruct).FirstOrDefault();

                        // Determine which data block the color data is in
                        // This always seems to be in the second data block
                        switch (colorDataSturct.DataBlock)
                        {
                            case 0:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset0;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize0;
                                break;
                            case 1:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset1;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize1;

                                break;
                            default:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset2;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize2;
                                break;
                        }

                        // There is always one set of normals per vertex
                        for (var i = 0; i < meshData.MeshInfo.VertexCount; i++)
                        {
                            var colorOffset = lod.VertexDataOffset + vertexDataOffset + colorDataSturct.DataOffset + vertexDataSize * i;

                            br.BaseStream.Seek(colorOffset, SeekOrigin.Begin);

                            var a = br.ReadByte();
                            var r = br.ReadByte();
                            var g = br.ReadByte();
                            var b = br.ReadByte();

                            vertexData.Colors.Add(new Byte4(a, r, g, b));
                        }
                        #endregion


                        #region TextureCoordinates

                        // Get the Vertex Data Structure for texture coordinates
                        var tcDataSturct = (from vertexDataStruct in meshData.VertexDataStructList
                            where vertexDataStruct.DataUsage == VertexUsageType.TextureCoordinate
                            select vertexDataStruct).FirstOrDefault();

                        // Determine which data block the texture coordinate data is in
                        // This always seems to be in the second data block
                        switch (tcDataSturct.DataBlock)
                        {
                            case 0:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset0;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize0;
                                break;
                            case 1:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset1;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize1;

                                break;
                            default:
                                vertexDataOffset = meshData.MeshInfo.VertexDataOffset2;
                                vertexDataSize = meshData.MeshInfo.VertexDataEntrySize2;
                                break;
                        }

                        // There is always one set of normals per vertex
                        for (var i = 0; i < meshData.MeshInfo.VertexCount; i++)
                        {
                            var tcOffset = lod.VertexDataOffset + vertexDataOffset + tcDataSturct.DataOffset + vertexDataSize * i;

                            br.BaseStream.Seek(tcOffset, SeekOrigin.Begin);

                            Vector2 tcVector1;
                            Vector2 tcVector2;
                            // Normal data is either stored in half-floats or singles
                            if (tcDataSturct.DataType == VertexDataType.Half4)
                            {
                                var x  = new SharpDX.Half(br.ReadUInt16());
                                var y  = new SharpDX.Half(br.ReadUInt16());
                                var x1 = new SharpDX.Half(br.ReadUInt16());
                                var y1 = new SharpDX.Half(br.ReadUInt16());

                                tcVector1 = new Vector2(x, y);
                                tcVector2 = new Vector2(x1, y1);
                            }
                            else
                            {
                                var x  = br.ReadSingle();
                                var y  = br.ReadSingle();
                                var x1 = br.ReadSingle();
                                var y1 = br.ReadSingle();

                                tcVector1 = new Vector2(x, y);
                                tcVector2 = new Vector2(x1, y1);
                            }

                            vertexData.TextureCoordinates0.Add(tcVector1);
                            vertexData.TextureCoordinates1.Add(tcVector2);
                        }

                        #endregion

                        #region Indices

                        var indexOffset = lod.IndexDataOffset + meshData.MeshInfo.IndexDataOffset;

                        br.BaseStream.Seek(indexOffset, SeekOrigin.Begin);

                        for (var i = 0; i < meshData.MeshInfo.IndexCount; i++)
                        {
                            vertexData.Indices.Add(br.ReadInt16());
                        }

                        #endregion

                        meshData.VertexData = vertexData;
                    }
                }
            }

            return xivMdl;
        }


        private (string Folder, string File) GetMdlPath(IItemModel itemModel, XivRace xivRace, XivItemType itemType)
        {
            string mdlFolder = "", mdlFile = "";
            var id = itemModel.PrimaryModelInfo.ModelID.ToString().PadLeft(4, '0');
            var bodyVer = itemModel.PrimaryModelInfo.Body.ToString().PadLeft(4, '0');
            var race = xivRace.GetRaceCode();

            switch (itemType)
            {
                case XivItemType.equipment:
                    mdlFolder = $"chara/{itemType}/e{id}/model";
                    mdlFile   = $"c{race}e{id}_{SlotAbbreviationDictionary[itemModel.ItemCategory]}{MdlExtension}";
                    break;
                case XivItemType.accessory:
                    mdlFolder = $"chara/{itemType}/a{id}/model";
                    mdlFile   = $"c{race}a{id}_{SlotAbbreviationDictionary[itemModel.ItemCategory]}{MdlExtension}";
                    break;
                case XivItemType.weapon:
                    mdlFolder = $"chara/{itemType}/w{id}/obj/body/b{bodyVer}/model";
                    mdlFile   = $"w{id}b{bodyVer}{MdlExtension}";
                    break;

                case XivItemType.monster:
                    mdlFolder = $"chara/{itemType}/m{id}/obj/body/b{bodyVer}/model";
                    mdlFile   = $"m{id}b{bodyVer}{MdlExtension}";
                    break;
                case XivItemType.demihuman:
                    mdlFolder = $"chara/{itemType}/d{id}/obj/equipment/e{bodyVer}/model";
                    mdlFile   = $"d{id}e{bodyVer}_{SlotAbbreviationDictionary[itemModel.ItemCategory]}{MdlExtension}";
                    break;
                    //TODO: Check these
                case XivItemType.human:
                    if (itemModel.ItemCategory.Equals(XivStrings.Body))
                    {
                        mdlFolder = $"chara/{itemType}/c{id}/obj/body/b{bodyVer}/model";
                        mdlFile   = $"c{id}b{bodyVer}_{SlotAbbreviationDictionary[XivStrings.Body]}{MdlExtension}";
                    }
                    else if (itemModel.ItemCategory.Equals(XivStrings.Hair))
                    {
                        mdlFolder = $"chara/{itemType}/c{id}/obj/body/h{bodyVer}/model";
                        mdlFile   = $"c{id}h{bodyVer}_{SlotAbbreviationDictionary[itemModel.ItemCategory]}_{MdlExtension}";
                    }
                    else if (itemModel.ItemCategory.Equals(XivStrings.Face))
                    {
                        mdlFolder = $"chara/{itemType}/c{id}/obj/body/f{bodyVer}/model";
                        mdlFile   = $"c{id}f{bodyVer}_{SlotAbbreviationDictionary[itemModel.ItemCategory]}_{MdlExtension}";
                    }
                    else if (itemModel.ItemCategory.Equals(XivStrings.Tail))
                    {
                        mdlFolder = $"chara/{itemType}/c{id}/obj/body/t{bodyVer}/model";
                        mdlFile   = $"c{id}t{bodyVer}_{MdlExtension}";
                    }

                    break;
                default:
                    mdlFolder = "";
                    mdlFile = "";
                    break;
            }

            return (mdlFolder, mdlFile);
        }

        private static readonly Dictionary<string, string> SlotAbbreviationDictionary = new Dictionary<string, string>
        {
            {XivStrings.Head, "met"},
            {XivStrings.Hands, "glv"},
            {XivStrings.Legs, "dwn"},
            {XivStrings.Feet, "sho"},
            {XivStrings.Body, "top"},
            {XivStrings.Ears, "ear"},
            {XivStrings.Neck, "nek"},
            {XivStrings.Rings, "rir"},
            {XivStrings.Wrists, "wrs"},
            {XivStrings.Head_Body, "top"},
            {XivStrings.Body_Hands, "top"},
            {XivStrings.Body_Hands_Legs, "top"},
            {XivStrings.Body_Legs_Feet, "top"},
            {XivStrings.Body_Hands_Legs_Feet, "top"},
            {XivStrings.Legs_Feet, "top"},
            {XivStrings.All, "top"},
            {XivStrings.Face, "fac"},
            {XivStrings.Iris, "iri"},
            {XivStrings.Etc, "etc"},
            {XivStrings.Accessory, "acc"},
            {XivStrings.Hair, "hir"}
        };

        private static readonly Dictionary<byte, VertexDataType> VertexTypeDictionary =
            new Dictionary<byte, VertexDataType>
            {
                {0x0, VertexDataType.Float1},
                {0x1, VertexDataType.Float2},
                {0x2, VertexDataType.Float3},
                {0x3, VertexDataType.Float4},
                {0x5, VertexDataType.Ubyte4},
                {0x6, VertexDataType.Short2},
                {0x7, VertexDataType.Short4},
                {0x8, VertexDataType.Ubyte4n},
                {0x9, VertexDataType.Short2n},
                {0xA, VertexDataType.Short4n},
                {0xD, VertexDataType.Half2},
                {0xE, VertexDataType.Half4},
                {0xF, VertexDataType.Compress}
            };

        private static readonly Dictionary<byte, VertexUsageType> VertexUsageDictionary =
            new Dictionary<byte, VertexUsageType>
            {
                {0x0, VertexUsageType.Position },
                {0x1, VertexUsageType.BoneWeight },
                {0x2, VertexUsageType.BoneIndex },
                {0x3, VertexUsageType.Normal },
                {0x4, VertexUsageType.TextureCoordinate },
                {0x5, VertexUsageType.Tangent },
                {0x6, VertexUsageType.Binormal },
                {0x7, VertexUsageType.Color }
            };
    }
}