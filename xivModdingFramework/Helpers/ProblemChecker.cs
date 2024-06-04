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

namespace xivModdingFramework.Helpers
{
    public static class ProblemChecker
    {

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
            await Task.Run(() =>
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
                    await Task.Run(async () =>
                    {
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

            var currentVersion = XivCache.GameInfo.GameVersion;
            var backupFile = Path.Combine(backupDirectory, GameInfo.GameVersionFileName);
            var backupVersion = GameInfo.ReadVersionFile(backupFile);

            if(currentVersion != backupVersion)
            {
                throw new InvalidDataException("Index Backups FFXIV Version does not match current FFXIV Version");
            }


            var backups = GetAvailableIndexBackups(backupDirectory);
            if(backups.Count == 0)
            {
                throw new FileNotFoundException("No Index Backups found to restore.");
            }

            var basePath = XivCache.GameInfo.GameDirectory.Parent.FullName;
            foreach(var backup in backups)
            {
                var target = Path.GetFullPath(Path.Combine(basePath, new DirectoryInfo(backup).Parent.ToString(), Path.GetFileName(backup)));
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

    }
}