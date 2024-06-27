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

using xivModdingFramework.Models.Enums;

namespace xivModdingFramework.Models.DataContainers
{
    /// <summary>
    /// This class contains the properties for the Vertex Data Structures
    /// </summary>
    public class VertexDataStruct
    {

        /// <summary>
        /// The vertex data block the data belongs to
        /// </summary>
        /// <remarks>
        /// There are usually 2 data blocks and usually contain the following data
        /// Block 0: Positions, Blend Weights, Blend indices
        /// Block 1: Normal, Tangent, Color, Texture Coordinates
        /// </remarks>
        public byte DataBlock { get; set; }

        /// <summary>
        /// The offset to the data within the Data Block
        /// </summary>
        public byte DataOffset { get; set; }

        /// <summary>
        /// The type of the data
        /// </summary>
        public VertexDataType DataType { get; set; }

        /// <summary>
        /// What the data will be used for
        /// </summary>
        public VertexUsageType DataUsage { get; set; }

        public byte Count { get; set; }
    }
}
