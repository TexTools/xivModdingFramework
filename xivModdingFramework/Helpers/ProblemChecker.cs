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

namespace xivModdingFramework.Helpers
{
    public class ProblemChecker
    {
        private readonly DirectoryInfo _gameDirectory;
        private readonly Index _index;
        private readonly Dat _dat;

        public ProblemChecker(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
            _index = new Index(_gameDirectory);
            _dat = new Dat(_gameDirectory);
        }

        /// <summary>
        /// Checks the index for the number of dats the game will attempt to read
        /// </summary>
        /// <returns>True if there is a problem, False otherwise</returns>
        public Task<bool> CheckIndexDatCounts(XivDataFile dataFile)
        {
            return Task.Run(() =>
            {
                var indexDatCounts = _index.GetIndexDatCount(dataFile);
                var largestDatNum = _dat.GetLargestDatNumber(dataFile) + 1;

                if (indexDatCounts.Index1 != largestDatNum)
                {
                    return true;
                }

                if (indexDatCounts.Index2 != largestDatNum)
                {
                    return true;
                }

                return false;
            });
        }

        /// <summary>
        /// Checks the index for any empty dat files
        /// </summary>
        /// <returns>A list of dats which are empty if any</returns>
        public Task<List<int>> CheckForEmptyDatFiles(XivDataFile dataFile)
        {
            return Task.Run(() =>
            {
                var largestDatNum = _dat.GetLargestDatNumber(dataFile) + 1;
                var emptyList = new List<int>();

                for (var i = 0; i < largestDatNum; i++)
                {
                    var fileInfo = new FileInfo(Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}.win32.dat{i}"));

                    if (fileInfo.Length == 0)
                    {
                        emptyList.Add(i);
                    }
                }

                return emptyList;
            });
        }

        /// <summary>
        /// Checks the index for the number of dats the game will attempt to read
        /// </summary>
        /// <returns>True if there is a problem, False otherwise</returns>
        public Task<bool> CheckForLargeDats(XivDataFile dataFile)
        {
            return Task.Run(() =>
            {
                var largestDatNum = _dat.GetLargestDatNumber(dataFile) + 1;

                var fileSizeList = new List<long>();

                for (var i = 0; i < largestDatNum; i++)
                {
                    var fileInfo = new FileInfo(Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}.win32.dat{i}"));

                    try
                    {
                        fileSizeList.Add(fileInfo.Length);
                    }
                    catch
                    {
                        return true;
                    }
                }

                if (largestDatNum > 8 || fileSizeList.FindAll(x => x.Equals(2048)).Count > 1)
                {
                    return true;
                }

                return false;
            });
        }

        /// <summary>
        /// Repairs the dat count in the index files
        /// </summary>
        public Task RepairIndexDatCounts(XivDataFile dataFile)
        {
            return Task.Run(() =>
            {
                var largestDatNum = _dat.GetLargestDatNumber(dataFile);

                _index.UpdateIndexDatCount(dataFile, largestDatNum);
            });
        }

        /// <summary>
        /// This function returns TRUE if the backups should be used, and FALSE if they should not.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="backupsDirectory"></param>
        /// <returns></returns>
        public Task<bool> CheckForOutdatedBackups(XivDataFile dataFile, DirectoryInfo backupsDirectory)
        {
            return Task.Run(() =>
            {
                var backupDataFile =
                    new DirectoryInfo(Path.Combine(backupsDirectory.FullName, $"{dataFile.GetDataFileName()}.win32.index"));
                var currentDataFile =
                    new DirectoryInfo(Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}.win32.index"));

                // Since the material addition directly adds to section 1 we can no longer check for outdated using that section header
                // so instead compare the hashes of sections 2 and 3
                byte[] currentHashSection2;
                byte[] currentHashSection3;
                byte[] backupHashSection2;
                byte[] backupHashSection3;
                try
                {
                    currentHashSection2 = _index.GetIndexSection2Hash(currentDataFile);
                    currentHashSection3 = _index.GetIndexSection3Hash(currentDataFile);
                }
                catch
                {
                    // Base files are fucked, use *any* backups.
                    return true;
                }

                try
                {
                    backupHashSection2 = _index.GetIndexSection2Hash(backupDataFile);
                    backupHashSection3 = _index.GetIndexSection3Hash(backupDataFile);
                }
                catch
                {
                    // Backups are fucked, can't use those.
                    return false;
                }


                return backupHashSection2.SequenceEqual(currentHashSection2) && backupHashSection3.SequenceEqual(currentHashSection3);
            });
        }

