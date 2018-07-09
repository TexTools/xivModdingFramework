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
using xivModdingFramework.Helpers;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.Enums;

namespace xivModdingFramework.Textures.FileTypes
{
    /// <summary>
    /// This class contains the methods that deal with the .tex file type 
    /// </summary>
    public class Tex
    {
        private readonly XivTexType _texType;
        private const string TexExtension = ".tex";
        private readonly XivTex _texInfo;
        private readonly byte[] _rawData;

        public Tex(DirectoryInfo gameDirectory,  TexTypePath ttp)
        {
            var folder = Path.GetDirectoryName(ttp.Path);
            var file = Path.GetFileName(ttp.Path);

            var index = new Index(gameDirectory);
            var dat = new Dat(gameDirectory);

            var offset = index.GetDataOffset(HashGenerator.GetHash(folder), HashGenerator.GetHash(file), ttp.DataFile);
            var (rawData, texData) = dat.GetType4Data(offset, ttp.DataFile);
            _texInfo = texData;
            _rawData = rawData;
            _texType = ttp.Type;
        }

        /// <summary>
        /// Gets the raw pixel data for the texture
        /// </summary>
        public byte[] GetImageData()
        {
            byte[] imageData = null;

            switch (_texInfo.TextureFormat)
            {
                case XivTexFormat.DXT1:
                    imageData = DxtUtil.DecompressDxt1(_rawData, _texInfo.Width, _texInfo.Heigth);
                    break;
                case XivTexFormat.DXT3:
                    imageData = DxtUtil.DecompressDxt3(_rawData, _texInfo.Width, _texInfo.Heigth);
                    break;
                case XivTexFormat.DXT5:
                    imageData = DxtUtil.DecompressDxt5(_rawData, _texInfo.Width, _texInfo.Heigth);
                    break;
                case XivTexFormat.L8:
                case XivTexFormat.A8:
                case XivTexFormat.A4R4G4B4:
                case XivTexFormat.A1R5G5B5:
                case XivTexFormat.A8R8G8B8:
                case XivTexFormat.X8R8G8B8:
                case XivTexFormat.R32F:
                case XivTexFormat.G16R16F:
                case XivTexFormat.G32R32F:
                case XivTexFormat.A16B16G16R16F:
                case XivTexFormat.A32B32G32R32F:
                case XivTexFormat.D16:
                default:
                    imageData = _rawData;
                    break;
            }

            return imageData;
        }


        /// <summary>
        /// Gets the DDS raw data including the DDS header
        /// </summary>
        /// <returns>the raw byte data for the dds file</returns>
        public byte[] GetDDS()
        {
            var DDS = new List<byte>();
            switch (_texType)
            {
                case XivTexType.ColorSet:
                    DDS.AddRange(CreateColorDDSHeader());
                    DDS.AddRange(_rawData);
                    break;
                case XivTexType.Vfx:
                case XivTexType.Diffuse:
                case XivTexType.Specular:
                case XivTexType.Normal:
                case XivTexType.Multi:
                case XivTexType.Mask:
                case XivTexType.Skin:
                case XivTexType.Map:
                case XivTexType.Icon:
                default:
                    DDS.AddRange(CreateDDSHeader());
                    DDS.AddRange(_rawData);
                    break;
            }

            return DDS.ToArray();
        }

        /// <summary>
        /// Gets the texture info
        /// </summary>
        /// <returns>The Texture Info</returns>
        public XivTex GetTextureInfo()
        {
            return _texInfo;
        }


