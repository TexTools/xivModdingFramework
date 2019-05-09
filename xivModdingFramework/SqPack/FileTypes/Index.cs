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
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;

namespace xivModdingFramework.SqPack.FileTypes
{
    /// <summary>
    /// This class contains the methods that deal with the .index file type 
    /// </summary>
    public class Index
    {
        private const string IndexExtension = ".win32.index";
        private const string Index2Extension = ".win32.index2";
        private readonly DirectoryInfo _gameDirectory;
        private static SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

        public Index(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
        }

        /// <summary>
        /// Update the dat count within the index files.
        /// </summary>
        /// <param name="dataFile">The data file to update the index for.</param>
        /// <param name="datNum">The dat number to update to.</param>
        public void UpdateIndexDatCount(XivDataFile dataFile, int datNum)
        {
            var datCount = (byte)(datNum + 1);

            var indexPaths = new[]
            {
                Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}"),
                Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{Index2Extension}")
            };

            foreach (var indexPath in indexPaths)
            {
                using (var bw = new BinaryWriter(File.OpenWrite(indexPath)))
                {
                    bw.BaseStream.Seek(1104, SeekOrigin.Begin);
                    bw.Write(datCount);
                }
            }
        }

        /// <summary>
        /// Gets the dat count within the index files.
        /// </summary>
        /// <param name="dataFile">The data file to update the index for.</param>
        public (int Index1, int Index2) GetIndexDatCount(XivDataFile dataFile)
        {
            int index1 = 0, index2 = 0;

            var indexPaths = new[]
            {
                Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}"),
                Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{Index2Extension}")
            };

            for (var i = 0; i < indexPaths.Length; i++)
            {
                using (var br = new BinaryReader(File.OpenRead(indexPaths[i])))
                {
                    br.BaseStream.Seek(1104, SeekOrigin.Begin);
                    if (i == 0)
                    {
                        index1 = br.ReadByte();
                    }
                    else
                    {
                        index2 = br.ReadByte();
                    }

                }
            }

