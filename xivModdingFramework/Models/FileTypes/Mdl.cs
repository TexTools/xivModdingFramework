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

using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HelixToolkit.SharpDX.Core;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.Enums;
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.Enums;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;
using BoundingBox = xivModdingFramework.Models.DataContainers.BoundingBox;
using System.Diagnostics;
using xivModdingFramework.Items.Categories;
using HelixToolkit.SharpDX.Core.Core;
using System.Transactions;
using System.Runtime.CompilerServices;
using System.Threading;
using SharpDX.Win32;
using xivModdingFramework.Models.Helpers;

namespace xivModdingFramework.Models.FileTypes
{
    public class Mdl
    {
        private const string MdlExtension = ".mdl";
        private readonly DirectoryInfo _gameDirectory;
        private readonly DirectoryInfo _modListDirectory;
        private readonly XivDataFile _dataFile;

        public Mdl(DirectoryInfo gameDirectory, XivDataFile dataFile)
        {
            _gameDirectory = gameDirectory;
            _modListDirectory = new DirectoryInfo(Path.Combine(gameDirectory.Parent.Parent.FullName, XivStrings.ModlistFilePath));

            _dataFile = dataFile;
        }

        private byte[] _rawData;

        /// <summary>
        /// Retrieves and clears the RawData value.
        /// </summary>
        /// <returns></returns>
        public byte[] GetRawData()
        {
            var ret = _rawData;
            _rawData = null;
            return ret;
        }


        /// <summary>
        /// Retrieves all items that share the same model.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="language"></param>
        /// <returns></returns>
        public async Task<List<IItemModel>> GetSameModelList(IItemModel item, XivLanguage language = XivLanguage.English)
        {
            var sameModelItems = new List<IItemModel>();
            var gear = new Gear(_gameDirectory, language);
            var character = new Character(_gameDirectory, language);
            var companions = new Companions(_gameDirectory, language);
            var ui = new UI(_gameDirectory, language);
            var housing = new Housing(_gameDirectory, language);

            if (item.PrimaryCategory.Equals(XivStrings.Gear))
            {

                // Scan the gear list for anything using the same model ID and slot.
                sameModelItems.AddRange(
                    (await gear.GetGearList())
                    .Where(it =>
                    it.ModelInfo.PrimaryID == item.ModelInfo.PrimaryID
                    && it.SecondaryCategory == item.SecondaryCategory).Select(it => it as IItemModel).ToList()
                );
            }
            else if (item.PrimaryCategory.Equals(XivStrings.Character))
            {

                // Character models are assumed to have no shared models,
                // So return a copy of the original item.
                sameModelItems.Add((IItemModel) item.Clone());
            }
            return sameModelItems;
        }


        /// <summary>
        /// Gets the MDL Data given a model and race
        /// </summary>
        /// <param name="itemModel">The Item model</param>
        /// <param name="xivRace">The race for which to get the data</param>
        /// <param name="secondaryModel">The secondary model info if needed</param>
        /// <returns>An XivMdl structure containing all mdl data.</returns>
        public async Task<XivMdl> GetMdlData(IItemModel itemModel, XivRace xivRace, XivModelInfo secondaryModel = null, string mdlStringPath = null, int originalOffset = 0, string ringSide = null)
        {
            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);
            var modding = new Modding(_gameDirectory);
            var getShapeData = true;

            var itemType = ItemType.GetPrimaryItemType(itemModel);

            var mdlPath = GetMdlPath(itemModel, xivRace, itemType, secondaryModel, mdlStringPath, ringSide);

            var offset = await index.GetDataOffset(HashGenerator.GetHash(mdlPath.Folder), HashGenerator.GetHash(mdlPath.File),
                _dataFile);

            if (await modding.IsModEnabled($"{mdlPath.Folder}/{mdlPath.File}", false) == XivModStatus.Enabled &&
                originalOffset == 0)
            {
                //getShapeData = false;
            }

            if (originalOffset != 0)
            {
                offset = originalOffset;
            }

            if (offset == 0)
            {
                throw new Exception($"Could not find offset for {mdlPath.Folder}/{mdlPath.File}");
            }

            var mdlData = await dat.GetType3Data(offset, _dataFile);

            var xivMdl = new XivMdl {MdlPath = mdlPath};
            int totalNonNullMaterials = 0;

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
                    MaterialList  = new List<string>(),
                    ShapeList     = new List<string>(),
                    ExtraPathList = new List<string>()
                };

                // Get the entire path string block to parse later
                // This will be done when we obtain the path counts for each type
                var pathBlock = br.ReadBytes(mdlPathData.PathBlockSize);

                var mdlModelData = new MdlModelData
                {
                    Unknown0            = br.ReadInt32(),
                    MeshCount           = br.ReadInt16(),
                    AttributeCount      = br.ReadInt16(),
                    MeshPartCount       = br.ReadInt16(),
                    MaterialCount       = br.ReadInt16(),
                    BoneCount           = br.ReadInt16(),
                    BoneListCount       = br.ReadInt16(),
                    ShapeCount          = br.ReadInt16(),
                    ShapePartCount      = br.ReadInt16(),
                    ShapeDataCount     = br.ReadInt16(),
                    Unknown1            = br.ReadInt16(),
                    Unknown2            = br.ReadInt16(),
                    Unknown3            = br.ReadInt16(),
                    Unknown4            = br.ReadInt16(),
                    Unknown5            = br.ReadInt16(),
                    Unknown6            = br.ReadInt16(),
                    Unknown7            = br.ReadInt16(),
                    Unknown8            = br.ReadInt16(), // Used for transform count with furniture
                    Unknown9            = br.ReadInt16(),
                    Unknown10a          = br.ReadByte(),
                    Unknown10b          = br.ReadByte(),
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

                        var mat = Encoding.ASCII.GetString(materialName.ToArray()).Replace("\0", "");
                        if(mat.StartsWith("shp_"))
                        {
                            // Catch case for situation where there's null values at the end of the materials list.
                            mdlPathData.ShapeList.Add(mat);
                        } else
                        {
                            totalNonNullMaterials++;
                            mdlPathData.MaterialList.Add(mat);
                        }
                    }


                    // Shape Paths
                    for (var i = 0; i < mdlModelData.ShapeCount; i++)
                    {
                        byte a;
                        var shapeName = new List<byte>();
                        while ((a = br1.ReadByte()) != 0)
                        {
                            shapeName.Add(a);
                        }

                        var shp = Encoding.ASCII.GetString(shapeName.ToArray()).Replace("\0", "");

                        mdlPathData.ShapeList.Add(shp);
                    }

                    var remainingPathData = mdlPathData.PathBlockSize - br1.BaseStream.Position;
                    if (remainingPathData > 2)
                    {
                        while (remainingPathData != 0)
                        {
                            byte a;
                            var extraName = new List<byte>();
                            while ((a = br1.ReadByte()) != 0)
                            {
                                extraName.Add(a);
                                remainingPathData--;
                            }

                            remainingPathData--;

                            if (extraName.Count > 0)
                            {
                                var extra = Encoding.ASCII.GetString(extraName.ToArray()).Replace("\0", "");

                                mdlPathData.ExtraPathList.Add(extra);
                            }
                        }

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

                var totalLoDMeshes = 0;

                // We add each LoD to the list
                // Note: There is always 3 LoD
                xivMdl.LoDList = new List<LevelOfDetail>();
                for (var i = 0; i < 3; i++)
                {
                    var lod = new LevelOfDetail
                    {
                        MeshOffset       = br.ReadUInt16(),
                        MeshCount        = br.ReadInt16(),
                        Unknown0         = br.ReadInt32(),
                        Unknown1         = br.ReadInt32(),
                        MeshEnd          = br.ReadInt16(),
                        ExtraMeshCount   = br.ReadInt16(),
                        MeshSum          = br.ReadInt16(),
                        Unknown2         = br.ReadInt16(),
                        Unknown3         = br.ReadInt32(),
                        Unknown4         = br.ReadInt32(),
                        Unknown5         = br.ReadInt32(),
                        IndexDataStart   = br.ReadInt32(),
                        Unknown6         = br.ReadInt32(),
                        Unknown7         = br.ReadInt32(),
                        VertexDataSize   = br.ReadInt32(),
                        IndexDataSize    = br.ReadInt32(),
                        VertexDataOffset = br.ReadInt32(),
                        IndexDataOffset  = br.ReadInt32(),
                        MeshDataList     = new List<MeshData>()
                    };
                    // Finished reading LoD

                    totalLoDMeshes += lod.MeshCount;

                    // if LoD0 shows no mesh, add one (This is rare, but happens on company chest for example)
                    if (i == 0 && lod.MeshCount == 0)
                    {
                        lod.MeshCount = 1;
                    }

                    //Adding to xivMdl
                    xivMdl.LoDList.Add(lod);
                }

                //HACK: This is a workaround for certain furniture items, mainly with picture frames and easel
                var isEmpty = false;
                try
                {
                    isEmpty = br.PeekChar() == 0;
                }
                catch{}

                if (isEmpty && totalLoDMeshes < mdlModelData.MeshCount)
                {
                    xivMdl.ExtraLoDList = new List<LevelOfDetail>();

                    for (var i = 0; i < mdlModelData.Unknown10a; i++)
                    {
                        var lod = new LevelOfDetail
                        {
                            MeshOffset       = br.ReadUInt16(),
                            MeshCount        = br.ReadInt16(),
                            Unknown0         = br.ReadInt32(),
                            Unknown1         = br.ReadInt32(),
                            MeshEnd          = br.ReadInt16(),
                            ExtraMeshCount   = br.ReadInt16(),
                            MeshSum          = br.ReadInt16(),
                            Unknown2         = br.ReadInt16(),
                            Unknown3         = br.ReadInt32(),
                            Unknown4         = br.ReadInt32(),
                            Unknown5         = br.ReadInt32(),
                            IndexDataStart   = br.ReadInt32(),
                            Unknown6         = br.ReadInt32(),
                            Unknown7         = br.ReadInt32(),
                            VertexDataSize   = br.ReadInt32(),
                            IndexDataSize    = br.ReadInt32(),
                            VertexDataOffset = br.ReadInt32(),
                            IndexDataOffset  = br.ReadInt32(),
                            MeshDataList     = new List<MeshData>()
                        };

                        xivMdl.ExtraLoDList.Add(lod);
                    }
                }

                // Now that we have the LoD data, we can go back and read the Vertex Data Structures
                // First we save our current position
                var savePosition = br.BaseStream.Position;

                var loDStructPos = 68;
                // for each mesh in each lod
                for (var i = 0; i < xivMdl.LoDList.Count; i++)
                {
                    var totalMeshCount = xivMdl.LoDList[i].MeshCount + xivMdl.LoDList[i].ExtraMeshCount;
                    for (var j = 0; j < totalMeshCount; j++)
                    {
                        xivMdl.LoDList[i].MeshDataList.Add(new MeshData());
                        xivMdl.LoDList[i].MeshDataList[j].VertexDataStructList = new List<VertexDataStruct>();

                        // LoD Index * Vertex Data Structure size + Header
                        
                        br.BaseStream.Seek(j * 136 + loDStructPos, SeekOrigin.Begin);

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

                    loDStructPos += 136 * xivMdl.LoDList[i].MeshCount;
                }

                // Now that we finished reading the Vertex Data Structures, we can go back to our saved position
                br.BaseStream.Seek(savePosition, SeekOrigin.Begin);

                // Mesh Data Information
                var meshNum = 0;
                foreach (var lod in xivMdl.LoDList)
                {
                    var totalMeshCount = lod.MeshCount + lod.ExtraMeshCount;

                    for (var i = 0; i < totalMeshCount; i++)
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

                        // In the event we have a null material reference, set it to material 0 to be safe.
                        if(meshDataInfo.MaterialIndex >= totalNonNullMaterials)
                        {
                            meshDataInfo.MaterialIndex = 0;
                        }

                        var materialString = xivMdl.PathData.MaterialList[meshDataInfo.MaterialIndex];
                        // Try block to cover odd cases like Au Ra Male Face #92 where for some reason the
                        // Last LoD points to using a shp for a material for some reason.
                        try
                        {
                            var typeChar = materialString[4].ToString() + materialString[9].ToString();

                            if (typeChar.Equals("cb"))
                            {
                                lod.MeshDataList[i].IsBody = true;
                            }
                        } catch(Exception e)
                        {

                        }

                        meshNum++;
                    }
                }

                // Data block for attributes offset paths
                var attributeDataBlock = new AttributeDataBlock
                {
                    AttributePathOffsetList = new List<int>(xivMdl.ModelData.AttributeCount)
                };

                for (var i = 0; i < xivMdl.ModelData.AttributeCount; i++)
                {
                    attributeDataBlock.AttributePathOffsetList.Add(br.ReadInt32());
                }

                xivMdl.AttrDataBlock = attributeDataBlock;

                // Unknown data block
                // This is commented out to allow housing items to display, the data does not exist for housing items
                // more investigation needed as to what this data is
                var unkData1 = new UnknownData1
                {
                    //Unknown = br.ReadBytes(xivMdl.ModelData.Unknown3 * 20)
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
                                AttributeBitmask  = br.ReadUInt32(),
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
                    MaterialPathOffsetList = new List<int>(xivMdl.ModelData.MaterialCount)
                };

                for (var i = 0; i < xivMdl.ModelData.MaterialCount; i++)
                {
                    matDataBlock.MaterialPathOffsetList.Add(br.ReadInt32());
                }

                xivMdl.MatDataBlock = matDataBlock;

                // Data block for bones
                // Currently unknown usage
                var boneDataBlock = new BoneDataBlock
                {
                    BonePathOffsetList = new List<int>(xivMdl.ModelData.BoneCount)
                };

                for (var i = 0; i < xivMdl.ModelData.BoneCount; i++)
                {
                    boneDataBlock.BonePathOffsetList.Add(br.ReadInt32());
                }

                xivMdl.BoneDataBlock = boneDataBlock;

                // Bone Lists
                xivMdl.MeshBoneSets = new List<BoneSet>();
                for (var i = 0; i < xivMdl.ModelData.BoneListCount; i++)
                {
                    var boneIndexMesh = new BoneSet
                    {
                        BoneIndices = new List<short>(64)
                    };

                    for (var j = 0; j < 64; j++)
                    {
                        boneIndexMesh.BoneIndices.Add(br.ReadInt16());
                    }

                    boneIndexMesh.BoneIndexCount = br.ReadInt32();

                    xivMdl.MeshBoneSets.Add(boneIndexMesh);
                }

                var shapeDataLists = new ShapeData
                {
                    ShapeInfoList     = new List<ShapeData.ShapeInfo>(),
                    ShapeParts = new List<ShapeData.ShapePart>(),
                    ShapeDataList     = new List<ShapeData.ShapeDataEntry>()
                };

                var totalPartCount = 0;
                // Shape Info

                // Each shape has a header entry, then a per-lod entry.
                for (var i = 0; i < xivMdl.ModelData.ShapeCount; i++)
                {

                    // Header - Offset to the shape name.
                    var shapeInfo = new ShapeData.ShapeInfo
                    {
                        ShapeNameOffset = br.ReadInt32(),
                        Name = xivMdl.PathData.ShapeList[i],
                        ShapeLods = new List<ShapeData.ShapeLodInfo>()
                    };

                    // Per LoD entry (offset to this shape's parts in the shape set)
                    var dataInfoIndexList = new List<ushort>();
                    for (var j = 0; j < xivMdl.LoDList.Count; j++)
                    {
                        dataInfoIndexList.Add(br.ReadUInt16());
                    }

                    // Per LoD entry (number of parts in the shape set)
                    var infoPartCountList = new List<short>();
                    for (var j = 0; j < xivMdl.LoDList.Count; j++)
                    {
                        infoPartCountList.Add(br.ReadInt16());
                    }

                    for (var j = 0; j < xivMdl.LoDList.Count; j++)
                    {
                        var shapeIndexPart = new ShapeData.ShapeLodInfo
                        {
                            PartOffset = dataInfoIndexList[j],
                            PartCount = infoPartCountList[j]
                        };
                        shapeInfo.ShapeLods.Add(shapeIndexPart);
                        totalPartCount += shapeIndexPart.PartCount;
                    }

                    shapeDataLists.ShapeInfoList.Add(shapeInfo);
                }

                // Shape Index Info
                for (var i = 0; i < xivMdl.ModelData.ShapePartCount; i++)
                {
                    var shapeIndexInfo = new ShapeData.ShapePart
                    {
                        MeshIndexOffset = br.ReadInt32(),  // The offset to the index block this Shape Data should be replacing in. -- This is how Shape Data is tied to each mesh.
                        IndexCount      = br.ReadInt32(),  // # of triangle indices to replace.
                        ShapeDataOffset = br.ReadInt32()   // The offset where this part should start reading in the Shape Data list.
                    };

                    shapeDataLists.ShapeParts.Add(shapeIndexInfo);
                }

                // Shape data
                for (var i = 0; i < xivMdl.ModelData.ShapeDataCount; i++)
                {
                    var shapeData = new ShapeData.ShapeDataEntry
                    {
                        BaseIndex   = br.ReadUInt16(),  // Base Triangle Index we're replacing
                        ShapeVertex  = br.ReadUInt16()  // The Vertex that Triangle Index should now point to instead.
                    };
                    shapeDataLists.ShapeDataList.Add(shapeData);
                }

                xivMdl.MeshShapeData = shapeDataLists;

                // Build the list of offsets so we can match it for shape data.
                var indexOffsets = new List<List<int>>();
                for(int l = 0; l < xivMdl.LoDList.Count; l++)
                {
                    indexOffsets.Add(new List<int>());
                    for(int m = 0; m < xivMdl.LoDList[l].MeshDataList.Count; m++)
                    {
                        indexOffsets[l].Add(xivMdl.LoDList[l].MeshDataList[m].MeshInfo.IndexDataOffset);
                    }

                }
                xivMdl.MeshShapeData.AssignMeshAndLodNumbers(indexOffsets);

                // Sets the boolean flag if the model has shape data
                xivMdl.HasShapeData = xivMdl.ModelData.ShapeCount > 0;

                // Bone index for Parts
                var partBoneSet = new BoneSet
                {
                    BoneIndexCount = br.ReadInt32(),
                    BoneIndices  = new List<short>()
                };

                for (var i = 0; i < partBoneSet.BoneIndexCount / 2; i++)
                {
                    partBoneSet.BoneIndices.Add(br.ReadInt16());
                }

                xivMdl.PartBoneSets = partBoneSet;

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

                var transformCount = xivMdl.ModelData.BoneCount;

                if (itemType == XivItemType.furniture)
                {
                    transformCount = xivMdl.ModelData.Unknown8;
                }

                for (var i = 0; i < transformCount; i++)
                {
                    var boneTransformData = new BoneTransformData
                    {
                        Transform0 = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                        Transform1 = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle())
                    };

                    xivMdl.BoneTransformDataList.Add(boneTransformData);
                }

