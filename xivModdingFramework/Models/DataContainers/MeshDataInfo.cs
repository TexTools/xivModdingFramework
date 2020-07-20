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
    /// This class contains the properties of the Mesh Data Information
    /// </summary>
    public class MeshDataInfo
    {
        /// <summary>
        /// The number of vertices the mesh contains
        /// </summary>
        public int VertexCount { get; set; }

        /// <summary>
        /// The number of indcies the mesh contains
        /// </summary>
        public int IndexCount { get; set; }

        /// <summary>
        /// The index of the material used by the mesh
        /// </summary>
        /// <remarks>
        /// This is the index of the material in the MaterialList from MdlPathData
        /// </remarks>
        public short MaterialIndex { get; set; }

        /// <summary>
        /// The index of the Mesh Part to start at
        /// </summary>
        public short MeshPartIndex { get; set; }

        /// <summary>
        /// The number of Parts the mesh contains
        /// </summary>
        public short MeshPartCount { get; set; }

        /// <summary>
        /// The index of the Bone List to use for the mesh
        /// </summary>
        /// <remarks>
        /// This is the index of the bone list in the BoneDataList
        /// </remarks>
        public short BoneSetIndex { get; set; }

        /// <summary>
        /// The offset to the Index Data Block
        /// </summary>
        public int IndexDataOffset { get; set; }

        /// <summary>
        /// The offset to the first Vertex Data Block
        /// </summary>
        public int VertexDataOffset0 { get; set; }

        /// <summary>
        /// The offset to the second Vertex Data Block
        /// </summary>
        public int VertexDataOffset1 { get; set; }

        /// <summary>
        /// The offset to the Third Vertex Data Block
        /// </summary>
        /// <remarks>
        /// This value is usually blank
        /// </remarks>
        public int VertexDataOffset2 { get; set; }

        /// <summary>
        /// The size of each individual Vertex Data Entry in the first Vertex Data Block
        /// </summary>
        public byte VertexDataEntrySize0 { get; set; }

        /// <summary>
        /// The size of each individual Vertex Data Entry in the second Vertex Data Block
        /// </summary>
        public byte VertexDataEntrySize1 { get; set; }

        /// <summary>
        /// The size of each individual Vertex Data Entry in the third Vertex Data Block
        /// </summary>
        /// <remarks>
        /// This value is usually blank
        /// </remarks>
        public byte VertexDataEntrySize2 { get; set; }

        /// <summary>
        /// The number of vertex data blocks for the mesh
        /// </summary>
        public byte VertexDataBlockCount { get; set; }
    }
}