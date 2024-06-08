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
using System.Threading.Tasks;
using xivModdingFramework.General.Enums;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Mods;
using xivModdingFramework.Resources;
using xivModdingFramework.Cache;

using Index = xivModdingFramework.SqPack.FileTypes.Index;
using xivModdingFramework.Mods.Enums;
using System.Diagnostics;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.SqPack.DataContainers;
using System.Security.Cryptography;

namespace xivModdingFramework.Helpers
{
    public static class ProblemChecker
    {

        /// <summary>
        /// Validates index hashes and resaves the files if their hashes are off.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async Task RevalidateAllIndexHashes()
        {
            if (!XivCache.GameWriteEnabled)
            {
                throw new Exception("Cannot perform Dat Manipulations while DAT writing is disabled.");
            }

            await Task.Run(async () =>
            {
                // The simplest way to do this is to just open a tx, pull every index, and save.
                // That way all hashes/etc. will be recalculated.
                var tx = ModTransaction.BeginTransaction(true, null, null, false, false);
                try
                {
                    foreach (XivDataFile df in Enum.GetValues(typeof(XivDataFile)))
                    {
                        if (CheckHashes(df))
                        {
                            continue;
                        }
                        await tx.GetIndexFile(df);
                    }

                    await ModTransaction.CommitTransaction(tx, true);
                }
                catch
                {
                    ModTransaction.CancelTransaction(tx);
                }
            });

        }
        public static bool CheckHashes(XivDataFile df)
        {
            var index1Path = XivDataFiles.GetFullPath(df, Index.IndexExtension);
            var index2Path = XivDataFiles.GetFullPath(df, Index.Index2Extension);
            using (var index1Stream = new BinaryReader(File.OpenRead(index1Path)))
            {
                if (!CheckHashes(index1Stream))
                {
                    return false;
                }
            }
            using (var index2Stream = new BinaryReader(File.OpenRead(index2Path)))
            {
                if (!CheckHashes(index2Stream))
                {
                    return false;
                }
            }
            return true;
        }
        private static bool CheckHashes(BinaryReader br)
        {
            br.BaseStream.Seek(IndexFile._SqPackHeaderSize, SeekOrigin.Begin);
            var headerSize = br.ReadUInt32();
            if (headerSize != IndexFile._IndexHeaderSize)
            {
                throw new Exception("Invalid index or index file format changed.");
            }

            var segmentHeaderOffsets = new List<int>()
            {
                (int)IndexFile._SqPackHeaderSize + 8,
                (int)IndexFile._SqPackHeaderSize + 84,
                (int)IndexFile._SqPackHeaderSize + 156,
                (int)IndexFile._SqPackHeaderSize + 228,
            };

            foreach (var offset in segmentHeaderOffsets)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
                if (!CheckSegmentHash(br))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool CheckSegmentHash(BinaryReader br)
        {
            int segmentOffset = br.ReadInt32();
            int segmentSize = br.ReadInt32();
            var hash = br.ReadBytes(64);
            br.BaseStream.Seek(segmentOffset, SeekOrigin.Begin);
            var data = br.ReadBytes(segmentSize);
            var sh = SHA1.Create();
            var hash2 = sh.ComputeHash(data);

            for (int i = 0; i < hash.Length && i < hash2.Length; i++)
            {
                if (hash[i] != hash2[i])
                {
                    return false;
                }
            }
            return true;
        }


        /// <summary>
        /// Performs a full reset of game files.  Restores Index Backups, Deletes extra DATs, etc.
        /// If for some reason we cannot restore the Index Backups (Ex. They don't exist, or are for the wrong game version), 
        /// all mods are deleted instead.  If that also fails, an error is thrown instead.
        /// </summary>
        /// <param name="backupsDirectory"></param>
        /// <param name="progress"></param>
        /// <param name="language"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async Task ResetAllGameFiles(DirectoryInfo backupsDirectory, IProgress<string> progress = null)
        {
            await Task.Run(async () =>
            {
                if (!Dat.AllowDatAlteration)
                {
                    throw new Exception("Cannot perform Dat Manipulations while DAT writing is disabled.");
                }

                progress?.Report("Shutting down Cache Worker...");
                var workerStatus = XivCache.CacheWorkerEnabled;
                XivCache.CacheWorkerEnabled = false;
                try
                {
                    if (!Index.CanWriteAllIndexes())
                    {
                        throw new Exception("Unable to open one or more index files for writing.  The files may currently be in use.");
                    }

                    progress?.Report("Restoring index file backups...");
                    try
                    {
                        // Try restoring the indexes FIRST.
                        RestoreIndexBackups(backupsDirectory.FullName);
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            // Index backup failed for some reason.
                            // Try at least deleting all existing mods.
                            using (var tx = ModTransaction.BeginTransaction(true))
                            {
                                progress?.Report("Index restore failed, attempting to delete all mods instead...");
                                await Modding.SetAllModStates(EModState.UnModded, null, tx);
                                await ModTransaction.CommitTransaction(tx);

                                // Wait for anything listening to the TX events to do its thing.
                                await Task.Delay(3000);
                            }
                        }
                        catch (Exception ex2)
                        {
                            // Hard failure.
                            throw new Exception("Start Over Failed: Index Backups Invalid and Unable to Delete all mods.");
                        }
                    }

                    progress?.Report("Deleting modded dat files...");


                    // Delete modded dat files
                    foreach (var xivDataFile in (XivDataFile[])Enum.GetValues(typeof(XivDataFile)))
                    {
                        var datFiles = Dat.GetModdedDatList(xivDataFile);

                        foreach (var datFile in datFiles)
                        {
                            File.Delete(datFile);
                        }
                    }

                    // If for some reason our DAT counts don't match up, update them.
                    Index.UNSAFE_NormalizeAllIndexDatCounts();

                    progress?.Report("Cleaning up mod list...");

                    var modListDirectory = new DirectoryInfo(Path.Combine(XivCache.GameInfo.GameDirectory.Parent.Parent.FullName, XivStrings.ModlistFilePath));

                    // Delete mod list
                    File.Delete(modListDirectory.FullName);

                    Modding.CreateModlist();

                    progress?.Report("Rebuilding Cache...");

                    await Task.Run(async () =>
                    {
                        XivCache.RebuildCache(XivCache.CacheVersion);
                    });
                }
                finally
                {
                    XivCache.CacheWorkerEnabled = workerStatus;
                }
            });
        }

