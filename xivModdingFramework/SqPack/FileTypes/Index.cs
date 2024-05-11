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
using xivModdingFramework.General;
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
        public const string IndexExtension = ".win32.index";
        public const string Index2Extension = ".win32.index2";
        private readonly DirectoryInfo _gameDirectory;
        internal static SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

        public Index(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
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

        /// <summary>
        /// Checks whether the index file contains any of the folders passed in
        /// 
        /// NOTE: Not Transaction safe... Though since this codepath is really only used for jank listing of available face/hair/etc. entries,
        /// this isn't really a big concern at present.
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


        private static Dictionary<XivDataFile, long> _ReadOnlyIndexLastModifiedTime = new Dictionary<XivDataFile, long>();
        private static Dictionary<XivDataFile, IndexFile> _CachedReadOnlyIndexFiles = new Dictionary<XivDataFile, IndexFile>();

        public void ClearIndexCache()
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
                            index = new IndexFile(dataFile, index1Stream, index2Stream, true);
                        }
                    }
                    return index;
                } else
                {
                    var lastTime = File.GetLastWriteTimeUtc(index1Path).Ticks;

                    // If we don't have the file cached or the write time doesn't match exactly.
                    if (!_ReadOnlyIndexLastModifiedTime.ContainsKey(dataFile) || lastTime != _ReadOnlyIndexLastModifiedTime[dataFile] || lastTime == 0)
                    {
                        using (var index1Stream = new BinaryReader(File.OpenRead(index1Path)))
                        {
                            using (var index2Stream = new BinaryReader(File.OpenRead(index2Path)))
                            {
                                index = new IndexFile(dataFile, index1Stream, index2Stream, false);
                            }
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
 