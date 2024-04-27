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
using System.Threading.Tasks;
using xivModdingFramework.Helpers;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.Enums;

namespace xivModdingFramework.Textures.FileTypes
{
    /// <summary>
    /// This class deals with dds file types
    /// </summary>
    public static class DDS
    {
        /// <summary>
        /// Creates a DDS file for the given Texture
        /// </summary>
        /// <param name="saveDirectory">The directory to save the dds file to</param>
        /// <param name="xivTex">The Texture information</param>
        public static void MakeDDS(XivTex xivTex, string savePath)
        {
            var DDS = new List<byte>();
            switch (xivTex.TextureTypeAndPath.Type)
            {
                case XivTexType.ColorSet:
                    DDS.AddRange(CreateColorDDSHeader());
                    DDS.AddRange(xivTex.TexData);
                    break;
                case XivTexType.Vfx:
                case XivTexType.Diffuse:
                case XivTexType.Specular:
                case XivTexType.Normal:
                case XivTexType.Mask:
                case XivTexType.Skin:
                case XivTexType.Map:
                case XivTexType.Icon:
                default:
                    DDS.AddRange(CreateDDSHeader(xivTex));

                    var data = xivTex.TexData;
                    if (xivTex.TextureFormat == XivTexFormat.A8R8G8B8 && xivTex.Layers > 1)
                    {
                        data = ShiftLayers(data);
                    }
                    DDS.AddRange(data);
                    break;
            }

            File.WriteAllBytes(savePath, DDS.ToArray());
        }


        public static byte[] MakeDDS(byte[] data, XivTexFormat format, int width, int height, int layers, int mipCount)
        {
            var DDS = new List<byte>();
            DDS.AddRange(CreateDDSHeader(format, width, height, layers, mipCount));

            if (format == XivTexFormat.A8R8G8B8 && layers > 1)
            {
                data = ShiftLayers(data);
            }
            DDS.AddRange(data);

            return DDS.ToArray();
        }

        // This is a simple shift of the layers around in order to convert ARGB to RGBA
        private static byte[] ShiftLayers(byte[] data)
        {
            for(int i = 0; i < data.Length; i += 4)
            {
                var alpha = data[i];
                var red = data[i + 1];
                var green  = data[i + 2];
                var blue = data[i + 3];

                data[i] = red;
                data[i + 1] = green;
                data[i + 2] = blue;
                data[i + 3] = alpha;
            }
            return data;
        }

        /// <summary>
        /// Creates the DDS header for given texture data.
        /// <see cref="https://msdn.microsoft.com/en-us/library/windows/desktop/bb943982(v=vs.85).aspx"/>
        /// </summary>
        /// <returns>Byte array containing DDS header</returns>
        private static byte[] CreateDDSHeader(XivTex xivTex)
        {
            return CreateDDSHeader(xivTex.TextureFormat, xivTex.Width, xivTex.Height, xivTex.Layers, xivTex.MipMapCount);
        }


        private static byte[] CreateDDSHeader(XivTexFormat format, int width, int height, int layers, int mipCount)
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
            uint dwFlags = 528391;
            if(layers > 1)
            {
                dwFlags = 0x00000004;
            }
            header.AddRange(BitConverter.GetBytes(dwFlags));

            // Surface height (in pixels). 
            var dwHeight = (uint)height;
            header.AddRange(BitConverter.GetBytes(dwHeight));

            // Surface width (in pixels).
            var dwWidth = (uint)width;
            header.AddRange(BitConverter.GetBytes(dwWidth));

            // DX10 format magic number
            const uint fourccDX10 = 0x30315844;

