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

using SharpDX.DXGI;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.Enums;
using xivModdingFramework.Textures.FileTypes;
using static xivModdingFramework.Textures.FileTypes.Tex;

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
        public int Height { get; set; }

        /// <summary>
        /// Number of layers in the in the texture file.
        /// </summary>
        public int Layers { get; set; }

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


        /// <summary>
        /// Creates an XivTex object from the uncompressed bytes of a .TEX file.
        /// </summary>
        /// <param name="texData"></param>
        /// <returns></returns>
        public static XivTex FromUncompressedTex(byte[] texData, int offset = 0)
        {
            var _tex = new Tex(XivCache.GameInfo.GameDirectory);
            using (var ms = new MemoryStream(texData)) {
                using (var br = new BinaryReader(ms))
                {
                    return FromUncompressedTex(br, texData.Length, offset);
                }
            }
        }

        /// <summary>
        /// Creates an XivTex object from the uncompressed bytes of a .TEX file.
        /// </summary>
        /// <param name="texData"></param>
        /// <returns></returns>
        public static XivTex FromUncompressedTex(BinaryReader br, int dataSize, long offset = -1)
        {
            if(offset >= 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            } else
            {
                offset = br.BaseStream.Position;
            }

            var header = TexHeader.ReadTexHeader(br);

            var tex = new XivTex();

            tex.TextureFormat = Dat.TextureTypeDictionary[(int)header.TextureFormat];
            tex.Width = header.Width;
            tex.Height = header.Height;


            tex.Layers = header.Depth;
            tex.MipMapCount = header.MipCount;

            // We can technically calculate this size based on the texture format and size information.
            // But it's easier for the moment to just pipe the raw size in.
            var mipDataLength = dataSize - (int)Tex._TexHeaderSize;
            var mipData = br.ReadBytes(mipDataLength);
            tex.TexData = mipData;

            return tex;
        }
    }
}