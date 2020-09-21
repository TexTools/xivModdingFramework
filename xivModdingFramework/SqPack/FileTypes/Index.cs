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

using HelixToolkit.SharpDX.Core.Core2D;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.SqPack.DataContainers;

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
        public void UpdateIndexDatCount(XivDataFile dataFile, int datNum, bool alreadySemaphoreLocked = false)
        {
            var datCount = (byte)(datNum + 1);

            var indexPaths = new[]
            {
                Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}"),
                Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{Index2Extension}")
            };

            foreach (var indexPath in indexPaths)
            {
                if (!alreadySemaphoreLocked)
                {
                    _semaphoreSlim.Wait();
                }
                try
                {
                    using (var bw = new BinaryWriter(File.OpenWrite(indexPath)))
                    {
                        bw.BaseStream.Seek(1104, SeekOrigin.Begin);
                        bw.Write(datCount);
                    }
                }
                finally
                {
                    if (!alreadySemaphoreLocked)
                    {
                        _semaphoreSlim.Release();
                    }
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

            _semaphoreSlim.Wait();
            try
            {
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
            } finally
            {
                _semaphoreSlim.Release();
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

            _semaphoreSlim.Wait();
            try
            {
                using (var br = new BinaryReader(File.OpenRead(indexPath.FullName)))
                {
                    br.BaseStream.Seek(1040, SeekOrigin.Begin);
                    sha1Bytes = br.ReadBytes(20);
                }
            }
            finally
            {
                _semaphoreSlim.Release();
            }

            return sha1Bytes;
        }

        /// <summary>
        /// Gets the SHA1 hash for the file section
        /// </summary>
        /// <param name="dataFile">The data file to get the hash for</param>
        /// <returns>The byte array containing the hash value</returns>
        public byte[] GetIndexSection2Hash(DirectoryInfo indexPath)
        {
            byte[] sha1Bytes;

            _semaphoreSlim.Wait();
            try
            {
                using (var br = new BinaryReader(File.OpenRead(indexPath.FullName)))
                {
                    br.BaseStream.Seek(1116, SeekOrigin.Begin);
                    sha1Bytes = br.ReadBytes(20);
                }
            }
            finally
            {
                _semaphoreSlim.Release();
            }

            return sha1Bytes;
        }

        /// <summary>
        /// Gets the SHA1 hash for the file section
        /// </summary>
        /// <param name="dataFile">The data file to get the hash for</param>
        /// <returns>The byte array containing the hash value</returns>
        public byte[] GetIndexSection3Hash(DirectoryInfo indexPath)
        {
            byte[] sha1Bytes;

            _semaphoreSlim.Wait();
            try
            {
                using (var br = new BinaryReader(File.OpenRead(indexPath.FullName)))
                {
                    br.BaseStream.Seek(1188, SeekOrigin.Begin);
                    sha1Bytes = br.ReadBytes(20);
                }
            }
            finally
            {
                _semaphoreSlim.Release();
            }
            

            return sha1Bytes;
        }

        public async Task<long> GetDataOffset(string fullPath)
        {
            var dataFile = IOUtil.GetDataFileFromPath(fullPath);
            var index = await GetIndexFile(dataFile, false, true);
            return index.Get8xDataOffset(fullPath);
        }

        /// <summary>
        /// Retrieves all of the offsets for an arbitrary list of files in the FFXIV file system, using a batch operation for speed.
        /// </summary>
        /// <param name="files"></param>
        /// <returns></returns>
        public async Task<Dictionary<string, long>> GetDataOffsets(List<string> files)
        {
            // Here we need to do two things.
            // 1. Group the files by their data file.
            // 2. Hash the files into their folder/file hashes and build the dictionaries to pass to the private function.

            // Thankfully, we can just do all of that in one pass.

            // This is keyed by Data File => Folder hash => File Hash => Full File Path
            // This is used as dictionaries vs compound objects or lists b/c dictionary key look ups are immensely faster than
            // full list scans, when working with lists of potentially 10000+ files.
            Dictionary<XivDataFile, Dictionary<int, Dictionary<int, string>>> dict = new Dictionary<XivDataFile, Dictionary<int, Dictionary<int, string>>>();

            foreach(var file in files)
            {
                var dataFile = IOUtil.GetDataFileFromPath(file);
                var pathHash = HashGenerator.GetHash(file.Substring(0, file.LastIndexOf("/", StringComparison.Ordinal)));
                var fileHash = HashGenerator.GetHash(Path.GetFileName(file));

                
                if(!dict.ContainsKey(dataFile))
                {
                    dict.Add(dataFile, new Dictionary<int, Dictionary<int, string>>());
                }

                if(!dict[dataFile].ContainsKey(pathHash))
                {
                    dict[dataFile].Add(pathHash, new Dictionary<int, string>());
                }

                if(!dict[dataFile][pathHash].ContainsKey(fileHash))
                {
                    dict[dataFile][pathHash].Add(fileHash, file);
                }
            }

            var ret = new Dictionary<string, long>();
            foreach(var kv in dict)
            {
                var offsets = await GetDataOffsets(kv.Key, kv.Value);

                foreach(var kv2 in offsets)
                {
                    if(!ret.ContainsKey(kv2.Key))
                    {
                        ret.Add(kv2.Key, kv2.Value);
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Retrieves all of the offsets for an arbitrary list of files within the same data file.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <returns></returns>
        private async Task<Dictionary<string, long>> GetDataOffsets(XivDataFile dataFile, Dictionary<int, Dictionary<int, string>> FolderFiles)
        {
            var ret = new Dictionary<string, long>();
            return await Task.Run(() =>
            {
                var indexPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");

                // These are the offsets to relevant data
                const int fileCountOffset = 1036;
                const int dataStartOffset = 2048;

                int count = 0;
                _semaphoreSlim.Wait();
                try
                {
                    using (var br = new BinaryReader(File.OpenRead(indexPath)))
                    {
                        br.BaseStream.Seek(fileCountOffset, SeekOrigin.Begin);
                        var fileCount = br.ReadInt32();

                        br.BaseStream.Seek(dataStartOffset, SeekOrigin.Begin);

                        // loop through each file entry
                        for (var i = 0; i < fileCount; i += 16)
                        {
                            var fileNameHash = br.ReadInt32();
                            var folderPathHash = br.ReadInt32();
                            long offset = br.ReadUInt32();
                            var unused = br.ReadInt32();

                            if (FolderFiles.ContainsKey(folderPathHash))
                            {
                                if(FolderFiles[folderPathHash].ContainsKey(fileNameHash))
                                {
                                    count++;
                                    ret.Add(FolderFiles[folderPathHash][fileNameHash], offset * 8);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    _semaphoreSlim.Release();
                }

                return ret;
            });
        }
        /// <summary>
        /// Retrieves all of the offsets for an arbitrary list of files in the FFXIV file system, using a batch operation for speed.
        /// </summary>
        /// <param name="files"></param>
        /// <returns></returns>
        public async Task<Dictionary<string, long>> GetDataOffsetsIndex2(List<string> files)
        {
            // Here we need to do two things.
            // 1. Group the files by their data file.
            // 2. Get their file hashes.

            // Thankfully, we can just do all of that in one pass.

            // This is keyed by Data File => Folder hash => File Hash => Full File Path
            // This is used as dictionaries vs compound objects or lists b/c dictionary key look ups are immensely faster than
            // full list scans, when working with lists of potentially 10000+ files.
            Dictionary<XivDataFile, Dictionary<uint, string>> dict = new Dictionary<XivDataFile, Dictionary<uint, string>>();

            foreach (var file in files)
            {
                var dataFile = IOUtil.GetDataFileFromPath(file);
                var fullHash = (uint) HashGenerator.GetHash(file);


                if (!dict.ContainsKey(dataFile))
                {
                    dict.Add(dataFile, new Dictionary<uint, string>());
                }

                if (!dict[dataFile].ContainsKey(fullHash))
                {
                    dict[dataFile].Add(fullHash, file);
                }
            }

            var ret = new Dictionary<string, long>();
            foreach (var kv in dict)
            {
                var offsets = await GetDataOffsetsIndex2(kv.Key, kv.Value);

                foreach (var kv2 in offsets)
                {
                    if (!ret.ContainsKey(kv2.Key))
                    {
                        ret.Add(kv2.Key, kv2.Value);
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Retrieves all of the offsets for an arbitrary list of files within the same data file, via their Index2 entries.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <returns></returns>
        private async Task<Dictionary<string, long>> GetDataOffsetsIndex2(XivDataFile dataFile, Dictionary<uint, string> fileHashes)
        {
            var ret = new Dictionary<string, long>();
            return await Task.Run(async () =>
            {
                var index2Path = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{Index2Extension}");


                var SegmentHeaders = new int[4];
                var SegmentOffsets = new int[4];
                var SegmentSizes = new int[4];

                // Segment header offsets
                SegmentHeaders[0] = 1028;                   // Files
                SegmentHeaders[1] = 1028 + (72 * 1) + 4;    // Unknown
                SegmentHeaders[2] = 1028 + (72 * 2) + 4;    // Unknown
                SegmentHeaders[3] = 1028 + (72 * 3) + 4;    // Folders


                await _semaphoreSlim.WaitAsync();
                try
                {

                    // Might as well grab the whole thing since we're doing a full scan.
                    byte[] originalIndex = File.ReadAllBytes(index2Path);

                    // Get all the segment header data
                    for (int i = 0; i < SegmentHeaders.Length; i++)
                    {
                        SegmentOffsets[i] = BitConverter.ToInt32(originalIndex, SegmentHeaders[i] + 4);
                        SegmentSizes[i] = BitConverter.ToInt32(originalIndex, SegmentHeaders[i] + 8);
                    }

                    int fileCount = SegmentSizes[0] / 8;

                    for (int i = 0; i < fileCount; i++)
                    {
                        int position = SegmentOffsets[0] + (i * 8);
                        uint iFullPathHash = BitConverter.ToUInt32(originalIndex, position);
                        uint iOffset = BitConverter.ToUInt32(originalIndex, position + 4);

                        // Index 2 is just in hash order, so find the spot where we fit in.
                        if (fileHashes.ContainsKey(iFullPathHash))
                        {
                            long offset = (long)iOffset;
                            ret.Add(fileHashes[iFullPathHash], offset * 8);
                        }
                    }
                }
                finally
                {
                    _semaphoreSlim.Release();
                }
                return ret;
            });
        }


        public async Task<long> GetDataOffsetIndex2(string fullPath)
        {
            var fullPathHash = HashGenerator.GetHash(fullPath);
            var uFullPathHash = BitConverter.ToUInt32(BitConverter.GetBytes(fullPathHash), 0);
            var dataFile = IOUtil.GetDataFileFromPath(fullPath);
            var index2Path = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{Index2Extension}");

            var SegmentHeaders = new int[4];
            var SegmentOffsets = new int[4];
            var SegmentSizes = new int[4];

            // Segment header offsets
            SegmentHeaders[0] = 1028;                   // Files
            SegmentHeaders[1] = 1028 + (72 * 1) + 4;    // Unknown
            SegmentHeaders[2] = 1028 + (72 * 2) + 4;    // Unknown
            SegmentHeaders[3] = 1028 + (72 * 3) + 4;    // Folders


            await _semaphoreSlim.WaitAsync();
            try
            {

                // Dump the index into memory, since we're going to have to inject data.
                byte[] originalIndex = File.ReadAllBytes(index2Path);

                // Get all the segment header data
                for (int i = 0; i < SegmentHeaders.Length; i++)
                {
                    SegmentOffsets[i] = BitConverter.ToInt32(originalIndex, SegmentHeaders[i] + 4);
                    SegmentSizes[i] = BitConverter.ToInt32(originalIndex, SegmentHeaders[i] + 8);
                }

                int fileCount = SegmentSizes[0] / 8;

                for (int i = 0; i < fileCount; i++)
                {
                    int position = SegmentOffsets[0] + (i * 8);
                    uint iFullPathHash = BitConverter.ToUInt32(originalIndex, position);
                    uint iOffset = BitConverter.ToUInt32(originalIndex, position + 4);

                    // Index 2 is just in hash order, so find the spot where we fit in.
                    if (iFullPathHash == uFullPathHash)
                    {
                        long offset = (long)iOffset;

                        return offset * 8;
                    }
                }
            }
            finally
            {
                _semaphoreSlim.Release();
            }
            return 0;
        }

        /// <summary>
        /// Gets the offset for the data in the .dat file
        /// </summary>
        /// <param name="hashedFolder">The hashed value of the folder path</param>
        /// <param name="hashedFile">The hashed value of the file name</param>
        /// <param name="dataFile">The data file to look in</param>
        /// <returns>The offset to the data</returns>
        public Task<long> GetDataOffset(int hashedFolder, int hashedFile, XivDataFile dataFile)
        {
            return Task.Run(async () =>
            {
                var indexPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");
                long offset = 0;

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
                        for (var i = 0; i < fileCount; i += 16)
                        {
                            // 16 Bytes per entry.
                            var fileNameHash = br.ReadInt32();
                            var folderPathHash = br.ReadInt32();
                            long fileoffset = br.ReadUInt32();
                            var unknown = br.ReadUInt32();

                            // check if the provided file name hash matches the current file name hash
                            if (fileNameHash == hashedFile)
                            {

                                // check if the provided folder path hash matches the current folder path hash
                                if (folderPathHash == hashedFolder)
                                {
                                    // this is the entry we are looking for, get the offset and break out of the loop
                                    offset = fileoffset * 8;
                                    break;
                                }
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
        /// Checks whether the index file contains any of the folders passed in
        /// </summary>
        /// <remarks>
        /// Runs through the index file once checking if the hashed folder value exists in the dictionary
        /// then adds it to the list if it does.
        /// </remarks>
        /// <param name="hashNumDictionary">A Dictionary containing the folder hash and item number</param>
        /// <param name="dataFile">The data file to look in</param>
        /// <returns></returns>
        public async Task<List<int>> GetFolderExistsList(Dictionary<int, int> hashNumDictionary, XivDataFile dataFile)
        {
            var ret = new List<int>();
            var index = await GetIndexFile(dataFile, false, true);
            foreach (var hashKv in hashNumDictionary)
            {
                if (index.FolderExists((uint) hashKv.Key)) {
                    ret.Add(hashKv.Value);
                }
            }

            return ret;
        }



        /// <summary>
        /// Tests if a given file path in FFXIV's internal directory structure
        /// is one that ships with FFXIV, or something added by the framework.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> IsDefaultFilePath(string fullPath)
        {
            // The framework adds flag files alongside every custom created file.
            // This lets you check for them even if the Modlist gets corrupted/lost.
            var exists = await FileExists(fullPath);
            var hasFlag = await FileExists(fullPath + ".flag");

            // In order to be considered a DEFAULT file, the file must both EXIST *and* not have a flag.
            var stockFile = exists && !hasFlag;
            return stockFile;
        }

        public async Task<bool> FileExists(string fullPath)
        {
            var dataFile = IOUtil.GetDataFileFromPath(fullPath);
            return await FileExists(fullPath, dataFile);
        }

        /// <summary>
        /// Determines whether the given file path exists
        /// </summary>
        /// <param name="fileHash">The hashed file</param>
        /// <param name="folderHash">The hashed folder</param>
        /// <param name="dataFile">The data file</param>
        /// <returns>True if it exists, False otherwise</returns>
        public async Task<bool> FileExists(string filePath, XivDataFile dataFile)
        {
            var index = await GetIndexFile(dataFile, false, true);
            return index.FileExists(filePath);
        }

        /// <summary>
        /// Determines whether the given folder path exists
        /// </summary>
        /// <param name="folderHash">The hashed folder</param>
        /// <param name="dataFile">The data file</param>
        /// <returns>True if it exists, False otherwise</returns>
        public async Task<bool> FolderExists(string folder, XivDataFile dataFile)
        {
            var index = await GetIndexFile(dataFile, false, true);
            return index.FolderExists(folder);
        }

        /// <summary>
        /// Gets all the file offsets in a given folder path
        /// </summary>
        /// <param name="hashedFolder">The hashed value of the folder path</param>
        /// <param name="dataFile">The data file to look in</param>
        /// <returns>A list of all of the offsets in the given folder</returns>
        public async Task<List<long>> GetAllFileOffsetsInFolder(int hashedFolder, XivDataFile dataFile)
        {
            var index = await GetIndexFile(dataFile, false, true);
            var entries = index.GetEntriesInFolder((uint)hashedFolder);

            var hashes = entries.Select(x => ((long)x.RawOffset) * 8L);
            return hashes.ToList();
        }

        /// <summary>
        /// Get all the hashed values of the files in a given folder 
        /// </summary>
        /// <param name="hashedFolder">The hashed value of the folder path</param>
        /// <param name="dataFile">The data file to look in</param>
        /// <returns>A list containing the hashed values of the files in the given folder</returns>
        public async Task<List<int>> GetAllHashedFilesInFolder(int hashedFolder, XivDataFile dataFile)
        {
            var index = await GetIndexFile(dataFile, false, true);
            var entries = index.GetEntriesInFolder((uint)hashedFolder);

            var hashes = entries.Select(x => (int)x.FileNameHash);
            return hashes.ToList();
        }

        /// <summary>
        /// Gets the entire universe of hash pairs (folder, file) for a datafile.
        /// </summary>
        public async Task<Dictionary<uint, HashSet<uint>>> GetAllHashes(XivDataFile dataFile)
        {
            var index = await GetIndexFile(dataFile, false, true);
            return index.GetAllHashes();
        }


        /// <summary>
        /// Deletes a file descriptor/stub from the Index files.
        /// </summary>
        /// <param name="fullPath">Full internal file path to the file that should be deleted.</param>
        /// <param name="dataFile">Which data file to use</param>
        /// <returns></returns>
        public async Task<bool> DeleteFileDescriptor(string fullPath, XivDataFile dataFile, bool updateCache = true)
        {
            await UpdateDataOffset(0, fullPath, false, true);

            // This is a metadata entry being deleted, we'll need to restore the metadata entries back to default.
            if (fullPath.EndsWith(".meta"))
            {
                var root = await XivCache.GetFirstRoot(fullPath);
                await ItemMetadata.RestoreDefaultMetadata(root);
            }

            if (updateCache)
            {
                // Queue us for updating, *after* updating the associated metadata files.
                XivCache.QueueDependencyUpdate(fullPath);
            }

            return true;
        }


        private static Dictionary<XivDataFile, long> _ReadOnlyIndexLastModifiedTime = new Dictionary<XivDataFile, long>();
        private static Dictionary<XivDataFile, IndexFile> _CachedReadOnlyIndexFiles = new Dictionary<XivDataFile, IndexFile>();

        public async Task ClearIndexCache()
        {
            _ReadOnlyIndexLastModifiedTime = new Dictionary<XivDataFile, long>();
            _CachedReadOnlyIndexFiles = new Dictionary<XivDataFile, IndexFile>();
        }

        /// <summary>
        /// Creates an Index File object from the game index files.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <returns></returns>
        public async Task<IndexFile> GetIndexFile(XivDataFile dataFile, bool alreadySemaphoreLocked = false, bool allowReadOnly = false)
        {
            var index1Path = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");
            var index2Path = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{Index2Extension}");

            if (!alreadySemaphoreLocked)
            {
                await _semaphoreSlim.WaitAsync();
            }
            try { 

            IndexFile index;

                if (!allowReadOnly)
                {
                    // If we're getting a writeable index, we need to get a fresh copy to avoid polluting the cache.
                    using (var index1Stream = new BinaryReader(File.OpenRead(index1Path)))
                    {
                        using (var index2Stream = new BinaryReader(File.OpenRead(index2Path)))
                        {
                            index = new IndexFile(dataFile, index1Stream, index2Stream);
                        }
                    }
                    return index;
                } else
                {
                    var lastTime = File.GetLastWriteTimeUtc(index1Path).Ticks;
                    var creationTime = File.GetCreationTimeUtc(index1Path).Ticks;

                    // If we don't have the file cached or the write time doesn't match exactly.
                    if (!_ReadOnlyIndexLastModifiedTime.ContainsKey(dataFile) || lastTime != _ReadOnlyIndexLastModifiedTime[dataFile] || lastTime == creationTime || lastTime == 0)
                    {
                        using (var index1Stream = new BinaryReader(File.OpenRead(index1Path)))
                        {
                            index = new IndexFile(dataFile, index1Stream, null);
                        }

                        _ReadOnlyIndexLastModifiedTime[dataFile] = lastTime;
                        _CachedReadOnlyIndexFiles[dataFile] = index;
                        return index;
                    }
                    else
                    {
                        return _CachedReadOnlyIndexFiles[dataFile];
                    }
                }


            }
            finally
            {
                if (!alreadySemaphoreLocked)
                {
                    _semaphoreSlim.Release();
                }
            }

        }

        public async Task SaveIndexFile(IndexFile index, bool alreadySemaphoreLocked = false)
        {
            var index1Path = Path.Combine(_gameDirectory.FullName, $"{index.DataFile.GetDataFileName()}{IndexExtension}");
            var index2Path = Path.Combine(_gameDirectory.FullName, $"{index.DataFile.GetDataFileName()}{Index2Extension}");

            if (!alreadySemaphoreLocked)
            {
                await _semaphoreSlim.WaitAsync();
            }
            try
            {
                if (IsIndexLocked(index.DataFile))
                {
                    throw new Exception("Cannot write index files; files current in use.");
                }

                using (var index1Stream = new BinaryWriter(File.OpenWrite(index1Path)))
                {
                    using (var index2Stream = new BinaryWriter(File.OpenWrite(index2Path)))
                    {
                        index.Save(index1Stream, index2Stream);
                    }
                }


                var _dat = new Dat(XivCache.GameInfo.GameDirectory);
                var largestDatNum = _dat.GetLargestDatNumber(index.DataFile);
                UpdateIndexDatCount(index.DataFile, largestDatNum, true);
            }
            finally
            {
                if (!alreadySemaphoreLocked)
                {
                    _semaphoreSlim.Release();
                }
            }
        }




        /// <summary>
        /// Adds a new file descriptor/stub into the Index files.
        /// </summary>
        /// <param name="fullPath">Full path to the new file.</param>
        /// <param name="dataOffset">Raw DAT file offset to use for the new file.</param>
        /// <param name="dataFile">Which data file set to use.</param>
        /// <returns></returns>
        public async Task<bool> AddFileDescriptor(string fullPath, long dataOffset, XivDataFile dataFile, bool updateCache = true)
        {
            await UpdateDataOffset(dataOffset, fullPath, updateCache);
            return true;
        }


        /// <summary>
        /// Handles updating both indexes in a safe way.
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="fullPath"></param>
        /// <returns></returns>
        public async Task<uint> UpdateDataOffset(long offset, string fullPath, bool updateCache = true, bool allowDeletion = false)
        {

            if(offset <= 0 && !allowDeletion)
            {
                throw new InvalidDataException("Cannot delete file descriptor without delete flag set.");
            }

            uint oldOffset = 0;

            await _semaphoreSlim.WaitAsync();
            try
            {
                var dataFile = IOUtil.GetDataFileFromPath(fullPath);

                var indexPath = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");
                var index2Path = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{Index2Extension}");

                // Test both index files for write access.

                try
                {
                    using (var fs = new FileStream(indexPath, FileMode.Open))
                    {
                        var canRead = fs.CanRead;
                        var canWrite = fs.CanWrite;
                        if (!canRead || !canWrite)
                        {
                            throw new Exception();
                        }
                    }
                    using (var fs = new FileStream(index2Path, FileMode.Open))
                    {
                        var canRead = fs.CanRead;
                        var canWrite = fs.CanWrite;
                        if (!canRead || !canWrite)
                        {
                            throw new Exception();
                        }
                    }
                }
                catch
                {
                    throw new Exception("Unable to update Index files.  File(s) are currently in use.");
                }

                // Now get the actual index data and update.
                var index = await GetIndexFile(dataFile, true);

                oldOffset = index.SetDataOffset(fullPath, offset);
                await SaveIndexFile(index, true);
            }
            finally
            {
                _semaphoreSlim.Release();
            }


            if (updateCache)
            {

                // Queue us up for dependency pre-calcluation, since we're a modded file.
                XivCache.QueueDependencyUpdate(fullPath);

            }

            return oldOffset;
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
 