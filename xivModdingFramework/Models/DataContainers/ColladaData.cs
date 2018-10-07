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
        /// The bone strings in the collada file
        /// </summary>
        public string[] Bones { get; set; }

        /// <summary>
        /// The positions in the collada file
        /// </summary>
        public List<float> Positions { get; set; } = new List<float>();

        /// <summary>
        /// The normals in the collada file
        /// </summary>
        public List<float> Normals { get; set; } = new List<float>();

        /// <summary>
        /// The primary texture coordinates in the collada file
        /// </summary>
        public List<float> TextureCoordinates0 { get; set; } = new List<float>();

        /// <summary>
        /// The secondary texture coordinates in the collada file
        /// </summary>
        public List<float> TextureCoordinates1 { get; set; } = new List<float>();

        /// <summary>
        /// The bone weights in the collada file
        /// </summary>
        public List<float> BoneWeights { get; set; } = new List<float>();

        /// <summary>
        /// The BiNormals in the collada file
        /// </summary>
        public List<float> BiNormals { get; set; } = new List<float>();

        /// <summary>
        /// The tangents in the collada file
        /// </summary>
        public List<float> Tangents { get; set; } = new List<float>();

        /// <summary>
        /// The indices in teh collada file
        /// </summary>
        public List<int> Indices { get; set; } = new List<int>();


        public List<int> BoneIndices { get; set; } = new List<int>();

        public List<int> Vcounts { get; set; } = new List<int>();

        public List<int> PositionIndices { get; set; } = new List<int>();

        public List<int> NormalIndices { get; set; } = new List<int>();

        public List<int> BiNormalIndices { get; set; } = new List<int>();

        public List<int> TextureCoordinate0Indices { get; set; } = new List<int>();

        public List<int> TextureCoordinate1Indices { get; set; } = new List<int>();

        public Dictionary<int, int> PartsDictionary = new Dictionary<int, int>();

        public int IndexStride { get; set; }

        public int TextureCoordinateStride { get; set; }

        public bool IsBlender { get; set; }
    }
}