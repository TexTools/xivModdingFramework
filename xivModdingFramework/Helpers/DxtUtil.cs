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
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using JeremyAnsel.BcnSharp;

namespace xivModdingFramework.Helpers
{
    // S3TC/BCn decoders. Output is RGBA8888. BC5/BC7 go through BcnSharp.
    internal static class DxtUtil
    {
        internal static byte[] DecompressDxt1(byte[] compData, int width, int height)
            => DecodeBlocks(compData, width, height, 8, DecodeDxt1Block);

        internal static byte[] DecompressDxt3(byte[] compData, int width, int height)
            => DecodeBlocks(compData, width, height, 16, DecodeDxt3Block);

        internal static byte[] DecompressDxt5(byte[] compData, int width, int height)
            => DecodeBlocks(compData, width, height, 16, DecodeDxt5Block);

        internal static byte[] DecompressBc4(byte[] imageData, int width, int height)
            => DecodeBlocks(imageData, width, height, 8, DecodeBc4Block);

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

        internal static void SwapRedBlue(byte[] imageData)
        {
            for (int i = 0; i < imageData.Length; i += 4)
            {
                byte tmp = imageData[i];
                imageData[i] = imageData[i + 2];
                imageData[i + 2] = tmp;
            }
        }

        private delegate void BlockDecoder(ReadOnlySpan<byte> block, int blockX, int blockY, int width, int height, byte[] output);

        private static byte[] DecodeBlocks(byte[] source, int width, int height, int bytesPerBlock, BlockDecoder decoder)
        {
            var output = new byte[width * height * 4];

            int blocksAcross = (width + 3) >> 2;
            int blocksDown = (height + 3) >> 2;

            Parallel.ForEach(Partitioner.Create(0, blocksDown), range =>
            {
                for (int by = range.Item1; by < range.Item2; by++)
                {
                    for (int bx = 0; bx < blocksAcross; bx++)
                    {
                        int offset = (by * blocksAcross + bx) * bytesPerBlock;
                        var block = new ReadOnlySpan<byte>(source, offset, bytesPerBlock);
                        decoder(block, bx, by, width, height, output);
                    }
                }
            });

            return output;
        }

        // In 1-bit mode (DXT1, e0 <= e1) index 2 is the midpoint and index 3 is
        // transparent black; otherwise 2 and 3 are the 1/3 and 2/3 blends.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BuildColorPalette(ushort e0, ushort e1, bool treatAsOneBit, Span<byte> palette)
        {
            Rgb565To888(e0, out byte r0, out byte g0, out byte b0);
            Rgb565To888(e1, out byte r1, out byte g1, out byte b1);

            palette[0] = r0; palette[1] = g0; palette[2] = b0; palette[3] = 255;
            palette[4] = r1; palette[5] = g1; palette[6] = b1; palette[7] = 255;

            if (treatAsOneBit)
            {
                palette[8]  = (byte)((r0 + r1) / 2);
                palette[9]  = (byte)((g0 + g1) / 2);
                palette[10] = (byte)((b0 + b1) / 2);
                palette[11] = 255;

                palette[12] = 0; palette[13] = 0; palette[14] = 0; palette[15] = 0;
            }
            else
            {
                palette[8]  = (byte)((2 * r0 + r1) / 3);
                palette[9]  = (byte)((2 * g0 + g1) / 3);
                palette[10] = (byte)((2 * b0 + b1) / 3);
                palette[11] = 255;

                palette[12] = (byte)((r0 + 2 * r1) / 3);
                palette[13] = (byte)((g0 + 2 * g1) / 3);
                palette[14] = (byte)((b0 + 2 * b1) / 3);
                palette[15] = 255;
            }
        }

        // Only DXT1 wants the palette's alpha; the alpha formats overwrite it.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteColorBlock(uint indices, ReadOnlySpan<byte> palette, int blockX, int blockY, int width, int height, byte[] output, bool writeAlphaFromPalette)
        {
            for (int py = 0; py < 4; py++)
            {
                int oy = (blockY << 2) + py;
                if (oy >= height) continue;

                for (int px = 0; px < 4; px++)
                {
                    int ox = (blockX << 2) + px;
                    if (ox >= width) continue;

                    int sel = (int)((indices >> (2 * (4 * py + px))) & 0x3) << 2;
                    int dst = ((oy * width) + ox) << 2;

                    output[dst]     = palette[sel];
                    output[dst + 1] = palette[sel + 1];
                    output[dst + 2] = palette[sel + 2];
                    if (writeAlphaFromPalette)
                    {
                        output[dst + 3] = palette[sel + 3];
                    }
                }
            }
        }

        private static void DecodeDxt1Block(ReadOnlySpan<byte> block, int blockX, int blockY, int width, int height, byte[] output)
        {
            ushort e0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
            ushort e1 = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(2));
            uint indices = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(4));

            Span<byte> palette = stackalloc byte[16];
            BuildColorPalette(e0, e1, e0 <= e1, palette);

