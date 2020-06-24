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
using System.Collections.Generic;
using System.IO;
using System.Resources;
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
        public ushort DataStruct1Count { get; set; }

        /// <summary>
        /// The number of type 2 data structures
        /// </summary>
        public ushort DataStruct2Count { get; set; }

        /// <summary>
        /// The number of parameter stuctures
        /// </summary>
        public ushort ParameterStructCount { get; set; }

        /// <summary>
        /// The shader number used by the item
        /// </summary>
        /// <remarks>
        /// This is a guess and has not been tested to be true
        /// </remarks>
        public ushort ShaderNumber { get; set; }

        /// <summary>
        /// Unknown Value
        /// </summary>
        public ushort Unknown3 { get; set; }

        /// <summary>
        /// The list of Type 1 data structures
        /// </summary>
        public List<DataStruct1> DataStruct1List { get; set; }

        /// <summary>
        /// The list of Type 2 data structures
        /// </summary>
        public List<DataStruct2> DataStruct2List { get; set; }

        /// <summary>
        /// The list of Parameter data structures
        /// </summary>
        public List<ParameterStruct> ParameterStructList { get; set; }

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

        public MapInfo GetMapInfo(XivTexType MapType)
        {
            var info = new MapInfo();
            int mapIndex = -1;

            info.Usage = MapType;

            // Look for the right map type in the parameter list.
            foreach (var paramSet in ParameterStructList)
            {
                if (paramSet.ID == Mtrl._MtrlParamUsage[MapType])
                {
                    mapIndex = (int) paramSet.TextureIndex; 

                    // Pare format short down of extraneous data.
                    short clearLast6Bits = -64; // Hex 0xFFC0
                    short format = (short)(paramSet.FileFormat & clearLast6Bits);
                    short setFirstBit = -32768;
                    format = (short)(format | setFirstBit);

                    // Scan through known formats.
                    info.Format = MtrlMapFormat.Other;
                    foreach (var formatEntry in Mtrl._MtrlParamFormat)
                    {
                        if(format == formatEntry.Value)
                        {
                            info.Format = formatEntry.Key;
                        }
                    }
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

        public void SetMapInfo(XivTexType MapType, MapInfo info)
        {
            // Sanity check.
            if(info != null && info.Usage != MapType)
            {
                throw new System.Exception("Invalid attempt to reassign map materials.");
            }
           
            var paramIdx = -1;
            ParameterStruct oldInfo = new ParameterStruct();
            // Look for the right map type in the parameter list.
            for( var i = 0; i < ParameterStructList.Count; i++)
            {
                var paramSet = ParameterStructList[i];

                if (paramSet.ID == Mtrl._MtrlParamUsage[MapType])
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
                ParameterStructList.RemoveAt(paramIdx);

                // Update other texture offsets
                for(var i = 0; i < ParameterStructList.Count; i++)
                {
                    var p = ParameterStructList[i];
                    if(p.TextureIndex > oldInfo.TextureIndex)
                    {
                        p.TextureIndex--;
                    }
                    ParameterStructList[i] = p;
                }

                // Remove struct1 entry for the removed map type, if it exists.
                for(var i = 0; i < DataStruct1List.Count; i++)
                {
                    var s = DataStruct1List[i];
                    if(s.ID == Mtrl._MtrlStruct1Data[MapType].ID)
                    {
                        DataStruct1List.RemoveAt(i);
                        break;
                    }
                }

                return;

            }



            var raw = new ParameterStruct();
            raw.ID = Mtrl._MtrlParamUsage[info.Usage];
            raw.Unknown2 = 15;
            raw.FileFormat = Mtrl._MtrlParamFormat[info.Format];
            raw.TextureIndex = (paramIdx >= 0 ? (uint)oldInfo.TextureIndex : (uint)TexturePathList.Count);

            // Inject the new parameters.
            if(paramIdx >= 0)
            {
                ParameterStructList[paramIdx] = raw;
            } else
            {
                ParameterStructList.Add(raw);
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
            for (var i = 0; i < DataStruct1List.Count; i++)
            {
                var s = DataStruct1List[i];
                if (s.ID == Mtrl._MtrlStruct1Data[MapType].ID)
                {
                    foundEntry = true;
                    break;
                }
            }

            if(!foundEntry)
            {
                var s1 = new DataStruct1()
                {
                    ID = Mtrl._MtrlStruct1Data[MapType].ID,
                    Unknown1 = Mtrl._MtrlStruct1Data[MapType].Unknown1
                };

                DataStruct1List.Add(s1);
            }

        }

    }

    /// <summary>
    /// This class contains the information for a Type 1 MTRL data structure
    /// </summary>
    public class DataStruct1
    {
        public uint ID;

        public uint Unknown1;
    }

    /// <summary>
    /// This class contains the information for a Type 2 MTRL data structure
    /// </summary>
    public class DataStruct2
    {
        public uint ID;

        public short Offset;

        public short Size;
    }

    /// <summary>
    /// This class contains the information for a MTRL Parameter data structure
    /// </summary>
    public class ParameterStruct
    {
        public uint ID;

        public short FileFormat;

        public short Unknown2;

        public uint TextureIndex;
    }


    // Enum representation of the format map data is used as.
    public enum MtrlMapFormat
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
    }

    public class MapInfo
    {
        public XivTexType Usage;
        public MtrlMapFormat Format;
        public string path;
    }


}