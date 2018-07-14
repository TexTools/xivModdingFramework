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
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.Enums;

namespace xivModdingFramework.SqPack.FileTypes
{
    /// <summary>
    /// This class contains the methods that deal with the .dat file type 
    /// </summary>
    public class Dat
    {
        private const string DatExtension = ".win32.dat";
        private readonly DirectoryInfo _gameDirectory;

        public Dat(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
        }


        /// <summary>
        /// Creates a new dat file to store moddified data.
        /// </summary>
        /// <remarks>
        /// This will first find what the largest dat number is for a given data file
        /// It will then create a new dat file that is one number larger
        /// Lastly it will update the index files to reflect the new dat count
        /// </remarks>
        /// <param name="dataFile">The data file to create a new dat for.</param>
        /// <returns>The new dat number.</returns>
        public int CreateNewDat(XivDataFile dataFile)
        {
            var nextDatNumber = GetLargestDatNumber(dataFile) + 1;

            var datPath = _gameDirectory.FullName + "\\" + dataFile.GetDataFileName() + DatExtension + nextDatNumber;

            using (var fs = File.Create(datPath))
            {
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write(MakeSqPackHeader());
                    bw.Write(MakeDatHeader());
                }
            }

            var index = new Index(_gameDirectory);
            index.UpdateIndexDatCount(dataFile, nextDatNumber);

            return nextDatNumber;
        }

        /// <summary>
        /// Gets the largest dat number for a given data file.
        /// </summary>
        /// <param name="dataFile">The data file to check.</param>
        /// <returns>The largest dat number for the given data file.</returns>
        private int GetLargestDatNumber(XivDataFile dataFile)
        {
            var allFiles = Directory.GetFiles(_gameDirectory.FullName);

            var dataFiles = from file in allFiles where file.Contains(dataFile.GetDataFileName()) && file.Contains(".dat") select file;

            var max = dataFiles.Select(file => int.Parse(file.Substring(file.Length - 1))).Concat(new[] { 0 }).Max();

            return max;
        }

        /// <summary>
        /// Makes the header for the SqPack portion of the dat file. 
        /// </summary>
        /// <returns>byte array containing the header.</returns>
        private static byte[] MakeSqPackHeader()
        {
            var header = new byte[1024];

            using (var bw = new BinaryWriter(new MemoryStream(header)))
            {
                var sha1 = new SHA1Managed();

                bw.Write(1632661843);
                bw.Write(27491);
                bw.Write(0);
                bw.Write(1024);
                bw.Write(1);
                bw.Write(1);
                bw.Seek(8, SeekOrigin.Current);
                bw.Write(-1);
                bw.Seek(960, SeekOrigin.Begin);
                bw.Write(sha1.ComputeHash(header, 0, 959));
            }

            return header;
        }

        /// <summary>
        /// Makes the header for the dat file.
        /// </summary>
        /// <returns>byte array containing the header.</returns>
        private static byte[] MakeDatHeader()
        {
            var header = new byte[1024];

            using (var bw = new BinaryWriter(new MemoryStream(header)))
            {
                var sha1 = new SHA1Managed();

                bw.Write(header.Length);
                bw.Write(0);
                bw.Write(16);
                bw.Write(2048);
                bw.Write(2);
                bw.Write(0);
                bw.Write(2000000000);
                bw.Write(0);
                bw.Seek(960, SeekOrigin.Begin);
                bw.Write(sha1.ComputeHash(header, 0, 959));
            }

            return header;
        }

        /// <summary>
        /// Gets the data for type 2 files.
        /// </summary>
        /// <remarks>
        /// Type 2 files vary in content.
        /// </remarks>
        /// <param name="offset">The offset where the data is located.</param>
        /// <param name="dataFile">The data file that contains the data.</param>
        /// <returns>Byte array containing the decompressed type 2 data.</returns>
        public byte[] GetType2Data(int offset, XivDataFile dataFile)
        {
            var type2Bytes = new List<byte>();

            // This formula is used to obtain the dat number in which the offset is located
            var datNum = ((offset / 8) & 0x0F) / 2;

            var datPath = _gameDirectory + "\\" + dataFile.GetDataFileName() + DatExtension + datNum;

            offset = OffsetCorrection(datNum, offset);

            using (var br = new BinaryReader(File.OpenRead(datPath)))
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);

                var headerLength = br.ReadInt32();

                br.ReadBytes(16);

                var dataBlockCount = br.ReadInt32();

