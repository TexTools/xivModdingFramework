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

namespace xivModdingFramework.Models.DataContainers
{
    public class MeshData
    {
        /// <summary>
        /// The information for the mesh data
        /// </summary>
        public MeshDataInfo MeshInfo { get; set; }

        /// <summary>
        /// The list of parts for the mesh
        /// </summary>
        public List<MeshPart> MeshPartList { get; set; }

        /// <summary>
        /// The list of vertex data structures for the mesh
        /// </summary>
        public List<VertexDataStruct> VertexDataStructList { get; set; }

        /// <summary>
        /// The vertex data for the mesh
        /// </summary>
        public VertexData VertexData { get; set; }

        /// <summary>
        /// Determines whether this mesh contains a body material
        /// </summary>
        public bool IsBody { get; set; }

        /// <summary>
        /// A dictionary containing the reference index and position data for mesh hiding
        /// </summary>
        public Dictionary<int, Vector3> ReferencePositionsDictionary { get; set; }

        /// <summary>
        /// A dictionary containing the shape offset and position data for mesh hiding
        /// </summary>
        public Dictionary<int, Vector3> ShapePositionsDictionary { get; set; }

        /// <summary>
        /// A dictionary containing the reference index and shape offset for mesh hiding
        /// </summary>
        public Dictionary<int, Dictionary<ushort, ushort>> ShapeIndexOffsetDictionary { get; set; }

        /// <summary>
        /// A list of the shape paths associated with this mesh
        /// </summary>
        public List<string> ShapePathList { get; set; }
    }
}