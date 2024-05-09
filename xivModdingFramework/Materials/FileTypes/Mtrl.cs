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

using HelixToolkit.SharpDX.Core.Helper;
using SharpDX;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
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
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.Enums;
using xivModdingFramework.Textures.FileTypes;
using xivModdingFramework.Variants.FileTypes;
using static xivModdingFramework.Materials.DataContainers.ShaderHelpers;
using Index = xivModdingFramework.SqPack.FileTypes.Index;

namespace xivModdingFramework.Materials.FileTypes
{
    /// <summary>
    /// This class contains the methods that deal with the .mtrl file type 
    /// </summary>
    public class Mtrl
    {
        private const string MtrlExtension = ".mtrl";
        private readonly DirectoryInfo _gameDirectory;
        private static SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

        public const string EmptySamplerPrefix = "_EMPTY_SAMPLER_";

        public Mtrl(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
        }


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
        public async Task<XivMtrl> GetMtrlData(IItemModel item, string mtrlFile, int dxVersion = 11, ModTransaction tx = null)
        {

            var imc = new Imc(_gameDirectory);
            var materialSet = 0;
            try
            {
                var imcEntry = await imc.GetImcInfo(item);
                materialSet = imcEntry.MaterialSet;
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

            return await GetMtrlData(mtrlFile, materialSet, dxVersion, tx);
        }

        /// <summary>
        /// Retrieves the material information for a given MTRL file, dynamically resolving the path based on the file name.
        /// </summary>
        /// <param name="mtrlFile"></param>
        /// <param name="materialSet"></param>
        /// <param name="dxVersion"></param>
        /// <returns></returns>
        public async Task<XivMtrl> GetMtrlData(string mtrlFile, int materialSet = -1, int dxVersion = 11, ModTransaction tx = null) { 
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

            if (tx != null)
            {
                var df = IOUtil.GetDataFileFromPath(mtrlPath);
                mtrlOffset = (await tx.GetIndexFile(df)).Get8xDataOffset(mtrlPath);
            }
            else
            {
                mtrlOffset = await index.GetDataOffset(mtrlPath);
            }

            if(mtrlOffset == 0)
            {
                return null;
            }

            var mtrlData = await GetMtrlData(mtrlOffset, mtrlPath);

            return mtrlData;
        }

        private static Regex _dummyTextureRegex = new Regex("^bgcommon/texture/dummy_[a-z]\\.tex$");

        /// <summary>
        /// Retrieves the list of texture paths used by the given mtrl path (significantly faster than loading the entire material and scanning it).
        /// </summary>
        /// <param name="mtrlPath"></param>
        /// <returns></returns>
        public async Task<List<string>> GetTexturePathsFromMtrlPath(string mtrlPath, bool includeDummies = false, bool forceOriginal = false, ModTransaction tx = null)
        {
            var dat = new Dat(_gameDirectory);
            var mtrlData = await dat.GetType2Data(mtrlPath, forceOriginal, tx);
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


        public XivMtrl GetMtrlData(byte[] bytes, string internalMtrlPath = "")
        {
            var xivMtrl = new XivMtrl();
            using (var br = new BinaryReader(new MemoryStream(bytes)))
            {
                xivMtrl = new XivMtrl
                {
                    MTRLPath = internalMtrlPath,
                    Signature = br.ReadInt32(),
                    FileSize = br.ReadInt16(),
                };

                var colorSetDataSize = br.ReadUInt16();
                var stringBlockSize = br.ReadUInt16();
                var shaderNameOffset = br.ReadUInt16();
                var texCount = br.ReadByte();
                var mapCount = br.ReadByte();
                var colorsetCount = br.ReadByte();
                var additionalDataSize = br.ReadByte();


                xivMtrl.Textures = new List<MtrlTexture>();


                // Texture String Information.
                var texPathOffsets = new List<int>(texCount);
                var texFlags = new List<short>(texCount);
                for (var i = 0; i < texCount; i++)
                {
                    var tex = new MtrlTexture();
                    texPathOffsets.Add(br.ReadInt16());

                    var flags = br.ReadInt16();
                    texFlags.Add(flags);
                    tex.Flags = (ushort)flags;
                    xivMtrl.Textures.Add(tex);
                }

                // Map String Information.
                var mapOffset = new List<int>(mapCount);
                xivMtrl.MapStrings = new List<MtrlString>();
                for (var i = 0; i < mapCount; i++)
                {
                    mapOffset.Add(br.ReadInt16());

                    var map = new MtrlString();
                    map.Flags = br.ReadUInt16();
                    xivMtrl.MapStrings.Add(map);
                }

                // Colorset String Information.
                var colorsetOffsets = new List<int>(colorsetCount);
                xivMtrl.ColorsetStrings = new List<MtrlString>();
                for (var i = 0; i < colorsetCount; i++)
                {
                    colorsetOffsets.Add(br.ReadInt16());

                    var colorset = new MtrlString();
                    colorset.Flags = br.ReadUInt16();
                    xivMtrl.ColorsetStrings.Add(colorset);
                }

                var stringBlockStart = br.BaseStream.Position;
                for (var i = 0; i < texCount; i++)
                {
                    br.BaseStream.Seek(stringBlockStart + texPathOffsets[i], SeekOrigin.Begin);
                    var path = Dat.ReadNullTerminatedString(br);
                    xivMtrl.Textures[i].TexturePath = path;
                }

                for (var i = 0; i < xivMtrl.MapStrings.Count; i++)
                {
                    br.BaseStream.Seek(stringBlockStart + mapOffset[i], SeekOrigin.Begin);
                    var st = Dat.ReadNullTerminatedString(br);
                    xivMtrl.MapStrings[i].Value = st;
                }

                for (var i = 0; i < xivMtrl.ColorsetStrings.Count; i++)
                {
                    br.BaseStream.Seek(stringBlockStart + colorsetOffsets[i], SeekOrigin.Begin);
                    var st = Dat.ReadNullTerminatedString(br);
                    xivMtrl.ColorsetStrings[i].Value = st;
                }

                br.BaseStream.Seek(stringBlockStart + shaderNameOffset, SeekOrigin.Begin);
                xivMtrl.ShaderPackRaw = Dat.ReadNullTerminatedString(br);

                br.BaseStream.Seek(stringBlockStart + stringBlockSize, SeekOrigin.Begin);

                xivMtrl.AdditionalData = br.ReadBytes(additionalDataSize);

                xivMtrl.ColorSetData = new List<Half>();


                if (colorSetDataSize > 0)
                {
                    // Color Data is always 512 (6 x 14 = 64 x 8bpp = 512)
                    // DT: Color Data is always 2048 instead
                    var colorDataSize = (colorSetDataSize >= 2048) ? 2048 : 512;

                    for (var i = 0; i < colorDataSize / 2; i++)
                    {
                        xivMtrl.ColorSetData.Add(new Half(br.ReadUInt16()));
                    }

                    // If the color set is 544 (DT: 2080) in length, it has an extra 32 bytes at the end
                    if (colorSetDataSize == colorDataSize + 32)
                    {
                        // Endwalker style Dye Data
                        // ( 2 Bytes per Row )
                        // 5 Bits flags
                        // Flags :
                        // 0 - Copies Diffuse Color (bytes 0/1/2)
                        // 1 - Copies Spec Color (bytes 4/5/6)
                        // 2 - Glow Color (Bytes 8/9/10)
                        // 3 - Spec Power (Byte 3)
                        // 4 - Gloss (Byte 7)
                        // 
                        // 11x Bits Template ID 

                        xivMtrl.ColorSetDyeData = br.ReadBytes(32);
                    }
                    if (colorSetDataSize == colorDataSize + 128)
                    {
                        // Dawntrail style Dye Data
                        // ( 4 bytes per row )
                        // 12 Bits Flags - Determines what to copy

                        // 4 Bits Unknown
                        // 11 Bits Template ID
                        // 2 Bits Dye Channel Selector
                        // 3 Bits Unknown.

                        // 0 - Copies Diffuse Color (half 0/1/2)
                        // 1 - Copies Spec Color (half 4/5/6)
                        // 2 - Glow Color (half 8/9/10)
                        // 3 - ??? (half 11)
                        // 4 - ??? (half 18)
                        // 5 - ??? (half 16)
                        // 6 - ??? (half 12)
                        // 7 - ??? (half 13)
                        // 8 - ??? (half 14)
                        // 9 - ??? (half 19)
                        //10 - ??? (half 27)
                        //11 - ??? (half 21)

                        xivMtrl.ColorSetDyeData = br.ReadBytes(128);
                    }
                }

                var shaderConstantsDataSize = br.ReadUInt16();

                var shaderKeysCount = br.ReadUInt16();
                var shaderConstantsCount = br.ReadUInt16();
                var textureSamplerCount = br.ReadUInt16();

                xivMtrl.MaterialFlags = br.ReadUInt16();
                xivMtrl.MaterialFlags2 = br.ReadUInt16();

                xivMtrl.ShaderKeys = new List<ShaderKey>((int)shaderKeysCount);
                for (var i = 0; i < shaderKeysCount; i++)
                {
                    xivMtrl.ShaderKeys.Add(new ShaderKey
                    {
                        KeyId = br.ReadUInt32(),
                        Value = br.ReadUInt32()
                    });
                }

                xivMtrl.ShaderConstants = new List<ShaderConstant>(shaderConstantsCount);
                var constantOffsets = new List<short>();
                var constantSizes = new List<short>();
                for (var i = 0; i < shaderConstantsCount; i++)
                {
                    xivMtrl.ShaderConstants.Add(new ShaderConstant
                    {
                        ConstantId = br.ReadUInt32()
                    });
                    constantOffsets.Add(br.ReadInt16());
                    constantSizes.Add(br.ReadInt16());
                }

                for (var i = 0; i < textureSamplerCount; i++)
                {
                    var sampler = new TextureSampler
                    {
                        SamplerIdRaw = br.ReadUInt32(),
                        SamplerSettingsRaw = br.ReadUInt32(),
                    };

                    var textureIndex = br.ReadByte();
                    var padding = br.ReadBytes(3);

                    if (xivMtrl.Textures.Count > textureIndex)
                    {
                        xivMtrl.Textures[textureIndex].Sampler = sampler;
                    }
                    else
                    {
                        // Create a fake texture to hold this sampler.
                        var tex = new MtrlTexture();
                        tex.TexturePath = EmptySamplerPrefix + sampler.SamplerId;
                        tex.Sampler = sampler;
                        xivMtrl.Textures.Add(tex);
                    }
                }


                var bytesRead = 0;
                for (int i = 0; i < xivMtrl.ShaderConstants.Count; i++)
                {
                    var shaderConstant = xivMtrl.ShaderConstants[i];
                    var offset = constantOffsets[i];
                    var size = constantSizes[i];
                    shaderConstant.Values = new List<float>();
                    if (bytesRead + size <= shaderConstantsDataSize)
                    {
                        for (var idx = offset; idx < offset + size; idx += 4)
                        {
                            var arg = br.ReadSingle();
                            shaderConstant.Values.Add(arg);
                            bytesRead += 4;
                        }
                    }
                    else
                    {
                        // Just use a blank array if we have missing/invalid shader data.
                        shaderConstant.Values = new List<float>(new float[size / 4]);
                    }
                }

                // Chew through any remaining padding.
                while (bytesRead < shaderConstantsDataSize)
                {
                    br.ReadByte();
                    bytesRead++;
                }
            }

            return xivMtrl;
        }

        /// <summary>
        /// Gets the MTRL data for the given offset and path
        /// </summary>
        /// <param name="mtrlOffset">The offset to the mtrl in the dat file</param>
        /// <param name="mtrlPath">The full internal game path for the mtrl</param>
        /// <returns>XivMtrl containing all the mtrl data</returns>
        public async Task<XivMtrl> GetMtrlData(long mtrlOffset, string mtrlPath)
        {
            var dat = new Dat(_gameDirectory);
            var index = new Index(_gameDirectory);

            // Get uncompressed mtrl data
            var df = IOUtil.GetDataFileFromPath(mtrlPath);
            var mtrlData = await dat.GetType2Data(mtrlOffset, df);

            XivMtrl xivMtrl = null;
            await Task.Run((Func<Task>)(async () =>
            {
                xivMtrl = GetMtrlData(mtrlData, mtrlPath);
            }));

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
                var width = 4;
                var height = 16;

                if(xivMtrl.ColorSetData.Count == 1024)
                {
                    width = 8;
                    height = 32;
                }

                var xivTex = new XivTex
                {
                    Width = width,
                    Height = height,
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
            var toWrite = xivMtrl.ColorSetDyeData != null ? xivMtrl.ColorSetDyeData : new byte[32];
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
        public async Task<long> ImportMtrl(XivMtrl xivMtrl, IItem item, string source, bool validateTextures = true, ModTransaction tx = null)
        {
            try
            {
                var mtrlBytes = CreateMtrlFile(xivMtrl);
                var dat = new Dat(_gameDirectory);

                // Create the actual raw MTRL first. - Files should always be created top down.
                long offset = await dat.ImportType2Data(mtrlBytes.ToArray(), xivMtrl.MTRLPath, source, item, tx);

                if (validateTextures)
                {
                    // The MTRL file is now ready to go, but we need to validate the texture paths and create them if needed.
                    var _index = new Index(_gameDirectory);
                    var _tex = new Tex(_gameDirectory);

                    var dataFile = IOUtil.GetDataFileFromPath(xivMtrl.MTRLPath);
                    IndexFile index = tx == null ? await _index.GetIndexFile(dataFile, false, true) : await tx.GetIndexFile(dataFile);

                    foreach (var tex in xivMtrl.Textures)
                    {
                        var path = tex.TexturePath;

                        // Ignore empty samplers.
                        if (path.StartsWith(EmptySamplerPrefix)) continue;

                        var exists = index.FileExists(tex.TexturePath);
                        if (exists)
                        {
                            continue;
                        }

                        var format = XivTexFormat.A8R8G8B8;

                        var xivTex = new XivTex();
                        xivTex.TextureTypeAndPath = new TexTypePath()
                        {
                            DataFile = IOUtil.GetDataFileFromPath(path),
                            Path = path,
                            Type = tex.Usage
                        };
                        xivTex.TextureFormat = format;

                        var di = Tex.GetDefaultTexturePath(tex.Usage);

                        var newOffset = await _tex.ImportTex(xivTex.TextureTypeAndPath.Path, di.FullName, item, source, tx);

                    }
                }

                return offset;
            }
            catch(Exception ex)
            {
                throw ex;
            }
        }

        public async Task FixPreDawntrailMaterials(List<string> paths, string source, ModTransaction tx, IProgress<(int current, int total, string message)> progress = null)
        {
            var _dat = new Dat(XivCache.GameInfo.GameDirectory);

            var total = paths.Count;

            progress?.Report((0, total, "Updating Materials..."));

            // Alter the MTRLs.
            var indexesToCreate = new List<(string indexTextureToCreate, string normalToCreateFrom)>();
            var indexToMtrlDictionary = new Dictionary<string, string>();

            var count = 0;
            foreach (var path in paths)
            {
                var res = await FixPreDawntrailMaterial(await GetMtrlData(path), source, tx);
                if(res.indexTextureToCreate != null)
                {
                    indexesToCreate.Add(res);

                    if (!indexToMtrlDictionary.ContainsKey(res.indexTextureToCreate))
                    {
                        indexToMtrlDictionary.Add(res.indexTextureToCreate, path);
                    }
                }
                count++;
                progress?.Report((count, total, "Updating Materials..."));
            }

            count = 0;
            progress?.Report((0, total, "Creating Index Textures..."));
            // Create the new Index DDS files.

            // Max we allow to run at a time.
            // This is a safety measure to prevent us nuking the user's RAM by loading a zillion textures into memory at once.
            const int _SIMULTANEOUS_MAX = 10;

            for(int i = 0; i < indexesToCreate.Count; i += _SIMULTANEOUS_MAX)
            {
                var subList = indexesToCreate.Skip(i).Take(_SIMULTANEOUS_MAX).ToList();
                
                var tasks = new List<Task<(string indexFilePath, byte[] data)>>();                
                foreach (var tup in subList)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var val = await CreateIndexFromNormal(tup.indexTextureToCreate, tup.normalToCreateFrom);
                        count++;
                        progress?.Report((count, total, "Creating Index Textures..."));
                        return val;
                    }));
                }

                await Task.WhenAll(tasks);

                // Write this chunk of files to disk.
                var results = tasks.Select(x => x.Result).ToList();
                foreach (var texData in results)
                {
                    await _dat.WriteModFile(texData.data, texData.indexFilePath, source, null, tx);
                }

                // Fix their modpack references.
                var modList = await tx.GetModList();

                foreach(var kv in indexToMtrlDictionary)
                {
                    var mtrlMod = modList.Mods.FirstOrDefault(x => x.fullPath == kv.Value);

                    if (mtrlMod == null)
                        continue;

                    var indexMod = modList.Mods.FirstOrDefault(x => x.fullPath == kv.Key);

                    indexMod.modPack = mtrlMod.modPack;
                }
            }
        }