            // The pitch or number of bytes per scan line in an uncompressed texture; the total number of bytes in the top level texture for a compressed texture.
            if (format == XivTexFormat.A16B16G16R16F)
            {
                dwPitchOrLinearSize = 512;
            }
            else if (format == XivTexFormat.A8R8G8B8)
            {
                dwPitchOrLinearSize = (dwHeight * dwWidth) * 4;
            }
            else if (format == XivTexFormat.DXT1)
            {
                dwPitchOrLinearSize = (dwHeight * dwWidth) / 2;
            }
            else if (format == XivTexFormat.A4R4G4B4 || format == XivTexFormat.A1R5G5B5)
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
            var dwMipMapCount = (uint)mipCount;
            header.AddRange(BitConverter.GetBytes(dwMipMapCount));

            // Unused.
            var dwReserved1 = new byte[44];
            Array.Clear(dwReserved1, 0, 44);
            header.AddRange(dwReserved1);

            // DDS_PIXELFORMAT start

            // Structure size; set to 32 (bytes).
            const uint pfSize = 32;
            header.AddRange(BitConverter.GetBytes(pfSize));

            switch (format)
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

            switch (format)
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
                case XivTexFormat.BC5:
                case XivTexFormat.BC7:
                    dwFourCC = fourccDX10;
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

            if(layers > 1)
            {
                dwFourCC = fourccDX10;
            }

            header.AddRange(BitConverter.GetBytes(dwFourCC));

            switch (format)
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

            // Need to write DX10 header here.
            if(dwFourCC == fourccDX10)
            {
                // DXGI_FORMAT dxgiFormat
                uint dxgiFormat = 0;
                if (format == XivTexFormat.DXT1) {
                    dxgiFormat = (uint)DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM;
                } else if (format == XivTexFormat.DXT3)
                {
                    dxgiFormat = (uint)DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM;
                } else if (format == XivTexFormat.DXT5)
                {
                    dxgiFormat = (uint)DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM;
                } else if (format == XivTexFormat.BC5)
                {
                    dxgiFormat = (uint)DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM;
                } else if (format == XivTexFormat.BC7)
                {
                    dxgiFormat = (uint)DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM;
                } else {
                    dxgiFormat = (uint)DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM;
                }
                header.AddRange(BitConverter.GetBytes(dxgiFormat));

                // D3D10_RESOURCE_DIMENSION resourceDimension
                header.AddRange(BitConverter.GetBytes((int)3));


                // UINT miscFlag
                header.AddRange(BitConverter.GetBytes((int)0));

                // UINT arraySize
                header.AddRange(BitConverter.GetBytes(layers));

                // UINT miscFlags2
                header.AddRange(BitConverter.GetBytes((int)0));
            }

            return header.ToArray();
        }


        /// <summary>
        /// Creates the DDS header for given texture data.
        /// <see cref="https://msdn.microsoft.com/en-us/library/windows/desktop/bb943982(v=vs.85).aspx"/>
        /// </summary>
        /// <returns>Byte array containing DDS header</returns>
        private static byte[] CreateColorDDSHeader()
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