                var lodNum = 0;
                var totalMeshNum = 0;
                foreach (var lod in xivMdl.LoDList)
                {
                    if(lod.MeshCount == 0) continue;

                    var meshDataList = lod.MeshDataList;

                    if (lod.MeshOffset != totalMeshNum)
                    {
                        meshDataList = xivMdl.LoDList[lodNum + 1].MeshDataList;
                    }

                    foreach (var meshData in meshDataList)
                    {
                        var vertexData = new VertexData
                        {
                            Positions = new Vector3Collection(),
                            BoneWeights = new List<float[]>(),
                            BoneIndices = new List<byte[]>(),
                            Normals = new Vector3Collection(),
                            BiNormals = new Vector3Collection(),
                            BiNormalHandedness = new List<byte>(),
                            Tangents = new Vector3Collection(),
                            Colors = new List<Color>(),
                            Colors4 = new Color4Collection(),
                            TextureCoordinates0 = new Vector2Collection(),
                            TextureCoordinates1 = new Vector2Collection(),
                            Indices = new IntCollection()
                        };

                        #region Positions
                        // Get the Vertex Data Structure for positions
                        var posDataStruct = (from vertexDataStruct in meshData.VertexDataStructList
                                             where vertexDataStruct.DataUsage == VertexUsageType.Position
                                             select vertexDataStruct).FirstOrDefault();

                        int vertexDataOffset;
                        int vertexDataSize;

                        if (posDataStruct != null)
                        {
                            // Determine which data block the position data is in
                            // This always seems to be in the first data block
                            switch (posDataStruct.DataBlock)
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
                                var positionOffset = lod.VertexDataOffset + vertexDataOffset + posDataStruct.DataOffset + vertexDataSize * i;

                                // Go to the Data Block
                                br.BaseStream.Seek(positionOffset, SeekOrigin.Begin);

                                Vector3 positionVector;
                                // Position data is either stored in half-floats or singles
                                if (posDataStruct.DataType == VertexDataType.Half4)
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
                        }

                        #endregion


                        #region BoneWeights

                        // Get the Vertex Data Structure for bone weights
                        var bwDataStruct = (from vertexDataStruct in meshData.VertexDataStructList
                            where vertexDataStruct.DataUsage == VertexUsageType.BoneWeight
                            select vertexDataStruct).FirstOrDefault();

                        if (bwDataStruct != null)
                        {
                            // Determine which data block the bone weight data is in
                            // This always seems to be in the first data block
                            switch (bwDataStruct.DataBlock)
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
                                var bwOffset = lod.VertexDataOffset + vertexDataOffset + bwDataStruct.DataOffset + vertexDataSize * i;

                                br.BaseStream.Seek(bwOffset, SeekOrigin.Begin);

                                var bw0 = br.ReadByte() / 255f;
                                var bw1 = br.ReadByte() / 255f;
                                var bw2 = br.ReadByte() / 255f;
                                var bw3 = br.ReadByte() / 255f;

                                vertexData.BoneWeights.Add(new[] { bw0, bw1, bw2, bw3 });
                            }
                        }


                        #endregion


                        #region BoneIndices

                        // Get the Vertex Data Structure for bone indices
                        var biDataStruct = (from vertexDataStruct in meshData.VertexDataStructList
                            where vertexDataStruct.DataUsage == VertexUsageType.BoneIndex
                            select vertexDataStruct).FirstOrDefault();

                        if (biDataStruct != null)
                        {
                            // Determine which data block the bone index data is in
                            // This always seems to be in the first data block
                            switch (biDataStruct.DataBlock)
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
                                var biOffset = lod.VertexDataOffset + vertexDataOffset + biDataStruct.DataOffset + vertexDataSize * i;

                                br.BaseStream.Seek(biOffset, SeekOrigin.Begin);

                                var bi0 = br.ReadByte();
                                var bi1 = br.ReadByte();
                                var bi2 = br.ReadByte();
                                var bi3 = br.ReadByte();

                                vertexData.BoneIndices.Add(new[] { bi0, bi1, bi2, bi3 });
                            }
                        }

                        #endregion


                        #region Normals

                        // Get the Vertex Data Structure for Normals
                        var normDataStruct = (from vertexDataStruct in meshData.VertexDataStructList
                            where vertexDataStruct.DataUsage == VertexUsageType.Normal
                            select vertexDataStruct).FirstOrDefault();

                        if (normDataStruct != null)
                        {
                            // Determine which data block the normal data is in
                            // This always seems to be in the second data block
                            switch (normDataStruct.DataBlock)
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
                                var normOffset = lod.VertexDataOffset + vertexDataOffset + normDataStruct.DataOffset + vertexDataSize * i;

                                br.BaseStream.Seek(normOffset, SeekOrigin.Begin);

                                Vector3 normalVector;
                                // Normal data is either stored in half-floats or singles
                                if (normDataStruct.DataType == VertexDataType.Half4)
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
                        }

                        #endregion


                        #region BiNormals

                        // Get the Vertex Data Structure for BiNormals
                        var biNormDataStruct = (from vertexDataStruct in meshData.VertexDataStructList
                            where vertexDataStruct.DataUsage == VertexUsageType.Binormal
                            select vertexDataStruct).FirstOrDefault();

                        if (biNormDataStruct != null)
                        {
                            // Determine which data block the binormal data is in
                            // This always seems to be in the second data block
                            switch (biNormDataStruct.DataBlock)
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

                            // There is always one set of biNormals per vertex
                            for (var i = 0; i < meshData.MeshInfo.VertexCount; i++)
                            {
                                var biNormOffset = lod.VertexDataOffset + vertexDataOffset + biNormDataStruct.DataOffset + vertexDataSize * i;

                                br.BaseStream.Seek(biNormOffset, SeekOrigin.Begin);

                                var x = br.ReadByte() * 2 / 255f - 1f;
                                var y = br.ReadByte() * 2 / 255f - 1f;
                                var z = br.ReadByte() * 2 / 255f - 1f;
                                var w = br.ReadByte();

                                vertexData.BiNormals.Add(new Vector3(x, y, z));
                                vertexData.BiNormalHandedness.Add(w);
                            }
                        }

                        #endregion

                        #region Tangents

                        // Get the Vertex Data Structure for Tangents
                        var tangentDataStruct = (from vertexDataStruct in meshData.VertexDataStructList
                                                where vertexDataStruct.DataUsage == VertexUsageType.Tangent
                                                select vertexDataStruct).FirstOrDefault();

                        if (tangentDataStruct != null)
                        {
                            // Determine which data block the tangent data is in
                            // This always seems to be in the second data block
                            switch (tangentDataStruct.DataBlock)
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

                            // There is one set of tangents per vertex
                            for (var i = 0; i < meshData.MeshInfo.VertexCount; i++)
                            {
                                var tangentOffset = lod.VertexDataOffset + vertexDataOffset + tangentDataStruct.DataOffset + vertexDataSize * i;

                                br.BaseStream.Seek(tangentOffset, SeekOrigin.Begin);

                                var x = br.ReadByte() * 2 / 255f - 1f;
                                var y = br.ReadByte() * 2 / 255f - 1f;
                                var z = br.ReadByte() * 2 / 255f - 1f;
                                var w = br.ReadByte();

                                vertexData.Tangents.Add(new Vector3(x, y, z));
                                //vertexData.TangentHandedness.Add(w);
                            }
                        }

                        #endregion


                        #region VertexColor

                        // Get the Vertex Data Structure for colors
                        var colorDataStruct = (from vertexDataStruct in meshData.VertexDataStructList
                            where vertexDataStruct.DataUsage == VertexUsageType.Color
                            select vertexDataStruct).FirstOrDefault();

                        if (colorDataStruct != null)
                        {
                            // Determine which data block the color data is in
                            // This always seems to be in the second data block
                            switch (colorDataStruct.DataBlock)
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

                            // There is always one set of colors per vertex
                            for (var i = 0; i < meshData.MeshInfo.VertexCount; i++)
                            {
                                var colorOffset = lod.VertexDataOffset + vertexDataOffset + colorDataStruct.DataOffset + vertexDataSize * i;

                                br.BaseStream.Seek(colorOffset, SeekOrigin.Begin);

                                var r = br.ReadByte();
                                var g = br.ReadByte();
                                var b = br.ReadByte();
                                var a = br.ReadByte();

                                vertexData.Colors.Add(new Color(r, g, b, a));
                                vertexData.Colors4.Add(new Color4((r / 255f), (g / 255f), (b / 255f), (a / 255f)));
                            }
                        }

                        #endregion


                        #region TextureCoordinates

                        // Get the Vertex Data Structure for texture coordinates
                        var tcDataStruct = (from vertexDataStruct in meshData.VertexDataStructList
                            where vertexDataStruct.DataUsage == VertexUsageType.TextureCoordinate
                            select vertexDataStruct).FirstOrDefault();

                        if (tcDataStruct != null)
                        {
                            // Determine which data block the texture coordinate data is in
                            // This always seems to be in the second data block
                            switch (tcDataStruct.DataBlock)
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

                            // There is always one set of texture coordinates per vertex
                            for (var i = 0; i < meshData.MeshInfo.VertexCount; i++)
                            {
                                var tcOffset = lod.VertexDataOffset + vertexDataOffset + tcDataStruct.DataOffset + vertexDataSize * i;

                                br.BaseStream.Seek(tcOffset, SeekOrigin.Begin);

                                Vector2 tcVector1;
                                Vector2 tcVector2;
                                // Normal data is either stored in half-floats or singles
                                if (tcDataStruct.DataType == VertexDataType.Half4)
                                {
                                    var x = new SharpDX.Half(br.ReadUInt16());
                                    var y = new SharpDX.Half(br.ReadUInt16());
                                    var x1 = new SharpDX.Half(br.ReadUInt16());
                                    var y1 = new SharpDX.Half(br.ReadUInt16());

                                    tcVector1 = new Vector2(x, y);
                                    tcVector2 = new Vector2(x1, y1);


                                    vertexData.TextureCoordinates0.Add(tcVector1);
                                    vertexData.TextureCoordinates1.Add(tcVector2);
                                }
                                else if (tcDataStruct.DataType == VertexDataType.Half2)
                                {
                                    var x = new SharpDX.Half(br.ReadUInt16());
                                    var y = new SharpDX.Half(br.ReadUInt16());

                                    tcVector1 = new Vector2(x, y);

                                    vertexData.TextureCoordinates0.Add(tcVector1);
                                }
                                else if (tcDataStruct.DataType == VertexDataType.Float2)
                                {
                                    var x = br.ReadSingle();
                                    var y = br.ReadSingle();

                                    tcVector1 = new Vector2(x, y);
                                    vertexData.TextureCoordinates0.Add(tcVector1);
                                }
                                else if(tcDataStruct.DataType == VertexDataType.Float4)
                                {
                                    var x = br.ReadSingle();
                                    var y = br.ReadSingle();
                                    var x1 = br.ReadSingle();
                                    var y1 = br.ReadSingle();

                                    tcVector1 = new Vector2(x, y);
                                    tcVector2 = new Vector2(x1, y1);


                                    vertexData.TextureCoordinates0.Add(tcVector1);
                                    vertexData.TextureCoordinates1.Add(tcVector2);
                                }

                            }
                        }

                        #endregion

                        #region Indices

                        var indexOffset = lod.IndexDataOffset + meshData.MeshInfo.IndexDataOffset * 2;

                        br.BaseStream.Seek(indexOffset, SeekOrigin.Begin);

                        for (var i = 0; i < meshData.MeshInfo.IndexCount; i++)
                        {
                            vertexData.Indices.Add(br.ReadUInt16());
                        }

                        #endregion

