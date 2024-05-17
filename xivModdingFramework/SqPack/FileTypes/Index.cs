﻿// xivModdingFramework
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
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.SqPack.DataContainers;

namespace xivModdingFramework.SqPack.FileTypes
{
    /// <summary>
    /// Class for manipulating Index files.
    /// Primarily this holds a number of weird one-off Index parsing functions for use in data scraping.
    /// </summary>
    internal class Index
    {
        internal const string IndexExtension = ".win32.index";
        internal const string Index2Extension = ".win32.index2";
        private readonly DirectoryInfo _gameDirectory;
        internal static SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

        internal Index(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
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
        internal async Task<List<int>> GetFolderExistsList(Dictionary<int, int> hashNumDictionary, XivDataFile dataFile, ModTransaction tx = null)
        {
            if(tx == null)
            {
                tx = ModTransaction.BeginTransaction();
            }

            var ret = new List<int>();
            var index = await tx.GetIndexFile(dataFile);
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
        internal async Task<List<long>> GetAllFileOffsetsInFolder(int hashedFolder, XivDataFile dataFile, ModTransaction tx)
        {
            var index = await tx.GetIndexFile(dataFile);
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
        internal async Task<List<int>> GetAllHashedFilesInFolder(int hashedFolder, XivDataFile dataFile, ModTransaction tx)
        {
            var index = await tx.GetIndexFile(dataFile);
            var entries = index.GetEntriesInFolder((uint)hashedFolder);

            var hashes = entries.Select(x => (int)x.FileNameHash);
            return hashes.ToList();
        }

        /// <summary>
        /// Gets the entire universe of hash pairs (folder, file) for a datafile.
        /// </summary>
        internal async Task<Dictionary<uint, HashSet<uint>>> GetAllHashes(XivDataFile dataFile)
        {
            var index = await GetIndexFile(dataFile, false, true);
            return index.GetAllHashes();
        }


        private static Dictionary<XivDataFile, long> _ReadOnlyIndexLastModifiedTime = new Dictionary<XivDataFile, long>();
        private static Dictionary<XivDataFile, IndexFile> _CachedReadOnlyIndexFiles = new Dictionary<XivDataFile, IndexFile>();

        internal void ClearIndexCache()
        {
            _ReadOnlyIndexLastModifiedTime = new Dictionary<XivDataFile, long>();
            _CachedReadOnlyIndexFiles = new Dictionary<XivDataFile, IndexFile>();
        }

        /// <summary>
        /// Creates an Index File object from the game index files.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <returns></returns>
        internal async Task<IndexFile> GetIndexFile(XivDataFile dataFile, bool alreadySemaphoreLocked = false, bool readOnly = false)
        {
            var index1Path = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{IndexExtension}");
            var index2Path = Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}{Index2Extension}");

            if (!alreadySemaphoreLocked)
            {
                await _semaphoreSlim.WaitAsync();
            }
            try { 

            IndexFile index;

                if (!readOnly)
                {
                    // If we're getting a writeable index, we need to get a fresh copy to avoid polluting the cache.
                    using (var index1Stream = new BinaryReader(File.OpenRead(index1Path)))
                    {
                        using (var index2Stream = new BinaryReader(File.OpenRead(index2Path)))
                        {
                            index = new TransactionIndexFile(dataFile, index1Stream, index2Stream, readOnly);
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
                                index = new IndexFile(dataFile, index1Stream, index2Stream, readOnly);
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


        /// <summary>
        /// Tests if the given formatted paths exist, returning the ones that do.
        /// </summary>
        /// <param name="tx"></param>
        /// <param name="format"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        public static async Task<List<int>> CheckExistsMultiple(ModTransaction tx, string format, int min, int max)
        {
            var ret = new List<int>();
            for(int i = min; i < max; i++)
            {
                var s = string.Format(format, i.ToString("D4"));
                if (await tx.FileExists(s))
                {
                    ret.Add(i);
                }
            }
            return ret;
        }

    }
}
 