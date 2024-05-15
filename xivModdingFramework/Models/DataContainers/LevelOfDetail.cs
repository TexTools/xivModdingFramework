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

using System;
using System.Collections.Generic;
using xivModdingFramework.Materials.DataContainers;

namespace xivModdingFramework.Models.DataContainers
{
    /// <summary>
    /// This class contains properties for the Level of Detail data
    /// </summary>
    public class LevelOfDetail
    {
        public int TotalMeshCount 
        {
            get
            {
                return MeshCount
                    + WaterMeshCount
                    + ExtraMeshCount
                    + ShadowMeshCount
                    + TerrainShadowMeshCount
                    + FogMeshCount;
            }
        }

        public EMeshType GetMeshType(int offset)
        {
            if (offset < MeshIndex) {
                throw new Exception("Invalid Mesh Offset");
            } else if (offset < WaterMeshIndex) {
                return EMeshType.Normal;
            } else if (offset < ExtraMeshIndex) {
                return EMeshType.Water;
            } else if (offset < ShadowMeshIndex) {
                return EMeshType.Extra;
            } else if (offset < TerrainShadowMeshIndex || (TerrainShadowMeshIndex == 0 && offset < FogMeshIndex)) {
                return EMeshType.Shadow;
            } else if(offset < FogMeshIndex && TerrainShadowMeshCount > 0) {
                return EMeshType.TerrainShadow;
            } else {
                return EMeshType.Fog;
            }
        }

        /// <summary>
        /// The offset to the mesh data block
        /// </summary>
        public ushort MeshIndex { get; set; }

        /// <summary>
        /// The number of meshes to use
        /// </summary>
        public short MeshCount { get; set; }

        public ushort ExtraMeshIndex { get; set; }
        public ushort ExtraMeshCount { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public float ModelLoDRange { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public float TextureLoDRange { get; set; }

        /// <summary>
        /// Mesh End
        /// </summary>
        public short WaterMeshIndex { get; set; }

        /// <summary>
        /// Extra Mesh Count
        /// </summary>
        public short WaterMeshCount { get; set; }

        /// <summary>
        /// Mesh Sum
        /// </summary>
        public short ShadowMeshIndex{ get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short ShadowMeshCount { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short TerrainShadowMeshIndex { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short TerrainShadowMeshCount { get; set; }


        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short FogMeshIndex { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short FogMeshCount { get; set; }


        /// <summary>
        /// Unknown Usage
        /// </summary>
        public int EdgeGeometrySize { get; set; }

        /// <summary>
        /// The offset at which the index data begins
        /// </summary>
        public int EdgeGeometryOffset { get; set; }

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