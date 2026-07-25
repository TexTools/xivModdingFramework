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

using SharpDX.Direct2D1;
using SharpDX.DXGI;
using SharpDX.Toolkit.Graphics;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.Helpers;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.Enums;
using xivModdingFramework.Textures.FileTypes;
using static xivModdingFramework.Textures.FileTypes.Tex;
using SixLabors.ImageSharp;

namespace xivModdingFramework.Textures.DataContainers
{
    /// <summary>
    /// Class that represents an uncompressed .tex file.
    /// 
    /// Kind of scuffed since it also includes a semi-arbitrary 'TexTypePath'
    /// stapled onto it as well representing the original source used for loading 
    /// and estimated usage of the texture file.  However, there is not guarantee
    /// that the file stemmed from that location, or that that is the only location 
    /// or even necessarily correct or only usage of the texture.
    /// </summary>
    public class XivTex : ICloneable
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
        /// The base path this texture originated from/is destined for.
        /// May or may not always be populated, depending on construction format.
        /// </summary>
        public string FilePath { get; set; }


        /// <summary>
        /// Creates an XivTex object from the uncompressed bytes of a .TEX file.
        /// </summary>
        /// <param name="texData"></param>
        /// <returns></returns>
        public static XivTex FromUncompressedTex(byte[] texData, int offset = 0)
        {
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

            tex.TextureFormat = Tex.TextureTypeDictionary[(int)header.TextureFormat];
            tex.Width = header.Width;
            tex.Height = header.Height;


            // I HAVE NO IDEA WHAT I'M DOING WITH MULTILAYER DDS
            tex.Layers = header.ArraySize * header.Depth;
            tex.MipMapCount = header.MipCount;

            // We can technically calculate this size based on the texture format and size information.
            // But it's easier for the moment to just pipe the raw size in.
            var mipDataLength = dataSize - (int)Tex._TexHeaderSize;
            var mipData = br.ReadBytes(mipDataLength);
            tex.TexData = mipData;

            return tex;
        }


        /// <summary>
        /// Converts this XivTex to uncompressed .TEX format bytes.
        /// Note: This conversion is lossy (at the header/metadata level).
        /// XivTex does not store attribute data from the original .TEX header used in its creation.
        /// </summary>
        /// <returns></returns>
        public byte[] ToUncompressedTex()
        {
            var header = Tex.CreateTexFileHeader(TextureFormat, Width, Height, MipMapCount);
            var ret = header.Concat(TexData);
            return ret.ToArray();
        }


        /// <summary>
        /// Converts the DDS-Format pixel data in this XivTex to 8.8.8.8 RGBA Pixel data and returns it.
        /// </summary>
        /// <param name="xivTex">The texture data</param>
        /// <returns>A byte array with the image data</returns>
        public async Task<byte[]> GetRawPixels(int layer = -1)
        {
            var layers = Layers;
            if (layers == 0)
            {
                layers = 1;
            }
            return await DDS.ConvertPixelData(TexData, Width, Height, TextureFormat, layers, layer);
        }

        public object Clone()
        {
            var tex = (XivTex) MemberwiseClone();
            tex.TexData = (byte[]) TexData.Clone();

            return tex;

        }

        public async Task SaveAs(string externalFilePath)
        {

            var ext = Path.GetExtension(externalFilePath).ToLower();

            if (ext == ".tex" || ext == ".atex")
            {
                File.WriteAllBytes(externalFilePath, ToUncompressedTex());
                return;
            }
            else if (ext == ".dds")
            {
                Tex.SaveTexAsDDS(externalFilePath, this);
                return;
            }

            IImageEncoder encoder;
            if (ext == ".bmp")
            {
                encoder = new BmpEncoder()
                {
                    SupportTransparency = true,
                    BitsPerPixel = BmpBitsPerPixel.Pixel32
                };
            }
            else if (ext == ".png")
            {
                encoder = new PngEncoder()
                {
                    BitDepth = PngBitDepth.Bit16
                };
            }
            else
            {
                encoder = new TgaEncoder()
                {
                    Compression = TgaCompression.None,
                    BitsPerPixel = TgaBitsPerPixel.Pixel32,
                };
            };

            var pixelData = await GetRawPixels();
            using (var img = SixLabors.ImageSharp.Image.LoadPixelData<Rgba32>(pixelData, Width, Height))
            {
                using (var fs = File.OpenWrite(externalFilePath))
                {
                    img.Save(fs, encoder);
                }
            }
        }
    }
}