        /// <summary>
        /// Creates the DDS header for given texture data.
        /// <see cref="https://msdn.microsoft.com/en-us/library/windows/desktop/bb943982(v=vs.85).aspx"/>
        /// </summary>
        /// <returns>Byte array containing DDS header</returns>
        private byte[] CreateDDSHeader()
        {
            uint dwPitchOrLinearSize, pfFlags, dwFourCC;
            var header = new List<byte>();

            // DDS header magic number
            const uint dwMagic = 0x20534444;
            header.AddRange(BitConverter.GetBytes(dwMagic));

            // Size of structure. This member must be set to 124.
            const uint dwSize = 124;
            header.AddRange(BitConverter.GetBytes(dwSize));

            // Flags to indicate which members contain valid data.
            const uint dwFlags = 528391;
            header.AddRange(BitConverter.GetBytes(dwFlags));

            // Surface height (in pixels).
            var dwHeight = (uint)_texInfo.Heigth;
            header.AddRange(BitConverter.GetBytes(dwHeight));

            // Surface width (in pixels).
            var dwWidth = (uint)_texInfo.Width;
            header.AddRange(BitConverter.GetBytes(dwWidth));

            // The pitch or number of bytes per scan line in an uncompressed texture; the total number of bytes in the top level texture for a compressed texture.
            if (_texInfo.TextureFormat == XivTexFormat.A16B16G16R16F)
            {
                dwPitchOrLinearSize = 512;
            }
            else if (_texInfo.TextureFormat == XivTexFormat.A8R8G8B8)
            {
                dwPitchOrLinearSize = (dwHeight * dwWidth) * 4;
            }
            else if (_texInfo.TextureFormat == XivTexFormat.DXT1)
            {
                dwPitchOrLinearSize = (dwHeight * dwWidth) / 2;
            }
            else if (_texInfo.TextureFormat == XivTexFormat.A4R4G4B4 || _texInfo.TextureFormat == XivTexFormat.A1R5G5B5)
            {
                dwPitchOrLinearSize = (dwHeight * dwWidth) * 2;
            }
            else
            {
                dwPitchOrLinearSize = dwHeight * dwWidth;
            }
            header.AddRange(BitConverter.GetBytes(dwPitchOrLinearSize));


            // Depth of a volume texture (in pixels), otherwise unused.
            const uint dwDepth = 0;
            header.AddRange(BitConverter.GetBytes(dwDepth));

            // Number of mipmap levels, otherwise unused.
            var dwMipMapCount = (uint)_texInfo.MipMapCount;
            header.AddRange(BitConverter.GetBytes(dwMipMapCount));

            // Unused.
            var dwReserved1 = new byte[44];
            Array.Clear(dwReserved1, 0, 44);
            header.AddRange(dwReserved1);

            // DDS_PIXELFORMAT start

            // Structure size; set to 32 (bytes).
            const uint pfSize = 32;
            header.AddRange(BitConverter.GetBytes(pfSize));

            switch (_texInfo.TextureFormat)
            {
                // Values which indicate what type of data is in the surface.
                case XivTexFormat.A8R8G8B8:
                case XivTexFormat.A4R4G4B4:
                case XivTexFormat.A1R5G5B5:
                    pfFlags = 65;
                    break;
                case XivTexFormat.A8:
                    pfFlags = 2;
                    break;
                default:
                    pfFlags = 4;
                    break;
            }
            header.AddRange(BitConverter.GetBytes(pfFlags));

            switch (_texInfo.TextureFormat)
            {
                // Four-character codes for specifying compressed or custom formats.
                case XivTexFormat.DXT1:
                    dwFourCC = 0x31545844;
                    break;
                case XivTexFormat.DXT5:
                    dwFourCC = 0x35545844;
                    break;
                case XivTexFormat.DXT3:
                    dwFourCC = 0x33545844;
                    break;
                case XivTexFormat.A16B16G16R16F:
                    dwFourCC = 0x71;
                    break;
                case XivTexFormat.A8R8G8B8:
                case XivTexFormat.A8:
                case XivTexFormat.A4R4G4B4:
                case XivTexFormat.A1R5G5B5:
                    dwFourCC = 0;
                    break;
                default:
                    return null;
            }
            header.AddRange(BitConverter.GetBytes(dwFourCC));

            switch (_texInfo.TextureFormat)
            {
                case XivTexFormat.A8R8G8B8:
                {
                    // Number of bits in an RGB (possibly including alpha) format.
                    const uint dwRGBBitCount = 32;
                    header.AddRange(BitConverter.GetBytes(dwRGBBitCount));

                    // Red (or lumiannce or Y) mask for reading color data. 
                    const uint dwRBitMask = 16711680;
                    header.AddRange(BitConverter.GetBytes(dwRBitMask));

                    // Green (or U) mask for reading color data.
                    const uint dwGBitMask = 65280;
                    header.AddRange(BitConverter.GetBytes(dwGBitMask));

                    // Blue (or V) mask for reading color data.
                    const uint dwBBitMask = 255;
                    header.AddRange(BitConverter.GetBytes(dwBBitMask));

                    // Alpha mask for reading alpha data.
                    const uint dwABitMask = 4278190080;
                    header.AddRange(BitConverter.GetBytes(dwABitMask));

                    // DDS_PIXELFORMAT End

                    // Specifies the complexity of the surfaces stored.
                    const uint dwCaps = 4096;
                    header.AddRange(BitConverter.GetBytes(dwCaps));

                    // dwCaps2, dwCaps3, dwCaps4, dwReserved2.
                    // Unused.
                    var blank1 = new byte[16];
                    header.AddRange(blank1);

                    break;
                }
                case XivTexFormat.A8:
                {
                    // Number of bits in an RGB (possibly including alpha) format.
                    const uint dwRGBBitCount = 8;
                    header.AddRange(BitConverter.GetBytes(dwRGBBitCount));

                    // Red (or lumiannce or Y) mask for reading color data. 
                    const uint dwRBitMask = 0;
                    header.AddRange(BitConverter.GetBytes(dwRBitMask));

                    // Green (or U) mask for reading color data.
                    const uint dwGBitMask = 0;
                    header.AddRange(BitConverter.GetBytes(dwGBitMask));

                    // Blue (or V) mask for reading color data.
                    const uint dwBBitMask = 0;
                    header.AddRange(BitConverter.GetBytes(dwBBitMask));

                    // Alpha mask for reading alpha data.
                    const uint dwABitMask = 255;
                    header.AddRange(BitConverter.GetBytes(dwABitMask));

                    // DDS_PIXELFORMAT End

                    // Specifies the complexity of the surfaces stored.
                    const uint dwCaps = 4096;
                    header.AddRange(BitConverter.GetBytes(dwCaps));

                    // dwCaps2, dwCaps3, dwCaps4, dwReserved2.
                    // Unused.
                    var blank1 = new byte[16];
                    header.AddRange(blank1);
                    break;
                }
                case XivTexFormat.A1R5G5B5:
                {
                    // Number of bits in an RGB (possibly including alpha) format.
                    const uint dwRGBBitCount = 16;
                    header.AddRange(BitConverter.GetBytes(dwRGBBitCount));

                    // Red (or lumiannce or Y) mask for reading color data. 
                    const uint dwRBitMask = 31744;
                    header.AddRange(BitConverter.GetBytes(dwRBitMask));

                    // Green (or U) mask for reading color data.
                    const uint dwGBitMask = 992;
                    header.AddRange(BitConverter.GetBytes(dwGBitMask));

                    // Blue (or V) mask for reading color data.
                    const uint dwBBitMask = 31;
                    header.AddRange(BitConverter.GetBytes(dwBBitMask));

                    // Alpha mask for reading alpha data.
                    const uint dwABitMask = 32768;
                    header.AddRange(BitConverter.GetBytes(dwABitMask));

                    // DDS_PIXELFORMAT End

                    // Specifies the complexity of the surfaces stored.
                    const uint dwCaps = 4096;
                    header.AddRange(BitConverter.GetBytes(dwCaps));

                    // dwCaps2, dwCaps3, dwCaps4, dwReserved2.
                    // Unused.
                    var blank1 = new byte[16];
                    header.AddRange(blank1);
                    break;
                }
                case XivTexFormat.A4R4G4B4:
                {
                    // Number of bits in an RGB (possibly including alpha) format.
                    const uint dwRGBBitCount = 16;
                    header.AddRange(BitConverter.GetBytes(dwRGBBitCount));

                    // Red (or lumiannce or Y) mask for reading color data. 
                    const uint dwRBitMask = 3840;
                    header.AddRange(BitConverter.GetBytes(dwRBitMask));

                    // Green (or U) mask for reading color data.
                    const uint dwGBitMask = 240;
                    header.AddRange(BitConverter.GetBytes(dwGBitMask));

                    // Blue (or V) mask for reading color data.
                    const uint dwBBitMask = 15;
                    header.AddRange(BitConverter.GetBytes(dwBBitMask));

                    // Alpha mask for reading alpha data.
                    const uint dwABitMask = 61440;
                    header.AddRange(BitConverter.GetBytes(dwABitMask));

                    // DDS_PIXELFORMAT End

                    // Specifies the complexity of the surfaces stored.
                    const uint dwCaps = 4096;
                    header.AddRange(BitConverter.GetBytes(dwCaps));

                    // dwCaps2, dwCaps3, dwCaps4, dwReserved2.
                    // Unused.
                    var blank1 = new byte[16];
                    header.AddRange(blank1);
                    break;
                }
                default:
                {
                    // dwRGBBitCount, dwRBitMask, dwGBitMask, dwBBitMask, dwABitMask, dwCaps, dwCaps2, dwCaps3, dwCaps4, dwReserved2.
                    // Unused.
                    var blank1 = new byte[40];
                    header.AddRange(blank1);
                    break;
                }
            }

            return header.ToArray();
        }