            return (index1, index2);
        }

        /// <summary>
        /// Gets the SHA1 hash for the file section
        /// </summary>
        /// <param name="dataFile">The data file to get the hash for</param>
        /// <returns>The byte array containing the hash value</returns>
        public byte[] GetIndexSection1Hash(DirectoryInfo indexPath)
        {
            byte[] sha1Bytes;

            using (var br = new BinaryReader(File.OpenRead(indexPath.FullName)))
            {
                br.BaseStream.Seek(1040, SeekOrigin.Begin);
                sha1Bytes = br.ReadBytes(20);
            }

            return sha1Bytes;
        }

        /// <summary>
        /// Gets the offset for the data in the .dat file
        /// </summary>
        /// <param name="hashedFolder">The hashed value of the folder path</param>
        /// <param name="hashedFile">The hashed value of the file name</param>
        /// <param name="dataFile">The data file to look in</param>
        /// <returns>The offset to the data</returns>
        public Task<int> GetDataOffset(int hashedFolder, int hashedFile, XivDataFile dataFile)
        {
            return Task.Run(async () =>
            {
                var indexPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");
                var offset = 0;

                // These are the offsets to relevant data
                const int fileCountOffset = 1036;
                const int dataStartOffset = 2048;

                await _semaphoreSlim.WaitAsync();

                try
                {
                    using (var br = new BinaryReader(File.OpenRead(indexPath)))
                    {
                        br.BaseStream.Seek(fileCountOffset, SeekOrigin.Begin);
                        var fileCount = br.ReadInt32();

                        br.BaseStream.Seek(dataStartOffset, SeekOrigin.Begin);

                        // loop through each file entry
                        for (var i = 0; i < fileCount; br.ReadBytes(4), i += 16)
                        {
                            var fileNameHash = br.ReadInt32();

                            // check if the provided file name hash matches the current file name hash
                            if (fileNameHash == hashedFile)
                            {
                                var folderPathHash = br.ReadInt32();

                                // check if the provided folder path hash matches the current folder path hash
                                if (folderPathHash == hashedFolder)
                                {
                                    // this is the entry we are looking for, get the offset and break out of the loop
                                    offset = br.ReadInt32() * 8;
                                    break;
                                }

                                br.ReadBytes(4);
                            }
                            else
                            {
                                br.ReadBytes(8);
                            }
                        }
                    }
                }
                finally
                {
                    _semaphoreSlim.Release();
                }

                return offset;
            });
        }

        /// <summary>
        /// Gets the file dictionary for the data in the .dat file
        /// </summary>
        /// <param name="dataFile">The data file to look in</param>
        /// <returns>Dictionary containing (concatenated string of file+folder hashes, offset) </returns>
        public Task<Dictionary<string, int>> GetFileDictionary(XivDataFile dataFile)
        {
            return Task.Run(() =>
            {
                var fileDictionary = new Dictionary<string, int>();
                var indexPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");

                // These are the offsets to relevant data
                const int fileCountOffset = 1036;
                const int dataStartOffset = 2048;

                using (var br = new BinaryReader(File.OpenRead(indexPath)))
                {
                    br.BaseStream.Seek(fileCountOffset, SeekOrigin.Begin);
                    var fileCount = br.ReadInt32();

                    br.BaseStream.Seek(dataStartOffset, SeekOrigin.Begin);

                    // loop through each file entry
                    for (var i = 0; i < fileCount; br.ReadBytes(4), i += 16)
                    {
                        var fileNameHash = br.ReadInt32();
                        var folderPathHash = br.ReadInt32();
                        var offset = br.ReadInt32() * 8;

                        fileDictionary.Add($"{fileNameHash}{folderPathHash}", offset);
                    }
                }

                return fileDictionary;
            });
        }

        /// <summary>
        /// Checks whether the index file contains any of the folders passed in
        /// </summary>
        /// <remarks>
        /// Runs through the index file once checking if the hashed folder value exists in the dictionary
        /// then adds it to the list if it does.
        /// </remarks>
        /// <param name="hashNumDictionary">A Dictionary containing the folder hash and item number</param>
        /// <param name="dataFile">The data file to look in</param>
        /// <returns></returns>
        public Task<List<int>> GetFolderExistsList(Dictionary<int, int> hashNumDictionary, XivDataFile dataFile)
        {
            return Task.Run(async () =>
            {
                await _semaphoreSlim.WaitAsync();

                // HashSet because we don't want any duplicates
                var folderExistsList = new HashSet<int>();

                try
                {
                    // These are the offsets to relevant data
                    const int fileCountOffset = 1036;
                    const int dataStartOffset = 2048;

                    var indexPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");

                    using (var br = new BinaryReader(File.OpenRead(indexPath)))
                    {
                        br.BaseStream.Seek(fileCountOffset, SeekOrigin.Begin);
                        var totalFiles = br.ReadInt32();

                        br.BaseStream.Seek(dataStartOffset, SeekOrigin.Begin);
                        for (var i = 0; i < totalFiles; br.ReadBytes(4), i += 16)
                        {
                            br.ReadBytes(4);

                            var folderPathHash = br.ReadInt32();

                            if (hashNumDictionary.ContainsKey(folderPathHash))
                            {
                                folderExistsList.Add(hashNumDictionary[folderPathHash]);

                                br.ReadBytes(4);
                            }
                            else
                            {
                                br.ReadBytes(4);
                            }
                        }
                    }
                }
                finally
                {
                    _semaphoreSlim.Release();
                }

                return folderExistsList.ToList();
            });
        }


        /// <summary>
        /// Determines whether the given file path exists
        /// </summary>
        /// <param name="fileHash">The hashed file</param>
        /// <param name="folderHash">The hashed folder</param>
        /// <param name="dataFile">The data file</param>
        /// <returns>True if it exists, False otherwise</returns>
        public async Task<bool> FileExists(int fileHash, int folderHash, XivDataFile dataFile)
        {
            var indexPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");

            // These are the offsets to relevant data
            const int fileCountOffset = 1036;
            const int dataStartOffset = 2048;

            return await Task.Run(() =>
            {
                using (var br = new BinaryReader(File.OpenRead(indexPath)))
                {
                    br.BaseStream.Seek(fileCountOffset, SeekOrigin.Begin);
                    var numOfFiles = br.ReadInt32();

                    br.BaseStream.Seek(dataStartOffset, SeekOrigin.Begin);
                    for (var i = 0; i < numOfFiles; br.ReadBytes(4), i += 16)
                    {
                        var fileNameHash = br.ReadInt32();

                        if (fileNameHash == fileHash)
                        {
                            var folderPathHash = br.ReadInt32();

                            if (folderPathHash == folderHash)
                            {
                                return true;
                            }

                            br.ReadBytes(4);
                        }
                        else
                        {
                            br.ReadBytes(8);
                        }
                    }
                }

                return false;
            });
        }

        /// <summary>
        /// Determines whether the given folder path exists
        /// </summary>
        /// <param name="folderHash">The hashed folder</param>
        /// <param name="dataFile">The data file</param>
        /// <returns>True if it exists, False otherwise</returns>
        public async Task<bool> FolderExists(int folderHash, XivDataFile dataFile)
        {
            var indexPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");

            // These are the offsets to relevant data
            const int fileCountOffset = 1036;
            const int dataStartOffset = 2048;

            return await Task.Run(() =>
            {
                using (var br = new BinaryReader(File.OpenRead(indexPath)))
                {
                    br.BaseStream.Seek(fileCountOffset, SeekOrigin.Begin);
                    var numOfFiles = br.ReadInt32();

                    br.BaseStream.Seek(dataStartOffset, SeekOrigin.Begin);
                    for (var i = 0; i < numOfFiles; br.ReadBytes(4), i += 16)
                    {
                        var fileNameHash = br.ReadInt32();

                        var folderPathHash = br.ReadInt32();

                        if (folderPathHash == folderHash)
                        {
                            return true;
                        }

                        br.ReadBytes(4);
                    }
                }

                return false;
            });
        }

        /// <summary>
        /// Gets all the file offsets in a given folder path
        /// </summary>
        /// <param name="hashedFolder">The hashed value of the folder path</param>
        /// <param name="dataFile">The data file to look in</param>
        /// <returns>A list of all of the offsets in the given folder</returns>
        public async Task<List<int>> GetAllFileOffsetsInFolder(int hashedFolder, XivDataFile dataFile)
        {
            var fileOffsetList = new List<int>();

            // These are the offsets to relevant data
            const int fileCountOffset = 1036;
            const int dataStartOffset = 2048;

            var indexPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");

            await Task.Run(() =>
            {
                using (var br = new BinaryReader(File.OpenRead(indexPath)))
                {
                    br.BaseStream.Seek(fileCountOffset, SeekOrigin.Begin);
                    var totalFiles = br.ReadInt32();

                    br.BaseStream.Seek(dataStartOffset, SeekOrigin.Begin);
                    for (var i = 0; i < totalFiles; br.ReadBytes(4), i += 16)
                    {
                        br.ReadBytes(4);

                        var folderPathHash = br.ReadInt32();

                        if (folderPathHash == hashedFolder)
                        {
                            fileOffsetList.Add(br.ReadInt32() * 8);
                        }
                        else
                        {
                            br.ReadBytes(4);
                        }
                    }
                }
            });

            return fileOffsetList;
        }

        /// <summary>
        /// Gets all the folder hashes in a given folder path
        /// </summary>
        /// <param name="dataFile">The data file to look in</param>
        /// <returns>A list of all of the folder hashes</returns>
        public async Task<List<int>> GetAllFolderHashes(XivDataFile dataFile)
        {
            var folderHashList = new HashSet<int>();

            // These are the offsets to relevant data
            const int fileCountOffset = 1036;
            const int dataStartOffset = 2048;

            var indexPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");

            await Task.Run(() =>
            {
                using (var br = new BinaryReader(File.OpenRead(indexPath)))
                {
                    br.BaseStream.Seek(fileCountOffset, SeekOrigin.Begin);
                    var totalFiles = br.ReadInt32();

                    br.BaseStream.Seek(dataStartOffset, SeekOrigin.Begin);
                    for (var i = 0; i < totalFiles; br.ReadBytes(4), i += 16)
                    {
                        br.ReadBytes(4);

                        var folderPathHash = br.ReadInt32();

                        folderHashList.Add(folderPathHash);

                        br.ReadBytes(4);
                    }
                }
            });

            return folderHashList.ToList();
        }

        /// <summary>
        /// Get all the hashed values of the files in a given folder 
        /// </summary>
        /// <param name="hashedFolder">The hashed value of the folder path</param>
        /// <param name="dataFile">The data file to look in</param>
        /// <returns>A list containing the hashed values of the files in the given folder</returns>
        public async Task<List<int>> GetAllHashedFilesInFolder(int hashedFolder, XivDataFile dataFile)
        {
            var fileHashesList = new List<int>();

            // These are the offsets to relevant data
            const int fileCountOffset = 1036;
            const int dataStartOffset = 2048;

            var indexPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");

            await Task.Run(() =>
            {
                using (var br = new BinaryReader(File.OpenRead(indexPath)))
                {
                    br.BaseStream.Seek(fileCountOffset, SeekOrigin.Begin);
                    var totalFiles = br.ReadInt32();

                    br.BaseStream.Seek(dataStartOffset, SeekOrigin.Begin);
                    for (var i = 0; i < totalFiles; br.ReadBytes(4), i += 16)
                    {
                        var hashedFile = br.ReadInt32();

                        var folderPathHash = br.ReadInt32();

                        if (folderPathHash == hashedFolder)
                        {
                            fileHashesList.Add(hashedFile);
                        }

                        br.ReadBytes(4);
                    }
                }
            });

            return fileHashesList;
        }

        /// <summary>
        /// Get all the file hash and file offset in a given folder 
        /// </summary>
        /// <param name="hashedFolder">The hashed value of the folder path</param>
        /// <param name="dataFile">The data file to look in</param>
        /// <returns>A list containing the hashed values of the files in the given folder</returns>
        public async Task<Dictionary<int, int>> GetAllHashedFilesAndOffsetsInFolder(int hashedFolder, XivDataFile dataFile)
        {
            var fileHashesDict = new Dictionary<int, int>();

            // These are the offsets to relevant data
            const int fileCountOffset = 1036;
            const int dataStartOffset = 2048;

            var indexPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");

            await Task.Run(() =>
            {
                using (var br = new BinaryReader(File.OpenRead(indexPath)))
                {
                    br.BaseStream.Seek(fileCountOffset, SeekOrigin.Begin);
                    var totalFiles = br.ReadInt32();

                    br.BaseStream.Seek(dataStartOffset, SeekOrigin.Begin);
                    for (var i = 0; i < totalFiles; br.ReadBytes(4), i += 16)
                    {
                        var hashedFile = br.ReadInt32();

                        var folderPathHash = br.ReadInt32();

                        if (folderPathHash == hashedFolder)
                        {
                            fileHashesDict.Add(hashedFile, br.ReadInt32() * 8);
                        }
                        else
                        {
                            br.ReadBytes(4);
                        }
                    }
                }
            });

            return fileHashesDict;
        }

        /// <summary>
        /// Deletes a file descriptor/stub from the Index files.
        /// </summary>
        /// <param name="fullPath">Full internal file path to the file that should be deleted.</param>
        /// <param name="dataFile">Which data file to use</param>
        /// <returns></returns>
        public bool DeleteFileDescriptor(string fullPath, XivDataFile dataFile)
        {
            fullPath = fullPath.Replace("\\", "/");
            var pathHash = HashGenerator.GetHash(fullPath.Substring(0, fullPath.LastIndexOf("/", StringComparison.Ordinal)));
            var fileHash = HashGenerator.GetHash(Path.GetFileName(fullPath));
            var uPathHash = BitConverter.ToUInt32(BitConverter.GetBytes(pathHash), 0);
            var uFileHash = BitConverter.ToUInt32(BitConverter.GetBytes(fileHash), 0);
            var fullPathHash = HashGenerator.GetHash(fullPath);
            var uFullPathHash = BitConverter.ToUInt32(BitConverter.GetBytes(fullPathHash), 0);

            var SegmentHeaders = new int[4];
            var SegmentOffsets = new int[4];
            var SegmentSizes = new int[4];

            // Segment header offsets
            SegmentHeaders[0] = 1028;                   // Files
            SegmentHeaders[1] = 1028 + (72 * 1) + 4;    // Unknown
            SegmentHeaders[2] = 1028 + (72 * 2) + 4;    // Unknown
            SegmentHeaders[3] = 1028 + (72 * 3) + 4;    // Folders


            // Index 1 Closure
            {
                var indexPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");

                // Dump the index into memory, since we're going to have to inject data.
                byte[] originalIndex = File.ReadAllBytes(indexPath);
                byte[] modifiedIndex = new byte[originalIndex.Length - 16];

                // Get all the segment header data
                for (int i = 0; i < SegmentHeaders.Length; i++)
                {
                    SegmentOffsets[i] = BitConverter.ToInt32(originalIndex, SegmentHeaders[i] + 4);
                    SegmentSizes[i] = BitConverter.ToInt32(originalIndex, SegmentHeaders[i] + 8);
                }

                int fileCount = SegmentSizes[0] / 16;

                // Search for appropriate location to inject data.
                var deleteLocation = 0;

                for (int i = 0; i < fileCount; i++)
                {
                    int position = SegmentOffsets[0] + (i * 16);
                    uint iHash = BitConverter.ToUInt32(originalIndex, position);
                    uint iPathHash = BitConverter.ToUInt32(originalIndex, position + 4);
                    uint iOffset = BitConverter.ToUInt32(originalIndex, position + 8);
                    
                    if (iHash == uFileHash && iPathHash == uPathHash)
                    {
                        deleteLocation = position;
                        break;
                    }
                }

                // Cancel if we failed to find the file.
                if (deleteLocation == 0)
                {
                    return false;
                }

                byte[] DataToDelete = new byte[16];
                Array.Copy(originalIndex, deleteLocation, DataToDelete, 0, 16);

                // Split the file at the injection point.
                int remainder = originalIndex.Length - deleteLocation - 16;
                Array.Copy(originalIndex, 0, modifiedIndex, 0, deleteLocation);
                Array.Copy(originalIndex, deleteLocation + 16, modifiedIndex, deleteLocation, remainder);


                // Update the segment headers.
                for (int i = 0; i < SegmentHeaders.Length; i++)
                {
                    // Update Segment 0 Size.
                    if (i == 0)
                    {
                        SegmentSizes[i] -= 16;
                        Array.Copy(BitConverter.GetBytes(SegmentSizes[i]), 0, modifiedIndex, SegmentHeaders[i] + 8, 4);

                    }
                    // Update other segments' offsets.
                    else
                    {
                        SegmentOffsets[i] -= 16;
                        Array.Copy(BitConverter.GetBytes(SegmentOffsets[i]), 0, modifiedIndex, SegmentHeaders[i] + 4, 4);
                    }
                }
                // Update the folder structure
                var folderCount = SegmentSizes[3] / 16;
                bool foundFolder = false;

                for (int i = 0; i < folderCount; i++)
                {
                    int position = SegmentOffsets[3] + (i * 16);
                    uint iHash = BitConverter.ToUInt32(modifiedIndex, position);
                    uint iOffset = BitConverter.ToUInt32(modifiedIndex, position + 4);
                    uint iFolderSize = BitConverter.ToUInt32(modifiedIndex, position + 8);

                    // Update folder offset
                    if (iOffset > deleteLocation)
                    {
                        Array.Copy(BitConverter.GetBytes(iOffset - 16), 0, modifiedIndex, position + 4, 4);
                    }

                    // Update folder size
                    if (iHash == uPathHash)
                    {
                        foundFolder = true;
                        Array.Copy(BitConverter.GetBytes(iFolderSize - 16), 0, modifiedIndex, position + 8, 4);
                    }
                }

                if (!foundFolder)
                {
                    return false;
                }

                
                // Update SHA-1 Hashes.
                SHA1 sha = new SHA1CryptoServiceProvider();
                byte[] shaHash;
                for (int i = 0; i < SegmentHeaders.Length; i++)
                {
                    //Segment
                    shaHash = sha.ComputeHash(modifiedIndex, SegmentOffsets[i], SegmentSizes[i]);
                    Array.Copy(shaHash, 0, modifiedIndex, SegmentHeaders[i] + 12, 20);
                }

                // Compute Hash of the header segment
                shaHash = sha.ComputeHash(modifiedIndex, 0, 960);
                Array.Copy(shaHash, 0, modifiedIndex, 960, 20);
                


                // Write file
                File.WriteAllBytes(indexPath, modifiedIndex);
            }

            // Index 2 Closure
            {
                var index2Path = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{Index2Extension}");

                // Dump the index into memory, since we're going to have to inject data.
                byte[] originalIndex = File.ReadAllBytes(index2Path);
                byte[] modifiedIndex = new byte[originalIndex.Length - 8];

                // Get all the segment header data
                for (int i = 0; i < SegmentHeaders.Length; i++)
                {
                    SegmentOffsets[i] = BitConverter.ToInt32(originalIndex, SegmentHeaders[i] + 4);
                    SegmentSizes[i] = BitConverter.ToInt32(originalIndex, SegmentHeaders[i] + 8);
                }

                int fileCount = SegmentSizes[0] / 8;

                // Search for appropriate location to inject data.
                var deleteLocation = 0;

                for (int i = 0; i < fileCount; i++)
                {
                    int position = SegmentOffsets[0] + (i * 8);
                    uint iFullPathHash = BitConverter.ToUInt32(originalIndex, position);
                    uint iOffset = BitConverter.ToUInt32(originalIndex, position + 4);

                    // Index 2 is just in hash order, so find the spot where we fit in.
                    if (iFullPathHash == uFullPathHash)
                    {
                        deleteLocation = position;
                    }
                }

                // It's possible a valid file doesn't have an Index 2 entry, just skip it in that case.
                if (deleteLocation > 0)
                {

                    byte[] DataToDelete = new byte[8];
                    Array.Copy(originalIndex, deleteLocation, DataToDelete, 0, 8);


                    // Split the file at the injection point.
                    int remainder = originalIndex.Length - deleteLocation - 8;
                    Array.Copy(originalIndex, 0, modifiedIndex, 0, deleteLocation);
                    Array.Copy(originalIndex, deleteLocation + 8, modifiedIndex, deleteLocation, remainder);



                    // Update the segment headers.
                    for (int i = 0; i < SegmentHeaders.Length; i++)
                    {
                        // Update Segment 0 Size.
                        if (i == 0)
                        {
                            SegmentSizes[i] -= 8;
                            Array.Copy(BitConverter.GetBytes(SegmentSizes[i]), 0, modifiedIndex, SegmentHeaders[i] + 8, 4);

                        }
                        // Update other segments' offsets.
                        else
                        {
                            // Index 2 doesn't have all 4 segments.
                            if (SegmentOffsets[i] != 0)
                            {
                                SegmentOffsets[i] -= 8;
                                Array.Copy(BitConverter.GetBytes(SegmentOffsets[i]), 0, modifiedIndex, SegmentHeaders[i] + 4, 4);
                            }
                        }
                    }
                    
                    // Update SHA-1 Hashes.
                    SHA1 sha = new SHA1CryptoServiceProvider();
                    byte[] shaHash;
                    for (int i = 0; i < SegmentHeaders.Length; i++)
                    {
                        if (SegmentSizes[i] > 0)
                        {
                            //Segment
                            byte[] oldHash = new byte[20];
                            Array.Copy(originalIndex, SegmentHeaders[i] + 12, oldHash, 0, 20);

                            shaHash = sha.ComputeHash(modifiedIndex, SegmentOffsets[i], SegmentSizes[i]);
                            Array.Copy(shaHash, 0, modifiedIndex, SegmentHeaders[i] + 12, 20);
                        }
                    }

                    // Compute Hash of the header segment
                    shaHash = sha.ComputeHash(modifiedIndex, 0, 960);
                    Array.Copy(shaHash, 0, modifiedIndex, 960, 20);
                    
                    // Write file
                    File.WriteAllBytes(index2Path, modifiedIndex);
                }
            }

            return true;
        }

        /// <summary>
        /// Adds a new file descriptor/stub into the Index files.
        /// </summary>
        /// <param name="fullPath">Full path to the new file.</param>
        /// <param name="dataOffset">Raw DAT file offset to use for the new file.</param>
        /// <param name="dataFile">Which data file set to use.</param>
        /// <returns></returns>
        public bool AddFileDescriptor(string fullPath, int dataOffset, XivDataFile dataFile)
        {
            fullPath = fullPath.Replace("\\", "/");
            var pathHash = HashGenerator.GetHash(fullPath.Substring(0, fullPath.LastIndexOf("/", StringComparison.Ordinal)));
            var fileHash = HashGenerator.GetHash(Path.GetFileName(fullPath));
            var uPathHash = BitConverter.ToUInt32(BitConverter.GetBytes(pathHash), 0);
            var uFileHash = BitConverter.ToUInt32(BitConverter.GetBytes(fileHash), 0);
            var fullPathHash = HashGenerator.GetHash(fullPath);
            var uFullPathHash = BitConverter.ToUInt32(BitConverter.GetBytes(fullPathHash), 0);

            var SegmentHeaders = new int[4];
            var SegmentOffsets = new int[4];
            var SegmentSizes = new int[4];

            // Segment header offsets
            SegmentHeaders[0] = 1028;                   // Files
            SegmentHeaders[1] = 1028 + (72 * 1) + 4;    // Unknown
            SegmentHeaders[2] = 1028 + (72 * 2) + 4;    // Unknown
            SegmentHeaders[3] = 1028 + (72 * 3) + 4;    // Folders


            // Index 1 Closure
            {
                var indexPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");

                // Dump the index into memory, since we're going to have to inject data.
                byte[] originalIndex = File.ReadAllBytes(indexPath);
                byte[] modifiedIndex = new byte[originalIndex.Length + 16];

                // Get all the segment header data
                for (int i = 0; i < SegmentHeaders.Length; i++)
                {
                    SegmentOffsets[i] = BitConverter.ToInt32(originalIndex, SegmentHeaders[i] + 4);
                    SegmentSizes[i] = BitConverter.ToInt32(originalIndex, SegmentHeaders[i] + 8);
                }

                int fileCount = SegmentSizes[0] / 16;

                // Search for appropriate location to inject data.
                bool foundFolder = false;
                var injectLocation = SegmentOffsets[0] + SegmentSizes[0];

                for (int i = 0; i < fileCount; i++)
                {
                    int position = SegmentOffsets[0] + (i * 16);
                    uint iHash = BitConverter.ToUInt32(originalIndex, position);
                    uint iPathHash = BitConverter.ToUInt32(originalIndex, position + 4);
                    uint iOffset = BitConverter.ToUInt32(originalIndex, position + 8);

                    if (iPathHash == uPathHash)
                    {
                        foundFolder = true;

                        if(iHash == uFileHash)
                        {
                            // File already exists
                            return false;
                        }
                        else if (iHash > uFileHash)
                        {
                            injectLocation = position;
                            break;
                        }
                    }
                    else
                    {
                        // End of folder - inject file here if we haven't yet.
                        if (foundFolder == true)
                        {
                            injectLocation = position;
                            break;
                        }
                    }
                }

                // Cancel if we failed to find the path.
                if (foundFolder == false)
                {
                    return false;
                }

                // Split the file at the injection point.
                int remainder = originalIndex.Length - injectLocation;
                Array.Copy(originalIndex, 0, modifiedIndex, 0, injectLocation);
                Array.Copy(originalIndex, injectLocation, modifiedIndex, injectLocation + 16, remainder);


                // Update the segment headers.
                for (int i = 0; i < SegmentHeaders.Length; i++)
                {
                    // Update Segment 0 Size.
                    if (i == 0)
                    {
                        SegmentSizes[i] += 16;
                        Array.Copy(BitConverter.GetBytes(SegmentSizes[i]), 0, modifiedIndex, SegmentHeaders[i] + 8, 4);

                    }
                    // Update other segments' offsets.
                    else
                    {
                        SegmentOffsets[i] += 16;
                        Array.Copy(BitConverter.GetBytes(SegmentOffsets[i]), 0, modifiedIndex, SegmentHeaders[i] + 4, 4);
                    }
                }

                // Set the actual Injected Data
                Array.Copy(BitConverter.GetBytes(fileHash), 0, modifiedIndex, injectLocation, 4);
                Array.Copy(BitConverter.GetBytes(pathHash), 0, modifiedIndex, injectLocation + 4, 4);
                Array.Copy(BitConverter.GetBytes(dataOffset), 0, modifiedIndex, injectLocation + 8, 4);

                // Update the folder structure
                var folderCount = SegmentSizes[3] / 16;
                foundFolder = false;

                for (int i = 0; i < folderCount; i++)
                {
                    int position = SegmentOffsets[3] + (i * 16);
                    uint iHash = BitConverter.ToUInt32(modifiedIndex, position);
                    uint iOffset = BitConverter.ToUInt32(modifiedIndex, position + 4);
                    uint iFolderSize = BitConverter.ToUInt32(modifiedIndex, position + 8);

                    // Update folder offset
                    if (iOffset > injectLocation)
                    {
                        Array.Copy(BitConverter.GetBytes(iOffset + 16), 0, modifiedIndex, position + 4, 4);
                    }

                    // Update folder size
                    if (iHash == uPathHash)
                    {
                        foundFolder = true;
                        Array.Copy(BitConverter.GetBytes(iFolderSize + 16), 0, modifiedIndex, position + 8, 4);
                    }
                }

                if(!foundFolder)
                {
                    return false;
                }
                
                // Update SHA-1 Hashes.
                SHA1 sha = new SHA1CryptoServiceProvider();
                byte[] shaHash;
                for (int i = 0; i < SegmentHeaders.Length; i++)
                {
                    //Segment
                    shaHash = sha.ComputeHash(modifiedIndex, SegmentOffsets[i], SegmentSizes[i]);
                    Array.Copy(shaHash, 0, modifiedIndex, SegmentHeaders[i]+12, 20);
                }

                // Compute Hash of the header segment
                shaHash = sha.ComputeHash(modifiedIndex, 0, 960);
                Array.Copy(shaHash, 0, modifiedIndex, 960, 20);
                


                // Write file
                File.WriteAllBytes(indexPath, modifiedIndex);
            }

            // Index 2 Closure
            {
                var index2Path = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{Index2Extension}");

                // Dump the index into memory, since we're going to have to inject data.
                byte[] originalIndex = File.ReadAllBytes(index2Path);
                byte[] modifiedIndex = new byte[originalIndex.Length + 16];

                // Get all the segment header data
                for (int i = 0; i < SegmentHeaders.Length; i++)
                {
                    SegmentOffsets[i] = BitConverter.ToInt32(originalIndex, SegmentHeaders[i] + 4);
                    SegmentSizes[i] = BitConverter.ToInt32(originalIndex, SegmentHeaders[i] + 8);
                }

                int fileCount = SegmentSizes[0] / 8;

                // Search for appropriate location to inject data.
                var injectLocation = SegmentOffsets[0] + SegmentSizes[0];
                
                for (int i = 0; i < fileCount; i++)
                {
                    int position = SegmentOffsets[0] + (i * 8);
                    uint iFullPathHash = BitConverter.ToUInt32(originalIndex, position);
                    uint iOffset = BitConverter.ToUInt32(originalIndex, position + 4);

                    // Index 2 is just in hash order, so find the spot where we fit in.
                    if(iFullPathHash > uFullPathHash)
                    {
                        injectLocation = position;
                        break;
                    }
                }

                // Split the file at the injection point.
                int remainder = originalIndex.Length - injectLocation;
                Array.Copy(originalIndex, 0, modifiedIndex, 0, injectLocation);
                Array.Copy(originalIndex, injectLocation, modifiedIndex, injectLocation + 8, remainder);


                // Update the segment headers.
                for (int i = 0; i < SegmentHeaders.Length; i++)
                {
                    // Update Segment 0 Size.
                    if (i == 0)
                    {
                        SegmentSizes[i] += 8;
                        Array.Copy(BitConverter.GetBytes(SegmentSizes[i]), 0, modifiedIndex, SegmentHeaders[i] + 8, 4);

                    }
                    // Update other segments' offsets.
                    else
                    {
                        // Index 2 doesn't have all 4 segments.
                        if (SegmentOffsets[i] != 0)
                        {
                            SegmentOffsets[i] += 8;
                            Array.Copy(BitConverter.GetBytes(SegmentOffsets[i]), 0, modifiedIndex, SegmentHeaders[i] + 4, 4);
                        }
                    }
                }

                // Set the actual Injected Data
                Array.Copy(BitConverter.GetBytes(uFullPathHash), 0, modifiedIndex, injectLocation, 4);
                Array.Copy(BitConverter.GetBytes(dataOffset), 0, modifiedIndex, injectLocation + 4, 4);
                
                // Update SHA-1 Hashes.
                SHA1 sha = new SHA1CryptoServiceProvider();
                byte[] shaHash;
                for (int i = 0; i < SegmentHeaders.Length; i++)
                {
                    if (SegmentSizes[i] > 0)
                    {
                        //Segment
                        shaHash = sha.ComputeHash(modifiedIndex, SegmentOffsets[i], SegmentSizes[i]);
                        Array.Copy(shaHash, 0, modifiedIndex, SegmentHeaders[i] + 12, 20);
                    }
                }
                // Compute Hash of the header segment
                shaHash = sha.ComputeHash(modifiedIndex, 0, 960);
                Array.Copy(shaHash, 0, modifiedIndex, 960, 20);
                

                // Write file
                File.WriteAllBytes(index2Path, modifiedIndex);

            }

            return true;
        }

        /// <summary>
        /// Updates the .index files offset for a given item.
        /// </summary>
        /// <param name="offset">The new offset to be used.</param>
        /// <param name="fullPath">The internal path of the file whos offset is to be updated.</param>
        /// <param name="dataFile">The data file to update the index for</param>
        /// <returns>The offset which was replaced.</returns>
        public async Task<int> UpdateIndex(long offset, string fullPath, XivDataFile dataFile)
        {
            fullPath = fullPath.Replace("\\", "/");
            var folderHash =
                HashGenerator.GetHash(fullPath.Substring(0, fullPath.LastIndexOf("/", StringComparison.Ordinal)));
            var fileHash = HashGenerator.GetHash(Path.GetFileName(fullPath));
            var oldOffset = 0;

            // These are the offsets to relevant data
            const int fileCountOffset = 1036;
            const int dataStartOffset = 2048;

            var indexPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");

            await Task.Run(() =>
            {
                using (var index = File.Open(indexPath, FileMode.Open))
                {
                    using (var br = new BinaryReader(index))
                    {
                        using (var bw = new BinaryWriter(index))
                        {
                            br.BaseStream.Seek(fileCountOffset, SeekOrigin.Begin);
                            var numOfFiles = br.ReadInt32();

                            br.BaseStream.Seek(dataStartOffset, SeekOrigin.Begin);
                            for (var i = 0; i < numOfFiles; br.ReadBytes(4), i += 16)
                            {
                                var fileNameHash = br.ReadInt32();

                                if (fileNameHash == fileHash)
                                {
                                    var folderPathHash = br.ReadInt32();

                                    if (folderPathHash == folderHash)
                                    {
                                        oldOffset = br.ReadInt32();
                                        bw.BaseStream.Seek(br.BaseStream.Position - 4, SeekOrigin.Begin);
                                        bw.Write(offset / 8);
                                        break;
                                    }

                                    br.ReadBytes(4);
                                }
                                else
                                {
                                    br.ReadBytes(8);
                                }
                            }
                        }
                    }
                }
            });

            return oldOffset;
        }

        /// <summary>
        /// Updates the .index2 files offset for a given item.
        /// </summary>
        /// <param name="offset">The new offset to be used.</param>
        /// <param name="fullPath">The internal path of the file whos offset is to be updated.</param>
        /// <param name="dataFile">The data file to update the index for</param>
        /// <returns>The offset which was replaced.</returns>
        public async Task UpdateIndex2(long offset, string fullPath, XivDataFile dataFile)
        {
            fullPath = fullPath.Replace("\\", "/");
            var pathHash = HashGenerator.GetHash(fullPath);

            // These are the offsets to relevant data
            const int fileCountOffset = 1036;
            const int dataStartOffset = 2048;

            var index2Path = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{Index2Extension}");

            await Task.Run(() =>
            {
                using (var index = File.Open(index2Path, FileMode.Open))
                {
                    using (var br = new BinaryReader(index))
                    {
                        using (var bw = new BinaryWriter(index))
                        {
                            br.BaseStream.Seek(fileCountOffset, SeekOrigin.Begin);
                            var numOfFiles = br.ReadInt32();

                            br.BaseStream.Seek(dataStartOffset, SeekOrigin.Begin);
                            for (var i = 0; i < numOfFiles; i += 8)
                            {
                                var fullPathHash = br.ReadInt32();

                                if (fullPathHash == pathHash)
                                {
                                    bw.BaseStream.Seek(br.BaseStream.Position, SeekOrigin.Begin);
                                    bw.Write((int) (offset / 8));
                                    break;
                                }

                                br.ReadBytes(4);
                            }
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Creates a backup of the index file.
        /// </summary>
        /// <param name="backupsDirectory">The directory in which to place the backup files.
        /// The directory will be created if it does not exist.</param>
        /// <param name="dataFile">The file to backup.</param>
        public void CreateIndexBackups(DirectoryInfo backupsDirectory, XivDataFile dataFile)
        {
            var fileName = dataFile.GetDataFileName();

            var indexPath = Path.Combine(_gameDirectory.FullName, $"{fileName}{IndexExtension}");
            var index2Path = Path.Combine(_gameDirectory.FullName, $"{fileName}{Index2Extension}");

            var indexBackupPath = Path.Combine(backupsDirectory.FullName, $"{fileName}{IndexExtension}");
            var index2BackupPath = Path.Combine(backupsDirectory.FullName, $"{fileName}{Index2Extension}");

            Directory.CreateDirectory(backupsDirectory.FullName);

            File.Copy(indexPath, indexBackupPath, true);
            File.Copy(index2Path, index2BackupPath, true);
        }


        /// <summary>
        /// Creates a backup of all the index files.
        /// </summary>
        /// <param name="backupsDirectory">The directory in which to place the backup files.</param>
        public void BackupAllIndexFiles(DirectoryInfo backupsDirectory)
        {
            var dataFileList = Enum.GetValues(typeof(XivDataFile)).Cast<XivDataFile>();

            foreach (var dataFile in dataFileList)
            {
                CreateIndexBackups(backupsDirectory, dataFile);
            }
        }

        /// <summary>
        /// Checks to see whether the index file is locked
        /// </summary>
        /// <param name="dataFile">The data file to check</param>
        /// <returns>True if locked</returns>
        public bool IsIndexLocked(XivDataFile dataFile)
        {
            var fileName = dataFile.GetDataFileName();
            var isLocked = false;

            var indexPath = Path.Combine(_gameDirectory.FullName, $"{fileName}{IndexExtension}");
            var index2Path = Path.Combine(_gameDirectory.FullName, $"{fileName}{Index2Extension}");

            FileStream stream = null;
            FileStream stream1 = null;

            try
            {
                stream = File.Open(indexPath, FileMode.Open);
                stream1= File.Open(index2Path, FileMode.Open);
            }
            catch (Exception e)
            {
                isLocked = true;
            }
            finally
            {
                stream?.Dispose();
                stream?.Close();
                stream1?.Dispose();
                stream1?.Close();
            }

            return isLocked;
        }
    }
}