        public Task PerformStartOver(DirectoryInfo backupsDirectory, IProgress<string> progress = null, XivLanguage language = XivLanguage.None)
        {
            return Task.Run(async () =>
            {
                var modding = new Modding(_gameDirectory);
                var backupsRestored = false;

                // Stop the cache worker since we're blowing up the entire index file and db anyways.
                // The cache rebuild will start it up again after the cache is rebuilt.
                XivCache.CacheWorkerEnabled = false;

                try
                {
                    // Try restoring the indexes FIRST.
                    backupsRestored = await RestoreBackups(backupsDirectory);
                    progress?.Report("Restoring index file backups...");

                    if (!backupsRestored)
                    {
                        throw new Exception("Start Over Failed: Index backups missing/outdated.");
                    }
                }
                catch(Exception ex)
                {
                    try
                    {
                        // If the index restore failed, try just disabling.
                        await modding.DeleteAllFilesAddedByTexTools();
                        await modding.ToggleAllMods(false);
                        progress?.Report("Index restore failed, attempting to delete all mods instead...");
                    } catch
                    {
                        throw new Exception("Start Over Failed: Index Backups Invalid and Unable to Disable all mods.");
                    }
                }
                finally
                {
                    progress?.Report("Deleting modded dat files...");

                    var dat = new Dat(_gameDirectory);

                    // Delete modded dat files
                    foreach (var xivDataFile in (XivDataFile[])Enum.GetValues(typeof(XivDataFile)))
                    {
                        var datFiles = await dat.GetModdedDatList(xivDataFile);

                        foreach (var datFile in datFiles)
                        {
                            File.Delete(datFile);
                        }

                        if (datFiles.Count > 0)
                        {
                            await RepairIndexDatCounts(xivDataFile);
                        }
                    }

                    progress?.Report("Cleaning up mod list...");

                    var modListDirectory = new DirectoryInfo(Path.Combine(_gameDirectory.Parent.Parent.FullName, XivStrings.ModlistFilePath));

                    // Delete mod list
                    File.Delete(modListDirectory.FullName);

                    modding.CreateModlist();

                    progress?.Report("Rebuilding Cache...");

                    await Task.Run(async () =>
                    {
                        XivCache.RebuildCache(XivCache.CacheVersion);
                    });
                }
            });
        }

        public Task BackupIndexFiles(DirectoryInfo backupsDirectory)
        {
            return Task.Run(async () => {
                var indexFiles = new XivDataFile[] { XivDataFile._0A_Exd, XivDataFile._04_Chara, XivDataFile._06_Ui, XivDataFile._01_Bgcommon };
                var index = new Index(_gameDirectory);
                var modding = new Modding(_gameDirectory);

                if (index.IsIndexLocked(XivDataFile._0A_Exd))
                {
                    throw new Exception("Index files are in use by another process.");
                }

                try
                {
                    // Toggle off all mods
                    await modding.ToggleAllMods(false);
                }
                catch
                {
                    throw new Exception("Failed to disable mods.\n\n" +
                        "Please check For problems by selecting Help -> Check For Problems");
                }


                var originalFiles = Directory.GetFiles(_gameDirectory.FullName);
                foreach (var originalFile in originalFiles)
                {
                    try
                    {
                        if (originalFile.Contains(".win32.index"))
                        {
                            File.Copy(originalFile, $"{backupsDirectory}/{Path.GetFileName(originalFile)}", true);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Failed to copy index files.\n\n" + ex.Message);
                    }
                }
            });
        }

        public Task<bool> RestoreBackups(DirectoryInfo backupsDirectory)
        {
            return Task.Run(async () =>
            {
                var backupFiles = Directory.GetFiles(backupsDirectory.FullName);
                var filesToCheck = new XivDataFile[] { XivDataFile._0A_Exd, XivDataFile._04_Chara, XivDataFile._06_Ui, XivDataFile._01_Bgcommon };
                var outdated = false;

                foreach (var xivDataFile in filesToCheck)
                {
                    var backupFile = new DirectoryInfo($"{backupsDirectory.FullName}\\{xivDataFile.GetDataFileName()}.win32.index");

                    if (!File.Exists(backupFile.FullName)) continue;

                    try
                    {
                        var outdatedCheck = await CheckForOutdatedBackups(xivDataFile, backupsDirectory);

                        if (!outdatedCheck)
                        {
                            outdated = true;
                        }
                    }
                    catch { 
                        // If the outdated check errored out, we likely have completely broken internal dat files.
                        // ( Either deleted or 0 byte files ), so replacing them with *anything* is an improvement.
                    }
                }

                var _index = new Index(_gameDirectory);
                // Make sure backups exist and are up to date unless called with forceRestore true
                if (backupFiles.Length != 0 && !outdated)
                {
                    // Copy backups to ffxiv folder
                    foreach (var backupFile in backupFiles)
                    {
                        if (backupFile.Contains(".win32.index"))
                        {
                            File.Copy(backupFile, $"{_gameDirectory}/{Path.GetFileName(backupFile)}", true);
                        }
                    }

                    // Update all the index counts to be safe, in case the user's index backups were generated when some mod dats existed.
                    _index.UpdateAllIndexDatCounts();
                    return true;
                }
                return false;
            });            
        }
    }
}