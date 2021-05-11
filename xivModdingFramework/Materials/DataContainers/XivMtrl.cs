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
    public class XivMtrl : ICloneable
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
        public ushort ColorSetDataSize
        {
            get
            {
                var size = ColorSetData.Count * 2;
                size += ColorSetDyeData == null ? 0 : ColorSetDyeData.Length;
                return (ushort)size;
            }
        }

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
        public List<short> TextureDxSettingsList { get; set; }

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
        public ushort ShaderConstantsDataSize
        {
            get
            {
                var size = 0;
                ShaderConstantList.ForEach(x =>
                {
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
        public ushort ShaderTechniqueCount
        {
            get
            {
                return (ushort)ShaderTechniqueList.Count;
            }
            set
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
            get { return (ushort)ShaderConstantList.Count; }
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
        public List<ShaderTechnique> ShaderTechniqueList { get; set; }

        /// <summary>
        /// The list of Type 2 data structures
        /// </summary>
        public List<ShaderConstant> ShaderConstantList { get; set; }

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
        

        // Performs a proper deep copy clone of this material.
        public object Clone()
        {
            var obj = this.MemberwiseClone();
            var castedObj = (XivMtrl)obj;

            // Clone all the primitive lists/arrays.
            castedObj.TexturePathOffsetList = TexturePathOffsetList.Select(item => item).ToList();
            castedObj.TextureDxSettingsList = TextureDxSettingsList.Select(item => item).ToList();
            castedObj.MapPathOffsetList = MapPathOffsetList.Select(item => item).ToList();
            castedObj.MapPathDxList = MapPathDxList.Select(item => item).ToList();
            castedObj.ColorSetPathOffsetList = ColorSetPathOffsetList.Select(item => item).ToList();
            castedObj.ColorSetPathUnknownList = ColorSetPathUnknownList.Select(item => item).ToList();
            castedObj.TexturePathList = TexturePathList.Select(item => item).ToList();
            castedObj.MapPathList = MapPathList.Select(item => item).ToList();
            castedObj.ColorSetPathList = ColorSetPathList.Select(item => item).ToList();
            castedObj.DxFlags = (byte[]) DxFlags.Clone();
            castedObj.ColorSetData = ColorSetData.Select(item => item).ToList();
            castedObj.ColorSetDyeData = (byte[])ColorSetDyeData.Clone();


            // Clone all the object lists
            castedObj.ShaderTechniqueList = ShaderTechniqueList.Select(item => (ShaderTechnique) item.Clone()).ToList();
            castedObj.ShaderConstantList = ShaderConstantList.Select(item => (ShaderConstant)item.Clone()).ToList();
            castedObj.TextureSamplerSettingsList = TextureSamplerSettingsList.Select(item => (TextureSamplerSettings)item.Clone()).ToList();

            return castedObj;
        }

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
                    info.Shader = MtrlShader.Character;
                    break;
                case "characterglass.shpk":
                    info.Shader = MtrlShader.CharacterGlass;
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
                    info.Shader = MtrlShader.Bg;
                    break;
                case "bgcolorchange.shpk":
                    info.Shader = MtrlShader.BgColorChange;
                    break;
                default:
                    info.Shader = MtrlShader.Other;
                    break;
            }

            // Check the transparency bit.
            const ushort transparencyBit = 16;
            const ushort backfaceBit = 1;
            var bit = (ushort)(RenderFlags & transparencyBit);

            if (bit > 0)
            {
                info.TransparencyEnabled = true;
            }
            else
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
            var shpl = ShaderConstantList;

            info.Preset = MtrlShaderPreset.Default;
            if (info.Shader == MtrlShader.Character)
            {
                bool hasSpec = GetMapInfo(XivTexType.Specular) != null;
                bool hasDiffuse = GetMapInfo(XivTexType.Diffuse) != null;
                bool hasMulti = GetMapInfo(XivTexType.Multi) != null;

                if (hasDiffuse)
                {
                    if (hasSpec)
                    {
                        if (GetShaderTechnique(ShaderTechniqueId.SpecToMulti) != null)
                        {
                            info.Preset = MtrlShaderPreset.Monster;
                        }
                        else
                        {
                            info.Preset = MtrlShaderPreset.DiffuseSpecular;
                        }
                    }
                    else
                    {
                        info.Preset = MtrlShaderPreset.DiffuseMulti;
                    }
                }
                else
                {
                    info.Preset = MtrlShaderPreset.Default;
                }
            }
            else if (info.Shader == MtrlShader.Skin)
            {
                if (GetShaderTechnique(ShaderTechniqueId.Skin) == null)
                {
                    info.Preset = MtrlShaderPreset.Face;
                }

                if (GetShaderTechnique(ShaderTechniqueId.Skin) != null && GetShaderTechnique(ShaderTechniqueId.Skin).Value == 1476344676)
                {
                    info.Preset = MtrlShaderPreset.BodyWithHair;
                }
                else if (ShaderConstantList.Any(x => x.ConstantId == ShaderConstantId.SkinTileMaterial))
                {
                    var param = ShaderConstantList.First(x => x.ConstantId == ShaderConstantId.SkinTileMaterial);
                    if (param.Constants[0] == 0)
                    {
                        if (info.Preset == MtrlShaderPreset.Face)
                        {
                            info.Preset = MtrlShaderPreset.FaceNoPores;

                        }
                        else
                        {
                            info.Preset = MtrlShaderPreset.BodyNoPores;
                        }
                    }
                }
            }
            else if (info.Shader == MtrlShader.Hair)
            {
                if (GetShaderTechnique(ShaderTechniqueId.HighlightsToTattoo) != null)
                {
                    info.Preset = MtrlShaderPreset.Face;
                    var mul = 1.4f;
                    var param = ShaderConstantList.FirstOrDefault(x => x.ConstantId == ShaderConstantId.SkinColor);
                    if (param != null)
                    {
                        mul = param.Constants[0];
                    }

                    // Limbal ring faces (Au Ra) use a 3.0 multiplier for brightness.
                    if (mul == 3.0)
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

            for (var idx = 0; idx < TextureDxSettingsList.Count; idx++)
            {
                TextureDxSettingsList[idx] = 0;
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
                case MtrlShader.Character:
                    ShaderPack = "character.shpk";
                    break;
                case MtrlShader.CharacterGlass:
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
                case MtrlShader.Bg:
                    ShaderPack = "bg.shpk";
                    break;
                case MtrlShader.BgColorChange:
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
            if (!info.HasColorset)
            {
                // ColorSetCount seems to always be 1, even when the data is empty.
                ColorSetCount = 1;
                ColorSetData = new List<Half>();
                ColorSetDyeData = null;
            }
            else
            {
                if (ColorSetCount == 0 || ColorSetData == null || ColorSetData.Count != 256)
                {
                    // Get default Colorset Data.
                    ColorSetData = Tex.GetColorsetDataFromDDS(Tex.GetDefaultTexturePath(XivTexType.ColorSet));
                }
                if (ColorSetDyeData == null || ColorSetDyeData.Length != 32)
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

            info.Usage = MapType;

            // Look for the right map type in the parameter list.
            foreach (var samplerInfo in TextureSamplerSettingsList)
            {
                if (SamplerToTexType.ContainsKey(samplerInfo.SamplerId) && SamplerToTexType[samplerInfo.SamplerId] == MapType)
                {
                    info.Path = samplerInfo.TexturePath;
                    info.Sampler = samplerInfo;
                }
            }

            if (info.Sampler == null)
            {
                return null;
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
            for (var i = 0; i < TexturePathList.Count; i++)
            {
                if (TexturePathList[i] == path)
                {
                    idx = i;
                    break;
                }
            }
            if (idx == -1)
            {
                return null;
            }

            var info = new MapInfo();
            info.Path = path;

            foreach (var samplerInfo in TextureSamplerSettingsList)
            {
                // Found the descriptor.
                if (samplerInfo.TexturePath == path)
                {
                    // If we know what this texture descriptor actually means
                    if (SamplerToTexType.ContainsKey(samplerInfo.SamplerId))
                    {
                        info.Usage = SamplerToTexType[samplerInfo.SamplerId];
                    }
                    else
                    {
                        info.Usage = XivTexType.Other;
                    }
                    info.Sampler = samplerInfo;
                }
            }

            info.Path = TokenizePath(info.Path, info.Usage);

            return info;
        }

        /// <summary>
        /// Sets or deletes the texture information from the Mtrl based on the
        /// incoming usage and info.
        /// 
        /// A null MapType creates a map based off the sampler info in the MapInfo object.
        /// </summary>
        /// <param name="MapType"></param>
        /// <param name="info"></param>
        public void SetMapInfo(XivTexType? MapType, MapInfo info)
        {
            // Sanity check.
            if (info != null && info.Usage != MapType)
            {
                throw new System.Exception("Invalid attempt to reassign map materials.");
            }

            if (MapType == null && info.Sampler == null)
            {
                throw new InvalidDataException("Either MapType or SamplerInfo must be provided to set material texture map info.");
            }

            var paramIdx = -1;
            TextureSamplerSettings oldInfo = new TextureSamplerSettings();

            if (MapType != null)
            {
                // Look for the right map type in the parameter list.
                for (var i = 0; i < TextureSamplerSettingsList.Count; i++)
                {
                    var samplerInfo = TextureSamplerSettingsList[i];

                    if (SamplerToTexType.ContainsKey(samplerInfo.SamplerId) && SamplerToTexType[samplerInfo.SamplerId] == MapType)
                    {
                        paramIdx = i;
                        oldInfo = samplerInfo;
                        break;
                    }
                }
            }


            // Deleting existing info.
            if (paramIdx >= 0)
            {
                var texIdx = oldInfo.GetTextureIndex(this);
                // Remove texture from path list if it exists.
                if (texIdx >= 0)
                {
                    TexturePathList.RemoveAt(texIdx);
                    TextureDxSettingsList.RemoveAt(texIdx);
                }

                // Remove Parameter List
                TextureSamplerSettingsList = TextureSamplerSettingsList.Where(x => x.TexturePath != oldInfo.TexturePath).ToList();
            }

            if (info == null)
            {
                // If we just wanted to delete, and nothing else, end here.
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
            }
            catch
            {
                // Prefix the item's personal path onto it.
                info.Path = ItemPathToken + "/" + info.Path;
                info.Path = DetokenizePath(info.Path, info.Usage);
            }

            // Ensure .tex or .atex ending for sanity.
            var match = Regex.Match(info.Path, "\\.a?tex$");
            if (!match.Success)
            {
                info.Path = info.Path += ".tex";
            }

            TextureSamplerSettings newInfo;
            if (info.Sampler == null)
            {
                // If no sampler was supplied, use the defaults.
                newInfo = new TextureSamplerSettings();
                newInfo.SamplerId = TexTypeToSamplerDefault[info.Usage];
                newInfo.Flags = 15;
            } else
            {
                // If an explicit sampler was provided, use that.
                newInfo = (TextureSamplerSettings) info.Clone();
            }

            // Update sampler to use the top level path supplied in case of conflicts.
            newInfo.TexturePath = info.Path;

            // TODO Could use some better handling here.
            if (MapType == XivTexType.Normal)
            {
                newInfo.SamplerSettings = (ushort)TextureSamplerSettingsPresets.UsesColorset;
            }
            else
            {
                newInfo.SamplerSettings = (ushort)TextureSamplerSettingsPresets.NoColorset;
            }


            // Inject the new parameters.
            TextureSamplerSettingsList.Add(newInfo);

            // Inject the new string if needed
            var newTexIdx = newInfo.GetTextureIndex(this);
            if (newTexIdx < 0)
            {
                TexturePathList.Add(info.Path);
                TextureDxSettingsList.Add((short)0); // This value seems to always be 0 for textures.
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
            if (info.Shader == MtrlShader.Bg || info.Shader == MtrlShader.BgColorChange || info.Shader == MtrlShader.Iris || info.Shader == MtrlShader.Hair)
            {
                if (info.Shader == MtrlShader.Hair && info.Preset != MtrlShaderPreset.Default)
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
                    SetShaderTechnique(ShaderTechniqueId.CharacterCommon);
                }
                else if (info.Preset == MtrlShaderPreset.BodyWithHair)
                {
                    SetShaderTechnique(ShaderTechniqueId.Skin, 1476344676);
                }
                else
                {
                    // Non-Face Skin textures use a single custom usage value.
                    SetShaderTechnique(ShaderTechniqueId.Skin);
                }

            }
            else if (info.Shader == MtrlShader.Character)
            {
                SetShaderTechnique(ShaderTechniqueId.CharacterCommon);
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
                SetShaderTechnique(ShaderTechniqueId.CharacterCommon);
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

                if (info.Preset == MtrlShaderPreset.BodyNoPores || info.Preset == MtrlShaderPreset.FaceNoPores)
                {
                    args.Add(ShaderConstantId.SkinTileMaterial, new List<float>() { 0 });
                    args.Add(ShaderConstantId.SkinTileMultiplier, new List<float>() { 0, 0 });

                }
                else
                {
                    args.Add(ShaderConstantId.SkinTileMaterial, null);
                    args.Add(ShaderConstantId.SkinTileMultiplier, null);
                }

                if (info.Preset == MtrlShaderPreset.Face || info.Preset == MtrlShaderPreset.FaceNoPores)
                {
                    args.Add(ShaderConstantId.Face1, null);
                }
            }
            else if (info.Shader == MtrlShader.Character)
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
            else if (info.Shader == MtrlShader.Hair)
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
            else if (info.Shader == MtrlShader.CharacterGlass)
            {
                args.Remove(ShaderConstantId.AlphaLimiter);
                args.Remove(ShaderConstantId.Occlusion);
                args.Add(ShaderConstantId.AlphaLimiter, new List<float>() { 0.25f });
                args.Add(ShaderConstantId.Hair2, new List<float>() { 1.0f });
            }


            // Regenerate the list.
            ShaderConstantList.Clear();
            //args.ForEach(x => SetShaderParameter(x));
            foreach (var kv in args)
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
        private ShaderConstant GetShaderParameter(ShaderConstantId parameterId)
        {
            return ShaderConstantList.FirstOrDefault(x => x.ConstantId == parameterId);
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
                var value = ShaderConstantList.First(x => x.ConstantId == parameterId);

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

                ShaderConstantList.Add(new ShaderConstant
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
            ShaderConstantList = ShaderConstantList.Where(x => x.ConstantId != parameterId).ToList();
        }


        /// <summary>
        /// Retrieves a given shader technique
        /// </summary>
        /// <param name="usage"></param>
        /// <returns></returns>
        private ShaderTechnique GetShaderTechnique(ShaderTechniqueId techniqueId)
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
            if (value == null)
            {
                value = Mtrl.ShaderTechniqueDefaults[techniqueId].Value;
            }
            try
            {
                var val = ShaderTechniqueList.First(x => x.TechniqueId == techniqueId);
                if (val != null)
                {
                    val.Value = (uint)value;
                }
                else
                {
                    ShaderTechniqueList.Add(new ShaderTechnique()
                    {
                        TechniqueId = techniqueId,
                        Value = (uint)value
                    });
                }
            }
            catch (Exception ex)
            {
                ShaderTechniqueList.Add(new ShaderTechnique()
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
            for (var i = 0; i < TexturePathList.Count; i++)
            {
                var info = new MapInfo();
                info.Path = TexturePathList[i];
                info.Usage = XivTexType.Other;

                // Find the texture's sampler.
                foreach (var p in TextureSamplerSettingsList)
                {
                    if (p.TexturePath == info.Path)
                    {
                        // This is a known parameter.
                        if (SamplerToTexType.ContainsKey(p.SamplerId))
                        {
                            info.Usage = SamplerToTexType[p.SamplerId];
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

                }
                else if (shaderInfo.Shader == MtrlShader.Bg && map.Path.Contains("dummy"))
                {
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
                if (match.Success)
                {
                    root = match.Groups[1].Value + "texture";
                }
            }
            else
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
            else
            {
                ret += "unknown";
            }

            ret += GetItemTypeIdentifier();

            // Followed by the material identifier, if above [a].
            var identifier = GetMaterialIdentifier();
            if (identifier != 'a')
            {
                ret += "_" + identifier;
            }



            if (texType == XivTexType.Normal)
            {
                ret += "_n";
            }
            else if (texType == XivTexType.Specular)
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
            }
            else
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
            if (match.Success)
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
            }
            else if (MTRLPath.Contains("/obj/tail/t"))
            {
                // Tails have their textures (but not materials) listed as _etc parts.
                return "_etc";
            }
            return "";
        }

        public XivRace GetRace()
        {
            var races = Enum.GetValues(typeof(XivRace)).Cast<XivRace>();
            foreach (var race in races)
            {
                // Test the root path for the racial identifier.
                var match = Regex.Match(MTRLPath, "c" + race.GetRaceCode());
                if (match.Success)
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
            { MtrlSamplerId.NormalMap0, XivTexType.Normal },
            { MtrlSamplerId.Specular, XivTexType.Specular },
            { MtrlSamplerId.SpecularMap0, XivTexType.Specular },
            { MtrlSamplerId.Diffuse, XivTexType.Diffuse },
            { MtrlSamplerId.ColorMap0, XivTexType.Diffuse },
            { MtrlSamplerId.Multi, XivTexType.Multi },
            { MtrlSamplerId.Catchlight, XivTexType.Reflection },
            { MtrlSamplerId.Reflection, XivTexType.Reflection },
        };


        // These values extracted by Aers, and should cover the entire universe of currently used values as of patch 5.5 Global -Sel
        // Each of these values is some kind of CRC32 hashed string.
        // Default values are always index[0] in their resepective lists.
        public static Dictionary<ShaderTechniqueId, List<uint>> AvailableValuesByTechnique = new Dictionary<ShaderTechniqueId, List<uint>>() {
            { ShaderTechniqueId.CharacterCommon, new List<uint>() { 0xDFE74BAC, 0xA7D2FF60 } },
            { ShaderTechniqueId.Decal, new List<uint>() { 0xF35F5131, 0x7C6FA05B, 0xBD94649A } },
            { ShaderTechniqueId.Bg1, new List<uint>() { 0x7C6FA05B, 0xBD94649A } },
            { ShaderTechniqueId.BgAnimated1, new List<uint>() { 0x5D146A23, 0x72AAA9AE } },
            { ShaderTechniqueId.Lighting, new List<uint>() { 0x470E5A1E, 0x2807B89E } }, //LightingNormal and LightingLow
            { ShaderTechniqueId.Diffuse, new List<uint>() { 0x5CC605B5, 0x600EF9DF, 0x22A4AABF, 0x669A451B, 0x1DF2985C, 0x941820BE, 0xE49AD72B } },
            { ShaderTechniqueId.SpecToMulti, new List<uint>() { 0x198D11CD, 0xA02F4828 } },
            { ShaderTechniqueId.Skin, new List<uint>() { 0xF5673524, 0x2BDB45F1, 0x57FF3B64 } },
            { ShaderTechniqueId.HighlightsToTattoo, new List<uint>() { 0xF7B8956E, 0x6E5B8F10 } },
            { ShaderTechniqueId.Lightshaft, new List<uint>() { 0xB1064103, 0xC6017195 } },
            { ShaderTechniqueId.River1, new List<uint>() { 0x32F05363, 0x26E40878 } },
            { ShaderTechniqueId.River2, new List<uint>() { 0x86B217C3, 0x08404EC3, 0xE6A6AD27 } },
            { ShaderTechniqueId.Water1, new List<uint>() { 0x28981633, 0xDD54E76C } },
            { ShaderTechniqueId.Water2, new List<uint>() { 0x0EC4134E, 0x1140F45C } },
            { ShaderTechniqueId.Water3, new List<uint>() { 0x4B740B02, 0x824D5B42 } }

        };

        /// <summary>
        ///  Mapping of the few Shader Technique values we actually have some kind of name/reference for.
        /// </summary>
        public static Dictionary<uint, string> ShaderTechniqueValueNames = new Dictionary<uint, string>()
        {
            { 0xDFE74BAC, "CharacterCommon Default" },
            { 0xF35F5131, "Decal Default" },
            { 0x7C6FA05B, "Bg1 Default" },
            { 0x5D146A23, "BgAnimated1 Default" },

            { 0x470E5A1E, "LightingNormal" },
            { 0x2807B89E, "LightingLow" },

            { 0x5CC605B5, "Diffuse Default" },
            { 0x669A451B, "Diffuse BG Default" },

            { 0x198D11CD, "SpecToMulti Default" },
            
            { 0xF5673524, "Skin Default" },
            { 0x57FF3B64, "Hrothgar Skin" },

            { 0xF7B8956E, "HighlightsToTattoo Default" },
            { 0xB1064103, "Lightshaft Default" },
            { 0x32F05363, "River1 Default" },
            { 0x86B217C3, "River2 Default" },
            { 0x28981633, "Water1 Default" },
            { 0x0EC4134E, "Water2 Default" },
            { 0x4B740B02, "Water3 Default" },
        };


        public static Dictionary<MtrlShader, List<ShaderTechniqueId>> AvailableTechniquesByShader = new Dictionary<MtrlShader, List<ShaderTechniqueId>>()
        {
            { MtrlShader.Character, new List<ShaderTechniqueId>() { ShaderTechniqueId.CharacterCommon, ShaderTechniqueId.Decal, ShaderTechniqueId.Diffuse, ShaderTechniqueId.SpecToMulti } },
            { MtrlShader.CharacterGlass, new List<ShaderTechniqueId>() { ShaderTechniqueId.CharacterCommon, } },
            { MtrlShader.Skin, new List<ShaderTechniqueId>() { ShaderTechniqueId.CharacterCommon, ShaderTechniqueId.Skin } },
            { MtrlShader.Hair, new List<ShaderTechniqueId>() { ShaderTechniqueId.CharacterCommon, ShaderTechniqueId.HighlightsToTattoo } },
            { MtrlShader.Iris, new List<ShaderTechniqueId>() { ShaderTechniqueId.CharacterCommon, } },
            { MtrlShader.Bg, new List<ShaderTechniqueId>() { ShaderTechniqueId.Bg1, ShaderTechniqueId.BgAnimated1, ShaderTechniqueId.Lighting, ShaderTechniqueId.Diffuse } },
            { MtrlShader.BgColorChange, new List<ShaderTechniqueId>() { ShaderTechniqueId.Bg1, ShaderTechniqueId.BgAnimated1, ShaderTechniqueId.Lighting, ShaderTechniqueId.Diffuse } },
            { MtrlShader.BgUvScroll, new List<ShaderTechniqueId>() { ShaderTechniqueId.BgAnimated1, ShaderTechniqueId.Lighting, ShaderTechniqueId.Diffuse } },
            { MtrlShader.BgDecal, new List<ShaderTechniqueId>() { ShaderTechniqueId.Lighting, } },
            { MtrlShader.Water, new List<ShaderTechniqueId>() { ShaderTechniqueId.Water1, ShaderTechniqueId.Water2, ShaderTechniqueId.Water3, ShaderTechniqueId.River2 } },
            { MtrlShader.River, new List<ShaderTechniqueId>() { ShaderTechniqueId.River1, ShaderTechniqueId.River2 } },
            { MtrlShader.Lightshaft, new List<ShaderTechniqueId>() { ShaderTechniqueId.Lightshaft } },
            { MtrlShader.Other, new List<ShaderTechniqueId>() { } }
        };
        /*
            None = 0,
            Sampler = 0x88408C04,
            Sampler1 = 0x213CB439,
            Sampler2 = 0x563B84AF,
            Catchlight = 0xFEA0F3D2,
            ColorMap0 = 0x1E6FEF9C, // Furnishing Sampler
            ColorMap1 = 0x6968DF0A, // Furnishing Sampler
            Diffuse = 0x115306BE,
            EnvMap = 0xF8D7957A,
            Multi = 0x8A4E82B6,
            Normal = 0x0C5EC1F1,
            FurnishingNormal = 0xAAB4D9E9,
            NormalMap2 = 0xDDB3E97F,
            Reflection = 0x87F6474D,
            Specular = 0x2B99E025,
            SpecularMap0 = 0x1BBC2F12,  // Furnishing Sampler
            SpecularMap1 = 0x6CBB1F84,  // Furnishing Sampler
            WaveMap = 0xE6321AFC,
            WaveletMap1 = 0x574E22D6,
            WaveletMap2 = 0x20491240,
            WhitecapMap = 0x95E1F64D,
            LightDiffuse = 0x23D0F850,
            LightSpecular = 0x6C19ACA4,
            GBuffer = 0xEBBB29BD,
            Occlusion = 0x32667BD7,
            Dither = 0x9F467267,
            Decal = 0x0237CB94,
            Shadow = 0x58AD2B38,
            Caustics = 0x0EFB24F7*/

        /* 
         
        Character,           // character.shpk
        Glass,              // characterglass.shpk
        Skin,               // skin.shpk
        Hair,               // hair.shpk
        Iris,               // iris.shpk
        Furniture,          // bg.shpk
        DyeableFurniture,   // bgcolorchange.shpk 
        BgScroll,           // bguvscroll.shpk
        BgDecal,            // bgdecal.shpk
        Water,              // water.shpk
        River,              // river.shpk
        Lightshaft,         // lightshaft.shpk
        Other               // Unknown Shader
        */
        public static Dictionary<MtrlShader, List<MtrlSamplerId>> AvailableSamplersByShader = new Dictionary<MtrlShader, List<MtrlSamplerId>>()
        {
            { MtrlShader.Character, new List<MtrlSamplerId>() { MtrlSamplerId.Normal, MtrlSamplerId.TileNormal, MtrlSamplerId.LightDiffuse, MtrlSamplerId.LightSpecular, MtrlSamplerId.Reflection, MtrlSamplerId.Occlusion, MtrlSamplerId.Diffuse, MtrlSamplerId.TileDiffuse, MtrlSamplerId.Dither, MtrlSamplerId.Specular, MtrlSamplerId.Decal, MtrlSamplerId.Multi } },
            { MtrlShader.CharacterGlass, new List<MtrlSamplerId>() { MtrlSamplerId.Normal, MtrlSamplerId.Multi, MtrlSamplerId.Reflection, MtrlSamplerId.Dither } },
            { MtrlShader.Skin, new List<MtrlSamplerId>() { MtrlSamplerId.LightDiffuse, MtrlSamplerId.LightSpecular, MtrlSamplerId.TileNormal, MtrlSamplerId.TileDiffuse, MtrlSamplerId.Occlusion, MtrlSamplerId.Dither, MtrlSamplerId.Normal, MtrlSamplerId.Multi } },
            { MtrlShader.Hair, new List<MtrlSamplerId>() { MtrlSamplerId.Normal, MtrlSamplerId.Multi, MtrlSamplerId.LightDiffuse, MtrlSamplerId.LightSpecular, MtrlSamplerId.Occlusion, MtrlSamplerId.Decal, MtrlSamplerId.Dither, MtrlSamplerId.Reflection  } },
            { MtrlShader.Iris, new List<MtrlSamplerId>() { MtrlSamplerId.Normal, MtrlSamplerId.LightDiffuse, MtrlSamplerId.LightSpecular, MtrlSamplerId.Multi, MtrlSamplerId.Catchlight, MtrlSamplerId.Occlusion, MtrlSamplerId.Reflection, MtrlSamplerId.Dither } },
            { MtrlShader.Bg, new List<MtrlSamplerId>() { MtrlSamplerId.ColorMap0, MtrlSamplerId.SpecularMap0, MtrlSamplerId.Occlusion, MtrlSamplerId.Fresnel, MtrlSamplerId.LightDiffuse, MtrlSamplerId.LightSpecular, MtrlSamplerId.Dither, MtrlSamplerId.NormalMap0, MtrlSamplerId.NormalMap1, MtrlSamplerId.ColorMap1, MtrlSamplerId.SpecularMap1 } },
            { MtrlShader.BgColorChange, new List<MtrlSamplerId>() { MtrlSamplerId.ColorMap0, MtrlSamplerId.NormalMap0, MtrlSamplerId.SpecularMap0, MtrlSamplerId.Dither } },
            { MtrlShader.BgUvScroll, new List<MtrlSamplerId>() { MtrlSamplerId.ColorMap0, MtrlSamplerId.NormalMap0, MtrlSamplerId.SpecularMap0, MtrlSamplerId.Dither, MtrlSamplerId.NormalMap1, MtrlSamplerId.ColorMap1, MtrlSamplerId.SpecularMap1 } },
            { MtrlShader.BgDecal, new List<MtrlSamplerId>() { MtrlSamplerId.NormalMap, MtrlSamplerId.ColorMap, MtrlSamplerId.Occlusion, MtrlSamplerId.Fresnel, MtrlSamplerId.LightDiffuse, MtrlSamplerId.LightSpecular, MtrlSamplerId.Dither } },
            { MtrlShader.Water, new List<MtrlSamplerId>() { } },
            { MtrlShader.River, new List<MtrlSamplerId>() { } },
            { MtrlShader.Lightshaft, new List<MtrlSamplerId>() { } },
            { MtrlShader.Other, new List<MtrlSamplerId>() { } },
        };
    }

    /// <summary>
    /// These control the shader passes/major shader features used by this material.
    /// </summary>
    public class ShaderTechnique : ICloneable
    {
        // The ID for this type of input.
        public ShaderTechniqueId TechniqueId;

        // Some kind of modifying value.
        public uint Value;
        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }

    /// <summary>
    /// These are constants supplied to the shaders
    /// </summary>
    public class ShaderConstant : ICloneable
    {
        public ShaderConstantId ConstantId;

        public List<float> Constants;

        public object Clone()
        {
            var obj = this.MemberwiseClone();

            // Not the most efficient, but works.
            ((ShaderConstant)obj).Constants = Constants.Select(item => item).ToList();
            return obj;
        }
    }

    /// <summary>
    /// These control how each texture is sampled/piped into the shaders.
    /// </summary>
    public class TextureSamplerSettings : ICloneable
    {
        // Which texture sampler to use.
        public MtrlSamplerId SamplerId;

        // Flags, primarily seems to specify Colorset usage, or at least that's the only known ones currently.
        public ushort SamplerSettings;

        // Unknown flags, seems to always be [15]
        public ushort Flags;

        // Texture path this sampler uses.
        public string TexturePath;

        public int GetTextureIndex(XivMtrl mtrl)
        {
            for(int i = 0; i < mtrl.TexturePathList.Count; i++)
            {
                if(mtrl.TexturePathList[i] == TexturePath)
                {
                    return i;
                }
            }
            return -1;
        }
        public object Clone()
        {
            return this.MemberwiseClone();
        }
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
        Character,          // character.shpk
        CharacterGlass,     // characterglass.shpk
        Skin,               // skin.shpk
        Hair,               // hair.shpk
        Iris,               // iris.shpk
        Bg,                 // bg.shpk
        BgColorChange,      // bgcolorchange.shpk 
        BgCrestChange,
        BgDecal,            // bgdecal.shpk
        BgUvScroll,         // bguvscroll.shpk
        Water,              // water.shpk
        River,              // river.shpk
        Lightshaft,         // lightshaft.shpk
        PanelLighting,
        PointLighting,
        SpotLighting,
        DirectionalLighting,
        DirectionalShadow,
        Grass,
        VerticalFog,
        Weather,
        Channeling,
        _3DUI,              // Awkward naming because enums can't start with numbers.  3dui.shpk
        Other               // Unknown Shader
    }

    // Will we actually ever use these? Who knows, but might as well keep the list here.
    public enum ApricotShader
    {
        apricot_decal,
        apricot_decal_dummy,
        apricot_decal_ring,
        apricot_model,
        apricot_model_dummy,
        apricot_morph,
        apricot_powder,
        apricot_powder_dummy,
        apricot_shape,
        apricot_shape_dummy
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
        // Placeholder value for TT menus/etc., there isn't actually a 'none/unused' setting in XIV, the entire sampler
        // block just gets omitted in those cases.
        None = 0,

        // Common item/character samplers.
        Catchlight = 0xFEA0F3D2,
        Diffuse = 0x115306BE,
        EnvMap = 0xF8D7957A,
        Multi = 0x8A4E82B6,
        Fresnel = 0xBA8D7950,
        Normal = 0x0C5EC1F1,
        Reflection = 0x87F6474D,
        Specular = 0x2B99E025,
        LightDiffuse = 0x23D0F850,
        LightSpecular = 0x6C19ACA4,
        Occlusion = 0x32667BD7,
        Dither = 0x9F467267,
        Decal = 0x0237CB94,
        TileNormal = 0x92F03E53,
        TileDiffuse = 0x29156A85,
        RefractionMap = 0xA38E45E1,


        // Probably internal use only samplers.
        GBuffer = 0xEBBB29BD,
        Gbuffer1 = 0xE4E57422, 
        Gbuffer2 = 0x7DEC2598,
        Gbuffer3 = 0x0AEB150E,
        ViewPosition = 0xBC615663,
        Table = 0x2005679F, // Pretty sure this is just a var and not actually a 'sampler', but the name is the same structure as the samplers, soo...
        Index = 0x565F8FD8, // Pretty sure this is just a var and not actually a 'sampler', but the name is the same structure as the samplers, soo...

        // Bg Element Samplers
        ColorMap = 0x6E1DF4A2,
        ColorMap0 = 0x1E6FEF9C,
        ColorMap1 = 0x6968DF0A,
        NormalMap = 0xBE95B65E,
        NormalMap0 = 0xAAB4D9E9,
        NormalMap1 = 0xDDB3E97F,
        SpecularMap = 0xBD8A6965,
        SpecularMap0 = 0x1BBC2F12,  
        SpecularMap1 = 0x6CBB1F84,

        // Oddball Samplers
        Sampler = 0x88408C04,
        Sampler1 = 0x213CB439,
        Sampler2 = 0x563B84AF,
        DistortionMap = 0x180A838E,
        WaveMap = 0xE6321AFC,
        WaveletMap1 = 0x574E22D6,
        WaveletMap2 = 0x20491240,
        WhitecapMap = 0x95E1F64D,
        Shadow = 0x58AD2B38,
        Caustics = 0x0EFB24F7,
        CloudShadow = 0xB821F0D3,

        // Apricot / VFX Samplers
        Depth = 0x2C8FF4B0,
        Sky = 0xB4C285EF,
        Distortion = 0xD7033544,
        ToneMap = 0x592595B8,
        Color1 = 0x77B74A36,
        Color2 = 0xEEBE1B8C,
        Color3 = 0x99B92B1A,
        Color4 = 0x07DDBEB9,
        Palette = 0x781777B1

    }

    public enum ShaderTechniqueId : uint
    {
        // Used always(?)
        // iris, skin, characterglass, hair, character
        CharacterCommon = 0xF52CCF05,

        // Pipes Equipment Decal data in.
        // character
        Decal = 0xD2777173,

        // bg
        Bg1 = 0x4F4F0636,

        // bg, bguvscroll
        BgAnimated1 = 0xA9A3EE25,

        // bgdecal, bg, bguvscroll
        Lighting = 0x575CA84C,


        // Used primarily by Diffuse texture materials?
        // character, bg, bguvscroll
        Diffuse = 0xB616DC5A,

        // Converts Specular inputs to Multi inputs (only used on monster items?)
        // character
        SpecToMulti = 0xC8BD1DEF,

        // Something to do with skin color.
        // Alternative sub/flag value for this switches between normal skin usage hrothgar style skin (hair color piped in)
        // skin
        Skin = 0x380CAED0,

        // Converts Hair Highlight color to Tattoo color (used for facial hair/"etc" textures)
        // hair
        HighlightsToTattoo = 0x24826489,

        // lightshaft
        Lightshaft = 0x0DA8270B,

        // river
        River1 = 0xE041892A,

        // river, water
        River2 = 0xF8EF655E,

        // water
        Water1 = 0x28981633,

        // water
        Water2 = 0xFB7AD5E4,

        // water
        Water3 = 0xB5B1C44A,

    }


    public class ShaderInfo
    {
        public MtrlShader Shader;
        public MtrlShaderPreset Preset;
        public bool TransparencyEnabled;
        public bool RenderBackfaces = false;

        public bool HasColorset
        {
            get
            {
                if (Shader == MtrlShader.Character || Shader == MtrlShader.CharacterGlass || Shader == MtrlShader.Bg || Shader == MtrlShader.BgColorChange)
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
                if (Shader == MtrlShader.Character && Preset == MtrlShaderPreset.Default)
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
                else if (Shader == MtrlShader.CharacterGlass)
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
                if (Shader == MtrlShader.Character && (Preset == MtrlShaderPreset.DiffuseSpecular || Preset == MtrlShaderPreset.Monster))
                {
                    return true;
                }
                else if (Shader == MtrlShader.Bg || Shader == MtrlShader.BgColorChange)
                {
                    return true;
                }
                else
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
                if (Shader == MtrlShader.Character || Shader == MtrlShader.Bg || Shader == MtrlShader.BgColorChange)
                {
                    // These shaders allow variable transparency.
                    return null;
                }
                else
                {
                    // These shaders require transparency to be strictly On or Off.
                    if (Shader == MtrlShader.Hair || Shader == MtrlShader.CharacterGlass)
                    {
                        return true;
                    }
                    else
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
            if (shader == MtrlShader.Character)
            {
                //presets.Add(MtrlShaderPreset.DiffuseMulti);
                presets.Add(MtrlShaderPreset.DiffuseSpecular);
                presets.Add(MtrlShaderPreset.Monster);
            }
            else if (shader == MtrlShader.Skin)
            {
                presets.Add(MtrlShaderPreset.BodyNoPores);
                presets.Add(MtrlShaderPreset.BodyWithHair);
                presets.Add(MtrlShaderPreset.Face);
                presets.Add(MtrlShaderPreset.FaceNoPores);
            }
            else if (shader == MtrlShader.Hair)
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

        // Optional Sampler Settings information.
        public TextureSamplerSettings Sampler;

        public object Clone()
        {
            var obj = this.MemberwiseClone();
            ((MapInfo)obj).Sampler = (TextureSamplerSettings)Sampler.Clone();
            return obj;
        }
    }

    public class MapInfoDetail : ICloneable
    {

        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }

}