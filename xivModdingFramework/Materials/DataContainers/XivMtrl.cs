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
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Materials.FileTypes;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.Enums;
using xivModdingFramework.Textures.FileTypes;

namespace xivModdingFramework.Materials.DataContainers
{
    /// <summary>
    /// This class holds the information for an MTRL file
    /// </summary>
    public class XivMtrl
    {
        public const string ItemPathToken = "{item_folder}";
        public const string VariantToken = "{variant}";
        public const string TextureNameToken = "{default_name}";
        public const string CommonPathToken = "{shared_folder}";

        /// <summary>
        /// The MTRL file signature
        /// </summary>
        /// <remarks>
        /// 0x00000301 (16973824)
        /// </remarks>
        public int Signature { get; set; }

        /// <summary>
        /// The size of the MTRL file
        /// </summary>
        public short FileSize { get; set; }

        /// <summary>
        /// The size of the ColorSet Data section
        /// </summary>
        /// <remarks>
        /// Can be 0 if there is no ColorSet Data
        /// </remarks>
        public ushort ColorSetDataSize { get {
            var size = ColorSetData.Count * 2;
            size += ColorSetDyeData == null ? 0 : ColorSetDyeData.Length;
            return (ushort) size;
        } }

        public List<MtrlString> MapStrings { get; set; }

        public List<MtrlString> ColorsetStrings { get; set; }

        /// <summary>
        /// The name of the shader used by the item
        /// </summary>
        public string Shader { get; set; }

        /// <summary>
        /// Unknown value
        /// </summary>
        public byte[] AdditionalData { get; set; }

        /// <summary>
        /// The list of half floats containing the ColorSet data
        /// </summary>
        public List<Half> ColorSetData { get; set; }

        /// <summary>
        /// The byte array containing the extra ColorSet data
        /// </summary>
        public byte[] ColorSetDyeData { get; set; }

        /// <summary>
        /// The size of the additional MTRL Data
        /// </summary>
        public ushort ShaderConstantsDataSize { 
            get {
                var size = 0;
                ShaderConstants.ForEach(x => {
                    size += x.Values.Count * 4;
                }
                );

                return (ushort)size;
            }
            set
            {
                //No-Op
                throw new Exception("Attempted to directly set AdditionalDataSize");
            } 
        }

        /// <summary>
        /// The number of type 1 data sturctures 
        /// </summary>
        public ushort ShaderKeyCount { get { return (ushort)ShaderKeys.Count; } set
            {
                //No-Op
                throw new Exception("Attempted to directly set TextureUsageCount");
            }
        }

        /// <summary>
        /// The number of type 2 data structures
        /// </summary>
        public ushort ShaderConstantsCount
        {
            get { return (ushort)ShaderConstants.Count; }
            set
            {
                //No-Op
                throw new Exception("Attempted to directly set ShaderParameterCount");
            }
        }

        public List<MtrlTexture> Textures;

        /// <summary>
        /// Shader flags, only partially known:
        /// 
        /// 0x0001 Show Backfaces
        /// 0x0002 
        /// 0x0004 
        /// 0x0008 
        /// 0x0010 Enable Transparency 
        /// 0x0020 
        /// 0x0040
        /// 0x0080
        /// 0x0100
        /// 0x0200
        /// 0x0400
        /// 0x0800
        /// 0x1000
        /// 0x2000
        /// 0x4000
        /// 0x8000
        /// </remarks>
        public ushort ShaderFlags { get; set; }

        /// <summary>
        /// Unknown Value
        /// </summary>
        public ushort ShaderUnknown { get; set; }

        /// <summary>
        /// The list of Type 1 data structures
        /// </summary>
        public List<ShaderKey> ShaderKeys { get; set; }

        /// <summary>
        /// The list of Type 2 data structures
        /// </summary>
        public List<ShaderConstant> ShaderConstants { get; set; }

        /// <summary>
        /// The TexTools expected list of textures in the item, including colorset 'texture'.
        /// </summary>
        public List<TexTypePath> TextureTypePathList
        {
            get
            {
                return GetTextureTypePathList();
            }
        }

        /// <summary>
        /// Whether or not this Material's Variant has VFX associated with it.
        /// Used to indicate if TexTools should retrieve the VFX textures
        /// for the Texture Listing.
        /// </summary>
        public bool hasVfx { get; set; }

        /// <summary>
        /// The internal MTRL path
        /// </summary>
        public string MTRLPath { get; set; }

