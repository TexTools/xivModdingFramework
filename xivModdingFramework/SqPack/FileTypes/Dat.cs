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
using HelixToolkit.SharpDX.Core.Helper;
using SharpDX;
using xivModdingFramework.Cache;
using xivModdingFramework.Exd.Enums;
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
using xivModdingFramework.Textures.FileTypes;

namespace xivModdingFramework.SqPack.FileTypes
{
    /// <summary>
    /// This class contains the methods that deal with the .dat file type
    /// </summary>
    public class Dat
    {
        private const string DatExtension = ".win32.dat";
        private readonly DirectoryInfo _gameDirectory;
        static SemaphoreSlim _lock = new SemaphoreSlim(1);

        public Dat(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
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
                case "NTFS":
                    // 2 ^35 is the maximum addressable size in the Index files. (28 precision bits, left-shifted 7 bits (increments of 128)
                    return 34359738368;
                case "exFAT":
                    return 34359738368;
                case "ext2":
                    return 34359738368;
                case "ext3":
                    return 34359738368;
                case "ext4":
                    return 34359738368;
                case "XFS":
                    return 34359738368;
                case "btrfs":
                    return 34359738368;
                case "ZFS":
                    return 34359738368;
                case "ReiserFS":
                    return 34359738368;
                case "apfs":
                    return 34359738368;
                default:
                    // Unknown HDD Format, default to the basic limit.
                    return 2000000000;
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

            var datPath = Dat.GetDatPath(dataFile, nextDatNumber);

            using (var fs = File.Create(datPath))
            {
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write(MakeSqPackHeader());
                    bw.Write(MakeDatHeader());
                }
            }

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
            var datPath = Dat.GetDatPath(dataFile, datNum);

            for (int i = 0; i < unmoddedList.Count; i++)
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
                        var datFilePath = Dat.GetDatPath(dataFile, i);

