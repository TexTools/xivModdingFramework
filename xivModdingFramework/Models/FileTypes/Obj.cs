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
using System.IO;
using System.Text;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Models.DataContainers;

namespace xivModdingFramework.Models.FileTypes
{
    /// <summary>
    /// This class handles Obj files
    /// </summary>
    public class Obj
    {
        private readonly DirectoryInfo _gameDirectory;

        public Obj(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
        }

        /// <summary>
        /// Exports each mesh as an obj file
        /// </summary>
        /// <param name="item">The item to be exported</param>
        /// <param name="xivMdl">The XivMdl model data</param>
        /// <param name="saveLocation">The location in which to save the obj file</param>
        public void ExportObj(IItemModel item, XivMdl xivMdl, DirectoryInfo saveLocation, XivRace race)
        {
            var meshes = xivMdl.LoDList[0].MeshDataList;

            var path = $"{IOUtil.MakeItemSavePath(item, saveLocation, race)}\\3D";

            Directory.CreateDirectory(path);

            var meshNum = 0;
            foreach (var meshData in meshes)
            {
                var modelName = $"{Path.GetFileNameWithoutExtension(xivMdl.MdlPath.File)}_{meshNum}";
                var savePath = Path.Combine(path, modelName) + ".obj";

                meshNum++;

                File.WriteAllText(savePath, ExportObj(meshData));
            }
        }

        public string ExportObj(MeshData meshData)
        {
            var sb = new StringBuilder();

            var vertexData = meshData.VertexData;

            foreach (var vertexDataPosition in vertexData.Positions)
            {
                sb.AppendLine($"v {vertexDataPosition.X:N5} {vertexDataPosition.Y:N5} {vertexDataPosition.Z:N5}");
            }

            foreach (var texCoord in vertexData.TextureCoordinates0)
            {
                var ox = texCoord.X - Math.Truncate(texCoord.X);
                var oy = texCoord.Y - Math.Truncate(texCoord.Y);
                sb.AppendLine($"vt {ox:N5} {(1 - oy):N5}");
            }

            foreach (var vertexDataNormal in vertexData.Normals)
            {
                sb.AppendLine($"vn {vertexDataNormal.X:N5} {vertexDataNormal.Y:N5} {vertexDataNormal.Z:N5}");
            }

            for (var i = 0; i < vertexData.Indices.Count; i += 3)
            {
                var index1 = vertexData.Indices[i] + 1;
                var index2 = vertexData.Indices[i + 1] + 1;
                var index3 = vertexData.Indices[i + 2] + 1;
                sb.AppendLine($"f {index1}/{index1}/{index1} {index2}/{index2}/{index2} {index3}/{index3}/{index3}");
            }

            return sb.ToString();
        }
    }
}