            WriteColorBlock(indices, palette, blockX, blockY, width, height, output, writeAlphaFromPalette: true);
        }

        private static void DecodeDxt3Block(ReadOnlySpan<byte> block, int blockX, int blockY, int width, int height, byte[] output)
        {
            ushort e0 = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(8));
            ushort e1 = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(10));
            uint indices = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(12));

            Span<byte> palette = stackalloc byte[16];
            BuildColorPalette(e0, e1, false, palette);

            for (int py = 0; py < 4; py++)
            {
                int oy = (blockY << 2) + py;
                byte alphaByte0 = block[py * 2];
                byte alphaByte1 = block[py * 2 + 1];

                for (int px = 0; px < 4; px++)
                {
                    int ox = (blockX << 2) + px;

                    // 4 bits per texel, replicated to 8. Two texels per byte.
                    byte src = (px < 2) ? alphaByte0 : alphaByte1;
                    int nibble = ((px & 1) == 0) ? (src & 0x0F) : ((src >> 4) & 0x0F);
                    byte alpha = (byte)(nibble | (nibble << 4));

                    if (ox >= width || oy >= height) continue;

                    int sel = (int)((indices >> (2 * (4 * py + px))) & 0x3) << 2;
                    int dst = ((oy * width) + ox) << 2;

                    output[dst]     = palette[sel];
                    output[dst + 1] = palette[sel + 1];
                    output[dst + 2] = palette[sel + 2];
                    output[dst + 3] = alpha;
                }
            }
        }

        private static void DecodeDxt5Block(ReadOnlySpan<byte> block, int blockX, int blockY, int width, int height, byte[] output)
        {
            byte a0 = block[0];
            byte a1 = block[1];
            ulong alphaBits = ReadAlphaIndices(block);

            ushort e0 = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(8));
            ushort e1 = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(10));
            uint indices = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(12));

            Span<byte> palette = stackalloc byte[16];
            BuildColorPalette(e0, e1, false, palette);

            for (int py = 0; py < 4; py++)
            {
                int oy = (blockY << 2) + py;
                if (oy >= height) continue;

                for (int px = 0; px < 4; px++)
                {
                    int ox = (blockX << 2) + px;
                    if (ox >= width) continue;

                    int texel = 4 * py + px;
                    int alphaSel = (int)((alphaBits >> (3 * texel)) & 0x7);
                    byte alpha = InterpolateGrey(a0, a1, alphaSel);

                    int sel = (int)((indices >> (2 * texel)) & 0x3) << 2;
                    int dst = ((oy * width) + ox) << 2;

                    output[dst]     = palette[sel];
                    output[dst + 1] = palette[sel + 1];
                    output[dst + 2] = palette[sel + 2];
                    output[dst + 3] = alpha;
                }
            }
        }

        private static void DecodeBc4Block(ReadOnlySpan<byte> block, int blockX, int blockY, int width, int height, byte[] output)
        {
            byte r0 = block[0];
            byte r1 = block[1];
            ulong bits = ReadAlphaIndices(block);

            for (int py = 0; py < 4; py++)
            {
                int oy = (blockY << 2) + py;
                if (oy >= height) continue;

                for (int px = 0; px < 4; px++)
                {
                    int ox = (blockX << 2) + px;
                    if (ox >= width) continue;

                    int texel = 4 * py + px;
                    int sel = (int)((bits >> (3 * texel)) & 0x7);
                    byte value = InterpolateGrey(r0, r1, sel);

                    int dst = ((oy * width) + ox) << 2;
                    output[dst]     = value;
                    output[dst + 1] = value;
                    output[dst + 2] = value;
                    output[dst + 3] = 255;
                }
            }
        }

        // The 48-bit index field used by DXT5 alpha and BC4.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ReadAlphaIndices(ReadOnlySpan<byte> block)
        {
            return (ulong)block[2]
                 | (ulong)block[3] << 8
                 | (ulong)block[4] << 16
                 | (ulong)block[5] << 24
                 | (ulong)block[6] << 32
                 | (ulong)block[7] << 40;
        }

        // Shared by DXT5 alpha and BC4. When e0 <= e1 the ramp is 6 steps and
        // selectors 6/7 mean 0 and 255; otherwise it's a full 8-step ramp.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte InterpolateGrey(byte e0, byte e1, int selector)
        {
            if (selector == 0) return e0;
            if (selector == 1) return e1;

            if (e0 > e1)
            {
                return (byte)(((8 - selector) * e0 + (selector - 1) * e1) / 7);
            }

            if (selector == 6) return 0;
            if (selector == 7) return 255;

            return (byte)(((6 - selector) * e0 + (selector - 1) * e1) / 5);
        }

        // Old S3TC expansion: (v * 255 + bias) / step per channel, shifted out.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Rgb565To888(ushort color, out byte r, out byte g, out byte b)
        {
            int five;

            five = ((color >> 11) & 0x1F) * 255 + 16;
            r = (byte)(((five >> 5) + five) >> 5);

            int six = ((color >> 5) & 0x3F) * 255 + 32;
            g = (byte)(((six >> 6) + six) >> 6);

            five = (color & 0x1F) * 255 + 16;
            b = (byte)(((five >> 5) + five) >> 5);
        }
    }
}