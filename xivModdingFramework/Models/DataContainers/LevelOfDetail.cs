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
using System.Linq;
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
                return MeshTypes.Sum(x => x.Key == EMeshType.TerrainShadow ? 0 :  x.Value.Count);
            }
        }

        public bool HasExtraMeshes
        {
            get {
                var extraStart = (int)EMeshType.LightShaft;
                var extraMeshCount = 10;

                for(int i = extraStart; i < extraStart + extraMeshCount; i++)
                {
                    var e = (EMeshType)i;
                    if (MeshTypes.ContainsKey(e))
                    {
                        if (MeshTypes[e].Count > 0)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        public EMeshType GetMeshType(int offset)
        {
            foreach (var kv in MeshTypes)
            {
                if (offset >= kv.Value.Offset && offset < kv.Value.Offset + kv.Value.Count)
                {
                    return kv.Key;
                }
            }
            throw new Exception("Unknown Mesh Type.");

        }

        public Dictionary<EMeshType, (ushort Offset, ushort Count)> MeshTypes = new Dictionary<EMeshType, (ushort Offset, ushort Count)>();

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public float ModelLoDRange { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public float TextureLoDRange { get; set; }

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
        /// This appears to be multiple individual byte values, with the first 2 being related to neck morph data
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