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
using Newtonsoft.Json;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Mods.DataContainers;
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

                        var decompressedData = IOUtil.Decompressor(compressedData, uncompressedSize);

                        type2Bytes.AddRange(decompressedData);
                    }
                }
            }

            return type2Bytes.ToArray();
        }

        /// <summary>
        /// Gets the original data for type 2 files.
        /// </summary>
        /// <remarks>
        /// Type 2 files vary in content.
        /// </remarks>
        /// <param name="internalPath">The internal file path of the item</param>
        /// <param name="dataFile">The data file that contains the data.</param>
        /// <param name="modListDirectory">The directory where the mod list is located.</param>
        /// <returns>Byte array containing the decompressed type 2 data.</returns>
        public byte[] GetType2OriginalData(string internalPath, XivDataFile dataFile, DirectoryInfo modListDirectory)
        {
            var lineNum = 0;
            var inModList = false;
            ModInfo modInfo = null;

            using (var sr = new StreamReader(modListDirectory.FullName))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    modInfo = JsonConvert.DeserializeObject<ModInfo>(line);
                    if (modInfo.fullPath.Equals(internalPath))
                    {
                        inModList = true;
                        break;
                    }
                    lineNum++;
                }
            }

            // Throw exception if the item does not exist in the modlist
            if (!inModList) throw new Exception("Item not found in Modlist.");

            var modOffset = modInfo.modOffset;

            var type2Bytes = new List<byte>();

            // This formula is used to obtain the dat number in which the offset is located
            var datNum = ((modOffset / 8) & 0x0F) / 2;

            var datPath = _gameDirectory + "\\" + dataFile.GetDataFileName() + DatExtension + datNum;

            modOffset = OffsetCorrection(datNum, modOffset);

            using (var br = new BinaryReader(File.OpenRead(datPath)))
            {
                br.BaseStream.Seek(modOffset, SeekOrigin.Begin);

                var headerLength = br.ReadInt32();

                br.ReadBytes(16);

                var dataBlockCount = br.ReadInt32();

                for (var i = 0; i < dataBlockCount; i++)
                {
                    br.BaseStream.Seek(modOffset + (24 + (8 * i)), SeekOrigin.Begin);

                    var dataBlockOffset = br.ReadInt32();

                    br.BaseStream.Seek(modOffset + headerLength + dataBlockOffset, SeekOrigin.Begin);

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

                        var decompressedData = IOUtil.Decompressor(compressedData, uncompressedSize);

                        type2Bytes.AddRange(decompressedData);
                    }
                }
            }

            return type2Bytes.ToArray();
        }

        /// <summary>
        /// Imports any Type 2 data
        /// </summary>
        /// <param name="importFilePath">The file path where the file to be imported is located.</param>
        /// <param name="itemName">The name of the item being imported.</param>
        /// <param name="internalFilePath">The internal file path of the item.</param>
        /// <param name="category">The items category.</param>
        /// <param name="modListDirectory">The file path where the mod list is located.</param>
        /// <param name="dataFile">The data file to import the data into.</param>
        public int ImportType2Data(DirectoryInfo importFilePath, string itemName, string internalFilePath, string category,
            DirectoryInfo modListDirectory, XivDataFile dataFile)
        {
            ModInfo modInfo = null;
            var lineNum = 0;
            var inModList = false;

            var newData = new List<byte>();
            var headerData = new List<byte>();
            var dataBlocks = new List<byte>();

            // Checks if the item being imported already exists in the modlist
            using (var streamReader = new StreamReader(modListDirectory.FullName))
            {
                string line;
                while ((line = streamReader.ReadLine()) != null)
                {
                    modInfo = JsonConvert.DeserializeObject<ModInfo>(line);
                    if (modInfo.fullPath.Equals(internalFilePath))
                    {
                        inModList = true;
                        break;
                    }
                    lineNum++;
                }
            }

            var rawBytes = File.ReadAllBytes(importFilePath.FullName);

            // Header size is defaulted to 128, but may need to change if the data being imported is very large.
            headerData.AddRange(BitConverter.GetBytes(128));
            headerData.AddRange(BitConverter.GetBytes(2));
            headerData.AddRange(BitConverter.GetBytes(rawBytes.Length));

            var dataOffset = 0;
            var totalCompSize = 0;
            var uncompressedLength = rawBytes.Length;

            var partCount = (int)Math.Ceiling(uncompressedLength / 16000f);

            headerData.AddRange(BitConverter.GetBytes(partCount));

            var remainder = uncompressedLength;

            using (var binaryReader = new BinaryReader(new MemoryStream(rawBytes)))
            {
                binaryReader.BaseStream.Seek(0, SeekOrigin.Begin);

                for (var i = 1; i <= partCount; i++)
                {
                    if (i == partCount)
                    {
                        var compressedData = IOUtil.Compressor(binaryReader.ReadBytes(remainder));
                        var padding = 128 - ((compressedData.Length + 16) % 128);

                        dataBlocks.AddRange(BitConverter.GetBytes(16));
                        dataBlocks.AddRange(BitConverter.GetBytes(0));
                        dataBlocks.AddRange(BitConverter.GetBytes(compressedData.Length));
                        dataBlocks.AddRange(BitConverter.GetBytes(remainder));
                        dataBlocks.AddRange(compressedData);
                        dataBlocks.AddRange(new byte[padding]);

                        headerData.AddRange(BitConverter.GetBytes(dataOffset));
                        headerData.AddRange(BitConverter.GetBytes((short)((compressedData.Length + 16) + padding)));
                        headerData.AddRange(BitConverter.GetBytes((short)remainder));

                        totalCompSize = dataOffset + ((compressedData.Length + 16) + padding);
                    }
                    else
                    {
                        var compressedData = IOUtil.Compressor(binaryReader.ReadBytes(16000));
                        var padding = 128 - ((compressedData.Length + 16) % 128);

                        dataBlocks.AddRange(BitConverter.GetBytes(16));
                        dataBlocks.AddRange(BitConverter.GetBytes(0));
                        dataBlocks.AddRange(BitConverter.GetBytes(compressedData.Length));
                        dataBlocks.AddRange(BitConverter.GetBytes(16000));
                        dataBlocks.AddRange(compressedData);
                        dataBlocks.AddRange(new byte[padding]);

                        headerData.AddRange(BitConverter.GetBytes(dataOffset));
                        headerData.AddRange(BitConverter.GetBytes((short)((compressedData.Length + 16) + padding)));
                        headerData.AddRange(BitConverter.GetBytes((short)16000));

                        dataOffset += ((compressedData.Length + 16) + padding);
                        remainder -= 16000;
                    }
                }
            }

            headerData.InsertRange(12, BitConverter.GetBytes(totalCompSize / 128));
            headerData.InsertRange(16, BitConverter.GetBytes(totalCompSize / 128));

            var headerSize = 128;

            if (headerData.Count > 128)
            {
                headerData.RemoveRange(0, 4);
                headerData.InsertRange(0, BitConverter.GetBytes(256));
                headerSize = 256;
            }
            var headerPadding = headerSize - headerData.Count;

            headerData.AddRange(new byte[headerPadding]);

            newData.AddRange(headerData);
            newData.AddRange(dataBlocks);

            var newOffset = WriteToDat(newData, modInfo, inModList, internalFilePath, category, itemName, lineNum, dataFile, modListDirectory);

            if (newOffset == 0)
            {
                throw new Exception("There was an error writing to the dat file. Offset returned was 0.");
            }

            return newOffset;
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
                        var partDecompBytes = IOUtil.Decompressor(br.ReadBytes(partCompSize), partDecompSize);

                        byteList.AddRange(partDecompBytes);
                    }

                    br.BaseStream.Seek(lastPos + blockSizes[i], SeekOrigin.Begin);
                }
            }

            return (meshCount, materialCount, byteList.ToArray());
        }

        /// <summary>
        /// Gets the original data for Type 3 (Model) files
        /// </summary>
        /// <remarks>
        /// Type 3 files are used for models
        /// </remarks>
        /// <param name="internalPath">The internal file path of the item</param>
        /// <param name="dataFile">The data file that contains the data.</param>
        /// <param name="modListDirectory">The directory where the mod list is located.</param>
        /// <returns>A tuple containing the mesh count, material count, and decompressed data</returns>
        public (int MeshCount, int MaterialCount, byte[] Data) GetType3OriginalData(string internalPath, XivDataFile dataFile, DirectoryInfo modListDirectory)
        {
            var lineNum = 0;
            var inModList = false;
            ModInfo modInfo = null;

            using (var sr = new StreamReader(modListDirectory.FullName))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    modInfo = JsonConvert.DeserializeObject<ModInfo>(line);
                    if (modInfo.fullPath.Equals(internalPath))
                    {
                        inModList = true;
                        break;
                    }
                    lineNum++;
                }
            }

            // Throw exception if the item does not exist in the modlist
            if (!inModList) throw new Exception("Item not found in Modlist.");

            var modOffset = modInfo.modOffset;

            // This formula is used to obtain the dat number in which the offset is located
            var datNum = ((modOffset / 8) & 0x0F) / 2;

            modOffset = OffsetCorrection(datNum, modOffset);

            var datPath = _gameDirectory + "\\" + dataFile.GetDataFileName() + DatExtension + datNum;

            var byteList = new List<byte>();
            var meshCount = 0;
            var materialCount = 0;

            using (var br = new BinaryReader(File.OpenRead(datPath)))
            {
                br.BaseStream.Seek(modOffset, SeekOrigin.Begin);

                var headerLength = br.ReadInt32();
                var fileType = br.ReadInt32();
                var decompressedSize = br.ReadInt32();
                var buffer1 = br.ReadInt32();
                var buffer2 = br.ReadInt32();
                var parts = br.ReadInt16();

                var endOfHeader = modOffset + headerLength;

                byteList.AddRange(new byte[68]);

                br.BaseStream.Seek(modOffset + 24, SeekOrigin.Begin);

                var chunkUncompSizes = new int[11];
                var chunkLengths = new int[11];
                var chunkOffsets = new int[11];
                var chunkBlockStart = new int[11];
                var chunkNumBlocks = new int[11];

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

                br.BaseStream.Seek(modOffset + headerLength + chunkOffsets[0], SeekOrigin.Begin);

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
                        var partDecompBytes = IOUtil.Decompressor(br.ReadBytes(partCompSize), partDecompSize);

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

                        var decompressedPartData = IOUtil.Decompressor(compressedData, uncompressedSize);

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
                                decompressedPartData = IOUtil.Decompressor(compressedData, uncompressedSize);

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

                            var uncompressedData = IOUtil.Decompressor(compressedData, uncompressedSize);

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
        /// Gets the original data for Type 4 (Texture) files.
        /// </summary>
        /// <remarks>
        /// Type 4 files are used for Textures
        /// </remarks>
        /// <param name="internalPath">The internal file path of the item</param>
        /// <param name="dataFile">The data file that contains the data.</param>
        /// <param name="modListDirectory">The directory where the mod list is located.</param>
        /// <param name="xivTex">The XivTex container to fill</param>
        public void GetType4OriginalData(string internalPath, XivDataFile dataFile, DirectoryInfo modListDirectory, XivTex xivTex)
        {
            var lineNum = 0;
            var inModList = false;
            ModInfo modInfo = null;

            using (var sr = new StreamReader(modListDirectory.FullName))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    modInfo = JsonConvert.DeserializeObject<ModInfo>(line);
                    if (modInfo.fullPath.Equals(internalPath))
                    {
                        inModList = true;
                        break;
                    }
                    lineNum++;
                }
            }

            // Throw exception if the item does not exist in the modlist
            if (!inModList) throw new Exception("Item not found in Modlist.");

            var modOffset = modInfo.modOffset;

            var decompressedData = new List<byte>();

            // This formula is used to obtain the dat number in which the offset is located
            var datNum = ((modOffset / 8) & 0x0F) / 2;

            modOffset = OffsetCorrection(datNum, modOffset);

            var datPath = _gameDirectory + "\\" + dataFile.GetDataFileName() + DatExtension + datNum;

            using (var br = new BinaryReader(File.OpenRead(datPath)))
            {
                br.BaseStream.Seek(modOffset, SeekOrigin.Begin);

                var headerLength = br.ReadInt32();
                var fileType = br.ReadInt32();
                var uncompressedFileSize = br.ReadInt32();
                br.ReadBytes(8);
                xivTex.MipMapCount = br.ReadInt32();

                var endOfHeader = modOffset + headerLength;
                var mipMapInfoOffset = modOffset + 24;

                br.BaseStream.Seek(endOfHeader + 4, SeekOrigin.Begin);

                xivTex.TextureFormat = TextureTypeDictionary[br.ReadInt32()];
                xivTex.Width = br.ReadInt16();
                xivTex.Heigth = br.ReadInt16();

                for (int i = 0, j = 0; i < xivTex.MipMapCount; i++)
                {
                    br.BaseStream.Seek(mipMapInfoOffset + j, SeekOrigin.Begin);

                    var offsetFromHeaderEnd = br.ReadInt32();
                    var mipMapLength = br.ReadInt32();
                    var mipMapSize = br.ReadInt32();
                    var mipMapStart = br.ReadInt32();
                    var mipMapParts = br.ReadInt32();

                    var mipMapPartOffset = endOfHeader + offsetFromHeaderEnd;

                    br.BaseStream.Seek(mipMapPartOffset, SeekOrigin.Begin);

                    br.ReadBytes(8);
                    var compressedSize = br.ReadInt32();
                    var uncompressedSize = br.ReadInt32();

                    if (mipMapParts > 1)
                    {
                        var compressedData = br.ReadBytes(compressedSize);

                        var decompressedPartData = IOUtil.Decompressor(compressedData, uncompressedSize);

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
                                decompressedPartData = IOUtil.Decompressor(compressedData, uncompressedSize);

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

                            var uncompressedData = IOUtil.Decompressor(compressedData, uncompressedSize);

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
        /// Creates the header for the compressed texture data to be imported.
        /// </summary>
        /// <param name="xivTex">Data for the currently displayed texture.</param>
        /// <param name="mipPartOffsets">List of part offsets.</param>
        /// <param name="mipPartCount">List containing the amount of parts per mipmap.</param>
        /// <param name="uncompressedLength">Length of the uncompressed texture file.</param>
        /// <param name="newMipCount">The number of mipmaps the DDS texture to be imported contains.</param>
        /// <param name="newWidth">The width of the DDS texture to be imported.</param>
        /// <param name="newHeight">The height of the DDS texture to be imported.</param>
        /// <returns>The created header data.</returns>
        public byte[] MakeType4DatHeader(XivTex xivTex, List<short> mipPartOffsets, List<short> mipPartCount, int uncompressedLength, int newMipCount, int newWidth, int newHeight)
        {
            var headerData = new List<byte>();

            var headerSize = 24 + (newMipCount * 20) + (mipPartOffsets.Count * 2);
            var headerPadding = 128 - (headerSize % 128);

            headerData.AddRange(BitConverter.GetBytes(headerSize + headerPadding));
            headerData.AddRange(BitConverter.GetBytes(4));
            headerData.AddRange(BitConverter.GetBytes(uncompressedLength));
            headerData.AddRange(BitConverter.GetBytes(0));
            headerData.AddRange(BitConverter.GetBytes(0));
            headerData.AddRange(BitConverter.GetBytes(newMipCount));


            var partIndex = 0;
            var mipOffsetIndex = 80;
            var uncompMipSize = newHeight * newWidth;

            switch (xivTex.TextureFormat)
            {
                case XivTexFormat.DXT1:
                    uncompMipSize = (newWidth * newHeight) / 2;
                    break;
                case XivTexFormat.DXT5:
                case XivTexFormat.A8:
                    uncompMipSize = newWidth * newHeight;
                    break;
                case XivTexFormat.A1R5G5B5:
                case XivTexFormat.A4R4G4B4:
                    uncompMipSize = (newWidth * newHeight) * 2;
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
                    uncompMipSize = (newWidth * newHeight) * 4;
                    break;
            }

            for (var i = 0; i < newMipCount; i++)
            {
                headerData.AddRange(BitConverter.GetBytes(mipOffsetIndex));

                var paddedSize = 0;

                for (var j = 0; j < mipPartCount[i]; j++)
                {
                    paddedSize = paddedSize + mipPartOffsets[j + partIndex];
                }

                headerData.AddRange(BitConverter.GetBytes(paddedSize));

                headerData.AddRange(uncompMipSize > 16
                    ? BitConverter.GetBytes(uncompMipSize)
                    : BitConverter.GetBytes(16));

                uncompMipSize = uncompMipSize / 4;

                headerData.AddRange(BitConverter.GetBytes(partIndex));
                headerData.AddRange(BitConverter.GetBytes((int)mipPartCount[i]));

                partIndex = partIndex + mipPartCount[i];
                mipOffsetIndex = mipOffsetIndex + paddedSize;
            }

            foreach (var part in mipPartOffsets)
            {
                headerData.AddRange(BitConverter.GetBytes(part));
            }

            headerData.AddRange(new byte[headerPadding]);

            return headerData.ToArray();
        }

        /// <summary>
        /// Writes the newly imported data to the .dat for modifications.
        /// </summary>
        /// <param name="data">The data to be written.</param>
        /// <param name="modEntry">The modlist entry (if any) for the given file.</param>
        /// <param name="inModList">Is the item already contained within the mod list.</param>
        /// <param name="internalFilePath">The internal file path of the item being modified.</param>
        /// <param name="category">The category of the item.</param>
        /// <param name="itemName">The name of the item being modified.</param>
        /// <param name="lineNum">The line number of the existing mod list entry for the item if it exists.</param>
        /// <param name="dataFile">The data file to which we write the data</param>
        /// <returns>The new offset in which the modified data was placed.</returns>
        public int WriteToDat(List<byte> data, ModInfo modEntry, bool inModList, string internalFilePath,
            string category, string itemName, int lineNum, XivDataFile dataFile, DirectoryInfo modListDirectory)
        {
            var offset = 0;
            var dataOverwritten = false;

            var index = new Index(_gameDirectory);

            var datNum = GetLargestDatNumber(dataFile);

            var modDatPath = _gameDirectory + "\\" + dataFile.GetDataFileName() + DatExtension + datNum;

            if (inModList)
            {
                datNum = ((modEntry.modOffset / 8) & 0x0F) / 2;
                modDatPath = _gameDirectory + "\\" + modEntry.datFile + DatExtension + datNum;
            }
            else
            {
                var fileLength = new FileInfo(modDatPath).Length;

                // Creates a new Dat if the current dat is at the 2GB limit
                if (fileLength >= 2000000000)
                {
                    var newDatNum = CreateNewDat(dataFile);

                    modDatPath = _gameDirectory + "\\" + modEntry.datFile + DatExtension + newDatNum;
                }
            }

            // Checks to make sure the offsets in the mod list are not 0
            // If they are 0, something went wrong in the import proccess (Technically shouldn't happen)
            if (inModList)
            {
                if (modEntry.modOffset == 0)
                {
                    throw new Exception("The mod offset located in the mod list cannot be 0");
                }

                if (modEntry.originalOffset == 0)
                {
                    throw new Exception("The original offset located in the mod list cannot be 0");
                }
            }

            /* 
             * If the item has been previously modified and the compressed data being imported is smaller or equal to the exisiting data
             *  replace the existing data with new data.
             */
            if (inModList && data.Count <= modEntry.modSize)
            {
                if (modEntry.modOffset != 0)
                {
                    var sizeDiff = modEntry.modSize - data.Count;

                    datNum = ((modEntry.modOffset / 8) & 0x0F) / 2;
                    modDatPath = _gameDirectory + "\\" + modEntry.datFile + DatExtension + datNum;
                    var datOffsetAmount = 16 * datNum;

                    using (var bw = new BinaryWriter(File.OpenWrite(modDatPath)))
                    {
                        bw.BaseStream.Seek(modEntry.modOffset - datOffsetAmount, SeekOrigin.Begin);

                        bw.Write(data.ToArray());

                        bw.Write(new byte[sizeDiff]);
                    }

                    index.UpdateIndex(modEntry.modOffset, internalFilePath, dataFile);
                    index.UpdateIndex2(modEntry.modOffset, internalFilePath, dataFile);

                    offset = modEntry.modOffset;

                    dataOverwritten = true;
                }
            }
            else
            {
                var emptyLine = 0;

                /* 
                 * If there is an empty entry in the modlist and the compressed data being imported is smaller or equal to the available space
                *  write the compressed data in the existing space.
                */

                foreach (var line in File.ReadAllLines(modListDirectory.FullName))
                {
                    var emptyEntry = JsonConvert.DeserializeObject<ModInfo>(line);

                    if (emptyEntry.fullPath.Equals("") && emptyEntry.datFile.Equals(dataFile.GetDataFileName()))
                    {
                        if (emptyEntry.modOffset != 0)
                        {
                            var emptyLength = emptyEntry.modSize;

                            if (emptyLength > data.Count)
                            {
                                var sizeDiff = emptyLength - data.Count;

                                datNum = ((emptyEntry.modOffset / 8) & 0x0F) / 2;
                                modDatPath = _gameDirectory + "\\" + emptyEntry.datFile + DatExtension + datNum;
                                var datOffsetAmount = 16 * datNum;

                                using (var bw = new BinaryWriter(File.OpenWrite(modDatPath)))
                                {
                                    bw.BaseStream.Seek(emptyEntry.modOffset - datOffsetAmount, SeekOrigin.Begin);

                                    bw.Write(data.ToArray());

                                    bw.Write(new byte[sizeDiff]);
                                }

                                var originalOffset = index.UpdateIndex(emptyEntry.modOffset, internalFilePath, dataFile) * 8;
                                index.UpdateIndex2(emptyEntry.modOffset, internalFilePath, dataFile);

                                if (inModList)
                                {
                                    originalOffset = modEntry.originalOffset;

                                    var replaceOriginalEntry = new ModInfo
                                    {
                                        category = string.Empty,
                                        name = "Empty Replacement",
                                        fullPath = string.Empty,
                                        originalOffset = 0,
                                        modOffset = modEntry.modOffset,
                                        modSize = modEntry.modSize,
                                        datFile = dataFile.GetDataFileName()
                                    };

                                    var oLines = File.ReadAllLines(modListDirectory.FullName);
                                    oLines[lineNum] = JsonConvert.SerializeObject(replaceOriginalEntry);
                                    File.WriteAllLines(modListDirectory.FullName, oLines);
                                }


                                var replaceEntry = new ModInfo
                                {
                                    category = category,
                                    name = itemName,
                                    fullPath = internalFilePath,
                                    originalOffset = originalOffset,
                                    modOffset = emptyEntry.modOffset,
                                    modSize = emptyEntry.modSize,
                                    datFile = dataFile.GetDataFileName()
                                };

                                var lines = File.ReadAllLines(modListDirectory.FullName);
                                lines[emptyLine] = JsonConvert.SerializeObject(replaceEntry);
                                File.WriteAllLines(modListDirectory.FullName, lines);

                                offset = emptyEntry.modOffset;

                                dataOverwritten = true;
                                break;
                            }
                        }
                    }
                    emptyLine++;
                }

                if (!dataOverwritten)
                {
                    using (var bw = new BinaryWriter(File.OpenWrite(modDatPath)))
                    {
                        bw.BaseStream.Seek(0, SeekOrigin.End);

                        while ((bw.BaseStream.Position & 0xFF) != 0)
                        {
                            bw.Write((byte)0);
                        }

                        var eof = (int)bw.BaseStream.Position + data.Count;

                        while ((eof & 0xFF) != 0)
                        {
                            data.AddRange(new byte[16]);
                            eof = eof + 16;
                        }

                        var datOffsetAmount = 16 * datNum;
                        offset = (int)bw.BaseStream.Position + datOffsetAmount;

                        if (offset != 0)
                        {
                            bw.Write(data.ToArray());
                        }
                        else
                        {
                            throw new Exception("There was an issue obtaining the offset to write to.");
                        }
                    }
                }
            }

            if (!dataOverwritten)
            {
                if (offset != 0)
                {
                    var oldOffset = index.UpdateIndex(offset, internalFilePath, dataFile) * 8;
                    index.UpdateIndex2(offset, internalFilePath, dataFile);

                    /*
                     * If the item has been previously modifed, but the new compressed data to be imported is larger than the existing data
                     * remove the data from the modlist, leaving the offset and size intact for future use
                    */
                    if (inModList && data.Count > modEntry.modSize)
                    {
                        oldOffset = modEntry.originalOffset;

                        var replaceEntry = new ModInfo
                        {
                            category = string.Empty,
                            name = string.Empty,
                            fullPath = string.Empty,
                            originalOffset = 0,
                            modOffset = modEntry.modOffset,
                            modSize = modEntry.modSize,
                            datFile = dataFile.GetDataFileName()
                        };

                        var lines = File.ReadAllLines(modListDirectory.FullName);
                        lines[lineNum] = JsonConvert.SerializeObject(replaceEntry);
                        File.WriteAllLines(modListDirectory.FullName, lines);
                    }

                    var entry = new ModInfo
                    {
                        category = category,
                        name = itemName,
                        fullPath = internalFilePath,
                        originalOffset = oldOffset,
                        modOffset = offset,
                        modSize = data.Count,
                        datFile = dataFile.GetDataFileName()
                    };

                    using (var modFile = new StreamWriter(modListDirectory.FullName, true))
                    {
                        modFile.BaseStream.Seek(0, SeekOrigin.End);
                        modFile.WriteLine(JsonConvert.SerializeObject(entry));
                    }
                }
            }

            return offset;
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