                        if (File.Exists(datFilePath))
                        {
                            using (var binaryReader = new BinaryReader(File.OpenRead(datFilePath)))
                            {
                                binaryReader.BaseStream.Seek(24, SeekOrigin.Begin);
                                var one = binaryReader.ReadInt32();
                                var two = binaryReader.ReadInt32();

                                if(one != 1337 || two != 1337)
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
                        var datFilePath = Dat.GetDatPath(dataFile, i);

                        if (File.Exists(datFilePath))
                        {

                            using (var binaryReader = new BinaryReader(File.OpenRead(datFilePath)))
                            {
                                binaryReader.BaseStream.Seek(24, SeekOrigin.Begin);
                                var one = binaryReader.ReadInt32();
                                var two = binaryReader.ReadInt32();

                                // Check the magic numbers
                                if(one == 1337 && two == 1337)
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
                bw.Write(1337);
                bw.Write(1337);
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
        public async Task<byte[]> ReadSqPackType2(string internalPath, bool forceOriginal, ModTransaction tx = null)
        {
            var dataFile = IOUtil.GetDataFileFromPath(internalPath);

            var _index = new Index(_gameDirectory);
            var _modding = new Modding(_gameDirectory);

            IndexFile index = tx == null ? await _index.GetIndexFile(dataFile, false, true) : await tx.GetIndexFile(dataFile);

            if (forceOriginal)
            {
                ModList modlist = tx == null ? await _modding.GetModList() : await tx.GetModList();
                // Checks if the item being imported already exists in the modlist
                var modEntry = modlist.Mods.FirstOrDefault(x => x.fullPath == internalPath);

                // If the file exists in the modlist, get the data from the original data
                if (modEntry != null)
                {
                    return await ReadSqPackType2(modEntry.data.originalOffset, dataFile);
                }
            }

            // If it doesn't exist in the modlist(the item is not modded) or force original is false,
            // grab the data directly from them index file.

            var offset = index.Get8xDataOffset(internalPath);

            if (offset == 0)
            {
                throw new Exception($"Could not find offset for {internalPath}");
            }

            return await ReadSqPackType2(offset, dataFile);
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
        public async Task<byte[]> ReadSqPackType2(long offset, XivDataFile dataFile)
        {
            if (offset <= 0)
            {
                throw new InvalidDataException("Cannot get file data without valid offset.");
            }


            byte[] type2Bytes = null;

            // This formula is used to obtain the dat number in which the offset is located
            var datNum = (int)((offset / 8) & 0x0F) / 2;

            var datPath = Dat.GetDatPath(dataFile, datNum);


            offset = OffsetCorrection(datNum, offset);
            await _lock.WaitAsync();
            try
            {
                await Task.Run(async () =>
                {
                    using (var br = new BinaryReader(File.OpenRead(datPath)))
                    {
                        type2Bytes = await ReadSqPackType2(br, offset);
                    }
                });
            }
            finally
            {
                _lock.Release();
            }
            if (type2Bytes == null)
            {
                return new byte[0];
            }

            return type2Bytes;
        }

        public async Task<byte[]> ReadSqPackType2(byte[] data, long offset = 0)
        {
            using (var ms = new MemoryStream(data))
            {
                using (var br = new BinaryReader(ms))
                {
                    return await ReadSqPackType2(br, offset);
                }
            }
        }
        public async Task<byte[]> ReadSqPackType2(BinaryReader br, long offset = -1)
        {
            if(offset >= 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            } else
            {
                offset = br.BaseStream.Position;
            }

            var headerLength = br.ReadInt32();
            var fileType = br.ReadInt32();
            if(fileType != 2)
            {
                return null;
            }
            var uncompSize = br.ReadInt32();
            var bufferInfoA = br.ReadInt32();
            var bufferInfoB = br.ReadInt32();

            var dataBlockCount = br.ReadInt32();

            var type2Bytes = new List<byte>(uncompSize);
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
        public async Task<long> ImportType2Data(DirectoryInfo importFilePath, string internalPath, string source, IItem referenceItem = null, ModTransaction tx = null)
        {
            return await ImportType2Data(File.ReadAllBytes(importFilePath.FullName), internalPath, source, referenceItem, tx);
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
        public async Task<long> ImportType2Data(byte[] dataToImport,  string internalPath, string source, IItem referenceItem = null, ModTransaction tx = null)
        {

            var newData = (await CompressType2Data(dataToImport));
            var newOffset = await WriteModFile(newData, internalPath, source, referenceItem, tx);

            // This can be -1 after Lumina imports
            if (newOffset == 0)
            {
                throw new Exception("There was an error writing to the dat file. Offset returned was 0.");
            }

            return newOffset;
        }


        /// <summary>
        /// Create compressed type 2 data from uncompressed binary data.
        /// </summary>
        /// <param name="dataToCreate">Bytes to Type 2data</param>
        /// <returns></returns>
        public async Task<byte[]> CompressType2Data(byte[] dataToCreate)
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
        /// Retrieves the uncompressed data for an SQPack type 3 file from the given path.
        /// </summary>
        /// <remarks>
        /// Type 3 files are used for models
        /// </remarks>
        /// <param name="internalPath">The internal file path of the item</param>
        /// <param name="forceOriginal">Flag used to get original game data</param>
        /// <returns>A tuple containing the mesh count, material count, and decompressed data</returns>
        public async Task<byte[]> ReadSqPackType3(string internalPath, bool forceOriginal, ModTransaction tx = null)
        {

            var dataFile = GetDataFileFromPath(internalPath);

            if (forceOriginal)
            {
                var modding = new Modding(_gameDirectory);
                // Checks if the item being imported already exists in the modlist
                Mod modEntry = null;
                if (tx != null)
                {
                    var modList = await tx.GetModList();
                    modEntry = modList.Mods.FirstOrDefault(x => x.fullPath == internalPath);
                }
                else
                {
                    modEntry = await modding.TryGetModEntry(internalPath);
                }

                // If the file exists in the modlist, get the data from the original data
                if (modEntry != null)
                {
                    return await ReadSqPackType3(modEntry.data.originalOffset, dataFile);
                }
            }

            // If it doesn't exist in the modlist(the item is not modded) or force original is false,
            // grab the data directly from them index file.
            var folder = Path.GetDirectoryName(internalPath);
            folder = folder.Replace("\\", "/");
            var file = Path.GetFileName(internalPath);

            long offset = 0;
            if(tx != null)
            {
                offset = (await tx.GetIndexFile(dataFile)).Get8xDataOffset(internalPath);
            } else
            {
                var index = new Index(_gameDirectory);
                offset = await index.GetDataOffset(internalPath);
            }

            if (offset == 0)
            {
                throw new Exception($"Could not find offset for {internalPath}");
            }

            return await ReadSqPackType3(offset, dataFile);
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
        public async Task<byte[]> ReadSqPackType3(long offset, XivDataFile dataFile)
        {
            if (offset <= 0)
            {
                throw new InvalidDataException("Cannot get file data without valid offset.");
            }

            // This formula is used to obtain the dat number in which the offset is located
            var datNum = (int)((offset / 8) & 0x0F) / 2;

            offset = OffsetCorrection(datNum, offset);

            var datPath = Dat.GetDatPath(dataFile, datNum);

            var meshCount = 0;
            var materialCount = 0;
            byte[] result = null;

            var index = 0;
            return await Task.Run(async () =>
            {
                await _lock.WaitAsync();
                try
                {
                    using (var br = new BinaryReader(File.OpenRead(datPath)))
                    {
                        return await ReadSqPackType3(br, offset);
                    }
                }
                finally
                {
                    _lock.Release();
                }
            });
        }

        public async Task<byte[]> ReadSqPackType3(BinaryReader br, long offset = -1)
        {
            if (offset >= 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            }
            else
            {
                offset = br.BaseStream.Position;
            }

            const int baseHeaderLength = 68; // start of file until after "padding"
            var headerLength = br.ReadInt32();
            var fileType = br.ReadInt32();
            var decompressedSize = br.ReadInt32();
            var buffer1 = br.ReadInt32();
            var buffer2 = br.ReadInt32();
            var version = br.ReadInt32();


            var endOfHeader = offset + headerLength;

            // Uncompressed...
            var vertexInfoSize = br.ReadInt32();
            var modelDataSize = br.ReadInt32();
            var vertexBufferSizes = Read3IntBuffer(br);
            var edgeGeometryVertexBufferSizes = Read3IntBuffer(br);
            var indexBufferSizes = Read3IntBuffer(br);

            // Compressed...
            var vertexInfoCompressedSize = br.ReadInt32();
            var modelDataCompressedSize = br.ReadInt32();
            var compressedvertexBufferSizes = Read3IntBuffer(br);
            var compressededgeGeometryVertexBufferSizes = Read3IntBuffer(br);
            var compressedindexBufferSizes = Read3IntBuffer(br);

            // Offsets....
            var vertexInfoOffset = br.ReadInt32();
            var modelDataOffset = br.ReadInt32();
            var vertexBufferOffsets = Read3IntBuffer(br);
            var edgeGeometryVertexBufferOffsets = Read3IntBuffer(br);
            var indexBufferOffsets = Read3IntBuffer(br);

            // Block Indexes....
            var vertexInfoBlockIndex = br.ReadInt16();
            var modelDataBLockIndex = br.ReadInt16();
            var vertexBufferBlockIndexs = Read3IntBuffer(br, true);
            var edgeGeometryVertexBufferBlockIndexs = Read3IntBuffer(br, true);
            var indexBufferBlockIndexs = Read3IntBuffer(br, true);

            // Block Counts....
            var vertexInfoBlockCount = br.ReadInt16();
            var modelDataBlockCount = br.ReadInt16();
            var vertexBufferBlockCounts = Read3IntBuffer(br, true);
            var edgeGeometryVertexBufferBlockCounts = Read3IntBuffer(br, true);
            var indexBufferBlockCounts = Read3IntBuffer(br, true);

            var meshCount = br.ReadUInt16();
            var materialCount = br.ReadUInt16();

            var lodCount = br.ReadByte();
            var flags = br.ReadByte();

            var padding = br.ReadBytes(2);

            var totalBlocks = vertexInfoBlockCount + modelDataBlockCount;
            totalBlocks += vertexBufferBlockCounts.Sum(x => (int)x);
            totalBlocks += edgeGeometryVertexBufferBlockCounts.Sum(x => (int)x);
            totalBlocks += indexBufferBlockCounts.Sum(x => (int)x);

            var blockSizes = new int[totalBlocks];

            for (var i = 0; i < totalBlocks; i++)
            {
                blockSizes[i] = br.ReadUInt16();
            }

            var distanceToEndOfHeader = endOfHeader - br.BaseStream.Position;
            var extraData = br.ReadBytes((int)distanceToEndOfHeader);


            // Read the compressed blocks.
            // These could technically be read as contiguous blocks typically,
            // But it's safer to actually use their offsets and validate them in the process.

            var vertexInfoData = BeginReadCompressedBlocks(br, vertexInfoBlockCount, endOfHeader + vertexInfoOffset);
            var modelInfoData = BeginReadCompressedBlocks(br, modelDataBlockCount, endOfHeader + modelDataOffset);

            const int _VertexSegments = 3;
            var vertexBuffers = new List<Task<byte[]>>[_VertexSegments];
            for (int i = 0; i < _VertexSegments; i++)
            {
                vertexBuffers[i] = BeginReadCompressedBlocks(br, (int)vertexBufferBlockCounts[i], endOfHeader + vertexBufferOffsets[i]);
            }

            var edgeBuffers = new List<Task<byte[]>>[_VertexSegments];
            for (int i = 0; i < _VertexSegments; i++)
            {
                edgeBuffers[i] = BeginReadCompressedBlocks(br, (int)edgeGeometryVertexBufferBlockCounts[i], endOfHeader + edgeGeometryVertexBufferOffsets[i]);
            }

            var indexBuffers = new List<Task<byte[]>>[_VertexSegments];
            for (int i = 0; i < _VertexSegments; i++)
            {
                indexBuffers[i] = BeginReadCompressedBlocks(br, (int)indexBufferBlockCounts[i], endOfHeader + indexBufferOffsets[i]);
            }

            // Reserve space at the start of the result for the header
            var decompressedData = new byte[baseHeaderLength + decompressedSize];
            int decompOffset = baseHeaderLength;

            // Need to mark these as we unzip them.
            var vertexBufferUncompressedOffsets = new uint[_VertexSegments];
            var indexBufferUncompressedOffsets = new uint[_VertexSegments];
            var vertexBufferRealSizes = new uint[_VertexSegments];
            var indexOffsetRealSizes = new uint[_VertexSegments];

            // Vertex and Model Headers
            decompOffset += await CompleteReadCompressedBlocks(vertexInfoData, decompressedData, decompOffset);
            decompOffset += await CompleteReadCompressedBlocks(modelInfoData, decompressedData, decompOffset);

            for (int i = 0; i < _VertexSegments; i++)
            {
                // Geometry data in LoD order.
                // Mark the real uncompressed offsets and sizes on the way through.
                vertexBufferUncompressedOffsets[i] = (uint)decompOffset - (uint)baseHeaderLength;
                vertexBufferRealSizes[i] = (uint)await CompleteReadCompressedBlocks(vertexBuffers[i], decompressedData, decompOffset);
                decompOffset += (int)vertexBufferRealSizes[i];

                decompOffset += await CompleteReadCompressedBlocks(edgeBuffers[i], decompressedData, decompOffset);

                indexBufferUncompressedOffsets[i] = (uint)decompOffset - (uint)baseHeaderLength;
                indexOffsetRealSizes[i] = (uint)await CompleteReadCompressedBlocks(indexBuffers[i], decompressedData, decompOffset);
                decompOffset += (int)indexOffsetRealSizes[i];
            }

            var header = new List<byte>(baseHeaderLength);

            // Generated header for live/uncompressed MDL files.
            header.AddRange(BitConverter.GetBytes(version));
            header.AddRange(BitConverter.GetBytes(vertexInfoSize));
            header.AddRange(BitConverter.GetBytes(modelDataSize));
            header.AddRange(BitConverter.GetBytes((ushort)meshCount));
            header.AddRange(BitConverter.GetBytes((ushort)materialCount));

            Write3IntBuffer(header, vertexBufferUncompressedOffsets);
            Write3IntBuffer(header, indexBufferUncompressedOffsets);
            Write3IntBuffer(header, vertexBufferRealSizes);
            Write3IntBuffer(header, indexOffsetRealSizes);

            header.Add(lodCount);
            header.Add(flags);
            header.AddRange(padding);

            // Copy the header over the reserved space at the start of decompressedData
            header.CopyTo(decompressedData, 0);

            return decompressedData;
        }

        public static void Write3IntBuffer(List<byte> bufferTarget, uint[] dataToAdd)
        {
            bufferTarget.AddRange(BitConverter.GetBytes(dataToAdd[0]));
            bufferTarget.AddRange(BitConverter.GetBytes(dataToAdd[1]));
            bufferTarget.AddRange(BitConverter.GetBytes(dataToAdd[2]));
        }
        public static uint[] Read3IntBuffer(BinaryReader br, bool shortOnly = false)
        {
            uint[] data = new uint[3];
            if (shortOnly)
            {
                data[0] = br.ReadUInt16();
                data[1] = br.ReadUInt16();
                data[2] = br.ReadUInt16();
            }
            else
            {
                data[0] = br.ReadUInt32();
                data[1] = br.ReadUInt32();
                data[2] = br.ReadUInt32();
            }
            return data;
        }
        public static uint[] Read3IntBuffer(byte[] body, int offset)
        {
            uint[] data = new uint[3];
            data[0] = BitConverter.ToUInt32(body, offset);
            data[1] = BitConverter.ToUInt32(body, offset + 4);
            data[2] = BitConverter.ToUInt32(body, offset + 8);
            return data;
        }

        // Begins decompressing a sequence of blocks in parallel
        // Returns a list of tasks that should be passed to CompleteReadCompressedBlocks()
        public List<Task<byte[]>> BeginReadCompressedBlocks(BinaryReader br, int blockCount, long offset = -1)
        {
            if(blockCount == 0)
            {
                return new();
            }

            if (offset > 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            }

            var tasks = new List<Task<byte[]>>();

            for (int i = 0; i < blockCount; i++)
            {
                byte[] data;

                var start = br.BaseStream.Position;

                // Some variety of magic numbers presumably?
                var sixTeen = br.ReadByte();

                // This is a shitty catch for old improperly spaced blocks generated by Endwalker and earlier TexTools.
                while(sixTeen != 16 && sixTeen == 0)
                {
                    sixTeen = br.ReadByte();
                }
                var zeros = br.ReadBytes(3);

                var zero = br.ReadInt32();

                if (sixTeen != 16 || zero != 0 || zeros.Any(x => x != 0))
                {
                    throw new Exception("Unable to locate valid compressed block header.");
                }

                // Relevant info.
                var partCompSize = br.ReadInt32();
                var partDecompSize = br.ReadInt32();

                Task<byte[]> task;

                void readBlockPadding()
                {
                    var end = br.BaseStream.Position;
                    var length = end - start;
                    var targetLength = Pad((int)length, 128);
                    var remaining = targetLength - length;

                    var paddingData = br.ReadBytes((int)remaining);

                    var sixTeenIndex = Array.IndexOf(paddingData, (byte)16);

                    // Ugh.  This is an old broken TexTools import that has improper block spacing.
                    // We have to rewind the stream to the start of the next block.
                    if(sixTeenIndex != -1)
                    {
                        var rewind = paddingData.Length - sixTeenIndex;
                        br.BaseStream.Position = br.BaseStream.Position - rewind;
                    } else if (paddingData.Any(x => x != 0))
                    {
                        throw new Exception("Unexpected real data in compressed data block padding section.");
                    }
                }

                if (partCompSize == 32000)
                {
                    data = br.ReadBytes(partDecompSize);
                    readBlockPadding();
                    var completedTask = new TaskCompletionSource<byte[]>();
                    completedTask.SetResult(data);
                    task = completedTask.Task;
                }
                else
                {
                    data = br.ReadBytes(partCompSize);
                    readBlockPadding();

                    // Not 100% sure this really needs to be shipped as Task.Run,
                    // but Task.Run should ensure that we actually get scheduled on the thread pool
                    // for potential new threads.
                    task = Task.Run(async () =>
                    {
                        return await IOUtil.Decompressor(data, partDecompSize);
                    });
                }

                tasks.Add(task);
            }

            return tasks;
        }

        // Completes all provided tasks from BeginReadCompressedBlocks and writes them sequentially in to destBuffer
        // Returns the number of bytes written in to destBuffer
        public async Task<int> CompleteReadCompressedBlocks(List<Task<byte[]>> tasks, byte[] destBuffer, int destOffset)
        {
            int currentOffset = destOffset;
            foreach (var task in tasks)
            {
                await task;
                var result = task.Result;
                result.CopyTo(destBuffer, currentOffset);
                currentOffset += result.Length;
            }

            return currentOffset - destOffset;
        }

        public async Task<byte[]> ReadCompressedBlock(BinaryReader br, long offset = -1)
        {
            if (offset > 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            }

            var start = br.BaseStream.Position;


            byte[] data;

            // Some variety of magic numbers presumably?
            var sixTeen = br.ReadByte();

            // This is a shitty catch for old improperly spaced blocks generated by Endwalker and earlier TexTools.
            while (sixTeen != 16 && sixTeen == 0)
            {
                sixTeen = br.ReadByte();
            }
            var zeros = br.ReadBytes(3);

            var zero = br.ReadInt32();

            if (sixTeen != 16 || zero != 0 || zeros.Any(x => x != 0))
            {
                throw new Exception("Unable to locate valid compressed block header.");
            }

            // Relevant info.
            var partCompSize = br.ReadInt32();
            var partDecompSize = br.ReadInt32();

            if (partCompSize == 32000)
            {
                data = br.ReadBytes(partDecompSize);
            }
            else
            {
                data = await IOUtil.Decompressor(br.ReadBytes(partCompSize), partDecompSize);
            }

            var end = br.BaseStream.Position;
            var length = end - start;

            var target = Pad((int)length, 128);
            var remaining = target - length;

            var paddingData = br.ReadBytes((int)remaining);

            var sixTeenIndex = Array.IndexOf(paddingData, (byte)16);

            // Ugh.  This is an old broken TexTools import that has improper block spacing.
            // We have to rewind the stream to the start of the next block.
            if (sixTeenIndex != -1)
            {
                var rewind = paddingData.Length - sixTeenIndex;
                br.BaseStream.Position = br.BaseStream.Position - rewind;
            }
            else if (paddingData.Any(x => x != 0))
            {
                throw new Exception("Unexpected real data in compressed data block padding section.");
            }

            return data;
        }

        public async Task<byte[]> ReadCompressedBlocks(BinaryReader br, int blockCount, long offset = -1)
        {
            if (blockCount == 0)
            {
                return new byte[0];
            }

            if (offset > 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            }

            var ret = (IEnumerable<byte>)new List<byte>();
            for (int i = 0; i < blockCount; i++)
            {
                var data = await ReadCompressedBlock(br);
                ret = ret.Concat(data);
            }
            return ret.ToArray();
        }
        public async Task<uint> GetReportedType4UncompressedSize(XivDataFile df, long offsetWithDatNumber)
        {
            // This formula is used to obtain the dat number in which the offset is located
            var datNum = (int)((offsetWithDatNumber / 8) & 0x0F) / 2;

            var offset = OffsetCorrection(datNum, offsetWithDatNumber);

            var datPath = Dat.GetDatPath(df, datNum);

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

        /// <summary>
        /// WARNING: DOES NOT USE/RESPECT TRANSACTIONS
        /// This is a very specific fixer-function designed to handle a very specific error caused by old TexTools builds that would
        /// generate invalid file sizes.
        /// </summary>
        /// <param name="df"></param>
        /// <param name="offsetWithDatNumber"></param>
        /// <param name="correctedFileSize"></param>
        /// <returns></returns>
        public async Task UpdateType4UncompressedSize(XivDataFile df, long offsetWithDatNumber, uint correctedFileSize)
        {
            // This formula is used to obtain the dat number in which the offset is located
            var datNum = (int)((offsetWithDatNumber / 8) & 0x0F) / 2;

            var offset = OffsetCorrection(datNum, offsetWithDatNumber);

            var datPath = Dat.GetDatPath(df, datNum);

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
        public async Task<XivTex> GetTexFromDat(string internalPath, bool forceOriginal, ModTransaction tx = null)
        {

            var dataFile = GetDataFileFromPath(internalPath);

            if (forceOriginal)
            {
                // Checks if the item being imported already exists in the modlist
                Mod modEntry = null;
                if(tx != null)
                {
                    modEntry = (await tx.GetModList()).Mods.FirstOrDefault(x => x.fullPath == internalPath);
                } else
                {
                    var modding = new Modding(_gameDirectory);
                    modEntry = await modding.TryGetModEntry(internalPath);
                }

                // If the file exists in the modlist, get the data from the original data
                if (modEntry != null)
                {
                    return await GetTexFromDat(modEntry.data.originalOffset, dataFile);
                }
            }

            // If it doesn't exist in the modlist(the item is not modded) or force original is false,
            // grab the data directly from them index file.

            var folder = Path.GetDirectoryName(internalPath);
            folder = folder.Replace("\\", "/");
            var file = Path.GetFileName(internalPath);

            long offset = 0;
            if (tx != null)
            {
                offset = (await tx.GetIndexFile(dataFile)).Get8xDataOffset(internalPath);
            }
            else
            {
                var index = new Index(_gameDirectory);
                offset = await index.GetDataOffset(internalPath);
            }

            if (offset == 0)
            {
                throw new Exception($"Could not find offset for {internalPath}");
            }

            return await GetTexFromDat(offset, dataFile);
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
        public async Task<XivTex> GetTexFromDat(long offset, XivDataFile dataFile)
        {
            if (offset <= 0)
            {
                throw new InvalidDataException("Cannot get file size data without valid offset.");
            }

            var xivTex = new XivTex();

            byte[] decompressedData = null;

            // This formula is used to obtain the dat number in which the offset is located
            var datNum = (int)((offset / 8) & 0x0F) / 2;

            await _lock.WaitAsync();

            try
            {
                offset = OffsetCorrection(datNum, offset);

                var datPath = Dat.GetDatPath(dataFile, datNum);

                await Task.Run(async () =>
                {
                    using (var br = new BinaryReader(File.OpenRead(datPath)))
                    {
                        br.BaseStream.Seek(offset, SeekOrigin.Begin);


                        // Type 4 data is pretty simple.

                        // Standard SQPack header.
                        var headerLength = br.ReadInt32();
                        var fileType = br.ReadInt32();
                        var uncompressedFileSize = br.ReadInt32();
                        var ikd1 = br.ReadInt32();
                        var ikd2 = br.ReadInt32();

                        // Count of mipmaps.
                        xivTex.MipMapCount = br.ReadInt32();

                        var endOfHeader = offset + headerLength;
                        var mipMapInfoOffset = offset + 24;

                        br.BaseStream.Seek(endOfHeader + 4, SeekOrigin.Begin);

                        // Tex File Header
                        var format = br.ReadInt32();
                        xivTex.TextureFormat = TextureTypeDictionary[format];
                        xivTex.Width = br.ReadInt16();
                        xivTex.Height = br.ReadInt16();
                        xivTex.Layers = br.ReadInt16();
                        var imageCount2 = br.ReadInt16();

                        decompressedData = new byte[uncompressedFileSize];
                        int decompOffset = 0;

                        var mipData = new List<Task<byte[]>>[xivTex.MipMapCount];

                        // Each MipMap has a basic header of information, and a set of compressed data blocks of info.
                        for (int i = 0; i < xivTex.MipMapCount; i++)
                        {
                            const int _MipMapHeaderSize = 20;
                            br.BaseStream.Seek(mipMapInfoOffset + (_MipMapHeaderSize * i), SeekOrigin.Begin);

                            var offsetFromHeaderEnd = br.ReadInt32();
                            var mipMapLength = br.ReadInt32();
                            var mipMapSize = br.ReadInt32();
                            var mipMapStart = br.ReadInt32();
                            var mipMapParts = br.ReadInt32();

                            var mipMapPartOffset = endOfHeader + offsetFromHeaderEnd;

                            br.BaseStream.Seek(mipMapPartOffset, SeekOrigin.Begin);

                            mipData[i] = BeginReadCompressedBlocks(br, mipMapParts);
                        }

                        for (int i = 0; i < xivTex.MipMapCount; i++)
                        {
                            decompOffset += await CompleteReadCompressedBlocks(mipData[i], decompressedData, decompOffset);
                        }
                    }
                });
                xivTex.TexData = decompressedData ?? new byte[0];
            }
            finally
            {
                _lock.Release();
            }

            return xivTex;
        }

        /// <summary>
        /// Reads and decompresses an SQPack type 4 file from the given data stream.
        /// </summary>
        /// <param name="br"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public async Task<byte[]> ReadSqPackType4(BinaryReader br, long offset = -1)
        {
            if (offset >= 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            } else
            {
                offset = br.BaseStream.Position;
            }

            // Type 4 data is pretty simple.

            // Standard SQPack header.
            var headerLength = br.ReadInt32();
            var fileType = br.ReadInt32();
            var uncompressedFileSize = br.ReadInt32();
            var ikd1 = br.ReadInt32();
            var ikd2 = br.ReadInt32();

            // Count of mipmaps.
            var mipCount = br.ReadInt32();

            var endOfHeader = offset + headerLength;
            var mipMapInfoOffset = offset + 24;


            // Tex File Header
            br.BaseStream.Seek(endOfHeader, SeekOrigin.Begin);
            var texHeader = br.ReadBytes((int)Tex._TexHeaderSize);

            // Decompress Mipmap blocks ...
            var decompressedData = new byte[uncompressedFileSize];
            int decompOffset = 0;

            var mipData = new List<Task<byte[]>>[mipCount];

            // Each MipMap has a basic header of information, and a set of compressed data blocks of info.
            for (int i = 0; i < mipCount; i++)
            {
                const int _MipMapHeaderSize = 20;
                br.BaseStream.Seek(mipMapInfoOffset + (_MipMapHeaderSize * i), SeekOrigin.Begin);

                var offsetFromHeaderEnd = br.ReadInt32();
                var mipMapLength = br.ReadInt32();
                var mipMapSize = br.ReadInt32();
                var mipMapStart = br.ReadInt32();
                var mipMapParts = br.ReadInt32();

                var mipMapPartOffset = endOfHeader + offsetFromHeaderEnd;

                br.BaseStream.Seek(mipMapPartOffset, SeekOrigin.Begin);

                mipData[i] = BeginReadCompressedBlocks(br, mipMapParts);
            }

            for (int i = 0; i < mipCount; i++)
            {
                decompOffset += await CompleteReadCompressedBlocks(mipData[i], decompressedData, decompOffset);
            }

            byte[] finalbytes = new byte[texHeader.Length + decompressedData.Length];
            Array.Copy(texHeader, 0, finalbytes, 0, texHeader.Length);
            Array.Copy(decompressedData, 0, finalbytes, texHeader.Length, decompressedData.Length);

            return finalbytes;
        }



        /// <summary>
        /// Retrieves the raw uncompressed/De-SQPacked bytes for a given file.
        /// </summary>
        /// <param name="internalPath"></param>
        /// <param name="forceOriginal"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        public async Task<byte[]> GetUncompressedData(string internalPath, bool forceOriginal, ModTransaction tx = null)
        {
            if (tx == null)
            {
                // Use simple readonly tx if we don't have one.
                tx = ModTransaction.BeginTransaction(true);
            }
            var offset8x = await tx.Get8xDataOffset(internalPath);
            var df = IOUtil.GetDataFileFromPath(internalPath);

            var parts = Dat.Offset8xToParts(offset8x);
            var datPath = GetDatPath(df, parts.DatNum);

            int type = -1;
            using (var br = new BinaryReader(File.OpenRead(datPath)))
            {
                return await GetUncompressedData(br, parts.Offset);
            }
        }

        /// <summary>
        /// Decompresses (De-SqPacks) a given block of data.
        /// </summary>
        /// <param name="sqpackData"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Task<byte[]> GetUncompressedData(byte[] sqpackData)
        {
            using (var ms = new MemoryStream(sqpackData))
            {
                using (var br = new BinaryReader(ms))
                {
                    return await GetUncompressedData(br);
                }
            }
        }

        /// <summary>
        /// Reads an SQPack file from the given data stream.
        /// </summary>
        /// <param name="br"></param>
        /// <returns></returns>
        public async Task<byte[]> GetUncompressedData(BinaryReader br, long offset = -1)
        {
            if(offset >= 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            }
            else
            {
                offset = br.BaseStream.Position;
            }
            int type = -1;

            br.BaseStream.Seek(offset + 4, SeekOrigin.Begin);
            type = br.ReadInt32();

            br.BaseStream.Seek(offset, SeekOrigin.Begin);
            if (type == 2)
            {
                return await ReadSqPackType2(br, offset);
            }
            else if (type == 3)
            {
                return await ReadSqPackType3(br, offset);
            }
            else if (type == 4)
            {
                return await ReadSqPackType4(br, offset);
            }
            throw new NotImplementedException("Unable to read invalid SQPack File Type.");
        }

        /// <summary>
        /// Pads a given byte list to the target padding interval with empty bytes.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        /// <param name="paddingTarget"></param>
        /// <param name="forcePadding"></param>
        public static void Pad<T>(List<T> data, int paddingTarget, bool forcePadding = false)
        {
            var pad = paddingTarget - (data.Count % paddingTarget);
            if(pad == paddingTarget && !forcePadding)
            {
                return;
            }
            data.AddRange(new T[pad]);
        }

        /// <summary>
        /// Pads a given byte array to the target padding interval with empty bytes.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        /// <param name="paddingTarget"></param>
        /// <param name="forcePadding"></param>
        public static T[] PadArray<T>(T[] data, int paddingTarget, bool forcePadding = false)
        {
            var pad = paddingTarget - (data.Length % paddingTarget);
            if (pad == paddingTarget && !forcePadding)
            {
                return data;
            }
            var res = new T[data.Length + pad];
            data.CopyTo(res, 0);
            return res;
        }


        /// <summary>
        /// Pads a given int length to the next padding interval.
        /// </summary>
        /// <param name="size"></param>
        /// <param name="paddingTarget"></param>
        /// <returns></returns>
        public static int Pad(int size, int paddingTarget, bool forcePadding = false)
        {
            var pad = paddingTarget - (size % paddingTarget);
            if(pad == paddingTarget && !forcePadding)
            {
                return size;
            }
            return size + pad;
        }

        /// <summary>
        /// Pads a given int length to the next padding interval.
        /// </summary>
        /// <param name="size"></param>
        /// <param name="paddingTarget"></param>
        /// <returns></returns>
        public static long Pad(long size, long paddingTarget, bool forcePadding = false)
        {
            var pad = paddingTarget - (size % paddingTarget);
            if (pad == paddingTarget && !forcePadding)
            {
                return size;
            }
            return size + pad;
        }

        /// <summary>
        /// Compresses a single data block, returning the singular compressed byte array.
        /// For blocks larger than 16,000, use CompressData() instead.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static async Task<byte[]> CompressSmallData(byte[] data)
        {
            if (data.Length > 16000)
            {
                throw new Exception("CompressSmallData() data is too large.");
            }

            // Vertex Info Compression
            var compressedData = await IOUtil.Compressor(data);

            var pad = 128 - ((compressedData.Length + 16) % 128);
            if (pad == 128)
            {
                pad = 0;
            }

            var paddedSize = 16 + compressedData.Length + pad;

            // Pre-allocate the array.
            List<byte> result = new List<byte>(paddedSize);
            result.AddRange(BitConverter.GetBytes(16));
            result.AddRange(BitConverter.GetBytes(0));
            result.AddRange(BitConverter.GetBytes(compressedData.Length));
            result.AddRange(BitConverter.GetBytes(data.Length));
            result.AddRange(compressedData);
            result.AddRange(new byte[pad]);

            return result.ToArray();
        }

        /// <summary>
        /// Compresses data, returning the compressed byte arrays in parts.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static async Task<List<byte[]>> CompressData(List<byte> data)
        {
            var partCount = (int)Math.Ceiling(data.Count / 16000f);
            var partSizes = new List<int>(partCount);
            var remainingDataSize = data.Count;

            for (var i = 0; i < partCount; i++)
            {
                if (remainingDataSize >= 16000)
                {
                    partSizes.Add(16000);
                    remainingDataSize -= 16000;
                }
                else
                {
                    partSizes.Add(remainingDataSize);
                }
            }

            var parts = new List<byte[]>();

            var compressionTasks = new List<Task<byte[]>>();
            for (var i = 0; i < partCount; i++)
            {
                // Hand the compression task to the thread scheduler.
                var start = i * 16000;
                var size = partSizes[i];
                compressionTasks.Add(Task.Run(async () => {
                    return await CompressSmallData(data.GetRange(start, size).ToArray());
                }));
            }
            await Task.WhenAll(compressionTasks);

            foreach(var task in compressionTasks)
            {
                parts.Add(task.Result);
            }

            return parts;
        }

        public async Task<int> GetCompressedFileSize(string path, ModTransaction tx)
        {
            var offset8x = await tx.Get8xDataOffset(path);
            var df = IOUtil.GetDataFileFromPath(path);
            return await GetCompressedFileSize(offset8x, df);
        }

        public async Task<int> GetCompressedFileSize(long offset, XivDataFile dataFile)
        {
            if (offset <= 0)
            {
                throw new InvalidDataException("Cannot get file size data without valid offset.");
            }


            var xivTex = new XivTex();

            // This formula is used to obtain the dat number in which the offset is located
            var offsetParts = Offset8xToParts(offset);
            offset = offsetParts.Offset;

            await _lock.WaitAsync();

            try
            {
                var datPath = Dat.GetDatPath(dataFile, offsetParts.DatNum);

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
                                // 11 Segments.  Vertex Info, Model Data, [Vertex Data x3], [Edge Data x3], [Index Data x3]
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

        public static string ReadNullTerminatedString(BinaryReader br)
        {
            var data = new List<byte>();
            var b = br.ReadByte();
            while(b != 0)
            {
                data.Add(b);
                b = br.ReadByte();
            }
            return System.Text.Encoding.UTF8.GetString(data.ToArray());
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

            var datPath = Dat.GetDatPath(dataFile, datNum);

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

        public static string GetDatPath(XivDataFile dataFile, int datNumber)
        {
            var datPath = $"{XivCache.GameInfo.GameDirectory}/{dataFile.GetDataFileName()}{Dat.DatExtension}{datNumber}";
            return datPath;
        }

        public async Task<byte[]> GetCompressedData(string path, ModTransaction tx)
        {
            var dataSize = await GetCompressedFileSize(path, tx);

            var offset8x = await tx.Get8xDataOffset(path);
            var df = IOUtil.GetDataFileFromPath(path);
            var offsetParts = Offset8xToParts(offset8x);
            var datPath = GetDatPath(df, offsetParts.DatNum);

            using (var br = new BinaryReader(File.OpenRead(datPath)))
            {
                try
                {
                    br.BaseStream.Seek(offsetParts.Offset, SeekOrigin.Begin);
                    return br.ReadBytes(dataSize);
                }
                catch
                {
                    return null;
                }
            }
        }

        public byte[] GetCompressedData(long offset8xWithDatEmbed, XivDataFile dataFile, int dataSize)
        {
            var offsetParts = Offset8xToParts(offset8xWithDatEmbed);
            var datPath = GetDatPath(dataFile, offsetParts.DatNum);

            using (var br = new BinaryReader(File.OpenRead(datPath)))
            {
                try
                {
                    br.BaseStream.Seek(offsetParts.Offset, SeekOrigin.Begin);

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
        /// <param name="uncompressedLength">Length of the uncompressed texture file.</param>
        /// <param name="newMipCount">The number of mipmaps the DDS texture to be imported contains.</param>
        /// <param name="newWidth">The width of the DDS texture to be imported.</param>
        /// <param name="newHeight">The height of the DDS texture to be imported.</param>
        /// <returns>The created header data.</returns>
        public byte[] MakeType4DatHeader(XivTexFormat format, List<List<byte[]>> ddsParts, int uncompressedLength, int newWidth, int newHeight)
        {
            var headerData = new List<byte>();

            var mipCount = ddsParts.Count;
            var totalParts = ddsParts.Sum(x => x.Count);
            var headerSize = 24 + (mipCount * 20) + (totalParts * 2);
            var headerPadding = 128 - (headerSize % 128);

            headerData.AddRange(BitConverter.GetBytes(headerSize + headerPadding));
            headerData.AddRange(BitConverter.GetBytes(4));
            headerData.AddRange(BitConverter.GetBytes(uncompressedLength + 80));
            headerData.AddRange(BitConverter.GetBytes(0)); // Buffer info 0 apparently works fine?
            headerData.AddRange(BitConverter.GetBytes(0)); // Buffer info 0 apparently works fine?
            headerData.AddRange(BitConverter.GetBytes(mipCount));


            var dataBlockOffset = 0;
            var mipCompressedOffset = 80;
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

            for (var i = 0; i < mipCount; i++)
            {
                // Compressed Offset (Starting after sqpack header)
                headerData.AddRange(BitConverter.GetBytes(mipCompressedOffset));

                // Compressed Size
                var compressedSize = ddsParts[i].Sum(x => x.Length);
                headerData.AddRange(BitConverter.GetBytes(compressedSize));
                
                // Uncompressed Size
                var uncompressedSize = uncompMipSize > 16 ? uncompMipSize : 16;
                headerData.AddRange(BitConverter.GetBytes(uncompressedSize));

                // Data Block Offset
                headerData.AddRange(BitConverter.GetBytes(dataBlockOffset));

                // Data Block Size
                headerData.AddRange(BitConverter.GetBytes(ddsParts[i].Count));


                // Every MipMap is 1/4th the net size, so this is a easy way to recalculate it.
                uncompMipSize = uncompMipSize / 4;

                dataBlockOffset = dataBlockOffset + ddsParts[i].Count;
                mipCompressedOffset = mipCompressedOffset + compressedSize;
            }

            // This seems to be (another) listing of part sizes?
            foreach (var mip in ddsParts)
            {
                foreach(var part in mip)
                {
                    headerData.AddRange(BitConverter.GetBytes((ushort) part.Length));
                }
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

                var datPath = Dat.GetDatPath(dataFile, i);


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
        /// Copies a file from a given offset to a new path in the game files.
        /// </summary>
        /// <param name="targetPath"></param>
        /// <param name="source"></param>
        /// <returns></returns>
        public async Task<long> CopyFile(string sourcePath, string targetPath, string source = "Unknown", bool overwrite = false, IItem referenceItem = null, ModTransaction tx = null)
        {
            var ownTx = false;
            if(tx == null)
            {
                ownTx = true;
                tx = ModTransaction.BeginTransaction();
            }
            try
            {
                var exists = await tx.FileExists(targetPath);
                if (exists && !overwrite)
                {
                    return await tx.Get8xDataOffset(targetPath);
                }

                var data = await GetCompressedData(sourcePath, tx);


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

                var newOffset = await WriteModFile(data, targetPath, source, referenceItem, tx);
                if (ownTx)
                {
                    await ModTransaction.CommitTransaction(tx);
                }
                return newOffset;
            }
            catch
            {
                if(ownTx)
                {
                    ModTransaction.CancelTransaction(tx);
                }
                throw;
            }
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

                var datPath = Dat.GetDatPath(dataFile, datNum);

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
        /// <param name="retainModpack"></param>
        /// <returns></returns>
        public async Task<long> WriteModFile(byte[] fileData, string internalFilePath, string sourceApplication, IItem referenceItem = null, ModTransaction tx = null)
        {

            var _modding = new Modding(XivCache.GameInfo.GameDirectory);
            var _index = new Index(XivCache.GameInfo.GameDirectory);
            var df = IOUtil.GetDataFileFromPath(internalFilePath);

            var doDatSave = tx == null;


            if (XivCache.GameInfo.UseLumina)
            {
                return await DoLuminaWrite(fileData, internalFilePath, doDatSave);
            }

            // Open a transaction if we don't have one.
            if(tx == null)
            {
                tx = ModTransaction.BeginTransaction();
            }

            var modList = await tx.GetModList();
            var index = await tx.GetIndexFile(IOUtil.GetDataFileFromPath(internalFilePath));

            var mod = modList.Mods.FirstOrDefault(x => x.fullPath == internalFilePath);

            // Resolve Item to attach to.
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
            long retOffset = -1;

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
                // If we're part of a larger transaction, just write to the end of the file.
                rawOffset = await WriteToDat(fileData, df);
            }

            retOffset = ((long)rawOffset) * 8L;
            uint originalOffset = 0;

            // Update the Index files.
            originalOffset = index.SetDataOffset(internalFilePath, retOffset);

            var longOriginal = ((long)originalOffset) * 8L;
            var fileType = BitConverter.ToInt32(fileData, 4);

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

                // If we don't have a specified modpack, but this file is already modded, retain its modpack association.
                mod.modPack = mod.IsInternal() ? null : tx.ModPack;
                modList.Mods.Add(mod);
            }
            else
            {
                var mPack = tx.ModPack == null ? mod.modPack : tx.ModPack;
                var fileAdditionMod = originalOffset == 0 || mod.IsCustomFile();
                if (fileAdditionMod)
                {
                    mod.data.originalOffset = retOffset;
                }
                mod.data.modOffset = retOffset;
                mod.enabled = true;
                mod.modPack = mod.IsInternal() ? null : mPack;
                mod.data.modSize = size;
                mod.data.dataType = fileType;
                mod.name = itemName;
                mod.category = category;
                mod.source = sourceApplication;
            }

            if (doDatSave)
            {
                // Commit the transaction if we're doing a single file save.
                await ExpandMetadata(fileData, internalFilePath, tx);
                await ModTransaction.CommitTransaction(tx);
            }
            XivCache.QueueDependencyUpdate(internalFilePath);

            // Job done.
            return retOffset;
        }

        /// <summary>
        /// Expands the given .meta or .rgsp file.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="internalPath"></param>
        /// <returns></returns>
        private async Task ExpandMetadata(byte[] data, string internalPath, ModTransaction tx = null)
        {
            // Perform metadata expansion if needed.
            var ext = Path.GetExtension(internalPath);

            if (ext == ".meta")
            {
                byte[] metaRaw;
                metaRaw = (await ReadSqPackType2(data)).ToArray();

                var meta = await ItemMetadata.Deserialize(metaRaw);
                meta.Validate(internalPath);

                await ItemMetadata.ApplyMetadata(meta, tx);
            }
            else if (ext == ".rgsp")
            {
                byte[] rgspRaw;
                rgspRaw = (await ReadSqPackType2(data)).ToArray();
                // Expand the racial scaling file.
                await CMP.ApplyRgspFile(rgspRaw, tx);
            }

        }

        private async Task<long> DoLuminaWrite(byte[] fileData, string internalFilePath, bool expandMetadata)
        {
            var doLumina = XivCache.GameInfo.UseLumina;
            var luminaOutDir = XivCache.GameInfo.LuminaDirectory;
            if (doLumina && luminaOutDir == null)
            {
                throw new InvalidDataException("Cannot perform Lumina imports without valid Lumina directory.");
            }

            if (doLumina && (luminaOutDir == null || !luminaOutDir.Exists))
                throw new ArgumentException("No valid lumina output path was specified.", nameof(luminaOutDir));

            DecompressAndWrite(fileData, luminaOutDir, internalFilePath);
            if (expandMetadata)
            {
                await ExpandMetadata(fileData, internalFilePath);
            }

            return -1;
        }

        /// <summary>
        /// Write the uncompressed SQPack file to the given file directory.
        /// </summary>
        /// <param name="data">The modded data.</param>
        /// <param name="outDirectory">The output folder to write to.</param>
        /// <param name="internalPath"></param>
        private async Task DecompressAndWrite(byte[] data, DirectoryInfo outDirectory, string internalPath)
        {
            var extractedFile = new FileInfo(Path.Combine(outDirectory.FullName, internalPath));
            extractedFile.Directory?.Create();
            var uncompressedData = await GetUncompressedData(data);
            File.WriteAllBytes(extractedFile.FullName, uncompressedData);
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
            {16704, XivTexFormat.D16 },
            {25136, XivTexFormat.BC5 },
            {25650, XivTexFormat.BC7 }
        };


        /// <summary>
        /// Takes an 8x Dat Embeded offset, returning the constituent parts.
        /// </summary>
        /// <param name="offset8xWithDatNumEmbed"></param>
        /// <returns></returns>
        public static (long Offset, int DatNum) Offset8xToParts(long offset8xWithDatNumEmbed)
        {
            var datNum = (int)((offset8xWithDatNumEmbed / 8) & 0x0F) / 2;
            var offset = OffsetCorrection(datNum, offset8xWithDatNumEmbed);
            return (offset, datNum);
        }

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
            var modlist = await _modding.GetModList();

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
