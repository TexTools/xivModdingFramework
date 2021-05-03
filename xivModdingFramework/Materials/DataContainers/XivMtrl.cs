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

        /// <summary>
        /// The size of the Material Data section
        /// </summary>
        /// <remarks>
        /// This is the size of the data chunk containing all of the path and filename strings
        /// </remarks>
        public ushort MaterialDataSize { get; set; }

        /// <summary>
        /// The size of the Texture Path Data section
        /// </summary>
        /// <remarks>
        /// This is the size of the data chucnk containing only the texture paths
        /// </remarks>
        public ushort TexturePathsDataSize { get; set; }

        /// <summary>
        /// The number of textures paths in the mtrl
        /// </summary>
        public byte TextureCount { get; set; }

        /// <summary>
        /// The number of map paths in the mtrl
        /// </summary>
        public byte MapCount { get; set; }

        /// <summary>
        /// The amount of color sets in the mtrl
        /// </summary>
        /// <remarks>
        /// It is not known if there are any instances where this is greater than 1
        /// </remarks>
        public byte ColorSetCount { get; set; }

        /// <summary>
        /// The number of bytes to skip after path section
        /// </summary>
        public byte DxFlagsDataSize { get; set; }

        /// <summary>
        /// A list containing the Texture Path offsets
        /// </summary>
        public List<int> TexturePathOffsetList { get; set; }

        /// <summary>
        /// A list containing the Texture Path Unknowns
        /// </summary>
        public List<short> TexturePathUnknownList { get; set; }

        /// <summary>
        /// A list containing the Map Path offsets
        /// </summary>
        public List<int> MapPathOffsetList { get; set; }

        /// <summary>
        /// A list containing the Map Path DX information
        /// </summary>
        public List<short> MapPathDxList { get; set; }

        /// <summary>
        /// A list containing the ColorSet Path offsets
        /// </summary>
        public List<int> ColorSetPathOffsetList { get; set; }

        /// <summary>
        /// A list containing the ColorSet Path Unknowns
        /// </summary>
        public List<short> ColorSetPathUnknownList { get; set; }

        /// <summary>
        /// A list containing the Texture Path strings
        /// </summary>
        public List<string> TexturePathList { get; set; }

        /// <summary>
        /// A list containing the Map Path strings
        /// </summary>
        public List<string> MapPathList { get; set; }

        /// <summary>
        /// A list containing the ColorSet Path strings
        /// </summary>
        public List<string> ColorSetPathList { get; set; }

        /// <summary>
        /// The name of the shader pack used by the item
        /// </summary>
        public string ShaderPack { get; set; }

        /// <summary>
        /// Flags for DX Level information and some other unknown flags
        /// </summary>
        public byte[] DxFlags { get; set; }

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
                ShaderParameterList.ForEach(x => {
                    size += x.Constants.Count * 4;
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
        public ushort ShaderTechniqueCount { get { return (ushort)ShaderTechniqueList.Count; } set
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
            get { return (ushort)ShaderParameterList.Count; }
            set
            {
                //No-Op
                throw new Exception("Attempted to directly set ShaderParameterCount");
            }
        }
        /// <summary>
        /// The number of parameter stuctures
        /// </summary>
        public ushort TextureSamplerSettingsCount
        {
            get { return (ushort)TextureSamplerSettingsList.Count; }
            set
            {
                //No-Op
                throw new Exception("Attempted to directly set TextureDescriptorCount");
            }
        }

        /// <summary>
        /// Some flags used in rendering.  Of note, includes the backface toggle bit.
        /// </summary>
        public ushort RenderFlags { get; set; }

        /// <summary>
        /// Some unknown flags or padding data.
        /// </summary>
        public ushort UnknownFlags { get; set; }

        /// <summary>
        /// The list of Type 1 data structures
        /// </summary>
        public List<ShaderTechniques> ShaderTechniqueList { get; set; }

        /// <summary>
        /// The list of Type 2 data structures
        /// </summary>
        public List<ShaderConstants> ShaderParameterList { get; set; }

        /// <summary>
        /// The list of Parameter data structures
        /// </summary>
        public List<TextureSamplerSettings> TextureSamplerSettingsList { get; set; }

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
            switch (ShaderPack)
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
            var bit = (ushort)(RenderFlags & transparencyBit);

            if(bit > 0)
            {
                info.TransparencyEnabled = true;
            } else
            {
                info.TransparencyEnabled = false;
            }

            bit = (ushort)(RenderFlags & backfaceBit);

            if (bit > 0)
            {
                info.RenderBackfaces = false;
            }
            else
            {
                info.RenderBackfaces = true;
            }

            var txul = ShaderTechniqueList;
            var txdl = TextureSamplerSettingsList;
            var shpl = ShaderParameterList;

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
                        if (GetShaderTechnique(ShaderTechniqueId.SpecToMulti) != null)
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
                if(GetShaderTechnique(ShaderTechniqueId.Skin) == null)
                {
                    info.Preset = MtrlShaderPreset.Face;
                }

                if(GetShaderTechnique(ShaderTechniqueId.Skin) != null && GetShaderTechnique(ShaderTechniqueId.Skin).Value == 1476344676)
                {
                    info.Preset = MtrlShaderPreset.BodyWithHair;
                }
                else if(ShaderParameterList.Any(x => x.ConstantId == ShaderConstantId.SkinTileMaterial))
                {
                    var param = ShaderParameterList.First(x => x.ConstantId == ShaderConstantId.SkinTileMaterial);
                    if(param.Constants[0] == 0)
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
                if(GetShaderTechnique(ShaderTechniqueId.HighlightsToTattoo) != null)
                {
                    info.Preset = MtrlShaderPreset.Face;
                    var mul = 1.4f;
                    var param = ShaderParameterList.FirstOrDefault(x => x.ConstantId == ShaderConstantId.SkinColor);
                    if(param != null)
                    {
                        mul = param.Constants[0];
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
                RenderFlags = (ushort)(RenderFlags | transparencyBit);
            }
            else
            {
                RenderFlags = (ushort)(RenderFlags & (~transparencyBit));
            }

            // Set Backfaces bit.
            var backfaces = info.RenderBackfaces;
            if (!backfaces)
            {
                RenderFlags = (ushort)(RenderFlags | backfaceBit);
            }
            else
            {
                RenderFlags = (ushort)(RenderFlags & (~backfaceBit));
            }

            // Update us to DX11 material style if we're not already.
            for (var idx = 0; idx < DxFlags.Length; idx++)
            {
                if (idx == 0)
                {
                    DxFlags[idx] = 12;
                }
                else
                {
                    DxFlags[idx] = 0;
                }
            }

            for (var idx = 0; idx < TexturePathUnknownList.Count; idx++)
            {
                TexturePathUnknownList[idx] = 0;
            }


            if (forced == false && info.Shader == old.Shader && info.Preset == old.Preset)
            {
                // Nothing needs to be changed.
                // Returning here ensures shader information
                // is not bashed unless edited.
                return;
            }

            RegenerateTextureUsageList(info);
            RegenerateShaderParameterList(info);

            switch (info.Shader)
            {
                case MtrlShader.Standard:
                    ShaderPack = "character.shpk";
                    break;
                case MtrlShader.Glass:
                    ShaderPack = "characterglass.shpk";
                    break;
                case MtrlShader.Hair:
                    ShaderPack = "hair.shpk";
                    break;
                case MtrlShader.Iris:
                    ShaderPack = "iris.shpk";
                    break;
                case MtrlShader.Skin:
                    ShaderPack = "skin.shpk";
                    break;
                case MtrlShader.Furniture:
                    ShaderPack = "bg.shpk";
                    break;
                case MtrlShader.DyeableFurniture:
                    ShaderPack = "bgcolorchange.shpk";
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
                ColorSetCount = 1;
                ColorSetData = new List<Half>();
                ColorSetDyeData = null;
            } else
            {
                if(ColorSetCount == 0 || ColorSetData == null || ColorSetData.Count != 256)
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
        private static TextureSamplerSettingsPresets GetFlagSet(short raw)
        {

            // Pare format short down of extraneous data.
            short clearLast6Bits = -64; // Hex 0xFFC0
            short format = (short)(raw & clearLast6Bits);
            short setFirstBit = -32768;
            format = (short)(format | setFirstBit);

            // Scan through known formats.
            foreach (var formatEntry in Mtrl.SamplerSettingsPresets)
            {
                if (format == formatEntry.Value)
                {
                    return formatEntry.Key;
                }
            }
            return TextureSamplerSettingsPresets.Other;
        }


        /// <summary>
        /// Gets the given texture map info based on the textures used in this mtrl file.
        /// </summary>
        /// <param name="MapType"></param>
        /// <returns></returns>
        public MapInfo GetMapInfo(XivTexType MapType)
        {
            var info = new MapInfo();
            int mapIndex = -1;

            info.Usage = MapType;

            // Look for the right map type in the parameter list.
            foreach (var paramSet in TextureSamplerSettingsList)
            {
                if (SamplerToTexType.ContainsKey(paramSet.SamplerId) && SamplerToTexType[paramSet.SamplerId] == MapType)
                {
                    mapIndex = (int) paramSet.TextureIndex;
                }
            }

            if(mapIndex < 0)
            {
                return null;
            }

            info.Path = "";
            if ( mapIndex < TexturePathList.Count )
            {
                info.Path = TexturePathList[mapIndex];
            }

            info.Path = TokenizePath(info.Path, info.Usage);
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

            // Step 1 - Get index of the path
            var idx = -1;
            for(var i = 0; i < TexturePathList.Count; i++)
            {
                if(TexturePathList[i] == path)
                {
                    idx = i;
                    break;
                }
            }
            if(idx == -1)
            {
                return null;
            }

            var info = new MapInfo();
            info.Path = path;
            
            foreach(var descriptor in TextureSamplerSettingsList)
            {
                // Found the descriptor.
                if(descriptor.TextureIndex == idx)
                {
                    // If we know what this texture descriptor actually means
                    if(SamplerToTexType.ContainsKey(descriptor.SamplerId))
                    {
                        info.Usage = SamplerToTexType[descriptor.SamplerId];
                    }
                    else
                    {
                        info.Usage = XivTexType.Other;
                    }

                }
            }

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
           
            var paramIdx = -1;
            TextureSamplerSettings oldInfo = new TextureSamplerSettings();
            // Look for the right map type in the parameter list.
            for( var i = 0; i < TextureSamplerSettingsList.Count; i++)
            {
                var paramSet = TextureSamplerSettingsList[i];

                if (SamplerToTexType.ContainsKey(paramSet.SamplerId) && SamplerToTexType[paramSet.SamplerId] == MapType)
                {
                    paramIdx = i;
                    oldInfo = paramSet;
                    break;
                }
            }


            // Deleting existing info.
            if (info == null)
            {

                if (paramIdx >= 0)
                {
                    // Remove texture from path list if it exists.
                    if (TexturePathList.Count > oldInfo.TextureIndex)
                    {
                        TexturePathList.RemoveAt((int)oldInfo.TextureIndex);
                        TexturePathUnknownList.RemoveAt((int)oldInfo.TextureIndex);
                    }

                    // Remove Parameter List
                    TextureSamplerSettingsList.RemoveAt(paramIdx);

                    // Update other texture offsets
                    for (var i = 0; i < TextureSamplerSettingsList.Count; i++)
                    {
                        var p = TextureSamplerSettingsList[i];
                        if (p.TextureIndex > oldInfo.TextureIndex)
                        {
                            p.TextureIndex--;
                        }
                        TextureSamplerSettingsList[i] = p;
                    }
                }

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




            var raw = new TextureSamplerSettings();
            raw.SamplerId = TexTypeToSamplerDefault[info.Usage];
            raw.Flags = 15;

            // TODO: FIXFIX
            raw.SamplerSettings = (ushort) TextureSamplerSettingsPresets.UsesColorset;
            raw.TextureIndex = (paramIdx >= 0 ? (uint)oldInfo.TextureIndex : (uint)TexturePathList.Count);

            // Inject the new parameters.
            if(paramIdx >= 0)
            {
                TextureSamplerSettingsList[paramIdx] = raw;
            } else
            {
                TextureSamplerSettingsList.Add(raw);
            }

            // Inject the new string
            if(raw.TextureIndex == TexturePathList.Count)
            {
                TexturePathList.Add(info.Path);
                TexturePathUnknownList.Add((short) 0); // This value seems to always be 0 for textures.
            } else
            {
                TexturePathList[(int) raw.TextureIndex] = info.Path;
            }
        }

        /// <summary>
        /// Regenerates/Cleans up the texture usage list based on the 
        /// Texture Maps that are set in the Texture Description Fields.
        /// </summary>
        private void RegenerateTextureUsageList(ShaderInfo info)
        {
            ShaderTechniqueList.Clear();

            // These shaders do not use the texture usage list at all.
            if (info.Shader == MtrlShader.Furniture || info.Shader == MtrlShader.DyeableFurniture || info.Shader == MtrlShader.Iris || info.Shader == MtrlShader.Hair)
            {
                if(info.Shader == MtrlShader.Hair && info.Preset != MtrlShaderPreset.Default) 
                { 
                    // The facial hair shaders use this texture usage value to pipe in 
                    // Tattoo/Limbal color instead of Hair Highlight color.
                    SetShaderTechnique(ShaderTechniqueId.HighlightsToTattoo);
                }
                return;
            }

            if (info.Shader == MtrlShader.Skin)
            {
                if (info.Preset == MtrlShaderPreset.Face || info.Preset == MtrlShaderPreset.FaceNoPores)
                {
                    SetShaderTechnique(ShaderTechniqueId.Common);
                }
                else if(info.Preset == MtrlShaderPreset.BodyWithHair)
                {
                    SetShaderTechnique(ShaderTechniqueId.Skin, 1476344676);
                } else 
                {
                    // Non-Face Skin textures use a single custom usage value.
                    SetShaderTechnique(ShaderTechniqueId.Skin);
                }

            }
            else if (info.Shader == MtrlShader.Standard)
            {
                SetShaderTechnique(ShaderTechniqueId.Common);
                if (info.Preset == MtrlShaderPreset.Default)
                {
                    SetShaderTechnique(ShaderTechniqueId.Decal);

                }
                else if (info.Preset == MtrlShaderPreset.DiffuseMulti)
                {
                    // This seems to crash the game atm.
                    SetShaderTechnique(ShaderTechniqueId.Diffuse);
                    SetShaderTechnique(ShaderTechniqueId.Decal);
                }
                else if (info.Preset == MtrlShaderPreset.Monster)
                {
                    SetShaderTechnique(ShaderTechniqueId.Diffuse);
                    SetShaderTechnique(ShaderTechniqueId.Decal);

                    // This flag seems to convert Specular textures to Multi textures.
                    SetShaderTechnique(ShaderTechniqueId.SpecToMulti);
                }
                else
                {
                    SetShaderTechnique(ShaderTechniqueId.Diffuse);
                    SetShaderTechnique(ShaderTechniqueId.Decal);
                }
            }
            else
            {
                // This is uh... Glass shader?  I think is the only fall through here.
                SetShaderTechnique(ShaderTechniqueId.Common);
                SetShaderTechnique(ShaderTechniqueId.Decal);
            }

        }

        /// <summary>
        /// Regenerates the Shader Parameter list based on shader and texture information.
        /// </summary>
        private void RegenerateShaderParameterList(ShaderInfo info)
        {
            var args = new Dictionary<ShaderConstantId, List<float>>();

            args.Add(ShaderConstantId.AlphaLimiter, null);
            args.Add(ShaderConstantId.Occlusion, null);

            if (info.Shader == MtrlShader.Skin)
            {
                args.Add(ShaderConstantId.SkinColor, null);
                args.Add(ShaderConstantId.SkinMatParamRow2, null);
                args.Add(ShaderConstantId.SkinWetnessLerp, null);
                args.Add(ShaderConstantId.SkinUnknown2, null);
                args.Add(ShaderConstantId.SkinFresnel, null);
                args.Add(ShaderConstantId.Reflection1, null);

                if(info.Preset == MtrlShaderPreset.BodyNoPores || info.Preset == MtrlShaderPreset.FaceNoPores)
                {
                    args.Add(ShaderConstantId.SkinTileMaterial, new List<float>() { 0 });
                    args.Add(ShaderConstantId.SkinTileMultiplier, new List<float>() { 0, 0 });

                } else
                {
                    args.Add(ShaderConstantId.SkinTileMaterial, null);
                    args.Add(ShaderConstantId.SkinTileMultiplier, null);
                }

                if (info.Preset == MtrlShaderPreset.Face || info.Preset == MtrlShaderPreset.FaceNoPores)
                {
                    args.Add(ShaderConstantId.Face1, null);
                }
            }
            else if (info.Shader == MtrlShader.Standard)
            {
                if (info.Preset == MtrlShaderPreset.Monster)
                {
                    args.Remove(ShaderConstantId.AlphaLimiter);
                    args.Remove(ShaderConstantId.Occlusion);
                    args.Add(ShaderConstantId.AlphaLimiter, new List<float>() { 0.5f });
                    args.Add(ShaderConstantId.Occlusion, new List<float>() { 0.25f });
                    args.Add(ShaderConstantId.Hair1, new List<float>() { 0 });
                }
                else
                {
                    args.Add(ShaderConstantId.Equipment1, null);
                    args.Add(ShaderConstantId.Reflection1, null);
                }
            }
            else if (info.Shader == MtrlShader.Iris)
            {
                args.Add(ShaderConstantId.Equipment1, null);
                args.Add(ShaderConstantId.Reflection1, null);
                args.Add(ShaderConstantId.SkinFresnel, null);
                args.Add(ShaderConstantId.SkinColor, null);
                args.Add(ShaderConstantId.SkinWetnessLerp, null);
            }
            else if(info.Shader == MtrlShader.Hair)
            {
                args.Add(ShaderConstantId.Equipment1, null);
                args.Add(ShaderConstantId.Reflection1, null);
                args.Add(ShaderConstantId.SkinColor, null);
                args.Add(ShaderConstantId.SkinWetnessLerp, null);
                args.Add(ShaderConstantId.Hair1, null);
                args.Add(ShaderConstantId.Hair2, null);

                if (info.Preset == MtrlShaderPreset.FaceBright)
                {
                    // Limbals use a 3x skin color modifier.
                    args[ShaderConstantId.SkinColor] = new List<float>() { 3f, 3f, 3f };
                }
            }
            else if(info.Shader == MtrlShader.Glass)
            {
                args.Remove(ShaderConstantId.AlphaLimiter);
                args.Remove(ShaderConstantId.Occlusion);
                args.Add(ShaderConstantId.AlphaLimiter, new List<float>() { 0.25f });
                args.Add(ShaderConstantId.Hair2, new List<float>() { 1.0f });
            }


            // Regenerate the list.
            ShaderParameterList.Clear();
            //args.ForEach(x => SetShaderParameter(x));
            foreach(var kv in args)
            {
                // Nulls use defaults.
                SetShaderParameter(kv.Key, kv.Value);
            }
        }


        /// <summary>
        /// Gets a given shader Parameter by ID.
        /// </summary>
        /// <param name="parameterId"></param>
        /// <returns></returns>
        private ShaderConstants GetShaderParameter(ShaderConstantId parameterId)
        {
            return ShaderParameterList.FirstOrDefault(x => x.ConstantId == parameterId);
        }

        /// <summary>
        /// Sets or adds a given shader parameter.
        /// </summary>
        /// <param name="parameterId"></param>
        /// <param name="data"></param>
        private void SetShaderParameter(ShaderConstantId parameterId, List<float> data = null)
        {
            try
            {
                var value = ShaderParameterList.First(x => x.ConstantId == parameterId);

                // Only overwrite if we were given explicit data.
                if (value != null && data != null)
                {
                    value.Constants = data;
                }
            }
            catch (Exception ex)
            {
                if (data == null)
                {
                    data = Mtrl.ShaderParameterDefaults[parameterId];
                }

                ShaderParameterList.Add(new ShaderConstants
                {
                    ConstantId = parameterId,
                    Constants = data
                });
            }
        }

        /// <summary>
        /// Removes a given shader parameter.
        /// </summary>
        /// <param name="parameterId"></param>
        private void ClearShaderParameter(ShaderConstantId parameterId)
        {
            ShaderParameterList = ShaderParameterList.Where(x => x.ConstantId != parameterId).ToList();
        }


        /// <summary>
        /// Retrieves a given shader technique
        /// </summary>
        /// <param name="usage"></param>
        /// <returns></returns>
        private ShaderTechniques GetShaderTechnique(ShaderTechniqueId  techniqueId)
        {
            return ShaderTechniqueList.FirstOrDefault(x => x.TechniqueId == techniqueId);
        }

        /// <summary>
        /// Removes a given shader technique setting.
        /// </summary>
        /// <param name="usage"></param>
        private bool ClearShaderTechnique(ShaderTechniqueId techniqueId)
        {
            var oldCount = ShaderTechniqueList.Count;
            ShaderTechniqueList = ShaderTechniqueList.Where(x => x.TechniqueId != techniqueId).ToList();
            return oldCount != ShaderTechniqueList.Count;
        }

        /// <summary>
        /// Adds or changes a shader technique value
        /// </summary>
        /// <param name="usage"></param>
        /// <param name="unknownValue"></param>
        private void SetShaderTechnique(ShaderTechniqueId techniqueId, uint? value = null)
        {
            if(value == null)
            {
                value = Mtrl.ShaderTechniqueDefaults[techniqueId].Value;
            }
            try
            {
                var val = ShaderTechniqueList.First(x => x.TechniqueId == techniqueId);
                if(val != null)
                {
                    val.Value = (uint)value;
                } else
                {
                    ShaderTechniqueList.Add(new ShaderTechniques()
                    {
                        TechniqueId = techniqueId,
                        Value = (uint)value
                    });
                }
            } catch(Exception ex)
            {
                ShaderTechniqueList.Add(new ShaderTechniques()
                {
                    TechniqueId = techniqueId,
                    Value = (uint)value
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
            for(var i = 0; i < TexturePathList.Count; i++)
            {
                var info = new MapInfo();
                info.Path = TexturePathList[i];
                info.Usage = XivTexType.Other;

                // Check if the texture appears in the parameter list.
                foreach(var p in TextureSamplerSettingsList)
                {
                    if(p.TextureIndex == i)
                    {
                        // This is a known parameter.
                        if(SamplerToTexType.ContainsKey(p.SamplerId))
                        {
                            info.Usage = SamplerToTexType[p.SamplerId];
                        } else 
                        {
                            info.Usage = XivTexType.Other;
                        }

                        break;
                    }
                }

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
            if (ColorSetCount > 0 && ColorSetData.Count > 0)
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


        /// <summary>
        /// This is the default we assign for each tex type.  May not be the correct type in the case of furniture or custom materials.
        /// </summary>
        public static Dictionary<XivTexType, MtrlSamplerId> TexTypeToSamplerDefault = new Dictionary<XivTexType, MtrlSamplerId>() {
            { XivTexType.Normal, MtrlSamplerId.Normal },
            { XivTexType.Specular, MtrlSamplerId.Specular },
            { XivTexType.Diffuse, MtrlSamplerId.Diffuse },
            { XivTexType.Multi, MtrlSamplerId.Multi },
            { XivTexType.Reflection, MtrlSamplerId.Catchlight },
        };

        public static Dictionary<MtrlSamplerId, XivTexType> SamplerToTexType = new Dictionary<MtrlSamplerId, XivTexType>() {
            { MtrlSamplerId.Normal, XivTexType.Normal },
            { MtrlSamplerId.FurnishingNormal, XivTexType.Normal },
            { MtrlSamplerId.Specular, XivTexType.Specular },
            { MtrlSamplerId.FurnishingSpecular, XivTexType.Specular },
            { MtrlSamplerId.Diffuse, XivTexType.Diffuse },
            { MtrlSamplerId.FurnishingDiffuse, XivTexType.Diffuse },
            { MtrlSamplerId.Multi, XivTexType.Multi },
            { MtrlSamplerId.Catchlight, XivTexType.Reflection },
            { MtrlSamplerId.Reflection, XivTexType.Reflection },
        };
    }

    /// <summary>
    /// These control the shader passes/major shader features used by this material.
    /// </summary>
    public class ShaderTechniques
    {
        // The ID for this type of input.
        public ShaderTechniqueId TechniqueId; 

        // Some kind of modifying value.
        public uint Value;
    }

    /// <summary>
    /// These are constants supplied to the shaders
    /// </summary>
    public class ShaderConstants
    {
        public ShaderConstantId ConstantId;

        public ushort Offset;

        public ushort Size;

        public List<float> Constants;
    }

    /// <summary>
    /// These control how each texture is sampled/piped into the shaders.
    /// </summary>
    public class TextureSamplerSettings
    {
        // Which texture sampler to use.
        public MtrlSamplerId SamplerId;

        // Flags, primarily seems to specify Colorset usage, or at least that's the only known ones currently.
        public ushort SamplerSettings; 

        // Unknown flags, seems to always be [15]
        public ushort Flags;   

        // Index to the texture to be used in the material's texture list.
        public uint TextureIndex;
    }

    // Enums representing common channel descriptor formats used by the game.
    public enum TextureSamplerSettingsPresets : ushort
    {
        Other = 0,
        UsesColorset = 32768,
        NoColorset = 33600
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
        DyeableFurniture,   // bgcolorchange.shpk 
        Other               // Unknown Shader
    }

    // These are the custom presets TexTools has set up.
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
    /// Enums for the various shader constants.  These likely are used to pipe extra data from elsewhere into the shader.
    /// Ex. Local Reflection Maps, Character Skin/Hair Color, Dye Color, etc.
    /// </summary>
    public enum ShaderConstantId : uint
    {
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


    /// <summary>
    /// Known valid texture samplers.
    /// </summary>
    public enum MtrlSamplerId : uint
    {
        Basic1 = 0x88408C04,
        Basic2 = 0x213CB439,
        Basic3 = 0x563B84AF,
        Catchlight = 0xFEA0F3D2,
        FurnishingDiffuse = 0x1E6FEF9C,
        ColorMap2 = 0x6968DF0A,
        Diffuse = 0x115306BE,
        EnvMap = 0xF8D7957A,
        Multi = 0x8A4E82B6,
        Normal = 0x0C5EC1F1,
        FurnishingNormal = 0xAAB4D9E9,
        NormalMap2 = 0xDDB3E97F,
        Reflection = 0x87F6474D,
        Specular = 0x2B99E025,
        FurnishingSpecular = 0x1BBC2F12,
        SpecularMap2 = 0x6CBB1F84,
        WaveMap = 0xE6321AFC,
        WaveletMap1 = 0x574E22D6,
        WaveletMap2 = 0x20491240,
        WhitecapMap = 0x95E1F64D
    }

    public enum ShaderTechniqueId : uint
    {
        // Used always(?)
        Common = 4113354501,

        // Pipes Equipment Decal data in.
        Decal = 3531043187,

        //???
        Diffuse = 3054951514,
        
        // Converts Specular inputs to Multi inputs (only used on monster items?)
        SpecToMulti = 3367837167,

        // Something to do with skin color.
        // Alternative sub/flag value for this switches between normal skin usage hrothgar style skin (hair color piped in)
        Skin = 940355280,

        // Converts Hair Highlight color to Tattoo color (used for facial hair/"etc" textures)
        HighlightsToTattoo = 612525193,
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
        public string Path;


        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }


}