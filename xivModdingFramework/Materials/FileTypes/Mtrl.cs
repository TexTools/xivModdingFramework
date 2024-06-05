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

using HelixToolkit.SharpDX.Core;
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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Materials.DataContainers;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.Enums;
using xivModdingFramework.Textures.FileTypes;
using xivModdingFramework.Variants.DataContainers;
using xivModdingFramework.Variants.FileTypes;
using static xivModdingFramework.Materials.DataContainers.ShaderHelpers;
using Index = xivModdingFramework.SqPack.FileTypes.Index;

namespace xivModdingFramework.Materials.FileTypes
{
    /// <summary>
    /// This class contains the methods that deal with the .mtrl file type 
    /// </summary>
    public static class Mtrl
    {
        #region Consts and Constructor
        private const string MtrlExtension = ".mtrl";

        public const string EmptySamplerPrefix = "_EMPTY_SAMPLER_";
        private static Regex _dummyTextureRegex = new Regex("^bgcommon/texture/dummy_[a-z]\\.tex$");


        #endregion

        #region High-Level XivMtrl Accessors
        /// <summary>
        /// Gets the MTRL data for the given item.
        /// Uses the Item to resolve what material set is used, dynamically replacing the value in the mtrl file path as needed.
        /// The file path can be an absolute internal path or relative one.
        /// </summary>
        /// <param name="mtrlFile">The Mtrl file</param>
        /// <returns>XivMtrl containing all the mtrl data</returns>
        public static async Task<XivMtrl> GetXivMtrl(string mtrlFileOrPath, IItemModel item, bool forceOriginal = false, ModTransaction tx = null)
        {

            var materialSet = 0;
            try
            {
                if (Imc.UsesImc(item))
                {
                    var imcEntry = await Imc.GetImcInfo(item, false, tx);
                    materialSet = imcEntry.MaterialSet;
                }
            }
            catch
            {
                var root = XivDependencyGraph.ExtractRootInfo(mtrlFileOrPath);
                if (!root.IsValid())
                {
                    root = XivDependencyGraph.ExtractRootInfoFilenameOnly(mtrlFileOrPath);
                }
                if (root.SecondaryType == XivItemType.hair || root.SecondaryType == XivItemType.tail || root.SecondaryType == XivItemType.body)
                {
                    // These don't have IMC files, but still have material sets somehow, but are defaulted to 1.
                    materialSet = 1;
                }
            }

            return await GetXivMtrl(mtrlFileOrPath, materialSet, forceOriginal, tx);
        }

        /// <summary>
        /// Loads an XivMtrl from the given transaction store or game files, dynamically replacing the material set in the file path.
        /// Can 
        /// </summary>
        /// <param name="mtrlPath"></param>
        /// <param name="materialSet"></param>
        /// <param name="forceOriginal"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        public static async Task<XivMtrl> GetXivMtrl(string mtrlFileOrPath, int materialSet, bool forceOriginal = false, ModTransaction tx = null)
        {
            // Get the root from the material file in specific.
            var root = XivDependencyGraph.ExtractRootInfo(mtrlFileOrPath);
            if (!root.IsValid())
            {
                root = XivDependencyGraph.ExtractRootInfoFilenameOnly(mtrlFileOrPath);
            }
            var mtrlFile = Path.GetFileName(mtrlFileOrPath);

            // Reconstitute the full path with the new material set.
            var mtrlFolder = GetMtrlFolder(root, materialSet);
            var mtrlPath = $"{mtrlFolder}/{mtrlFile}";

            return await GetXivMtrl(mtrlPath, forceOriginal, tx);
        }

        /// <summary>
        /// Loads an  XivMtrl from the given transaction store or game files.
        /// </summary>
        /// <param name="mtrlPath"></param>
        /// <param name="materialSet"></param>
        /// <returns></returns>
        public static async Task<XivMtrl> GetXivMtrl(string mtrlPath, bool forceOriginal = false, ModTransaction tx = null) { 

            if (tx == null)
            {
                // Readonly tx if we don't have one.
                tx = ModTransaction.BeginTransaction();
            }

            if(!await tx.FileExists(mtrlPath, forceOriginal))
            {
                return null;
            }

            // Get uncompressed mtrl data
            var mtrlData = await tx.ReadFile(mtrlPath, forceOriginal);

            XivMtrl xivMtrl = GetXivMtrl(mtrlData, mtrlPath);

            return xivMtrl;
        }

