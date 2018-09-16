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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SharpDX;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Materials.DataContainers;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.Enums;
using xivModdingFramework.Textures.FileTypes;
using xivModdingFramework.Variants.FileTypes;

namespace xivModdingFramework.Materials.FileTypes
{
    /// <summary>
    /// This class contains the methods that deal with the .mtrl file type 
    /// </summary>
    public class Mtrl
    {
        private const string MtrlExtension = ".mtrl";
        private readonly DirectoryInfo _gameDirectory;
        private readonly XivDataFile _dataFile;

        public Mtrl(DirectoryInfo gameDirectory, XivDataFile dataFile)
        {
            _gameDirectory = gameDirectory;
            _dataFile = dataFile;
        }


        /// <summary>
        /// Gets the MTRL data for the given item 
        /// </summary>
        /// <remarks>
        /// It requires a race (The default is usually <see cref="XivRace.Hyur_Midlander_Male"/>)
        /// It also requires an mtrl part <see cref="GearInfo.GetPartList(IItemModel, XivRace)"/> (default is 'a')
        /// </remarks>
        /// <param name="itemModel">Item that contains model data</param>
        /// <param name="race">The race for the requested data</param>
        /// <param name="part">The Mtrl part </param>
        /// <returns>XivMtrl containing all the mtrl data/returns>
        public XivMtrl GetMtrlData(IItemModel itemModel, XivRace race, char part)
        {
            var index = new Index(_gameDirectory);
            var itemType = ItemType.GetItemType(itemModel);

            // Get mtrl path
            var mtrlPath = GetMtrlPath(itemModel, race, part, itemType);
            var mtrlStringPath = $"{mtrlPath.Folder}/{mtrlPath.File}";

            // Get mtrl offset
            var mtrlOffset = index.GetDataOffset(HashGenerator.GetHash(mtrlPath.Folder), HashGenerator.GetHash(mtrlPath.File),
                _dataFile);

            if (mtrlOffset == 0)
            {
                throw new Exception($"Could not find offest for {mtrlStringPath}");
            }

            var mtrlData = GetMtrlData(mtrlOffset, mtrlStringPath);

            if (mtrlPath.HasVfx)
            {
                var atex = new ATex(_gameDirectory, _dataFile);
                mtrlData.TextureTypePathList.AddRange(atex.GetAtexPaths(itemModel));
            }

            return mtrlData;
        }