        /// <summary>
        /// Retreives the Shader information for this Mtrl file.
        /// </summary>
        /// <returns></returns>
        public ShaderInfo GetShaderInfo()
        {
            var info = new ShaderInfo();
            switch (Shader)
            {
                case "character.shpk":
                    info.Shader = MtrlShader.Standard;
                    break;
                case "characterglass.shpk":
                    info.Shader = MtrlShader.Glass;
                    break;
                case "skin.shpk":
                    info.Shader = MtrlShader.Skin;
                    break;
                case "hair.shpk":
                    info.Shader = MtrlShader.Hair;
                    break;
                case "iris.shpk":
                    info.Shader = MtrlShader.Iris;
                    break;
                case "bg.shpk":
                    info.Shader = MtrlShader.Furniture;
                    break;
                case "bgcolorchange.shpk":
                    info.Shader = MtrlShader.DyeableFurniture;
                    break;
                default:
                    info.Shader = MtrlShader.Other;
                    break;
            }

            // Check the transparency bit.
            const ushort transparencyBit = 16;
            const ushort backfaceBit = 1;
            var bit = (ushort)(ShaderFlags & transparencyBit);

            if(bit > 0)
            {
                info.TransparencyEnabled = true;
            } else
            {
                info.TransparencyEnabled = false;
            }

            bit = (ushort)(ShaderFlags & backfaceBit);

            if (bit > 0)
            {
                info.RenderBackfaces = false;
            }
            else
            {
                info.RenderBackfaces = true;
            }

            info.Preset = MtrlShaderPreset.Default;
            if (info.Shader == MtrlShader.Standard)
            {
                bool hasSpec = GetMapInfo(XivTexType.Specular) != null;
                bool hasDiffuse = GetMapInfo(XivTexType.Diffuse) != null;
                bool hasMulti = GetMapInfo(XivTexType.Multi) != null;

                if (hasDiffuse)
                {
                    if(hasSpec)
                    {
                        if (GetShaderKey(XivTexType.Specular) != null)
                        {
                            info.Preset = MtrlShaderPreset.Monster;
                        }
                        else
                        {
                            info.Preset = MtrlShaderPreset.DiffuseSpecular;
                        }
                    } else
                    {
                        info.Preset = MtrlShaderPreset.DiffuseMulti;
                    }
                } else
                {
                    info.Preset = MtrlShaderPreset.Default;
                }
            }
            else if (info.Shader == MtrlShader.Skin)
            {
                if(GetShaderKey(XivTexType.Skin) == null)
                {
                    info.Preset = MtrlShaderPreset.Face;
                }

                if(GetShaderKey(XivTexType.Skin) != null && GetShaderKey(XivTexType.Skin).Value == 1476344676)
                {
                    info.Preset = MtrlShaderPreset.BodyWithHair;
                }
                else if(ShaderConstants.Any(x => x.ConstantId == MtrlShaderConstantId.SkinTileMaterial))
                {
                    var param = ShaderConstants.First(x => x.ConstantId == MtrlShaderConstantId.SkinTileMaterial);
                    if(param.Values[0] == 0)
                    {
                        if(info.Preset == MtrlShaderPreset.Face)
                        {
                            info.Preset = MtrlShaderPreset.FaceNoPores;

                        } else
                        {
                            info.Preset = MtrlShaderPreset.BodyNoPores;
                        }
                    }
                }
            } else if(info.Shader == MtrlShader.Hair)
            {
                if(GetShaderKey(XivTexType.Other) != null)
                {
                    info.Preset = MtrlShaderPreset.Face;
                    var mul = 1.4f;
                    var param = ShaderConstants.FirstOrDefault(x => x.ConstantId == MtrlShaderConstantId.SkinColor);
                    if(param != null)
                    {
                        mul = param.Values[0];
                    }

                    // Limbal ring faces (Au Ra) use a 3.0 multiplier for brightness.
                    if(mul == 3.0)
                    {
                        info.Preset = MtrlShaderPreset.FaceBright;
                    }


                }
            }


            return info;
        }

        /// <summary>
        /// Sets the shader info for this Mtrl file.
        /// </summary>
        /// <param name="info"></param>
        public void SetShaderInfo(ShaderInfo info, bool forced = false)
        {
            var old = GetShaderInfo();

            // Set transparency bit as needed.
            const ushort transparencyBit = 16;
            const ushort backfaceBit = 1;
            var transparency = info.ForcedTransparency == null ? info.TransparencyEnabled : (bool)info.ForcedTransparency;
            if (transparency)
            {
                ShaderFlags = (ushort)(ShaderFlags | transparencyBit);
            }
            else
            {
                ShaderFlags = (ushort)(ShaderFlags & (~transparencyBit));
            }

            // Set Backfaces bit.
            var backfaces = info.RenderBackfaces;
            if (!backfaces)
            {
                ShaderFlags = (ushort)(ShaderFlags | backfaceBit);
            }
            else
            {
                ShaderFlags = (ushort)(ShaderFlags & (~backfaceBit));
            }

            // Update us to DX11 material style if we're not already.
            for (var idx = 0; idx < AdditionalData.Length; idx++)
            {
                if (idx == 0)
                {
                    AdditionalData[idx] = 12;
                }
                else
                {
                    AdditionalData[idx] = 0;
                }
            }

            for (var idx = 0; idx < Textures.Count; idx++)
            {
                Textures[idx].Flags = 0;
            }


            if (forced == false && info.Shader == old.Shader && info.Preset == old.Preset)
            {
                // Nothing needs to be changed.
                // Returning here ensures shader information
                // is not bashed unless edited.
                return;
            }

            RegenerateShaderKeys(info);
            RegenerateShaderConstants(info);

            switch (info.Shader)
            {
                case MtrlShader.Standard:
                    Shader = "character.shpk";
                    break;
                case MtrlShader.Glass:
                    Shader = "characterglass.shpk";
                    break;
                case MtrlShader.Hair:
                    Shader = "hair.shpk";
                    break;
                case MtrlShader.Iris:
                    Shader = "iris.shpk";
                    break;
                case MtrlShader.Skin:
                    Shader = "skin.shpk";
                    break;
                case MtrlShader.Furniture:
                    Shader = "bg.shpk";
                    break;
                case MtrlShader.DyeableFurniture:
                    Shader = "bgcolorchange.shpk";
                    break;
                default:
                    // No change to the Shader for 'Other' type entries.
                    break;
            }

            // Clear unused maps.
            if (!info.HasDiffuse)
            {
                SetMapInfo(XivTexType.Diffuse, null);
            }
            if (!info.HasSpec)
            {
                SetMapInfo(XivTexType.Specular, null);
            }
            if (!info.HasMulti)
            {
                SetMapInfo(XivTexType.Multi, null);
            }
            if (!info.HasReflection)
            {
                SetMapInfo(XivTexType.Reflection, null);
            }

            // Clear or set the Colorset if needed.
            if(!info.HasColorset)
            {
                // ColorSetCount seems to always be 1, even when the data is empty.
                ColorsetStrings = new List<MtrlString>();
                ColorSetData = new List<Half>();
                ColorSetDyeData = null;
            } else
            {
                if(ColorsetStrings.Count == 0 || ColorSetData == null || ColorSetData.Count != 256)
                {
                    // Get default Colorset Data.
                    ColorSetData = Tex.GetColorsetDataFromDDS(Tex.GetDefaultTexturePath(XivTexType.ColorSet));
                }
                if(ColorSetDyeData == null || ColorSetDyeData.Length != 32)
                {
                    ColorSetDyeData = Tex.GetColorsetExtraDataFromDDS(Tex.GetDefaultTexturePath(XivTexType.ColorSet));
                }
            }

        }

