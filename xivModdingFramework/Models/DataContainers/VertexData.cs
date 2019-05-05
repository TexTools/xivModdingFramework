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
using HelixToolkit.SharpDX.Core;

namespace xivModdingFramework.Models.DataContainers
{
    /// <summary>
    /// This class contains the properties for the Vertex Data
    /// </summary>
    public class VertexData
    {
        /// <summary>
        /// The vertex position data in Vector3 format (X, Y, Z)
        /// </summary>
        public Vector3Collection Positions { get; set; }

        /// <summary>
        /// The bone weight array per vertex
        /// </summary>
        /// <remarks>
        /// Each vertex can hold a maximum of 4 bone weights
        /// </remarks>
        public List<float[]> BoneWeights { get; set; }

        /// <summary>
        /// The bone index array per vertex
        /// </summary>
        /// <remarks>
        /// Each vertex can hold a maximum of 4 bone indices
        /// </remarks>
        public List<byte[]> BoneIndices { get; set; }

        /// <summary>
        /// The vertex normal data in Vector4 format (X, Y, Z, W)
        /// </summary>
        /// <remarks>
        /// The W coordinate is present but has never been noticed to be anything other than 0
        /// </remarks>
        public Vector3Collection Normals { get; set; }

        /// <summary>
        /// The vertex BiNormal data in Vector3 format (X, Y, Z)
        /// </summary>
        public Vector3Collection BiNormals { get; set; }

        /// <summary>
        /// The vertex BiNormal Handedness data in bytes
        /// </summary>
        public List<byte> BiNormalHandedness { get; set; }

        /// <summary>
        /// The vertex Tangent data in Vector3 format (X, Y, Z)
        /// </summary>
        public Vector3Collection Tangents { get; set; }

        /// <summary>
        /// The vertex color data in Byte4 format (A, R, G, B)
        /// </summary>
        public List<Color> Colors { get; set; }

        /// <summary>
        /// The vertex color data in Color4 format
        /// </summary>
        public Color4Collection Colors4 { get; set; }

        /// <summary>
        /// The primary texture coordinates for the mesh in Vector2 format (X, Y)
        /// </summary>
        public Vector2Collection TextureCoordinates0 { get; set; }

        /// <summary>
        /// The secondary texture coordinates for the mesh in Vector2 format (X, Y)
        /// </summary>
        public Vector2Collection TextureCoordinates1 { get; set; }

        /// <summary>
        /// The index data for the mesh
        /// </summary>
        public IntCollection Indices { get; set; }
    }
}