        /// <summary>
        /// Reads and parses data from the DDS file to be imported.
        /// </summary>
        /// <param name="br">The currently active BinaryReader.</param>
        /// <param name="xivTex">The Texture data.</param>
        /// <param name="newWidth">The width of the DDS texture to be imported.</param>
        /// <param name="newHeight">The height of the DDS texture to be imported.</param>
        /// <param name="newMipCount">The number of mipmaps the DDS texture to be imported contains.</param>
        /// <returns>A tuple containing the compressed DDS data, a list of offsets to the mipmap parts, a list with the number of parts per mipmap.</returns>
        public static async Task<(List<byte> compressedDDS, List<short> mipPartOffsets, List<short> mipPartCounts)> ReadDDS(BinaryReader br, XivTexFormat format, int newWidth, int newHeight, int newMipCount)
        {
            var compressedDDS = new List<byte>();
            var mipPartOffsets = new List<short>();
            var mipPartCount = new List<short>();

            int mipLength;

            switch (format)
            {
                case XivTexFormat.DXT1:
                    mipLength = (newWidth * newHeight) / 2;
                    break;
                case XivTexFormat.DXT5:
                case XivTexFormat.BC5:
                case XivTexFormat.BC7:
                case XivTexFormat.A8:
                    mipLength = newWidth * newHeight;
                    break;
                    mipLength = newWidth * newHeight;
                    break;
                case XivTexFormat.A1R5G5B5:
                case XivTexFormat.A4R4G4B4:
                    mipLength = (newWidth * newHeight) * 2;
                    break;
                case XivTexFormat.L8:
                case XivTexFormat.A8R8G8B8:
                case XivTexFormat.X8R8G8B8:
                case XivTexFormat.R32F:
                case XivTexFormat.G16R16F:
                case XivTexFormat.G32R32F:
                case XivTexFormat.A16B16G16R16F:
                case XivTexFormat.A32B32G32R32F:
                case XivTexFormat.DXT3:
                case XivTexFormat.D16:
                default:
                    mipLength = (newWidth * newHeight) * 4;
                    break;
            }

            br.BaseStream.Seek(80, SeekOrigin.Begin);

            br.ReadInt32();
            var texType = br.ReadInt32();

            // DX10 format magic number
            const uint fourccDX10 = 0x30315844;
            var extraHeaderBytes = (texType == fourccDX10 ? 20 : 0); // sizeof DDS_HEADER_DXT10
            br.BaseStream.Seek(128 + extraHeaderBytes, SeekOrigin.Begin);

            for (var i = 0; i < newMipCount; i++)
            {
                var mipParts = (int)Math.Ceiling(mipLength / 16000f);
                mipPartCount.Add((short)mipParts);

                if (mipParts > 1)
                {
                    for (var j = 0; j < mipParts; j++)
                    {
                        int uncompLength;
                        var comp = true;

                        if (j == mipParts - 1)
                        {
                            uncompLength = mipLength % 16000;
                        }
                        else
                        {
                            uncompLength = 16000;
                        }

                        var uncompBytes = br.ReadBytes(uncompLength);
                        var compressed = await IOUtil.Compressor(uncompBytes);

                        if (compressed.Length > uncompLength)
                        {
                            compressed = uncompBytes;
                            comp = false;
                        }

                        compressedDDS.AddRange(BitConverter.GetBytes(16));
                        compressedDDS.AddRange(BitConverter.GetBytes(0));

                        compressedDDS.AddRange(!comp
                            ? BitConverter.GetBytes(32000)
                            : BitConverter.GetBytes(compressed.Length));

                        compressedDDS.AddRange(BitConverter.GetBytes(uncompLength));
                        compressedDDS.AddRange(compressed);

                        var padding = 128 - (compressed.Length % 128);

                        compressedDDS.AddRange(new byte[padding]);

                        mipPartOffsets.Add((short)(compressed.Length + padding + 16));
                    }
                }
                else
                {
                    int uncompLength;
                    var comp = true;

                    if (mipLength != 16000)
                    {
                        uncompLength = mipLength % 16000;
                    }
                    else
                    {
                        uncompLength = 16000;
                    }

                    var uncompBytes = br.ReadBytes(uncompLength);
                    var compressed = await IOUtil.Compressor(uncompBytes);

                    if (compressed.Length > uncompLength)
                    {
                        compressed = uncompBytes;
                        comp = false;
                    }

                    compressedDDS.AddRange(BitConverter.GetBytes(16));
                    compressedDDS.AddRange(BitConverter.GetBytes(0));

                    compressedDDS.AddRange(!comp
                        ? BitConverter.GetBytes(32000)
                        : BitConverter.GetBytes(compressed.Length));

                    compressedDDS.AddRange(BitConverter.GetBytes(uncompLength));
                    compressedDDS.AddRange(compressed);

                    var padding = 128 - (compressed.Length % 128);

                    compressedDDS.AddRange(new byte[padding]);

                    mipPartOffsets.Add((short)(compressed.Length + padding + 16));
                }

                if (mipLength > 32)
                {
                    mipLength = mipLength / 4;
                }
                else
                {
                    mipLength = 8;
                }
            }

            return (compressedDDS, mipPartOffsets, mipPartCount);
        }