        /// <summary>
        /// Converts raw Mtrl format data into the appropriate enum.
        /// Does a bit of math to ignore extraneous bits that don't have known purpose yet.
        /// </summary>
        /// <param name="raw"></param>
        /// <returns></returns>
        private static MtrlTextureSamplerFormatPresets GetFormat(short raw)
        {

            // Pare format short down of extraneous data.
            short clearLast6Bits = -64; // Hex 0xFFC0
            short format = (short)(raw & clearLast6Bits);
            short setFirstBit = -32768;
            format = (short)(format | setFirstBit);

            // Scan through known formats.
            foreach (var formatEntry in Mtrl.TextureSamplerFormatFlags)
            {
                if (format == formatEntry.Value)
                {
                    return formatEntry.Key;
                }
            }
            return MtrlTextureSamplerFormatPresets.Other;
        }


        /// <summary>
        /// Gets the given texture map info based on the textures used in this mtrl file.
        /// </summary>
        /// <param name="MapType"></param>
        /// <returns></returns>
        public MapInfo GetMapInfo(XivTexType MapType, bool tokenized = true)
        {

            var tex = Textures.FirstOrDefault(x => x.Usage == MapType);
            if(tex == null)
            {
                return null;
            }

            var info = new MapInfo();
            info.Usage = MapType;
            info.Path = tex.TexturePath;

            if (tokenized)
            {
                info.Path = TokenizePath(info.Path, info.Usage);
            }
            return info;
        }

        /// <summary>
        /// Retrieves the MapInfo struct for a given path contained in the Mtrl.
        /// Null if texture does not exist in the Mtrl.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public MapInfo GetMapInfo(string path)
        {
            var tex = Textures.FirstOrDefault(x => x.TexturePath == path);
            if(tex == null)
            {
                return null;
            }

            var info = new MapInfo();
            info.Path = path; 
            info.Format = GetFormat(tex.Sampler.FormatFlags);
            info.Usage = tex.Usage;

            info.Path = TokenizePath(info.Path, info.Usage);

            return info;
        }

        /// <summary>
        /// Sets or deletes the texture information from the Mtrl based on the
        /// incoming usage and info.
        /// </summary>
        /// <param name="MapType"></param>
        /// <param name="info"></param>
        public void SetMapInfo(XivTexType MapType, MapInfo info)
        {
            // Sanity check.
            if(info != null && info.Usage != MapType)
            {
                throw new System.Exception("Invalid attempt to reassign map materials.");
            }
           
            var tex = Textures.FirstOrDefault(x => x.Usage == MapType);

            if(info == null)
            {
                Textures.Remove(tex);
                return;
            }

            var rootPath = GetTextureRootDirectoy();
            var defaultFileName = GetDefaultTexureName(info.Usage);

            // No path, assign it by default.
            if (info.Path.Trim() == "")
            {
                info.Path = rootPath + "/" + defaultFileName;
            }

            // Detokenize paths
            info.Path = DetokenizePath(info.Path, info.Usage);

            // Test the path goes to a legitimate DAT file.
            try
            {
                IOUtil.GetDataFileFromPath(info.Path);
            } catch
            {
                // Prefix the item's personal path onto it.
                info.Path = ItemPathToken + "/" + info.Path;
                info.Path = DetokenizePath(info.Path, info.Usage);
            }

            // Ensure .tex or .atex ending for sanity.
            var match = Regex.Match(info.Path, "\\.a?tex$");
            if(!match.Success)
            {
                info.Path = info.Path += ".tex";
            }




            var sampler = new TextureSampler();
            sampler.SamplerId = Mtrl.TextureSamplerIds[info.Usage];
            sampler.UnknownFlags = 15;
            sampler.FormatFlags = Mtrl.TextureSamplerFormatFlags[info.Format];

            var newTex = new MtrlTexture()
            {
                TexturePath = info.Path,
                Sampler = sampler
            };
            Textures.Add(newTex);
        }

