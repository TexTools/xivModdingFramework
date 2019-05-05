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
using xivModdingFramework.Textures.DataContainers;

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

        public short Unknown1;

        public short Unknown2;

        public uint TextureIndex;
    }
}