        public async Task<(string indexTextureToCreate, string normalToCreateFrom)> FixPreDawntrailMaterial(XivMtrl mtrl, string source, ModTransaction tx = null)
        {
            if(mtrl.ColorSetData.Count != 256)
            {
                // Already updated or doesn't need updating.
                return (null, null);
            }

            if(mtrl.ShaderPack == EShaderPack.Character || mtrl.ShaderPack == EShaderPack.Skin)
            {
                return await FixPreDawntrailCharacterMaterial(mtrl, source, tx);
            }

            if(mtrl.ShaderPack== EShaderPack.Hair)
            {

            }
            return (null, null);
        }


        /// <summary>
        /// Updates a given Endwalker style Material to a Dawntrail style material, returning a tuple containing the Index Map that should be created after,
        /// and the normal map that should be used in the creation.
        /// </summary>
        /// <param name="mtrl"></param>
        /// <param name="updateShaders"></param>
        /// <param name="source"></param>
        /// <returns></returns>
        private async Task<(string indexTextureToCreate, string normalToCreateFrom)> FixPreDawntrailCharacterMaterial(XivMtrl mtrl, string source, ModTransaction tx = null)
        {
            if(mtrl.ColorSetData.Count != 256)
            {
                // This is already upgraded.
                return (null, null);
            }

            if (mtrl.ShaderPack == EShaderPack.Character)
            {
                mtrl.ShaderPack = EShaderPack.CharacterLegacy;
            } else if(mtrl.ShaderPack == EShaderPack.Skin)
            {
                mtrl.ShaderPack = EShaderPack.SkinLegacy;
            } else
            {
                // No upgrade protocol for other shaders.
                return (null, null);
            }

            if (mtrl.ColorSetData != null)
            {
                // Update Colorset
                List<Half> newData = new List<Half>();
                for (int i = 0; i < mtrl.ColorSetData.Count; i += 16)
                {
                    var pixel = i + 0;

                    // Diffuse Pixel
                    newData.Add(mtrl.ColorSetData[pixel + 0]);
                    newData.Add(mtrl.ColorSetData[pixel + 1]);
                    newData.Add(mtrl.ColorSetData[pixel + 2]);
                    newData.Add(mtrl.ColorSetData[pixel + 7]);  // SE flipped Specular Power and Gloss values for some reason.

                    pixel += 4;

                    // Specular Pixel
                    newData.Add(mtrl.ColorSetData[pixel + 0]);
                    newData.Add(mtrl.ColorSetData[pixel + 1]);
                    newData.Add(mtrl.ColorSetData[pixel + 2]);
                    newData.Add(mtrl.ColorSetData[pixel - 1]);  // SE flipped Specular Power and Gloss values for some reason.

                    pixel += 4;
                    // Emissive Pixel
                    newData.Add(mtrl.ColorSetData[pixel + 0]);
                    newData.Add(mtrl.ColorSetData[pixel + 1]);
                    newData.Add(mtrl.ColorSetData[pixel + 2]);
                    newData.Add(1.0f);

                    //Unknown1
                    newData.Add(0);
                    newData.Add(0);
                    newData.Add(2.0f);
                    newData.Add(0);

                    //Unknown2
                    newData.Add(0.5f);
                    newData.Add(0);
                    newData.Add(0);
                    newData.Add(0);

                    //Unknown3
                    newData.Add(0);
                    newData.Add(0);
                    newData.Add(0);
                    newData.Add(0);

                    //Unknown + subsurface material id
                    newData.Add(0);
                    newData.Add(mtrl.ColorSetData[pixel + 3]);
                    newData.Add(1.0f);  //  Subsurface Material Alpha
                    newData.Add(0);

                    pixel += 4;
                    //Subsurface scaling data.
                    newData.Add(mtrl.ColorSetData[pixel + 0]);
                    newData.Add(mtrl.ColorSetData[pixel + 1]);
                    newData.Add(mtrl.ColorSetData[pixel + 2]);
                    newData.Add(mtrl.ColorSetData[pixel + 3]);

                    // Add a blank row after, since only populating every other row.
                    newData.AddRange(GetDefaultColorsetRow());
                }

                mtrl.ColorSetData = newData;
                if (mtrl.ColorSetDyeData != null)
                {
                    // Update Dye information.
                    var newDyeData = new byte[128];
                    // Update old dye information
                    for (int i = 0; i < 16; i++)
                    {
                        var oldOffset = i * 2;
                        var newOffset = (i * 2) * 4;

                        var newDyeBlock = (uint)0;
                        var oldDyeBlock = BitConverter.ToUInt16(mtrl.ColorSetDyeData, oldOffset);

                        // Old dye bitmask was 5 bits long.
                        uint dyeBits = (uint)(oldDyeBlock & 0x1F);
                        uint oldTemplate = (uint)(oldDyeBlock >> 5);

                        newDyeBlock |= (oldTemplate << 16);
                        newDyeBlock |= dyeBits;

                        var newDyeBytes = BitConverter.GetBytes(newDyeBlock);

                        Array.Copy(newDyeBytes, 0, newDyeData, newOffset, newDyeBytes.Length);
                    }

                    mtrl.ColorSetDyeData = newDyeData;
                }
            }

            var normalTex = mtrl.Textures.FirstOrDefault(x => x.Usage == XivTexType.Normal);
            var idTex = mtrl.Textures.FirstOrDefault(x => x.Usage == XivTexType.Index);
            string idPath = null;
            string normalPath = null;

            // If we don't have an ID Texture, and we have a colorset + normal map, create one.
            if (normalTex != null && idTex == null && mtrl.ColorSetData != null && mtrl.ColorSetData.Count > 0)
            {
                idPath = normalTex.TexturePath.Replace(".tex", "_id.tex");
                normalPath = normalTex.TexturePath;

                var tex = new MtrlTexture();
                tex.TexturePath = idPath;
                tex.Sampler = new TextureSampler()
                {
                    SamplerSettingsRaw = 0x000F8340,
                    SamplerIdRaw = 1449103320,
                };
                mtrl.Textures.Add(tex);
            }

            await ImportMtrl(mtrl, null, source, false, tx);

            return (idPath, normalPath);
        }