        /// <summary>
        /// Regenerates/Cleans up the texture usage list based on the 
        /// Texture Maps that are set in the Texture Description Fields.
        /// </summary>
        private void RegenerateShaderKeys(ShaderInfo info)
        {
            ShaderKeys.Clear();

            // These shaders do not use the texture usage list at all.
            if (info.Shader == MtrlShader.Furniture || info.Shader == MtrlShader.DyeableFurniture || info.Shader == MtrlShader.Iris || info.Shader == MtrlShader.Hair)
            {
                if(info.Shader == MtrlShader.Hair && info.Preset != MtrlShaderPreset.Default) 
                { 
                    // The facial hair shaders use this texture usage value to pipe in 
                    // Tattoo/Limbal color instead of Hair Highlight color.
                    SetShaderKey(XivTexType.Other);
                }
                return;
            }

            if (info.Shader == MtrlShader.Skin)
            {
                if (info.Preset == MtrlShaderPreset.Face || info.Preset == MtrlShaderPreset.FaceNoPores)
                {
                    SetShaderKey(XivTexType.Normal);
                    SetShaderKey(XivTexType.Diffuse);
                    SetShaderKey(XivTexType.Decal);
                    SetShaderKey(XivTexType.Specular);
                }
                else if(info.Preset == MtrlShaderPreset.BodyWithHair)
                {
                    SetShaderKey(XivTexType.Skin, 1476344676);
                } else 
                {
                    // Non-Face Skin textures use a single custom usage value.
                    SetShaderKey(XivTexType.Skin);
                }

            }
            else if (info.Shader == MtrlShader.Standard)
            {
                SetShaderKey(XivTexType.Normal);
                if (info.Preset == MtrlShaderPreset.Default)
                {
                    SetShaderKey(XivTexType.Decal);

                }
                else if (info.Preset == MtrlShaderPreset.DiffuseMulti)
                {
                    // This seems to crash the game atm.
                    SetShaderKey(XivTexType.Diffuse);
                    SetShaderKey(XivTexType.Decal);
                }
                else if (info.Preset == MtrlShaderPreset.Monster)
                {
                    SetShaderKey(XivTexType.Diffuse);
                    SetShaderKey(XivTexType.Decal);

                    // This flag seems to convert Specular textures to Multi textures.
                    SetShaderKey(XivTexType.Specular);
                }
                else
                {
                    SetShaderKey(XivTexType.Diffuse);
                    SetShaderKey(XivTexType.Decal);
                }
            }
            else
            {
                // This is uh... Glass shader?  I think is the only fall through here.
                SetShaderKey(XivTexType.Normal);
                SetShaderKey(XivTexType.Decal);
            }

        }

        /// <summary>
        /// Regenerates the Shader Parameter list based on shader and texture information.
        /// </summary>
        private void RegenerateShaderConstants(ShaderInfo info)
        {
            var args = new Dictionary<MtrlShaderConstantId, List<float>>();

            args.Add(MtrlShaderConstantId.AlphaLimiter, null);
            args.Add(MtrlShaderConstantId.Occlusion, null);

            if (info.Shader == MtrlShader.Skin)
            {
                args.Add(MtrlShaderConstantId.SkinColor, null);
                args.Add(MtrlShaderConstantId.SkinMatParamRow2, null);
                args.Add(MtrlShaderConstantId.SkinWetnessLerp, null);
                args.Add(MtrlShaderConstantId.SkinUnknown2, null);
                args.Add(MtrlShaderConstantId.SkinFresnel, null);
                args.Add(MtrlShaderConstantId.Reflection1, null);

                if(info.Preset == MtrlShaderPreset.BodyNoPores || info.Preset == MtrlShaderPreset.FaceNoPores)
                {
                    args.Add(MtrlShaderConstantId.SkinTileMaterial, new List<float>() { 0 });
                    args.Add(MtrlShaderConstantId.SkinTileMultiplier, new List<float>() { 0, 0 });

                } else
                {
                    args.Add(MtrlShaderConstantId.SkinTileMaterial, null);
                    args.Add(MtrlShaderConstantId.SkinTileMultiplier, null);
                }

                if (info.Preset == MtrlShaderPreset.Face || info.Preset == MtrlShaderPreset.FaceNoPores)
                {
                    args.Add(MtrlShaderConstantId.Face1, null);
                }
            }
            else if (info.Shader == MtrlShader.Standard)
            {
                if (info.Preset == MtrlShaderPreset.Monster)
                {
                    args.Remove(MtrlShaderConstantId.AlphaLimiter);
                    args.Remove(MtrlShaderConstantId.Occlusion);
                    args.Add(MtrlShaderConstantId.AlphaLimiter, new List<float>() { 0.5f });
                    args.Add(MtrlShaderConstantId.Occlusion, new List<float>() { 0.25f });
                    args.Add(MtrlShaderConstantId.Hair1, new List<float>() { 0 });
                }
                else
                {
                    args.Add(MtrlShaderConstantId.Equipment1, null);
                    args.Add(MtrlShaderConstantId.Reflection1, null);
                }
            }
            else if (info.Shader == MtrlShader.Iris)
            {
                args.Add(MtrlShaderConstantId.Equipment1, null);
                args.Add(MtrlShaderConstantId.Reflection1, null);
                args.Add(MtrlShaderConstantId.SkinFresnel, null);
                args.Add(MtrlShaderConstantId.SkinColor, null);
                args.Add(MtrlShaderConstantId.SkinWetnessLerp, null);
            }
            else if(info.Shader == MtrlShader.Hair)
            {
                args.Add(MtrlShaderConstantId.Equipment1, null);
                args.Add(MtrlShaderConstantId.Reflection1, null);
                args.Add(MtrlShaderConstantId.SkinColor, null);
                args.Add(MtrlShaderConstantId.SkinWetnessLerp, null);
                args.Add(MtrlShaderConstantId.Hair1, null);
                args.Add(MtrlShaderConstantId.Hair2, null);

                if (info.Preset == MtrlShaderPreset.FaceBright)
                {
                    // Limbals use a 3x skin color modifier.
                    args[MtrlShaderConstantId.SkinColor] = new List<float>() { 3f, 3f, 3f };
                }
            }
            else if(info.Shader == MtrlShader.Glass)
            {
                args.Remove(MtrlShaderConstantId.AlphaLimiter);
                args.Remove(MtrlShaderConstantId.Occlusion);
                args.Add(MtrlShaderConstantId.AlphaLimiter, new List<float>() { 0.25f });
                args.Add(MtrlShaderConstantId.Hair2, new List<float>() { 1.0f });
            }


            // Regenerate the list.
            ShaderConstants.Clear();
            //args.ForEach(x => SetShaderParameter(x));
            foreach(var kv in args)
            {
                // Nulls use defaults.
                SetShaderConstant(kv.Key, kv.Value);
            }
        }


