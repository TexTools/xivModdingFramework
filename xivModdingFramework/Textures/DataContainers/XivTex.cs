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

using xivModdingFramework.Textures.Enums;

namespace xivModdingFramework.Textures.DataContainers
{
    /// <summary>
    /// This class holds information for a Texture
    /// </summary>
    public class XivTex
    {
        /// <summary>
        /// The Textures Format <see cref="XivTexFormat"/>
        /// </summary>
        public XivTexFormat TextureFormat { get; set; }

        /// <summary>
        /// The width of the texture
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// The height of the texture
        /// </summary>
        public int Heigth { get; set; }

        /// <summary>
        /// The amount of mipmaps the texture contains
        /// </summary>
        public int MipMapCount { get; set; }

        /// <summary>
        /// The texture byte data
        /// </summary>
        public byte[] TexData { get; set; }

        /// <summary>
        /// The type and path of the texture
        /// </summary>
        public TexTypePath TextureTypeAndPath { get; set; }

    }
}