        private async Task<(string indexFilePath, byte[] data)> CreateIndexFromNormal(string indexPath, string sourceNormalPath)
        {
            var _tex = new Tex(_gameDirectory);
            var _dat = new Dat(_gameDirectory);

            // Read normal file.
            var normalTex = await _tex.GetTexData(sourceNormalPath);
            var texData = await _tex.GetImageData(normalTex);

            // The DDS Importer will implode with tiny files.  Just assume micro size files are single flat color.
            var idPixels = new byte[texData.Length];
            var width = normalTex.Width;
            var height = normalTex.Height;
            
            if (height <= 32 || width <= 32)
            {
                height = 64;
                width = 64;
                var pix = texData[3];
                idPixels = new byte[64 * 64 * 4];
                for (int i = 0; i < idPixels.Length; i += 4)
                {
                    // We're going from RGBA to BGRA here.
                    idPixels[i] = 0;
                    idPixels[i + 1] = 255;
                    idPixels[i + 2] = pix;
                    idPixels[i + 3] = 255;
                }
            }
            else
            {
                for (int i = 0; i < idPixels.Length; i += 4)
                {
                    // We're going from RGBA to BGRA here,
                    // And trying to copy over data.
                    byte src = texData[i + 3];

                    idPixels[i] = 0;
                    idPixels[i + 1] = 255;
                    idPixels[i + 2] = src;
                    idPixels[i + 3] = 255;
                }
            }

            try
            {
                // This is very RAM heavy, given we're looping through 4 phases of alteration.
                // - Original Normal Map
                // - Altered Pixel Data
                // - DDS Format Pixel Data
                // - Uncompressed Tex Format Pixel Data
                // - Compressed Tex (Type4) Data

                var ddsBytes = await _tex.ConvertToDDS(idPixels, XivTexFormat.A8R8G8B8, true, height, width, true);
                ddsBytes = await _tex.DDSToUncompressedTex(ddsBytes);
                ddsBytes = await _tex.CompressTexFile(ddsBytes);
                return (indexPath, ddsBytes);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        private Half[] GetDefaultColorsetRow()
        {
            var row = new Half[32];

            // Diffuse pixel base
            row[0] = 1.0f;
            row[1] = 1.0f;
            row[2] = 1.0f;
            for(int i =0 ; i < 7; i++)
            {
                row[i] = 1.0f;
            }

            row[11] = 1.0f;
            row[14] = 2.0f;
            row[16] = 0.5f;
            row[25] = 0.0078125f;
            row[26] = 1.0f;


            row[7 * 4 + 0] = 16.0f;
            row[7 * 4 + 1] = 16.0f;
            return row;
        }

        /// <summary>
        /// Converts an XivMtrl object into the raw bytes of a Mtrl file.
        /// </summary>
        /// <param name="xivMtrl">The XivMtrl containing the mtrl data</param>
        /// <param name="item">The item</param>
        /// <returns>The new mtrl file byte data</returns>
        public byte[] CreateMtrlFile(XivMtrl xivMtrl)
        {
            var mtrlBytes = new List<byte>();

            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.Signature));

            var fileSizePointer = mtrlBytes.Count;
            mtrlBytes.AddRange(BitConverter.GetBytes((ushort)0)); // File Size - Backfilled later
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ColorSetDataSize));

