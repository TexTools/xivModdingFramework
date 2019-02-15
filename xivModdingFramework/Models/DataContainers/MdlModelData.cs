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

namespace xivModdingFramework.Models.DataContainers
{
    /// <summary>
    /// This class cotains the properties for the MDL model data
    /// </summary>
    /// <remarks>
    /// This section of the MDL file still has a lot of unknowns
    /// </remarks>
    public class MdlModelData
    {
        /// <summary>
        /// Unknown Usage
        /// </summary>
        public int Unknown0 { get; set; }

        /// <summary>
        /// The total number of meshes that the model contains
        /// </summary>
        /// <remarks>
        /// This includes all LoD meshes
        /// </remarks>
        public short MeshCount { get; set; }

        /// <summary>
        /// The number of attributes used by the model
        /// </summary>
        public short AttributeCount { get; set; }

        /// <summary>
        /// The total number of mesh parts the model contains
        /// </summary>
        public short MeshPartCount { get; set; }

        /// <summary>
        /// The number of materials used by the model
        /// </summary>
        public short MaterialCount { get; set; }

        /// <summary>
        /// The number of bones used by the model
        /// </summary>
        public short BoneCount { get; set; }

        /// <summary>
        /// The total number of Bone Lists the model uses
        /// </summary>
        /// <remarks>
        /// There is usually one per LoD
        /// </remarks>
        public short BoneListCount { get; set; }

        /// <summary>
        /// The number of Mesh Shapes
        /// </summary>
        public short ShapeCount { get; set; }

        /// <summary>
        /// The number of data blocks in the mesh shapes
        /// </summary>
        public short ShapeDataCount { get; set; }

        /// <summary>
        /// The total number of indices that the mesh shapes uses
        /// </summary>
        public short ShapeIndexCount { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown1 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown2 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown3 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown4 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown5 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown6 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown7 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown8 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown9 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public byte Unknown10a { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public byte Unknown10b { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown11 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown12 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown13 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown14 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown15 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown16 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown17 { get; set; }
    }
}