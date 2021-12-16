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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Resources;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data;
using xivModdingFramework.Cache;
using xivModdingFramework.General;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.Enums;

namespace xivModdingFramework.SqPack.FileTypes
{
    /// <summary>
    /// This class contains the methods that deal with the .dat file type
    /// </summary>
    public class Dat
    {
        public const string DatExtension = ".win32.dat";
        private readonly DirectoryInfo _gameDirectory;
        private readonly DirectoryInfo _modListDirectory;
        static SemaphoreSlim _lock = new SemaphoreSlim(1);

        public Dat(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;

            var modding = new Modding(_gameDirectory);
            modding.CreateModlist();
            _modListDirectory = modding.ModListDirectory;
        }

        public static long GetMaximumDatSize()
        {
            var dxMode = XivCache.GameInfo.DxMode;
            var is64b = Environment.Is64BitOperatingSystem;
            var runningIn32bMode = IntPtr.Size == 4;

            if (dxMode < 11 || !is64b || runningIn32bMode)
            {
                return 2000000000;
            } else
            {
                // Check the user's FFXIV installation drive to see what the maximum file size is for their file system.
                var drive = XivCache.GameInfo.GameDirectory.FullName.Substring(0, 1);
                return GetMaximumFileSize(drive);
            }
        }

        // Returns the maximum file size in bytes on the filesystem type of the specified drive.
        private static long GetMaximumFileSize(string drive)
        {
            var driveInfo = new System.IO.DriveInfo(drive);

            switch (driveInfo.DriveFormat)
            {
                case "FAT16":
                    return 2147483647;
                case "FAT32":
                    return 4294967296;
                default:
                    // Unknown HDD Format, default to the max index limit.
                    // 2 ^35 is the maximum addressable size in the Index files. (28 precision bits, left-shifted 7 bits (increments of 128)
                    return 34359738368;
            }
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
        public int CreateNewDat(XivDataFile dataFile, bool alreadyLocked = false)
        {
            var nextDatNumber = GetLargestDatNumber(dataFile, alreadyLocked) + 1;

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
            index.UpdateIndexDatCount(dataFile, nextDatNumber, alreadyLocked);

            return nextDatNumber;
        }

        /// <summary>
        /// Gets the largest dat number for a given data file.
        /// </summary>
        /// <param name="dataFile">The data file to check.</param>
        /// <returns>The largest dat number for the given data file.</returns>
        public int GetLargestDatNumber(XivDataFile dataFile, bool alreadyLocked = false)
        {

            string[] allFiles = null;
            if (!alreadyLocked)
            {
                _lock.Wait();
            }
            try
            {
                allFiles = Directory.GetFiles(_gameDirectory.FullName);
            }
            finally
            {
                if (!alreadyLocked)
                {
                    _lock.Release();
                }
            }

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


        // Whether a DAT is an original dat or a Modded dat never changes during runtime.
        // As such, we can cache that information, rather than having to constantly re-check the filesystem (somewhat expensive operation)
        private static Dictionary<XivDataFile, Dictionary<int, bool>> OriginalDatStatus = new Dictionary<XivDataFile, Dictionary<int, bool>>();

        /// <summary>
        /// Determines whether a mod dat already exists
        /// </summary>
        /// <param name="dataFile">The dat file to check.</param>
        /// <returns>True if it is original, false otherwise</returns>
        private async Task<bool> IsOriginalDat(XivDataFile dataFile, int datNum, bool alreadyLocked = false)
        {
            if(!OriginalDatStatus.ContainsKey(dataFile))
            {
                OriginalDatStatus.Add(dataFile, new Dictionary<int, bool>());
            }

            if(OriginalDatStatus[dataFile].ContainsKey(datNum))
            {
                return OriginalDatStatus[dataFile][datNum];
            }

            var unmoddedList = await GetUnmoddedDatList(dataFile, alreadyLocked);
            var datPath = $"{dataFile.GetDataFileName()}{DatExtension}{datNum}";

            for(int i = 0; i < unmoddedList.Count; i++)
            {
                unmoddedList[i] = Path.GetFileName(unmoddedList[i]);
            }

            var result = unmoddedList.Contains(datPath);
            OriginalDatStatus[dataFile][datNum] = result;

            return result;
        }

        /// <summary>
        /// Gets the modded dat files
        /// </summary>
        /// <param name="dataFile">The data file to check</param>
        /// <returns>A list of modded dat files</returns>
        public async Task<List<string>> GetUnmoddedDatList(XivDataFile dataFile, bool alreadyLocked = false)
        {
            var datList = new List<string>();

            await Task.Run(async () =>
            {
                if (!alreadyLocked)
                {
                    await _lock.WaitAsync();
                }
                try
                {
                    for (var i = 0; i < 20; i++)
                    {
                        var datFilePath = $"{_gameDirectory}/{dataFile.GetDataFileName()}.win32.dat{i}";

                        if (File.Exists(datFilePath))
                        {
                            using (var binaryReader = new BinaryReader(File.OpenRead(datFilePath)))
                            {
                                binaryReader.BaseStream.Seek(24, SeekOrigin.Begin);
                                bool anyNonZero = false;
                                for (int byteIdx = 0; byteIdx < 8; byteIdx++)
                                {
                                    if (binaryReader.ReadByte() != 0)
                                    {
                                        anyNonZero = true;
                                        break;
                                    }
                                }

                                // If there is any data in these bytes, it is an original dat.
                                if (anyNonZero)
                                {
                                    datList.Add(datFilePath);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    if (!alreadyLocked)
                    {
                        _lock.Release();
                    }
                }
            });
            return datList;
        }

        /// <summary>
        /// Gets the modded dat files
        /// </summary>
        /// <param name="dataFile">The data file to check</param>
        /// <returns>A list of modded dat files</returns>
        public async Task<List<string>> GetModdedDatList(XivDataFile dataFile, bool alreadyLocked = false)
        {
            var datList = new List<string>();

            await Task.Run(async () =>
            {
                if (!alreadyLocked)
                {
                    await _lock.WaitAsync();
                }
                try
                {
                    for (var i = 1; i < 20; i++)
                    {
                        var datFilePath = $"{_gameDirectory}/{dataFile.GetDataFileName()}.win32.dat{i}";

                        if (File.Exists(datFilePath))
                        {
                            // Due to an issue where 060000 dat1 gets deleted, we are skipping it here
                            if (datFilePath.Contains("060000.win32.dat1"))
                            {
                                continue;
                            }
                            using (var binaryReader = new BinaryReader(File.OpenRead(datFilePath)))
                            {
                                binaryReader.BaseStream.Seek(24, SeekOrigin.Begin);
                                bool anyNonZero = false;
                                for (int byteIdx = 0; byteIdx < 8; byteIdx++)
                                {
                                    if (binaryReader.ReadByte() != 0)
                                    {
                                        anyNonZero = true;
                                        break;
                                    }
                                }

                                // If it's all zeros, this is a custom modded dat.
                                if(!anyNonZero)
                                {
                                    datList.Add(datFilePath);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    if (!alreadyLocked)
                    {
                        _lock.Release();
                    }
                }
            });
            return datList;
        }

        /// <summary>
        /// Makes the header for the SqPack portion of the dat file.
        /// </summary>
        /// <returns>byte array containing the header.</returns>
        internal static byte[] MakeSqPackHeader()
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
        internal static byte[] MakeDatHeader()
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
        public async Task<byte[]> GetType2Data(string internalPath, bool forceOriginal, IndexFile index = null, ModList modlist = null)
        {
            var dataFile = IOUtil.GetDataFileFromPath(internalPath);

            if (index == null)
            {

                var _index = new Index(_gameDirectory);
                index = await _index.GetIndexFile(dataFile, false, true);
            }

            if (forceOriginal)
            {
                var _modding = new Modding(_gameDirectory);
                modlist = await _modding.GetModListAsync();
                // Checks if the item being imported already exists in the modlist
                var modEntry = modlist.Mods.FirstOrDefault(x => x.fullPath == internalPath);

                // If the file exists in the modlist, get the data from the original data
                if (modEntry != null)
                {
                    return await GetType2Data(modEntry.data.originalOffset, dataFile);
                }
            }

            // If it doesn't exist in the modlist(the item is not modded) or force original is false,
            // grab the data directly from them index file.

            var offset = index.Get8xDataOffset(internalPath);

            if (offset == 0)
            {
                throw new Exception($"Could not find offset for {internalPath}");
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
        public async Task<byte[]> GetType2Data(long offset, XivDataFile dataFile)
        {
            if (offset <= 0)
            {
                throw new InvalidDataException("Cannot get file data without valid offset.");
            }


            var type2Bytes = new List<byte>();

            // This formula is used to obtain the dat number in which the offset is located
            var datNum = (int) ((offset / 8) & 0x0F) / 2;

            var datPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{DatExtension}{datNum}");


            await _lock.WaitAsync();
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
                _lock.Release();
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
        public async Task<long> ImportType2Data(DirectoryInfo importFilePath, string internalPath, string source, IItem referenceItem = null, IndexFile cachedIndexFile = null, ModList cachedModList = null)
        {
            return await ImportType2Data(File.ReadAllBytes(importFilePath.FullName), internalPath, source, referenceItem, cachedIndexFile, cachedModList);
        }

        /// <summary>
        /// Imports type 2 data.
        /// </summary>
        /// <param name="dataToImport">Raw data to import</param>
        /// <param name="internalPath">Internal path to update index for.</param>
        /// <param name="source">Source application making the changes/</param>
        /// <param name="referenceItem">Item to reference for name/category information, etc.</param>
        /// <param name="cachedIndexFile">Cached index file, if available</param>
        /// <param name="cachedModList">Cached modlist file, if available</param>
        /// <returns></returns>
        public async Task<long> ImportType2Data(byte[] dataToImport,  string internalPath, string source, IItem referenceItem = null, IndexFile cachedIndexFile = null, ModList cachedModList = null)
        {
            var dataFile = GetDataFileFromPath(internalPath);
            var modding = new Modding(_gameDirectory);

            var modEntry = await modding.TryGetModEntry(internalPath);
            var newData = (await CreateType2Data(dataToImport));

            var newOffset = await WriteModFile(newData, internalPath, source, referenceItem, cachedIndexFile, cachedModList);

            // This can be -1 after Lumina imports
            if (newOffset == 0)
            {
                throw new Exception("There was an error writing to the dat file. Offset returned was 0.");
            }

            return newOffset;
        }


        /// <summary>
        /// Create any Type 2 data
        /// </summary>
        /// <param name="dataToCreate">Bytes to Type 2data</param>
        /// <returns></returns>
        public async Task<byte[]> CreateType2Data(byte[] dataToCreate)
        {
            var newData = new List<byte>();
            var headerData = new List<byte>();
            var dataBlocks = new List<byte>();

            // Header size is defaulted to 128, but may need to change if the data being imported is very large.
            headerData.AddRange(BitConverter.GetBytes(128));
            headerData.AddRange(BitConverter.GetBytes(2));
            headerData.AddRange(BitConverter.GetBytes(dataToCreate.Length));

            var dataOffset = 0;
            var totalCompSize = 0;
            var uncompressedLength = dataToCreate.Length;

            var partCount = (int)Math.Ceiling(uncompressedLength / 16000f);

            headerData.AddRange(BitConverter.GetBytes(partCount));

            var remainder = uncompressedLength;

            using (var binaryReader = new BinaryReader(new MemoryStream(dataToCreate)))
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

            var headerSize = headerData.Count;
            var rem = headerSize % 128;
            if(rem != 0)
            {
                headerSize += (128 - rem);
            }

            headerData.RemoveRange(0, 4);
            headerData.InsertRange(0, BitConverter.GetBytes(headerSize));

            var headerPadding = rem == 0 ? 0 : 128 - rem;
            headerData.AddRange(new byte[headerPadding]);

            newData.AddRange(headerData);
            newData.AddRange(dataBlocks);
            return newData.ToArray();
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
                throw new Exception($"Could not find offset for {internalPath}");
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
        public async Task<(int MeshCount, int MaterialCount, byte[] Data)> GetType3Data(long offset, XivDataFile dataFile)
        {
            if (offset <= 0)
            {
                throw new InvalidDataException("Cannot get file data without valid offset.");
            }

            // This formula is used to obtain the dat number in which the offset is located
            var datNum = (int) ((offset / 8) & 0x0F) / 2;

            offset = OffsetCorrection(datNum, offset);

            var datPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{DatExtension}{datNum}");

            var byteList = new List<byte>();
            var meshCount = 0;
            var materialCount = 0;

            await Task.Run(async () =>
            {
                await _lock.WaitAsync();
                try
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
                            long lastPos = br.BaseStream.Position;

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
                }
                finally
                {
                    _lock.Release();
                }
            });

            return (meshCount, materialCount, byteList.ToArray());
        }

        public async Task<uint> GetReportedType4UncompressedSize(XivDataFile df, long offsetWithDatNumber)
        {
            // This formula is used to obtain the dat number in which the offset is located
            var datNum = (int)((offsetWithDatNumber / 8) & 0x0F) / 2;

            var offset = OffsetCorrection(datNum, offsetWithDatNumber);

            var datPath = Path.Combine(_gameDirectory.FullName, $"{df.GetDataFileName()}{DatExtension}{datNum}");

            return await Task.Run(async () =>
            {
                using (var br = new BinaryReader(File.OpenRead(datPath)))
                {
                    br.BaseStream.Seek(offset+8, SeekOrigin.Begin);

                    var size = br.ReadUInt32();
                    return size;
                }
            });
        }

        public async Task UpdateType4UncompressedSize(XivDataFile df, long offsetWithDatNumber, uint correctedFileSize)
        {
            // This formula is used to obtain the dat number in which the offset is located
            var datNum = (int)((offsetWithDatNumber / 8) & 0x0F) / 2;

            var offset = OffsetCorrection(datNum, offsetWithDatNumber);

            var datPath = Path.Combine(_gameDirectory.FullName, $"{df.GetDataFileName()}{DatExtension}{datNum}");

            await Task.Run(async () =>
            {
                using (var br = new BinaryWriter(File.OpenWrite(datPath)))
                {
                    br.BaseStream.Seek(offset + 8, SeekOrigin.Begin);
                    br.Write(correctedFileSize);
                }
            });
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
                throw new Exception($"Could not find offset for {internalPath}");
            }

            return await GetType4Data(offset, dataFile);
        }


        public async Task<int> GetCompressedFileSize(long offset, XivDataFile dataFile)
        {
            if (offset <= 0)
            {
                throw new InvalidDataException("Cannot get file size data without valid offset.");
            }


            var xivTex = new XivTex();

            var decompressedData = new List<byte>();

            // This formula is used to obtain the dat number in which the offset is located
            var datNum = (int)((offset / 8) & 0x0F) / 2;

            await _lock.WaitAsync();

            try
            {
                offset = OffsetCorrection(datNum, offset);

                var datPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{DatExtension}{datNum}");

                return await Task.Run(async () =>
                {
                    using (var br = new BinaryReader(File.OpenRead(datPath)))
                    {
                        br.BaseStream.Seek(offset, SeekOrigin.Begin);
                        var headerLength = br.ReadInt32();
                        var fileType = br.ReadInt32();
                        var uncompSize = br.ReadInt32();
                        var unknown = br.ReadInt32();
                        var maxBufferSize = br.ReadInt32();
                        var blockCount = br.ReadInt16();

                        var endOfHeader = offset + headerLength;

                        if (fileType != 2 && fileType != 3 && fileType != 4)
                        {
                            throw new NotSupportedException("Cannot get compressed file size of unknown type.");
                        }

                        int compSize = 0;
                        // Ok, time to parse the block headers and figure out how long the compressed data runs.
                        if(fileType == 2)
                        {
                            br.BaseStream.Seek(endOfHeader + 4, SeekOrigin.Begin);
                            var lastSize = 0;
                            var lastOffset = 0;
                            for(int i = 0; i < blockCount; i++)
                            {
                                br.BaseStream.Seek(offset + (24 + (8 * i)), SeekOrigin.Begin);
                                var blockOffset = br.ReadInt32();
                                var blockCompressedSize = br.ReadUInt16();

                                lastOffset = blockOffset;
                                lastSize = blockCompressedSize + 16;    // 16 bytes of header data per block.
                            }

                            // Pretty straight forward.  Header + Total size of the compressed data.
                            compSize = headerLength + lastOffset + lastSize;

                        } else if(fileType == 3)
                        {

                            // 24 byte header, then 88 bytes to the first chunk offset.
                            br.BaseStream.Seek(offset + 112, SeekOrigin.Begin);
                            var firstOffset = br.ReadInt32();

                            // 24 byte header, then 178 bytes to the start of the block count.
                            br.BaseStream.Seek(offset + 178, SeekOrigin.Begin);

                            var totalBlocks = 0;
                            for (var i = 0; i < 11; i++)
                            {
                                totalBlocks += br.ReadUInt16();
                            }


                            // 24 byte header, then 208 bytes to the list of block sizes.
                            br.BaseStream.Seek(offset + 208, SeekOrigin.Begin);

                            var blockSizes = new int[totalBlocks];
                            for (var i = 0; i < totalBlocks; i++)
                            {
                                blockSizes[i] = br.ReadUInt16();
                            }

                            int totalCompressedSize = 0;
                            foreach(var size in blockSizes)
                            {
                                totalCompressedSize += size;
                            }


                            // Header + Chunk headers + compressed data.
                            compSize = headerLength + firstOffset + totalCompressedSize;
                        } else if(fileType == 4)
                        {
                            br.BaseStream.Seek(endOfHeader + 4, SeekOrigin.Begin);
                            // Textures.
                            var lastOffset = 0;
                            var lastSize = 0;
                            var mipMapInfoOffset = offset + 24;
                            for (int i = 0, j = 0; i < blockCount; i++)
                            {
                                br.BaseStream.Seek(mipMapInfoOffset + j, SeekOrigin.Begin);

                                j = j + 20;

                                var offsetFromHeaderEnd = br.ReadInt32();
                                var mipMapCompressedSize = br.ReadInt32();


                                lastOffset = offsetFromHeaderEnd;
                                lastSize = mipMapCompressedSize;
                            }

                            // Pretty straight forward.  Header + Total size of the compressed data.
                            compSize = headerLength + lastOffset + lastSize;

                        }


                        // Round out to the nearest 256 bytes.
                        if (compSize % 256 != 0)
                        {
                            var padding = 256 - (compSize % 256);
                            compSize += padding;
                        }
                        return compSize;

                    }
                });
            }
            finally
            {
                _lock.Release();
            }
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
        public async Task<XivTex> GetType4Data(long offset, XivDataFile dataFile)
        {
            if (offset <= 0)
            {
                throw new InvalidDataException("Cannot get file size data without valid offset.");
            }

            var xivTex = new XivTex();

            var decompressedData = new List<byte>();

            // This formula is used to obtain the dat number in which the offset is located
            var datNum = (int)((offset / 8) & 0x0F) / 2;

            await _lock.WaitAsync();

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
                        var ikd1 = br.ReadInt32();
                        var ikd2 = br.ReadInt32();
                        xivTex.MipMapCount = br.ReadInt32();

                        var endOfHeader = offset + headerLength;
                        var mipMapInfoOffset = offset + 24;

                        br.BaseStream.Seek(endOfHeader + 4, SeekOrigin.Begin);

                        xivTex.TextureFormat = TextureTypeDictionary[br.ReadInt32()];
                        xivTex.Width = br.ReadInt16();
                        xivTex.Height = br.ReadInt16();
                        var pos = br.BaseStream.Position;
                        xivTex.Layers = br.ReadInt16();
                        var imageCount2 = br.ReadInt16();

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
                _lock.Release();
            }

            return xivTex;
        }

        /// <summary>
        /// Gets the file type of an item
        /// </summary>
        /// <param name="offset">Offset to the texture data.</param>
        /// <param name="dataFile">The data file that contains the data.</param>
        /// <returns>The file type</returns>
        public int GetFileType(long offset, XivDataFile dataFile)
        {
            // This formula is used to obtain the dat number in which the offset is located
            var datNum = (int)((offset / 8) & 0x0F) / 2;

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

        public byte[] GetRawData(long offset, XivDataFile dataFile, int dataSize)
        {
            // This formula is used to obtain the dat number in which the offset is located
            var datNum = (int)((offset / 8) & 0x0F) / 2;

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
        public byte[] MakeType4DatHeader(XivTexFormat format, List<short> mipPartOffsets, List<short> mipPartCount, int uncompressedLength, int newMipCount, int newWidth, int newHeight)
        {
            var headerData = new List<byte>();

            var headerSize = 24 + (newMipCount * 20) + (mipPartOffsets.Count * 2);
            var headerPadding = 128 - (headerSize % 128);

            headerData.AddRange(BitConverter.GetBytes(headerSize + headerPadding));
            headerData.AddRange(BitConverter.GetBytes(4));
            headerData.AddRange(BitConverter.GetBytes(uncompressedLength + 80));
            headerData.AddRange(BitConverter.GetBytes(0));
            headerData.AddRange(BitConverter.GetBytes(0));
            headerData.AddRange(BitConverter.GetBytes(newMipCount));


            var partIndex = 0;
            var mipOffsetIndex = 80;
            var uncompMipSize = newHeight * newWidth;

            switch (format)
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
        /// Gets the first DAT file with space to add a new file to it.
        /// Ignores default DAT files, and creates a new DAT file if necessary.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <returns></returns>
        private async Task<int> GetFirstDatWithSpace(XivDataFile dataFile, int fileSize = 0, bool alreadyLocked = false)
        {
            if(fileSize < 0)
            {
                throw new InvalidDataException("Cannot check space for a negative size file.");
            }

            if(fileSize % 256 != 0)
            {
                // File will be rounded up to 256 bytes on entry, so we have to account for that.
                var remainder = 256 - (fileSize % 256);
                fileSize += remainder;
            }

            var targetDat = -1;
            Dictionary<int, FileInfo> finfos = new Dictionary<int, FileInfo>(8);

            // Scan all the dat numbers...
            for (int i = 0; i < 8; i++)
            {
                var original = await IsOriginalDat(dataFile, i, alreadyLocked);

                // Don't let us inject to original dat files.
                if (original) continue;

                var datPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{DatExtension}{i}");


                var fInfo = new FileInfo(datPath);
                finfos[i] = fInfo;

                // If the DAT doesn't exist at all, we can assume we need to create a new DAT.
                if (fInfo == null || !fInfo.Exists) break;


                var datSize = fInfo.Length;

                // Files will only be injected on multiples of 256 bytes, so we have to account for the potential
                // extra padding space to get to that point.
                if(datSize % 256 != 0)
                {
                    var remainder = 256 - (datSize % 256);
                    datSize += remainder;
                }

                // Dat is too large to fit this file, we can't write to it.
                if (datSize + fileSize >= GetMaximumDatSize()) continue;


                // Found an existing dat that has space.
                targetDat = i;
                break;
            }
            //sound/battle/enpc/se_enpc_alchemist_goblin_a.scd
            // Didn't find a DAT file with space, gotta create a new one.
            if (targetDat < 0)
            {
                targetDat = CreateNewDat(dataFile, alreadyLocked);
            }

            if(targetDat > 7 || targetDat < 0)
            {
                throw new NotSupportedException("Maximum data size limit reached for DAT: " + dataFile.GetDataFileName());
            }
            return targetDat;
        }

        /// <summary>
        /// Copies a given file to a new location in the game files.
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <param name="targetPath"></param>
        /// <param name="category"></param>
        /// <param name="itemName"></param>
        /// <param name="source"></param>
        /// <returns></returns>
        public async Task<long> CopyFile(string sourcePath, string targetPath, string source = "Unknown", bool overwrite = false, IItem referenceItem = null, IndexFile cachedIndexFile = null, ModList cachedModlist = null)
        {
            var _index = new Index(_gameDirectory);
            long offset = 0;
            if (cachedIndexFile != null)
            {
                offset = cachedIndexFile.Get8xDataOffset(sourcePath);
            }
            else
            {
                offset = await _index.GetDataOffset(sourcePath);
            }
            if(offset == 0)
            {
                return 0;
            }

            var dataFile = IOUtil.GetDataFileFromPath(sourcePath);
            return await CopyFile(offset, dataFile, targetPath, source, overwrite, referenceItem, cachedIndexFile, cachedModlist);
        }

        /// <summary>
        /// Copies a file from a given offset to a new path in the game files.
        ///
        /// If the Index File and Modlist are provided, the actions will only be wrtiten to memory,
        /// and not to the live Index Files/ModList.
        /// </summary>
        /// <param name="originalOffset"></param>
        /// <param name="originalDataFile"></param>
        /// <param name="targetPath"></param>
        /// <param name="source"></param>
        /// <returns></returns>
        public async Task<long> CopyFile(long originalOffset, XivDataFile originalDataFile, string targetPath, string source = "Unknown", bool overwrite = false, IItem referenceItem = null, IndexFile cachedIndexFile = null, ModList cachedModlist = null)
        {
            var _modding = new Modding(_gameDirectory);
            var _index = new Index(_gameDirectory);

            var exists = await _index.FileExists(targetPath);
            if(exists && !overwrite)
            {
                long offset = 0;
                if (cachedIndexFile != null)
                {
                    offset = cachedIndexFile.Get8xDataOffset(targetPath);
                }
                else
                {
                    offset = await _index.GetDataOffset(targetPath);
                }
                return offset;
            }

            var size = await GetCompressedFileSize(originalOffset, originalDataFile);
            var data = GetRawData(originalOffset, originalDataFile, size);


            XivDependencyRoot root = null;

            if (referenceItem == null)
            {
                try
                {
                    root = await XivCache.GetFirstRoot(targetPath);
                    if (root != null)
                    {
                        var item = root.GetFirstItem();

                        referenceItem = item;
                    }
                    else
                    {
                        referenceItem = new XivGenericItemModel()
                        {
                            Name = Path.GetFileName(targetPath),
                            SecondaryCategory = "Raw File Copy"
                        };
                    }
                }
                catch
                {
                    referenceItem = new XivGenericItemModel()
                    {
                        Name = Path.GetFileName(targetPath),
                        SecondaryCategory = "Raw File Copy"
                    };
                }
            }



            return await WriteModFile(data, targetPath, source, referenceItem, cachedIndexFile, cachedModlist);
        }


        /// <summary>
        /// Writes a new block of data to the given data file, without changing
        /// the indexes.  Returns the raw-index-style offset to the new data.
        ///
        /// A target offset of 0 or negative will append to the end of the first data file with space.
        /// </summary>
        /// <param name="importData"></param>
        /// <param name="dataFile"></param>
        /// <returns></returns>
        public async Task<uint> WriteToDat(byte[] importData, XivDataFile dataFile, long targetOffset = 0)
        {
            // Perform basic validation.
            if (importData == null || importData.Length < 8)
            {
                throw new Exception("Attempted to write NULL data to DAT files.");
            }

            var fileType = BitConverter.ToInt32(importData, 4);
            if (fileType < 2 || fileType > 4)
            {
                throw new Exception("Attempted to write Invalid data to DAT files.");
            }

            long seekPointer = 0;
            if(targetOffset >= 2048)
            {
                seekPointer = (targetOffset >> 7) << 7;

                // If the space we're told to write to would require modification,
                // don't allow writing to it, because we might potentially write
                // past the end of this safe slot.
                if(seekPointer % 256 != 0)
                {
                    seekPointer = 0;
                    targetOffset = 0;
                }
            }

            long filePointer = 0;
            await _lock.WaitAsync();
            try
            {
                // This finds the first dat with space, OR creates one if needed.
                var datNum = 0;
                if (targetOffset >= 2048)
                {
                    datNum = (int)(((targetOffset / 8) & 0xF) >> 1);
                    var defaultDats = (await GetUnmoddedDatList(dataFile, true)).Count;

                    if(datNum < defaultDats)
                    {
                        // Safety check.  No writing to default dats, even with an explicit offset.
                        datNum = await GetFirstDatWithSpace(dataFile, importData.Length, true);
                        targetOffset = 0;
                    }
                } else
                {
                    datNum = await GetFirstDatWithSpace(dataFile, importData.Length, true);
                }

                var datPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{DatExtension}{datNum}");

                // Copy the data into the file.
                BinaryWriter bw = null;

                try
                {
                    try
                    {
                        bw = new BinaryWriter(File.OpenWrite(datPath));
                    }
                    catch
                    {
                        if(bw != null)
                        {
                            bw.Dispose();
                        }

                        // Wait just a bit and try again.
                        await Task.Delay(100);
                        bw = new BinaryWriter(File.OpenWrite(datPath));
                    }

                    if (targetOffset >= 2048)
                    {
                        bw.BaseStream.Seek(seekPointer, SeekOrigin.Begin);
                    }
                    else
                    {
                        bw.BaseStream.Seek(0, SeekOrigin.End);
                    }

                    // Make sure we're starting on an actual accessible interval.
                    while ((bw.BaseStream.Position % 256) != 0)
                    {
                        bw.Write((byte)0);
                    }

                    filePointer = bw.BaseStream.Position;

                    // Write data.
                    bw.Write(importData);

                    // Make sure we end on an accessible interval as well to be safe.
                    while ((bw.BaseStream.Position % 256) != 0)
                    {
                        bw.Write((byte)0);
                    }
                }
                finally
                {
                    if (bw != null)
                    {
                        bw.Dispose();
                    }
                }

                var intFormat = (uint)(filePointer / 8);
                uint datIdentifier = (uint)(datNum * 2);

                uint indexOffset = (uint) (intFormat | datIdentifier);

                return indexOffset;
            } finally
            {
                _lock.Release();
            }
        }


        /// <summary>
        /// Writes a given block of data to the DAT files, updates the index to point to it for the given file path,
        /// creates or updates the modlist entry for the item, and triggers metadata expansion if needed.
        ///
        /// NOTE -- If the Index File and ModList are provided, the steps SAVING those entires are SKIPPED for performance.
        /// It is assumed if they are provided, that the calling function will handle saving them once it is done manipulating them.
        ///
        /// LUMINA - If Lumina writing is enabled, the indexes/modlist/dats will NEVER be modified by this function, making it
        /// functionally a NoOp() as far as the internal TexTools system state is concerned.  This means if another function
        /// relies upon the DATs/Indexes/Modlist to be altered coming out of this function, the calling function needs to
        /// assert() that Lumina writing is disabled.
        /// </summary>
        /// <param name="fileData"></param>
        /// <param name="internalFilePath"></param>
        /// <param name="sourceApplication"></param>
        /// <returns></returns>
        public async Task<long> WriteModFile(byte[] fileData, string internalFilePath, string sourceApplication, IItem referenceItem = null, IndexFile index = null, ModList modList = null)
        {
            var _modding = new Modding(XivCache.GameInfo.GameDirectory);
            var _index = new Index(XivCache.GameInfo.GameDirectory);
            var df = IOUtil.GetDataFileFromPath(internalFilePath);
            var doLumina = XivCache.GameInfo.UseLumina;
            var luminaOutDir = XivCache.GameInfo.LuminaDirectory;
            if (doLumina && luminaOutDir == null)
            {
                throw new InvalidDataException("Cannot perform Lumina imports without valid Lumina directory.");
            }

            if (doLumina && (luminaOutDir == null || !luminaOutDir.Exists))
                throw new ArgumentException("No valid lumina output path was specified.", nameof(luminaOutDir));

            if((index == null && modList != null ) || (index != null && modList == null))
            {
                throw new InvalidDataException("Index and Modlist must both be null or both be non-null.");
            }

            var doDatSave = index == null;

            modList ??= _modding.GetModList();

            var mod = modList.Mods.FirstOrDefault(x => x.fullPath == internalFilePath);

            string itemName = "Unknown";
            string category = "Unknown";
            if (referenceItem == null)
            {
                try
                {
                    var root = await XivCache.GetFirstRoot(internalFilePath);
                    if (root != null)
                    {
                        var item = root.GetFirstItem();

                        referenceItem = item;
                        itemName = referenceItem.GetModlistItemName();
                        category = referenceItem.GetModlistItemCategory();
                    }
                }
                catch
                {
                    itemName = Path.GetFileName(internalFilePath);
                    category = "Raw File";
                }
            } else
            {
                itemName = referenceItem.GetModlistItemName();
                category = referenceItem.GetModlistItemCategory();
            }

            var size = fileData.Length;
            if(size % 256 != 0)
            {
                size += 256 - (size % 256);
            }

            // Update the DAT files.
            uint rawOffset = 0;

            if (!doLumina)
            {
                if (mod != null && mod.data.modSize >= size && doDatSave)
                {
                    // If our existing mod slot is large enough to hold us, keep using it.
                    // *only* if we're going to immediately save the modlist though.
                    // Otherwise it's possible this index update may get rolled back, so it would be unsafe
                    // to overwrite any data.
                    rawOffset = await WriteToDat(fileData, df, mod.data.modOffset);

                }
                else if (index == null && doDatSave)
                {
                    // If we're doing a singleton/non-batch update, go ahead and take the time to calculate a free spot.

                    var slots = await Dat.ComputeOpenSlots(df);
                    var slot = slots.FirstOrDefault(x => x.Value >= size);

                    if (slot.Key >= 2048)
                    {
                        rawOffset = await WriteToDat(fileData, df, slot.Key);
                    }
                    else
                    {
                        rawOffset = await WriteToDat(fileData, df);
                    }
                }
                else
                {
                    // If we're doing a batch update, or a transaction type update, just write to the end of the file.
                    rawOffset = await WriteToDat(fileData, df);
                }
            }

            var retOffset = ((long)rawOffset) * 8L;
            uint originalOffset = 0;

            // Only update the index files if we actually wrote to the dat.
            if (!doLumina)
            {
                // Update the Index files.

                if (doDatSave)
                {
                    originalOffset = await _index.UpdateDataOffset(retOffset, internalFilePath, true);
                }
                else
                {
                    originalOffset = index.SetDataOffset(internalFilePath, retOffset);
                }

            }

            var longOriginal = ((long)originalOffset) * 8L;
            var fileType = BitConverter.ToInt32(fileData, 4);

            // Update the Mod List file if we actually wrote to the dat.
            if (!doLumina)
            {
                if (mod == null)
                {
                    // Determine if this is an original game file or not.
                    var fileAdditionMod = originalOffset == 0;

                    mod = new Mod()
                    {
                        name = itemName,
                        category = category,
                        datFile = df.GetDataFileName(),
                        source = sourceApplication,
                        fullPath = internalFilePath,
                        data = new Data()
                    };
                    mod.data.modOffset = retOffset;
                    mod.data.originalOffset = (fileAdditionMod ? retOffset : longOriginal);
                    mod.data.modSize = size;
                    mod.data.dataType = fileType;
                    mod.enabled = true;
                    mod.modPack = null;
                    modList.Mods.Add(mod);
                }
                else
                {
                    var fileAdditionMod = originalOffset == 0 || mod.IsCustomFile();
                    if (fileAdditionMod)
                    {
                        mod.data.originalOffset = retOffset;
                    }

                    mod.data.modOffset = retOffset;
                    mod.enabled = true;
                    mod.modPack = null;
                    mod.data.modSize = size;
                    mod.data.dataType = fileType;
                    mod.name = itemName;
                    mod.category = category;
                    mod.source = sourceApplication;
                }
            }

            if (doLumina)
            {
                WriteWithLumina(fileData, luminaOutDir, internalFilePath);
                retOffset = -1;
            }

            if (doDatSave) {
                if (!doLumina)
                {
                    await _modding.SaveModListAsync(modList);
                }

                // Perform metadata expansion if needed.
                var ext = Path.GetExtension(internalFilePath);
                if (ext == ".meta")
                {
                    byte[] metaRaw;
                    if (!doLumina)
                        metaRaw = await GetType2Data(retOffset, df);
                    else
                        metaRaw = ReadWithLumina(fileData);

                    var meta = await ItemMetadata.Deserialize(metaRaw);

                    meta.Validate(internalFilePath);

                    await ItemMetadata.ApplyMetadata(meta);
                }
                else if (ext == ".rgsp")
                {
                    byte[] rgspRaw;
                    if (!doLumina)
                        rgspRaw = await GetType2Data(retOffset, df);
                    else
                        rgspRaw = ReadWithLumina(fileData);

                    // Expand the racial scaling file.
                    await CMP.ApplyRgspFile(rgspRaw);
                }

                XivCache.QueueDependencyUpdate(internalFilePath);
            }

            // Job done.
            return retOffset;
        }

        /// <summary>
        /// Write out the specified SqPack data at the specified location in Lumina/Umbra/Penumbra compatible formats.
        /// </summary>
        /// <param name="data">The modded data.</param>
        /// <param name="outDirectory">The output folder to write to.</param>
        /// <param name="internalPath"></param>
        private void WriteWithLumina(byte[] data, DirectoryInfo outDirectory, string internalPath)
        {
            Debug.WriteLine($"[LUMINA] Export START for {internalPath}");


            var extractedFile = new FileInfo(Path.Combine(outDirectory.FullName, internalPath));
            extractedFile.Directory?.Create();

            var gameData = ReadWithLumina(data);

            if (extractedFile.FullName.EndsWith("mdl"))
            {
                FixupTextoolsMdl(gameData);
            }

            File.WriteAllBytes(extractedFile.FullName, gameData);
        }

        /// <summary>
        /// Read a FFXIV game file from SqPack data using Lumina.
        /// </summary>
        /// <param name="data">SqPack data to read from.</param>
        /// <param name="offset"></param>
        /// <returns>Decompressed deserialized data.</returns>
        private static byte[] ReadWithLumina(byte[] data, long offset = 0)
        {
            using var dataStream = new SqPackStream(new MemoryStream(data));
            var gameFile = dataStream.ReadFile<FileResource>(offset);

            return gameFile.Data;
        }

        /// <summary>
        /// Fix xivModdingFramework MDL quirks.
        /// </summary>
        /// <param name="mdl">The MDL data to be fixed up.</param>
        private static void FixupTextoolsMdl(byte[] mdl)
        {
            // Model file header LOD num
            mdl[64] = 1;

            // Model header LOD num
            var stackSize = BitConverter.ToUInt32(mdl, 4);
            var runtimeBegin = stackSize + 0x44;
            var stringsLengthOffset = runtimeBegin + 4;
            var stringsLength = BitConverter.ToUInt32(mdl, (int)stringsLengthOffset);
            var modelHeaderStart = stringsLengthOffset + stringsLength + 4;
            var modelHeaderLodOffset = 22;
            mdl[modelHeaderStart + modelHeaderLodOffset] = 1;
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
        public static long OffsetCorrection(int datNum, long offset)
        {
            var ret = offset - (16 * datNum);
            return ret;
        }

        /// <summary>
        /// Computes a dictionary listing of all the open space in a given dat file (within the modded dats only).
        /// </summary>
        /// <param name="df"></param>
        /// <returns></returns>
        public static async Task<Dictionary<long, long>> ComputeOpenSlots(XivDataFile df)
        {
            var _dat = new Dat(XivCache.GameInfo.GameDirectory);
            var _modding = new Modding(XivCache.GameInfo.GameDirectory);

            var moddedDats = await _dat.GetModdedDatList(df);

            var slots = new Dictionary<long, long>();
            var modlist = await _modding.GetModListAsync();

            var modsByFile = modlist.Mods.Where(x => !String.IsNullOrWhiteSpace(x.fullPath) && x.datFile == df.GetDataFileName()).GroupBy(x => {
                long offset = x.data.modOffset;
                var rawOffset = offset / 8;
                var datNum = (rawOffset & 0xF) >> 1;
                return (int)datNum;
            });

            foreach(var kv in modsByFile)
            {
                var file = kv.Key;
                long fileOffsetKey = file << 4;

                // Order by their offset, ascending.
                var ordered = kv.OrderBy(x => x.data.modOffset);


                // Scan through each mod, and any time there's a gap, add it to the listing.
                long lastEndPoint = 2048;
                foreach (var mod in ordered) {
                    var fileOffset = (mod.data.modOffset >> 7) << 7;

                    var size = mod.data.modSize;
                    if(size <= 0)
                    {
                        size = await _dat.GetCompressedFileSize(mod.data.modOffset, df);
                    }

                    if(size % 256 != 0)
                    {
                        size += (256 - (size % 256));
                    }

                    var slotSize = fileOffset - lastEndPoint;
                    if (slotSize > 256)
                    {
                        var mergedStart = lastEndPoint | fileOffsetKey;
                        slots.Add(mergedStart, slotSize);
                    }

                    lastEndPoint = fileOffset + size;
                }
            }


            return slots;
        }
    }
}
