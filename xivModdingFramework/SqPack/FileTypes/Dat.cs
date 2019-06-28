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

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Resources;
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
        private readonly DirectoryInfo _modListDirectory;
        private static SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

        public Dat(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;

            var modding = new Modding(_gameDirectory);
            modding.CreateModlist();
            _modListDirectory = modding.ModListDirectory;
        }


        /// <summary>
        /// Creates a new dat file to store modified data.
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

            if (nextDatNumber == 8)
            {
                return 8;
            }

            var datPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{DatExtension}{nextDatNumber}");

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
        public int GetLargestDatNumber(XivDataFile dataFile)
        {
            var allFiles = Directory.GetFiles(_gameDirectory.FullName);

            var dataFiles = from file in allFiles where file.Contains(dataFile.GetDataFileName()) && file.Contains(".dat") select file;

            try
            {
                var max = dataFiles.Select(file => int.Parse(file.Substring(file.Length - 1))).Concat(new[] { 0 }).Max();

                return max;
            }
            catch(Exception ex)
            {
                var fileList = "";
                foreach (var file in dataFiles)
                {
                    fileList += $"{file}\n";
                }

                throw new Exception($"Unable to determine Dat Number from one of the following files\n\n{fileList}");
            }

        }

        /// <summary>
        /// Determines whether a mod dat already exists
        /// </summary>
        /// <param name="dataFile">The dat file to check.</param>
        /// <returns>True if it is original, false otherwise</returns>
        private async Task<bool> IsOriginalDat(XivDataFile dataFile)
        {
            var moddedList = await GetModdedDatList(dataFile);

            return moddedList.Count <= 0;
        }

        /// <summary>
        /// Gets the modded dat files
        /// </summary>
        /// <param name="dataFile">The data file to check</param>
        /// <returns>A list of modded dat files</returns>
        public async Task<List<string>> GetModdedDatList(XivDataFile dataFile)
        {
            var datList = new List<string>();

            await Task.Run(() =>
            {
                for (var i = 1; i < 20; i++)
                {
                    var datFilePath = $"{_gameDirectory}/{dataFile.GetDataFileName()}.win32.dat{i}";

                    if (File.Exists(datFilePath))
                    {
                        // Due to an issue where 060000 dat1 gets deleted, we are skipping it here
                        if (datFilePath.Contains("060000.win32.dat1")) continue;

                        using (var binaryReader = new BinaryReader(File.OpenRead(datFilePath)))
                        {
                            binaryReader.BaseStream.Seek(24, SeekOrigin.Begin);

                            if (binaryReader.ReadByte() == 0)
                            {
                                datList.Add(datFilePath);
                            }
                        }
                    }
                }
            });

            return datList;
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
        /// Gets a XivDataFile category for the specified path.
        /// </summary>
        /// <param name="internalPath">The internal file path</param>
        /// <returns>A XivDataFile entry for the needed dat category</returns>
        private XivDataFile GetDataFileFromPath(string internalPath)
        {
            var folderKey = internalPath.Substring(0, internalPath.IndexOf("/", StringComparison.Ordinal));

            var cats = Enum.GetValues(typeof(XivDataFile)).Cast<XivDataFile>();

            foreach (var cat in cats)
            {
                if (cat.GetFolderKey() == folderKey)
                    return cat;
            }

            throw new ArgumentException("[Dat] Could not find category for path: " + internalPath);
        }

        /// <summary>
        /// Gets the original or modded data for type 2 files based on the path specified.
        /// </summary>
        /// <remarks>
        /// Type 2 files vary in content.
        /// </remarks>
        /// <param name="internalPath">The internal file path of the item</param>
        /// <param name="forceOriginal">Flag used to get original game data</param>
        /// <returns>Byte array containing the decompressed type 2 data.</returns>
        public async Task<byte[]> GetType2Data(string internalPath, bool forceOriginal)
        {
            var index = new Index(_gameDirectory);
            var modding = new Modding(_gameDirectory);

            var dataFile = GetDataFileFromPath(internalPath);

            if (forceOriginal)
            {
                // Checks if the item being imported already exists in the modlist
                var modEntry = await modding.TryGetModEntry(internalPath);

                // If the file exists in the modlist, get the data from the original data
                if (modEntry != null)
                {
                    return await GetType2Data(modEntry.data.originalOffset, dataFile);
                }
            }

            // If it doesn't exist in the modlist(the item is not modded) or force original is false,
            // grab the data directly from them index file. 
            var folder = Path.GetDirectoryName(internalPath);
            folder = folder.Replace("\\", "/");
            var file = Path.GetFileName(internalPath);

            var offset = await index.GetDataOffset(HashGenerator.GetHash(folder), HashGenerator.GetHash(file),
                dataFile);

            if (offset == 0)
            {
                throw new Exception($"Could not find offest for {internalPath}");
            }

            return await GetType2Data(offset, dataFile);
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
        public async Task<byte[]> GetType2Data(int offset, XivDataFile dataFile)
        {
            var type2Bytes = new List<byte>();

            // This formula is used to obtain the dat number in which the offset is located
            var datNum = ((offset / 8) & 0x0F) / 2;

            var datPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{DatExtension}{datNum}");

            await _semaphoreSlim.WaitAsync();

            try
            {
                offset = OffsetCorrection(datNum, offset);

                await Task.Run(async () =>
                {
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

                                var decompressedData = await IOUtil.Decompressor(compressedData, uncompressedSize);

                                type2Bytes.AddRange(decompressedData);
                            }
                        }
                    }
                });
            }
            finally
            {
                _semaphoreSlim.Release();
            }

            return type2Bytes.ToArray();
        }

        /// <summary>
        /// Imports any Type 2 data
        /// </summary>
        /// <param name="importFilePath">The file path where the file to be imported is located.</param>
        /// <param name="itemName">The name of the item being imported.</param>
        /// <param name="internalPath">The internal file path of the item.</param>
        /// <param name="category">The items category.</param>
        /// <param name="source">The source/application that is writing to the dat.</param>
        public async Task<int> ImportType2Data(DirectoryInfo importFilePath, string itemName, string internalPath,
            string category, string source)
        {
            return await ImportType2Data(File.ReadAllBytes(importFilePath.FullName), itemName, internalPath, category, source);
        }

        /// <summary>
        /// Imports any Type 2 data
        /// </summary>
        /// <param name="dataToImport">Bytes to import.</param>
        /// <param name="itemName">The name of the item being imported.</param>
        /// <param name="internalPath">The internal file path of the item.</param>
        /// <param name="category">The items category.</param>
        /// <param name="source">The source/application that is writing to the dat.</param>
        public async Task<int> ImportType2Data(byte[] dataToImport, string itemName, string internalPath,
            string category, string source)
        {
            var dataFile = GetDataFileFromPath(internalPath);
            var modding = new Modding(_gameDirectory);

            var newData = new List<byte>();
            var headerData = new List<byte>();
            var dataBlocks = new List<byte>();

            // Checks if the item being imported already exists in the modlist
            var modEntry = await modding.TryGetModEntry(internalPath);

            // Header size is defaulted to 128, but may need to change if the data being imported is very large.
            headerData.AddRange(BitConverter.GetBytes(128));
            headerData.AddRange(BitConverter.GetBytes(2));
            headerData.AddRange(BitConverter.GetBytes(dataToImport.Length));

            var dataOffset = 0;
            var totalCompSize = 0;
            var uncompressedLength = dataToImport.Length;

            var partCount = (int)Math.Ceiling(uncompressedLength / 16000f);

            headerData.AddRange(BitConverter.GetBytes(partCount));

            var remainder = uncompressedLength;

            using (var binaryReader = new BinaryReader(new MemoryStream(dataToImport)))
            {
                binaryReader.BaseStream.Seek(0, SeekOrigin.Begin);

                for (var i = 1; i <= partCount; i++)
                {
                    if (i == partCount)
                    {
                        var compressedData = await IOUtil.Compressor(binaryReader.ReadBytes(remainder));
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
                        var compressedData = await IOUtil.Compressor(binaryReader.ReadBytes(16000));
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

            var newOffset = await WriteToDat(newData, modEntry, internalPath, category, itemName, dataFile, source, 2);

            if (newOffset == 0)
            {
                throw new Exception("There was an error writing to the dat file. Offset returned was 0.");
            }

            return newOffset;
        }

        /// <summary>
        /// Gets the original or modded data for type 3 files based on the path specified.
        /// </summary>
        /// <remarks>
        /// Type 3 files are used for models
        /// </remarks>
        /// <param name="internalPath">The internal file path of the item</param>
        /// <param name="forceOriginal">Flag used to get original game data</param>
        /// <returns>A tuple containing the mesh count, material count, and decompressed data</returns>
        public async Task<(int MeshCount, int MaterialCount, byte[] Data)> GetType3Data(string internalPath, bool forceOriginal)
        {
            var index = new Index(_gameDirectory);
            var modding = new Modding(_gameDirectory);

            var dataFile = GetDataFileFromPath(internalPath);

            if (forceOriginal)
            {
                // Checks if the item being imported already exists in the modlist

                var modEntry = await modding.TryGetModEntry(internalPath);

                // If the file exists in the modlist, get the data from the original data
                if (modEntry != null)
                {
                    return await GetType3Data(modEntry.data.originalOffset, dataFile);
                }
            }

            // If it doesn't exist in the modlist(the item is not modded) or force original is false,
            // grab the data directly from them index file. 
            var folder = Path.GetDirectoryName(internalPath);
            folder = folder.Replace("\\", "/");
            var file = Path.GetFileName(internalPath);

            var offset = await index.GetDataOffset(HashGenerator.GetHash(folder), HashGenerator.GetHash(file),
                dataFile);

            if (offset == 0)
            {
                throw new Exception($"Could not find offest for {internalPath}");
            }

            return await GetType3Data(offset, dataFile);
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
        public async Task<(int MeshCount, int MaterialCount, byte[] Data)> GetType3Data(int offset, XivDataFile dataFile)
        {
            // This formula is used to obtain the dat number in which the offset is located
            var datNum = ((offset / 8) & 0x0F) / 2;

            offset = OffsetCorrection(datNum, offset);

            var datPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{DatExtension}{datNum}");

            var byteList = new List<byte>();
            var meshCount = 0;
            var materialCount = 0;

            await Task.Run(async () =>
            {
                using (var br = new BinaryReader(File.OpenRead(datPath)))
                {
                    br.BaseStream.Seek(offset, SeekOrigin.Begin);

                    var headerLength = br.ReadInt32();
                    var fileType = br.ReadInt32();
                    var decompressedSize = br.ReadInt32();
                    var buffer1 = br.ReadInt32();
                    var buffer2 = br.ReadInt32();
                    var parts = br.ReadInt16();

                    var endOfHeader = offset + headerLength;

                    byteList.AddRange(new byte[68]);

                    br.BaseStream.Seek(offset + 24, SeekOrigin.Begin);

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
                        chunkBlockStart[i] = br.ReadUInt16();
                    }

                    var totalBlocks = 0;
                    for (var i = 0; i < 11; i++)
                    {
                        chunkNumBlocks[i] = br.ReadUInt16();

                        totalBlocks += chunkNumBlocks[i];
                    }

                    meshCount = br.ReadUInt16();
                    materialCount = br.ReadUInt16();

                    br.ReadBytes(4);

                    var blockSizes = new int[totalBlocks];

                    for (var i = 0; i < totalBlocks; i++)
                    {
                        blockSizes[i] = br.ReadUInt16();
                    }

                    br.BaseStream.Seek(offset + headerLength + chunkOffsets[0], SeekOrigin.Begin);

                    for (var i = 0; i < totalBlocks; i++)
                    {
                        var lastPos = (int) br.BaseStream.Position;

                        br.ReadBytes(8);

                        var partCompSize = br.ReadInt32();
                        var partDecompSize = br.ReadInt32();

                        if (partCompSize == 32000)
                        {
                            byteList.AddRange(br.ReadBytes(partDecompSize));
                        }
                        else
                        {
                            var partDecompBytes = await IOUtil.Decompressor(br.ReadBytes(partCompSize), partDecompSize);

                            byteList.AddRange(partDecompBytes);
                        }

                        br.BaseStream.Seek(lastPos + blockSizes[i], SeekOrigin.Begin);
                    }
                }
            });

            return (meshCount, materialCount, byteList.ToArray());
        }

        /// <summary>
        /// Gets the original or modded data for type 4 files based on the path specified.
        /// </summary>
        /// <remarks>
        /// Type 4 files are used for Textures
        /// </remarks>
        /// <param name="internalPath">The internal file path of the item</param>
        /// <param name="forceOriginal">Flag used to get original game data</param>
        /// <returns>An XivTex containing all the type 4 texture data</returns>
        public async Task<XivTex> GetType4Data(string internalPath, bool forceOriginal)
        {
            var index = new Index(_gameDirectory);
            var modding = new Modding(_gameDirectory);

            var dataFile = GetDataFileFromPath(internalPath);

            if (forceOriginal)
            {
                // Checks if the item being imported already exists in the modlist
                var modEntry = await modding.TryGetModEntry(internalPath);

                // If the file exists in the modlist, get the data from the original data
                if (modEntry != null)
                {
                    return await GetType4Data(modEntry.data.originalOffset, dataFile);
                }
            }

            // If it doesn't exist in the modlist(the item is not modded) or force original is false,
            // grab the data directly from them index file. 

            var folder = Path.GetDirectoryName(internalPath);
            folder = folder.Replace("\\", "/");
            var file = Path.GetFileName(internalPath);

            var offset = await index.GetDataOffset(HashGenerator.GetHash(folder), HashGenerator.GetHash(file),
                dataFile);

            if (offset == 0)
            {
                throw new Exception($"Could not find offest for {internalPath}");
            }

            return await GetType4Data(offset, dataFile);
        }


        /// <summary>
        /// Gets the data for Type 4 (Texture) files.
        /// </summary>
        /// <remarks>
        /// Type 4 files are used for Textures
        /// </remarks>
        /// <param name="offset">Offset to the texture data.</param>
        /// <param name="dataFile">The data file that contains the data.</param>
        /// <returns>An XivTex containing all the type 4 texture data</returns>
        public async Task<XivTex> GetType4Data(int offset, XivDataFile dataFile)
        {
            var xivTex = new XivTex();

            var decompressedData = new List<byte>();

            // This formula is used to obtain the dat number in which the offset is located
            var datNum = ((offset / 8) & 0x0F) / 2;

            await _semaphoreSlim.WaitAsync();

            try
            {
                offset = OffsetCorrection(datNum, offset);

                var datPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{DatExtension}{datNum}");

                await Task.Run(async () =>
                {
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
                        xivTex.Height = br.ReadInt16();

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

                                var decompressedPartData = await IOUtil.Decompressor(compressedData, uncompressedSize);

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
                                        decompressedPartData =
                                            await IOUtil.Decompressor(compressedData, uncompressedSize);

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

                                    var uncompressedData = await IOUtil.Decompressor(compressedData, uncompressedSize);

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

                        if (decompressedData.Count < uncompressedFileSize)
                        {
                            var difference = uncompressedFileSize - decompressedData.Count;
                            var padding = new byte[difference];
                            Array.Clear(padding, 0, difference);
                            decompressedData.AddRange(padding);
                        }
                    }
                });

                xivTex.TexData = decompressedData.ToArray();
            }
            finally
            {
                _semaphoreSlim.Release();
            }

            return xivTex;
        }

        /// <summary>
        /// Gets the file type of an item
        /// </summary>
        /// <param name="offset">Offset to the texture data.</param>
        /// <param name="dataFile">The data file that contains the data.</param>
        /// <returns>The file type</returns>
        public int GetFileType(int offset, XivDataFile dataFile)
        {
            // This formula is used to obtain the dat number in which the offset is located
            var datNum = ((offset / 8) & 0x0F) / 2;

            offset = OffsetCorrection(datNum, offset);

            var datPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{DatExtension}{datNum}");

            if (File.Exists(datPath))
            {
                using (var br = new BinaryReader(File.OpenRead(datPath)))
                {
                    br.BaseStream.Seek(offset, SeekOrigin.Begin);

                    br.ReadInt32(); // Header Length
                    return br.ReadInt32(); // File Type
                }
            }
            else
            {
                throw new Exception($"Unable to find {datPath}");
            }
        }

        public byte[] GetRawData(int offset, XivDataFile dataFile, int dataSize)
        {
            // This formula is used to obtain the dat number in which the offset is located
            var datNum = ((offset / 8) & 0x0F) / 2;

            offset = OffsetCorrection(datNum, offset);

            var datPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{DatExtension}{datNum}");

            using (var br = new BinaryReader(File.OpenRead(datPath)))
            {
                try
                {
                    br.BaseStream.Seek(offset, SeekOrigin.Begin);

                    return br.ReadBytes(dataSize);
                }
                catch
                {
                    return null;
                }

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
        /// <param name="importData">The data to be written.</param>
        /// <param name="modEntry">The modlist entry (if any) for the given file.</param>
        /// <param name="internalFilePath">The internal file path of the item being modified.</param>
        /// <param name="category">The category of the item.</param>
        /// <param name="itemName">The name of the item being modified.</param>
        /// <param name="dataFile">The data file to which we write the data</param>
        /// <param name="source">The source/application that is writing to the dat.</param>
        /// <param name="dataType">The data type (2, 3, 4)</param>
        /// <param name="modPack">The modpack associated with the import data if any</param>
        /// <returns>The new offset in which the modified data was placed.</returns>
        public async Task<int> WriteToDat(List<byte> importData, Mod modEntry, string internalFilePath,
            string category, string itemName, XivDataFile dataFile, string source, int dataType,
            ModPack modPack = null)
        {
            var offset = 0;
            var dataOverwritten = false;

            internalFilePath = internalFilePath.Replace("\\", "/");

            var index = new Index(_gameDirectory);

            var datNum = GetLargestDatNumber(dataFile);

            var modDatPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{DatExtension}{datNum}");

            if (category.Equals(itemName))
            {
                category = XivStrings.Character;
            }

            // If there is an existing modlist entry, use that data to get the modDatPath
            if (modEntry != null)
            {
                datNum = ((modEntry.data.modOffset / 8) & 0x0F) / 2;
                modDatPath = Path.Combine(_gameDirectory.FullName, $"{modEntry.datFile}{DatExtension}{datNum}");

                if (!File.Exists(modDatPath))
                {
                    throw new Exception($"A mod entry is pointing to {Path.GetFileName(modDatPath)}, but the file does not exist.\n\n" +
                                        $"It is recommended to do a Start Over.");
                }
            }

            var fileLength = new FileInfo(modDatPath).Length;

            // Creates a new Dat if the current dat is at the 2GB limit
            if (modEntry == null)
            {
                if (fileLength >= 2000000000)
                {
                    datNum = CreateNewDat(dataFile);

                    modDatPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{DatExtension}{datNum}");
                }
                else
                {
                    // If it is an original dat file, then create a new mod dat file
                    if (await IsOriginalDat(dataFile))
                    {
                        datNum = CreateNewDat(dataFile);

                        modDatPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{DatExtension}{datNum}");
                    }
                }
            }

            if (datNum >= 8)
            {
                throw new NotSupportedException($"Dat limit has been reached, no new mods can be imported for {dataFile.GetDataFileName()}");
            }

            // Checks to make sure the offsets in the mod list are not 0
            // If they are 0, something went wrong in the import proccess (Technically shouldn't happen)
            if (modEntry != null)
            {
                if (modEntry.data.modOffset == 0)
                {
                    throw new Exception("The mod offset located in the mod list cannot be 0");
                }

                if (modEntry.data.originalOffset == 0)
                {
                    throw new Exception("The original offset located in the mod list cannot be 0");
                }
            }

            /* 
             * If the item has been previously modified and the compressed data being imported is smaller or equal to the existing data
             *  replace the existing data with new data.
             */
            if (modEntry != null && importData.Count <= modEntry.data.modSize)
            {
                if (modEntry.data.modOffset != 0)
                {
                    var sizeDiff = modEntry.data.modSize - importData.Count;

                    datNum = ((modEntry.data.modOffset / 8) & 0x0F) / 2;
                    modDatPath = Path.Combine(_gameDirectory.FullName, $"{modEntry.datFile}{DatExtension}{datNum}");
                    var datOffsetAmount = 16 * datNum;

                    using (var bw = new BinaryWriter(File.OpenWrite(modDatPath)))
                    {
                        bw.BaseStream.Seek(modEntry.data.modOffset - datOffsetAmount, SeekOrigin.Begin);

                        bw.Write(importData.ToArray());

                        bw.Write(new byte[sizeDiff]);
                    }

                    await index.UpdateIndex(modEntry.data.modOffset, internalFilePath, dataFile);
                    await index.UpdateIndex2(modEntry.data.modOffset, internalFilePath, dataFile);

                    offset = modEntry.data.modOffset;

                    dataOverwritten = true;

                    var modList = JsonConvert.DeserializeObject<ModList>(File.ReadAllText(_modListDirectory.FullName));

                    var entryEnableUpdate = (from entry in modList.Mods
                        where entry.fullPath.Equals(modEntry.fullPath)
                        select entry).FirstOrDefault();

                    entryEnableUpdate.enabled = true;

                    if (modPack != null)
                    {
                        entryEnableUpdate.modPack = modPack;
                    }

                    File.WriteAllText(_modListDirectory.FullName, JsonConvert.SerializeObject(modList, Formatting.Indented));
                }
            }
            else
            {
                /* 
                 * If there is an empty entry in the modlist and the compressed data being imported is smaller or equal to the available space
                *  write the compressed data in the existing space.
                */

                var modList = JsonConvert.DeserializeObject<ModList>(File.ReadAllText(_modListDirectory.FullName));

                if (modList != null && modList.emptyCount > 0)
                {
                    foreach (var mod in modList.Mods)
                    {
                        if (!mod.fullPath.Equals(string.Empty) || !mod.datFile.Equals(dataFile.GetDataFileName()))
                            continue;

                        if (mod.data.modOffset == 0) continue;

                        var emptyEntryLength = mod.data.modSize;

                        if (emptyEntryLength > importData.Count)
                        {
                            var sizeDiff = emptyEntryLength - importData.Count;

                            datNum = ((mod.data.modOffset / 8) & 0x0F) / 2;
                            modDatPath = Path.Combine(_gameDirectory.FullName, $"{mod.datFile}{DatExtension}{datNum}");
                            var datOffsetAmount = 16 * datNum;

                            using (var bw = new BinaryWriter(File.OpenWrite(modDatPath)))
                            {
                                bw.BaseStream.Seek(mod.data.modOffset - datOffsetAmount, SeekOrigin.Begin);

                                bw.Write(importData.ToArray());

                                bw.Write(new byte[sizeDiff]);
                            }

                            var originalOffset = await index.UpdateIndex(mod.data.modOffset, internalFilePath, dataFile) * 8;
                            await index.UpdateIndex2(mod.data.modOffset, internalFilePath, dataFile);

                            // The imported data was larger than the original existing mod,
                            // and an empty slot large enough for the data was available,
                            // so we need to empty out the original entry so it may be used later
                            if (modEntry != null)
                            {
                                var entryToEmpty = (from entry in modList.Mods
                                    where entry.fullPath.Equals(modEntry.fullPath)
                                    select entry).FirstOrDefault();

                                originalOffset = entryToEmpty.data.originalOffset;

                                entryToEmpty.name = string.Empty;
                                entryToEmpty.category = string.Empty;
                                entryToEmpty.fullPath = string.Empty;
                                entryToEmpty.source = string.Empty;
                                entryToEmpty.modPack = null;
                                entryToEmpty.enabled = false;
                                entryToEmpty.data.originalOffset = 0;
                                entryToEmpty.data.dataType = 0;

                                modList.emptyCount += 1;
                            }

                            // Replace the empty entry with the new data
                            mod.source = source;
                            mod.name = itemName;
                            mod.category = category;
                            mod.fullPath = internalFilePath;
                            mod.datFile = dataFile.GetDataFileName();
                            mod.data.originalOffset = originalOffset;
                            mod.data.dataType = dataType;
                            mod.enabled = true;
                            mod.modPack = modPack;

                            modList.emptyCount -= 1;
                            modList.modCount += 1;

                            File.WriteAllText(_modListDirectory.FullName, JsonConvert.SerializeObject(modList, Formatting.Indented));

                            offset = mod.data.modOffset;

                            dataOverwritten = true;
                            break;
                        }
                    }
                }

                // If there was no mod entry overwritten, write the new import data at the end of the dat file
                if (!dataOverwritten)
                {
                    /*
                     * If the item has been previously modified, but the new compressed data to be imported is larger than the existing data,
                     * and no empty slot was found for it, then write the data to the highest dat,
                     * or create a new one if necessary
                    */
                    if (modEntry != null)
                    {
                        datNum = GetLargestDatNumber(dataFile);

                        modDatPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{DatExtension}{datNum}");

                        fileLength = new FileInfo(modDatPath).Length;

                        if (fileLength >= 2000000000)
                        {
                            datNum = CreateNewDat(dataFile);

                            modDatPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{DatExtension}{datNum}");
                        }

                        if (datNum >= 8)
                        {
                            throw new NotSupportedException($"Dat limit has been reached, no new mods can be imported for {dataFile.GetDataFileName()}");
                        }
                    }

                    using (var bw = new BinaryWriter(File.OpenWrite(modDatPath)))
                    {
                        bw.BaseStream.Seek(0, SeekOrigin.End);

                        while ((bw.BaseStream.Position & 0xFF) != 0)
                        {
                            bw.Write((byte)0);
                        }

                        var eof = (int)bw.BaseStream.Position + importData.Count;

                        while ((eof & 0xFF) != 0)
                        {
                            importData.AddRange(new byte[16]);
                            eof = eof + 16;
                        }

                        var datOffsetAmount = 16 * datNum;
                        offset = (int)bw.BaseStream.Position + datOffsetAmount;

                        if (offset != 0)
                        {
                            bw.Write(importData.ToArray());
                        }
                        else
                        {
                            throw new Exception("There was an issue obtaining the offset to write to.");
                        }
                    }
                }
            }

            // If there was no mod entry overwritten, write a new mod entry
            if (!dataOverwritten)
            {
                if (offset != 0)
                {
                    var modList = JsonConvert.DeserializeObject<ModList>(File.ReadAllText(_modListDirectory.FullName));

                    var oldOffset = await index.UpdateIndex(offset, internalFilePath, dataFile) * 8;
                    await index.UpdateIndex2(offset, internalFilePath, dataFile);

                    /*
                     * If the item has been previously modified, but the new compressed data to be imported is larger than the existing data,
                     * and no empty slot was found for it, then empty out the entry from the modlist, 
                     * leaving the offset and size intact for future use
                    */
                    if (modEntry != null)
                    {
                        var entryToEmpty = (from entry in modList.Mods
                            where entry.fullPath.Equals(modEntry.fullPath)
                            select entry).FirstOrDefault();

                        oldOffset = entryToEmpty.data.originalOffset;

                        entryToEmpty.name = string.Empty;
                        entryToEmpty.category = string.Empty;
                        entryToEmpty.fullPath = string.Empty;
                        entryToEmpty.source = string.Empty;
                        entryToEmpty.modPack = null;
                        entryToEmpty.enabled = false;
                        entryToEmpty.data.originalOffset = 0;
                        entryToEmpty.data.dataType = 0;

                        modList.emptyCount += 1;
                    }

                    var newEntry = new Mod
                    {
                        source = source,
                        name = itemName,
                        category = category,
                        fullPath = internalFilePath,
                        datFile = dataFile.GetDataFileName(),
                        enabled = true,
                        modPack = modPack,
                        data = new Data
                        {
                            dataType = dataType,
                            originalOffset = oldOffset,
                            modOffset = offset,
                            modSize = importData.Count
                        }
                    };

                    modList.Mods.Add(newEntry);

                    modList.modCount += 1;

                    File.WriteAllText(_modListDirectory.FullName, JsonConvert.SerializeObject(modList, Formatting.Indented));
                }
            }

            return offset;
        }

        /// <summary>
        /// Dictionary that holds [Texture Code, Texture Format] data
        /// </summary>
        public static readonly Dictionary<int, XivTexFormat> TextureTypeDictionary = new Dictionary<int, XivTexFormat>
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