        /// <summary>
        /// Gets a given shader Parameter by ID.
        /// </summary>
        /// <param name="parameterId"></param>
        /// <returns></returns>
        private ShaderConstant GetShaderConstant(MtrlShaderConstantId parameterId)
        {
            return ShaderConstants.FirstOrDefault(x => x.ConstantId == parameterId);
        }

        /// <summary>
        /// Sets or adds a given shader parameter.
        /// </summary>
        /// <param name="parameterId"></param>
        /// <param name="data"></param>
        private void SetShaderConstant(MtrlShaderConstantId parameterId, List<float> data = null)
        {
            try
            {
                var value = ShaderConstants.First(x => x.ConstantId == parameterId);

                // Only overwrite if we were given explicit data.
                if (value != null && data != null)
                {
                    value.Values = data;
                }
            }
            catch (Exception ex)
            {
                if (data == null)
                {
                    data = Mtrl.ShaderConstantValues[parameterId];
                }

                ShaderConstants.Add(new ShaderConstant
                {
                    ConstantId = parameterId,
                    Values = data
                });
            }
        }

        /// <summary>
        /// Removes a given shader parameter.
        /// </summary>
        /// <param name="parameterId"></param>
        private void ClearShaderConstant(MtrlShaderConstantId parameterId)
        {
            ShaderConstants = ShaderConstants.Where(x => x.ConstantId != parameterId).ToList();
        }


        /// <summary>
        /// Gets a given shader key by its usage value.
        /// </summary>
        /// <param name="usage"></param>
        /// <returns></returns>
        private ShaderKey GetShaderKey(XivTexType usage)
        {
            return ShaderKeys.FirstOrDefault(x => x.Category == Mtrl.ShaderKeyCategories[usage].Category);
        }

        /// <summary>
        /// Clears a texture usage value.
        /// </summary>
        /// <param name="usage"></param>
        private bool ClearShaderKey(XivTexType usage)
        {
            var oldCount = ShaderKeys.Count;
            ShaderKeys = ShaderKeys.Where(x => x.Category != Mtrl.ShaderKeyCategories[usage].Category).ToList();
            return oldCount != ShaderKeys.Count;
        }

        /// <summary>
        /// Adds or changes a shader key.
        /// </summary>
        /// <param name="usage"></param>
        /// <param name="unknownValue"></param>
        private void SetShaderKey(XivTexType usage, uint? unknownValue = null)
        {
            if(unknownValue == null)
            {
                unknownValue = Mtrl.ShaderKeyCategories[usage].Value;
            }
            try
            {
                var val = ShaderKeys.First(x => x.Category == Mtrl.ShaderKeyCategories[usage].Category);
                if(val != null)
                {
                    // Despite being named 'Struct' this is actually a class.
                    val.Value = (uint) unknownValue;
                } else
                {
                    ShaderKeys.Add(new ShaderKey()
                    {
                        Category = Mtrl.ShaderKeyCategories[usage].Category,
                        Value = (uint) unknownValue
                    });
                }
            } catch(Exception ex)
            {
                ShaderKeys.Add(new ShaderKey()
                {
                    Category = Mtrl.ShaderKeyCategories[usage].Category,
                    Value = (uint) unknownValue
                });
            }
        }

        /// <summary>
        /// Retrieve all MapInfo structs for all textures associated with this MTRL,
        /// Including textures with unknown usage.
        /// </summary>
        /// <returns></returns>
        public List<MapInfo> GetAllMapInfos(bool tokenize = true)
        {
            var ret = new List<MapInfo>();
            var shaderInfo = GetShaderInfo();

            foreach(var tex in Textures)
            {
                var info = new MapInfo();
                info.Path = tex.TexturePath;
                info.Format = GetFormat(tex.Sampler.FormatFlags);
                info.Usage = tex.Usage;

                if (tokenize)
                {
                    info.Path = TokenizePath(info.Path, info.Usage);
                }
                ret.Add(info);
            }

            return ret;
        }