        /// <summary>
        /// Gets the MTRL data for the given offset and path
        /// </summary>
        /// <param name="mtrlOffset">The offset to the mtrl in the dat file</param>
        /// <param name="mtrlPath">The full internal game path for the mtrl</param>
        /// <returns>XivMtrl containing all the mtrl data</returns>
        public XivMtrl GetMtrlData(int mtrlOffset, string mtrlPath)
        {
            var dat = new Dat(_gameDirectory);

            // Get uncompressed mtrl data
            var mtrlData = dat.GetType2Data(mtrlOffset, _dataFile);

            XivMtrl xivMtrl;

            using (var br = new BinaryReader(new MemoryStream(mtrlData)))
            {
                xivMtrl = new XivMtrl
                {
                    Signature = br.ReadInt32(),
                    FileSize = br.ReadInt16(),
                    ColorSetDataSize = br.ReadInt16(),
                    MaterialDataSize = br.ReadInt16(),
                    TexturePathsDataSize = br.ReadByte(),
                    Unknown = br.ReadByte(),
                    TextureCount = br.ReadByte(),
                    MapCount = br.ReadByte(),
                    ColorSetCount = br.ReadByte(),
                    Unknown1 = br.ReadByte(),
                    TextureTypePathList = new List<TexTypePath>(),
                    MTRLPath = mtrlPath
                };

                var pathSizeList = new List<int>();

                // get the texture path offsets
                xivMtrl.TexturePathOffsetList = new List<int>(xivMtrl.TextureCount);
                for (var i = 0; i < xivMtrl.TextureCount; i++)
                {
                    xivMtrl.TexturePathOffsetList.Add(br.ReadInt16());
                    br.ReadBytes(2);

                    // add the size of the paths
                    if (i > 0)
                    {
                        pathSizeList.Add(xivMtrl.TexturePathOffsetList[i] - xivMtrl.TexturePathOffsetList[i - 1]);
                    }
                }

                // get the map path offsets
                xivMtrl.MapPathOffsetList = new List<int>(xivMtrl.MapCount);
                for (var i = 0; i < xivMtrl.MapCount; i++)
                {
                    xivMtrl.MapPathOffsetList.Add(br.ReadInt16());
                    br.ReadBytes(2);

                    // add the size of the paths
                    if (i > 0)
                    {
                        pathSizeList.Add(xivMtrl.MapPathOffsetList[i] - xivMtrl.MapPathOffsetList[i-1]);
                    }
                    else
                    {
                        pathSizeList.Add(xivMtrl.MapPathOffsetList[i] - xivMtrl.TexturePathOffsetList[xivMtrl.TextureCount - 1]);
                    }
                }

                // get the color set offsets
                xivMtrl.ColorSetPathOffsetList = new List<int>(xivMtrl.ColorSetCount);
                for (var i = 0; i < xivMtrl.ColorSetCount; i++)
                {
                    xivMtrl.ColorSetPathOffsetList.Add(br.ReadInt16());
                    br.ReadBytes(2);

                    // add the size of the paths
                    if (i > 0)
                    {
                        pathSizeList.Add(xivMtrl.ColorSetPathOffsetList[i] - xivMtrl.ColorSetPathOffsetList[i - 1]);
                    }
                    else
                    {
                        pathSizeList.Add(xivMtrl.ColorSetPathOffsetList[i] - xivMtrl.MapPathOffsetList[xivMtrl.MapCount - 1]);
                    }
                }

                pathSizeList.Add(xivMtrl.TexturePathsDataSize - xivMtrl.ColorSetPathOffsetList[xivMtrl.ColorSetCount- 1]);

                var count = 0;

                // get the texture path strings
                xivMtrl.TexturePathList = new List<string>(xivMtrl.TextureCount);
                for (var i = 0; i < xivMtrl.TextureCount; i++)
                {
                    xivMtrl.TexturePathList.Add(Encoding.UTF8.GetString(br.ReadBytes(pathSizeList[count])).Replace("\0", ""));
                    count++;
                }

                // add the textures to the TextureTypePathList
                xivMtrl.TextureTypePathList.AddRange(GetTexNames(xivMtrl.TexturePathList, _dataFile));

                // get the map path strings
                xivMtrl.MapPathList = new List<string>(xivMtrl.MapCount);
                for (var i = 0; i < xivMtrl.MapCount; i++)
                {
                    xivMtrl.MapPathList.Add(Encoding.UTF8.GetString(br.ReadBytes(pathSizeList[count])).Replace("\0", ""));
                    count++;
                }

                // get the color set path strings
                xivMtrl.ColorSetPathList = new List<string>(xivMtrl.ColorSetCount);
                for (var i = 0; i < xivMtrl.ColorSetCount; i++)
                {
                    xivMtrl.ColorSetPathList.Add(Encoding.UTF8.GetString(br.ReadBytes(pathSizeList[count])).Replace("\0", ""));
                    count++;
                }

                // If the mtrl file contains a color set, add it to the TextureTypePathList
                if (xivMtrl.ColorSetDataSize > 0)
                {
                    var ttp = new TexTypePath
                    {
                        Path = mtrlPath,
                        Type = XivTexType.ColorSet,
                        DataFile = _dataFile
                    };
                    xivMtrl.TextureTypePathList.Add(ttp);
                }

                var shaderPathSize = xivMtrl.MaterialDataSize - xivMtrl.TexturePathsDataSize;

                xivMtrl.Shader = Encoding.UTF8.GetString(br.ReadBytes(shaderPathSize)).Replace("\0", "");

                xivMtrl.Unknown2 = br.ReadInt32();

                xivMtrl.ColorSetData = new List<Half>();
                for (var i = 0; i < xivMtrl.ColorSetDataSize / 2; i++)
                {
                    xivMtrl.ColorSetData.Add(new Half(br.ReadUInt16()));
                }

                xivMtrl.AdditionalDataSize = br.ReadInt16();

                xivMtrl.DataStruct1Count = br.ReadInt16();

                xivMtrl.DataStruct2Count = br.ReadInt16();

                xivMtrl.ParameterStructCount = br.ReadInt16();

                xivMtrl.ShaderNumber = br.ReadInt16();

                xivMtrl.Unknown3 = br.ReadInt16();

                xivMtrl.DataStruct1List = new List<DataStruct1>(xivMtrl.DataStruct1Count);
                for (var i = 0; i < xivMtrl.DataStruct1Count; i++)
                {
                    xivMtrl.DataStruct1List.Add(new DataStruct1{ ID = br.ReadUInt32(), Unknown1 = br.ReadUInt32()});
                }

                xivMtrl.DataStruct2List = new List<DataStruct2>(xivMtrl.DataStruct2Count);
                for (var i = 0; i < xivMtrl.DataStruct2Count; i++)
                {
                    xivMtrl.DataStruct2List.Add(new DataStruct2{ID = br.ReadUInt32(), Offset = br.ReadInt16(), Size = br.ReadInt16()});
                }

                xivMtrl.ParameterStructList = new List<ParameterStruct>(xivMtrl.ParameterStructCount);
                for (var i = 0; i < xivMtrl.ParameterStructCount; i++)
                {
                    xivMtrl.ParameterStructList.Add(new ParameterStruct{ID = br.ReadUInt32(), Unknown1 = br.ReadInt16(), Unknown2 = br.ReadInt16(), TextureIndex = br.ReadUInt32()});
                }

                xivMtrl.AdditionalData = br.ReadBytes(xivMtrl.AdditionalDataSize);
            }

            return xivMtrl;
        }