                        meshData.VertexData = vertexData;
                        totalMeshNum++;
                    }

                    #region MeshShape

                    // If the model contains Shape Data, parse the data for each mesh
                    if (xivMdl.HasShapeData && getShapeData)
                    {
                        //Dictionary containing <index data offset, mesh number>
                        var indexMeshNum = new Dictionary<int, int>();

                        var shapeData = xivMdl.MeshShapeData.ShapeDataList;

                        // Get the index data offsets in each mesh
                        for (var i = 0; i < lod.MeshCount; i++)
                        {
                            var indexDataOffset = lod.MeshDataList[i].MeshInfo.IndexDataOffset;

                            if (!indexMeshNum.ContainsKey(indexDataOffset))
                            {
                                indexMeshNum.Add(indexDataOffset, i);
                            }
                        }

                        for (var i = 0; i < lod.MeshCount; i++)
                        {
                            var referencePositionsDictionary = new Dictionary<int, Vector3>();
                            var meshShapePositionsDictionary = new SortedDictionary<int, Vector3>();
                            var shapeIndexOffsetDictionary = new Dictionary<int, Dictionary<ushort, ushort>>();

                            // Shape info list
                            var shapeInfoList = xivMdl.MeshShapeData.ShapeInfoList;

                            // Number of shape info in each mesh
                            var perMeshCount = xivMdl.ModelData.ShapeCount;

                            for (var j = 0; j < perMeshCount; j++)
                            {
                                var shapeInfo = shapeInfoList[j];

                                var indexPart = shapeInfo.ShapeLods[lodNum];

                                // The part count
                                var infoPartCount = indexPart.PartCount;

                                for (var k = 0; k < infoPartCount; k++)
                                {
                                    // Gets the data info for the part
                                    var shapeDataInfo = xivMdl.MeshShapeData.ShapeParts[indexPart.PartOffset + k];

                                    // The offset in the shape data 
                                    var indexDataOffset = shapeDataInfo.MeshIndexOffset;

                                    var indexMeshLocation = 0;

                                    // Determine which mesh the info belongs to
                                    if (indexMeshNum.ContainsKey(indexDataOffset))
                                    {
                                        indexMeshLocation = indexMeshNum[indexDataOffset];

                                        // Move to the next part if it is not the current mesh
                                        if (indexMeshLocation != i)
                                        {
                                            continue;
                                        }
                                    }

                                    // Get the mesh data
                                    var mesh = lod.MeshDataList[indexMeshLocation];

                                    // Get the shape data for the current mesh
                                    var shapeDataForMesh = shapeData.GetRange(shapeDataInfo.ShapeDataOffset, shapeDataInfo.IndexCount);

                                    // Fill shape data dictionaries
                                    ushort dataIndex = ushort.MaxValue;
                                    foreach (var data in shapeDataForMesh)
                                    {
                                        var referenceIndex = 0;

                                        if (data.BaseIndex < mesh.VertexData.Indices.Count)
                                        {
                                            // Gets the index to which the data is referencing
                                            referenceIndex = mesh.VertexData.Indices[data.BaseIndex];

                                            //throw new Exception($"Reference Index is larger than the index count. Reference Index: {data.ReferenceIndexOffset}  Index Count: {mesh.VertexData.Indices.Count}");
                                        }

                                        if (!referencePositionsDictionary.ContainsKey(data.BaseIndex))
                                        {
                                            if (mesh.VertexData.Positions.Count > referenceIndex)
                                            {
                                                referencePositionsDictionary.Add(data.BaseIndex, mesh.VertexData.Positions[referenceIndex]);
                                            }
                                        }

                                        if (!meshShapePositionsDictionary.ContainsKey(data.ShapeVertex))
                                        {
                                            if (data.ShapeVertex >= mesh.VertexData.Positions.Count)
                                            {
                                                meshShapePositionsDictionary.Add(data.ShapeVertex, new Vector3(0));
                                            }
                                            else
                                            {
                                                meshShapePositionsDictionary.Add(data.ShapeVertex, mesh.VertexData.Positions[data.ShapeVertex]);
                                            }
                                        }
                                    }

                                    if (mesh.ShapePathList != null)
                                    {
                                        mesh.ShapePathList.Add(shapeInfo.Name);
                                    }
                                    else
                                    {
                                        mesh.ShapePathList = new List<string>{shapeInfo.Name};
                                    }
                                }
                            }
                        }
                    }

                    lodNum++;

                    #endregion
                }
            }

            return xivMdl;
        }


        /// <summary>
        /// Retreieves the available list of file extensions the framework has importers available for.
        /// </summary>
        /// <returns></returns>
        public List<string> GetAvailableImporters()
        {
            const string importerPath = "importers/";
            var ret = new List<string>();
            ret.Add("dae"); // DAE handler is internal.
            ret.Add("db");  // Raw already-parsed DB files are fine.

            var directories = Directory.GetDirectories(importerPath);
            foreach(var d in directories)
            {
                ret.Add((d.Replace(importerPath, "")).ToLower());
            }
            return ret;
        }

        // Just a default no-op function if we don't care about warning messages.
        private void NoOp(bool isWarning, string message)
        {
            //No-Op.
        }


        private async Task<string> RunExternalImporter(string importerName, string filePath, Action<bool, string> loggingFunction = null)
        {

            var importerFolder = Directory.GetCurrentDirectory() + "\\importers\\" + importerName;
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = importerFolder + "\\importer.exe",
                    Arguments = "\"" + filePath + "\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    WorkingDirectory = "" + importerFolder + "",
                    CreateNoWindow = true
                }
            };

            // Pipe the process output to our logging function.
            proc.OutputDataReceived += (object sender, DataReceivedEventArgs e) =>
            {
                loggingFunction(false, e.Data);
            };

            // Pipe the process output to our logging function.
            proc.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
            {
                loggingFunction(true, e.Data);
            };

            proc.EnableRaisingEvents = true;

            loggingFunction(false, "Starting " + importerName.ToUpper() + " Importer...");
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            int? code = null;
            proc.Exited += (object sender, EventArgs e) =>
            {
                code = proc.ExitCode;
            };

            return await Task.Run(async () =>
            {
                while(code == null)
                {
                    Thread.Sleep(100);
                }

                if (code != 0)
                {
                    throw (new Exception("Importer exited with error code: " + proc.ExitCode.ToString()));
                }
                return importerFolder + "\\result.db";
            });
        }

        /// <summary>
        /// Import a given model
        /// </summary>
        /// <param name="item">The current item being imported</param>
        /// <param name="currentMdl">The current (modified or unmodified) mdl for the item.</param>
        /// <param name="fileLocation">The location of the dae file to import</param>
        /// <param name="source">The source/application that is writing to the dat.</param>
        /// <param name="intermediaryFunction">Function to call after populating the TTModel but before converting it to a Mdl.
        ///     Takes in the populated TTModel.
        ///     Should return a boolean indicating whether the process should continue or cancel (false to cancel)
        /// </param>
        /// <param name="loggingFunction">
        /// Function to call when the importer receives a new log line.
        /// Takes in [bool isWarning, string message].
        /// </param>
        /// <param name="rawDataOnly">If this function should not actually finish the import and only return the raw byte data.</param>
        /// <returns>A dictionary containing any warnings encountered during import.</returns>
        public async Task ImportModel(IItemModel item, XivRace race, string path, ModelModifierOptions options = null, Action<bool, string> loggingFunction = null, Func <TTModel, Task<bool>> intermediaryFunction = null, string source = "Unknown", bool rawDataOnly = false)
        {

            #region Setup and Validation
            if (options == null)
            {
                options = new ModelModifierOptions();
            }

            if(loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            // Test the Path.
            DirectoryInfo fileLocation = null;
            try
            {
                fileLocation = new DirectoryInfo(path);
            }
            catch (Exception ex)
            {
                throw new IOException("Invalid file path.");
            }

            if (!File.Exists(fileLocation.FullName))
            {
                throw new IOException("The file provided for import does not exist");
            }

            var modding = new Modding(_gameDirectory);
            var dat = new Dat(_gameDirectory);

            // Resolve the current (possibly modded) Mdl.
            XivMdl currentMdl = null;
            try
            {
                 currentMdl = await this.GetMdlData(item, race);
            } catch(Exception ex)
            {
                // If we failed to load the MDL, see if we can get the unmodded MDL.
                var mdlPath = GetMdlPath(item, race, item.GetPrimaryItemType(), null, null, null);
                var mod = await modding.TryGetModEntry(mdlPath.Folder + "/" + mdlPath.File);
                if (mod != null)
                {
                    loggingFunction(true, "Unable to load current MDL file.  Attempting to use original MDL file...");

                    var ogOffset = mod.data.originalOffset;
                    currentMdl = await this.GetMdlData(item, IOUtil.GetRaceFromPath(path), null, mdlPath.Folder + "/" + mdlPath.File, ogOffset);
                } else
                {
                    throw new Exception("Unable to load base MDL file.");
                }
            }
            #endregion

            // Wrapping this in an await ensures we're run asynchronously on a new thread.
            await Task.Run(async () =>
            {

                #region TTModel Loading
                // Probably could stand to push this out to its own function later.
                var mdlPath = Path.Combine(currentMdl.MdlPath.Folder, currentMdl.MdlPath.File);
                
                loggingFunction = loggingFunction == null ? NoOp : loggingFunction;
                loggingFunction(false, "Starting Import of file: " + fileLocation.FullName);

                var suffix = fileLocation.Extension.ToLower();
                suffix = suffix.Substring(1);
                TTModel ttModel = null;

                // Loading and Running the actual Importers.
                if (suffix != "dae" && suffix != "db") {
                    var dbFile = await RunExternalImporter(suffix, path, loggingFunction);
                    loggingFunction(false, "Loading intermediate file...");
                    ttModel = TTModel.LoadFromFile(dbFile, loggingFunction);

                } else if (suffix == "dae")
                {
                    // Dae handling is a special snowflake.
                    var dae = new Dae(_gameDirectory, _dataFile);
                    loggingFunction(false, "Loading DAE file...");
                    ttModel = dae.ReadColladaFile(fileLocation, loggingFunction);
                    loggingFunction(false, "DAE File loaded successfully.");
                } else if (suffix == "db")
                {
                    loggingFunction(false, "Loading intermediate file...");
                    // Raw already converted DB file, just load it.
                    ttModel = TTModel.LoadFromFile(fileLocation.FullName, loggingFunction);
                }
                #endregion

                // At this point we now have a fully populated TTModel entry.
                // Time to pull in the Model Modifier for any extra steps before we pass
                // it to the raw MDL creation function.

                loggingFunction(false, "Merging in existing Attribute & Material Data...");

                XivMdl ogMdl = null;
                if (options.EnableShapeData && !ttModel.HasShapeData)
                {
                    // Load the original model if we're actually going to need it.
                    var mod = await modding.TryGetModEntry(mdlPath);
                    if (mod != null)
                    {
                        loggingFunction(false, "Loading original SE model to retrieve Shape Data...");
                        var ogOffset = mod.data.originalOffset;
                        ogMdl = await GetMdlData(item, IOUtil.GetRaceFromPath(mdlPath), null, mdlPath, ogOffset);
                    }
                }

                // Apply our Model Modifier options to the model.
                options.Apply(ttModel, currentMdl, ogMdl, loggingFunction);


                // Call the user function, if one was provided.
                if (intermediaryFunction != null)
                {
                    loggingFunction(false, "Waiting on user...");

                    // Bool says whether or not we should continue.
                    bool cont = await intermediaryFunction(ttModel);
                    if (!cont)
                    {
                        loggingFunction(false, "User cancelled import process.");
                        // This feels really dumb to cancel this via a throw, but we have no other method to do so...?
                        throw new Exception("cancel");
                    }
                }

                // Time to create the raw MDL.
                loggingFunction(false, "Creating MDL file from processed data...");
                var bytes = await MakeNewMdlFile(ttModel, currentMdl, loggingFunction);
                if (rawDataOnly)
                {
                    _rawData = bytes;
                    return;
                }

                var modEntry = await modding.TryGetModEntry(mdlPath);


                var filePath = Path.Combine(currentMdl.MdlPath.Folder, currentMdl.MdlPath.File);

                if (!rawDataOnly)
                {
                    loggingFunction(false, "Writing MDL File to FFXIV File System...");
                    await dat.WriteToDat(bytes.ToList(), modEntry, filePath, item.SecondaryCategory, item.Name, _dataFile, source, 3);
                }

                loggingFunction(false, "Job done!");
                return;
            });
        }


        /// <summary>
        /// Creates a new Mdl file from the given data
        /// </summary>
        /// <param name="ttModel">The ttModel to import</param>
        /// <param name="ogMdl">The currently modified Mdl file.</param>
        private async Task<byte[]> MakeNewMdlFile(TTModel ttModel, XivMdl ogMdl, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            try
            {
                var isAlreadyModified = false;
                var isAlreadyModified2 = false;

                // Final step modifications to the TTModel
                ModelModifiers.MakeImportReady(ttModel, loggingFunction);

                // Vertex Info
                #region Vertex Info Block

                var vertexInfoBlock = new List<byte>();
                var vertexInfoDict = new Dictionary<int, Dictionary<VertexUsageType, VertexDataType>>();

                var lodNum = 0;
                foreach (var lod in ogMdl.LoDList)
                {
                    var vdsDictionary = new Dictionary<VertexUsageType, VertexDataType>();
                    var meshMax = lodNum > 0 ? ogMdl.LoDList[lodNum].MeshCount : ttModel.MeshGroups.Count;

                    for (int meshNum = 0; meshNum < meshMax; meshNum++)
                    {
                        // Test if we have both old and new data or not.
                        var ogGroup = lod.MeshDataList.Count > meshNum ? lod.MeshDataList[meshNum] : null;
                        var ttMeshGroup = ttModel.MeshGroups.Count > meshNum ? ttModel.MeshGroups[meshNum] : null;

                        // Identify correct # of parts.
                        var partMax = lodNum == 0 ? ttMeshGroup.Parts.Count : ogGroup.MeshPartList.Count;

                        // Totals for each group
                        var ogPartCount = ogGroup == null ? 0 : lod.MeshDataList[meshNum].MeshPartList.Count;
                        var newPartCount = ttMeshGroup == null ? 0 : ttMeshGroup.Parts.Count;

                        List<VertexDataStruct> source;
                        if (ogGroup == null)
                        {
                            // New Group, copy data over.
                            source = lod.MeshDataList[0].VertexDataStructList;
                        } else
                        {
                            source = ogGroup.VertexDataStructList;
                        }

                        var dataSize = 0;
                        foreach (var vds in source)
                        {

                            // Padding
                            vertexInfoBlock.AddRange(new byte[4]);


                            var dataBlock = vds.DataBlock;
                            var dataOffset = vds.DataOffset;
                            var dataType = vds.DataType;
                            var dataUsage = vds.DataUsage;

                            if (lodNum == 0)
                            {

                                // Change Positions to Float from its default of Half for greater accuracy
                                // This increases the data from 8 bytes to 12 bytes
                                if (dataUsage == VertexUsageType.Position)
                                {
                                    // If the data type is already Float3 (in the case of an already modified model)
                                    // we skip it.
                                    if (dataType != VertexDataType.Float3)
                                    {
                                        dataType = VertexDataType.Float3;
                                    }
                                    else
                                    {
                                        isAlreadyModified2 = true;
                                    }
                                }

                                // Change Normals to Float from its default of Half for greater accuracy
                                // This increases the data from 8 bytes to 12 bytes
                                if (dataUsage == VertexUsageType.Normal)
                                {
                                    // If the data type is already Float3 (in the case of an already modified model)
                                    // we skip it.
                                    if (dataType != VertexDataType.Float3)
                                    {
                                        dataType = VertexDataType.Float3;
                                    }
                                    else
                                    {
                                        isAlreadyModified = true;
                                    }
                                }

                                // Change Texture Coordinates to Float from its default of Half for greater accuracy
                                // This increases the data from 8 bytes to 16 bytes, or from 4 bytes to 8 bytes if it is a housing item
                                if (dataUsage == VertexUsageType.TextureCoordinate)
                                {
                                    if (dataType == VertexDataType.Half2 || dataType == VertexDataType.Half4)
                                    {
                                        if (dataType == VertexDataType.Half2)
                                        {
                                            dataType = VertexDataType.Float2;
                                        }
                                        else
                                        {
                                            dataType = VertexDataType.Float4;
                                        }
                                    }
                                    else
                                    {
                                        isAlreadyModified = true;
                                    }
                                }

                                // We have to adjust each offset after the Normal value because its size changed
                                // Normal is always in data block 1 and the first so its offset is 0
                                // Note: Texture Coordinates are always last so there is no need to adjust for it
                                if (dataBlock == 1 && dataOffset > 0 && !isAlreadyModified)
                                {
                                    dataOffset += 4;
                                }
                                // We have to adjust each offset after the Normal value because its size changed
                                // Normal is always in data block 1 and the first so its offset is 0
                                // Note: Texture Coordinates are always last so there is no need to adjust for it
                                if (dataBlock == 0 && dataOffset > 0 && !isAlreadyModified2)
                                {
                                    dataOffset += 4;
                                }
                            }

                            vertexInfoBlock.Add((byte)dataBlock);
                            vertexInfoBlock.Add((byte)dataOffset);
                            vertexInfoBlock.Add((byte)dataType);
                            vertexInfoBlock.Add((byte)dataUsage);

                            if (!vdsDictionary.ContainsKey(dataUsage))
                            {
                                vdsDictionary.Add(dataUsage, dataType);
                            }

                            dataSize += 8;
                        }

                        // End flag
                        vertexInfoBlock.AddRange(new byte[4]);
                        vertexInfoBlock.Add(0xFF);
                        vertexInfoBlock.AddRange(new byte[3]);

                        dataSize += 8;

                        if (dataSize < 64)
                        {
                            var remaining = 64 - dataSize;

                            vertexInfoBlock.AddRange(new byte[remaining]);
                        }

                        // Padding between data
                        vertexInfoBlock.AddRange(new byte[72]);
                    }


                    vertexInfoDict.Add(lodNum, vdsDictionary);

                    lodNum++;
                }

                // The first vertex info block does not have padding so we remove it and add it at the end
                vertexInfoBlock.RemoveRange(0, 4);
                vertexInfoBlock.AddRange(new byte[4]);
                #endregion

                // All of the data blocks for the model data
                var fullModelDataBlock = new List<byte>();

                // Path Data
                #region Path Info Block

                var pathInfoBlock = new List<byte>();

                // Path Count
                pathInfoBlock.AddRange(BitConverter.GetBytes(0)); // Dummy value to rewrite later

                // Path Block Size
                pathInfoBlock.AddRange(BitConverter.GetBytes(0)); // Dummy value to rewrite later

                var pathCount = 0;

                // Attribute paths
                var attributeOffsetList = new List<int>();

                foreach (var atr in ttModel.Attributes)
                {
                    // Attribute offset in path data block
                    attributeOffsetList.Add(pathInfoBlock.Count - 8);

                    // Path converted to bytes
                    pathInfoBlock.AddRange(Encoding.UTF8.GetBytes(atr));

                    // Byte between paths
                    pathInfoBlock.Add(0);
                    pathCount++;
                }

                // Bone paths
                var boneOffsetList = new List<int>();
                var boneStrings = new List<string>();

                // Write the full model level list of bones.
                foreach (var bone in ttModel.Bones)
                {
                    // Bone offset in path data block
                    boneOffsetList.Add(pathInfoBlock.Count - 8);

                    // Path converted to bytes
                    pathInfoBlock.AddRange(Encoding.UTF8.GetBytes(bone));
                    boneStrings.Add(bone);

                    // Byte between paths
                    pathInfoBlock.Add(0);
                    pathCount++;
                }


                // Material paths
                var materialOffsetList = new List<int>();

                foreach (var material in ttModel.Materials)
                {
                    // Material offset in path data block
                    materialOffsetList.Add(pathInfoBlock.Count - 8);

                    // Path converted to bytes
                    pathInfoBlock.AddRange(Encoding.UTF8.GetBytes(material));

                    // Byte between paths
                    pathInfoBlock.Add(0);
                    pathCount++;
                }

                // Shape paths
                var shapeOffsetList = new List<int>();

                if (ttModel.HasShapeData)
                {
                    foreach (var shape in ttModel.ShapeNames)
                    {
                        // Shape offset in path data block
                        shapeOffsetList.Add(pathInfoBlock.Count - 8);

                        // Path converted to bytes
                        pathInfoBlock.AddRange(Encoding.UTF8.GetBytes(shape));

                        // Byte between paths
                        pathInfoBlock.Add(0);
                        pathCount++;
                    }
                }

                // Extra paths
                foreach (var extra in ogMdl.PathData.ExtraPathList)
                {
                    // Path converted to bytes
                    pathInfoBlock.AddRange(Encoding.UTF8.GetBytes(extra));

                    // Byte between paths
                    pathInfoBlock.Add(0);
                    pathCount++;
                }

                // Padding before next section
                var currentSize = pathInfoBlock.Count - 8;

                // Pad out to divisions of 8 bytes.
                var pathPadding = 2;
                pathInfoBlock.AddRange(new byte[pathPadding]);

                // Go back and rewrite our counts with correct data.
                IOUtil.ReplaceBytesAt(pathInfoBlock, BitConverter.GetBytes(pathCount), 0);
                int newPathBlockSize = pathInfoBlock.Count - 8;
                IOUtil.ReplaceBytesAt(pathInfoBlock, BitConverter.GetBytes(newPathBlockSize), 4);

                // Adjust the vertex data block offset to account for the size changes;
                var oldPathBlockSize = ogMdl.PathData.PathBlockSize;
                var pathSizeDiff = newPathBlockSize - oldPathBlockSize;
                ogMdl.LoDList[0].VertexDataOffset += pathSizeDiff;


                #endregion

                // Model Data
                #region Model Data Block

                var modelDataBlock = new List<byte>();

                var ogModelData = ogMdl.ModelData;

                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown0));
                
                short meshCount = (short)(ttModel.MeshGroups.Count + ogMdl.LoDList[0].ExtraMeshCount);
                short higherLodMeshCount = (short)(ogMdl.LoDList[2].MeshSum - ogMdl.LoDList[0].MeshSum);
                meshCount += higherLodMeshCount;
                // Update the total mesh count only if there are more meshes than the original
                // We do not remove mesh if they are missing from the DAE, we just set the mesh metadata to 0

                ogModelData.MeshCount = meshCount;
                modelDataBlock.AddRange(BitConverter.GetBytes(meshCount));

                modelDataBlock.AddRange(BitConverter.GetBytes((short)ttModel.Attributes.Count));

                var meshPartCount = ogModelData.MeshPartCount;

                // Recalculate total number of parts.
                short tParts = 0;
                for (int lIdx = 0; lIdx < ogMdl.LoDList.Count; lIdx++)
                {
                    if (lIdx == 0) {
                        foreach (var m in ttModel.MeshGroups)
                        {
                            foreach (var p in m.Parts)
                            {
                                tParts++;
                            }
                            /*
                             //Not sure when if ever the shape parts count here, but it seems like never.
                            foreach(var p in m.ShapeParts)
                            {
                                tParts++;
                            }*/
                        }
                    } else
                    {
                        foreach (var m in ogMdl.LoDList[lIdx].MeshDataList)
                        {
                            foreach(var p in m.MeshPartList)
                            {
                                tParts++;
                            }
                        }
                    }
                }

                modelDataBlock.AddRange(BitConverter.GetBytes(tParts));

                modelDataBlock.AddRange(BitConverter.GetBytes((short)ttModel.Materials.Count));
                modelDataBlock.AddRange(BitConverter.GetBytes((short)ttModel.Bones.Count));
                modelDataBlock.AddRange(BitConverter.GetBytes((short)ttModel.MeshGroups.Count));
                modelDataBlock.AddRange(BitConverter.GetBytes(ttModel.HasShapeData ? (short) ttModel.ShapeNames.Count : (short)0));
                modelDataBlock.AddRange(BitConverter.GetBytes(ttModel.HasShapeData ? (short) ttModel.ShapePartCount : (short)0));
                modelDataBlock.AddRange(BitConverter.GetBytes(ttModel.HasShapeData ? (short) ttModel.ShapeDataCount : (short)0));
                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown1));
                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown2));
                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown3));
                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown4));
                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown5));
                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown6)); // Unknown - Differential between gloves
                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown7));
                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown8)); // Unknown - Differential between gloves
                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown9));
                modelDataBlock.Add(ogModelData.Unknown10a);
                modelDataBlock.Add(ogModelData.Unknown10b);
                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown11));
                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown12));
                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown13));
                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown14));
                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown15));
                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown16));
                modelDataBlock.AddRange(BitConverter.GetBytes(ogModelData.Unknown17));



                #endregion

                // Unknown Data 0
                #region Unknown Data Block 0

                var unknownDataBlock0 = ogMdl.UnkData0.Unknown;



                #endregion


                // Get the imported data
                var importDataDictionary = GetImportData(ttModel, vertexInfoDict);

                // Extra LoD Data
                #region Extra Level Of Detail Block
                var extraLodDataBlock = new List<byte>();

                // This seems to mostly be used in furniture, but some other things
                // use it too.  Perchberd has great info on this stuff.
                if (ogMdl.ExtraLoDList != null && ogMdl.ExtraLoDList.Count > 0)
                {
                    foreach (var lod in ogMdl.ExtraLoDList)
                    {
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.MeshOffset));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.MeshCount));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.Unknown0));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.Unknown1));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.MeshEnd));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.ExtraMeshCount));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.MeshSum));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.Unknown2));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.Unknown3));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.Unknown4));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.Unknown5));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.IndexDataStart));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.Unknown6));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.Unknown7));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.VertexDataSize));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.IndexDataSize));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.VertexDataOffset));
                        extraLodDataBlock.AddRange(BitConverter.GetBytes(lod.IndexDataOffset));
                    }


                }
                #endregion


                // Mesh Data
                #region Mesh Data Block

                var meshDataBlock = new List<byte>();

                lodNum = 0;
                int lastVertexCount = 0;
                var previousIndexCount = 0;
                short totalParts = 0;
                var meshIndexOffsets = new List<int>();
                foreach (var lod in ogMdl.LoDList)
                {
                    var meshNum = 0;
                    var previousVertexDataOffset1 = 0;
                    var previousIndexDataOffset = 0;
                    var lod0VertexDataEntrySize0 = 0;
                    var lod0VertexDataEntrySize1 = 0;

                    foreach (var ttMeshGroup in ttModel.MeshGroups)
                    {
                        bool addedMesh = meshNum >= lod.MeshCount;

                        // Skip higher LoDs for any additional stuff.
                        if (lodNum > 0 && addedMesh)
                        {
                            continue;
                        }

                        var meshInfo = addedMesh ? null : lod.MeshDataList[meshNum].MeshInfo;

                        var vertexCount = addedMesh ? 0 : meshInfo.VertexCount;
                        var indexCount = addedMesh ? 0 : meshInfo.IndexCount;
                        var indexDataOffset = addedMesh ? 0 : meshInfo.IndexDataOffset;
                        var vertexDataOffset0 = addedMesh ? 0 : meshInfo.VertexDataOffset0;
                        var vertexDataOffset1 = addedMesh ? 0 : meshInfo.VertexDataOffset1;
                        var vertexDataOffset2 = addedMesh ? 0 : meshInfo.VertexDataOffset2;
                        byte vertexDataEntrySize0 = addedMesh ? (byte)lod0VertexDataEntrySize0 : meshInfo.VertexDataEntrySize0;
                        byte vertexDataEntrySize1 = addedMesh ? (byte)lod0VertexDataEntrySize1 : meshInfo.VertexDataEntrySize1;
                        byte vertexDataEntrySize2 = addedMesh ? (byte) 0 : meshInfo.VertexDataEntrySize2;
                        short partCount = addedMesh ? (short)0 : meshInfo.MeshPartCount;
                        short materialIndex = addedMesh ? (short)0 : meshInfo.MaterialIndex;
                        short boneSetIndex = addedMesh ? (short)0 : (short) 0;
                        byte vDataBlockCount = addedMesh ? (byte)0 : meshInfo.VertexDataBlockCount;



                        if (lodNum == 0)
                        {
                            vertexCount = (int)ttMeshGroup.VertexCount;
                            indexCount = (int)ttMeshGroup.IndexCount;
                            partCount = (short)ttMeshGroup.Parts.Count;
                            boneSetIndex = (short)meshNum;
                            materialIndex = ttModel.GetMaterialIndex(meshNum);
                            vDataBlockCount = 2;

                            if (!addedMesh)
                            {
                                // Since we changed Normals and Texture coordinates from half to floats, we need
                                // to adjust the vertex data entry size for that data block from 24 to 36, or from 16 to 24 if its a housing item
                                // we skip this adjustment if the model is already modified
                                if (!isAlreadyModified)
                                {
                                    var texCoordDataType = vertexInfoDict[0][VertexUsageType.TextureCoordinate];

                                    if (texCoordDataType == VertexDataType.Float2)
                                    {
                                        vertexDataEntrySize1 += 8;
                                    }
                                    else
                                    {
                                        vertexDataEntrySize1 += 12;
                                    }
                                }
                                if (!isAlreadyModified2)
                                {
                                    vertexDataEntrySize0 += 4;

                                }
                            }

                            // Add in any shape vertices.
                            if (ttModel.HasShapeData)
                            {
                                // These are effectively orphaned vertices until the shape
                                // data kicks in and rewrites the triangle index list.
                                foreach (var shapePart in ttMeshGroup.ShapeParts)
                                {
                                    vertexCount += shapePart.Vertices.Count;
                                }
                            }
                        }

                        // Calculate new index data offset
                        if (meshNum > 0)
                        {
                            // Padding used after index data block
                            var indexPadding = 8 - previousIndexCount % 8;

                            if (indexPadding == 8)
                            {
                                indexPadding = 0;
                            }

                            indexDataOffset = previousIndexDataOffset + previousIndexCount + indexPadding;
                        }

                        // Calculate new Vertex Data Offsets
                        if (meshNum > 0)
                        {
                            vertexDataOffset0 = previousVertexDataOffset1 + lastVertexCount * vertexDataEntrySize1;

                            vertexDataOffset1 = vertexDataOffset0 + vertexCount * vertexDataEntrySize0;

                        }
                        else
                        {
                            vertexDataOffset1 = vertexCount * vertexDataEntrySize0;
                        }

                            
                        lastVertexCount = vertexCount;

                        if (lod0VertexDataEntrySize0 == 0)
                        {
                            lod0VertexDataEntrySize0 = vertexDataEntrySize0;
                            lod0VertexDataEntrySize1 = vertexDataEntrySize1;
                        }

                        meshDataBlock.AddRange(BitConverter.GetBytes(vertexCount));
                        meshDataBlock.AddRange(BitConverter.GetBytes(indexCount));
                        meshDataBlock.AddRange(BitConverter.GetBytes((short)materialIndex));

                        meshDataBlock.AddRange(BitConverter.GetBytes((short)(totalParts)));
                        meshDataBlock.AddRange(BitConverter.GetBytes((short)(partCount)));
                        totalParts += partCount;

                        meshDataBlock.AddRange(BitConverter.GetBytes((short)boneSetIndex));
                        meshDataBlock.AddRange(BitConverter.GetBytes(indexDataOffset));
                        meshIndexOffsets.Add(indexDataOffset);  // Need these for later.

                        meshDataBlock.AddRange(BitConverter.GetBytes(vertexDataOffset0));
                        meshDataBlock.AddRange(BitConverter.GetBytes(vertexDataOffset1));
                        meshDataBlock.AddRange(BitConverter.GetBytes(vertexDataOffset2));
                        meshDataBlock.Add(vertexDataEntrySize0);
                        meshDataBlock.Add(vertexDataEntrySize1);
                        meshDataBlock.Add(vertexDataEntrySize2);
                        meshDataBlock.Add(vDataBlockCount);

                        previousVertexDataOffset1 = vertexDataOffset1;
                        previousIndexDataOffset = indexDataOffset;
                        previousIndexCount = indexCount;

                        meshNum++;
                    }

                    lodNum++;
                }



                #endregion

                // Unknown Attribute Data
                #region Attribute Sets

                var attrPathOffsetList = attributeOffsetList;

                var attributePathDataBlock = new List<byte>();
                foreach (var attributeOffset in attrPathOffsetList)
                {
                    attributePathDataBlock.AddRange(BitConverter.GetBytes(attributeOffset));
                }

                #endregion

                // Unknown Data 1
                #region Unknown Data Block 1

                var unknownDataBlock1 = ogMdl.UnkData1.Unknown;


                #endregion

                // Mesh Part
                #region Mesh Part Data Block

                var meshPartDataBlock = new List<byte>();

                lodNum = 0;

                short currentBoneOffset = 0;
                var previousIndexOffset = 0;
                previousIndexCount = 0;
                var indexOffset = 0;
                foreach (var lod in ogMdl.LoDList)
                {
                    var partPadding = 0;

                    // Identify the correct # of meshes
                    var meshMax = lodNum > 0 ? ogMdl.LoDList[lodNum].MeshCount : ttModel.MeshGroups.Count;

                    for(int meshNum = 0; meshNum < meshMax; meshNum++)
                    {
                        // Test if we have both old and new data or not.
                        var ogGroup = lod.MeshDataList.Count > meshNum ? lod.MeshDataList[meshNum] : null;
                        var ttMeshGroup = ttModel.MeshGroups.Count > meshNum ? ttModel.MeshGroups[meshNum] : null;

                        // Identify correct # of parts.
                        var partMax = lodNum == 0 ? ttMeshGroup.Parts.Count : ogGroup.MeshPartList.Count;

                        // Totals for each group
                        var ogPartCount = ogGroup == null ? 0 : lod.MeshDataList[meshNum].MeshPartList.Count;
                        var newPartCount = ttMeshGroup == null ? 0 : ttMeshGroup.Parts.Count;

                        // Skip higher LoD stuff for non-existent parts.
                        if (lodNum > 0 && ogGroup == null)
                        {
                            continue;
                        }


                        // Loop all the parts we should write.
                        for(var partNum = 0; partNum < partMax; partNum++)
                        {
                            // Get old and new data.
                            var ogPart = ogPartCount > partNum ? ogGroup.MeshPartList[partNum]: null;
                            var ttPart = newPartCount > partNum ? ttMeshGroup.Parts[partNum] : null;


                            // Skip higher LoD stuff for non-existent parts.
                            if(lodNum > 0 && ogPart == null)
                            {
                                continue;
                            }

                            var indexCount = 0;
                            short boneCount = 0;
                            uint attributeMask = 0;

                            if (lodNum == 0)
                            {
                                // At LoD Zero we're not importing old FFXIV data, we're importing
                                // the new stuff.

                                // Recalculate Index Offset
                                if (meshNum == 0)
                                {
                                    if (partNum == 0)
                                    {
                                        indexOffset = 0;
                                    }
                                    else
                                    {
                                        indexOffset = previousIndexOffset + previousIndexCount;
                                    }
                                }
                                else
                                {
                                    if (partNum == 0)
                                    {
                                        indexOffset = previousIndexOffset + previousIndexCount + partPadding;
                                    }
                                    else
                                    {
                                        indexOffset = previousIndexOffset + previousIndexCount;
                                    }

                                }

                                attributeMask = ttModel.GetAttributeBitmask(meshNum, partNum);
                                indexCount = ttModel.MeshGroups[meshNum].Parts[partNum].TriangleIndices.Count;

                                // Count of bones for Mesh.  High LoD Meshes get 0... Not really ideal.
                                // TODO: Fixfix - Need to save # of bones in higher lods.
                                boneCount = (short) (lodNum == 0 ? ttMeshGroup.Bones.Count : 0);

                                // Calculate padding between meshes
                                if (partNum == newPartCount - 1)
                                {
                                    var padd = (indexOffset + indexCount) % 8;

                                    if (padd != 0)
                                    {
                                        partPadding = 8 - padd;
                                    }
                                    else
                                    {
                                        partPadding = 0;
                                    }
                                }

                            }
                            else
                            {
                                // LoD non-zero
                                indexCount = ogPart.IndexCount;
                                boneCount = 0;
                                attributeMask = 0;
                            }

                            meshPartDataBlock.AddRange(BitConverter.GetBytes(indexOffset));
                            meshPartDataBlock.AddRange(BitConverter.GetBytes(indexCount));
                            meshPartDataBlock.AddRange(BitConverter.GetBytes(attributeMask));
                            meshPartDataBlock.AddRange(BitConverter.GetBytes(currentBoneOffset));
                            meshPartDataBlock.AddRange(BitConverter.GetBytes(boneCount));

                            previousIndexCount = indexCount;
                            previousIndexOffset = indexOffset;
                            currentBoneOffset += boneCount;

                        }
                    }

                    lodNum++;
                }



                #endregion

                // Unknown Data 2
                #region Unknown Data Block 2
                var unknownDataBlock2 = ogMdl.UnkData2.Unknown;
                #endregion

                // Material Offset Data
                #region Material Data Block

                var matPathOffsetList = materialOffsetList;

                var matPathOffsetDataBlock = new List<byte>();
                foreach (var materialOffset in matPathOffsetList)
                {
                    matPathOffsetDataBlock.AddRange(BitConverter.GetBytes(materialOffset));
                }

                #endregion

                // Bone Strings Offset Data
                #region Bone Data Block

                var bonePathOffsetList = boneOffsetList;

                var bonePathOffsetDataBlock = new List<byte>();
                foreach (var boneOffset in bonePathOffsetList)
                {

                    bonePathOffsetDataBlock.AddRange(BitConverter.GetBytes(boneOffset));
                }

                #endregion

                // Bone Indices for meshes
                #region Mesh Bone Sets

                var boneSetsBlock = new List<byte>();

                for(var mi = 0; mi < ttModel.MeshGroups.Count; mi++)
                {
                    boneSetsBlock.AddRange(ttModel.GetBoneSet(mi));
                }
                var boneIndexListSize = boneSetsBlock.Count;

                // Higher LoD Bone sets are omitted.

                #endregion

                #region Shape Stuff

                var FullShapeDataBlock = new List<byte>();
                if (ttModel.HasShapeData)
                {
                    #region Shape Part Counts

                    var meshShapeInfoDataBlock = new List<byte>();

                    var shapeInfoCount = ogMdl.MeshShapeData.ShapeInfoList.Count;
                    var shapePartCounts = ttModel.ShapePartCounts;

                    short runningSum = 0;
                    for(var sIdx = 0; sIdx < ttModel.ShapeNames.Count; sIdx++)
                    {

                        meshShapeInfoDataBlock.AddRange(BitConverter.GetBytes(shapeOffsetList[sIdx]));
                        var count = shapePartCounts[sIdx];

                        for (var l = 0; l < ogMdl.LoDList.Count; l++)
                        {
                            if(l == 0)
                            {
                                // LOD 0
                                meshShapeInfoDataBlock.AddRange(BitConverter.GetBytes((short)runningSum));
                                runningSum += count;

                            } else
                            {
                                // LOD 1+
                                meshShapeInfoDataBlock.AddRange(BitConverter.GetBytes((short)0));
                            }
                        }
                        for (var l = 0; l < ogMdl.LoDList.Count; l++)
                        {
                            if (l == 0)
                            {
                                // LOD 0
                                meshShapeInfoDataBlock.AddRange(BitConverter.GetBytes((short)count));

                            }
                            else
                            {
                                // LOD 1+
                                meshShapeInfoDataBlock.AddRange(BitConverter.GetBytes((short)0));
                            }
                        }
                    }

                    FullShapeDataBlock.AddRange(meshShapeInfoDataBlock);

                    #endregion

                    // Mesh Shape Index Info
                    #region Shape Parts Data Block

                    var shapePartsDataBlock = new List<byte>();
                    var parts = ttModel.ShapeParts;

                    int sum = 0;

                    foreach (var pair in parts)
                    {
                        shapePartsDataBlock.AddRange(BitConverter.GetBytes(meshIndexOffsets[pair.MeshId]));
                        shapePartsDataBlock.AddRange(BitConverter.GetBytes(pair.Part.Replacements.Count));
                        shapePartsDataBlock.AddRange(BitConverter.GetBytes(sum));

                        sum += pair.Part.Replacements.Count;
                    }

                    FullShapeDataBlock.AddRange(shapePartsDataBlock);

                    #endregion

                    // Mesh Shape Data
                    #region Raw Shape Data Data Block

                    var meshShapeDataBlock = new List<byte>();

                    var lodNumber = 0;
                    foreach (var lod in ogMdl.LoDList)
                    {
                        var indexMeshNum = new Dictionary<int, int>();

                        // Get the index data offsets in each mesh
                        for (var i = 0; i < lod.MeshCount; i++)
                        {
                            var indexDataOffset = lod.MeshDataList[i].MeshInfo.IndexDataOffset;

                            indexMeshNum.Add(indexDataOffset, i);
                        }


                        var shapeParts = ttModel.ShapeParts;

                        // We only store the shape info for LoD 0.
                        if (lodNumber == 0)
                        {
                            var newVertsSoFar = new List<uint>(new uint[ttModel.MeshGroups.Count]);
                            foreach (var p in shapeParts)
                            {
                                var meshNum = p.MeshId;
                                var baseVertexCount = ttModel.MeshGroups[meshNum].VertexCount + newVertsSoFar[meshNum];
                                foreach (var r in p.Part.Replacements)
                                {
                                    meshShapeDataBlock.AddRange(BitConverter.GetBytes((ushort) r.Key));

                                    // Shift these forward to be relative to the full mesh group, rather than
                                    // just the shape part.
                                    var vertexId = (uint) r.Value;
                                    vertexId += baseVertexCount;

                                    meshShapeDataBlock.AddRange(BitConverter.GetBytes((ushort) vertexId));
                                }

                                newVertsSoFar[meshNum] += (uint) p.Part.Vertices.Count;
                            }
                        }

                        lodNumber++;
                    }

                    FullShapeDataBlock.AddRange(meshShapeDataBlock);

                    #endregion
                }

                #endregion

                // Bone Index Part
                #region Part Bone Sets

                // These are referential arrays to subsets of their parent mesh bone set.
                // Their length is determined by the Part header's BoneCount field.
                var partBoneSetsBlock = new List<byte>();
                {
                    var bones = ttModel.Bones;

                    for (var j = 0; j < ttModel.MeshGroups.Count; j++)
                    {
                        for (short i = 0; i < ttModel.MeshGroups[j].Bones.Count; i++)
                        {
                            // It's probably not perfectly performant in game, but we can just
                            // write every bone from the parent set back in here.
                            partBoneSetsBlock.AddRange(BitConverter.GetBytes(i));
                        }
                    }

                    // Higher LoDs omitted (they're given 0 bones)

                    partBoneSetsBlock.InsertRange(0, BitConverter.GetBytes((int)(partBoneSetsBlock.Count)));
                }



                #endregion

                // Padding 
                #region Padding Data Block

                var paddingDataBlock = new List<byte>();

                paddingDataBlock.Add(ogMdl.PaddingSize);
                paddingDataBlock.AddRange(ogMdl.PaddedBytes);



                #endregion

                // Bounding Box
                #region Bounding Box Data Block

                var boundingBoxDataBlock = new List<byte>();
                
                var boundingBox = ogMdl.BoundBox;

                foreach (var point in boundingBox.PointList)
                {
                    boundingBoxDataBlock.AddRange(BitConverter.GetBytes(point.X));
                    boundingBoxDataBlock.AddRange(BitConverter.GetBytes(point.Y));
                    boundingBoxDataBlock.AddRange(BitConverter.GetBytes(point.Z));
                    boundingBoxDataBlock.AddRange(BitConverter.GetBytes(point.W));
                }



                #endregion

                // Bone Transform
                #region Bone Transform Data Block

                var boneTransformDataBlock = new List<byte>();

                for (var i = 0; i < ttModel.Bones.Count; i++)
                {
                    boneTransformDataBlock.AddRange(BitConverter.GetBytes(0f));
                    boneTransformDataBlock.AddRange(BitConverter.GetBytes(0f));
                    boneTransformDataBlock.AddRange(BitConverter.GetBytes(0f));
                    boneTransformDataBlock.AddRange(BitConverter.GetBytes(0f));

                    boneTransformDataBlock.AddRange(BitConverter.GetBytes(0f));
                    boneTransformDataBlock.AddRange(BitConverter.GetBytes(0f));
                    boneTransformDataBlock.AddRange(BitConverter.GetBytes(0f));
                    boneTransformDataBlock.AddRange(BitConverter.GetBytes(0f));
                }



                #endregion

                #region LoD Block
                // Combined Data Block Sizes
                // This is the offset to the beginning of the vertex data
                var combinedDataBlockSize = 68 + vertexInfoBlock.Count + pathInfoBlock.Count + modelDataBlock.Count + unknownDataBlock0.Length + (60 * ogMdl.LoDList.Count) + extraLodDataBlock.Count + meshDataBlock.Count +
                    attributePathDataBlock.Count + (unknownDataBlock1?.Length ?? 0) + meshPartDataBlock.Count + unknownDataBlock2.Length + matPathOffsetDataBlock.Count + bonePathOffsetDataBlock.Count +
                    boneSetsBlock.Count + FullShapeDataBlock.Count + partBoneSetsBlock.Count + paddingDataBlock.Count + boundingBoxDataBlock.Count + boneTransformDataBlock.Count;

                var lodDataBlock = new List<byte>();

                lodNum = 0;
                var importVertexDataSize = 0;
                var importIndexDataSize = 0;
                var previousVertexDataSize = 0;
                var previousindexDataSize = 0;
                var previousVertexDataOffset = 0;
                short meshOffset = 0;

                foreach (var lod in ogMdl.LoDList)
                {
                    short mCount = 0;
                    // Index Data Size is recalculated for LoD 0, because of the imported data, but remains the same
                    // for every other LoD.
                    var indexDataSize = lod.IndexDataSize;

                    // Both of these index values are always the same.
                    // Because index data starts after the vertex data, these values need to be recalculated because
                    // the imported data can add/remove vertex data
                    var indexDataStart = lod.IndexDataStart;
                    var indexDataOffset = lod.IndexDataOffset;


                    // This value is modified for LoD0 when import settings are present
                    // This value is recalculated for every other LoD because of the imported data can add/remove vertex data.
                    var vertexDataOffset = lod.VertexDataOffset;

                    // Vertex Data Size is recalculated for LoD 0, because of the imported data, but remains the same
                    // for every other LoD.
                    var vertexDataSize = lod.VertexDataSize;

                    // Calculate the new values based on imported data
                    // Note: Only the highest quality LoD is used which is LoD 0
                    if (lodNum == 0)
                    {
                        // Get the sum of the vertex data and indices for all meshes in the imported data
                        foreach (var importData in importDataDictionary)
                        {
                            mCount++;
                            MeshData meshData;

                            bool addedMesh = false;
                            // If meshes were added, no entry exists for it in the original data, so we grab the last available mesh
                            if (importData.Key >= lod.MeshDataList.Count)
                            {
                                var diff = (importData.Key + 1) - lod.MeshDataList.Count;
                                meshData = lod.MeshDataList[importData.Key - diff];
                                addedMesh = true;
                            }
                            else
                            {
                                meshData = lod.MeshDataList[importData.Key];
                            }

                            var shapeDataCount = 0;
                            // Write the shape data if it exists.
                            if (ttModel.HasShapeData && !addedMesh && lodNum == 0)
                            {
                                var entrySizeSum = meshData.MeshInfo.VertexDataEntrySize0 + meshData.MeshInfo.VertexDataEntrySize1;
                                if (!isAlreadyModified)
                                {
                                    var texCoordDataType = vertexInfoDict[0][VertexUsageType.TextureCoordinate];

                                    if (texCoordDataType == VertexDataType.Float2)
                                    {
                                        entrySizeSum += 8;
                                    }
                                    else
                                    {
                                        entrySizeSum += 12;
                                    }
                                }

                                var group = ttModel.MeshGroups[importData.Key];
                                var sum = 0;
                                foreach (var p in group.ShapeParts)
                                {
                                    sum += p.Vertices.Count;
                                }
                                shapeDataCount = sum * entrySizeSum;
                            }

                            importVertexDataSize += importData.Value.VertexData0.Count + importData.Value.VertexData1.Count + shapeDataCount;

                            var indexPadding = 16 - importData.Value.IndexData.Count % 16;
                            if (indexPadding == 16)
                            {
                                indexPadding = 0;
                            }

                            importIndexDataSize += importData.Value.IndexData.Count + indexPadding;
                        }

                        vertexDataOffset = combinedDataBlockSize;

                        vertexDataSize = importVertexDataSize;
                        indexDataSize = importIndexDataSize;

                        indexDataOffset = vertexDataOffset + vertexDataSize;
                        indexDataStart = indexDataOffset;
                    }
                    else
                    {
                        mCount = lod.MeshCount;
                        // The (vertex offset + vertex data size + index data size) of the previous LoD give you the vertex offset of the current LoD
                        vertexDataOffset = previousVertexDataOffset + previousVertexDataSize + previousindexDataSize;

                        // The (vertex data offset + vertex data size) of the current LoD give you the index offset
                        // In this case it uses the newly calculated vertex data offset to get the correct index offset
                        indexDataOffset = vertexDataOffset + vertexDataSize;
                        indexDataStart = indexDataOffset;
                    }

                    // We add any additional meshes to the offset if we added any through advanced importing, otherwise additionalMeshCount stays at 0
                    lodDataBlock.AddRange(BitConverter.GetBytes((short)meshOffset));
                    lodDataBlock.AddRange(BitConverter.GetBytes((short)mCount));

                    lodDataBlock.AddRange(BitConverter.GetBytes(lod.Unknown0));
                    lodDataBlock.AddRange(BitConverter.GetBytes(lod.Unknown1));

                    // Not sure when or how shapes are considered "extra" meshses for the sake of this var,
                    // but it seems like never?
                    short shapeMeshCount = (short)(0);
                    // We add any additional meshes to the mesh end and mesh sum if we added any through advanced imoprting, otherwise additionalMeshCount stays at 0
                    lodDataBlock.AddRange(BitConverter.GetBytes((short)(meshOffset + mCount)));
                    lodDataBlock.AddRange(BitConverter.GetBytes(shapeMeshCount));
                    lodDataBlock.AddRange(BitConverter.GetBytes((short)(meshOffset + mCount + shapeMeshCount)));
                    meshOffset += (short)(mCount + shapeMeshCount);


                    lodDataBlock.AddRange(BitConverter.GetBytes(lod.Unknown2));

                    lodDataBlock.AddRange(BitConverter.GetBytes(lod.Unknown3));
                    lodDataBlock.AddRange(BitConverter.GetBytes(lod.Unknown4));
                    lodDataBlock.AddRange(BitConverter.GetBytes(lod.Unknown5));

                    lodDataBlock.AddRange(BitConverter.GetBytes(indexDataStart));

                    lodDataBlock.AddRange(BitConverter.GetBytes(lod.Unknown6));
                    lodDataBlock.AddRange(BitConverter.GetBytes(lod.Unknown7));

                    lodDataBlock.AddRange(BitConverter.GetBytes(vertexDataSize));
                    lodDataBlock.AddRange(BitConverter.GetBytes(indexDataSize));
                    lodDataBlock.AddRange(BitConverter.GetBytes(vertexDataOffset));
                    lodDataBlock.AddRange(BitConverter.GetBytes(indexDataOffset));

                    previousVertexDataSize = vertexDataSize;
                    previousindexDataSize = indexDataSize;
                    previousVertexDataOffset = vertexDataOffset;

                    lodNum++;
                }
                #endregion

                // Combine All DataBlocks
                fullModelDataBlock.AddRange(pathInfoBlock);
                fullModelDataBlock.AddRange(modelDataBlock);
                fullModelDataBlock.AddRange(unknownDataBlock0);
                fullModelDataBlock.AddRange(lodDataBlock);
                fullModelDataBlock.AddRange(extraLodDataBlock);
                fullModelDataBlock.AddRange(meshDataBlock);
                fullModelDataBlock.AddRange(attributePathDataBlock);
                if (unknownDataBlock1 != null)
                {
                    fullModelDataBlock.AddRange(unknownDataBlock1);
                }
                fullModelDataBlock.AddRange(meshPartDataBlock);
                fullModelDataBlock.AddRange(unknownDataBlock2);
                fullModelDataBlock.AddRange(matPathOffsetDataBlock);
                fullModelDataBlock.AddRange(bonePathOffsetDataBlock);
                fullModelDataBlock.AddRange(boneSetsBlock);
                fullModelDataBlock.AddRange(FullShapeDataBlock);
                fullModelDataBlock.AddRange(partBoneSetsBlock);
                fullModelDataBlock.AddRange(paddingDataBlock);
                fullModelDataBlock.AddRange(boundingBoxDataBlock);
                fullModelDataBlock.AddRange(boneTransformDataBlock);

                // Data Compression
                #region Data Compression

                var compressedMDLData = new List<byte>();

                // Vertex Info Compression
                var compressedVertexInfo = await IOUtil.Compressor(vertexInfoBlock.ToArray());
                compressedMDLData.AddRange(BitConverter.GetBytes(16));
                compressedMDLData.AddRange(BitConverter.GetBytes(0));
                compressedMDLData.AddRange(BitConverter.GetBytes(compressedVertexInfo.Length));
                compressedMDLData.AddRange(BitConverter.GetBytes(vertexInfoBlock.Count));
                compressedMDLData.AddRange(compressedVertexInfo);

                var padding = 128 - (compressedVertexInfo.Length + 16) % 128;
                compressedMDLData.AddRange(new byte[padding]);
                var compressedVertexInfoSize = compressedVertexInfo.Length + 16 + padding;

                // Model Data Compression
                var totalModelDataCompressedSize = 0;
                var compressedModelSizes = new List<int>();

                var modelDataPartCount = (int)Math.Ceiling(fullModelDataBlock.Count / 16000f);
                var modelDataPartCountsList = new List<int>(modelDataPartCount);
                var remainingDataSize = fullModelDataBlock.Count;

                for (var i = 0; i < modelDataPartCount; i++)
                {
                    if (remainingDataSize >= 16000)
                    {
                        modelDataPartCountsList.Add(16000);
                        remainingDataSize -= 16000;
                    }
                    else
                    {
                        modelDataPartCountsList.Add(remainingDataSize);
                    }
                }

                for (var i = 0; i < modelDataPartCount; i++)
                {
                    var compressedModelData =
                        await IOUtil.Compressor(fullModelDataBlock.GetRange(i * 16000, modelDataPartCountsList[i]).ToArray());

                    compressedMDLData.AddRange(BitConverter.GetBytes(16));
                    compressedMDLData.AddRange(BitConverter.GetBytes(0));
                    compressedMDLData.AddRange(BitConverter.GetBytes(compressedModelData.Length));
                    compressedMDLData.AddRange(BitConverter.GetBytes(modelDataPartCountsList[i]));
                    compressedMDLData.AddRange(compressedModelData);

                    padding = 128 - (compressedModelData.Length + 16) % 128;
                    compressedMDLData.AddRange(new byte[padding]);

                    totalModelDataCompressedSize += compressedModelData.Length + 16 + padding;
                    compressedModelSizes.Add(compressedModelData.Length + 16 + padding);
                }
                #endregion

                // Vertex Data Block
                #region Vertex Data Block

                var vertexDataSectionList = new List<VertexDataSection>();
                var compressedMeshSizes = new List<int>();
                var compressedIndexSizes = new List<int>();

                if (ttModel.HasShapeData)
                {
                    // Shape parts need to be rewitten in specific order.
                    var parts = ttModel.ShapeParts;
                    foreach ( var p in parts)
                    {
                        // Because our imported data does not include mesh shape data, we must include it manually
                        var group = ttModel.MeshGroups[p.MeshId];
                        var importData = importDataDictionary[p.MeshId];
                        foreach (var v in p.Part.Vertices)
                        {
                            // Positions for Weapon and Monster item types are half precision floating points
                            var posDataType = vertexInfoDict[0][VertexUsageType.Position];


                            Half hx, hy, hz;
                            if (posDataType == VertexDataType.Half4)
                            {
                                hx = new Half(v.Position[0]);
                                hy = new Half(v.Position[1]);
                                hz = new Half(v.Position[2]);

                                importData.VertexData0.AddRange(BitConverter.GetBytes(hx.RawValue));
                                importData.VertexData0.AddRange(BitConverter.GetBytes(hy.RawValue));
                                importData.VertexData0.AddRange(BitConverter.GetBytes(hz.RawValue));

                                // Half float positions have a W coordinate but it is never used and is defaulted to 1.
                                var w = new Half(1);
                                importData.VertexData0.AddRange(BitConverter.GetBytes(w.RawValue));

                            }
                            // Everything else has positions as singles 
                            else
                            {
                                importData.VertexData0.AddRange(BitConverter.GetBytes(v.Position[0]));
                                importData.VertexData0.AddRange(BitConverter.GetBytes(v.Position[1]));
                                importData.VertexData0.AddRange(BitConverter.GetBytes(v.Position[2]));
                            }

                            // Furniture items do not have bone data
                            if (ttModel.HasWeights)
                            {
                                // Bone Weights
                                importData.VertexData0.AddRange(v.Weights);

                                // Bone Indices
                                importData.VertexData0.AddRange(v.BoneIds);
                            }

                            // Normals
                            hx = v.Normal[0];
                            hy = v.Normal[1];
                            hz = v.Normal[2];

                            importData.VertexData1.AddRange(BitConverter.GetBytes(hx));
                            importData.VertexData1.AddRange(BitConverter.GetBytes(hy));
                            importData.VertexData1.AddRange(BitConverter.GetBytes(hz));

                            // BiNormals
                            // Change the BiNormals based on Handedness
                            var biNormal = v.Binormal;
                            int handedness = v.Handedness ? 1 : -1;

                            // This part makes sense - Handedness defines when you need to flip the tangent/binormal...
                            // But the data gets written into the game, too, so why do we need to pre-flip it?

                            importData.VertexData1.AddRange(ConvertVectorBinormalToBytes(biNormal, handedness));


                            if (vertexInfoDict[0].ContainsKey(VertexUsageType.Tangent))
                            {
                                // 99% sure this code path is never actually used.
                                importData.VertexData1.AddRange(ConvertVectorBinormalToBytes(v.Tangent, handedness * -1));
                            }


                            importData.VertexData1.Add(v.VertexColor[0]);
                            importData.VertexData1.Add(v.VertexColor[1]);
                            importData.VertexData1.Add(v.VertexColor[2]);
                            importData.VertexData1.Add(v.VertexColor[3]);

                            // Texture Coordinates
                            var texCoordDataType = vertexInfoDict[0][VertexUsageType.TextureCoordinate];


                            importData.VertexData1.AddRange(BitConverter.GetBytes(v.UV1[0]));
                            importData.VertexData1.AddRange(BitConverter.GetBytes(v.UV1[1]));

                            if (texCoordDataType == VertexDataType.Float4)
                            {
                                importData.VertexData1.AddRange(BitConverter.GetBytes(v.UV2[0]));
                                importData.VertexData1.AddRange(BitConverter.GetBytes(v.UV2[1]));
                            }
                        }
                    }
                }


                lodNum = 0;
                foreach (var lod in ogMdl.LoDList)
                {
                    var vertexDataSection = new VertexDataSection();
                    var meshNum = 0;

                    if (lodNum == 0)
                    {
                        var totalMeshes = ttModel.MeshGroups.Count;

                        for (var i = 0; i < totalMeshes; i++)
                        {
                            var importData = importDataDictionary[meshNum];

                            vertexDataSection.VertexDataBlock.AddRange(importData.VertexData0);
                            vertexDataSection.VertexDataBlock.AddRange(importData.VertexData1);
                            vertexDataSection.IndexDataBlock.AddRange(importData.IndexData);

                            var indexPadding = (importData.IndexCount * 2) % 16;
                            if (indexPadding != 0)
                            {
                                vertexDataSection.IndexDataBlock.AddRange(new byte[16 - indexPadding]);
                            }

                            meshNum++;
                        }
                    }

                    if (lodNum > 0)
                    {
                        foreach (var meshData in lod.MeshDataList)
                        {
                            var vertexInfo = vertexInfoDict[lodNum];
                            var vertexData = GetVertexByteData(meshData.VertexData, vertexInfo, ttModel.HasWeights);

                            vertexDataSection.VertexDataBlock.AddRange(vertexData.VertexData0);
                            vertexDataSection.VertexDataBlock.AddRange(vertexData.VertexData1);
                            vertexDataSection.IndexDataBlock.AddRange(vertexData.IndexData);

                            var indexPadding = (vertexData.IndexCount * 2) % 16;

                            if (indexPadding != 0)
                            {
                                vertexDataSection.IndexDataBlock.AddRange(new byte[16 - indexPadding]);
                            }
                        }
                    }


                    // Vertex Compression
                    vertexDataSection.VertexDataBlockPartCount =
                        (int)Math.Ceiling(vertexDataSection.VertexDataBlock.Count / 16000f);
                    var vertexDataPartCounts = new List<int>(vertexDataSection.VertexDataBlockPartCount);
                    var remainingVertexData = vertexDataSection.VertexDataBlock.Count;

                    for (var i = 0; i < vertexDataSection.VertexDataBlockPartCount; i++)
                    {
                        if (remainingVertexData >= 16000)
                        {
                            vertexDataPartCounts.Add(16000);
                            remainingVertexData -= 16000;
                        }
                        else
                        {
                            vertexDataPartCounts.Add(remainingVertexData);
                        }
                    }

                    for (var i = 0; i < vertexDataSection.VertexDataBlockPartCount; i++)
                    {
                        var compressedVertexData = await IOUtil.Compressor(vertexDataSection.VertexDataBlock
                            .GetRange(i * 16000, vertexDataPartCounts[i]).ToArray());

                        compressedMDLData.AddRange(BitConverter.GetBytes(16));
                        compressedMDLData.AddRange(BitConverter.GetBytes(0));
                        compressedMDLData.AddRange(BitConverter.GetBytes(compressedVertexData.Length));
                        compressedMDLData.AddRange(BitConverter.GetBytes(vertexDataPartCounts[i]));
                        compressedMDLData.AddRange(compressedVertexData);

                        var vertexPadding = 128 - (compressedVertexData.Length + 16) % 128;
                        compressedMDLData.AddRange(new byte[vertexPadding]);

                        vertexDataSection.CompressedVertexDataBlockSize +=
                            compressedVertexData.Length + 16 + vertexPadding;

                        compressedMeshSizes.Add(compressedVertexData.Length + 16 + vertexPadding);
                    }

                    // Index Compression
                    vertexDataSection.IndexDataBlockPartCount =
                        (int)Math.Ceiling((vertexDataSection.IndexDataBlock.Count / 16000f));

                    var indexDataPartCounts = new List<int>(vertexDataSection.IndexDataBlockPartCount);
                    var remainingIndexData = vertexDataSection.IndexDataBlock.Count;

                    for (var i = 0; i < vertexDataSection.IndexDataBlockPartCount; i++)
                    {
                        if (remainingIndexData >= 16000)
                        {
                            indexDataPartCounts.Add(16000);
                            remainingIndexData -= 16000;
                        }
                        else
                        {
                            indexDataPartCounts.Add(remainingIndexData);
                        }
                    }

                    for (var i = 0; i < vertexDataSection.IndexDataBlockPartCount; i++)
                    {
                        var compressedIndexData = await IOUtil.Compressor(vertexDataSection.IndexDataBlock
                            .GetRange(i * 16000, indexDataPartCounts[i]).ToArray());

                        compressedMDLData.AddRange(BitConverter.GetBytes(16));
                        compressedMDLData.AddRange(BitConverter.GetBytes(0));
                        compressedMDLData.AddRange(BitConverter.GetBytes(compressedIndexData.Length));
                        compressedMDLData.AddRange(BitConverter.GetBytes(indexDataPartCounts[i]));
                        compressedMDLData.AddRange(compressedIndexData);

                        var indexPadding = 128 - (compressedIndexData.Length + 16) % 128;

                        compressedMDLData.AddRange(new byte[indexPadding]);

                        vertexDataSection.CompressedIndexDataBlockSize +=
                            compressedIndexData.Length + 16 + indexPadding;
                        compressedIndexSizes.Add(compressedIndexData.Length + 16 + indexPadding);
                    }

                    vertexDataSectionList.Add(vertexDataSection);

                    lodNum++;
                }
                #endregion

                // Header Creation
                #region Header Creation

                var datHeader = new List<byte>();

                // This is the most common size of header for models
                var headerLength = 256;

                var blockCount = compressedMeshSizes.Count + modelDataPartCount + 3 + compressedIndexSizes.Count;

                // If the data is large enough, the header length goes to the next larger size (add 128 bytes)
                if (blockCount > 24)
                {
                    var remainingBlocks = blockCount - 24;
                    var bytesUsed = remainingBlocks * 2;
                    var extensionNeeeded = (bytesUsed / 128) + 1;
                    var newSize = 256 + (extensionNeeeded * 128);
                    headerLength = newSize;
                }

                // Header Length
                datHeader.AddRange(BitConverter.GetBytes(headerLength));
                // Data Type (models are type 3 data)
                datHeader.AddRange(BitConverter.GetBytes(3));
                // Uncompressed size of the mdl file (68 is the header size (64) + vertex info block padding (4))
                var uncompressedSize = vertexInfoBlock.Count + fullModelDataBlock.Count + 68;
                // Add the vertex and index data block sizes to the uncomrpessed size
                foreach (var vertexDataSection in vertexDataSectionList)
                {
                    uncompressedSize += vertexDataSection.VertexDataBlock.Count + vertexDataSection.IndexDataBlock.Count;
                }

                datHeader.AddRange(BitConverter.GetBytes(uncompressedSize));

                // Max Buffer Size?
                datHeader.AddRange(BitConverter.GetBytes(compressedMDLData.Count / 128 + 16));
                // Buffer Size
                datHeader.AddRange(BitConverter.GetBytes(compressedMDLData.Count / 128));
                // Block count
                datHeader.AddRange(BitConverter.GetBytes((short)5));
                // Unknown
                datHeader.AddRange(BitConverter.GetBytes((short)256));

                // Vertex Info Block Uncompressed
                var datPadding = 128 - vertexInfoBlock.Count % 128;
                datPadding = datPadding == 128 ? 0 : datPadding;
                datHeader.AddRange(BitConverter.GetBytes(vertexInfoBlock.Count + datPadding));
                // Model Data Block Uncompressed
                datPadding = 128 - fullModelDataBlock.Count % 128;
                datPadding = datPadding == 128 ? 0 : datPadding;
                datHeader.AddRange(BitConverter.GetBytes(fullModelDataBlock.Count + datPadding));
                // Vertex Data Block LoD[0] Uncompressed
                datPadding = 128 - vertexDataSectionList[0].VertexDataBlock.Count % 128;
                datPadding = datPadding == 128 ? 0 : datPadding;
                datHeader.AddRange(BitConverter.GetBytes(vertexDataSectionList[0].VertexDataBlock.Count + datPadding));
                // Vertex Data Block LoD[1] Uncompressed
                datPadding = 128 - vertexDataSectionList[1].VertexDataBlock.Count % 128;
                datPadding = datPadding == 128 ? 0 : datPadding;
                datHeader.AddRange(BitConverter.GetBytes(vertexDataSectionList[1].VertexDataBlock.Count + datPadding));
                // Vertex Data Block LoD[2] Uncompressed
                datPadding = 128 - vertexDataSectionList[2].VertexDataBlock.Count % 128;
                datPadding = datPadding == 128 ? 0 : datPadding;
                datHeader.AddRange(BitConverter.GetBytes(vertexDataSectionList[2].VertexDataBlock.Count + datPadding));
                // Blank 1
                datHeader.AddRange(BitConverter.GetBytes(0));
                // Blank 2
                datHeader.AddRange(BitConverter.GetBytes(0));
                // Blank 3
                datHeader.AddRange(BitConverter.GetBytes(0));
                // Index Data Block LoD[0] Uncompressed
                datPadding = 128 - vertexDataSectionList[0].IndexDataBlock.Count % 128;
                datPadding = datPadding == 128 ? 0 : datPadding;
                datHeader.AddRange(BitConverter.GetBytes(vertexDataSectionList[0].IndexDataBlock.Count + datPadding));
                // Index Data Block LoD[1] Uncompressed
                datPadding = 128 - vertexDataSectionList[1].IndexDataBlock.Count % 128;
                datPadding = datPadding == 128 ? 0 : datPadding;
                datHeader.AddRange(BitConverter.GetBytes(vertexDataSectionList[1].IndexDataBlock.Count + datPadding));
                // Index Data Block LoD[2] Uncompressed
                datPadding = 128 - vertexDataSectionList[2].IndexDataBlock.Count % 128;
                datPadding = datPadding == 128 ? 0 : datPadding;
                datHeader.AddRange(BitConverter.GetBytes(vertexDataSectionList[2].IndexDataBlock.Count + datPadding));

                // Vertex Info Block Compressed
                datHeader.AddRange(BitConverter.GetBytes(compressedVertexInfoSize));
                // Model Data Block Compressed
                datHeader.AddRange(BitConverter.GetBytes(totalModelDataCompressedSize));
                // Vertex Data Block LoD[0] Compressed
                datHeader.AddRange(BitConverter.GetBytes(vertexDataSectionList[0].CompressedVertexDataBlockSize));
                // Vertex Data Block LoD[1] Compressed
                datHeader.AddRange(BitConverter.GetBytes(vertexDataSectionList[1].CompressedVertexDataBlockSize));
                // Vertex Data Block LoD[2] Compressed
                datHeader.AddRange(BitConverter.GetBytes(vertexDataSectionList[2].CompressedVertexDataBlockSize));
                // Blank 1
                datHeader.AddRange(BitConverter.GetBytes(0));
                // Blank 2
                datHeader.AddRange(BitConverter.GetBytes(0));
                // Blank 3
                datHeader.AddRange(BitConverter.GetBytes(0));
                // Index Data Block LoD[0] Compressed
                datHeader.AddRange(BitConverter.GetBytes(vertexDataSectionList[0].CompressedIndexDataBlockSize));
                // Index Data Block LoD[1] Compressed
                datHeader.AddRange(BitConverter.GetBytes(vertexDataSectionList[1].CompressedIndexDataBlockSize));
                // Index Data Block LoD[2] Compressed
                datHeader.AddRange(BitConverter.GetBytes(vertexDataSectionList[2].CompressedIndexDataBlockSize));

                var vertexInfoOffset = 0;
                var modelDataOffset = compressedVertexInfoSize;
                var vertexDataBlock1Offset = modelDataOffset + totalModelDataCompressedSize;
                var indexDataBlock1Offset = vertexDataBlock1Offset + vertexDataSectionList[0].CompressedVertexDataBlockSize;
                var vertexDataBlock2Offset = indexDataBlock1Offset + vertexDataSectionList[0].CompressedIndexDataBlockSize;
                var indexDataBlock2Offset = vertexDataBlock2Offset + vertexDataSectionList[1].CompressedVertexDataBlockSize;
                var vertexDataBlock3Offset = indexDataBlock2Offset + vertexDataSectionList[1].CompressedIndexDataBlockSize;
                var indexDataBlock3Offset = vertexDataBlock3Offset + vertexDataSectionList[2].CompressedVertexDataBlockSize;

                // Vertex Info Offset
                datHeader.AddRange(BitConverter.GetBytes(vertexInfoOffset));
                // Model Data Offset
                datHeader.AddRange(BitConverter.GetBytes(modelDataOffset));
                // Vertex Data Block LoD[0] Offset
                datHeader.AddRange(BitConverter.GetBytes(vertexDataBlock1Offset));
                // Vertex Data Block LoD[1] Offset
                datHeader.AddRange(BitConverter.GetBytes(vertexDataBlock2Offset));
                // Vertex Data Block LoD[2] Offset
                datHeader.AddRange(BitConverter.GetBytes(vertexDataBlock3Offset));
                // Blank 1
                datHeader.AddRange(BitConverter.GetBytes(0));
                // Blank 2
                datHeader.AddRange(BitConverter.GetBytes(0));
                // Blank 3
                datHeader.AddRange(BitConverter.GetBytes(0));
                // Index Data Block LoD[0] Offset
                datHeader.AddRange(BitConverter.GetBytes(indexDataBlock1Offset));
                // Index Data Block LoD[1] Offset
                datHeader.AddRange(BitConverter.GetBytes(indexDataBlock2Offset));
                // Index Data Block LoD[2] Offset
                datHeader.AddRange(BitConverter.GetBytes(indexDataBlock3Offset));

                var vertexDataBlock1 = 1 + modelDataPartCount;
                var indexDataBlock1 = vertexDataBlock1 + vertexDataSectionList[0].VertexDataBlockPartCount;
                var vertexDataBlock2 = indexDataBlock1 + vertexDataSectionList[0].IndexDataBlockPartCount;
                var indexDataBlock2 = vertexDataBlock2 + vertexDataSectionList[1].VertexDataBlockPartCount;
                var vertexDataBlock3 = indexDataBlock2 + 1;
                var indexDataBlock3 = vertexDataBlock3 + vertexDataSectionList[2].VertexDataBlockPartCount;

                // Vertex Info Index
                datHeader.AddRange(BitConverter.GetBytes((short)0));
                // Model Data Index
                datHeader.AddRange(BitConverter.GetBytes((short)1));
                // Vertex Data Block LoD[0] Index
                datHeader.AddRange(BitConverter.GetBytes((ushort)vertexDataBlock1));
                // Vertex Data Block LoD[1] Index
                datHeader.AddRange(BitConverter.GetBytes((ushort)vertexDataBlock2));
                // Vertex Data Block LoD[2] Index
                datHeader.AddRange(BitConverter.GetBytes((ushort)vertexDataBlock3));
                // Blank 1 (Copies Indices?)
                datHeader.AddRange(BitConverter.GetBytes((ushort)indexDataBlock1));
                // Blank 2 (Copies Indices?)
                datHeader.AddRange(BitConverter.GetBytes((ushort)indexDataBlock2));
                // Blank 3 (Copies Indices?)
                datHeader.AddRange(BitConverter.GetBytes((ushort)indexDataBlock3));
                // Index Data Block LoD[0] Index
                datHeader.AddRange(BitConverter.GetBytes((ushort)indexDataBlock1));
                // Index Data Block LoD[1] Index
                datHeader.AddRange(BitConverter.GetBytes((ushort)indexDataBlock2));
                // Index Data Block LoD[2] Index
                datHeader.AddRange(BitConverter.GetBytes((ushort)indexDataBlock3));

                // Vertex Info Part Count
                datHeader.AddRange(BitConverter.GetBytes((short)1));
                // Model Data Part Count
                datHeader.AddRange(BitConverter.GetBytes((ushort)modelDataPartCount));
                // Vertex Data Block LoD[0] part count
                datHeader.AddRange(BitConverter.GetBytes((ushort)vertexDataSectionList[0].VertexDataBlockPartCount));
                // Vertex Data Block LoD[1] part count
                datHeader.AddRange(BitConverter.GetBytes((ushort)vertexDataSectionList[1].VertexDataBlockPartCount));
                // Vertex Data Block LoD[2] part count
                datHeader.AddRange(BitConverter.GetBytes((ushort)vertexDataSectionList[2].VertexDataBlockPartCount));
                // Blank 1
                datHeader.AddRange(BitConverter.GetBytes((short)0));
                // Blank 2
                datHeader.AddRange(BitConverter.GetBytes((short)0));
                // Blank 3
                datHeader.AddRange(BitConverter.GetBytes((short)0));
                // Index Data Block LoD[0] part count
                datHeader.AddRange(BitConverter.GetBytes((ushort)vertexDataSectionList[0].IndexDataBlockPartCount));
                // Index Data Block LoD[1] part count
                datHeader.AddRange(BitConverter.GetBytes((ushort)vertexDataSectionList[1].IndexDataBlockPartCount));
                // Index Data Block LoD[2] part count
                datHeader.AddRange(BitConverter.GetBytes((ushort)vertexDataSectionList[2].IndexDataBlockPartCount));

                // Mesh Count
                datHeader.AddRange(BitConverter.GetBytes((ushort)ogModelData.MeshCount));
                // Material Count
                datHeader.AddRange(BitConverter.GetBytes((ushort)ogModelData.MaterialCount));
                // Unknown 1
                datHeader.AddRange(BitConverter.GetBytes((short)259));
                // Unknown 2
                datHeader.AddRange(BitConverter.GetBytes((short)0));

                var vertexDataBlockCount = 0;
                // Vertex Info Padded Size
                datHeader.AddRange(BitConverter.GetBytes((ushort)compressedVertexInfoSize));
                // Model Data Padded Size
                for (var i = 0; i < modelDataPartCount; i++)
                {
                    datHeader.AddRange(BitConverter.GetBytes((ushort)compressedModelSizes[i]));
                }

                // Vertex Data Block LoD[0] part padded sizes
                for (var i = 0; i < vertexDataSectionList[0].VertexDataBlockPartCount; i++)
                {
                    datHeader.AddRange(BitConverter.GetBytes((ushort)compressedMeshSizes[i]));
                }

                vertexDataBlockCount += vertexDataSectionList[0].VertexDataBlockPartCount;

                // Index Data Block LoD[0] padded size
                for (var i = 0; i < vertexDataSectionList[0].IndexDataBlockPartCount; i++)
                {
                    datHeader.AddRange(BitConverter.GetBytes((ushort)compressedIndexSizes[i]));
                }

                // Vertex Data Block LoD[1] part padded sizes
                for (var i = 0; i < vertexDataSectionList[1].VertexDataBlockPartCount; i++)
                {
                    datHeader.AddRange(BitConverter.GetBytes((ushort)compressedMeshSizes[vertexDataBlockCount + i]));
                }

                vertexDataBlockCount += vertexDataSectionList[1].VertexDataBlockPartCount;

                // Index Data Block LoD[1] padded size
                datHeader.AddRange(BitConverter.GetBytes((ushort)vertexDataSectionList[1].CompressedIndexDataBlockSize));

                // Vertex Data Block LoD[2] part padded sizes
                for (var i = 0; i < vertexDataSectionList[2].VertexDataBlockPartCount; i++)
                {
                    datHeader.AddRange(BitConverter.GetBytes((ushort)compressedMeshSizes[vertexDataBlockCount + i]));
                }

                // Index Data Block LoD[2] padded size
                datHeader.AddRange(BitConverter.GetBytes((ushort)vertexDataSectionList[2].CompressedIndexDataBlockSize));

                if (datHeader.Count != headerLength)
                {
                    var headerEnd = headerLength - datHeader.Count % headerLength;
                    datHeader.AddRange(new byte[headerEnd]);
                }

                // Add the header to the MDL data
                compressedMDLData.InsertRange(0, datHeader);

                return compressedMDLData.ToArray();

                #endregion
            }
            catch(Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// Gets the import data in byte format
        /// </summary>
        /// <param name="colladaMeshDataList">The list of mesh data obtained from the imported collada file</param>
        /// <param name="itemType">The item type</param>
        /// <returns>A dictionary containing the vertex byte data per mesh</returns>
        private Dictionary<int, VertexByteData> GetImportData(TTModel ttModel, Dictionary<int, Dictionary<VertexUsageType, VertexDataType>> vertexInfoDict)
        {
            var importDataDictionary = new Dictionary<int, VertexByteData>();

            var meshNumber = 0;


            // Add the first vertex data set to the ImportData list
            // This contains [ Position, Bone Weights, Bone Indices]
            foreach(var m in ttModel.MeshGroups)
            {
                var importData = new VertexByteData()
                {
                    VertexData0 = new List<byte>(),
                    VertexData1 = new List<byte>(),
                    IndexData = new List<byte>(),
                    VertexCount = (int)m.VertexCount,
                    IndexCount = (int)m.IndexCount
                };

                foreach (var p in m.Parts)
                {
                    foreach (var v in p.Vertices)
                    {
                        // Positions for Weapon and Monster item types are half precision floating points
                        var posDataType = vertexInfoDict[0][VertexUsageType.Position];


                        Half hx, hy, hz;
                        if (posDataType == VertexDataType.Half4)
                        {
                            hx = new Half(v.Position[0]);
                            hy = new Half(v.Position[1]);
                            hz = new Half(v.Position[2]);

                            importData.VertexData0.AddRange(BitConverter.GetBytes(hx.RawValue));
                            importData.VertexData0.AddRange(BitConverter.GetBytes(hy.RawValue));
                            importData.VertexData0.AddRange(BitConverter.GetBytes(hz.RawValue));

                            // Half float positions have a W coordinate but it is never used and is defaulted to 1.
                            var w = new Half(1);
                            importData.VertexData0.AddRange(BitConverter.GetBytes(w.RawValue));

                        }
                        // Everything else has positions as singles 
                        else
                        {
                            importData.VertexData0.AddRange(BitConverter.GetBytes(v.Position[0]));
                            importData.VertexData0.AddRange(BitConverter.GetBytes(v.Position[1]));
                            importData.VertexData0.AddRange(BitConverter.GetBytes(v.Position[2]));
                        }

                        // Furniture items do not have bone data
                        if (ttModel.HasWeights)
                        {
                            // Bone Weights
                            importData.VertexData0.AddRange(v.Weights);

                            // Bone Indices
                            importData.VertexData0.AddRange(v.BoneIds);
                        }

                        // Normals
                        hx = v.Normal[0];
                        hy = v.Normal[1];
                        hz = v.Normal[2];

                        importData.VertexData1.AddRange(BitConverter.GetBytes(hx));
                        importData.VertexData1.AddRange(BitConverter.GetBytes(hy));
                        importData.VertexData1.AddRange(BitConverter.GetBytes(hz));

                        // BiNormals
                        // Change the BiNormals based on Handedness
                        var biNormal = v.Binormal;
                        int handedness = v.Handedness ? 1 : -1;

                        // This part makes sense - Handedness defines when you need to flip the tangent/binormal...
                        // But the data gets written into the game, too, so why do we need to pre-flip it?

                        importData.VertexData1.AddRange(ConvertVectorBinormalToBytes(biNormal, handedness));


                        if (vertexInfoDict[0].ContainsKey(VertexUsageType.Tangent))
                        {
                            // 99% sure this code path is never actually used.
                            importData.VertexData1.AddRange(ConvertVectorBinormalToBytes(v.Tangent, handedness * -1));
                        }


                        importData.VertexData1.Add(v.VertexColor[0]);
                        importData.VertexData1.Add(v.VertexColor[1]);
                        importData.VertexData1.Add(v.VertexColor[2]);
                        importData.VertexData1.Add(v.VertexColor[3]);

                        // Texture Coordinates
                        var texCoordDataType = vertexInfoDict[0][VertexUsageType.TextureCoordinate];


                        importData.VertexData1.AddRange(BitConverter.GetBytes(v.UV1[0]));
                        importData.VertexData1.AddRange(BitConverter.GetBytes(v.UV1[1]));

                        if (texCoordDataType == VertexDataType.Float4)
                        {
                            importData.VertexData1.AddRange(BitConverter.GetBytes(v.UV2[0]));
                            importData.VertexData1.AddRange(BitConverter.GetBytes(v.UV2[1]));
                        }
                    }

                }

                var count = m.IndexCount;
                for (var i = 0; i < count; i++) 
                {
                    var index = m.GetIndexAt(i);
                    importData.IndexData.AddRange(BitConverter.GetBytes(((ushort)index)));
                }
                // Add the import data to the dictionary
                importDataDictionary.Add(meshNumber, importData);
                meshNumber++;
            }

            return importDataDictionary;
        }

        /// <summary>
        /// Converts a given Vector 3 Binormal into the the byte4 format SE uses for storing Binormal data.
        /// </summary>
        /// <param name="normal"></param>
        /// <param name="handedness"></param>
        /// <returns></returns>
        private static List<byte> ConvertVectorBinormalToBytes(Vector3 normal, int handedness)
        {
            // These four byte vector values are represented as
            // [ Byte x, Byte y, Byte z, Byte handedness(0/255) ]


            // Now, this is where things get a little weird compared to storing most 3D Models.
            // SE's standard format is to include BINOMRAL(aka Bitangent) data, but leave TANGENT data out, to be calculated on the fly from the BINORMAL data.
            // This is kind of reverse compared to most math you'll find where the TANGENT is kept, and the BINORMAL is calculated on the fly. (Or both are kept/both are generated on load)

            // The Binormal data has already had the handedness applied to generate an appropriate binormal, but we store
            // that handedness after for use when the game (or textools) regenerates the Tangent from the Normal + Binormal.

            var bytes = new List<byte>(4);
            var vec = normal;
            vec.Normalize();


            // The possible range of -1 to 1 Vector X/Y/Z Values are compressed
            // into a 0-255 range.

            // A simple way to solve this cleanly is to translate the vector by [1] in all directions
            // So the vector's range is 0 to 2.
            vec += Vector3.One;

            // And then multiply the resulting value times (255 / 2), and round off the result.
            // This helps minimize errors that arise from quirks in floating point arithmetic.
            var x = (byte) Math.Round(vec.X * (255f / 2f));
            var y = (byte) Math.Round(vec.Y * (255f / 2f));
            var z = (byte) Math.Round(vec.Z * (255f / 2f));


            bytes.Add(x);
            bytes.Add(y);
            bytes.Add(z);

            // Add handedness bit
            if (handedness < 0)
            {
                bytes.Add(0);
            } else
            {
                bytes.Add(255);
            }

            return bytes;
        }

        /// <summary>
        /// Get the vertex data in byte format
        /// </summary>
        /// <param name="vertexData">The vertex data to convert</param>
        /// <param name="itemType">The item type</param>
        /// <returns>A class containing the byte data for the given data</returns>
        private static VertexByteData GetVertexByteData(VertexData vertexData, Dictionary<VertexUsageType, VertexDataType> vertexInfoDict, bool hasWeights)
        {
            var vertexByteData = new VertexByteData
            {
                VertexCount = vertexData.Positions.Count,
                IndexCount = vertexData.Indices.Count
            };

            // Vertex Block 0
            for (var i = 0; i < vertexData.Positions.Count; i++)
            {
                if (vertexInfoDict[VertexUsageType.Position] == VertexDataType.Half4)
                {
                    var x = new Half(vertexData.Positions[i].X);
                    var y = new Half(vertexData.Positions[i].Y);
                    var z = new Half(vertexData.Positions[i].Z);

                    vertexByteData.VertexData0.AddRange(BitConverter.GetBytes(x.RawValue));
                    vertexByteData.VertexData0.AddRange(BitConverter.GetBytes(y.RawValue));
                    vertexByteData.VertexData0.AddRange(BitConverter.GetBytes(z.RawValue));

                    // Half float positions have a W coordinate but it is never used and is defaulted to 1.
                    var w = new Half(1.0f);
                    vertexByteData.VertexData0.AddRange(BitConverter.GetBytes(w.RawValue));
                }
                // If positions are not Half values, they are single values
                else
                {
                    vertexByteData.VertexData0.AddRange(BitConverter.GetBytes(vertexData.Positions[i].X));
                    vertexByteData.VertexData0.AddRange(BitConverter.GetBytes(vertexData.Positions[i].Y));
                    vertexByteData.VertexData0.AddRange(BitConverter.GetBytes(vertexData.Positions[i].Z));
                }

                if (hasWeights)
                {
                    // Bone Weights
                    foreach (var boneWeight in vertexData.BoneWeights[i])
                    {
                        vertexByteData.VertexData0.Add((byte)Math.Round(boneWeight * 255f));
                    }

                    // Bone Indices
                    vertexByteData.VertexData0.AddRange(vertexData.BoneIndices[i]);
                }
            }

            // Vertex Block 1
            for (var i = 0; i < vertexData.Normals.Count; i++)
            {
                if (vertexInfoDict[VertexUsageType.Normal] == VertexDataType.Half4)
                {
                    // Normals
                    var x = new Half(vertexData.Normals[i].X);
                    var y = new Half(vertexData.Normals[i].Y);
                    var z = new Half(vertexData.Normals[i].Z);
                    var w = new Half(0);

                    vertexByteData.VertexData1.AddRange(BitConverter.GetBytes(x.RawValue));
                    vertexByteData.VertexData1.AddRange(BitConverter.GetBytes(y.RawValue));
                    vertexByteData.VertexData1.AddRange(BitConverter.GetBytes(z.RawValue));
                    vertexByteData.VertexData1.AddRange(BitConverter.GetBytes(w.RawValue));
                }
                else
                {
                    // Normals
                    var x = vertexData.Normals[i].X;
                    var y = vertexData.Normals[i].Y;
                    var z = vertexData.Normals[i].Z;

                    vertexByteData.VertexData1.AddRange(BitConverter.GetBytes(x));
                    vertexByteData.VertexData1.AddRange(BitConverter.GetBytes(y));
                    vertexByteData.VertexData1.AddRange(BitConverter.GetBytes(z));
                }


                // BiNormals - GetVertexByteData
                vertexByteData.VertexData1.AddRange(ConvertVectorBinormalToBytes(vertexData.BiNormals[i], vertexData.BiNormalHandedness[i]));

                // Tangents
                if (vertexInfoDict.ContainsKey(VertexUsageType.Tangent))
                {
                    vertexByteData.VertexData1.AddRange(ConvertVectorBinormalToBytes(vertexData.Tangents[i], vertexData.BiNormalHandedness[i]));
                }

                // Colors
                if (vertexData.Colors.Count > 0)
                {
                    var colorVector = vertexData.Colors[i].ToVector4();

                    vertexByteData.VertexData1.Add((byte)(colorVector.W * 255));
                    vertexByteData.VertexData1.Add((byte)(colorVector.X * 255));
                    vertexByteData.VertexData1.Add((byte)(colorVector.Y * 255));
                    vertexByteData.VertexData1.Add((byte)(colorVector.Z * 255));
                }

                var texCoordDataType = vertexInfoDict[VertexUsageType.TextureCoordinate];

                if (texCoordDataType == VertexDataType.Float2 || texCoordDataType == VertexDataType.Float4)
                {
                    var tc0x = vertexData.TextureCoordinates0[i].X;
                    var tc0y = vertexData.TextureCoordinates0[i].Y * -1f;

                    vertexByteData.VertexData1.AddRange(BitConverter.GetBytes(tc0x));
                    vertexByteData.VertexData1.AddRange(BitConverter.GetBytes(tc0y));

                    if (vertexData.TextureCoordinates1.Count > 0)
                    {
                        var tc1x = vertexData.TextureCoordinates1[i].X;
                        var tc1y = vertexData.TextureCoordinates1[i].Y;

                        vertexByteData.VertexData1.AddRange(BitConverter.GetBytes(tc1x));
                        vertexByteData.VertexData1.AddRange(BitConverter.GetBytes(tc1y));
                    }
                }
                else
                {
                    // Texture Coordinates
                    var tc0x = new Half(vertexData.TextureCoordinates0[i].X);
                    var tc0y = new Half(vertexData.TextureCoordinates0[i].Y);

                    vertexByteData.VertexData1.AddRange(BitConverter.GetBytes(tc0x.RawValue));
                    vertexByteData.VertexData1.AddRange(BitConverter.GetBytes(tc0y.RawValue));

                    if (vertexData.TextureCoordinates1.Count > 0)
                    {
                        var tc1x = new Half(vertexData.TextureCoordinates1[i].X);
                        var tc1y = new Half(vertexData.TextureCoordinates1[i].Y);

                        vertexByteData.VertexData1.AddRange(BitConverter.GetBytes(tc1x.RawValue));
                        vertexByteData.VertexData1.AddRange(BitConverter.GetBytes(tc1y.RawValue));
                    }
                }

            }

            // Indices
            foreach (var index in vertexData.Indices)
            {
                vertexByteData.IndexData.AddRange(BitConverter.GetBytes((ushort)index));
            }

            return vertexByteData;
        }

        /// <summary>
        /// Calculate the missing Tangent data from a model based on the existent Normal and Binormal data.
        /// </summary>
        /// <param name="normals"></param>
        /// <param name="binormals"></param>
        /// <param name="handedness"></param>
        /// <returns></returns>
        public static Vector3Collection CalculateTangentsFromBinormals(Vector3Collection normals, Vector3Collection binormals, List<byte> handedness)
        {
            var tangents = new Vector3Collection(binormals.Count);
            if (normals.Count != binormals.Count || normals.Count != handedness.Count)
            {
                return tangents;
            }
            for(var idx = 0; idx < normals.Count; idx++)
            {
                var tangent = Vector3.Cross(normals[idx], binormals[idx]);
                tangent*= (handedness[idx] == 0 ? 1 : -1 );
                tangents.Add(tangent);
            }
            return tangents;
        }
        /// <summary>
        /// Calculate the missing Tangent data from a model based on a single point of existent Normal and Binormal data.
        /// </summary>
        /// <param name="normals"></param>
        /// <param name="binormals"></param>
        /// <param name="handedness"></param>
        /// <returns></returns>
        public static Vector3 CalculateTangentFromBinormal(Vector3 normal, Vector3 binormal, byte handedness)
        {
            var tangent = Vector3.Cross(normal, binormal);
            tangent *= (handedness == 0 ? 1 : -1);
            return tangent;
        }

        /// <summary>
        /// Calculates the tangent data for given mesh.
        /// </summary>
        /// <param name="triangleIndices">The list of indexes to serve when generating triangles from the other fields</param>
        /// <param name="positions"></param>
        /// <param name="normals"></param>
        /// <param name="uvCoordinates"></param>
        /// <param name="outTangents"></param>
        /// <param name="outBitangents"></param>
        /// <param name="outHandedness"></param>
        public static void CalculateTangentData(List<int> triangleIndices, List<Vector3> positions, List<Vector3> normals, List<Vector2> uvCoordinates, out List<Vector3> outTangents, out List<Vector3> outBitangents, out List<int> outHandedness)
        {
            // Sanity checks on argument structure.
            if (positions.Count != normals.Count || positions.Count != uvCoordinates.Count || triangleIndices.Count % 3 != 0)
            {
                throw (new Exception("Invalid arguments for tangent calculation."));
            }

            // Set up arrays.
            outTangents = new List<Vector3>(positions.Count);
            outTangents.AddRange(Enumerable.Repeat(Vector3.Zero, positions.Count));

            outBitangents = new List<Vector3>(positions.Count);
            outBitangents.AddRange(Enumerable.Repeat(Vector3.Zero, positions.Count));

            outHandedness = new List<int>(positions.Count);
            outHandedness.AddRange(Enumerable.Repeat(0, positions.Count));

            // Interim arrays for calculations
            var tangents = new List<Vector3>(positions.Count);
            tangents.AddRange(Enumerable.Repeat(Vector3.Zero, positions.Count));
            var bitangents = new List<Vector3>(positions.Count);
            bitangents.AddRange(Enumerable.Repeat(Vector3.Zero, positions.Count));

            // Make sure there's actually data to use...
            if (positions.Count == 0 || triangleIndices.Count == 0)
            {
                return;
            }

            var maxIndex = triangleIndices.Max();
            if (maxIndex >= positions.Count || maxIndex >= normals.Count || maxIndex > uvCoordinates.Count)
            {
                // Some unknown amount of indexes are invalid, just fail the whole thing.
                return;
            }


            // Calculate Tangent, Bitangent/Binormal and Handedness.

            // This loops for each TRI, building up the sum
            // tangent/bitangent angles at each VERTEX.
            for (var a = 0; a < triangleIndices.Count; a += 3)
            {
                var vertex1 = triangleIndices[a];
                var vertex2 = triangleIndices[a + 1];
                var vertex3 = triangleIndices[a + 2];

                    var position1 = positions[vertex1];
                var position2 = positions[vertex2];
                var position3 = positions[vertex3];
                var uv1 = uvCoordinates[vertex1];
                var uv2 = uvCoordinates[vertex2];
                var uv3 = uvCoordinates[vertex3];
                var deltaX1 = position2.X - position1.X;
                var deltaX2 = position3.X - position1.X;
                var deltaY1 = position2.Y - position1.Y;
                var deltaY2 = position3.Y - position1.Y;
                var deltaZ1 = position2.Z - position1.Z;
                var deltaZ2 = position3.Z - position1.Z;
                var deltaU1 = uv2.X - uv1.X;
                var deltaU2 = uv3.X - uv1.X;
                var deltaV1 = uv2.Y - uv1.Y;
                var deltaV2 = uv3.Y - uv1.Y;
                var r = 1.0f / (deltaU1 * deltaV2 - deltaU2 * deltaV1);
                var sdir = new Vector3((deltaV2 * deltaX1 - deltaV1 * deltaX2) * r, (deltaV2 * deltaY1 - deltaV1 * deltaY2) * r, (deltaV2 * deltaZ1 - deltaV1 * deltaZ2) * r);
                var tdir = new Vector3((deltaU1 * deltaX2 - deltaU2 * deltaX1) * r, (deltaU1 * deltaY2 - deltaU2 * deltaY1) * r, (deltaU1 * deltaZ2 - deltaU2 * deltaZ1) * r);

                tangents[vertex1] += sdir;
                tangents[vertex2] += sdir;
                tangents[vertex3] += sdir;

                bitangents[vertex1] += tdir;
                bitangents[vertex2] += tdir;
                bitangents[vertex3] += tdir;
            }

            // Loop the VERTEXES now to calculate the end tangent/bitangents based on the summed data for each VERTEX
            for (var a = 0; a < positions.Count; ++a)
            {
                // Reference: https://marti.works/posts/post-calculating-tangents-for-your-mesh/post/
                // We were already doing these calculations to establish handedness, but we weren't actually
                // using the other results before.  Better to kill the previous computations and use these numbers
                // for everything to avoid minor differences causing errors.

                //var posIdx = vDict[a];

                var n = normals[a];

                var t = tangents[a];
                var b = bitangents[a];

                // Calculate tangent vector
                var tangent = t - (n * Vector3.Dot(n, t));
                tangent = Vector3.Normalize(tangent);

                // Compute binormal
                var binormal = Vector3.Cross(n, tangent);
                binormal.Normalize();

                // Compute handedness
                int handedness = Vector3.Dot(Vector3.Cross(t, b), n) > 0 ? 1 : -1;

                // Apply handedness
                binormal *= handedness;

                outTangents[a] = tangent;
                outBitangents[a] = binormal;
                outHandedness[a] = handedness;
            }

        }

        /// <summary>
        /// Gets the MDL path
        /// </summary>
        /// <param name="itemModel">The item model</param>
        /// <param name="xivRace">The selected race for the given item</param>
        /// <param name="itemType">The items type</param>
        /// <param name="secondaryModel">The secondary model if any</param>
        /// <returns>A Tuple containing the Folder and File string paths</returns>
        public (string Folder, string File) GetMdlPath(IItemModel itemModel, XivRace xivRace, XivItemType itemType, XivModelInfo secondaryModel, string mdlStringPath, string ringSide)
        {
            if (mdlStringPath != null)
            {
                var folder = Path.GetDirectoryName(mdlStringPath).Replace("\\", "/");
                var file = Path.GetFileName(mdlStringPath);

                return (folder, file);
            }

            string mdlFolder = "", mdlFile = "";

            var mdlInfo = secondaryModel ?? itemModel.ModelInfo;
            var id = mdlInfo.PrimaryID.ToString().PadLeft(4, '0');
            var bodyVer = mdlInfo.SecondaryID.ToString().PadLeft(4, '0');
            var itemCategory = itemModel.SecondaryCategory;

            if (secondaryModel != null)
            {
                // Secondary model is gear if between 8800 and 8900 instead of weapon
                if (secondaryModel.PrimaryID > 8800 && secondaryModel.PrimaryID < 8900)
                {
                    itemType = XivItemType.equipment;
                    xivRace = XivRace.Hyur_Midlander_Male;
                    itemCategory = XivStrings.Hands;
                }
            }
          
            var race = xivRace.GetRaceCode();

            switch (itemType)
            {
                case XivItemType.equipment:
                    mdlFolder = $"chara/{itemType}/e{id}/model";
                    mdlFile   = $"c{race}e{id}_{itemModel.GetItemSlotAbbreviation()}{MdlExtension}";
                    break;
                case XivItemType.accessory:
                    mdlFolder = $"chara/{itemType}/a{id}/model";
                    if (ringSide != null)
                    {
                        if (ringSide.Equals("Right"))
                        {
                            mdlFile = $"c{race}a{id}_rir{MdlExtension}";
                        }
                        else
                        {
                            mdlFile = $"c{race}a{id}_ril{MdlExtension}";
                        }
                    }
                    else
                    {
                        mdlFile = $"c{race}a{id}_{itemModel.GetItemSlotAbbreviation()}{MdlExtension}";
                    }
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
                    mdlFile   = $"d{id}e{bodyVer}_{SlotAbbreviationDictionary[itemModel.TertiaryCategory]}{MdlExtension}";
                    break;
                case XivItemType.human:
                    if (itemCategory.Equals(XivStrings.Body))
                    {
                        mdlFolder = $"chara/{itemType}/c{race}/obj/body/b{bodyVer}/model";
                        mdlFile   = $"c{race}b{bodyVer}_{SlotAbbreviationDictionary[itemModel.TertiaryCategory]}{MdlExtension}";
                    }
                    else if (itemCategory.Equals(XivStrings.Hair))
                    {
                        mdlFolder = $"chara/{itemType}/c{race}/obj/hair/h{bodyVer}/model";
                        mdlFile   = $"c{race}h{bodyVer}_{SlotAbbreviationDictionary[itemCategory]}{MdlExtension}";
                    }
                    else if (itemCategory.Equals(XivStrings.Face))
                    {
                        mdlFolder = $"chara/{itemType}/c{race}/obj/face/f{bodyVer}/model";
                        mdlFile   = $"c{race}f{bodyVer}_{SlotAbbreviationDictionary[itemCategory]}{MdlExtension}";
                    }
                    else if (itemCategory.Equals(XivStrings.Tail))
                    {
                        mdlFolder = $"chara/{itemType}/c{race}/obj/tail/t{bodyVer}/model";
                        mdlFile   = $"c{race}t{bodyVer}_{SlotAbbreviationDictionary[itemCategory]}{MdlExtension}";
                    }
                    else if (itemCategory.Equals(XivStrings.Ears))
                    {
                        mdlFolder = $"chara/{itemType}/c{race}/obj/zear/z{bodyVer}/model";
                        mdlFile = $"c{race}z{bodyVer}_zer{MdlExtension}";
                    }
                    break;
                case XivItemType.furniture:
                    var part = "";
                    if (itemModel.TertiaryCategory != "base")
                    {
                        part = itemModel.TertiaryCategory;
                    }

                    if (itemCategory.Equals(XivStrings.Furniture_Indoor))
                    {
                        mdlFolder = $"bgcommon/hou/indoor/general/{id}/bgparts";
                        mdlFile = $"fun_b0_m{id}{part}{MdlExtension}";
                    }
                    else if (itemCategory.Equals(XivStrings.Furniture_Outdoor))
                    {
                        mdlFolder = $"bgcommon/hou/outdoor/general/{id}/bgparts";
                        mdlFile = $"gar_b0_m{id}{part}{MdlExtension}";
                    }

                    break;
                default:
                    mdlFolder = "";
                    mdlFile = "";
                    break;
            }

            return (mdlFolder, mdlFile);
        }

        public static readonly Dictionary<string, string> SlotAbbreviationDictionary = new Dictionary<string, string>
        {
            {XivStrings.Head, "met"},
            {XivStrings.Hands, "glv"},
            {XivStrings.Legs, "dwn"},
            {XivStrings.Feet, "sho"},
            {XivStrings.Body, "top"},
            {XivStrings.Ears, "ear"},
            {XivStrings.Neck, "nek"},
            {XivStrings.Rings, "rir"},
            {XivStrings.LeftRing, "ril"},
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
            {XivStrings.Hair, "hir"},
            {XivStrings.Tail, "til"}

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

        private class ColladaMeshData
        {
            public MeshGeometry3D MeshGeometry { get; set; }

            public List<byte[]> BoneIndices { get; set; }

            public List<byte[]> BoneWeights { get; set; }

            public List<int> Handedness { get; set; } = new List<int>();

            public Vector2Collection TextureCoordintes1 { get; set; }

            public Vector3Collection VertexColors { get; set; } = new Vector3Collection();

            public List<float> VertexAlphas { get; set; } = new List<float>();

            public Dictionary<int, int> PartsDictionary { get; set; }

            public Dictionary<int, List<string>> PartBoneDictionary { get; set; } = new Dictionary<int, List<string>>();

            public Dictionary<string, int> BoneNumDictionary;
        }

        /// <summary>
        /// This class holds the imported data after its been converted to bytes
        /// </summary>
        private class VertexByteData
        {
            public List<byte> VertexData0 { get; set; } = new List<byte>();

            public List<byte> VertexData1 { get; set; } = new List<byte>();

            public List<byte> IndexData { get; set; } = new List<byte>();

            public int VertexCount { get; set; }

            public int IndexCount { get; set; }
        }

        private class VertexDataSection
        {
            public int CompressedVertexDataBlockSize { get; set; }

            public int CompressedIndexDataBlockSize { get; set; }

            public int VertexDataBlockPartCount { get; set; }

            public int IndexDataBlockPartCount { get; set; }

            public List<byte> VertexDataBlock = new List<byte>();

            public List<byte> IndexDataBlock = new List<byte>();
        }
    }
}