        /// <summary>
        /// Get the 'TexTypePath' List that TT expects to populate the texture listing.
        /// Generated via using GetAllMapInfos to scan based on actual MTRL settings,
        /// Rather than file names.
        /// </summary>
        /// <returns></returns>
        public List<TexTypePath> GetTextureTypePathList()
        {
            var ret = new List<TexTypePath>();
            var maps = GetAllMapInfos(false);
            var shaderInfo = GetShaderInfo();
            TexTypePath ttp;
            foreach (var map in maps)
            {
                if (shaderInfo.Shader == MtrlShader.Skin && map.Usage == XivTexType.Multi)
                {
                    ttp = new TexTypePath() { DataFile = GetDataFile(), Path = map.Path, Type = XivTexType.Skin };

                } else if (shaderInfo.Shader == MtrlShader.Furniture && map.Path.Contains("dummy")) {
                    // Dummy textures are skipped.
                    continue;
                }
                else
                {
                    ttp = new TexTypePath() { DataFile = GetDataFile(), Path = map.Path, Type = map.Usage };
                }
                var fName = Path.GetFileNameWithoutExtension(map.Path);
                if (fName != "")
                {
                    var name = map.Usage.ToString() + ": " + fName;
                    ttp.Name = name;
                }

                ret.Add(ttp);
            }


            // Include the colorset as its own texture if we have one.
            if (ColorsetStrings.Count > 0 && ColorSetData.Count > 0)
            {
                ttp = new TexTypePath
                {
                    Path = MTRLPath,
                    Type = XivTexType.ColorSet,
                    DataFile = GetDataFile()
                };
                ret.Add(ttp);
            }

            return ret;
        }

        /// <summary>
        /// Retrieves the Data File this MTRL resides in based on the path of the MTRL file.
        /// </summary>
        /// <returns></returns>
        public XivDataFile GetDataFile()
        {
            return Helpers.IOUtil.GetDataFileFromPath(MTRLPath);
        }

        /// <summary>
        /// Retreives the base texture directory this material references.
        /// </summary>
        /// <returns></returns>
        public string GetTextureRootDirectoy()
        {
            string root = "";
            // Attempt to logically deduce base texture path.
            if (MTRLPath.Contains("material/"))
            {
                var match = Regex.Match(MTRLPath, "(.*)material/");
                if(match.Success)
                {
                    root = match.Groups[1].Value + "texture";
                }
            } else
            {
                // Default to common texture root.
                root = GetCommonTextureDirectory();
            }

            return root;
        }

        /// <summary>
        /// Gets the item version this MTRL is attached to, based on directory.
        /// </summary>
        /// <returns></returns>
        public uint GetVariant()
        {
            var match = Regex.Match(MTRLPath, "/v([0-9]{4})/");
            if (match.Success)
            {
                return (uint)Int32.Parse(match.Groups[1].Value);
            }
            return 0;
        }

        /// <summary>
        /// Gets the variant string based on the item version. Ex. 'v03_'
        /// </summary>
        /// <returns></returns>
        public string GetVariantString()
        {
            var version = GetVariant();

            var versionString = "";
            if (version > 0)
            {
                versionString += 'v' + (version.ToString().PadLeft(2, '0'));
            }
            return versionString;
        }

        /// <summary>
        /// Gets the default texture name for this material for a given texture type.
        /// </summary>
        /// <returns></returns>
        public string GetDefaultTexureName(XivTexType texType, bool includeVersion = true)
        {

            var ret = "";

            // When available, version number prefixes the texture name.
            if (includeVersion)
            {
                ret += GetVariantString() + "_";
            }

            // Followed by the character and secondary identifier.
            var match = Regex.Match(MTRLPath, "c[0-9]{4}[a-z][0-9]{4}");
            if (match.Success)
            {
                ret += match.Value;
            }
            else {
                ret += "unknown";
            }

            ret += GetItemTypeIdentifier();

            // Followed by the material identifier, if above [a].
            var identifier = GetMaterialIdentifier();
            if(identifier != 'a')
            {
                ret += "_" + identifier;
            }

            

            if(texType == XivTexType.Normal)
            {
                ret += "_n";
            } else if(texType == XivTexType.Specular)
            {
                ret += "_s";
            }
            else if (texType == XivTexType.Multi)
            {
                var path = MTRLPath;
                if (path.Contains("chara/human/c"))
                {
                    // The character folder uses _s instead of _m despite the textures being multis.
                    ret += "_s";
                }
                else
                {
                    ret += "_m";
                }
            }
            else if (texType == XivTexType.Diffuse)
            {
                ret += "_d";
            } else
            {
                ret += "_o";
            }

            ret += ".tex";
            return ret;
        }

        /// <summary>
        /// Gets the Material/Part identifier letter based on the file path.
        /// </summary>
        /// <returns></returns>
        public char GetMaterialIdentifier()
        {
            var match = Regex.Match(MTRLPath, "_([a-z0-9])\\.mtrl");
            if(match.Success)
            {
                return match.Groups[1].Value[0];
            }
            return 'a';
        }

        public string GetItemTypeIdentifier()
        {
            // This regex feels a little janky, but it's good enough for now.
            var match = Regex.Match(MTRLPath, "_([a-z]{3})_[a-z0-9]\\.mtrl");
            if (match.Success)
            {
                return "_" + match.Groups[1].Value;
            } else if (MTRLPath.Contains("/obj/tail/t")) {
                // Tails have their textures (but not materials) listed as _etc parts.
                return "_etc";
            }
            return "";
        }

