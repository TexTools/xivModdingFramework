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
    /// This class holds the data for the textures to be used on the 3D Model
    /// </summary>
    public class ModelTextureData
    {
        public int Width { get; set; }

        public int Height { get; set; }

        public byte[] Diffuse { get; set; }

        public byte[] Specular { get; set; }

        public byte[] Normal { get; set; }

        public byte[] Alpha { get; set; }

        public byte[] Emissive { get; set; }
    }
}