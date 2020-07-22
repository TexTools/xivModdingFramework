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
        public void ExportObj(TTModel model, string path)
        {
            var meshGroups = model.MeshGroups;

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            try
            {
                var meshNum = 0;
                foreach (var mesh in meshGroups)
                {
                    var modelName = $"{Path.GetFileNameWithoutExtension(path)}_{meshNum}.obj";
                    var savePath = Path.GetDirectoryName(path) + "\\" + modelName;

                    meshNum++;
                    File.WriteAllText(savePath, ExportObj(mesh));
                }
            } catch(Exception ex)
            {
                throw ex;
            }
        }

        public string ExportObj(TTMeshGroup mesh)
        {
            var sb = new StringBuilder();


            // Merge the index and vertex lists.
            var vertices = new List<TTVertex>((int)mesh.VertexCount);
            var indices = new List<int>((int)mesh.IndexCount);
            foreach (var p in mesh.Parts)
            {
                var preVertexCount = vertices.Count;
                vertices.AddRange(p.Vertices);
                foreach (var i in p.TriangleIndices)
                {
                    indices.Add(i + preVertexCount);
                }
            }

            foreach (var v in vertices)
            {
                sb.AppendLine($"v {v.Position.X:N5} {v.Position.Y:N5} {v.Position.Z:N5}");
            }

            foreach (var v in vertices)
            {
                var ox = v.UV1.X - Math.Truncate(v.UV1.X);
                var oy = v.UV1.Y - Math.Truncate(v.UV1.Y);
                sb.AppendLine($"vt {ox:N5} {(1 - oy):N5}");
            }

            foreach (var v in vertices)
            {
                sb.AppendLine($"vn {v.Normal.X:N5} {v.Normal.Y:N5} {v.Normal.Z:N5}");
            }

            for (var i = 0; i < indices.Count; i += 3)
            {
                var index1 = indices[i] + 1;
                var index2 = indices[i + 1] + 1;
                var index3 = indices[i + 2] + 1;
                sb.AppendLine($"f {index1}/{index1}/{index1} {index2}/{index2}/{index2} {index3}/{index3}/{index3}");
            }


            return sb.ToString();
        }
    }
}