        /// <summary>
        /// Retrieves the file list of available index backup files.  (Ex. Does not include the game version file)
        /// </summary>
        /// <param name="backupDirectory"></param>
        /// <returns></returns>
        public static List<string> GetAvailableIndexBackups(string backupDirectory)
        {
            var allBackups = new List<string>();
            if (!Directory.Exists(backupDirectory))
            {
                return allBackups;
            }

            var ffxivBackups = Path.Combine(backupDirectory, "ffxiv");
            if (!Directory.Exists(ffxivBackups))
            {
                // Old style TT backups folder or empty backups folder, needs to be updated before it can be used.
                return allBackups;
            }

            allBackups = IOUtil.GetFilesInFolder(backupDirectory, "*" + Index.IndexExtension).ToList();
            allBackups.AddRange(IOUtil.GetFilesInFolder(backupDirectory, "*" + Index.Index2Extension));
            return allBackups;
        }


        /// <summary>
        /// Check if the index backups that are stored exist and are for this game version.
        /// </summary>
        /// <param name="backupDirectory"></param>
        /// <returns></returns>
        public static bool AreBackupsValid(string backupDirectory)
        {
            var currentVersion = XivCache.GameInfo.GameVersion;
            var backupFile = Path.Combine(backupDirectory, GameInfo.GameVersionFileName);
            var backupVersion = GameInfo.ReadVersionFile(backupFile);

            if (currentVersion != backupVersion)
            {
                return false;
            }

            var backups = GetAvailableIndexBackups(backupDirectory);
            if (backups.Count == 0)
            {
                return false;
            }

            // Backups must include the Chara index backup as we use it in validation.
            var charaIndex2Backup = backups.FirstOrDefault(x => x.EndsWith(Path.Combine("ffxiv","040000.win32.index2")));
            if (charaIndex2Backup == null)
            {
                return false;
            }

            try
            {
                AssertIndexIsClean(charaIndex2Backup, XivDataFile._04_Chara);
            }
            catch(Exception ex)
            {
                Trace.WriteLine(ex);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Restores Indexes to their backup state, if backups exist.
        /// </summary>
        /// <param name="backupDirectory"></param>
        /// <exception cref="Exception"></exception>
        /// <exception cref="InvalidDataException"></exception>
        public static void RestoreIndexBackups(string backupDirectory)
        {
            if (!Dat.AllowDatAlteration)
            {
                throw new Exception("Cannot perform Dat Manipulations while DAT writing is disabled.");
            }

            if (!AreBackupsValid(backupDirectory))
            {
                throw new Exception("Cannot restore Index Backups when Index Backups are missing, from another FFXIV version, or invalid.");
            }

            var basePath = XivCache.GameInfo.GameDirectory.Parent.FullName;
            var validParents = new List<string>()
            {
                "ffxiv",
                "ex1",
                "ex2",
                "ex3",
                "ex4",
                "ex5",
                "ex6",
                "ex7",
                "ex8",
                "ex9",
            };

            var backups = GetAvailableIndexBackups(backupDirectory);
            foreach (var backup in backups)
            {
                var parPath = new DirectoryInfo(backup).Parent.ToString();
                if (!validParents.Contains(parPath))
                {
                    continue;
                }

                var target = Path.GetFullPath(Path.Combine(basePath, parPath, Path.GetFileName(backup)));
                File.Copy(backup, target, true);

            }
        }

        /// <summary>
        /// Safely attempts to clear old index backups, and creates the index backups folder after.
        /// Will Error if there is anything unusual about the backup folder state.
        /// </summary>
        /// <param name="backupDirectory"></param>
        /// <exception cref="Exception"></exception>
        public static void ClearIndexBackups(string backupDirectory)
        {
            Directory.CreateDirectory(backupDirectory);
            var files = IOUtil.GetFilesInFolder(backupDirectory);

            foreach(var file in files)
            {
                if(!file.EndsWith(Index.IndexExtension) && !file.EndsWith(Index.Index2Extension))
                {
                    if (!file.EndsWith(".ver"))
                    {
                        throw new Exception("Unexpected files in Index Backup Directory, cannot safely clear previous backups.");
                    }
                }
            }

            IOUtil.RecursiveDeleteDirectory(backupDirectory);
            Directory.CreateDirectory(backupDirectory);
        }

        /// <summary>
        /// Clears and creates new index backups.
        /// Will Error if there is an open transaction.
        /// </summary>
        /// <param name="backupDirectory"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async Task CreateIndexBackups(string backupDirectory)
        {
            // Run on a new task since this can be potentially heavy if there's a lot of mods to disable.
            await Task.Run(async () =>
            {
                if (ModTransaction.ActiveTransaction != null)
                {
                    throw new Exception("Cannot create Index Backups while there is an open write-enabled transaction.");
                }

                if (backupDirectory.StartsWith(XivCache.GameInfo.GameDirectory.Parent.FullName))
                {
                    throw new Exception("Cannot use the game directory as backup directory.");
                }

                // Readonly TX to check stuff.
                var rtx = ModTransaction.BeginTransaction();
                var ml = await rtx.GetModList();

                List<Mod> enabledMods = new List<Mod>();
                if (await Modding.AnyModsEnabled(rtx))
                {
                    // Have to use a real TX here, which also means we need write access.
                    if (!Dat.AllowDatAlteration)
                    {
                        throw new Exception("Cannot disable active mods to create Index Backups with DAT writing disabled.");
                    }

                    using (var tx = ModTransaction.BeginTransaction(true))
                    {
                        enabledMods = await Modding.GetActiveMods(tx);
                        await Modding.SetAllModStates(EModState.Disabled, null, tx);
                        await ModTransaction.CommitTransaction(tx);
                    }
                }

                // Reset Index DAT counts to their default game state.
                Index.UNSAFE_ResetAllIndexDatCounts();
                try
                {

                    // Validate that the indexes refer only to base game indexes.
                    try
                    {
                        await AssertIndexIsClean(XivDataFile._04_Chara);
                    }
                    catch
                    {
                        throw new InvalidDataException("Cannot create index backups.  Indexes are unclean and still refer to modified dats even after disabling all mods.");
                    }



                    ClearIndexBackups(backupDirectory);

                    foreach (XivDataFile df in Enum.GetValues(typeof(XivDataFile)))
                    {
                        var path = Path.Combine(backupDirectory, XivDataFiles.GetFilePath(df));
                        var folder = new DirectoryInfo(path).Parent.FullName;

                        Directory.CreateDirectory(folder);


                        var index1path = XivDataFiles.GetFullPath(df, Index.IndexExtension);
                        var index2path = XivDataFiles.GetFullPath(df, Index.Index2Extension);
                        var index1backup = path + Index.IndexExtension;
                        var index2backup = path + Index.Index2Extension;

                        File.Copy(index1path, index1backup);
                        File.Copy(index2path, index2backup);
                    }


                    var versionFile = XivCache.GameInfo.GameVersionFile;
                    if (!string.IsNullOrWhiteSpace(versionFile))
                    {
                        var fName = Path.GetFileName(versionFile);
                        var versionTarget = Path.Combine(backupDirectory, fName);
                        File.Copy(versionFile, versionTarget);
                    }
                }
                finally
                {
                    // Return DAT counts to normal.
                    Index.UNSAFE_NormalizeAllIndexDatCounts();

                    if (enabledMods.Count > 0)
                    {
                        // Re-Enable mods.
                        using (var tx = ModTransaction.BeginTransaction(true))
                        {
                            var paths = enabledMods.Select(x => x.FilePath);
                            await Modding.SetModStates(EModState.Enabled, paths, null, tx);
                            await ModTransaction.CommitTransaction(tx);
                        }
                    }
                }
            });
        }

        public static void AssertIndexIsClean(string index2Path, XivDataFile datafile)
        {
            var offsets = IndexFile.GetOffsetsFromRawIndex2File(index2Path);
            var originalList = Dat.GetOriginalDatList(datafile);
            var maxSafeDat = originalList.Count;

            foreach (var offset in offsets)
            {
                var parts = IOUtil.Offset8xToParts(offset);

                if (parts.DatNum > maxSafeDat)
                {
                    throw new InvalidDataException("The given index references modded data files.");
                }
            }
        }

        public static async Task AssertIndexIsClean(XivDataFile dataFile)
        {
            var rtx = ModTransaction.BeginTransaction();
            var iFile = await rtx.GetIndexFile(XivDataFile._04_Chara);

            var offsets = iFile.GetAllIndex2Offsets();

            var originalList = Dat.GetOriginalDatList(dataFile);
            var maxSafeDat = originalList.Count;

            foreach(var offset in offsets)
            {
                var parts = IOUtil.Offset8xToParts(offset);

                if(parts.DatNum > maxSafeDat)
                {
                    throw new InvalidDataException("The given index references modded data files.");
                }
            }
        }

        public static async Task AssertAllIndexesAreClean()
        {
            var broken = new List<XivDataFile>();
            foreach (XivDataFile df in Enum.GetValues(typeof(XivDataFile)))
            {
                try
                {
                    await AssertIndexIsClean(df);
                }
                catch (Exception ex)
                {
                    broken.Add(df);
                }
            }
            if(broken.Count == 0)
            {
                return;
            }

            var mes = "The following Indexes have invalid offsets contained within them:\n\n";
            foreach(var df in broken)
            {
                mes += XivDataFiles.GetFilePath(df) + "\n";
            }
            throw new InvalidDataException(mes);
        }
    }
}