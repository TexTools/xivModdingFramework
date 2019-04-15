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
    public class ColladaData
    {
        /// <summary>
        /// The bone strings as they appear in the collada file
        /// </summary>
        public string[] Bones { get; set; }

        /// <summary>
        /// The list of bones and their associated number
        /// </summary>
        public Dictionary<string, int> BoneNumDictionary { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// The bone names for the mesh
        /// </summary>
        public List<string> MeshBoneNames { get; set; } = new List<string>();

        /// <summary>
        /// The Vertex Positions
        /// </summary>
        public List<float> Positions { get; set; } = new List<float>();

        /// <summary>
        /// The Vertex Normals
        /// </summary>
        public List<float> Normals { get; set; } = new List<float>();

        /// <summary>
        /// The Vertex Colors
        /// </summary>
        public List<float> VertexColors { get; set; } = new List<float>();

        /// <summary>
        /// The Vertex Primary Texture Coordinates
        /// </summary>
        public List<float> TextureCoordinates0 { get; set; } = new List<float>();

        /// <summary>
        /// The Vertex Secondary Texture Coordinates
        /// </summary>
        public List<float> TextureCoordinates1 { get; set; } = new List<float>();

        /// <summary>
        /// The Vertex Alphas
        /// </summary>
        public List<float> VertexAlphas { get; set; } = new List<float>();

        /// <summary>
        /// The Vertex Bone Weights
        /// </summary>
        public List<float> BoneWeights { get; set; } = new List<float>();

        /// <summary>
        /// The Vertex BiNormals
        /// </summary>
        public List<float> BiNormals { get; set; } = new List<float>();

        /// <summary>
        /// The Vertex Tangents
        /// </summary>
        public List<float> Tangents { get; set; } = new List<float>();

        /// <summary>
        /// The Indices
        /// </summary>
        public List<int> Indices { get; set; } = new List<int>();

        /// <summary>
        /// Dictionary containing the location of vertex type indices
        /// </summary>
        public Dictionary<string, int> IndexLocDictionary { get; set; }

        /// <summary>
        /// The Vertex Bone Indices
        /// </summary>
        public List<int> BoneIndices { get; set; } = new List<int>();

        /// <summary>
        /// The V count
        /// </summary>
        /// <remarks>
        /// This list contains the number of bone weights per vertex
        /// </remarks>
        public List<int> Vcounts { get; set; } = new List<int>();

        /// <summary>
        /// THe Position Indices
        /// </summary>
        public List<int> PositionIndices { get; set; } = new List<int>();

        /// <summary>
        /// The Normal Indices
        /// </summary>
        public List<int> NormalIndices { get; set; } = new List<int>();

        /// <summary>
        /// The BiNormal Indices
        /// </summary>
        public List<int> BiNormalIndices { get; set; } = new List<int>();

        /// <summary>
        /// The Primary Texture Coordinate Indices
        /// </summary>
        public List<int> TextureCoordinate0Indices { get; set; } = new List<int>();

        /// <summary>
        /// The Secondary Texture Coordinate Indices
        /// </summary>
        public List<int> TextureCoordinate1Indices { get; set; } = new List<int>();

        /// <summary>
        /// The Secondary Texture Coordinate Indices
        /// </summary>
        public List<int> VertexColorIndices { get; set; } = new List<int>();

        /// <summary>
        /// The Secondary Texture Coordinate Indices
        /// </summary>
        public List<int> VertexAlphaIndices { get; set; } = new List<int>();

        /// <summary>
        /// The Parts Dictionary
        /// </summary>
        public Dictionary<int, int> PartsDictionary = new Dictionary<int, int>();

        /// <summary>
        /// The stride for Index values
        /// </summary>
        public int IndexStride { get; set; }

        /// <summary>
        /// The stride for Texture Coordinate values
        /// </summary>
        public int TextureCoordinateStride { get; set; }

        /// <summary>
        /// The stride for Vertex Color values
        /// </summary>
        public int VertexColorStride { get; set; }

        /// <summary>
        /// A flag to determine if the import is from Blender
        /// </summary>
        public bool IsBlender { get; set; }
    }
}