                for (var i = 0; i < dataBlockCount; i++)
                {
                    br.BaseStream.Seek(offset + (24 + (8 * i)), SeekOrigin.Begin);

                    var dataBlockOffset = br.ReadInt32();

                    br.BaseStream.Seek(offset + headerLength + dataBlockOffset, SeekOrigin.Begin);

                    br.ReadBytes(8);

                    var compressedSize = br.ReadInt32();
                    var uncompressedSize = br.ReadInt32();

                    // When the compressed size of a data block shows 32000, it is uncompressed.
                    if (compressedSize == 32000)
                    {
                        type2Bytes.AddRange(br.ReadBytes(uncompressedSize));
                    }
                    else
                    {
                        var compressedData = br.ReadBytes(compressedSize);

                        var decompressedData = new byte[uncompressedSize];

                        using (var ms = new MemoryStream(compressedData))
                        {
                            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                            {
                                ds.Read(decompressedData, 0, uncompressedSize);
                            }
                        }

                        type2Bytes.AddRange(decompressedData);
                    }
                }
            }

            return type2Bytes.ToArray();
        }

        /// <summary>
        /// Gets the data for Type 3 (Model) files
        /// </summary>
        /// <remarks>
        /// Type 3 files are used for models
        /// </remarks>
        /// <param name="offset">Offset to the type 3 data</param>
        /// <param name="dataFile">The data file that contains the data.</param>
        /// <returns>A tuple containing the mesh count, material count, and decompressed data</returns>
        public (int MeshCount, int MaterialCount, byte[] Data) GetType3Data(int offset, XivDataFile dataFile)
        {
            // This formula is used to obtain the dat number in which the offset is located
            var datNum = ((offset / 8) & 0x0F) / 2;

            offset = OffsetCorrection(datNum, offset);

            var datPath = _gameDirectory + "\\" + dataFile.GetDataFileName() + DatExtension + datNum;

            var byteList = new List<byte>();
            var meshCount = 0;
            var materialCount = 0;

            using (var br = new BinaryReader(File.OpenRead(datPath)))
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);

                var headerLength     = br.ReadInt32();
                var fileType         = br.ReadInt32();
                var decompressedSize = br.ReadInt32();
                var buffer1          = br.ReadInt32();
                var buffer2          = br.ReadInt32();
                var parts            = br.ReadInt16();

                var endOfHeader = offset + headerLength;

                byteList.AddRange(new byte[68]);

                br.BaseStream.Seek(offset + 24, SeekOrigin.Begin);

                var chunkUncompSizes = new int[11];
                var chunkLengths     = new int[11];
                var chunkOffsets     = new int[11];
                var chunkBlockStart  = new int[11];
                var chunkNumBlocks   = new int[11];

                for (var i = 0; i < 11; i++)
                {
                    chunkUncompSizes[i] = br.ReadInt32();
                }
                for (var i = 0; i < 11; i++)
                {
                    chunkLengths[i] = br.ReadInt32();
                }
                for (var i = 0; i < 11; i++)
                {
                    chunkOffsets[i] = br.ReadInt32();
                }
                for (var i = 0; i < 11; i++)
                {
                    chunkBlockStart[i] = br.ReadInt16();
                }
                var totalBlocks = 0;
                for (var i = 0; i < 11; i++)
                {
                    chunkNumBlocks[i] = br.ReadInt16();

                    totalBlocks += chunkNumBlocks[i];
                }

                meshCount = br.ReadInt16();
                materialCount = br.ReadInt16();

                br.ReadBytes(4);

                var blockSizes = new int[totalBlocks];

                for (var i = 0; i < totalBlocks; i++)
                {
                    blockSizes[i] = br.ReadInt16();
                }

                br.BaseStream.Seek(offset + headerLength + chunkOffsets[0], SeekOrigin.Begin);

                for (var i = 0; i < totalBlocks; i++)
                {
                    var lastPos = (int)br.BaseStream.Position;

                    br.ReadBytes(8);

                    var partCompSize = br.ReadInt32();
                    var partDecompSize = br.ReadInt32();

                    if (partCompSize == 32000)
                    {
                        byteList.AddRange(br.ReadBytes(partDecompSize));
                    }
                    else
                    {
                        var partDecompBytes = new byte[partDecompSize];
                        using (var ms = new MemoryStream(br.ReadBytes(partCompSize)))
                        {
                            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                            {
                                ds.Read(partDecompBytes, 0, partDecompSize);
                            }
                        }
                        byteList.AddRange(partDecompBytes);
                    }

                    br.BaseStream.Seek(lastPos + blockSizes[i], SeekOrigin.Begin);
                }
            }

            return (meshCount, materialCount, byteList.ToArray());
        }


        /// <summary>
        /// Gets the data for Type 4 (Texture) files.
        /// </summary>
        /// <remarks>
        /// Type 4 files are used for Textures
        /// </remarks>
        /// <param name="offset">Offset to the texture data.</param>
        /// <param name="dataFile">The data file that contains the data.</param>
        /// <param name="xivTex">The XivTex container to fill</param>
        public void GetType4Data(int offset, XivDataFile dataFile, XivTex xivTex)
        {
            var decompressedData = new List<byte>();

            // This formula is used to obtain the dat number in which the offset is located
            var datNum = ((offset / 8) & 0x0F) / 2;

            offset = OffsetCorrection(datNum, offset);

            var datPath = _gameDirectory + "\\" + dataFile.GetDataFileName() + DatExtension + datNum;

            using (var br = new BinaryReader(File.OpenRead(datPath)))
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);

                var headerLength = br.ReadInt32();
                var fileType = br.ReadInt32();
                var uncompressedFileSize = br.ReadInt32();
                br.ReadBytes(8);
                xivTex.MipMapCount = br.ReadInt32();

                var endOfHeader = offset + headerLength;
                var mipMapInfoOffset = offset + 24;

                br.BaseStream.Seek(endOfHeader + 4, SeekOrigin.Begin);

                xivTex.TextureFormat = TextureTypeDictionary[br.ReadInt32()];
                xivTex.Width = br.ReadInt16();
                xivTex.Heigth = br.ReadInt16();

                for (int i = 0, j = 0; i < xivTex.MipMapCount; i++)
                {
                    br.BaseStream.Seek(mipMapInfoOffset + j, SeekOrigin.Begin);

                    var offsetFromHeaderEnd = br.ReadInt32();
                    var mipMapLength        = br.ReadInt32();
                    var mipMapSize          = br.ReadInt32();
                    var mipMapStart         = br.ReadInt32();
                    var mipMapParts         = br.ReadInt32();

                    var mipMapPartOffset = endOfHeader + offsetFromHeaderEnd;

                    br.BaseStream.Seek(mipMapPartOffset, SeekOrigin.Begin);

                    br.ReadBytes(8);
                    var compressedSize = br.ReadInt32();
                    var uncompressedSize = br.ReadInt32();

                    if (mipMapParts > 1)
                    {
                        var compressedData = br.ReadBytes(compressedSize);
                        var decompressedPartData = new byte[uncompressedSize];

                        using (var ms = new MemoryStream(compressedData))
                        {
                            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                            {
                                ds.Read(decompressedPartData, 0x00, uncompressedSize);
                            }
                        }

                        decompressedData.AddRange(decompressedPartData);

                        for (var k = 1; k < mipMapParts; k++)
                        {
                            var check = br.ReadByte();
                            while (check != 0x10)
                            {
                                check = br.ReadByte();
                            }

                            br.ReadBytes(7);
                            compressedSize = br.ReadInt32();
                            uncompressedSize = br.ReadInt32();

                            // When the compressed size of a data block shows 32000, it is uncompressed.
                            if (compressedSize != 32000)
                            {
                                compressedData = br.ReadBytes(compressedSize);
                                decompressedPartData = new byte[uncompressedSize];
                                using (var ms = new MemoryStream(compressedData))
                                {
                                    using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                                    {
                                        ds.Read(decompressedPartData, 0x00, uncompressedSize);
                                    }
                                }
                                decompressedData.AddRange(decompressedPartData);
                            }
                            else
                            {
                                decompressedPartData = br.ReadBytes(uncompressedSize);
                                decompressedData.AddRange(decompressedPartData);
                            }
                        }
                    }
                    else
                    {
                        // When the compressed size of a data block shows 32000, it is uncompressed.
                        if (compressedSize != 32000)
                        {
                            var compressedData = br.ReadBytes(compressedSize);
                            var uncompressedData = new byte[uncompressedSize];

                            using (var ms = new MemoryStream(compressedData))
                            {
                                using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                                {
                                    ds.Read(uncompressedData, 0x00, uncompressedSize);
                                }
                            }

                            decompressedData.AddRange(uncompressedData);
                        }
                        else
                        {
                            var decompressedPartData = br.ReadBytes(uncompressedSize);
                            decompressedData.AddRange(decompressedPartData);
                        }
                    }
                    j = j + 20;
                }

                if (decompressedData.Count >= uncompressedFileSize) return;

                var difference = uncompressedFileSize - decompressedData.Count;
                var padding = new byte[difference];
                Array.Clear(padding, 0, difference);
                decompressedData.AddRange(padding);
            }
        }

        /// <summary>
        /// Dictionary that holds [Texture Code, Texture Format] data
        /// </summary>
        public Dictionary<int, XivTexFormat> TextureTypeDictionary = new Dictionary<int, XivTexFormat>
        {
            {4400, XivTexFormat.L8 },
            {4401, XivTexFormat.A8 },
            {5184, XivTexFormat.A4R4G4B4 },
            {5185, XivTexFormat.A1R5G5B5 },
            {5200, XivTexFormat.A8R8G8B8 },
            {5201, XivTexFormat.X8R8G8B8 },
            {8528, XivTexFormat.R32F},
            {8784, XivTexFormat.G16R16F },
            {8800, XivTexFormat.G32R32F },
            {9312, XivTexFormat.A16B16G16R16F },
            {9328, XivTexFormat.A32B32G32R32F },
            {13344, XivTexFormat.DXT1 },
            {13360, XivTexFormat.DXT3 },
            {13361, XivTexFormat.DXT5 },
            {16704, XivTexFormat.D16 }
        };

        /// <summary>
        /// Changes the offset to the correct location based on .dat number.
        /// </summary>
        /// <remarks>
        /// The dat files offset their data by 16 * dat number
        /// </remarks>
        /// <param name="datNum">The .dat number being used.</param>
        /// <param name="offset">The offset to correct.</param>
        /// <returns>The corrected offset.</returns>
        public static int OffsetCorrection(int datNum, int offset)
        {
            return offset - (16 * datNum);
        }
    }
}