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
        /// <returns>XivMtrl containing all the mtrl data</returns>
        public XivMtrl GetMtrlData(IItemModel itemModel, XivRace race, char part, int dxVersion)
        {
            var index = new Index(_gameDirectory);
            var itemType = ItemType.GetItemType(itemModel);

            // Get mtrl path
            var mtrlPath = GetMtrlPath(itemModel, race, part, itemType);
            var mtrlStringPath = $"{mtrlPath.Folder}/{mtrlPath.File}";

            // Get mtrl offset
            var mtrlOffset = index.GetDataOffset(HashGenerator.GetHash(mtrlPath.Folder), HashGenerator.GetHash(mtrlPath.File),
                _dataFile);

            if (mtrlOffset == 0 && itemType == XivItemType.furniture)
            {
                mtrlPath.File = mtrlPath.File.Replace("_0", "_1");
                mtrlStringPath = $"{mtrlPath.Folder}/{mtrlPath.File}";

                // Get mtrl offset
                mtrlOffset = index.GetDataOffset(HashGenerator.GetHash(mtrlPath.Folder), HashGenerator.GetHash(mtrlPath.File),
                    _dataFile);
            }

            if (mtrlOffset == 0)
            {
                throw new Exception($"Could not find offest for {mtrlStringPath}");
            }

            var mtrlData = GetMtrlData(mtrlOffset, mtrlStringPath, dxVersion);

            if (mtrlPath.HasVfx)
            {
                var atex = new ATex(_gameDirectory, _dataFile);
                mtrlData.TextureTypePathList.AddRange(atex.GetAtexPaths(itemModel));
            }

            return mtrlData;
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
        /// <param name="mtrlFile">The Mtrl file</param>
        /// <returns>XivMtrl containing all the mtrl data</returns>
        public XivMtrl GetMtrlData(IItemModel itemModel, XivRace race, string mtrlFile, int dxVersion)
        {
            var index = new Index(_gameDirectory);
            var itemType = ItemType.GetItemType(itemModel);

            // Get mtrl path
            var mtrlFolder = GetMtrlFolder(itemModel, race, itemType);
            var mtrlStringPath = $"{mtrlFolder}/{mtrlFile}";

            if (itemType == XivItemType.furniture)
            {
                mtrlStringPath = $"b{mtrlFile}";
                mtrlFolder = Path.GetDirectoryName(mtrlStringPath).Replace("\\", "/");
                mtrlFile = Path.GetFileName(mtrlStringPath);
            }

            // Get mtrl offset
            var mtrlOffset = index.GetDataOffset(HashGenerator.GetHash(mtrlFolder), HashGenerator.GetHash(mtrlFile),
                _dataFile);

            if (mtrlOffset == 0)
            {
                throw new Exception($"Could not find offest for {mtrlStringPath}");
            }

            var mtrlData = GetMtrlData(mtrlOffset, mtrlStringPath, dxVersion);

            return mtrlData;
        }

        /// <summary>
        /// Gets the MTRL data for the given offset and path
        /// </summary>
        /// <param name="mtrlOffset">The offset to the mtrl in the dat file</param>
        /// <param name="mtrlPath">The full internal game path for the mtrl</param>
        /// <returns>XivMtrl containing all the mtrl data</returns>
        public XivMtrl GetMtrlData(int mtrlOffset, string mtrlPath, int dxVersion)
        {
            var dat = new Dat(_gameDirectory);
            var index = new Index(_gameDirectory);

            // Get uncompressed mtrl data
            var mtrlData = dat.GetType2Data(mtrlOffset, _dataFile);

            XivMtrl xivMtrl;

            using (var br = new BinaryReader(new MemoryStream(mtrlData)))
            {
                xivMtrl = new XivMtrl
                {
                    Signature = br.ReadInt32(),
                    FileSize = br.ReadInt16(),
                    ColorSetDataSize = br.ReadUInt16(),
                    MaterialDataSize = br.ReadUInt16(),
                    TexturePathsDataSize = br.ReadUInt16(),
                    TextureCount = br.ReadByte(),
                    MapCount = br.ReadByte(),
                    ColorSetCount = br.ReadByte(),
                    UnknownDataSize = br.ReadByte(),
                    TextureTypePathList = new List<TexTypePath>(),
                    MTRLPath = mtrlPath
                };

                var pathSizeList = new List<int>();

                // get the texture path offsets
                xivMtrl.TexturePathOffsetList = new List<int>(xivMtrl.TextureCount);
                xivMtrl.TexturePathUnknownList = new List<short>(xivMtrl.TextureCount);
                for (var i = 0; i < xivMtrl.TextureCount; i++)
                {
                    xivMtrl.TexturePathOffsetList.Add(br.ReadInt16());
                    xivMtrl.TexturePathUnknownList.Add(br.ReadInt16());

                    // add the size of the paths
                    if (i > 0)
                    {
                        pathSizeList.Add(xivMtrl.TexturePathOffsetList[i] - xivMtrl.TexturePathOffsetList[i - 1]);
                    }
                }

                // get the map path offsets
                xivMtrl.MapPathOffsetList = new List<int>(xivMtrl.MapCount);
                xivMtrl.MapPathUnknownList = new List<short>(xivMtrl.MapCount);
                for (var i = 0; i < xivMtrl.MapCount; i++)
                {
                    xivMtrl.MapPathOffsetList.Add(br.ReadInt16());
                    xivMtrl.MapPathUnknownList.Add(br.ReadInt16());

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
                xivMtrl.ColorSetPathUnknownList = new List<short>(xivMtrl.ColorSetCount);
                for (var i = 0; i < xivMtrl.ColorSetCount; i++)
                {
                    xivMtrl.ColorSetPathOffsetList.Add(br.ReadInt16());
                    xivMtrl.ColorSetPathUnknownList.Add(br.ReadInt16());

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
                    var texturePath = Encoding.UTF8.GetString(br.ReadBytes(pathSizeList[count])).Replace("\0", "");
                    var dx11FileName = Path.GetFileName(texturePath).Insert(0, "--");

                    if (index.FileExists(HashGenerator.GetHash(dx11FileName),
                        HashGenerator.GetHash(Path.GetDirectoryName(texturePath).Replace("\\", "/")), _dataFile))
                    {
                        texturePath = texturePath.Insert(texturePath.LastIndexOf("/") + 1, "--");
                    }

                    xivMtrl.TexturePathList.Add(texturePath);
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

                xivMtrl.Unknown2 = br.ReadBytes(xivMtrl.UnknownDataSize);

                if (xivMtrl.ColorSetDataSize > 0)
                {
                    // Color Data is always 512 (6 x 14 = 64 x 8bpp = 512)
                    var colorDataSize = 512;

                    xivMtrl.ColorSetData = new List<Half>();
                    for (var i = 0; i < colorDataSize / 2; i++)
                    {
                        xivMtrl.ColorSetData.Add(new Half(br.ReadUInt16()));
                    }

                    // If the color set is 544 in length, it has an extra 32 bytes at the end
                    if (xivMtrl.ColorSetDataSize == 544)
                    {
                        xivMtrl.ColorSetExtraData = br.ReadBytes(32);
                    }
                }

                xivMtrl.AdditionalDataSize = br.ReadUInt16();

                xivMtrl.DataStruct1Count = br.ReadUInt16();

                xivMtrl.DataStruct2Count = br.ReadUInt16();

                xivMtrl.ParameterStructCount = br.ReadUInt16();

                xivMtrl.ShaderNumber = br.ReadUInt16();

                xivMtrl.Unknown3 = br.ReadUInt16();

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
        /// Converts an xivMtrl to a XivTex for ColorSet exporting
        /// </summary>
        /// <param name="xivMtrl">The XivMtrl with the ColorSet data</param>
        /// <returns>The XivTex of the ColorSet</returns>
        public XivTex MtrlToXivTex(XivMtrl xivMtrl, TexTypePath ttp)
        {
            var colorSetData = new List<byte>();

            foreach (var colorSetHalf in xivMtrl.ColorSetData)
            {
                colorSetData.AddRange(BitConverter.GetBytes(colorSetHalf.RawValue));
            }

            var xivTex = new XivTex
            {
                Width = 4,
                Height = 16,
                MipMapCount = 0,
                TexData = colorSetData.ToArray(),
                TextureFormat = XivTexFormat.A16B16G16R16F,
                TextureTypeAndPath = ttp
            };

            return xivTex;
        }

        /// <summary>
        /// Saves the Extra data from the ColorSet
        /// </summary>
        /// <param name="item">The item containing the ColorSet</param>
        /// <param name="xivMtrl">The XivMtrl for the ColorSet</param>
        /// <param name="saveDirectory">The save directory</param>
        /// <param name="race">The selected race for the item</param>
        public void SaveColorSetExtraData(IItem item, XivMtrl xivMtrl, DirectoryInfo saveDirectory, XivRace race)
        {
            if (xivMtrl.ColorSetExtraData != null)
            {
                var path = IOUtil.MakeItemSavePath(item, saveDirectory, race);

                Directory.CreateDirectory(path);

                var savePath = Path.Combine(path, Path.GetFileNameWithoutExtension(xivMtrl.MTRLPath) + ".dat");

                File.WriteAllBytes(savePath, xivMtrl.ColorSetExtraData);
            }
        }

        /// <summary>
        /// Toggles Translucency for an item on or off
        /// </summary>
        /// <param name="xivMtrl">The XivMtrl containing the mtrl data</param>
        /// <param name="item">The item to toggle translucency for</param>
        /// <param name="translucencyEnabled">Flag determining if translucency is to be enabled or disabled</param>
        /// <param name="source">The source/application that is writing to the dat.</param>
        public void ToggleTranslucency(XivMtrl xivMtrl, IItem item, bool translucencyEnabled, string source)
        {
            xivMtrl.ShaderNumber = !translucencyEnabled ? (ushort) 0x0D : (ushort) 0x1D;

            ImportMtrl(xivMtrl, item, source);
        }

        /// <summary>
        /// Imports an MTRL file
        /// </summary>
        /// <param name="xivMtrl">The XivMtrl containing the mtrl data</param>
        /// <param name="item">The item whos mtrl is being imported</param>
        /// <param name="source">The source/application that is writing to the dat.</param>
        /// <returns>The new offset</returns>
        public int ImportMtrl(XivMtrl xivMtrl, IItem item, string source)
        {
            var mtrlBytes = new List<byte>();

            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.Signature));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.FileSize));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ColorSetDataSize));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.MaterialDataSize));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.TexturePathsDataSize));
            mtrlBytes.Add(xivMtrl.TextureCount);
            mtrlBytes.Add(xivMtrl.MapCount);
            mtrlBytes.Add(xivMtrl.ColorSetCount);
            mtrlBytes.Add(xivMtrl.UnknownDataSize);

            for (int i = 0; i < xivMtrl.TexturePathOffsetList.Count; i++)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.TexturePathOffsetList[i]));
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.TexturePathUnknownList[i]));
            }

            for (int i = 0; i < xivMtrl.MapPathOffsetList.Count; i++)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.MapPathOffsetList[i]));
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.MapPathUnknownList[i]));
            }

            for (int i = 0; i < xivMtrl.ColorSetPathOffsetList.Count; i++)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.ColorSetPathOffsetList[i]));
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.ColorSetPathUnknownList[i]));
            }

            var pathStringList = new List<byte>();

            foreach (var texPathString in xivMtrl.TexturePathList)
            {
                pathStringList.AddRange(Encoding.UTF8.GetBytes(texPathString));
                pathStringList.Add(0);
            }

            foreach (var mapPathString in xivMtrl.MapPathList)
            {
                pathStringList.AddRange(Encoding.UTF8.GetBytes(mapPathString));
                pathStringList.Add(0);
            }

            foreach (var colorSetPathString in xivMtrl.ColorSetPathList)
            {
                pathStringList.AddRange(Encoding.UTF8.GetBytes(colorSetPathString));
                pathStringList.Add(0);
            }

            pathStringList.AddRange(Encoding.UTF8.GetBytes(xivMtrl.Shader));
            pathStringList.Add(0);

            var paddingSize = xivMtrl.MaterialDataSize - pathStringList.Count;

            pathStringList.AddRange(new byte[paddingSize]);

            mtrlBytes.AddRange(pathStringList);

            mtrlBytes.AddRange(xivMtrl.Unknown2);

            foreach (var colorSetHalf in xivMtrl.ColorSetData)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes(colorSetHalf.RawValue));
            }

            if (xivMtrl.ColorSetDataSize == 544)
            {
                mtrlBytes.AddRange(xivMtrl.ColorSetExtraData);
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
            return dat.ImportType2Data(mtrlBytes.ToArray(), item.Name, xivMtrl.MTRLPath, item.Category, source);
        }

        /// <summary>
        /// Creates an MTRL file
        /// </summary>
        /// <param name="xivMtrl">The XivMtrl containing the mtrl data</param>
        /// <param name="item">The item</param>
        /// <returns>The new mtrl file byte data</returns>
        public byte[] CreateMtrlFile(XivMtrl xivMtrl, IItem item)
        {
            var mtrlBytes = new List<byte>();

            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.Signature));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.FileSize));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ColorSetDataSize));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.MaterialDataSize));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.TexturePathsDataSize));
            mtrlBytes.Add(xivMtrl.TextureCount);
            mtrlBytes.Add(xivMtrl.MapCount);
            mtrlBytes.Add(xivMtrl.ColorSetCount);
            mtrlBytes.Add(xivMtrl.UnknownDataSize);

            for (int i = 0; i < xivMtrl.TexturePathOffsetList.Count; i++)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.TexturePathOffsetList[i]));
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.TexturePathUnknownList[i]));
            }

            for (int i = 0; i < xivMtrl.MapPathOffsetList.Count; i++)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.MapPathOffsetList[i]));
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.MapPathUnknownList[i]));
            }

            for (int i = 0; i < xivMtrl.ColorSetPathOffsetList.Count; i++)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.ColorSetPathOffsetList[i]));
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.ColorSetPathUnknownList[i]));
            }

            var pathStringList = new List<byte>();

            foreach (var texPathString in xivMtrl.TexturePathList)
            {
                pathStringList.AddRange(Encoding.UTF8.GetBytes(texPathString));
                pathStringList.Add(0);
            }

            foreach (var mapPathString in xivMtrl.MapPathList)
            {
                pathStringList.AddRange(Encoding.UTF8.GetBytes(mapPathString));
                pathStringList.Add(0);
            }

            foreach (var colorSetPathString in xivMtrl.ColorSetPathList)
            {
                pathStringList.AddRange(Encoding.UTF8.GetBytes(colorSetPathString));
                pathStringList.Add(0);
            }

            pathStringList.AddRange(Encoding.UTF8.GetBytes(xivMtrl.Shader));
            pathStringList.Add(0);

            var paddingSize = xivMtrl.MaterialDataSize - pathStringList.Count;

            pathStringList.AddRange(new byte[paddingSize]);

            mtrlBytes.AddRange(pathStringList);

            mtrlBytes.AddRange(xivMtrl.Unknown2);

            foreach (var colorSetHalf in xivMtrl.ColorSetData)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes(colorSetHalf.RawValue));
            }

            if (xivMtrl.ColorSetDataSize == 544)
            {
                mtrlBytes.AddRange(xivMtrl.ColorSetExtraData);
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

            return mtrlBytes.ToArray();
        }

        /// <summary>
        /// Gets the names of the textures based on file name
        /// </summary>
        /// <remarks>
        /// The name of the texture is obtained from the file name ending
        /// </remarks>
        /// <param name="texPathList">The list of texture paths</param>
        /// <returns>A list of TexTypePath</returns>
        private List<TexTypePath> GetTexNames(IEnumerable<string> texPathList, XivDataFile dataFile)
        {
            var index = new Index(_gameDirectory);
            var texTypePathList = new List<TexTypePath>();

            foreach (var path in texPathList)
            {
                if (!index.FileExists(HashGenerator.GetHash(Path.GetFileName(path)),
                    HashGenerator.GetHash(Path.GetDirectoryName(path).Replace("\\", "/")), dataFile))
                {
                    continue;
                }

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

            if (itemType != XivItemType.human && itemType != XivItemType.furniture)
            {
                // get the items version from the imc file
                var imc = new Imc(_gameDirectory, _dataFile);
                var imcInfo = imc.GetImcInfo(itemModel, itemModel.ModelInfo);
                version = imcInfo.Version.ToString().PadLeft(4, '0');

                if (imcInfo.Vfx > 0)
                {
                    hasVfx = true;
                }
            }

            var id = itemModel.ModelInfo.ModelID.ToString().PadLeft(4, '0');
            var bodyVer = itemModel.ModelInfo.Body.ToString().PadLeft(4, '0');
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
                case XivItemType.furniture:
                    if (itemModel.ItemCategory.Equals(XivStrings.Furniture_Indoor))
                    {
                        mtrlFolder = $"bgcommon/hou/indoor/general/{id}/material";
                        mtrlFile = $"fun_b0_m{id}_0{part}{MtrlExtension}";
                    }
                    else if (itemModel.ItemCategory.Equals(XivStrings.Furniture_Outdoor))
                    {
                        mtrlFolder = $"bgcommon/hou/outdoor/general/{id}/material";
                        mtrlFile = $"gar_b0_m{id}_0{part}{MtrlExtension}";
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
        /// Gets the mtrl folder for a given item
        /// </summary>
        /// <param name="itemModel">Item that contains model data</param>
        /// <param name="xivRace">The race for the requested data</param>
        /// <param name="itemType">The type of the item</param>
        /// <returns>The mtrl Folder</returns>
        private string GetMtrlFolder(IItemModel itemModel, XivRace xivRace, XivItemType itemType)
        {
            // The default version number
            var version = "0001";

            if (itemType != XivItemType.human && itemType != XivItemType.furniture)
            {
                // get the items version from the imc file
                var imc = new Imc(_gameDirectory, _dataFile);
                var imcInfo = imc.GetImcInfo(itemModel, itemModel.ModelInfo);
                version = imcInfo.Version.ToString().PadLeft(4, '0');
            }

            if (version.Equals("0000"))
            {
                version = "0001";
            }

            var id = itemModel.ModelInfo.ModelID.ToString().PadLeft(4, '0');
            var bodyVer = itemModel.ModelInfo.Body.ToString().PadLeft(4, '0');
            var race = xivRace.GetRaceCode();

            var mtrlFolder = "";

            switch (itemType)
            {
                case XivItemType.equipment:
                    mtrlFolder = $"chara/{itemType}/e{id}/material/v{version}";
                    break;
                case XivItemType.accessory:
                    mtrlFolder = $"chara/{itemType}/a{id}/material/v{version}";
                    break;
                case XivItemType.weapon:
                    mtrlFolder = $"chara/{itemType}/w{id}/obj/body/b{bodyVer}/material/v{version}";
                    break;
                case XivItemType.monster:
                    mtrlFolder = $"chara/{itemType}/m{id}/obj/body/b{bodyVer}/material/v{version}";
                    break;
                case XivItemType.demihuman:
                    mtrlFolder = $"chara/{itemType}/d{id}/obj/equipment/e{bodyVer}/material/v{version}";
                    break;
                case XivItemType.human:
                    if (itemModel.ItemCategory.Equals(XivStrings.Body))
                    {
                        mtrlFolder = $"chara/{itemType}/c{race}/obj/body/b{bodyVer}/material";
                    }
                    else if (itemModel.ItemCategory.Equals(XivStrings.Hair))
                    {
                        // Hair has a version number, but no IMC, so we leave it at the default 0001
                        mtrlFolder = $"chara/{itemType}/c{race}/obj/hair/h{bodyVer}/material/v{version}";
                    }
                    else if (itemModel.ItemCategory.Equals(XivStrings.Face))
                    {
                        mtrlFolder = $"chara/{itemType}/c{race}/obj/face/f{bodyVer}/material";
                    }
                    else if (itemModel.ItemCategory.Equals(XivStrings.Tail))
                    {
                        mtrlFolder = $"chara/{itemType}/c{race}/obj/tail/t{bodyVer}/material";
                    }
                    break;
                case XivItemType.furniture:
                    if (itemModel.ItemCategory.Equals(XivStrings.Furniture_Indoor))
                    {
                        mtrlFolder = $"bgcommon/hou/indoor/general/{id}/material";
                    }
                    else if (itemModel.ItemCategory.Equals(XivStrings.Furniture_Outdoor))
                    {
                        mtrlFolder = $"bgcommon/hou/outdoor/general/{id}/material";
                    }
                    break;
                default:
                    mtrlFolder = "";
                    break;
            }

            return mtrlFolder;
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

        /// <summary>
        /// A dictionary containing race data in the format [Race ID, XivRace]
        /// </summary>
        private static readonly Dictionary<string, XivRace> IDRaceDictionary = new Dictionary<string, XivRace>
        {
            {"0101", XivRace.Hyur_Midlander_Male},
            {"0104", XivRace.Hyur_Midlander_Male_NPC},
            {"0201", XivRace.Hyur_Midlander_Female},
            {"0204", XivRace.Hyur_Midlander_Female_NPC},
            {"0301", XivRace.Hyur_Highlander_Male},
            {"0304", XivRace.Hyur_Highlander_Male_NPC},
            {"0401", XivRace.Hyur_Highlander_Female},
            {"0404", XivRace.Hyur_Highlander_Female_NPC},
            {"0501", XivRace.Elezen_Male},
            {"0504", XivRace.Elezen_Male_NPC},
            {"0601", XivRace.Elezen_Female},
            {"0604", XivRace.Elezen_Female_NPC},
            {"0701", XivRace.Miqote_Male},
            {"0704", XivRace.Miqote_Male_NPC},
            {"0801", XivRace.Miqote_Female},
            {"0804", XivRace.Miqote_Female_NPC},
            {"0901", XivRace.Roegadyn_Male},
            {"0904", XivRace.Roegadyn_Male_NPC},
            {"1001", XivRace.Roegadyn_Female},
            {"1004", XivRace.Roegadyn_Female_NPC},
            {"1101", XivRace.Lalafell_Male},
            {"1104", XivRace.Lalafell_Male_NPC},
            {"1201", XivRace.Lalafell_Female},
            {"1204", XivRace.Lalafell_Female_NPC},
            {"1301", XivRace.AuRa_Male},
            {"1304", XivRace.AuRa_Male_NPC},
            {"1401", XivRace.AuRa_Female},
            {"1404", XivRace.AuRa_Female_NPC},
            {"9104", XivRace.NPC_Male},
            {"9204", XivRace.NPC_Female}
        };
    }
}