        /// <summary>
        /// Creates the DDS header for given texture data.
        /// <see cref="https://msdn.microsoft.com/en-us/library/windows/desktop/bb943982(v=vs.85).aspx"/>
        /// </summary>
        /// <returns>Byte array containing DDS header</returns>
        private byte[] CreateColorDDSHeader()
        {
            var header = new List<byte>();

            // DDS header magic number
            const uint dwMagic = 0x20534444;
            header.AddRange(BitConverter.GetBytes(dwMagic));

            // Size of structure. This member must be set to 124.
            const uint dwSize = 124;
            header.AddRange(BitConverter.GetBytes(dwSize));

            // Flags to indicate which members contain valid data.
            const uint dwFlags = 528399;
            header.AddRange(BitConverter.GetBytes(dwFlags));

            // Surface height (in pixels).
            const uint dwHeight = 16;
            header.AddRange(BitConverter.GetBytes(dwHeight));

            // Surface width (in pixels).
            const uint dwWidth = 4;
            header.AddRange(BitConverter.GetBytes(dwWidth));

            // The pitch or number of bytes per scan line in an uncompressed texture; the total number of bytes in the top level texture for a compressed texture.
            const uint dwPitchOrLinearSize = 512;
            header.AddRange(BitConverter.GetBytes(dwPitchOrLinearSize));

            // Depth of a volume texture (in pixels), otherwise unused.
            const uint dwDepth = 0;
            header.AddRange(BitConverter.GetBytes(dwDepth));

            // Number of mipmap levels, otherwise unused.
            const uint dwMipMapCount = 0;
            header.AddRange(BitConverter.GetBytes(dwMipMapCount));

            // Unused.
            var dwReserved1 = new byte[44];
            header.AddRange(dwReserved1);

            // DDS_PIXELFORMAT start

            // Structure size; set to 32 (bytes).
            const uint pfSize = 32;
            header.AddRange(BitConverter.GetBytes(pfSize));

            // Values which indicate what type of data is in the surface.
            const uint pfFlags = 4;
            header.AddRange(BitConverter.GetBytes(pfFlags));

            // Four-character codes for specifying compressed or custom formats.
            const uint dwFourCC = 0x71;
            header.AddRange(BitConverter.GetBytes(dwFourCC));

            // dwRGBBitCount, dwRBitMask, dwGBitMask, dwBBitMask, dwABitMask, dwCaps, dwCaps2, dwCaps3, dwCaps4, dwReserved2.
            // Unused.
            var blank1 = new byte[40];
            header.AddRange(blank1);

            return header.ToArray();
        }
    }
}