        public XivRace GetRace()
        {
            var races= Enum.GetValues(typeof(XivRace)).Cast<XivRace>();
            foreach(var race in races)
            {
                // Test the root path for the racial identifier.
                var match = Regex.Match(MTRLPath, "c" + race.GetRaceCode());
                if(match.Success)
                {
                    return race;
                }
            }
            return XivRace.All_Races;
        }


        /// <summary>
        /// Get the root shared common texture directory for FFXIV.
        /// </summary>
        /// <returns></returns>
        public static string GetCommonTextureDirectory()
        {
            return "chara/common/texture";
        }
        
        /// <summary>
        /// Tokenize a given path string using this Material's settings to create and resolve tokens.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="usage"></param>
        /// <returns></returns>
        public string TokenizePath(string path, XivTexType usage)
        {
            path = path.Replace(GetTextureRootDirectoy(), XivMtrl.ItemPathToken);

            var commonPath = XivMtrl.GetCommonTextureDirectory();
            path = path.Replace(commonPath, XivMtrl.CommonPathToken);


            var version = GetVariantString();
            if (version != "")
            {
                path = path.Replace(version, XivMtrl.VariantToken);
            }

            var texName = GetDefaultTexureName(usage, false);
            path = path.Replace(texName, XivMtrl.TextureNameToken);
            return path;
        }

        /// <summary>
        /// Detokenize a given path string using this Material's settings to create and resolve tokens.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="usage"></param>
        /// <returns></returns>
        public string DetokenizePath(string path, XivTexType usage)
        {
            var rootPath = GetTextureRootDirectoy();
            var defaultFileName = GetDefaultTexureName(usage);

            // No path, assign it by default.
            if (path == "")
            {
                path = rootPath + "/" + defaultFileName;
                return path;
            }

            var rootPathWithVersion = rootPath;
            var variantString = GetVariantString();


            var defaultFileNameWithoutVersion = GetDefaultTexureName(usage, false);
            path = path.Replace(ItemPathToken, rootPath);
            path = path.Replace(VariantToken, variantString);
            path = path.Replace(TextureNameToken, defaultFileNameWithoutVersion);
            path = path.Replace(CommonPathToken, GetCommonTextureDirectory());
            return path;
        }


    }

    public class MtrlString : ICloneable
    {
        public string Value;
        public ushort Flags;

        public object Clone()
        {
            return MemberwiseClone();
        }
    }

    /// <summary>
    /// Class representing the sum of information used as a texture reference in a material.
    /// </summary>
    public class MtrlTexture : ICloneable
    {
        public string TexturePath;

        public ushort Flags;

        public TextureSampler Sampler;

        /// <summary>
        /// Retrieves this texture's usage type based on the sampler id.
        /// </summary>
        public XivTexType Usage
        {
            get
            {
                if(Sampler == null || !Mtrl.TextureSamplerIds.ContainsValue(Sampler.SamplerId))
                {
                    return XivTexType.Other;
                }
                return Mtrl.TextureSamplerIds.First(x => x.Value == Sampler.SamplerId).Key;
            }
        }

        public object Clone()
        {
            var clone = (MtrlTexture) MemberwiseClone();
            clone.Sampler = (TextureSampler) Sampler.Clone();
            return clone;
        }
    }

    /// <summary>
    /// This class contains the information for the shader keys.
    /// Primarily these enable/disable shader functionality at large.
    /// </summary>
    public class ShaderKey : ICloneable
    {
        public uint Category; // Mappings for this TextureType value to XivTexType value are available in Mtrl.cs

        public uint Value;

        public object Clone()
        {
            return MemberwiseClone();
        }
    }

    /// <summary>
    /// This class contains the information for the shader parameters at the end the MTRL File
    /// </summary>
    public class ShaderConstant : ICloneable
    {
        public MtrlShaderConstantId ConstantId
        {
            get
            {
                if(Enum.IsDefined(typeof(MtrlShaderConstantId), ConstantIdRaw))
                {
                    return (MtrlShaderConstantId)ConstantIdRaw;
                }
                return MtrlShaderConstantId.Unknown;
            }
            set
            {
                ConstantIdRaw = (uint)value;
            }
        }

        public uint ConstantIdRaw;

        // The actual data (after extraction).
        public List<float> Values;

        public object Clone()
        {
            var clone = (ShaderConstant)MemberwiseClone();
            clone.Values = Values.ToList();
            return clone;
        }
    }

    /// <summary>
    /// This class contains the information for MTRL texture samplers.
    /// These determine how each texture is used.
    /// </summary>
    public class TextureSampler : ICloneable
    {
        public uint SamplerId; // Mappings for this TextureType value to XivTexType value are available in Mtrl.cs

        public short FormatFlags; // Mappings for this FileFormat value to MtrlTextureDescriptorFormat value are available in Mtrl.cs 

        public short UnknownFlags;   // Always seems to be [15]?
        public object Clone()
        {
            return MemberwiseClone();
        }
    }

    // Enum representation of the format map data is used as.
    public enum MtrlTextureSamplerFormatPresets
    {
        UsesColorset,
        NoColorset,
        Other
    }

    // Enum representation of the shader names used in mtrl files.
    public enum MtrlShader
    {
        Standard,           // character.shpk
        Glass,              // characterglass.shpk
        Skin,               // skin.shpk
        Hair,               // hair.shpk
        Iris,               // iris.shpk
        Furniture,          // bg.shpk
        DyeableFurniture,   //bgcolorchange.shpk 
        Other               // Unknown Shader
    }