            var materialDataSizePointer = mtrlBytes.Count;
            mtrlBytes.AddRange(BitConverter.GetBytes((ushort)0)); // String Block Size - Backfilled later

            var shaderNamePointerPointer = mtrlBytes.Count;
            mtrlBytes.AddRange(BitConverter.GetBytes((ushort)0)); // Shader Name Offset - Backfilled later

            mtrlBytes.Add((byte)xivMtrl.Textures.Count(x => !x.TexturePath.StartsWith(EmptySamplerPrefix)));
            mtrlBytes.Add((byte)xivMtrl.MapStrings.Count);
            mtrlBytes.Add((byte)xivMtrl.ColorsetStrings.Count);
            mtrlBytes.Add((byte)xivMtrl.AdditionalData.Length);


            // Build the string block and save the various offsets.
            var stringBlock = new List<byte>();

            var textureOffsets = new List<int>();
            var mapOffsets = new List<int>();
            var colorsetOffsets = new List<int>();

            foreach (var tex in xivMtrl.Textures)
            {
                // Ignore placeholder textures for empty samplers.
                if (tex.TexturePath.StartsWith(EmptySamplerPrefix)) continue;

                textureOffsets.Add(stringBlock.Count);
                var path = tex.TexturePath;
                // This is an old style DX9/DX11 Mixed Texture reference, make sure to clean it up if needed.
                if(tex.Flags != 0)
                {
                    // TODO: Is this still needed in Dawntrail?
                    path = path.Replace("--", string.Empty);
                }

                stringBlock.AddRange(Encoding.UTF8.GetBytes(path));
                stringBlock.Add(0);
            }