        /// <summary>
        /// Converts an uncompressed .MTRL file into an XivMtrl.
        /// This should probably be made into a static function on XivMtrl.
        /// Path is only used to staple it onto the resulting XivMtrl's MTRLPath value.
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="internalMtrlPath">Path that is stapled onto the </param>
        /// <returns></returns>
        public static XivMtrl GetXivMtrl(byte[] bytes, string internalMtrlPath = "")
        {
            var xivMtrl = new XivMtrl();
            using (var br = new BinaryReader(new MemoryStream(bytes)))
            {
                xivMtrl = new XivMtrl
                {
                    MTRLPath = internalMtrlPath,
                    Signature = br.ReadInt32(),
                };
                var fileSize = br.ReadInt16();

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
                    var path = IOUtil.ReadNullTerminatedString(br);
                    xivMtrl.Textures[i].TexturePath = path;
                }

                for (var i = 0; i < xivMtrl.MapStrings.Count; i++)
                {
                    br.BaseStream.Seek(stringBlockStart + mapOffset[i], SeekOrigin.Begin);
                    var st = IOUtil.ReadNullTerminatedString(br);
                    xivMtrl.MapStrings[i].Value = st;
                }

                for (var i = 0; i < xivMtrl.ColorsetStrings.Count; i++)
                {
                    br.BaseStream.Seek(stringBlockStart + colorsetOffsets[i], SeekOrigin.Begin);
                    var st = IOUtil.ReadNullTerminatedString(br);
                    xivMtrl.ColorsetStrings[i].Value = st;
                }

                br.BaseStream.Seek(stringBlockStart + shaderNameOffset, SeekOrigin.Begin);
                xivMtrl.ShaderPackRaw = IOUtil.ReadNullTerminatedString(br);

                br.BaseStream.Seek(stringBlockStart + stringBlockSize, SeekOrigin.Begin);

                xivMtrl.AdditionalData = br.ReadBytes(additionalDataSize);

                xivMtrl.ColorSetData = new List<Half>();
                xivMtrl.ColorSetDyeData = new byte[0];

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

        #endregion

        #region One-Off Functions
        /// <summary>
        /// Retrieves the list of texture paths used by the given mtrl.
        /// </summary>
        /// <param name="mtrlPath"></param>
        /// <returns></returns>
        public static async Task<List<string>> GetTexturePathsFromMtrlPath(string mtrlPath, bool includeDummies = false, bool forceOriginal = false, ModTransaction tx = null)
        {
            var uniqueTextures = new HashSet<string>();
            var mtrl = await Mtrl.GetXivMtrl(mtrlPath, forceOriginal, tx);
            if(mtrl == null)
            {
                return new List<string>();
            }

            foreach(var tex in mtrl.Textures)
            {
                uniqueTextures.Add(tex.Dx11Path);

                if(tex.Dx9Path != null)
                {
                    uniqueTextures.Add(tex.Dx9Path);
                }
            }

            List<string> ret;
            if (includeDummies)
            {
                ret = uniqueTextures.ToList();
            } else {
                ret = uniqueTextures.Where(x => !_dummyTextureRegex.IsMatch(x)).ToList();
            }

            return ret;
        }
        #endregion

        #region Colorset Image Exporting
        /// <summary>
        /// Creates an XivTex from the given material's colorset data.
        /// </summary>
        /// <param name="xivMtrl">The XivMtrl with the ColorSet data</param>
        /// <returns>The XivTex of the ColorSet</returns>
        public static Task<XivTex> GetColorsetXivTex(XivMtrl xivMtrl)
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

                if(xivMtrl.ColorSetData.Count >= 1024)
                {
                    width = 8;
                    height = 32;
                }

                var df = XivDataFile._04_Chara;
                if (!String.IsNullOrEmpty(xivMtrl.MTRLPath))
                {
                    df = IOUtil.GetDataFileFromPath(xivMtrl.MTRLPath);
                }

                var xivTex = new XivTex
                {
                    Width = width,
                    Height = height,
                    MipMapCount = 0,
                    TexData = colorSetData.ToArray(),
                    TextureFormat = XivTexFormat.A16B16G16R16F,
                    FilePath = xivMtrl.MTRLPath,
                };

                return xivTex;
            });
        }

        /// <summary>
        /// Saves the Dye Data from the ColorSet
        /// </summary>
        /// <param name="item">The item containing the ColorSet</param>
        /// <param name="xivMtrl">The XivMtrl for the ColorSet</param>
        /// <param name="saveDirectory">The save directory</param>
        /// <param name="race">The selected race for the item</param>
        public static void SaveColorsetDyeData(IItem item, XivMtrl xivMtrl, DirectoryInfo saveDirectory, XivRace race)
        {
            var path = IOUtil.MakeItemSavePath(item, saveDirectory, race);
            var savePath = Path.Combine(path, Path.GetFileNameWithoutExtension(xivMtrl.MTRLPath) + ".dat");
            SaveColorsetDyeData(xivMtrl, savePath);
        }

        public static void SaveColorsetDyeData(XivMtrl xivMtrl, string path)
        {
            var toWrite = xivMtrl.ColorSetDyeData != null ? xivMtrl.ColorSetDyeData : new byte[0];
            var dir = Directory.GetParent(path);
            Directory.CreateDirectory(dir.FullName);
            File.WriteAllBytes(path, toWrite);

        }
        #endregion

        #region Material Import Pipeline

        private static int _LastColorsetOffset = 0;

        /// <summary>
        /// Converts an XivMtrl object into the raw bytes of an uncompressed MTRL file.
        /// Should probably be an extension/member method of xivMtrl.
        /// </summary>
        /// <param name="xivMtrl">The XivMtrl containing the mtrl data</param>
        /// <param name="item">The item</param>
        /// <returns>The new mtrl file byte data</returns>
        public static byte[] XivMtrlToUncompressedMtrl(XivMtrl xivMtrl)
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

            _LastColorsetOffset = mtrlBytes.Count;
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
            mtrlBytes.AddRange(BitConverter.GetBytes((ushort)xivMtrl.Textures.Count(x => x.Sampler != null)));

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

            for (int i = 0; i < xivMtrl.Textures.Count; i++)
            {
                var tex = xivMtrl.Textures[i];
                if (tex.Sampler != null)
                {
                    if (tex.TexturePath.StartsWith(EmptySamplerPrefix))
                    {
                        mtrlBytes.AddRange(BitConverter.GetBytes(tex.Sampler.SamplerIdRaw));
                        mtrlBytes.AddRange(BitConverter.GetBytes(tex.Sampler.SamplerSettingsRaw));

                        // Empty samplers use 255 for their texture index.
                        mtrlBytes.Add((byte)255);
                        mtrlBytes.AddRange(new byte[3]);
                    }
                    else
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
            var fileSize = (short)mtrlBytes.Count;
            IOUtil.ReplaceBytesAt(mtrlBytes, BitConverter.GetBytes(fileSize), fileSizePointer);
            IOUtil.ReplaceBytesAt(mtrlBytes, BitConverter.GetBytes((ushort)stringBlock.Count), materialDataSizePointer);
            IOUtil.ReplaceBytesAt(mtrlBytes, BitConverter.GetBytes(shaderNamePointer), shaderNamePointerPointer);
            return mtrlBytes.ToArray();
        }


        /// <summary>
        /// Imports a XivMtrl by converting it to bytes, then injecting it.
        /// </summary>
        /// <param name="xivMtrl">The XivMtrl containing the mtrl data</param>
        /// <param name="item">The item whos mtrl is being imported</param>
        /// <param name="source">The source/application that is writing to the dat.</param>
        /// <returns>The new offset</returns>
        public static async Task<long> ImportMtrl(XivMtrl xivMtrl, IItem item = null, string source = "Unknown", bool validateTextures = true, ModTransaction tx = null)
        {
            var boiler = TxBoiler.BeginWrite(ref tx);
            try
            {
                var mtrlBytes = XivMtrlToUncompressedMtrl(xivMtrl);

                if (validateTextures)
                {
                    // The MTRL file is now ready to go, but we need to validate the texture paths and create them if needed.
                    var dataFile = IOUtil.GetDataFileFromPath(xivMtrl.MTRLPath);

                    foreach (var tex in xivMtrl.Textures)
                    {
                        if (tex.Dx9Path != null)
                        {
                            // Remove DX9 Flag if we have one but not both textures set up correctly. (User input error most likely)
                            if (await tx.FileExists(tex.Dx9Path) && !await tx.FileExists(tex.Dx11Path))
                            {
                                unchecked
                                {
                                    tex.Flags &= (ushort)~0x8000;
                                    mtrlBytes = XivMtrlToUncompressedMtrl(xivMtrl);
                                }
                            }
                        }

                        var path = tex.TexturePath;

                        // Ignore empty samplers.
                        if (path.StartsWith(EmptySamplerPrefix)) continue;

                        var exists = await tx.FileExists(path);
                        if (exists)
                        {
                            continue;
                        }


                        var format = XivTexFormat.A8R8G8B8;

                        var xivTex = new XivTex();
                        xivTex.FilePath = path;
                        xivTex.TextureFormat = format;

                        var di = Tex.GetDefaultTexturePath(tex.Usage);

                        await Tex.ImportTex(path, di.FullName, item, source, tx);
                        if(tex.Dx9Path != null)
                        {
                            // Create a fresh DX11 texture as well if we're in split DX9/11 tex mode.
                            await Tex.ImportTex(tex.Dx11Path, di.FullName, item, source, tx);
                        }
                    }
                }

                long offset = await Dat.ImportType2Data(mtrlBytes.ToArray(), xivMtrl.MTRLPath, source, item, tx);

                await boiler.Commit();

                return offset;
            }
            catch(Exception ex)
            {
                await boiler.Catch();

                throw ex;
            }
        }


        public static async Task ImportMtrlToAllVersions(XivMtrl mtrl, IItemModel item = null, string source = "Unknown", ModTransaction tx = null)
        {
            var boiler = TxBoiler.BeginWrite(ref tx);
            try
            {
                var version = (int)mtrl.GetVersion();
                var root = await XivCache.GetFirstRoot(mtrl.MTRLPath);
                if (item == null && root != null)
                {
                    item = root.GetFirstItem(version);
                }

                if (item == null || !Imc.UsesImc(item) || version == 0)
                {
                    // No valid item, so no shared versions.
                    // So just save the base material and call it a day.
                    await ImportMtrl(mtrl, item, source, true, tx);
                    await boiler.Commit();
                    return;
                }

                // Add new Materials for shared model items.    
                var oldMaterialIdentifier = mtrl.GetMaterialIdentifier();
                var oldMtrlName = Path.GetFileName(mtrl.MTRLPath);

                // Ordering these by name ensures that we create textures for the new variants in the first
                // item alphabetically, just for consistency's sake.
                var sameModelItems = (await item.GetSharedModelItems()).OrderBy(x => x.Name, new ItemNameComparer());

                var oldVariantString = "/v" + mtrl.GetVersion().ToString().PadLeft(4, '0') + '/';
                var modifiedVariants = new List<int>();

                var mtrlReplacementRegex = "_" + oldMaterialIdentifier + ".mtrl";


                var imcEntries = new List<XivImc>();
                var materialVersions = new HashSet<byte>();
                var imcInfo = await Imc.GetFullImcInfo(item, false, tx);
                imcEntries = imcInfo.GetAllEntries(root.Info.Slot, true);
                materialVersions = new HashSet<byte>(imcEntries.Select(x => x.MaterialSet));

                var count = 0;

                var allItems = (await root.GetAllItems(-1, tx));

                var matNumToItems = new Dictionary<int, List<IItemModel>>();
                foreach (var i in allItems)
                {
                    if (imcEntries.Count <= i.ModelInfo.ImcSubsetID) continue;

                    var matSet = imcEntries[i.ModelInfo.ImcSubsetID].MaterialSet;
                    if (!matNumToItems.ContainsKey(matSet))
                    {
                        matNumToItems.Add(matSet, new List<IItemModel>());
                    }

                    var saveItem = i;

                    matNumToItems[matSet].Add(saveItem);
                }

                var keys = matNumToItems.Keys.ToList();
                foreach (var key in keys)
                {
                    var list = matNumToItems[key];
                    matNumToItems[key] = list.OrderBy(x => x.Name, new ItemNameComparer()).ToList();
                }

                var baseMtrl = (XivMtrl)mtrl.Clone();
                foreach (var tex in baseMtrl.Textures)
                {
                    // Tokenize the paths before we copy things over, so {variant} texture keys will resolve properly.
                    tex.TexturePath = baseMtrl.TokenizePath(tex.TexturePath, mtrl.ResolveFullUsage(tex));
                }

                // Load and modify all the MTRLs.
                foreach (var materialVersionId in materialVersions)
                {
                    var variantPath = Mtrl.GetMtrlFolder(root.Info, materialVersionId);
                    var oldMaterialPath = variantPath + "/" + oldMtrlName;

                    // Don't create materials for set 0.  (SE sets the material ID to 0 when that particular set-slot doesn't actually exist as an item)
                    if (materialVersionId == 0 && imcEntries.Count > 0) continue;

                    XivMtrl itemXivMtrl = (XivMtrl)baseMtrl.Clone();

                    // If we're an item that doesn't use IMC variants, make sure we don't accidentally move the material around.
                    if (materialVersionId != 0)
                    {
                        // Shift the MTRL to the new variant folder.
                        itemXivMtrl.MTRLPath = Regex.Replace(itemXivMtrl.MTRLPath, oldVariantString, "/v" + materialVersionId.ToString().PadLeft(4, '0') + "/");
                    }

                    IItem saveItem;

                    if (matNumToItems.ContainsKey(materialVersionId))
                    {
                        saveItem = matNumToItems[materialVersionId].First();
                    }
                    else
                    {
                        saveItem = (await XivCache.GetFirstRoot(itemXivMtrl.MTRLPath)).GetFirstItem();
                    }

                    count++;

                    foreach (var tex in itemXivMtrl.Textures)
                    {
                        tex.TexturePath = itemXivMtrl.DetokenizePath(tex.TexturePath, tex);
                    }

                    // Write the new Material
                    await Mtrl.ImportMtrl(itemXivMtrl, saveItem, source, true, tx);
                }
                await boiler.Commit();
            }
            catch
            {
                await boiler.Catch();
                throw;
            }
        }

        public static XivMtrl CreateDefaultMaterial(string path)
        {
#if ENDWALKER
            var defaultFilePath = "Resources/DefaultTextures/default_material.mtrl";
#else
            var defaultFilePath = "Resources/DefaultTextures/default_material_dt.mtrl";
#endif
            if (!File.Exists(defaultFilePath))
            {
                var dud = new XivMtrl();
                dud.MTRLPath = path;
                return dud;
            }

            var mtrl = GetXivMtrl(File.ReadAllBytes(defaultFilePath), path);

            var samp = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Normal);
            if(samp != null)
            {
                var texpath = path.Replace(".mtrl", "_n.tex");
                samp.TexturePath = texpath;
            }

            samp = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Mask);
            if (samp != null)
            {
                var texpath = path.Replace(".mtrl", "_m.tex");
                samp.TexturePath = texpath;
            }

            samp = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Index);
            if (samp != null)
            {
                var texpath = path.Replace(".mtrl", "_id.tex");
                samp.TexturePath = texpath;
            }

            samp = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Specular);
            if (samp != null)
            {
                var texpath = path.Replace(".mtrl", "_s.tex");
                samp.TexturePath = texpath;
            }

            samp = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Diffuse);
            if (samp != null)
            {
                var texpath = path.Replace(".mtrl", "_d.tex");
                samp.TexturePath = texpath;
            }

            samp = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Reflection);
            if (samp != null)
            {
                var texpath = path.Replace(".mtrl", "_r.tex");
                samp.TexturePath = texpath;
            }

            return mtrl;

        }

        internal static async Task CreateMissingMaterial(List<string> knownMaterials,  string materialPath, IItem referenceItem, string sourceApplication, ModTransaction tx)
        {
            if(knownMaterials.Count == 0)
            {
                // Uhhh...
                var fakeMtrl = CreateDefaultMaterial(materialPath);
                await Mtrl.ImportMtrl(fakeMtrl, referenceItem, sourceApplication, true, tx);
                return;
            }

            // Maybe one day we could use some fancy logic to guess a better base material, but for now this works.
            var mtrl = CreateDefaultMaterial(materialPath);
            await Mtrl.ImportMtrl(mtrl, referenceItem, sourceApplication, true, tx);
            return;

        }

        /// <summary>
        /// Does a fairly dirty file analysis of two material files to determine where their differences are contained.
        /// Use for merging colorsets into active materials in cases where only the colorset changed, and vice versa.
        /// </summary>
        /// <param name="baseMaterial"></param>
        /// <param name="otherMaterial"></param>
        /// <returns></returns>
        public static (bool ColorsetDifferences, bool OtherDifferences) CompareMaterials(XivMtrl baseMaterial,  XivMtrl otherMaterial)
        {
            // Jank method to get these offsets, but efficient and safe.
            var originalData = XivMtrlToUncompressedMtrl(baseMaterial);
            var originalColorsetOffset = _LastColorsetOffset;
            var newData = XivMtrlToUncompressedMtrl(otherMaterial);
            var newColorsetOffset = _LastColorsetOffset;

            var originalRemStart = (originalColorsetOffset + baseMaterial.ColorSetDataSize);
            var newRemStart = (newColorsetOffset + otherMaterial.ColorSetDataSize);
            var originalRemSize = originalData.Length - originalRemStart;
            var newRemSize = newData.Length - newRemStart;

            var colorsetDifferences = false;
            var otherDifferences = false;

            if(baseMaterial.ColorSetDataSize != otherMaterial.ColorSetDataSize)
            {
                colorsetDifferences = true;
            }

            if (!colorsetDifferences)
            {
                // Both have colorsets of same size.
                for(int i = 0; i < baseMaterial.ColorSetData.Count; i++)
                {
                    if (baseMaterial.ColorSetData[i] != otherMaterial.ColorSetData[i])
                    {
                        colorsetDifferences = true;
                        break;
                    }
                }

                if(!colorsetDifferences && baseMaterial.ColorSetDyeData != null)
                {
                    for (int i = 0; i < baseMaterial.ColorSetDyeData.Length; i++)
                    {
                        if (baseMaterial.ColorSetDyeData[i] != otherMaterial.ColorSetDyeData[i])
                        {
                            colorsetDifferences = true;
                            break;
                        }

                    }
                }
            }

            // Colorset differences are sorted.

            if(originalColorsetOffset != newColorsetOffset || originalRemSize != newRemSize)
            {
                otherDifferences = true;
            } else
            {
                // Check first half of file.
                for(int i = 0; i < originalColorsetOffset; i++)
                {
                    if (originalData[i] != newData[i])
                    {
                        otherDifferences = true;
                        break;
                    }
                }

                if (!otherDifferences)
                {
                    // Check back half of file.
                    for(int i = 0; i < originalRemSize; i++)
                    {
                        var originalOffset = i + originalRemStart;
                        var newOffset = i + newRemStart;

                        if (originalData[originalOffset] != newData[newOffset])
                        {
                            otherDifferences = true;
                            break;
                        }
                    }

                }
            }


            return (colorsetDifferences, otherDifferences);
        }