        public enum DXGI_FORMAT : uint
        {
            DXGI_FORMAT_UNKNOWN,
            DXGI_FORMAT_R32G32B32A32_TYPELESS,
            DXGI_FORMAT_R32G32B32A32_FLOAT,
            DXGI_FORMAT_R32G32B32A32_UINT,
            DXGI_FORMAT_R32G32B32A32_SINT,
            DXGI_FORMAT_R32G32B32_TYPELESS,
            DXGI_FORMAT_R32G32B32_FLOAT,
            DXGI_FORMAT_R32G32B32_UINT,
            DXGI_FORMAT_R32G32B32_SINT,
            DXGI_FORMAT_R16G16B16A16_TYPELESS,
            DXGI_FORMAT_R16G16B16A16_FLOAT,
            DXGI_FORMAT_R16G16B16A16_UNORM,
            DXGI_FORMAT_R16G16B16A16_UINT,
            DXGI_FORMAT_R16G16B16A16_SNORM,
            DXGI_FORMAT_R16G16B16A16_SINT,
            DXGI_FORMAT_R32G32_TYPELESS,
            DXGI_FORMAT_R32G32_FLOAT,
            DXGI_FORMAT_R32G32_UINT,
            DXGI_FORMAT_R32G32_SINT,
            DXGI_FORMAT_R32G8X24_TYPELESS,
            DXGI_FORMAT_D32_FLOAT_S8X24_UINT,
            DXGI_FORMAT_R32_FLOAT_X8X24_TYPELESS,
            DXGI_FORMAT_X32_TYPELESS_G8X24_UINT,
            DXGI_FORMAT_R10G10B10A2_TYPELESS,
            DXGI_FORMAT_R10G10B10A2_UNORM,
            DXGI_FORMAT_R10G10B10A2_UINT,
            DXGI_FORMAT_R11G11B10_FLOAT,
            DXGI_FORMAT_R8G8B8A8_TYPELESS,
            DXGI_FORMAT_R8G8B8A8_UNORM,
            DXGI_FORMAT_R8G8B8A8_UNORM_SRGB,
            DXGI_FORMAT_R8G8B8A8_UINT,
            DXGI_FORMAT_R8G8B8A8_SNORM,
            DXGI_FORMAT_R8G8B8A8_SINT,
            DXGI_FORMAT_R16G16_TYPELESS,
            DXGI_FORMAT_R16G16_FLOAT,
            DXGI_FORMAT_R16G16_UNORM,
            DXGI_FORMAT_R16G16_UINT,
            DXGI_FORMAT_R16G16_SNORM,
            DXGI_FORMAT_R16G16_SINT,
            DXGI_FORMAT_R32_TYPELESS,
            DXGI_FORMAT_D32_FLOAT,
            DXGI_FORMAT_R32_FLOAT,
            DXGI_FORMAT_R32_UINT,
            DXGI_FORMAT_R32_SINT,
            DXGI_FORMAT_R24G8_TYPELESS,
            DXGI_FORMAT_D24_UNORM_S8_UINT,
            DXGI_FORMAT_R24_UNORM_X8_TYPELESS,
            DXGI_FORMAT_X24_TYPELESS_G8_UINT,
            DXGI_FORMAT_R8G8_TYPELESS,
            DXGI_FORMAT_R8G8_UNORM,
            DXGI_FORMAT_R8G8_UINT,
            DXGI_FORMAT_R8G8_SNORM,
            DXGI_FORMAT_R8G8_SINT,
            DXGI_FORMAT_R16_TYPELESS,
            DXGI_FORMAT_R16_FLOAT,
            DXGI_FORMAT_D16_UNORM,
            DXGI_FORMAT_R16_UNORM,
            DXGI_FORMAT_R16_UINT,
            DXGI_FORMAT_R16_SNORM,
            DXGI_FORMAT_R16_SINT,
            DXGI_FORMAT_R8_TYPELESS,
            DXGI_FORMAT_R8_UNORM,
            DXGI_FORMAT_R8_UINT,
            DXGI_FORMAT_R8_SNORM,
            DXGI_FORMAT_R8_SINT,
            DXGI_FORMAT_A8_UNORM,
            DXGI_FORMAT_R1_UNORM,
            DXGI_FORMAT_R9G9B9E5_SHAREDEXP,
            DXGI_FORMAT_R8G8_B8G8_UNORM,
            DXGI_FORMAT_G8R8_G8B8_UNORM,
            DXGI_FORMAT_BC1_TYPELESS,
            DXGI_FORMAT_BC1_UNORM,
            DXGI_FORMAT_BC1_UNORM_SRGB,
            DXGI_FORMAT_BC2_TYPELESS,
            DXGI_FORMAT_BC2_UNORM,
            DXGI_FORMAT_BC2_UNORM_SRGB,
            DXGI_FORMAT_BC3_TYPELESS,
            DXGI_FORMAT_BC3_UNORM,
            DXGI_FORMAT_BC3_UNORM_SRGB,
            DXGI_FORMAT_BC4_TYPELESS,
            DXGI_FORMAT_BC4_UNORM,
            DXGI_FORMAT_BC4_SNORM,
            DXGI_FORMAT_BC5_TYPELESS,
            DXGI_FORMAT_BC5_UNORM,
            DXGI_FORMAT_BC5_SNORM,
            DXGI_FORMAT_B5G6R5_UNORM,
            DXGI_FORMAT_B5G5R5A1_UNORM,
            DXGI_FORMAT_B8G8R8A8_UNORM,
            DXGI_FORMAT_B8G8R8X8_UNORM,
            DXGI_FORMAT_R10G10B10_XR_BIAS_A2_UNORM,
            DXGI_FORMAT_B8G8R8A8_TYPELESS,
            DXGI_FORMAT_B8G8R8A8_UNORM_SRGB,
            DXGI_FORMAT_B8G8R8X8_TYPELESS,
            DXGI_FORMAT_B8G8R8X8_UNORM_SRGB,
            DXGI_FORMAT_BC6H_TYPELESS,
            DXGI_FORMAT_BC6H_UF16,
            DXGI_FORMAT_BC6H_SF16,
            DXGI_FORMAT_BC7_TYPELESS,
            DXGI_FORMAT_BC7_UNORM,
            DXGI_FORMAT_BC7_UNORM_SRGB,
            DXGI_FORMAT_AYUV,
            DXGI_FORMAT_Y410,
            DXGI_FORMAT_Y416,
            DXGI_FORMAT_NV12,
            DXGI_FORMAT_P010,
            DXGI_FORMAT_P016,
            DXGI_FORMAT_420_OPAQUE,
            DXGI_FORMAT_YUY2,
            DXGI_FORMAT_Y210,
            DXGI_FORMAT_Y216,
            DXGI_FORMAT_NV11,
            DXGI_FORMAT_AI44,
            DXGI_FORMAT_IA44,
            DXGI_FORMAT_P8,
            DXGI_FORMAT_A8P8,
            DXGI_FORMAT_B4G4R4A4_UNORM,
            DXGI_FORMAT_P208,
            DXGI_FORMAT_V208,
            DXGI_FORMAT_V408,
            DXGI_FORMAT_SAMPLER_FEEDBACK_MIN_MIP_OPAQUE,
            DXGI_FORMAT_SAMPLER_FEEDBACK_MIP_REGION_USED_OPAQUE,
            DXGI_FORMAT_FORCE_UINT
        };
    }
}