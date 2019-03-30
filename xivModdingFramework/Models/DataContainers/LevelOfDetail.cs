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

using System.Collections.Generic;

namespace xivModdingFramework.Models.DataContainers
{
    /// <summary>
    /// This class contains properties for the Level of Detail data
    /// </summary>
    public class LevelOfDetail
    {
        /// <summary>
        /// The offset to the mesh data block
        /// </summary>
        public ushort MeshOffset { get; set; }

        /// <summary>
        /// The number of meshes to use
        /// </summary>
        public short MeshCount { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public int Unknown0 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public int Unknown1 { get; set; }

        /// <summary>
        /// Mesh End
        /// </summary>
        public short MeshEnd { get; set; }

        /// <summary>
        /// Extra Mesh Count
        /// </summary>
        public short ExtraMeshCount { get; set; }

        /// <summary>
        /// Mesh Sum
        /// </summary>
        public short MeshSum{ get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown2 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public int Unknown3 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public int Unknown4 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public int Unknown5 { get; set; }

        /// <summary>
        /// The offset at which the index data begins
        /// </summary>
        public int IndexDataStart { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public int Unknown6 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public int Unknown7 { get; set; }

        /// <summary>
        /// The size of the Vertex Data Block
        /// </summary>
        public int VertexDataSize { get; set; }

        /// <summary>
        /// The size of the Index Data Block
        /// </summary>
        public int IndexDataSize { get; set; }

        /// <summary>
        /// The offset to the Vertex Data Block
        /// </summary>
        public int VertexDataOffset { get; set; }

        /// <summary>
        /// The offset to the Index Data Block
        /// </summary>
        public int IndexDataOffset { get; set; }

        /// <summary>
        /// The list of MeshData for the LoD
        /// </summary>
        public List<MeshData> MeshDataList { get; set; }
    }
}