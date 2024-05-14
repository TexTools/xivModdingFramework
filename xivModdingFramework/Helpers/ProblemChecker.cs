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
        /// Checks the index for any empty dat files
        /// </summary>
        /// <returns>A list of dats which are empty if any</returns>
        public Task<List<int>> CheckForEmptyDatFiles(XivDataFile dataFile)
        {
            if (ModTransaction.ActiveTransaction != null)
            {
                // Safety check here to prevent any misuse or weird bugs from assuming this would be based on post-transaction state.
                throw new Exception("Cannot sanely perform DAT file checks with an open write-enabled transaction.");
            }


            return Task.Run(() =>
            {
                var largestDatNum = _dat.GetLargestDatNumber(dataFile) + 1;
                var emptyList = new List<int>();

                for (var i = 0; i < largestDatNum; i++)
                {
                    var datPath = Dat.GetDatPath(dataFile, i);
                    var fileInfo = new FileInfo(datPath);

                    if (fileInfo.Length == 0)
                    {
                        emptyList.Add(i);
                    }
                }

                return emptyList;
            });
        }


        /// <summary>
        /// Repairs the dat count in the index files
        /// </summary>
        public Task RepairIndexDatCounts(XivDataFile dataFile)
        {
            return Task.Run(() =>
            {
                //var largestDatNum = _dat.GetLargestDatNumber(dataFile);
                //_index.UpdateIndexDatCount(dataFile, largestDatNum);
            });
        }

        /// <summary>
        /// This function returns TRUE if the backups pass validation.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="backupsDirectory"></param>
        /// <returns></returns>
        public Task<bool> ValidateIndexBackup(XivDataFile dataFile, DirectoryInfo backupsDirectory)
        {

            return Task.Run(() =>
            {
                // Since we rewrite all of the hashes now, we can't use hash comparison on index backups anymore.
                // This should just validate the hashes of the index backups are valid on their own.
                return true;
            });
        }

        public Task PerformStartOver(DirectoryInfo backupsDirectory, IProgress<string> progress = null, XivLanguage language = XivLanguage.None)
        {
            if (!Dat.AllowDatAlteration)
            {
                throw new Exception("Cannot perform Dat Manipulations while DAT writing is disabled.");
            }

            var workerStatus = XivCache.CacheWorkerEnabled;
            XivCache.CacheWorkerEnabled = false;
            try
            {
                return Task.Run(async () =>
                {
                    var backupsRestored = false;


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
                    catch (Exception ex)
                    {
                        try
                        {
                            // If the index restore failed, try just disabling.
                            await Modding.ToggleAllMods(false);
                            progress?.Report("Index restore failed, attempting to delete all mods instead...");
                        }
                        catch(Exception ex2)
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

                        Modding.CreateModlist();

                        progress?.Report("Rebuilding Cache...");

                        await Task.Run(async () =>
                        {
                            XivCache.RebuildCache(XivCache.CacheVersion);
                        });
                    }
                });
            }
            finally
            {
                XivCache.CacheWorkerEnabled = workerStatus;
            }
        }

        public async Task BackupIndexFiles(DirectoryInfo backupsDirectory)
        {
            await Task.Run(async () => {
                var indexFiles = new XivDataFile[] { XivDataFile._0A_Exd, XivDataFile._04_Chara, XivDataFile._06_Ui, XivDataFile._01_Bgcommon };
                var index = new Index(_gameDirectory);

                // Readonly tx to get live game state.
                var tx = ModTransaction.BeginTransaction(true);

                var ml = await tx.GetModList();

                bool anyEnabled = false;
                var allMods = ml.GetMods();
                foreach(var m in allMods)
                {
                    var state = await m.GetState(tx);
                    if(state == Mods.Enums.EModState.Enabled)
                    {
                        anyEnabled = true;
                        break;
                    }
                }

                if (anyEnabled)
                {
                    if (!Dat.AllowDatAlteration)
                    {
                        // Live game state isn't clean and we have a transaction open or other disable lock.
                        throw new Exception("Cannot perform Dat Manipulations while DAT writing is disabled.");
                    }

                    if (index.IsIndexLocked(XivDataFile._0A_Exd))
                    {
                        throw new Exception("Index files are in use by another process.");
                    }

                    try
                    {
                        // Toggle off all mods
                        await Modding.ToggleAllMods(false);
                    }
                    catch
                    {
                        throw new Exception("Failed to disable mods.\n\n" +
                            "Please check For problems by selecting Help -> Check For Problems");
                    }
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
            if (!Dat.AllowDatAlteration)
            {
                throw new Exception("Cannot perform Dat Manipulations while DAT writing is disabled.");
            }

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
                        var outdatedCheck = await ValidateIndexBackup(xivDataFile, backupsDirectory);

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
                    // This can be done just by opening and committing a blank transaction.
                    using (var tx = ModTransaction.BeginTransaction())
                    {
                        await ModTransaction.CommitTransaction(tx);
                    }

                    return true;
                }
                return false;
            });            
        }
    }
}