#endregion

        #region Endwalker => Dawntrail Material Conversion
        public static async Task UpdateEndwalkerMaterials(List<string> paths, string source, ModTransaction tx, IProgress<(int current, int total, string message)> progress)
        {
#if ENDWALKER
            return;
#endif
            var total = paths.Count;
            var materials = new List<XivMtrl>();
            var i = 0;
            foreach(var path in paths)
            {
                progress?.Report((i, total, "Scanning for Endwalker Materials..."));
                i++;
                if (!await tx.FileExists(path))
                {
                    continue;
                }

                var mtrl = await Mtrl.GetXivMtrl(path, false, tx);
                if (!DoesMtrlNeedDawntrailUpdate(mtrl))
                {
                    continue;
                }

                materials.Add(mtrl);
            }

            total = materials.Count;
            i = 0;
            foreach (var mtrl in materials)
            {
                progress?.Report((i, total, "Updating Endwalker Materials..."));
                await UpdateEndwalkerMaterial(mtrl, source, true, tx);
                i++;
            }
        }

        private const uint _OldShaderConstant1 = 0x36080AD0; // == 1
        private const uint _OldShaderConstant2 = 0x992869AB; // == 3 (skin) or 4 (hair)

        public static bool DoesMtrlNeedDawntrailUpdate(XivMtrl mtrl)
        {
            if(mtrl.ColorSetData != null && mtrl.ColorSetData.Count == 256)
            {
                // Any old colorset, regardless of shader needs to be updated.
                return true;
            }

            // OLD

            if (mtrl.ShaderPack == EShaderPack.Skin)
            {
                // NEW
                var sheenRate = 0x800EE35F;
                var SSAOMask = 0xB7FA33E2;


                if(mtrl.ShaderConstants.Any(x => x.ConstantId == _OldShaderConstant1)
                    && mtrl.ShaderConstants.Any(x => x.ConstantId == _OldShaderConstant2))
                {
                    return true;
                }
            }

            if(mtrl.ShaderPack == EShaderPack.Hair)
            {
                if (mtrl.ShaderConstants.Any(x => x.ConstantId == _OldShaderConstant1)
                    && mtrl.ShaderConstants.Any(x => x.ConstantId == _OldShaderConstant2))
                {
                    return true;
                }
            }

            return false;
        }

        public static async Task UpdateEndwalkerMaterial(XivMtrl mtrl, string source, bool createTextures, ModTransaction tx = null)
        {
            if (!createTextures)
            {
                // Should we really allow this?
                throw new NotImplementedException();
            }

            if (!DoesMtrlNeedDawntrailUpdate(mtrl)) {
                return;
            }

            var boiler = TxBoiler.BeginWrite(ref tx);
            try
            {
                await UpdateEndwalkerMaterial(mtrl, source, tx);
                await boiler.Commit();
            }
            catch
            {
                await boiler.Catch();
                throw;
            }
        }

        /// <summary>
        /// Updates an individual material, potentially as part of a larger block of tasks.
        /// </summary>
        /// <param name="mtrl"></param>
        /// <param name="states"></param>
        /// <param name="source"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        private static async Task UpdateEndwalkerMaterial(XivMtrl mtrl, string source, ModTransaction tx)
        {

            if (mtrl.ColorSetDataSize > 0)
            {
                var texInfo = await UpdateEndwalkerColorset(mtrl, source, tx);

                var data = await CreateIndexFromNormal(texInfo.indexTextureToCreate, texInfo.normalToCreateFrom, tx);
                await Dat.WriteModFile(data.data, data.indexFilePath, source, null, tx, false);
            } else if(mtrl.ShaderPack == EShaderPack.Skin)
            {
                await UpdateEndwalkerSkinMaterial(mtrl, source, tx);
            }
            else if(mtrl.ShaderPack == EShaderPack.Hair)
            {
                await UpdateEndwalkerHairMaterial(mtrl, source, tx);
            }
        }

        /// <summary>
        /// Updates a given Endwalker style Material to a Dawntrail style material, returning a tuple containing the Index Map that should be created after,
        /// and the normal map that should be used in the creation.
        /// </summary>
        /// <param name="mtrl"></param>
        /// <param name="updateShaders"></param>
        /// <param name="source"></param>
        /// <returns></returns>
        private static async Task<(string indexTextureToCreate, string normalToCreateFrom)> UpdateEndwalkerColorset(XivMtrl mtrl, string source, ModTransaction tx)
        {
            if(mtrl.ColorSetData.Count != 256)
            {
                // This is already upgraded.
                return (null, null);
            }

            if (mtrl.ShaderPack == EShaderPack.Character)
            {
                mtrl.ShaderPack = EShaderPack.CharacterLegacy;
            } else
            {
                // Don't need to change the shaderpack for anything else here.
            }

            if(mtrl.ColorSetData == null)
            {
                await ImportMtrl(mtrl, null, source, false, tx);
                return (null, null);
            }

            // Update Colorset
            List<Half> newData = new List<Half>(1024);
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
            }

            for(int i = 0; i < 16; i++)
            {
                // Add empty rows after.
                newData.AddRange(GetDefaultColorsetRow());
            }

            mtrl.ColorSetData = newData;
            if (mtrl.ColorSetDyeData != null && mtrl.ColorSetDyeData.Length > 0)
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


            var normalTex = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Normal);
            var idTex = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Index);
            string idPath = null;
            string normalPath = null;


            // If we don't have an ID Texture, and we have a colorset + normal map, create one.
            if (normalTex != null && idTex == null)
            {
                idPath = normalTex.Dx11Path.Replace(".tex", "_id.tex");
                normalPath = normalTex.Dx11Path;

                var tex = new MtrlTexture();
                tex.TexturePath = idPath;
                tex.Sampler = new TextureSampler()
                {
                    SamplerSettingsRaw = 0x000F8340,
                    SamplerIdRaw = 1449103320,
                };
                mtrl.Textures.Add(tex);
            }

            var specTex = mtrl.Textures.FirstOrDefault(x => x.Sampler.SamplerId == ESamplerId.g_SamplerSpecular);
            if(specTex != null)
            {
                specTex.Sampler.SamplerId = ESamplerId.g_SamplerMask;
            }

            await ImportMtrl(mtrl, null, source, false, tx);

            return (idPath, normalPath);
        }


        /// <summary>
        /// Creates the actual index file data from the constituent parts.
        /// Returns the bytes of an uncompressed Tex file.
        /// </summary>
        /// <param name="indexPath"></param>
        /// <param name="sourceNormalPath"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        private static async Task<(string indexFilePath, byte[] data)> CreateIndexFromNormal(string indexPath, string sourceNormalPath, ModTransaction tx = null)
        {

            // Read normal file.
            var normalTex = await Tex.GetXivTex(sourceNormalPath, false, tx);
            var normalData = await normalTex.GetRawPixels();

            var indexData = new byte[normalTex.Width * normalTex.Height * 4];
            await TextureHelpers.CreateIndexTexture(normalData, indexData, normalTex.Width, normalTex.Height);

            // Create MipMaps (And DDS header that we don't really need)
            indexData = await Tex.ConvertToDDS(indexData, XivTexFormat.A8R8G8B8, true, normalTex.Height, normalTex.Width, true);

            // Convert DDS to uncompressed Tex
            indexData = Tex.DDSToUncompressedTex(indexData);

            return (indexPath, indexData);
        }

        private static async Task UpdateEndwalkerSkinMaterial(XivMtrl mtrl, string source, ModTransaction tx)
        {
            // Disabled for now.  SkinLegacy causes the Benchmark to crash in Benchmark 1.1
            return;


            // ShaderPack update is all we have for this one for now.
            mtrl.ShaderPack = EShaderPack.SkinLegacy;
            var mtrlData = Mtrl.XivMtrlToUncompressedMtrl(mtrl);
            await Dat.WriteModFile(mtrlData, mtrl.MTRLPath, source, null, tx, false);

        }
        private static async Task UpdateEndwalkerHairMaterial(XivMtrl mtrl, string source, ModTransaction tx)
        {
            var normalTexSampler = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Normal);
            var maskTexSampler = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Mask);

            if(normalTexSampler == null || maskTexSampler == null)
            {
                // Not Resolveable.
                return;
            }

            // Arbitrary base game hair file to use to replace our shader constants.
            var constantBase = await GetXivMtrl("chara/human/c0801/obj/hair/h0115/material/v0001/mt_c0801h0115_hir_a.mtrl", true, tx);
            mtrl.ShaderConstants = constantBase.ShaderConstants;
            var mtrlData = Mtrl.XivMtrlToUncompressedMtrl(mtrl);

            await UpdateEndwalkerHairTextures(normalTexSampler.Dx11Path, maskTexSampler.Dx11Path, source, tx);

            await Dat.WriteModFile(mtrlData, mtrl.MTRLPath, source, null, tx, false);
        }

        private static async Task UpdateEndwalkerHairTextures(string normalPath, string maskPath, string source, ModTransaction tx)
        {
            // Read normal file.
            var normalTex = await Tex.GetXivTex(normalPath, false, tx);
            var maskTex = await Tex.GetXivTex(maskPath, false, tx);

            // Resize to be same size.
            var data = await TextureHelpers.ResizeImages(normalTex, maskTex);

            // Create the final hair pixel data.
            await TextureHelpers.CreateHairMaps(data.TexA, data.TexB, data.Width, data.Height);

            // Create MipMaps (And DDS header that we don't really need)
            var normalData = await Tex.ConvertToDDS(data.TexA, XivTexFormat.A8R8G8B8, true, data.Height, data.Width, true);
            var maskData = await Tex.ConvertToDDS(data.TexB, XivTexFormat.A8R8G8B8, true, data.Height, data.Width, true);

            // Convert DDS to uncompressed Tex
            normalData = Tex.DDSToUncompressedTex(normalData);
            maskData = Tex.DDSToUncompressedTex(maskData);

            // Write final files.
            await Dat.WriteModFile(normalData, normalPath, source, null, tx, false);
            await Dat.WriteModFile(maskData, maskPath, source, null, tx, false);
        }

        private static Half[] GetDefaultColorsetRow()
        {
            var row = new Half[32];

            // Diffuse pixel base
            for(int i =0 ; i < 8; i++)
            {
                row[i] = 1.0f;
            }

            row[11] = 1.0f;
            row[12] = 0.09997559f;
            row[13] = 0.1999512f;
            row[14] = 5.0f;


            row[16] = 0.5f;
            row[25] = 0.0078125f;
            row[26] = 1.0f;


            row[7 * 4 + 0] = 16.0f;
            row[7 * 4 + 3] = 16.0f;
            return row;
        }

        // Resolves for old-style default hair textures.
        private static Regex OldHairTextureRegex = new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/hair\\/h[0-9]{4}\\/texture\\/(?:--)?c([0-9]{4})h([0-9]{4})_hir_([ns])\\.tex");
        private static Regex OldHairMaterialRegex = new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/hair\\/h[0-9]{4}\\/material\\/v0001\\/mt_c([0-9]{4})h([0-9]{4})_hir_a\\.mtrl");
        private static string OldHairMaterialFormat = "chara/human/c{0}/obj/hair/h{1}/material/v0001/mt_c{0}h{1}_hir_a.mtrl";
        private static string NewHairTextureFormat = "chara/human/c{0}/obj/hair/h{1}/texture/c{0}h{1}_hir_{2}.tex";

        /// <summary>
        /// This function does some jank analysis of inbound hair texture files,
        /// automatically copying them to SE's new pathing /if/ they were included by themselves, without their 
        /// associated default material.
        /// </summary>
        /// <param name="files"></param>
        /// <param name="source"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        internal static async Task CheckImportForOldHairJank(List<string> files, string source, ModTransaction tx)
        {

            var results = new Dictionary<int, Dictionary<int, List<(string Path, XivTexType TexType)>>>();

            var materials = new List<(int Race, int Hair)>();
            foreach(var file in files)
            {
                var matMatch = OldHairMaterialRegex.Match(file);
                if (matMatch.Success)
                {
                    var rid = Int32.Parse(matMatch.Groups[1].Value);
                    var hid = Int32.Parse(matMatch.Groups[2].Value);
                    materials.Add((rid, hid));
                    continue;
                }

                var match = OldHairTextureRegex.Match(file);
                if (!match.Success) continue;

                var raceId = Int32.Parse(match.Groups[1].Value);
                var hairId = Int32.Parse(match.Groups[2].Value);
                var tex = match.Groups[3].Value;
                if (!results.ContainsKey(raceId))
                {
                    results.Add(raceId, new Dictionary<int, List<(string Path, XivTexType TexType)>>());
                }

                if (!results[raceId].ContainsKey(hairId))
                {
                    results[raceId].Add(hairId, new List<(string Path, XivTexType TexType)>());
                }

                var tt = tex == "n" ? XivTexType.Normal : XivTexType.Specular;

                if (results[raceId][hairId].Any(x => x.TexType == tt))
                {
                    var prev = results[raceId][hairId].First(x => x.TexType == tt);
                    if (prev.Path.Contains("--"))
                    {
                        // Dx11 wins out.
                        continue;
                    } else
                    {
                        results[raceId][hairId].RemoveAll(x => x.TexType == tt);
                    }
                }
                results[raceId][hairId].Add((file, tt));
            }

            // Winnow list to only entries with both types, that also lack material files.
            var races = results.Keys.ToList();
            foreach(var r in races)
            {
                var hairs = results[r].Keys.ToList();
                foreach(var h in hairs)
                {
                    if (results[r][h].Count < 2)
                    {
                        results[r].Remove(h);
                    } else if(materials.Any(x => x.Hair == h && x.Race == r))
                    {
                        results[r].Remove(h);
                    }
                }

                if (results[r].Count == 0)
                {
                    results.Remove(r);
                }
            }

            if (results.Count == 0) return;

            foreach(var rKv in results)
            {
                var race = rKv.Key.ToString("D4");
                foreach(var hKv in rKv.Value)
                {
                    var hair = hKv.Key.ToString("D4");
                    var material = string.Format(OldHairMaterialFormat, race, hair);
                    if(!await tx.FileExists(material))
                    {
                        // Weird, but nothing to be done.
                        continue;
                    }
                    var root = await XivCache.GetFirstRoot(material);
                    IItem item = null;
                    if (root != null)
                    {
                        item = root.GetFirstItem();
                    }

                    foreach (var tex in hKv.Value)
                    {
                        var suffix = tex.TexType == XivTexType.Normal ? "norm" : "mask";
                        var newPath = string.Format(NewHairTextureFormat, race, hair, suffix);

                        if (files.Contains(newPath))
                        {
                            continue;
                        }

                        await Dat.CopyFile(tex.Path, newPath, source, true, item, tx);
                        files.Add(newPath);
                    }

                    var newNorm = string.Format(NewHairTextureFormat, race, hair, "norm");
                    var newMask = string.Format(NewHairTextureFormat, race, hair, "mask");
                    await UpdateEndwalkerHairTextures(newNorm, newMask, source, tx);
                }
            }
        }

        #endregion




        #region Dynamic Material Path Resolution

        // Helper regexes for GetMtrlPath.
        private static readonly Regex _raceRegex = new Regex("(c[0-9]{4})");
        private static readonly Regex _weaponMatch = new Regex("(w[0-9]{4})");
        private static readonly Regex _tailMatch = new Regex("(t[0-9]{4})");
        private static readonly Regex _raceMatch = new Regex("(c[0-9]{4})");
        private static readonly Regex _bodyRegex = new Regex("(b[0-9]{4})");
        private static readonly Regex _skinRegex = new Regex("^/mt_c([0-9]{4})b([0-9]{4})_.+\\.mtrl$");

        /// <summary>
        /// Calculates a name for a root's material based on the associated manually supplied data.
        /// For use in a few menus/etc. that don't actually have access to the original full material names, for whatever reason.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="race"></param>
        /// <param name="slot"></param>
        /// <param name="suffix"></param>
        /// <returns></returns>
        public static string GetMtrlNameByRootRaceSlotSuffix(XivDependencyRootInfo root, XivRace race = XivRace.All_Races, string slot = null, string suffix = null)
        {
            if (root.SecondaryType == null)
            {
                root.SecondaryType = root.PrimaryType;
                root.SecondaryId = root.PrimaryId;
                root.PrimaryId = race.GetRaceCodeInt();
                root.PrimaryType = XivItemType.human;
            }

            if (slot == null && root.Slot != null)
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
        /// Resolves the MTRL path for a given MDL path.
        /// Only needed because of the rare exception case of skin materials.
        /// </summary>
        /// <param name="mdlPath"></param>
        /// <param name="materialSet">Which material variant folder.  Defaulted to 1.</param>
        /// <returns></returns>
        public static string GetMtrlPath(string mdlPath, string mtrlName, int materialSet = 1)
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
                mtrlFolder = baseFolder + "/material/v" + materialSet.ToString().PadLeft(4, '0');
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
        public static string GetMtrlFolder(IItemModel itemModel, int materialSet = 0)
        {
            var root = itemModel.GetRootInfo();
            return GetMtrlFolder(root, materialSet);
        }

        public static async Task<int> GetMaterialSetId(IItem item, bool forceOriginal = false, ModTransaction tx = null)
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
                var im = item as IItemModel;
                if (im != null && im.ModelInfo != null && Imc.UsesImc(im))
                {
                    if (!Imc.UsesImc(im)){
                        return 0;
                    }
                    var entry = await Imc.GetImcInfo((IItemModel)item, false, tx);
                    if(entry== null)
                    {
                        return 0;
                    }

                    return entry.MaterialSet;
                } else
                {
                    return 0;
                }
            } catch
            {
                return 0;
            }

        }
        public static string GetMtrlFolder(XivDependencyRootInfo root, int materialSet = -1) 
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

        #endregion

        #region Shader DB Compilation
        public static async Task UpdateShaderDB(bool useIndex2 = false)
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
        /// 
        /// Not Transaction Safe
        /// </summary>
        /// <returns></returns>
        public static async Task<List<SimplifiedMtrlInfo>> GetAllMtrlInfo(bool useIndex2 = false)
        {
            if(ModTransaction.ActiveTransaction != null)
            {
                throw new Exception("Cannot perform material/shader scan with an open write-enabled transaction.");
            }

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

        /// <summary>
        /// Scans the entire collection of DATs for a given data file for material files, pulling their info for the shader scan.
        /// Not transaction safe - Should only be used in the Shader Scan pipeline.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="useIndex2"></param>
        /// <returns></returns>
        public static async Task<List<SimplifiedMtrlInfo>> GetAllMtrlInfo(XivDataFile dataFile, bool useIndex2 = false)
        {
            if (ModTransaction.ActiveTransaction != null)
            {
                throw new Exception("Cannot perform material/shader scan with an open write-enabled transaction.");
            }
            const int _ThreadCount = 32;
            const uint _DatCount = 8;

            var OffsetToIndex1Dictionary = new Dictionary<uint, (uint FolderHash, uint FileHash)>();
            var OffsetToIndex2Dictionary = new Dictionary<uint, uint>();
            var gameDirectory = XivCache.GameInfo.GameDirectory;

            var tx = ModTransaction.BeginTransaction();

            var index = await tx.GetIndexFile(dataFile);


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

                var datPath = Dat.GetDatPath(dataFile, i);
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


        /// <summary>
        /// Scans the given index entry file list to see which ones are materials and return their data where applicable.
        /// Not Transaction safe - Should only be used in the Shader Scan pipeline.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="files"></param>
        /// <param name="datData"></param>
        /// <param name="index1Dict"></param>
        /// <param name="index2Dict"></param>
        /// <returns></returns>
        private static async Task<List<SimplifiedMtrlInfo>> GetMaterials(XivDataFile dataFile, List<IndexEntry> files, byte[] datData, Dictionary<uint, (uint FolderHash, uint FileHash)> index1Dict, Dictionary<uint, uint> index2Dict)
        {
            if (ModTransaction.ActiveTransaction != null)
            {
                throw new Exception("Cannot perform material/shader scan with an open write-enabled transaction.");
            }

            var materials = new List<SimplifiedMtrlInfo>();
            var count = 0;
            var total = files.Count;


            BinaryReader br;
            if (datData == null)
            {
                var datPath = Dat.GetDatPath(dataFile, (int) files[0].DatNum);
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
                    var mtrlData = (await Dat.ReadSqPackType2(br, file.DataOffset)).ToArray();
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

                    var material = GetXivMtrl(mtrlData);
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
        #endregion
    }
}