        /// <summary>
        /// Toggles Translucency for an item on or off
        /// </summary>
        /// <param name="xivMtrl">The XivMtrl containing the mtrl data</param>
        /// <param name="item">The item to toggle translucency for</param>
        /// <param name="translucencyEnabled">Flag determining if translucency is to be enabled or disabled</param>
        public void ToggleTranslucency(XivMtrl xivMtrl, IItem item, bool translucencyEnabled)
        {
            xivMtrl.ShaderNumber = !translucencyEnabled ? (short) 0x0D : (short) 0x1D;

            ImportMtrl(xivMtrl, item);
        }

        /// <summary>
        /// Imports an MTRL file
        /// </summary>
        /// <param name="xivMtrl">The XivMtrl containing the mtrl data</param>
        /// <param name="item">The item whos mtrl is being imported</param>
        /// <returns>The new offset</returns>
        public int ImportMtrl(XivMtrl xivMtrl, IItem item)
        {
            var mtrlBytes = new List<byte>();

            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.Signature));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.FileSize));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ColorSetDataSize));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.MaterialDataSize));
            mtrlBytes.Add(xivMtrl.TexturePathsDataSize);
            mtrlBytes.Add(xivMtrl.Unknown);
            mtrlBytes.Add(xivMtrl.TextureCount);
            mtrlBytes.Add(xivMtrl.MapCount);
            mtrlBytes.Add(xivMtrl.ColorSetCount);
            mtrlBytes.Add(xivMtrl.Unknown1);

            foreach (var texPath in xivMtrl.TexturePathOffsetList)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes(texPath));
            }

            foreach (var mapPath in xivMtrl.MapPathOffsetList)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes(mapPath));
            }

            foreach (var colorPath in xivMtrl.ColorSetPathOffsetList)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes(colorPath));
            }

            foreach (var texPathString in xivMtrl.TexturePathList)
            {
                mtrlBytes.AddRange(Encoding.UTF8.GetBytes(texPathString));
                mtrlBytes.Add(0);
            }

            foreach (var mapPathString in xivMtrl.MapPathList)
            {
                mtrlBytes.AddRange(Encoding.UTF8.GetBytes(mapPathString));
                mtrlBytes.Add(0);
            }

            foreach (var colorSetPathString in xivMtrl.ColorSetPathList)
            {
                mtrlBytes.AddRange(Encoding.UTF8.GetBytes(colorSetPathString));
                mtrlBytes.Add(0);
            }

            mtrlBytes.AddRange(Encoding.UTF8.GetBytes(xivMtrl.Shader));
            mtrlBytes.Add(0);
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.Unknown2));

            foreach (var colorSetHalf in xivMtrl.ColorSetData)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes(colorSetHalf.RawValue));
            }

            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.AdditionalDataSize));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.DataStruct1Count));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.DataStruct2Count));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ParameterStructCount));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ShaderNumber));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.Unknown3));

            foreach (var dataStruct1 in xivMtrl.DataStruct1List)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes(dataStruct1.ID));
                mtrlBytes.AddRange(BitConverter.GetBytes(dataStruct1.Unknown1));
            }

            foreach (var dataStruct2 in xivMtrl.DataStruct2List)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes(dataStruct2.ID));
                mtrlBytes.AddRange(BitConverter.GetBytes(dataStruct2.Offset));
                mtrlBytes.AddRange(BitConverter.GetBytes(dataStruct2.Size));
            }

            foreach (var parameterStruct in xivMtrl.ParameterStructList)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes(parameterStruct.ID));
                mtrlBytes.AddRange(BitConverter.GetBytes(parameterStruct.Unknown1));
                mtrlBytes.AddRange(BitConverter.GetBytes(parameterStruct.Unknown2));
                mtrlBytes.AddRange(BitConverter.GetBytes(parameterStruct.TextureIndex));
            }

            mtrlBytes.AddRange(xivMtrl.AdditionalData);

            var dat = new Dat(_gameDirectory);
            return dat.ImportType2Data(mtrlBytes.ToArray(), item.Name, xivMtrl.MTRLPath, item.Category);
        }

        /// <summary>
        /// Gets the names of the textures based on file name
        /// </summary>
        /// <remarks>
        /// The name of the texture is obtained from the file name ending
        /// </remarks>
        /// <param name="texPathList">The list of texture paths</param>
        /// <returns>A list of TexTypePath</returns>
        private static List<TexTypePath> GetTexNames(IEnumerable<string> texPathList, XivDataFile dataFile)
        {
            var texTypePathList = new List<TexTypePath>();

            foreach (var path in texPathList)
            {
                var ttp = new TexTypePath {Path = path, DataFile = dataFile};

                if(path.Contains("dummy") || path.Equals(string.Empty)) continue;

                if (path.Contains("_s.tex"))
                {
                    ttp.Type = XivTexType.Specular;
                }
                else if (path.Contains("_d.tex"))
                {
                    ttp.Type = XivTexType.Diffuse;

                }
                else if (path.Contains("_n.tex"))
                {
                    ttp.Type = XivTexType.Normal;

                }
                else if (path.Contains("_m.tex"))
                {
                    ttp.Type = path.Contains("skin") ? XivTexType.Skin : XivTexType.Multi;
                }

                texTypePathList.Add(ttp);
            }

            return texTypePathList;
        }

        /// <summary>
        /// Gets the mtrl path for a given item
        /// </summary>
        /// <param name="itemModel">Item that contains model data</param>
        /// <param name="xivRace">The race for the requested data</param>
        /// <param name="part">The mtrl part <see cref="GearInfo.GetPartList(IItemModel, XivRace)"/></param>
        /// <param name="itemType">The type of the item</param>
        /// <returns>A tuple containing the mtrl folder and file, and whether it has a vfx</returns>
        private (string Folder, string File, bool HasVfx) GetMtrlPath(IItemModel itemModel, XivRace xivRace, char part, XivItemType itemType)
        {
            // The default version number
            var version = "0001";

            var hasVfx = false;

            if (itemType != XivItemType.human)
            {
                // get the items version from the imc file
                var imc = new Imc(_gameDirectory, _dataFile);
                var imcInfo = imc.GetImcInfo(itemModel, itemModel.PrimaryModelInfo);
                version = imcInfo.Version.ToString().PadLeft(4, '0');

                if (imcInfo.Vfx > 0)
                {
                    hasVfx = true;
                }
            }

            var id = itemModel.PrimaryModelInfo.ModelID.ToString().PadLeft(4, '0');
            var bodyVer = itemModel.PrimaryModelInfo.Body.ToString().PadLeft(4, '0');
            var race = xivRace.GetRaceCode();

            string mtrlFolder = "", mtrlFile = "";

            switch (itemType)
            {
                case XivItemType.equipment:
                    mtrlFolder = $"chara/{itemType}/e{id}/material/v{version}";
                    mtrlFile = $"mt_c{race}e{id}_{SlotAbbreviationDictionary[itemModel.ItemCategory]}_{part}{MtrlExtension}";
                    break;
                case XivItemType.accessory:
                    mtrlFolder = $"chara/{itemType}/a{id}/material/v{version}";
                    mtrlFile = $"mt_c{race}a{id}_{SlotAbbreviationDictionary[itemModel.ItemCategory]}_{part}{MtrlExtension}";
                    break;
                case XivItemType.weapon:
                    mtrlFolder = $"chara/{itemType}/w{id}/obj/body/b{bodyVer}/material/v{version}";
                    mtrlFile = $"mt_w{id}b{bodyVer}_{part}{MtrlExtension}";
                    break;

                case XivItemType.monster:
                    mtrlFolder = $"chara/{itemType}/m{id}/obj/body/b{bodyVer}/material/v{version}";
                    mtrlFile = $"mt_m{id}b{bodyVer}_{part}{MtrlExtension}";
                    break;
                case XivItemType.demihuman:
                    mtrlFolder = $"chara/{itemType}/d{id}/obj/equipment/e{bodyVer}/material/v{version}";
                    mtrlFile = $"mt_d{id}e{bodyVer}_{SlotAbbreviationDictionary[itemModel.ItemSubCategory]}_{part}{MtrlExtension}";
                    break;
                case XivItemType.human:
                    if (itemModel.ItemCategory.Equals(XivStrings.Body))
                    {
                        mtrlFolder = $"chara/{itemType}/c{race}/obj/body/b{bodyVer}/material";
                        mtrlFile = $"mt_c{race}b{bodyVer}_{part}{MtrlExtension}";
                    }
                    else if (itemModel.ItemCategory.Equals(XivStrings.Hair))
                    {
                        // Hair has a version number, but no IMC, so we leave it at the default 0001
                        mtrlFolder = $"chara/{itemType}/c{race}/obj/hair/h{bodyVer}/material/v{version}";
                        mtrlFile = $"mt_c{race}h{bodyVer}_{SlotAbbreviationDictionary[itemModel.ItemSubCategory]}_{part}{MtrlExtension}";
                    }
                    else if (itemModel.ItemCategory.Equals(XivStrings.Face))
                    {
                        mtrlFolder = $"chara/{itemType}/c{race}/obj/face/f{bodyVer}/material";
                        mtrlFile = $"mt_c{race}f{bodyVer}_{SlotAbbreviationDictionary[itemModel.ItemSubCategory]}_{part}{MtrlExtension}";
                    }
                    else if (itemModel.ItemCategory.Equals(XivStrings.Tail))
                    {
                        mtrlFolder = $"chara/{itemType}/c{race}/obj/tail/t{bodyVer}/material";
                        mtrlFile = $"mt_c{race}t{bodyVer}_{part}{MtrlExtension}";
                    }

                    break;
                default:
                    mtrlFolder = "";
                    mtrlFile = "";
                    break;
            }

            return (mtrlFolder, mtrlFile, hasVfx);
        }

        /// <summary>
        /// A dictionary containing the slot abbreviations in the format [equipment slot, slot abbreviation]
        /// </summary>
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
    }
}