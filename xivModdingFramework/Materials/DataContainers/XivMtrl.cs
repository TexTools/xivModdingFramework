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
using System.Resources;
using System.Text.RegularExpressions;
using xivModdingFramework.General.Enums;
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
        public const string ItemPathToken = "{item_path}";
        public const string VariantToken = "{variant}";
        public const string TextureNameToken = "{texture_name}";
        public const string CommonPathToken = "{common_path}";

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
            size += ColorSetExtraData == null ? 0 : ColorSetExtraData.Length;
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
        public byte UnknownDataSize { get; set; }

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
        /// A list containing the Map Path Unknowns
        /// </summary>
        public List<short> MapPathUnknownList { get; set; }

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
        /// The name of the shader used by the item
        /// </summary>
        public string Shader { get; set; }

        /// <summary>
        /// Unknown value
        /// </summary>
        public byte[] Unknown2 { get; set; }

        /// <summary>
        /// The list of half floats containing the ColorSet data
        /// </summary>
        public List<Half> ColorSetData { get; set; }

        /// <summary>
        /// The byte array containing the extra ColorSet data
        /// </summary>
        public byte[] ColorSetExtraData { get; set; }

        /// <summary>
        /// The size of the additional MTRL Data
        /// </summary>
        public ushort ShaderParameterDataSize { 
            get {
                var size = 0;
                ShaderParameterList.ForEach(x => {
                    size += x.Bytes.Count;
                }
                );

                if(size % 8 != 0)
                {
                    var padding = 8 - (size % 8);
                    size += padding;
                }

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
        public ushort TextureUsageCount { get { return (ushort)TextureUsageList.Count; } set
            {
                //No-Op
                throw new Exception("Attempted to directly set TextureUsageCount");
            }
        }

        /// <summary>
        /// The number of type 2 data structures
        /// </summary>
        public ushort ShaderParameterCount
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
        public ushort TextureDescriptorCount
        {
            get { return (ushort)TextureDescriptorList.Count; }
            set
            {
                //No-Op
                throw new Exception("Attempted to directly set TextureDescriptorCount");
            }
        }

        /// <summary>
        /// The shader number used by the item
        /// </summary>
        /// <remarks>
        /// This is a guess and has not been tested to be true
        /// Seems to be more likely that this is some base argument passed into the shader.
        /// </remarks>
        public ushort ShaderNumber { get; set; }

        /// <summary>
        /// Unknown Value
        /// </summary>
        public ushort Unknown3 { get; set; }

        /// <summary>
        /// The list of Type 1 data structures
        /// </summary>
        public List<TextureUsageStruct> TextureUsageList { get; set; }

        /// <summary>
        /// The list of Type 2 data structures
        /// </summary>
        public List<ShaderParameterStruct> ShaderParameterList { get; set; }

        /// <summary>
        /// The list of Parameter data structures
        /// </summary>
        public List<TextureDescriptorStruct> TextureDescriptorList { get; set; }

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
            var bit = (ushort)(ShaderNumber & transparencyBit);

            if(bit > 0)
            {
                info.TransparencyEnabled = true;
            } else
            {
                info.TransparencyEnabled = false;
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
                        info.Preset = MtrlShaderPreset.DiffuseSpecular;
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
                if(GetTextureUsage(XivTexType.Skin) == null)
                {
                    info.Preset = MtrlShaderPreset.Face;
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
            var transparency = info.ForcedTransparency == null ? info.TransparencyEnabled : (bool)info.ForcedTransparency;
            if (transparency)
            {
                ShaderNumber = (ushort)(ShaderNumber | transparencyBit);
            }
            else
            {
                ShaderNumber = (ushort)(ShaderNumber & (~transparencyBit));
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
                ColorSetCount = 1;
                ColorSetData = new List<Half>();
                ColorSetExtraData = null;
            } else
            {
                if(ColorSetCount == 0 || ColorSetData == null || ColorSetData.Count != 256)
                {
                    // Get default Colorset Data.
                    ColorSetData = Tex.GetColorsetDataFromDDS(Tex.GetDefaultTexturePath(XivTexType.ColorSet));
                }
                if(ColorSetExtraData == null || ColorSetExtraData.Length != 32)
                {
                    ColorSetExtraData = Tex.GetColorsetExtraDataFromDDS(Tex.GetDefaultTexturePath(XivTexType.ColorSet));
                }
            }

            
            // Clear out the unknown lists back to known default working values.
            for(var idx = 0; idx < TexturePathUnknownList.Count; idx++)
            {
                TexturePathUnknownList[idx] = 0;
            }

            for (var idx = 0; idx < Unknown2.Length; idx++)
            {
                if(idx == 0)
                {
                    Unknown2[idx] = 12;
                } else
                {
                    Unknown2[idx] = 0;
                } 
            }
        }

        /// <summary>
        /// Converts raw Mtrl format data into the appropriate enum.
        /// Does a bit of math to ignore extraneous bits that don't have known purpose yet.
        /// </summary>
        /// <param name="raw"></param>
        /// <returns></returns>
        private static MtrlTextureDescriptorFormat GetFormat(short raw)
        {

            // Pare format short down of extraneous data.
            short clearLast6Bits = -64; // Hex 0xFFC0
            short format = (short)(raw & clearLast6Bits);
            short setFirstBit = -32768;
            format = (short)(format | setFirstBit);

            // Scan through known formats.
            foreach (var formatEntry in Mtrl.TextureDescriptorFormatValues)
            {
                if (format == formatEntry.Value)
                {
                    return formatEntry.Key;
                }
            }
            return MtrlTextureDescriptorFormat.Other;
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
            foreach (var paramSet in TextureDescriptorList)
            {
                if (paramSet.TextureType == Mtrl.TextureDescriptorValues[MapType])
                {
                    mapIndex = (int) paramSet.TextureIndex;
                    info.Format = GetFormat(paramSet.FileFormat);
                }
                else if (paramSet.TextureType == Mtrl.FurnitureTextureDescriptorValues[MapType])
                {
                    mapIndex = (int)paramSet.TextureIndex;
                    info.Format = GetFormat(paramSet.FileFormat);
                }
            }

            if(mapIndex < 0)
            {
                return null;
            }

            info.path = "";
            if ( mapIndex < TexturePathList.Count )
            {
                info.path = TexturePathList[mapIndex];
            }

            info.path = TokenizePath(info.path, info.Usage);
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
            info.path = path;
            
            foreach(var descriptor in TextureDescriptorList)
            {
                // Found the descriptor.
                if(descriptor.TextureIndex == idx)
                {
                    // If we know what this texture descriptor actually means
                    if(Mtrl.TextureDescriptorValues.ContainsValue(descriptor.TextureType))
                    {
                        info.Usage = Mtrl.TextureDescriptorValues.First(x => x.Value == descriptor.TextureType).Key;
                    }
                    else if (Mtrl.FurnitureTextureDescriptorValues.ContainsValue(descriptor.TextureType))
                    {
                        info.Usage = Mtrl.FurnitureTextureDescriptorValues.First(x => x.Value == descriptor.TextureType).Key;
                    }
                    else
                    {
                        info.Usage = XivTexType.Other;
                    }

                    info.Format = GetFormat(descriptor.FileFormat);
                }
            }

            info.path = TokenizePath(info.path, info.Usage);

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
            TextureDescriptorStruct oldInfo = new TextureDescriptorStruct();
            // Look for the right map type in the parameter list.
            for( var i = 0; i < TextureDescriptorList.Count; i++)
            {
                var paramSet = TextureDescriptorList[i];

                if (paramSet.TextureType == Mtrl.TextureDescriptorValues[MapType])
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
                    TextureDescriptorList.RemoveAt(paramIdx);

                    // Update other texture offsets
                    for (var i = 0; i < TextureDescriptorList.Count; i++)
                    {
                        var p = TextureDescriptorList[i];
                        if (p.TextureIndex > oldInfo.TextureIndex)
                        {
                            p.TextureIndex--;
                        }
                        TextureDescriptorList[i] = p;
                    }
                }

                return;

            }

            var rootPath = GetTextureRootDirectoy();
            var defaultFileName = GetDefaultTexureName(info.Usage);

            // No path, assign it by default.
            if (info.path.Trim() == "")
            {
                info.path = rootPath + defaultFileName;
            }

            // Detokenize paths
            info.path = DetokenizePath(info.path, info.Usage);

            // Ensure .tex or .atex ending for sanity.
            var match = Regex.Match(info.path, "\\.a?tex$");
            if(!match.Success)
            {
                info.path = info.path += ".tex";
            }




            var raw = new TextureDescriptorStruct();
            raw.TextureType = Mtrl.TextureDescriptorValues[info.Usage];
            raw.Unknown = 15;
            raw.FileFormat = Mtrl.TextureDescriptorFormatValues[info.Format];
            raw.TextureIndex = (paramIdx >= 0 ? (uint)oldInfo.TextureIndex : (uint)TexturePathList.Count);

            // Inject the new parameters.
            if(paramIdx >= 0)
            {
                TextureDescriptorList[paramIdx] = raw;
            } else
            {
                TextureDescriptorList.Add(raw);
            }

            // Inject the new string
            if(raw.TextureIndex == TexturePathList.Count)
            {
                TexturePathList.Add(info.path);
                TexturePathUnknownList.Add((short) 0); // This value seems to always be 0 for textures.
            } else
            {
                TexturePathList[(int) raw.TextureIndex] = info.path;
            }
        }

        /// <summary>
        /// Regenerates/Cleans up the texture usage list based on the 
        /// Texture Maps that are set in the Texture Description Fields.
        /// </summary>
        private void RegenerateTextureUsageList(ShaderInfo info)
        {
            TextureUsageList.Clear();

            // These shaders do not use the texture usage list at all.
            if (info.Shader == MtrlShader.Furniture || info.Shader == MtrlShader.DyeableFurniture || info.Shader == MtrlShader.Iris || info.Shader == MtrlShader.Hair)
            {
                return;
            }

            if (info.Shader == MtrlShader.Skin)
            {
                if (info.Preset == MtrlShaderPreset.Face)
                {
                    SetTextureUsage(XivTexType.Normal);
                    SetTextureUsage(XivTexType.Diffuse);
                    SetTextureUsage(XivTexType.Multi);
                    SetTextureUsage(XivTexType.Specular);
                }
                else
                {
                    // Non-Face Skin textures use a single custom usage value.
                    SetTextureUsage(XivTexType.Skin);
                }

            }
            else if (info.Shader == MtrlShader.Standard)
            {
                SetTextureUsage(XivTexType.Normal);
                if (info.HasDiffuse)
                {
                    SetTextureUsage(XivTexType.Diffuse);
                }
                if (info.HasMulti || info.HasSpec)
                {
                    SetTextureUsage(XivTexType.Multi);
                    SetTextureUsage(XivTexType.Specular);
                }
            }
            else
            {
                SetTextureUsage(XivTexType.Normal);
                SetTextureUsage(XivTexType.Diffuse);
                SetTextureUsage(XivTexType.Multi);
                SetTextureUsage(XivTexType.Specular);
            }

        }

        /// <summary>
        /// Regenerates the Shader Parameter list based on shader and texture information.
        /// </summary>
        private void RegenerateShaderParameterList(ShaderInfo info)
        {
            var args = new List<MtrlShaderParameterId>();

            args.Add(MtrlShaderParameterId.Common1);
            args.Add(MtrlShaderParameterId.Common2);

            if (info.Shader == MtrlShader.Skin)
            {
                args.Add(MtrlShaderParameterId.SkinColor);
                args.Add(MtrlShaderParameterId.SkinOutline);
                args.Add(MtrlShaderParameterId.SkinUnknown1);
                args.Add(MtrlShaderParameterId.SkinUnknown2);
                args.Add(MtrlShaderParameterId.SkinUnknown3);
                args.Add(MtrlShaderParameterId.RacialSkin1);
                args.Add(MtrlShaderParameterId.RacialSkin2);
                args.Add(MtrlShaderParameterId.Reflection1);

                if (info.Preset == MtrlShaderPreset.Face)
                {
                    args.Add(MtrlShaderParameterId.Face1);
                }
            }
            else if (info.Shader == MtrlShader.Standard || info.Shader == MtrlShader.Glass)
            {
                args.Add(MtrlShaderParameterId.Equipment1);
                args.Add(MtrlShaderParameterId.Reflection1);
            }
            else if (info.Shader == MtrlShader.Iris)
            {
                args.Add(MtrlShaderParameterId.Equipment1);
                args.Add(MtrlShaderParameterId.Reflection1);
                args.Add(MtrlShaderParameterId.RacialSkin1);
                args.Add(MtrlShaderParameterId.SkinColor);
                args.Add(MtrlShaderParameterId.SkinOutline);
            }
            else if(info.Shader == MtrlShader.Hair)
            {
                args.Add(MtrlShaderParameterId.Equipment1);
                args.Add(MtrlShaderParameterId.Reflection1);
                args.Add(MtrlShaderParameterId.SkinColor);
                args.Add(MtrlShaderParameterId.SkinOutline);
                args.Add(MtrlShaderParameterId.Hair1);
                args.Add(MtrlShaderParameterId.Hair2);
            }


            // Regenerate the list.
            ShaderParameterList.Clear();
            args.ForEach(x => SetShaderParameter(x));
        }


        /// <summary>
        /// Gets a given shader Parameter by ID.
        /// </summary>
        /// <param name="parameterId"></param>
        /// <returns></returns>
        private ShaderParameterStruct GetShaderParameter(MtrlShaderParameterId parameterId)
        {
            return ShaderParameterList.FirstOrDefault(x => x.ParameterID == parameterId);
        }

        /// <summary>
        /// Sets or adds a given shader parameter.
        /// </summary>
        /// <param name="parameterId"></param>
        /// <param name="data"></param>
        private void SetShaderParameter(MtrlShaderParameterId parameterId, List<byte> data = null)
        {


            try
            {
                var value = ShaderParameterList.First(x => x.ParameterID == parameterId);

                // Only overwrite if we were given explicit data.
                if (value != null && data != null)
                {
                    value.Bytes = data;
                }
            }
            catch (Exception ex)
            {
                if (data == null)
                {
                    data = Mtrl.ShaderParameterValues[parameterId];
                }

                ShaderParameterList.Add(new ShaderParameterStruct
                {
                    ParameterID = parameterId,
                    Bytes = data
                });
            }
        }

        /// <summary>
        /// Removes a given shader parameter.
        /// </summary>
        /// <param name="parameterId"></param>
        private void ClearShaderParameter(MtrlShaderParameterId parameterId)
        {
            ShaderParameterList = ShaderParameterList.Where(x => x.ParameterID != parameterId).ToList();
        }


        /// <summary>
        /// Gets a given texture usage object by its usage value.
        /// </summary>
        /// <param name="usage"></param>
        /// <returns></returns>
        private TextureUsageStruct GetTextureUsage(XivTexType usage)
        {
            return TextureUsageList.FirstOrDefault(x => x.TextureType == Mtrl.TextureUsageValues[usage].TextureType);
        }

        /// <summary>
        /// Clears a texture usage value.
        /// </summary>
        /// <param name="usage"></param>
        private bool ClearTextureUsage(XivTexType usage)
        {
            var oldCount = TextureUsageList.Count;
            TextureUsageList = TextureUsageList.Where(x => x.TextureType != Mtrl.TextureUsageValues[usage].TextureType).ToList();
            return oldCount != TextureUsageList.Count;
        }

        /// <summary>
        /// Adds or changes a texture usage value.
        /// </summary>
        /// <param name="usage"></param>
        /// <param name="unknownValue"></param>
        private void SetTextureUsage(XivTexType usage, uint? unknownValue = null)
        {
            if(unknownValue == null)
            {
                unknownValue = Mtrl.TextureUsageValues[usage].Unknown;
            }
            try
            {
                var val = TextureUsageList.First(x => x.TextureType == Mtrl.TextureUsageValues[usage].TextureType);
                if(val != null)
                {
                    // Despite being named 'Struct' this is actually a class.
                    val.Unknown = (uint) unknownValue;
                } else
                {
                    TextureUsageList.Add(new TextureUsageStruct()
                    {
                        TextureType = Mtrl.TextureUsageValues[usage].TextureType,
                        Unknown = (uint) unknownValue
                    });
                }
            } catch(Exception ex)
            {
                TextureUsageList.Add(new TextureUsageStruct()
                {
                    TextureType = Mtrl.TextureUsageValues[usage].TextureType,
                    Unknown = (uint) unknownValue
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
                info.path = TexturePathList[i];
                info.Format = MtrlTextureDescriptorFormat.Other;
                info.Usage = XivTexType.Other;

                // Check if the texture appears in the parameter list.
                foreach(var p in TextureDescriptorList)
                {
                    if(p.TextureIndex == i)
                    {
                        // This is a known parameter.
                        if(Mtrl.TextureDescriptorValues.ContainsValue(p.TextureType))
                        {
                            var usage = Mtrl.TextureDescriptorValues.First(x =>
                            {
                                return (x.Value == p.TextureType);
                            }).Key;
                            info.Usage = usage;
                        } else if(Mtrl.FurnitureTextureDescriptorValues.ContainsValue(p.TextureType))
                        {
                            // Known parameter for the furniture shaders.
                            var usage = Mtrl.FurnitureTextureDescriptorValues.First(x =>
                            {
                                return (x.Value == p.TextureType);
                            }).Key;
                            info.Usage = usage;

                        } else 
                        {
                            info.Usage = XivTexType.Other;
                        }

                        info.Format = GetFormat(p.FileFormat);
                        break;
                    }
                }

                if (tokenize)
                {
                    info.path = TokenizePath(info.path, info.Usage);
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
                    ttp = new TexTypePath() { DataFile = GetDataFile(), Path = map.path, Type = XivTexType.Skin };

                } else if (shaderInfo.Shader == MtrlShader.Furniture && map.path.Contains("dummy")) {
                    // Dummy textures are skipped.
                    continue;
                }
                else
                {
                    ttp = new TexTypePath() { DataFile = GetDataFile(), Path = map.path, Type = map.Usage };
                }
                var fName = Path.GetFileNameWithoutExtension(map.path);
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
                    root = match.Groups[1].Value + "texture/";
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
                versionString += 'v' + (version.ToString().PadLeft(2, '0')) + '_';
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
                ret += GetVariantString();
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
                ret += "_m";
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
            return "chara/common/texture/";
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
                path = rootPath + defaultFileName;
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

    /// <summary>
    /// This class contains the information for the texture usage structs in MTRL data.
    /// </summary>
    public class TextureUsageStruct
    {
        public uint TextureType; // Mappings for this TextureType value to XivTexType value are available in Mtrl.cs

        public uint Unknown;
    }

    /// <summary>
    /// This class containst he information for the shader parameters at the end the MTRL File
    /// </summary>
    public class ShaderParameterStruct
    {
        public MtrlShaderParameterId ParameterID;

        public short Offset;

        public short Size;

        public List<byte> Bytes;
        public List<uint> Args;
    }

    /// <summary>
    /// This class contains the information for MTRL texture parameters.
    /// </summary>
    public class TextureDescriptorStruct
    {
        public uint TextureType; // Mappings for this TextureType value to XivTexType value are available in Mtrl.cs

        public short FileFormat; // Mappings for this FileFormat value to MtrlTextureDescriptorFormat value are available in Mtrl.cs 

        public short Unknown;   // Always seems to be [15]?

        public uint TextureIndex;
    }

    // Enum representation of the format map data is used as.
    public enum MtrlTextureDescriptorFormat
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
        Face,    
    }

    /// <summary>
    /// Enums for the various shader parameter Ids.  These likely are used to pipe extra data from elsewhere into the shader.
    /// Ex. Local Reflection Maps, Character Skin/Hair Color, Dye Color, etc.
    /// </summary>
    public enum MtrlShaderParameterId : uint
    {
        Common1 = 699138595,        // Used in every material.  Overwriting bytes with 0s seems to have no effect.
        Common2 = 1465565106,       // Used in every material.  Overwriting bytes with 0s seems to have no effect.

        SkinColor = 740963549,          // This skin args seem to be the same for all races.
        SkinOutline = 2569562539,
        SkinUnknown1 = 390837838,
        SkinUnknown2 = 950420322,          // Always all 0 data?
        SkinUnknown3 = 1112929012,

        Face1 = 2274043692,

        Equipment1 = 3036724004,    // Used in some equipment rarely, particularly Legacy items.  Always seems to have [0]'s for data.
        Reflection1 = 906496720,     // This seems to be some kind of setting related to reflecitivty/specular intensity.

        Hair1 = 364318261,          // Character Hair Color?
        Hair2 = 3042205627,         // Character Highlight Color

        RacialSkin1 = 1659128399,         // This arg is the same for most races, but Highlander and Roe M use a different value
        RacialSkin2 = 778088561,          // Roe M is the only one that has a change to this arg's data.?

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
        public bool HasColorset
        {
            get
            {
                if(Shader == MtrlShader.Standard || Shader == MtrlShader.Glass || Shader == MtrlShader.Furniture || Shader == MtrlShader.DyeableFurniture)
                {
                    return true;
                }
                return false;
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
                if (Shader == MtrlShader.Standard && Preset == MtrlShaderPreset.DiffuseSpecular)
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
                presets.Add(MtrlShaderPreset.DiffuseMulti);
                presets.Add(MtrlShaderPreset.DiffuseSpecular);
            } else if(shader == MtrlShader.Skin)
            {
                presets.Add(MtrlShaderPreset.Face);
            }

            return presets;
        }
    }

    public class MapInfo
    {
        public XivTexType Usage;
        public MtrlTextureDescriptorFormat Format;
        public string path;
    }


}