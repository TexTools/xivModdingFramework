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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
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
        private readonly XivLanguage _language;
        private XivDataFile _dataFile;
        private static SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

        public Mtrl(DirectoryInfo gameDirectory, XivDataFile dataFile, XivLanguage lang)
        {
            _gameDirectory = gameDirectory;
            _language = lang;
            DataFile = dataFile;
        }

        public XivDataFile DataFile
        {
            get => _dataFile;
            set => _dataFile = value;
        }


        // MtrlParam constants for texture types.
        public static Dictionary<XivTexType, uint> TextureDescriptorValues = new Dictionary<XivTexType, uint>()
        {
            { XivTexType.Normal, 207536625 },
            { XivTexType.Diffuse, 290653886 },
            { XivTexType.Specular, 731504677 },
            { XivTexType.Multi, 2320401078 },
            { XivTexType.Reflection, 4271961042 }   // Used for the Catchlight texture in Iris Materials.
        };

        public static Dictionary<XivTexType, uint> FurnitureTextureDescriptorValues = new Dictionary<XivTexType, uint>()
        {
            { XivTexType.Normal, 2863978985 },
            { XivTexType.Diffuse, 510652316 },
            { XivTexType.Specular, 465317650 },
            { XivTexType.Multi, 0 },
            { XivTexType.Reflection, 0 }
        };

        // MtrlParam constants for file compressions/formats.
        public static Dictionary<MtrlTextureDescriptorFormat, short> TextureDescriptorFormatValues = new Dictionary<MtrlTextureDescriptorFormat, short>()
        {
            { MtrlTextureDescriptorFormat.UsesColorset, -32768 },   // There is some variation on these values, but it always occures in the last 6 bits, and doesn't seem
            { MtrlTextureDescriptorFormat.NoColorset, -31936 },      // To have an appreciable change (Either [000000] or [010101])
            // Non-normal maps are always [WithoutAlpha].  Normal maps are always [WithAlpha], 
            // with the exception that 8.8.8.8 ARGB normal maps use the WithoutAlpha flag (And do not pull a colorset).

            // In the case of faces, the first bit is also flipped to 0 for the normal maps.  Unknown why.
        };


        // Data for setting up MTRL shaders.  Taken from SE samples.  Seems to be consistent across each type of setup.
        // The exact order of these structs does not seem to be important, only the data in question.
        // It may be viable (though inefficient) to simply include all of them.
        public static Dictionary<XivTexType, TextureUsageStruct> TextureUsageValues = new Dictionary<XivTexType, TextureUsageStruct>()
        {
            { XivTexType.Normal, new TextureUsageStruct() { TextureType = 4113354501, Unknown = 2815623008 } },
            { XivTexType.Multi, new TextureUsageStruct() { TextureType = 3531043187, Unknown = 4083110193 } },
            { XivTexType.Diffuse, new TextureUsageStruct() { TextureType = 3054951514, Unknown = 1611594207 } },
            { XivTexType.Specular, new TextureUsageStruct() { TextureType = 3367837167, Unknown = 2687453224 } },
            { XivTexType.Skin, new TextureUsageStruct() { TextureType = 940355280, Unknown = 735790577 } },
            { XivTexType.Other, new TextureUsageStruct() { TextureType = 612525193, Unknown = 1851494160 } },
        };  // Probably want to key this off a custom enum soon if we keep finding additional texture usage values.


        // Shader Parameter defaults.  For most of them they seem to be used as multipliers.
        public static Dictionary<MtrlShaderParameterId, List<float>> ShaderParameterValues = new Dictionary<MtrlShaderParameterId, List<float>>() {
            { MtrlShaderParameterId.Common1, new List<float>(){ 0.5f } },
            { MtrlShaderParameterId.Common2, new List<float>(){ 1f } },
            { MtrlShaderParameterId.SkinColor, new List<float>(){ 1.4f, 1.4f, 1.4f } },     // Direct R/G/B Multiplier.  3.0 for Limbal rings.
            { MtrlShaderParameterId.Reflection1, new List<float>(){ 1f } },
            { MtrlShaderParameterId.SkinWetnessLerp, new List<float>(){ 3f } },
            { MtrlShaderParameterId.SkinFresnel, new List<float>(){ 0.02f, 0.02f, 0.02f } },
            { MtrlShaderParameterId.SkinMatParamRow2, new List<float>(){ 0.4f, 0.4f, 0.4f } },
            { MtrlShaderParameterId.SkinUnknown2, new List<float>(){ 0f, 0f, 0f } },
            { MtrlShaderParameterId.SkinTileMultiplier, new List<float>(){ 65f, 100f } },
            { MtrlShaderParameterId.SkinTileMaterial, new List<float>(){ 63f } },
            { MtrlShaderParameterId.Equipment1, new List<float>(){ 0f } },
            { MtrlShaderParameterId.Face1, new List<float>(){ 32f } },
            { MtrlShaderParameterId.Hair1, new List<float>(){ 0.35f } },
            { MtrlShaderParameterId.Hair2, new List<float>(){ 0.5f } },


            { MtrlShaderParameterId.Furniture1, new List<float>(){ 1f, 1f, 1f } },
            { MtrlShaderParameterId.Furniture2, new List<float>(){ 1f, 1f, 1f } },
            { MtrlShaderParameterId.Furniture3, new List<float>(){ 0f, 0f, 0f } },
            { MtrlShaderParameterId.Furniture4, new List<float>(){ 1f } },
            { MtrlShaderParameterId.Furniture5, new List<float>(){ 0.15f } },
            { MtrlShaderParameterId.Furniture6, new List<float>(){ 0.15f } },
            { MtrlShaderParameterId.Furniture7, new List<float>(){ 1f } },
            { MtrlShaderParameterId.Furniture8, new List<float>(){ 1f } },
            { MtrlShaderParameterId.Furniture9, new List<float>(){ 1f } },
            { MtrlShaderParameterId.Furniture10, new List<float>(){ 1f, 1f, 1f } },
            { MtrlShaderParameterId.Furniture11, new List<float>(){ 1f, 1f, 1f } },
            { MtrlShaderParameterId.Furniture12, new List<float>(){ 1f, 1f, 1f } },
            { MtrlShaderParameterId.Furniture13, new List<float>(){ 0f, 0f, 0f } },
        };


        /// <summary>
        /// Gets a material for an item based upon the material suffix, rather than full material name.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="suffix"></param>
        /// <param name="dxVersion"></param>
        /// <returns></returns>
        public async Task<XivMtrl> GetMtrlDataByRaceSlotAndSuffix(IItemModel item, XivRace race, string slot = null, string suffix = null, int dxVersion = 11)
        {
            var root = item.GetRootInfo();
            var materialName = GetMtrlNameByRootRaceSlotSuffix(root, race, slot, suffix);
            return await GetMtrlData(item, materialName, dxVersion);
        }

        /// <summary>
        /// Calculates a name for a root's material based on the associated manually supplied data.
        /// For use in a few menus/etc. that don't actually have access to the original full material names, for whatever reason.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="race"></param>
        /// <param name="slot"></param>
        /// <param name="suffix"></param>
        /// <returns></returns>
        public static string GetMtrlNameByRootRaceSlotSuffix(XivDependencyRootInfo root, XivRace race, string slot = null, string suffix = null)
        {
            if (root.SecondaryType == null)
            {
                root.PrimaryId = race.GetRaceCodeInt();
                root.PrimaryType = XivItemType.human;
                root.SecondaryType = root.PrimaryType;
                root.SecondaryId = root.PrimaryId;
            }

            if(slot == null && root.Slot != null)
            {
                slot = root.Slot;
            }

            var pPrefix = XivItemTypes.GetSystemPrefix(root.PrimaryType);
            var pCode = root.PrimaryId.ToString().PadLeft(4, '0');
            var sPrefix = XivItemTypes.GetSystemPrefix((XivItemType)root.SecondaryType);
            var sCode = root.SecondaryId.ToString().PadLeft(4, '0');
            var mtrlFile = $"/mt_{pPrefix}{pCode}{sPrefix}{sCode}";
            if (!String.IsNullOrEmpty(slot))
            {
                mtrlFile += "_" + slot;
            }

            if (!String.IsNullOrEmpty(suffix))
            {
                mtrlFile += "_" + suffix;
            }

            mtrlFile += ".mtrl";

            return mtrlFile;
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
        public async Task<XivMtrl> GetMtrlData(IItemModel item, string mtrlFile, int dxVersion = 11)
        {

            var imc = new Imc(_gameDirectory);
            var materialSet = 0;
            try
            {
                var imcEntry = await imc.GetImcInfo(item);
                materialSet = imcEntry.Variant;
            }
            catch
            {
                var root = XivDependencyGraph.ExtractRootInfoFilenameOnly(mtrlFile);
                if (root.SecondaryType == XivItemType.hair || root.SecondaryType == XivItemType.tail || root.SecondaryType == XivItemType.body)
                {
                    // These don't have IMC files, but still have material sets somehow, but are defaulted to 1.
                    materialSet = 1;
                }
            }

            return await GetMtrlData(mtrlFile, materialSet, dxVersion);
        }

        /// <summary>
        /// Retrieves the material information for a given MTRL file, dynamically resolving the path based on the file name.
        /// </summary>
        /// <param name="mtrlFile"></param>
        /// <param name="materialSet"></param>
        /// <param name="dxVersion"></param>
        /// <returns></returns>
        public async Task<XivMtrl> GetMtrlData(string mtrlFile, int materialSet = -1, int dxVersion = 11) { 
            string mtrlPath = "";
            long mtrlOffset = 0;
            var index = new Index(_gameDirectory);
            var extractMSet = new Regex("v([0-9]{4})/");

            // Get the root from the material file in specific.
            var root = XivDependencyGraph.ExtractRootInfoFilenameOnly(mtrlFile);
            if ((!mtrlFile.StartsWith("chara/")) && mtrlFile.Count(x => x == '/') > 1)
            {
                // This is an absolute path reference.
                mtrlPath = mtrlFile;
            } else {
                var mSetMatch = extractMSet.Match(mtrlFile);
                if(mSetMatch.Success && materialSet < 0)
                {
                    materialSet = Int32.Parse(mSetMatch.Groups[1].Value);
                }
                mtrlFile = Path.GetFileName(mtrlFile);

                // Get mtrl path
                var mtrlFolder = GetMtrlFolder(root, materialSet);

                if (mtrlFile.StartsWith("/"))
                {
                    mtrlPath = $"{mtrlFolder}{mtrlFile}";
                }
                else
                {
                    mtrlPath = $"{mtrlFolder}/{mtrlFile}";
                }
            }

            mtrlOffset = await index.GetDataOffset(mtrlPath);

            if(mtrlOffset == 0)
            {
                return null;
            }

            var mtrlData = await GetMtrlData(mtrlOffset, mtrlPath, dxVersion);

            return mtrlData;
        }

        private static Regex _dummyTextureRegex = new Regex("^bgcommon/texture/dummy_[a-z]\\.tex$");

        /// <summary>
        /// Retrieves the list of texture paths used by the given mtrl path (significantly faster than loading the entire material and scanning it).
        /// </summary>
        /// <param name="mtrlPath"></param>
        /// <returns></returns>
        public async Task<List<string>> GetTexturePathsFromMtrlPath(string mtrlPath, bool includeDummies = false, bool forceOriginal = false)
        {
            var dat = new Dat(_gameDirectory);
            var mtrlData = await dat.GetType2Data(mtrlPath, forceOriginal);
            var uniqueTextures = new HashSet<string>();
            var texRegex = new Regex(".*\\.tex$");

            using (var br = new BinaryReader(new MemoryStream(mtrlData)))
            {
                // Texture count position.
                br.BaseStream.Seek(8, SeekOrigin.Begin);
                var materialDataSize = br.ReadUInt16();
                var pathsDataSize = br.ReadUInt16();
                var textureCount = br.ReadByte();
                var mapCount = br.ReadByte();
                var cSetCount = br.ReadByte();
                var dxInfoDataSize = br.ReadByte();

                var offset = 0;

                var dataOffsetBase = 16 + (mapCount * 4) + (cSetCount * 4) + (textureCount * 4);

                var textureDxInfo = new Dictionary<string, ushort>();
                for(int i = 0; i < textureCount; i++)
                {
                    // Jump to the texture name offset.
                    br.BaseStream.Seek(16 + offset, SeekOrigin.Begin);
                    var textureNameOffset = br.ReadInt16();
                    var texDxInfo = br.ReadUInt16();

                    // Jump to the texture name.
                    br.BaseStream.Seek(dataOffsetBase + textureNameOffset, SeekOrigin.Begin);

                    // Read the texture name.
                    byte a;
                    List<byte> bytes = new List<byte>(); ;
                    while ((a = br.ReadByte()) != 0)
                    {
                        bytes.Add(a);
                    }

                    var st = Encoding.ASCII.GetString(bytes.ToArray()).Replace("\0", "");

                    if (texRegex.IsMatch(st))
                    {
                        uniqueTextures.Add(st);
                    }
                    textureDxInfo[st] = texDxInfo;

                    // Bump to next texture name offset.
                    offset += 4;
                }


                //var dxInfoOffset = dataOffsetBase + materialDataSize;
                //br.BaseStream.Seek(dxInfoOffset, SeekOrigin.Begin);
                //var dxInfoByte = br.ReadByte();

                // Check for DX9/11 Conversion textures.
                List<string> add = new List<string>();
                foreach(var texture in uniqueTextures)
                {
                    // If this is a texture that has a DX Conversion.
                    if (textureDxInfo[texture] != 0)
                    {
                        if (texture.Contains("--"))
                        {
                            add.Add(texture.Replace("--", ""));
                        }
                        else
                        {
                            add.Add(texture.Insert(texture.LastIndexOf("/") + 1, "--"));
                        }
                    }
                    else
                    {
                        // This texture does not have a DX 11 conversion texture.
                    }
                }

                foreach(var s in add)
                {
                    uniqueTextures.Add(s);
                }
            }

            var rem = new List<string>();
            List<string> ret;
            if (includeDummies)
            {
                ret = uniqueTextures.ToList();
            } else {
                ret = uniqueTextures.Where(x => !_dummyTextureRegex.IsMatch(x)).ToList();
            }

            return ret;
        }

        /// <summary>
        /// Gets the MTRL data for the given offset and path
        /// </summary>
        /// <param name="mtrlOffset">The offset to the mtrl in the dat file</param>
        /// <param name="mtrlPath">The full internal game path for the mtrl</param>
        /// <returns>XivMtrl containing all the mtrl data</returns>
        public async Task<XivMtrl> GetMtrlData(long mtrlOffset, string mtrlPath, int dxVersion)
        {
            var dat = new Dat(_gameDirectory);
            var index = new Index(_gameDirectory);

            // Get uncompressed mtrl data
            var mtrlData = await dat.GetType2Data(mtrlOffset, DataFile);

            XivMtrl xivMtrl = null;

            // Why is there a semaphore here to read an in memory byte block?
            await _semaphoreSlim.WaitAsync();

            try
            {
                await Task.Run((Func<Task>)(async () =>
                {
                    using (var br = new BinaryReader(new MemoryStream(mtrlData)))
                    {
                        xivMtrl = new XivMtrl
                        {
                            Signature = br.ReadInt32(),
                            FileSize = br.ReadInt16(),
                        };
                        var colorSetDataSize = br.ReadUInt16();
                        xivMtrl.MaterialDataSize = br.ReadUInt16();
                        xivMtrl.TexturePathsDataSize = br.ReadUInt16();
                        xivMtrl.TextureCount = br.ReadByte();
                        xivMtrl.MapCount = br.ReadByte();
                        xivMtrl.ColorSetCount = br.ReadByte();
                        xivMtrl.UnknownDataSize = br.ReadByte();
                        xivMtrl.MTRLPath = mtrlPath;

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
                                pathSizeList.Add(
                                    xivMtrl.TexturePathOffsetList[i] - xivMtrl.TexturePathOffsetList[i - 1]);
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
                                pathSizeList.Add(xivMtrl.MapPathOffsetList[i] - xivMtrl.MapPathOffsetList[i - 1]);
                            }
                            else
                            {
                                if (xivMtrl.TextureCount > 0)
                                {
                                    pathSizeList.Add(xivMtrl.MapPathOffsetList[i] -
                                                     xivMtrl.TexturePathOffsetList[xivMtrl.TextureCount - 1]);
                                }
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
                                pathSizeList.Add(xivMtrl.ColorSetPathOffsetList[i] -
                                                 xivMtrl.ColorSetPathOffsetList[i - 1]);
                            }
                            else
                            {
                                pathSizeList.Add(xivMtrl.ColorSetPathOffsetList[i] -
                                                 xivMtrl.MapPathOffsetList[xivMtrl.MapCount - 1]);
                            }
                        }

                        pathSizeList.Add(xivMtrl.TexturePathsDataSize -
                                         xivMtrl.ColorSetPathOffsetList[xivMtrl.ColorSetCount - 1]);

                        var count = 0;

                        // get the texture path strings
                        xivMtrl.TexturePathList = new List<string>(xivMtrl.TextureCount);
                        for (var i = 0; i < xivMtrl.TextureCount; i++)
                        {
                            var texturePath = Encoding.UTF8.GetString(br.ReadBytes(pathSizeList[count]))
                                .Replace("\0", "");
                            var dx11FileName = Path.GetFileName(texturePath).Insert(0, "--");

                            if (await index.FileExists(HashGenerator.GetHash(dx11FileName),
                                HashGenerator.GetHash(Path.GetDirectoryName(texturePath).Replace("\\", "/")),
                                DataFile))
                            {
                                texturePath = texturePath.Insert(texturePath.LastIndexOf("/") + 1, "--");
                            }

                            xivMtrl.TexturePathList.Add(texturePath);
                            count++;
                        }

                        // get the map path strings
                        xivMtrl.MapPathList = new List<string>(xivMtrl.MapCount);
                        for (var i = 0; i < xivMtrl.MapCount; i++)
                        {
                            xivMtrl.MapPathList.Add(Encoding.UTF8.GetString(br.ReadBytes(pathSizeList[count]))
                                .Replace("\0", ""));
                            count++;
                        }

                        // get the color set path strings
                        xivMtrl.ColorSetPathList = new List<string>(xivMtrl.ColorSetCount);
                        for (var i = 0; i < xivMtrl.ColorSetCount; i++)
                        {
                            xivMtrl.ColorSetPathList.Add(Encoding.UTF8.GetString(br.ReadBytes(pathSizeList[count]))
                                .Replace("\0", ""));
                            count++;
                        }

                        var shaderPathSize = xivMtrl.MaterialDataSize - xivMtrl.TexturePathsDataSize;

                        xivMtrl.Shader = Encoding.UTF8.GetString(br.ReadBytes(shaderPathSize)).Replace("\0", "");

                        xivMtrl.Unknown2 = br.ReadBytes(xivMtrl.UnknownDataSize);

                        xivMtrl.ColorSetData = new List<Half>();
                        xivMtrl.ColorSetExtraData = null;
                        if (colorSetDataSize > 0)
                        {
                            // Color Data is always 512 (6 x 14 = 64 x 8bpp = 512)
                            var colorDataSize = 512;

                            for (var i = 0; i < colorDataSize / 2; i++)
                            {
                                xivMtrl.ColorSetData.Add(new Half(br.ReadUInt16()));
                            }

                            // If the color set is 544 in length, it has an extra 32 bytes at the end
                            if (colorSetDataSize == 544)
                            {
                                xivMtrl.ColorSetExtraData = br.ReadBytes(32);
                            }
                        }

                        var originalShaderParameterDataSize = br.ReadUInt16();

                        var originalTextureUsageCount = br.ReadUInt16();

                        var originalShaderParameterCount = br.ReadUInt16();

                        var originalTextureDescriptorCount = br.ReadUInt16();

                        xivMtrl.ShaderNumber = br.ReadUInt16();

                        xivMtrl.Unknown3 = br.ReadUInt16();

                        xivMtrl.TextureUsageList = new List<TextureUsageStruct>((int)originalTextureUsageCount);
                        for (var i = 0; i < originalTextureUsageCount; i++)
                        {
                            xivMtrl.TextureUsageList.Add(new TextureUsageStruct
                            {
                                TextureType = br.ReadUInt32(), Unknown = br.ReadUInt32()});
                        }

                        xivMtrl.ShaderParameterList = new List<ShaderParameterStruct>(originalShaderParameterCount);
                        for (var i = 0; i < originalShaderParameterCount; i++)
                        {
                            xivMtrl.ShaderParameterList.Add(new ShaderParameterStruct
                            {
                                ParameterID = (MtrlShaderParameterId) br.ReadUInt32(), Offset = br.ReadInt16(), Size = br.ReadInt16()
                            });
                        }

                        xivMtrl.TextureDescriptorList = new List<TextureDescriptorStruct>(originalTextureDescriptorCount);
                        for (var i = 0; i < originalTextureDescriptorCount; i++)
                        {
                            xivMtrl.TextureDescriptorList.Add(new TextureDescriptorStruct
                            {
                                TextureType = br.ReadUInt32(),
                                FileFormat = br.ReadInt16(),
                                Unknown = br.ReadInt16(),
                                TextureIndex = br.ReadUInt32()
                            });
                        }


                        var bytesRead = 0;
                        foreach (var shaderParam in xivMtrl.ShaderParameterList)
                        {
                            var offset = shaderParam.Offset;
                            var size = shaderParam.Size;
                            shaderParam.Args = new List<float>();
                            if (bytesRead + size <= originalShaderParameterDataSize)
                            {
                                for (var idx = offset; idx < offset + size; idx+=4)
                                {
                                    var arg = br.ReadSingle();
                                    shaderParam.Args.Add(arg);
                                    bytesRead += 4;
                                }
                            } else
                            {
                                // Just use a blank array if we have missing/invalid shader data.
                                shaderParam.Args = new List<float>(new float[size / 4]);
                            }
                        }

                        // Chew through any remaining padding.
                        while(bytesRead < originalShaderParameterDataSize)
                        {
                            br.ReadByte();
                            bytesRead++;
                        }


                    }
                }));
            }
            finally
            {
                _semaphoreSlim.Release();
            }

            return xivMtrl;
        }

        /// <summary>
        /// Converts an xivMtrl to a XivTex for ColorSet exporting
        /// </summary>
        /// <param name="xivMtrl">The XivMtrl with the ColorSet data</param>
        /// <returns>The XivTex of the ColorSet</returns>
        public Task<XivTex> MtrlToXivTex(XivMtrl xivMtrl, TexTypePath ttp)
        {
            return Task.Run(() =>
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
            });
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
            var toWrite = xivMtrl.ColorSetExtraData != null ? xivMtrl.ColorSetExtraData : new byte[32];
            var path = IOUtil.MakeItemSavePath(item, saveDirectory, race);

            Directory.CreateDirectory(path);

            var savePath = Path.Combine(path, Path.GetFileNameWithoutExtension(xivMtrl.MTRLPath) + ".dat");

            File.WriteAllBytes(savePath, toWrite);
        }


        /// <summary>
        /// Imports a XivMtrl by converting it to bytes, then injecting it.
        /// </summary>
        /// <param name="xivMtrl">The XivMtrl containing the mtrl data</param>
        /// <param name="item">The item whos mtrl is being imported</param>
        /// <param name="source">The source/application that is writing to the dat.</param>
        /// <returns>The new offset</returns>
        public async Task<long> ImportMtrl(XivMtrl xivMtrl, IItem item, string source)
        {
            try
            {
                var mtrlBytes = CreateMtrlFile(xivMtrl, item);
                var dat = new Dat(_gameDirectory);

                // Create the actual raw MTRL first. - Files should always be created top down.
                long offset = await dat.ImportType2Data(mtrlBytes.ToArray(), item.Name, xivMtrl.MTRLPath, item.SecondaryCategory, source);

                // The MTRL file is now ready to go, but we need to validate the texture paths and create them if needed.
                var mapInfoList = xivMtrl.GetAllMapInfos(false);
                var _index = new Index(_gameDirectory);
                var _tex = new Tex(_gameDirectory);
                foreach (var mapInfo in mapInfoList)
                {
                    var path = mapInfo.Path;
                    var fileHash = HashGenerator.GetHash(Path.GetFileName(path));
                    var pathHash = HashGenerator.GetHash(path.Substring(0, path.LastIndexOf("/", StringComparison.Ordinal)));
                    var exists = await _index.FileExists(fileHash, pathHash, IOUtil.GetDataFileFromPath(path));

                    if(exists)
                    {
                        continue;
                    }

                    var format = XivTexFormat.A8R8G8B8;

                    var xivTex = new XivTex();
                    xivTex.TextureTypeAndPath = new TexTypePath()
                    {
                        DataFile = IOUtil.GetDataFileFromPath(path), Path = path, Type = mapInfo.Usage
                    };
                    xivTex.TextureFormat = format;

                    var di = Tex.GetDefaultTexturePath(mapInfo.Usage);

                    var newOffset = await _tex.ImportTex(xivTex.TextureTypeAndPath.Path, di.FullName, item, source);

                }

                return offset;
            }
            catch(Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// Converts an XivMtrl object into the raw bytes of a Mtrl file.
        /// </summary>
        /// <param name="xivMtrl">The XivMtrl containing the mtrl data</param>
        /// <param name="item">The item</param>
        /// <returns>The new mtrl file byte data</returns>
        public byte[] CreateMtrlFile(XivMtrl xivMtrl, IItem item)
        {

            var mtrlBytes = new List<byte>();

            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.Signature));

            var fileSizePointer = mtrlBytes.Count;
            mtrlBytes.AddRange(BitConverter.GetBytes((ushort)0)); //Backfilled later
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ColorSetDataSize));

            var materialDataSizePointer = mtrlBytes.Count;
            mtrlBytes.AddRange(BitConverter.GetBytes((ushort)0)); //Backfilled later

            var texturePathsDataSizePointer = mtrlBytes.Count;
            mtrlBytes.AddRange(BitConverter.GetBytes((ushort)0)); //Backfilled later

            mtrlBytes.Add((byte)xivMtrl.TexturePathList.Count);
            mtrlBytes.Add((byte)xivMtrl.MapPathList.Count);
            mtrlBytes.Add((byte)xivMtrl.ColorSetPathList.Count);
            mtrlBytes.Add(xivMtrl.UnknownDataSize);

            // Regenerate offset list as we build the string list.
            xivMtrl.TexturePathOffsetList.Clear();
            xivMtrl.MapPathOffsetList.Clear();
            xivMtrl.ColorSetPathOffsetList.Clear();


            var stringListBytes = new List<byte>();
            var texIdx = 0;
            foreach (var texPathString in xivMtrl.TexturePathList)
            {
                xivMtrl.TexturePathOffsetList.Add(stringListBytes.Count);
                var path = texPathString;
                // This is an old style DX9/DX11 Mixed Texture reference, make sure to clean it up if needed.
                if(xivMtrl.TexturePathUnknownList[texIdx] != 0)
                {
                    path = path.Replace("--", string.Empty);
                }

                stringListBytes.AddRange(Encoding.UTF8.GetBytes(path));
                stringListBytes.Add(0);
                texIdx++;
            }

            foreach (var mapPathString in xivMtrl.MapPathList)
            {
                xivMtrl.MapPathOffsetList.Add(stringListBytes.Count);
                stringListBytes.AddRange(Encoding.UTF8.GetBytes(mapPathString));
                stringListBytes.Add(0);
            }

            foreach (var colorSetPathString in xivMtrl.ColorSetPathList)
            {
                xivMtrl.ColorSetPathOffsetList.Add(stringListBytes.Count);
                stringListBytes.AddRange(Encoding.UTF8.GetBytes(colorSetPathString));
                stringListBytes.Add(0);
            }

            xivMtrl.TexturePathsDataSize = (ushort)stringListBytes.Count;

            stringListBytes.AddRange(Encoding.UTF8.GetBytes(xivMtrl.Shader));
            stringListBytes.Add(0);

            var padding = (stringListBytes.Count % 8);
            if (padding != 0)
            {
                padding = 8 - padding;
            }

            stringListBytes.AddRange(new byte[padding]);
            xivMtrl.MaterialDataSize = (ushort)stringListBytes.Count;




            // Write the new offset list.
            for (var i = 0; i < xivMtrl.TexturePathOffsetList.Count; i++)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.TexturePathOffsetList[i]));
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.TexturePathUnknownList[i]));
            }

            for (var i = 0; i < xivMtrl.MapPathOffsetList.Count; i++)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.MapPathOffsetList[i]));
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.MapPathUnknownList[i]));
            }

            for (var i = 0; i < xivMtrl.ColorSetPathOffsetList.Count; i++)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.ColorSetPathOffsetList[i]));
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.ColorSetPathUnknownList[i]));
            }

            // Write the actual string list (including padding).
            mtrlBytes.AddRange(stringListBytes);

            // Don't know what these (4) bytes do, but hey, whatever.
            mtrlBytes.AddRange(xivMtrl.Unknown2);

            foreach (var colorSetHalf in xivMtrl.ColorSetData)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes(colorSetHalf.RawValue));
            }

            if (xivMtrl.ColorSetDataSize == 544)
            {
                mtrlBytes.AddRange(xivMtrl.ColorSetExtraData);
            }


            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ShaderParameterDataSize));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.TextureUsageCount));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ShaderParameterCount));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.TextureDescriptorCount));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ShaderNumber));

            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.Unknown3));

            foreach (var dataStruct1 in xivMtrl.TextureUsageList)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes(dataStruct1.TextureType));
                mtrlBytes.AddRange(BitConverter.GetBytes(dataStruct1.Unknown));
            }

            var offset = 0;
            foreach (var parameter in xivMtrl.ShaderParameterList)
            {
                // Ensure we're writing correctly calculated data.
                parameter.Offset = (short) offset;
                parameter.Size = (short)parameter.Args.Count;
                offset += parameter.Size * 4;
                short byteSize = (short)(parameter.Size * 4);

                mtrlBytes.AddRange(BitConverter.GetBytes((uint)parameter.ParameterID));
                mtrlBytes.AddRange(BitConverter.GetBytes(parameter.Offset));
                mtrlBytes.AddRange(BitConverter.GetBytes(byteSize));
            }

            foreach (var parameterStruct in xivMtrl.TextureDescriptorList)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes(parameterStruct.TextureType));
                mtrlBytes.AddRange(BitConverter.GetBytes(parameterStruct.FileFormat));
                mtrlBytes.AddRange(BitConverter.GetBytes(parameterStruct.Unknown));
                mtrlBytes.AddRange(BitConverter.GetBytes(parameterStruct.TextureIndex));
            }



            var shaderBytes = new List<byte>();
            foreach (var shaderParam in xivMtrl.ShaderParameterList)
            {
                foreach (var f in shaderParam.Args)
                {
                    shaderBytes.AddRange(BitConverter.GetBytes(f));
                }
            }

            // Pad out if we're missing anything.
            if (shaderBytes.Count < xivMtrl.ShaderParameterDataSize)
            {
                shaderBytes.AddRange(new byte[xivMtrl.ShaderParameterDataSize - shaderBytes.Count]);
            }
            mtrlBytes.AddRange(shaderBytes);



            // Backfill the actual file size data.
            xivMtrl.FileSize = (short)mtrlBytes.Count;
            IOUtil.ReplaceBytesAt(mtrlBytes, BitConverter.GetBytes(xivMtrl.FileSize), fileSizePointer);

            xivMtrl.MaterialDataSize = (ushort)stringListBytes.Count;
            IOUtil.ReplaceBytesAt(mtrlBytes, BitConverter.GetBytes(xivMtrl.MaterialDataSize), materialDataSizePointer);

            IOUtil.ReplaceBytesAt(mtrlBytes, BitConverter.GetBytes(xivMtrl.TexturePathsDataSize), texturePathsDataSizePointer);
            return mtrlBytes.ToArray();
        }



        // Helper regexes for GetMtrlPath.
        private static readonly Regex _raceRegex = new Regex("(c[0-9]{4})");
        private static readonly Regex _weaponMatch = new Regex("(w[0-9]{4})");
        private static readonly Regex _tailMatch = new Regex("(t[0-9]{4})");
        private static readonly Regex _raceMatch = new Regex("(c[0-9]{4})");
        private static readonly Regex _bodyRegex = new Regex("(b[0-9]{4})");
        private static readonly Regex _skinRegex = new Regex("^/mt_c([0-9]{4})b([0-9]{4})_.+\\.mtrl$");

        /// <summary>
        /// Resolves the MTRL path for a given MDL path.
        /// Only needed because of the rare exception case of skin materials.
        /// </summary>
        /// <param name="mdlPath"></param>
        /// <param name="mtrlVariant">Which material variant folder.  Defaulted to 1.</param>
        /// <returns></returns>
        public string GetMtrlPath(string mdlPath, string mtrlName, int mtrlVariant = 1)
        {
            var mtrlFolder = "";

            // Now then, skin materials resolve to their racial path, always.
            var match = _skinRegex.Match(mtrlName);
            if (match.Success)
            {

                // Only switch mdl races around if we're a skin texture.
                var mdlMatch = _raceMatch.Match(mdlPath);
                var mtrlMatch = _raceMatch.Match(mtrlName);


                // Both Items have racial model information in their path, and the races DON'T match.
                if (mdlMatch.Success && mtrlMatch.Success && mdlMatch.Groups[1].Value != mtrlMatch.Groups[1].Value)
                {

                    // Need to find the racial skin for this race.
                    var baseRace = XivRaces.GetXivRace(mdlMatch.Groups[1].Value.Substring(1));
                    var skinRace = XivRaceTree.GetSkinRace(baseRace);
                    var skinRaceString = "c" + XivRaces.GetRaceCode(skinRace);

                    // In this case, we actually replace both with the racial skin material based on the Model, which has priority.
                    mtrlName = mtrlName.Replace(mtrlMatch.Groups[1].Value, skinRaceString);
                    mdlPath = mdlPath.Replace(mdlMatch.Groups[1].Value, skinRaceString);

                    // If we actually shifted races, reset the body identifier.
                    // This shouldn't really ever happen, but safety check.
                    if(baseRace != skinRace)
                    {
                        mtrlName = _bodyRegex.Replace(mtrlName, "b0001");
                        mdlPath = _bodyRegex.Replace(mdlPath, "b0001");
                    }
                }


                var race = match.Groups[1].Value;
                var body = match.Groups[2].Value;

                mtrlFolder = "chara/human/c" + race + "/obj/body/b" + body + "/material/v0001";

            }
            else if (mtrlName.LastIndexOf("/") > 0)
            {
                // This a furniture item or something else that specifies an explicit full material path.
                // We can just return that.
                return mtrlName;

            } else if (mdlPath.Contains("/face/f") || mdlPath.Contains("/zear/z")) {

                // Faces and ears don't use material variants.
                var mdlFolder = Path.GetDirectoryName(mdlPath);
                mdlFolder = mdlFolder.Replace("\\", "/");
                var baseFolder = mdlFolder.Substring(0, mdlFolder.LastIndexOf("/"));
                mtrlFolder = baseFolder + "/material";
            }

            else {

                var mdlMatch = _raceRegex.Match(mdlPath);
                var mtrlMatch = _raceRegex.Match(mtrlName);

                // Both items have racaial information in their path, and the races DON'T match.
                if(mdlMatch.Success && mtrlMatch.Success && mdlMatch.Groups[1].Value != mtrlMatch.Groups[1].Value)
                {
                    // In this case, we need to replace the MDL path's racial string with the racial string from the MTRL.
                    // This only really happens in hair items, that have unique racial model paths, but often share materials still.
                    mdlPath = mdlPath.Replace(mdlMatch.Groups[1].Value, mtrlMatch.Groups[1].Value);
                }

                mdlMatch = _weaponMatch.Match(mdlPath);
                mtrlMatch = _weaponMatch.Match(mtrlName);

                // Both items have weapon model information in their path, and the weapons DON'T match.
                if (mdlMatch.Success && mtrlMatch.Success && mdlMatch.Groups[1].Value != mtrlMatch.Groups[1].Value)
                {
                    // In this case, we need to replace the MDL path's weapon string with the weapon string from the MTRL.
                    // This really only seems to happen with dual wield weapons and the Gauss Barrel.
                    mdlPath = mdlPath.Replace(mdlMatch.Groups[1].Value, mtrlMatch.Groups[1].Value);
                }

                mdlMatch = _tailMatch.Match(mdlPath);
                mtrlMatch = _tailMatch.Match(mtrlName);

                // Both items have tail model information in their path, and the weapons DON'T match.
                if (mdlMatch.Success && mtrlMatch.Success && mdlMatch.Groups[1].Value != mtrlMatch.Groups[1].Value)
                {
                    // Replacing the tail reference in the main path with the one from the MTRL.
                    // Needless to say, this only happens with tail items.
                    mdlPath = mdlPath.Replace(mdlMatch.Groups[1].Value, mtrlMatch.Groups[1].Value);
                }


                var mdlFolder = Path.GetDirectoryName(mdlPath);
                mdlFolder = mdlFolder.Replace("\\", "/");

                var baseFolder = mdlFolder.Substring(0, mdlFolder.LastIndexOf("/"));
                mtrlFolder = baseFolder + "/material/v" + mtrlVariant.ToString().PadLeft(4, '0');
            }

            return mtrlFolder + mtrlName;
        }




        /// <summary>
        /// Synchronously generate a MTRL foler from the constituent parts.
        /// </summary>
        /// <param name="itemModel"></param>
        /// <param name="itemType"></param>
        /// <param name="xivRace"></param>
        /// <param name="variant"></param>
        /// <returns></returns>
        public string GetMtrlFolder(IItemModel itemModel, int materialSet = 0)
        {
            var root = itemModel.GetRootInfo();
            return GetMtrlFolder(root, materialSet);
        }

        public static async Task<int> GetMaterialSetId(IItem item)
        {
            if (item == null) return -1;

            var root = item.GetRootInfo();
            if (root == null) return -1;

            if (root.PrimaryType == XivItemType.human && (root.SecondaryType == XivItemType.hair ||
                root.SecondaryType == XivItemType.body ||
                root.SecondaryType == XivItemType.tail))
            {
                return 1;
            }

            try
            {
                var imc = new Imc(XivCache.GameInfo.GameDirectory);
                var entry = await imc.GetImcInfo((IItemModel)item);
                return entry.Variant;
            } catch
            {
                return 0;
            }

        }
        public string GetMtrlFolder(XivDependencyRootInfo root, int materialSet = -1) 
        {
            // These types have exactly one material set, but don't have an IMC file saying so.
            if((root.SecondaryType == XivItemType.hair ||
                root.SecondaryType == XivItemType.body ||
                root.SecondaryType == XivItemType.tail ) && materialSet <= 0)
            {
                materialSet = 1; 
            }

            var mtrlFolder = root.GetRootFolder() + "material";
            if (materialSet > 0)
            {
                var version = materialSet.ToString().PadLeft(4, '0');
                mtrlFolder += $"/v{version}";
            }

            return mtrlFolder;

        }
        

        public void Dipose()
        {
            _semaphoreSlim?.Dispose();
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
            {XivStrings.Earring, "ear"},
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
            {XivStrings.Hair, "hir"},
            {XivStrings.Ear, "zer"},
            {XivStrings.InnerEar, "fac_"},
            {XivStrings.OuterEar, ""}
        };

    }
}