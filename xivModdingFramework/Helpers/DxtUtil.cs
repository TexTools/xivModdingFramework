/* FNA - XNA4 Reimplementation for Desktop Platforms
 * Copyright 2009-2017 Ethan Lee and the MonoGame Team
 *
 * Released under the Microsoft Public License.
 * See LICENSE for details.
 */

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using JeremyAnsel.BcnSharp;

namespace xivModdingFramework.Helpers
{
    internal static class DxtUtil
    {
        internal static byte[] DecompressDxt1(byte[] compData, int width, int height)
        {
            byte[] resultData = new byte[width * height * 4];

            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            const int blockSize = 8;

            Parallel.ForEach(Partitioner.Create(0, blockCountY), range =>
            {
                for (int y = range.Item1; y < range.Item2; y++)
                {
                    for (int x = 0; x < blockCountX; x++)
                    {
                        int offset = (y * blockCountX + x) * blockSize;
                        DecompressDxt1Block(compData, offset, x, y, width, height, resultData);
                    }
                }
            });

            return resultData;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DecompressDxt1Block(byte[] source, int sourceOffset, int x, int y, int width, int height, byte[] imageData)
        {
            ushort c0 = BitConverter.ToUInt16(source, sourceOffset);
            ushort c1 = BitConverter.ToUInt16(source, sourceOffset + 2);

            byte r0, g0, b0;
            byte r1, g1, b1;
            ConvertRgb565ToRgb888(c0, out r0, out g0, out b0);
            ConvertRgb565ToRgb888(c1, out r1, out g1, out b1);

            uint lookupTable = BitConverter.ToUInt32(source, sourceOffset + 4);

            for (int blockY = 0; blockY < 4; blockY++)
            {
                for (int blockX = 0; blockX < 4; blockX++)
                {
                    byte r = 0, g = 0, b = 0, a = 255;
                    uint index = (lookupTable >> 2 * (4 * blockY + blockX)) & 0x03;

                    if (c0 > c1)
                    {
                        switch (index)
                        {
                            case 0:
                                r = r0;
                                g = g0;
                                b = b0;
                                break;
                            case 1:
                                r = r1;
                                g = g1;
                                b = b1;
                                break;
                            case 2:
                                r = (byte)((2 * r0 + r1) / 3);
                                g = (byte)((2 * g0 + g1) / 3);
                                b = (byte)((2 * b0 + b1) / 3);
                                break;
                            case 3:
                                r = (byte)((r0 + 2 * r1) / 3);
                                g = (byte)((g0 + 2 * g1) / 3);
                                b = (byte)((b0 + 2 * b1) / 3);
                                break;
                        }
                    }
                    else
                    {
                        switch (index)
                        {
                            case 0:
                                r = r0;
                                g = g0;
                                b = b0;
                                break;
                            case 1:
                                r = r1;
                                g = g1;
                                b = b1;
                                break;
                            case 2:
                                r = (byte)((r0 + r1) / 2);
                                g = (byte)((g0 + g1) / 2);
                                b = (byte)((b0 + b1) / 2);
                                break;
                            case 3:
                                r = 0;
                                g = 0;
                                b = 0;
                                a = 0;
                                break;
                        }
                    }

                    int px = (x << 2) + blockX;
                    int py = (y << 2) + blockY;
                    if ((px < width) && (py < height))
                    {
                        int offset = ((py * width) + px) << 2;
                        imageData[offset] = r;
                        imageData[offset + 1] = g;
                        imageData[offset + 2] = b;
                        imageData[offset + 3] = a;
                    }
                }
            }
        }

        internal static byte[] DecompressDxt3(byte[] compData, int width, int height)
        {
            byte[] resultData = new byte[width * height * 4];

            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            const int blockSize = 16;

            Parallel.ForEach(Partitioner.Create(0, blockCountY), range =>
            {
                for (int y = range.Item1; y < range.Item2; y++)
                {
                    for (int x = 0; x < blockCountX; x++)
                    {
                        int offset = (y * blockCountX + x) * blockSize;
                        DecompressDxt3Block(compData, offset, x, y, width, height, resultData);
                    }
                }
            });

            return resultData;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DecompressDxt3Block(byte[] source, int sourceOffset, int x, int y, int width, int height, byte[] imageData)
        {
            using var blockReader = new BinaryReader(new MemoryStream(source, sourceOffset, 16));
            byte a0 = source[sourceOffset];
            byte a1 = source[sourceOffset + 1];
            byte a2 = source[sourceOffset + 2];
            byte a3 = source[sourceOffset + 3];
            byte a4 = source[sourceOffset + 4];
            byte a5 = source[sourceOffset + 5];
            byte a6 = source[sourceOffset + 6];
            byte a7 = source[sourceOffset + 7];

            ushort c0 = BitConverter.ToUInt16(source, sourceOffset + 8);
            ushort c1 = BitConverter.ToUInt16(source, sourceOffset + 10);

            byte r0, g0, b0;
            byte r1, g1, b1;
            ConvertRgb565ToRgb888(c0, out r0, out g0, out b0);
            ConvertRgb565ToRgb888(c1, out r1, out g1, out b1);

            uint lookupTable = BitConverter.ToUInt32(source, sourceOffset + 12);

            int alphaIndex = 0;
            for (int blockY = 0; blockY < 4; blockY++)
            {
                for (int blockX = 0; blockX < 4; blockX++)
                {
                    byte r = 0, g = 0, b = 0, a = 0;

                    uint index = (lookupTable >> 2 * (4 * blockY + blockX)) & 0x03;

                    switch (alphaIndex)
                    {
                        case 0:
                            a = (byte)((a0 & 0x0F) | ((a0 & 0x0F) << 4));
                            break;
                        case 1:
                            a = (byte)((a0 & 0xF0) | ((a0 & 0xF0) >> 4));
                            break;
                        case 2:
                            a = (byte)((a1 & 0x0F) | ((a1 & 0x0F) << 4));
                            break;
                        case 3:
                            a = (byte)((a1 & 0xF0) | ((a1 & 0xF0) >> 4));
                            break;
                        case 4:
                            a = (byte)((a2 & 0x0F) | ((a2 & 0x0F) << 4));
                            break;
                        case 5:
                            a = (byte)((a2 & 0xF0) | ((a2 & 0xF0) >> 4));
                            break;
                        case 6:
                            a = (byte)((a3 & 0x0F) | ((a3 & 0x0F) << 4));
                            break;
                        case 7:
                            a = (byte)((a3 & 0xF0) | ((a3 & 0xF0) >> 4));
                            break;
                        case 8:
                            a = (byte)((a4 & 0x0F) | ((a4 & 0x0F) << 4));
                            break;
                        case 9:
                            a = (byte)((a4 & 0xF0) | ((a4 & 0xF0) >> 4));
                            break;
                        case 10:
                            a = (byte)((a5 & 0x0F) | ((a5 & 0x0F) << 4));
                            break;
                        case 11:
                            a = (byte)((a5 & 0xF0) | ((a5 & 0xF0) >> 4));
                            break;
                        case 12:
                            a = (byte)((a6 & 0x0F) | ((a6 & 0x0F) << 4));
                            break;
                        case 13:
                            a = (byte)((a6 & 0xF0) | ((a6 & 0xF0) >> 4));
                            break;
                        case 14:
                            a = (byte)((a7 & 0x0F) | ((a7 & 0x0F) << 4));
                            break;
                        case 15:
                            a = (byte)((a7 & 0xF0) | ((a7 & 0xF0) >> 4));
                            break;
                    }
                    ++alphaIndex;

                    switch (index)
                    {
                        case 0:
                            r = r0;
                            g = g0;
                            b = b0;
                            break;
                        case 1:
                            r = r1;
                            g = g1;
                            b = b1;
                            break;
                        case 2:
                            r = (byte)((2 * r0 + r1) / 3);
                            g = (byte)((2 * g0 + g1) / 3);
                            b = (byte)((2 * b0 + b1) / 3);
                            break;
                        case 3:
                            r = (byte)((r0 + 2 * r1) / 3);
                            g = (byte)((g0 + 2 * g1) / 3);
                            b = (byte)((b0 + 2 * b1) / 3);
                            break;
                    }

                    int px = (x << 2) + blockX;
                    int py = (y << 2) + blockY;
                    if ((px < width) && (py < height))
                    {
                        int offset = ((py * width) + px) << 2;
                        imageData[offset] = r;
                        imageData[offset + 1] = g;
                        imageData[offset + 2] = b;
                        imageData[offset + 3] = a;
                    }
                }
            }
        }

        internal static byte[] DecompressDxt5(byte[] compData, int width, int height)
        {
            byte[] resultData = new byte[width * height * 4];

            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            const int blockSize = 16;

            Parallel.ForEach(Partitioner.Create(0, blockCountY), range =>
            {
                for (int y = range.Item1; y < range.Item2; y++)
                {
                    for (int x = 0; x < blockCountX; x++)
                    {
                        int offset = (y * blockCountX + x) * blockSize;
                        DecompressDxt5Block(compData, offset, x, y, width, height, resultData);
                    }
                }
            });

            return resultData;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DecompressDxt5Block(byte[] source, int sourceOffset, int x, int y, int width, int height, byte[] imageData)
        {
            byte alpha0 = source[sourceOffset];
            byte alpha1 = source[sourceOffset + 1];

            ulong alphaMask = (ulong)source[sourceOffset + 2]
                            | (ulong)source[sourceOffset + 3] << 8
                            | (ulong)source[sourceOffset + 4] << 16
                            | (ulong)source[sourceOffset + 5] << 24
                            | (ulong)source[sourceOffset + 6] << 32
                            | (ulong)source[sourceOffset + 7] << 40;

            ushort c0 = BitConverter.ToUInt16(source, sourceOffset + 8);
            ushort c1 = BitConverter.ToUInt16(source, sourceOffset + 10);

            byte r0, g0, b0;
            byte r1, g1, b1;
            ConvertRgb565ToRgb888(c0, out r0, out g0, out b0);
            ConvertRgb565ToRgb888(c1, out r1, out g1, out b1);

            uint lookupTable = BitConverter.ToUInt32(source, sourceOffset + 12);

            for (int blockY = 0; blockY < 4; blockY++)
            {
                for (int blockX = 0; blockX < 4; blockX++)
                {
                    byte r = 0, g = 0, b = 0, a = 255;
                    uint index = (lookupTable >> 2 * (4 * blockY + blockX)) & 0x03;

                    uint alphaIndex = (uint)((alphaMask >> 3 * (4 * blockY + blockX)) & 0x07);
                    if (alphaIndex == 0)
                    {
                        a = alpha0;
                    }
                    else if (alphaIndex == 1)
                    {
                        a = alpha1;
                    }
                    else if (alpha0 > alpha1)
                    {
                        a = (byte)(((8 - alphaIndex) * alpha0 + (alphaIndex - 1) * alpha1) / 7);
                    }
                    else if (alphaIndex == 6)
                    {
                        a = 0;
                    }
                    else if (alphaIndex == 7)
                    {
                        a = 0xff;
                    }
                    else
                    {
                        a = (byte)(((6 - alphaIndex) * alpha0 + (alphaIndex - 1) * alpha1) / 5);
                    }

                    switch (index)
                    {
                        case 0:
                            r = r0;
                            g = g0;
                            b = b0;
                            break;
                        case 1:
                            r = r1;
                            g = g1;
                            b = b1;
                            break;
                        case 2:
                            r = (byte)((2 * r0 + r1) / 3);
                            g = (byte)((2 * g0 + g1) / 3);
                            b = (byte)((2 * b0 + b1) / 3);
                            break;
                        case 3:
                            r = (byte)((r0 + 2 * r1) / 3);
                            g = (byte)((g0 + 2 * g1) / 3);
                            b = (byte)((b0 + 2 * b1) / 3);
                            break;
                    }

                    int px = (x << 2) + blockX;
                    int py = (y << 2) + blockY;
                    if ((px < width) && (py < height))
                    {
                        int offset = ((py * width) + px) << 2;
                        imageData[offset] = r;
                        imageData[offset + 1] = g;
                        imageData[offset + 2] = b;
                        imageData[offset + 3] = a;
                    }
                }
            }
        }

        // XXX: It seems like BcnSharp produces the wrong pixel format -- swap Red/Blue channels on its output
        internal static void SwapRedBlue(byte[] imageData)
        {
            for (int i = 0; i < imageData.Length; i += 4)
            {
                byte x = imageData[i];
                byte y = imageData[i + 2];
                imageData[i] = y;
                imageData[i + 2] = x;
            }
        }

        internal static byte[] DecompressBc5(byte[] imageData, int width, int height)
        {
            var result = new byte[width * height * 4];
            Bc5Sharp.Decode(imageData, result, width, height);
            SwapRedBlue(result);
            return result;
        }

        internal static byte[] DecompressBc7(byte[] imageData, int width, int height)
        {
            var result = new byte[width * height * 4];
            Bc7Sharp.Decode(imageData, result, width, height);
            SwapRedBlue(result);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ConvertRgb565ToRgb888(ushort color, out byte r, out byte g, out byte b)
        {
            int temp;

            temp = (color >> 11) * 255 + 16;
            r = (byte)((temp / 32 + temp) / 32);
            temp = ((color & 0x07E0) >> 5) * 255 + 32;
            g = (byte)((temp / 64 + temp) / 64);
            temp = (color & 0x001F) * 255 + 16;
            b = (byte)((temp / 32 + temp) / 32);
        }
    }
}