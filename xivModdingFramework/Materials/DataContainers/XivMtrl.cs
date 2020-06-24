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
using xivModdingFramework.Materials.FileTypes;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.Enums;

namespace xivModdingFramework.Materials.DataContainers
{
    /// <summary>
    /// This class holds the information for an MTRL file
    /// </summary>
    public class XivMtrl
    {
        public const string ItemPathToken = "{ItemPath}";
        public const string VersionToken = "{Version}";
        public const string TextureNameToken = "{TextureName}";
        public const string CommonPathToken = "{CommonPath}";

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
        public ushort ColorSetDataSize { get; set; }

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
        public ushort AdditionalDataSize { get; set; }

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
        /// The byte array of additional data
        /// </summary>
        public byte[] AdditionalData { get; set; }

        /// <summary>
        /// A list of TexTypePath for the mtrl <see cref="TexTypePath"/>
        /// </summary>
        public List<TexTypePath> TextureTypePathList { get; set; }

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


            return info;
        }

        /// <summary>
        /// Sets the shader info for this Mtrl file.
        /// </summary>
        /// <param name="info"></param>
        public void SetShaderInfo(ShaderInfo info)
        {
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
                default:
                    // No change to the Shader for 'Other' type entries.
                    break;
            }

            // Set transparency bit as needed.
            const ushort transparencyBit = 16;
            if(info.TransparencyEnabled)
            {
                ShaderNumber = (ushort)(ShaderNumber | transparencyBit);
            } else
            {
                ShaderNumber = (ushort)(ShaderNumber & (~transparencyBit));
            }
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

            return info;
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
                if(paramIdx < 0)
                {
                    // Didn't exist to start, nothing to do.
                    return;
                }

                // Remove texture from path list if it exists.
                if (TexturePathList.Count > oldInfo.TextureIndex)
                {
                    TexturePathList.RemoveAt((int)oldInfo.TextureIndex);
                    TexturePathUnknownList.RemoveAt((int)oldInfo.TextureIndex);
                }

                // Remove Parameter List
                TextureDescriptorList.RemoveAt(paramIdx);

                // Update other texture offsets
                for(var i = 0; i < TextureDescriptorList.Count; i++)
                {
                    var p = TextureDescriptorList[i];
                    if(p.TextureIndex > oldInfo.TextureIndex)
                    {
                        p.TextureIndex--;
                    }
                    TextureDescriptorList[i] = p;
                }

                // Remove struct1 entry for the removed map type, if it exists.
                for(var i = 0; i < TextureUsageList.Count; i++)
                {
                    var s = TextureUsageList[i];
                    if(s.TextureType == Mtrl.TextureUsageValues[MapType].TextureType)
                    {
                        TextureUsageList.RemoveAt(i);
                        break;
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

            var rootPathWithVersion = rootPath;
            var versionString = GetVersionString();


            var defaultFileNameWithoutVersion = GetDefaultTexureName(info.Usage, false);

            // Detokenize paths
            info.path = info.path.Replace(ItemPathToken, rootPath);
            info.path = info.path.Replace(VersionToken, versionString);
            info.path = info.path.Replace(TextureNameToken, defaultFileNameWithoutVersion);
            info.path = info.path.Replace(CommonPathToken, GetCommonTextureDirectory());



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

            // Update struct1 entry with the appropriate entry if it's not already there.
            bool foundEntry = false;
            for (var i = 0; i < TextureUsageList.Count; i++)
            {
                var s = TextureUsageList[i];
                if (s.TextureType == Mtrl.TextureUsageValues[MapType].TextureType)
                {
                    foundEntry = true;
                    break;
                }
            }

            if(!foundEntry)
            {
                var s1 = new TextureUsageStruct()
                {
                    TextureType = Mtrl.TextureUsageValues[MapType].TextureType,
                    Unknown = Mtrl.TextureUsageValues[MapType].Unknown
                };

                TextureUsageList.Add(s1);
            }

        }

        /// <summary>
        /// Retrieve all MapInfo structs for all textures associated with this MTRL,
        /// Including textures with unknown usage.
        /// </summary>
        /// <returns></returns>
        public List<MapInfo> GetAllMapInfos()
        {
            var ret = new List<MapInfo>();
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
                        } else
                        {
                            info.Usage = XivTexType.Other;
                        }

                        info.Format = GetFormat(p.FileFormat);
                        break;
                    }
                }

                ret.Add(info);
            }

            return ret;
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
        public uint GetVersion()
        {
            var match = Regex.Match(MTRLPath, "/v([0-9]{4})/");
            if (match.Success)
            {
                return (uint)Int32.Parse(match.Groups[1].Value);
            }
            return 0;
        }

        /// <summary>
        /// Gets the version string based on the item version. Ex. 'v03_'
        /// </summary>
        /// <returns></returns>
        public string GetVersionString()
        {
            var version = GetVersion();

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
                ret += GetVersionString();
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

            // Followed by the material identifier, if above [a].
            var identifier = GetMaterialIdentifier();
            if(identifier != 'a')
            {
                ret += "_" + identifier;
            }

            ret += "_met";

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
            var match = Regex.Match(MTRLPath, "_([a-z])\\.mtrl");
            if(match.Success)
            {
                return match.Groups[1].Value[0];
            }
            return 'a';
        }


        /// <summary>
        /// Get the root shared common texture directory for FFXIV.
        /// </summary>
        /// <returns></returns>
        public static string GetCommonTextureDirectory()
        {
            return "chara/common/texture/";
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
        public uint ParameterID;

        public short Offset;

        public short Size;

        public List<byte> Bytes;
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
        WithAlpha,
        WithoutAlpha,
        Other
    }

    // Enum representation of the shader names used in mtrl files.
    public enum MtrlShader
    {
        Standard,       // character.shpk
        Glass,          // characterglass.shpk
        Skin,           // skin.shpk
        Hair,           // hair.shpk
        Iris,           // iris.shpk
        Other           // Unknown Shader
    }

    public class ShaderInfo
    {
        public MtrlShader Shader;
        public bool TransparencyEnabled;
        // public List<ShaderParameterStruct> AdditionalParameters; - Does this need to be exposed here?
    }

    public class MapInfo
    {
        public XivTexType Usage;
        public MtrlTextureDescriptorFormat Format;
        public string path;
    }


}