    // Enum representation of the shader presets that the framework
    // Knows how to build.
    public enum MtrlShaderPreset
    {
        Default,      
        DiffuseMulti,    
        DiffuseSpecular,
        BodyNoPores,
        BodyWithHair,
        Face,
        FaceNoPores,
        FaceBright,
        Monster,
    }

    /// <summary>
    /// Enums for the various shader parameter Ids.  These likely are used to pipe extra data from elsewhere into the shader.
    /// Ex. Local Reflection Maps, Character Skin/Hair Color, Dye Color, etc.
    /// </summary>
    public enum MtrlShaderConstantId : uint
    {
        Unknown = 0,
        AlphaLimiter = 699138595,        // Used in every material.  Overwriting bytes with 0s seems to have no effect.
        Occlusion = 1465565106,       // Used in every material.  Overwriting bytes with 0s seems to have no effect.

        SkinColor = 740963549,          // This skin args seem to be the same for all races.
        SkinWetnessLerp = 2569562539,
        SkinMatParamRow2 = 390837838,
        SkinUnknown2 = 950420322,          // Always all 0 data?
        SkinTileMaterial = 1112929012,

        Face1 = 2274043692,

        Equipment1 = 3036724004,    // Used in some equipment rarely, particularly Legacy items.  Always seems to have [0]'s for data.
        Reflection1 = 906496720,     // This seems to be some kind of setting related to reflecitivty/specular intensity.

        Hair1 = 364318261,          // Character Hair Color?
        Hair2 = 3042205627,         // Character Highlight Color

        SkinFresnel = 1659128399,         // This arg is the same for most races, but Highlander and Roe M use a different value
        SkinTileMultiplier = 778088561,          // Roe M is the only one that has a change to this arg's data.?

        Furniture1 = 1066058257,
        Furniture2 = 337060565,
        Furniture3 = 2858904847,
        Furniture4 = 2033894819,
        Furniture5 = 2408251504,
        Furniture6 = 1139120744,
        Furniture7 = 3086627810,
        Furniture8 = 2456716813,
        Furniture9 = 3219771693,
        Furniture10 = 2781883474,
        Furniture11 = 2365826946,
        Furniture12 = 3147419510,
        Furniture13 = 133014596
    }


    public class ShaderInfo
    {
        public MtrlShader Shader;
        public MtrlShaderPreset Preset;
        public bool TransparencyEnabled;
        public bool RenderBackfaces = false;
        
        public bool HasColorset {
            get
            {
                if (Shader == MtrlShader.Standard || Shader == MtrlShader.Glass || Shader == MtrlShader.Furniture || Shader == MtrlShader.DyeableFurniture)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public bool HasDiffuse
        {
            get
            {
                if (Shader == MtrlShader.Standard && Preset == MtrlShaderPreset.Default)
                {
                    return false;
                }
                else if (Shader == MtrlShader.Hair)
                {
                    return false;
                }
                else if (Shader == MtrlShader.Iris)
                {
                    return false;
                }
                else if (Shader == MtrlShader.Glass)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }
        public bool HasReflection
        {
            get
            {
                if (Shader == MtrlShader.Iris)
                {
                    return true;
                }

                return false;
            }
        }
        public bool HasSpec
        {
            get
            {
                if (Shader == MtrlShader.Standard && (Preset == MtrlShaderPreset.DiffuseSpecular || Preset == MtrlShaderPreset.Monster))
                {
                    return true;
                } else if(Shader == MtrlShader.Furniture || Shader == MtrlShader.DyeableFurniture)
                {
                    return true;
                } else
                {
                    return false;
                }

            }
        }
        public bool HasMulti
        {
            get
            {
                return !HasSpec;
            }
        }
        public bool? ForcedTransparency
        {
            get
            {
                if (Shader == MtrlShader.Standard || Shader == MtrlShader.Furniture || Shader == MtrlShader.DyeableFurniture)
                {
                    // These shaders allow variable transparency.
                    return null;
                }
                else
                {
                    // These shaders require transparency to be strictly On or Off.
                    if(Shader == MtrlShader.Hair || Shader == MtrlShader.Glass)
                    {
                        return true;
                    } else
                    {
                        return false;
                    }
                }
            }
        }

        public static List<MtrlShaderPreset> GetAvailablePresets(MtrlShader shader)
        {
            var presets = new List<MtrlShaderPreset>();
            presets.Add(MtrlShaderPreset.Default);
            if (shader == MtrlShader.Standard)
            {
                //presets.Add(MtrlShaderPreset.DiffuseMulti);
                presets.Add(MtrlShaderPreset.DiffuseSpecular);
                presets.Add(MtrlShaderPreset.Monster);
            } else if(shader == MtrlShader.Skin)
            {
                presets.Add(MtrlShaderPreset.BodyNoPores);
                presets.Add(MtrlShaderPreset.BodyWithHair);
                presets.Add(MtrlShaderPreset.Face);
                presets.Add(MtrlShaderPreset.FaceNoPores);
            } else if(shader == MtrlShader.Hair)
            {
                presets.Add(MtrlShaderPreset.Face);
                presets.Add(MtrlShaderPreset.FaceBright);
            }

            return presets;
        }
    }

    public class MapInfo : ICloneable
    {
        public XivTexType Usage;
        public MtrlTextureSamplerFormatPresets Format;
        public string Path;


        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }


}