            foreach (var mapPathString in xivMtrl.MapStrings)
            {
                mapOffsets.Add(stringBlock.Count);
                stringBlock.AddRange(Encoding.UTF8.GetBytes(mapPathString.Value));
                stringBlock.Add(0);
            }

            foreach (var colorSetPathString in xivMtrl.ColorsetStrings)
            {
                colorsetOffsets.Add(stringBlock.Count);
                stringBlock.AddRange(Encoding.UTF8.GetBytes(colorSetPathString.Value));
                stringBlock.Add(0);
            }

            var shaderNamePointer = (ushort)stringBlock.Count;
            stringBlock.AddRange(Encoding.UTF8.GetBytes(xivMtrl.ShaderPackRaw));
            stringBlock.Add(0);

            Dat.Pad(stringBlock, 4);


            // Write the new offset list.
            for (var i = 0; i < xivMtrl.Textures.Count; i++)
            {
                // Ignore placeholder textures for empty samplers.
                if (xivMtrl.Textures[i].TexturePath.StartsWith(EmptySamplerPrefix)) continue;

                mtrlBytes.AddRange(BitConverter.GetBytes((short)textureOffsets[i]));
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.Textures[i].Flags));
            }

            for (var i = 0; i < mapOffsets.Count; i++)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes((short)mapOffsets[i]));
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.MapStrings[i].Flags));
            }

            for (var i = 0; i < colorsetOffsets.Count; i++)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes((short)colorsetOffsets[i]));
                mtrlBytes.AddRange(BitConverter.GetBytes((short)xivMtrl.ColorsetStrings[i].Flags));
            }

            // Add the actual string block.
            mtrlBytes.AddRange(stringBlock);

            // Set additional data flags as needed for colorset/dye information.
            if (xivMtrl.ColorSetDyeData != null && xivMtrl.ColorSetDyeData.Length > 0)
            {
                xivMtrl.AdditionalData[0] |= 0x08;
            }
            else
            {
                unchecked
                {
                    xivMtrl.AdditionalData[0] &= (byte)(~0x08);
                }
            }
            if (xivMtrl.ColorSetData != null && xivMtrl.ColorSetData.Count > 0)
            {
                xivMtrl.AdditionalData[0] |= 0x04;
            }
            else
            {
                unchecked
                {
                    xivMtrl.AdditionalData[0] &= (byte)(~0x04);
                }
            }


            mtrlBytes.AddRange(xivMtrl.AdditionalData);


            // Colorset and Dye info.
            foreach (var colorSetHalf in xivMtrl.ColorSetData)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes(colorSetHalf.RawValue));
            }

            if (xivMtrl.ColorSetDyeData != null && xivMtrl.ColorSetDyeData.Length > 0)
            {
                mtrlBytes.AddRange(xivMtrl.ColorSetDyeData);
            }


            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ShaderConstantsDataSize));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ShaderKeyCount));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.ShaderConstantsCount));
            mtrlBytes.AddRange(BitConverter.GetBytes((ushort) xivMtrl.Textures.Count(x => x.Sampler != null)));

            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.MaterialFlags));
            mtrlBytes.AddRange(BitConverter.GetBytes(xivMtrl.MaterialFlags2));

            foreach (var dataStruct1 in xivMtrl.ShaderKeys)
            {
                mtrlBytes.AddRange(BitConverter.GetBytes(dataStruct1.KeyId));
                mtrlBytes.AddRange(BitConverter.GetBytes(dataStruct1.Value));
            }

            var offset = 0;
            foreach (var parameter in xivMtrl.ShaderConstants)
            {
                // Ensure we're writing correctly calculated data.
                short byteSize = (short)(parameter.Values.Count * 4);

                mtrlBytes.AddRange(BitConverter.GetBytes((uint)parameter.ConstantId));
                mtrlBytes.AddRange(BitConverter.GetBytes((ushort)offset));
                mtrlBytes.AddRange(BitConverter.GetBytes((ushort)byteSize));
                offset += parameter.Values.Count * 4;
            }

            for(int i = 0; i < xivMtrl.Textures.Count; i++)
            {
                var tex = xivMtrl.Textures[i];
                if (tex.Sampler != null)
                {
                    if(tex.TexturePath.StartsWith(EmptySamplerPrefix))
                    {
                        mtrlBytes.AddRange(BitConverter.GetBytes(tex.Sampler.SamplerIdRaw));
                        mtrlBytes.AddRange(BitConverter.GetBytes(tex.Sampler.SamplerSettingsRaw));

                        // Empty samplers use 255 for their texture index.
                        mtrlBytes.Add((byte)255);
                        mtrlBytes.AddRange(new byte[3]);
                    } else
                    {
                        mtrlBytes.AddRange(BitConverter.GetBytes(tex.Sampler.SamplerIdRaw));
                        mtrlBytes.AddRange(BitConverter.GetBytes(tex.Sampler.SamplerSettingsRaw));
                        mtrlBytes.Add((byte)i);
                        mtrlBytes.AddRange(new byte[3]);
                    }
                }

            }

            var shaderBytes = new List<byte>();
            foreach (var shaderParam in xivMtrl.ShaderConstants)
            {
                foreach (var f in shaderParam.Values)
                {
                    shaderBytes.AddRange(BitConverter.GetBytes(f));
                }
            }

            // Pad out if we're missing anything.
            if (shaderBytes.Count < xivMtrl.ShaderConstantsDataSize)
            {
                shaderBytes.AddRange(new byte[xivMtrl.ShaderConstantsDataSize - shaderBytes.Count]);
            }
            mtrlBytes.AddRange(shaderBytes);



            // Backfill the header data.
            xivMtrl.FileSize = (short)mtrlBytes.Count;
            IOUtil.ReplaceBytesAt(mtrlBytes, BitConverter.GetBytes(xivMtrl.FileSize), fileSizePointer);
            IOUtil.ReplaceBytesAt(mtrlBytes, BitConverter.GetBytes((ushort)stringBlock.Count), materialDataSizePointer);
            IOUtil.ReplaceBytesAt(mtrlBytes, BitConverter.GetBytes(shaderNamePointer), shaderNamePointerPointer);
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
        /// Applies a given texture overlay onto the given texture type of the given material.
        /// the texture overlay stream must be a PNG or DDS file stream.
        /// 
        /// Returns the internal file path that was modified.
        /// </summary>
        /// <param name="mtrlPath">The path to the material to modify</param>
        /// <param name="textureType">The texture type within the material to replace.</param>
        /// <param name="index">Cached Index File</param>
        /// <param name="modList">Cached Modlist File</param>
        public async Task<string> ApplyOverlayToMaterial(string mtrlPath, XivTexType textureType, Stream overlayStream, string source, ModTransaction tx = null)
        {
            var mtrl = await GetMtrlData(mtrlPath, -1, 11);

            var tex = mtrl.Textures.FirstOrDefault(x => x.Usage == textureType);

            if (tex == null) return null;

            var texPath = tex.TexturePath;

            var _Tex = new Tex(_gameDirectory);

            var texData = await _Tex.GetTexData(tex);

            await _Tex.ApplyOverlay(texData, overlayStream, source, tx);

            return texPath;
        }

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
                return entry.MaterialSet;
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
        
        /// <summary>
        /// Hair is extremely annoying and uses literal hard-coded paths for things based on hair ID.
        /// </summary>
        /// <param name="root"></param>
        /// <returns></returns>
        public static XivDependencyRootInfo GetHairMaterialRoot(XivDependencyRootInfo root)
        {
            if(root.PrimaryType != XivItemType.human || root.SecondaryType != XivItemType.hair)
            {
                throw new InvalidDataException("Cannot get hair material info for non-hair root.");
            }

            if(root.SecondaryId < 101)
            {
                // Racial uniques.
                return root;
            } else if (root.SecondaryId < 116)
            {
                // 101-115 have Midlander M/F, and Miqo M/F
                if (root.PrimaryId == 701 || root.PrimaryId == 801)
                {
                    return root;
                }
                else
                {
                    var isFemale = ((root.PrimaryId / 100) % 2) == 0;
                    return new XivDependencyRootInfo()
                    {
                        PrimaryId = isFemale ? 201 : 101,
                        PrimaryType = root.PrimaryType,
                        SecondaryType = root.SecondaryType,
                        SecondaryId = root.SecondaryId,
                        Slot = root.Slot
                    };
                }

            } else if (root.SecondaryId < 201)
            {
                // These have just Midlander M/F
                var isFemale = ((root.PrimaryId / 100) % 2) == 0;
                return new XivDependencyRootInfo()
                {
                    PrimaryId = isFemale ? 201 : 101,
                    PrimaryType = root.PrimaryType,
                    SecondaryType = root.SecondaryType,
                    SecondaryId = root.SecondaryId,
                    Slot = root.Slot
                };

            } else
            {
                // Back to uniques.
                return root;
            }

        }


        public async Task UpdateShaderDB(bool useIndex2 = false)
        {
            const string _ShaderDbFilePath = "./Resources/DB/shader_info.db";
            const string _ShaderDbCreationScript = "CreateShaderDB.sql";

            var materials = await GetAllMtrlInfo(useIndex2);
            //var materials = new List<SimplifiedMtrlInfo>();

            try
            {
                // Spawn a DB connection to do the raw queries.
                // Using statements help ensure we don't accidentally leave any connections open and lock the file handle.
                var connectionString = "Data Source=" + _ShaderDbFilePath + ";Pooling=False;";
                if (File.Exists(_ShaderDbFilePath))
                {
                    File.Delete(_ShaderDbFilePath);
                }

                using (var db = new SQLiteConnection(connectionString))
                {
                    db.Open();

                    // Create the DB
                    var lines = File.ReadAllLines("Resources\\SQL\\" + _ShaderDbCreationScript);
                    var sqlCmd = String.Join("\n", lines);

                    using (var cmd = new SQLiteCommand(sqlCmd, db))
                    {
                        cmd.ExecuteScalar();
                    }

                    // Write the Data.
                    using (var transaction = db.BeginTransaction())
                    {
                        // Base Material Table.
                        var query = @"insert into materials values ($db_key, $data_file, $file_offset, $file_hash, $folder_hash, $full_hash, $file_path, $shader_pack)";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            foreach (var m in materials)
                            {
                                cmd.Parameters.AddWithValue("db_key", m.DbKey);
                                cmd.Parameters.AddWithValue("data_file", m.DataFile);
                                cmd.Parameters.AddWithValue("file_offset", (ulong) m.FileOffset);
                                cmd.Parameters.AddWithValue("file_hash", (ulong)m.FileHash);
                                cmd.Parameters.AddWithValue("folder_hash", (ulong)m.FolderHash);
                                cmd.Parameters.AddWithValue("full_hash", (ulong)m.FullHash);
                                cmd.Parameters.AddWithValue("file_path", m.FilePath);
                                cmd.Parameters.AddWithValue("shader_pack", m.ShaderPackRaw);
                                cmd.ExecuteScalar();
                            }
                        }

                        query = @"insert into shader_keys values ($db_key, $key_id, $value, $name)";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            foreach (var m in materials)
                            {
                                foreach(var sk in m.ShaderKeys)
                                {
                                    var info = sk.GetKeyInfo(m.ShaderPack);
                                    string name = null;
                                    if(info != null)
                                    {
                                        name = String.IsNullOrWhiteSpace(info.Value.Name) ? null : info.Value.Name;
                                    }
                                    cmd.Parameters.AddWithValue("db_key", m.DbKey);
                                    cmd.Parameters.AddWithValue("key_id", (ulong)sk.KeyId);
                                    cmd.Parameters.AddWithValue("value", (ulong)sk.Value);
                                    cmd.Parameters.AddWithValue("name", name);
                                    cmd.ExecuteScalar();
                                }
                            }
                        }

                        query = @"insert into shader_constants values ($db_key, $constant_id, $length, $value0, $value1, $value2, $value3, $name)";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            foreach (var m in materials)
                            {
                                foreach (var sc in m.ShaderConstants)
                                {
                                    var info = sc.GetConstantInfo(m.ShaderPack);
                                    string name = null;
                                    if (info != null)
                                    {
                                        name = String.IsNullOrWhiteSpace(info.Value.Name) ? null : info.Value.Name;
                                    }

                                    cmd.Parameters.AddWithValue("db_key", m.DbKey);
                                    cmd.Parameters.AddWithValue("constant_id", (ulong)sc.ConstantId);
                                    cmd.Parameters.AddWithValue("length", sc.Values.Count);
                                    cmd.Parameters.AddWithValue("value0", sc.Values.Count > 0 ? sc.Values[0] : null);
                                    cmd.Parameters.AddWithValue("value1", sc.Values.Count > 1 ? sc.Values[1] : null);
                                    cmd.Parameters.AddWithValue("value2", sc.Values.Count > 2 ? sc.Values[2] : null);
                                    cmd.Parameters.AddWithValue("value3", sc.Values.Count > 3 ? sc.Values[3] : null);
                                    cmd.Parameters.AddWithValue("name", name);
                                    cmd.ExecuteScalar();
                                }
                            }
                        }

                        query = @"insert into textures values ($db_key, $texture_path, $sampler_id, $sampler_settings, $name)";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            foreach (var m in materials)
                            {
                                foreach (var tex in m.Textures)
                                {
                                    cmd.Parameters.AddWithValue("db_key", m.DbKey);
                                    cmd.Parameters.AddWithValue("texture_path", tex.TexturePath);
                                    cmd.Parameters.AddWithValue("sampler_id", (ulong) tex.Sampler.SamplerIdRaw);
                                    cmd.Parameters.AddWithValue("sampler_settings",(ulong) tex.Sampler.SamplerSettingsRaw);
                                    cmd.Parameters.AddWithValue("name", tex.Sampler.SamplerId.ToString());
                                    cmd.ExecuteScalar();
                                }
                            }
                        }

                        transaction.Commit();
                    }
                }
            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// Retrieves simplified material info for ALL Materials in the entire game.
        /// Used to collect data to store into SQLite DB or JSON.
        /// </summary>
        /// <returns></returns>
        public async Task<List<SimplifiedMtrlInfo>> GetAllMtrlInfo(bool useIndex2 = false)
        {
            var materials = new List<SimplifiedMtrlInfo>();
            foreach (XivDataFile dat in Enum.GetValues(typeof(XivDataFile)))
            {
                Console.WriteLine("Scanning DAT: " + dat.ToString() + "...");
                var data = await GetAllMtrlInfo(dat, useIndex2);
                materials = materials.Concat(data).ToList();
            }
            Console.WriteLine(materials.Count + " Total Materials Identified...");
            return materials;
        }

        public async Task<List<SimplifiedMtrlInfo>> GetAllMtrlInfo(XivDataFile dataFile, bool useIndex2 = false)
        {
            const int _ThreadCount = 32;
            const uint _DatCount = 8;

            var OffsetToIndex1Dictionary = new Dictionary<uint, (uint FolderHash, uint FileHash)>();
            var OffsetToIndex2Dictionary = new Dictionary<uint, uint>();
            var gameDirectory = XivCache.GameInfo.GameDirectory;

            var _index = new Index(gameDirectory);
            var index = await _index.GetIndexFile(dataFile, false, true);


            // Populate dictionaries.
            Console.WriteLine("Creating offset dictionaries...");
            var index1Entries = index.GetAllEntriesIndex1();
            var index2Entries = index.GetAllEntriesIndex2();
            foreach (var entry in index1Entries)
            {
                if (!OffsetToIndex1Dictionary.ContainsKey(entry.RawOffset))
                {
                    OffsetToIndex1Dictionary.Add(entry.RawOffset, (entry.FolderPathHash, entry.FileNameHash));
                }
            }
            foreach (var entry in index2Entries)
            {
                if (!OffsetToIndex2Dictionary.ContainsKey(entry.RawOffset))
                {
                    OffsetToIndex2Dictionary.Add(entry.RawOffset, entry.FullPathHash);
                }
            }


            // Select whether to scan by index 1 or index 2 here.
            List<IndexEntry> data = new List<IndexEntry>(index1Entries);
            if(useIndex2)
            {
                data = new List<IndexEntry>(index2Entries);
            }

            var EntriesByDat = new List<List<IndexEntry>>();
            for (var i = 0; i < _DatCount; i++)
            {
                EntriesByDat.Add(new List<IndexEntry>());
            }

            // Group them by Dat.
            foreach (var item in data)
            {
                EntriesByDat[(int)item.DatNum].Add(item);
            }


            IEnumerable<SimplifiedMtrlInfo> materials = new List<SimplifiedMtrlInfo>();
            for (var i = 0; i < _DatCount; i++)
            {
                // Break the list into chunks for the threads.
                var z = 0;
                var parts = from item in EntriesByDat[i]
                            group item by z++ % _ThreadCount into part
                            select part.ToList();

                var datPath = Path.Combine(gameDirectory.FullName, $"{dataFile.GetDataFileName()}{Dat.DatExtension}{i}");
                if (!File.Exists(datPath))
                {
                    continue;
                }

                // Fuck the RAM, load the entire 2GB DAT file into RAM to make this not take 8 years.
                long length = new System.IO.FileInfo(datPath).Length;

                byte[] datData = null;
                if (length <= Int32.MaxValue && false)
                {
                    Console.WriteLine("Loading DAT" + i.ToString() + " into RAM...");
                    datData = File.ReadAllBytes(datPath);
                }
                else
                {
                    Console.WriteLine("Retaining DAT" + i.ToString() + " on disc due to file size being too large...");
                    // No-Op?
                }

                Console.WriteLine("Scanning " + EntriesByDat[i].Count + " index entries in Dat" + i.ToString() + "...");
                var TaskList = new List<Task<List<SimplifiedMtrlInfo>>>();
                foreach (var part in parts)
                {
                    // Spawn async tasks/threads to pull out the data.
                    TaskList.Add(Task.Run(async () =>
                    {
                        return await GetMaterials(dataFile, part, datData, OffsetToIndex1Dictionary, OffsetToIndex2Dictionary);
                    }));
                }

                await Task.WhenAll(TaskList);

                var count = 0;
                foreach (var task in TaskList)
                {
                    count += task.Result.Count;
                    materials = materials.Concat(task.Result);
                }

                Console.WriteLine("Materials Located in DAT" + i.ToString() + ": " + count.ToString());

                materials = materials.ToList();
            }


            Console.WriteLine("Total Materials Identified: " + materials.Count());
            return materials.ToList();
        }


        private async Task<List<SimplifiedMtrlInfo>> GetMaterials(XivDataFile dataFile, List<IndexEntry> files, byte[] datData, Dictionary<uint, (uint FolderHash, uint FileHash)> index1Dict, Dictionary<uint, uint> index2Dict)
        {
            var materials = new List<SimplifiedMtrlInfo>();
            var _Dat = new Dat(XivCache.GameInfo.GameDirectory);
            var count = 0;
            var total = files.Count;


            BinaryReader br;
            if (datData == null)
            {
                var datPath = Path.Combine(XivCache.GameInfo.GameDirectory.FullName, $"{dataFile.GetDataFileName()}{Dat.DatExtension}{files[0].DatNum}");
                var file = File.Open(datPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                br = new BinaryReader(file);
            }
            else
            {
                var ms = new MemoryStream(datData);
                br = new BinaryReader(ms);
            }

            //Console.WriteLine("Starting Chunk Scan: " + total.ToString() + " entries...");
            foreach (var file in files)
            {

                var fileTypeOffset = file.DataOffset + 4;
                br.BaseStream.Seek(fileTypeOffset, SeekOrigin.Begin);
                var type = br.ReadByte();
                if (type != 2)
                {
                    count++;
                    continue;
                }
                try
                {
                    var mtrlData = (await _Dat.DecompressType2Data(br, file.DataOffset)).ToArray();
                    if (mtrlData.Length < 4)
                    {
                        continue;
                    }

                    var signature = BitConverter.ToUInt32(mtrlData, 0);

                    if (signature != 16973824)
                    {
                        // Invalid Signature
                        continue;
                    }

                    var ff = file as FileIndex2Entry;

                    var material = GetMtrlData(mtrlData);
                    materials.Add(new SimplifiedMtrlInfo(dataFile, file, material, index1Dict, index2Dict));
                    count++;
                }
                catch (Exception ex)
                {
                    // Don't care if it fails.
                    count++;
                }
            }

            //Console.WriteLine("Chunk scan completed " + total.ToString() + " entries. (" + materials.Count + " materials)");
            return materials;
        }
        public struct SimplifiedMtrlInfo
        {
            public uint FullHash;
            public uint FileHash;
            public uint FolderHash;
            public uint FileOffset;
            public string FilePath;
            public XivDataFile DataFile;

            public string DbKey
            {
                get
                {
                    return DataFile.ToString() + "-" + FileOffset.ToString();
                }
            }

            public EShaderPack ShaderPack;
            public string ShaderPackRaw;
            public List<MtrlTexture> Textures;
            public List<ShaderKey> ShaderKeys;
            public List<ShaderConstant> ShaderConstants;

            public SimplifiedMtrlInfo(XivDataFile dataFile, IndexEntry index, XivMtrl material, Dictionary<uint, (uint FolderHash, uint FileHash)> index1Dict, Dictionary<uint, uint> index2Dict)
            {
                FileHash = index1Dict.ContainsKey(index.RawOffset) ? index1Dict[index.RawOffset].FileHash : 0;
                FolderHash = index1Dict.ContainsKey(index.RawOffset) ? index1Dict[index.RawOffset].FolderHash : 0;
                FullHash = index2Dict.ContainsKey(index.RawOffset) ? index2Dict[index.RawOffset] : 0;

                FilePath = "";

                ShaderPack = material.ShaderPack;
                ShaderPackRaw = material.ShaderPackRaw;

                FileOffset = index.RawOffset;
                DataFile = dataFile;

                Textures = material.Textures;
                ShaderKeys = material.ShaderKeys;
                ShaderConstants = material.ShaderConstants;
            }
        }

        public void Dipose()
        {
        }
    }
}