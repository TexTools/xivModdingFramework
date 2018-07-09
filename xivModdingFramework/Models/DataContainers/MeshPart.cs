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
    /// This class contins the properties for the Parts for a Mesh
    /// </summary>
    public class MeshPart
    {
        /// <summary>
        /// The offset to the start index in the Index Data Block
        /// </summary>
        public int IndexOffset { get; set; }

        /// <summary>
        /// The number of indices the mesh part contains
        /// </summary>
        public int IndexCount { get; set; }

        /// <summary>
        /// The index to the attribute used by the mesh part
        /// </summary>
        /// <remarks>
        /// This is the index to the value in MdlPathData.AttributeList
        /// </remarks>
        public int AttributeIndex { get; set; }

        /// <summary>
        /// The offset to the starting bone in the BoneList
        /// </summary>
        public short BoneStartOffset { get; set; }

        /// <summary>
        /// The number of bones to use from the bone list beginning at the BoneStartOffset
        /// </summary>
        public short BoneCount { get; set; }
    }
}