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

using Newtonsoft.Json;
using SharpDX.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.Enums;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.Mods
{
    /// <summary>
    /// This class contains the methods that deal with the .modlist file
    /// </summary>
    public class Modding
    {
        private readonly DirectoryInfo _gameDirectory;
        private readonly Version _modlistVersion = new Version(1, 0);
        private static SemaphoreSlim _modlistSemaphore = new SemaphoreSlim(1);

        public DirectoryInfo ModListDirectory { get; }

        /// <summary>
        /// Sets the modlist with a provided name
        /// </summary>
        /// <param name="modlistDirectory">The directory in which to place the Modlist</param>
        /// <param name="modListName">The name to give the modlist file</param>
        public Modding(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
            ModListDirectory = new DirectoryInfo(Path.Combine(_gameDirectory.Parent.Parent.FullName, XivStrings.ModlistFilePath));

        }

        public ModList GetModList()
        {
            ModList val = null;
            _modlistSemaphore.Wait();
            try
            {
                var modlistText = File.ReadAllText(ModListDirectory.FullName);
                val = JsonConvert.DeserializeObject<ModList>(modlistText);
            }
            finally
            {
                _modlistSemaphore.Release();
            }

            if(val == null)
            {
                throw new InvalidOperationException("GetModlist returned NULL Mod List.");
            }

            return val;
        }
        public async Task<ModList> GetModListAsync()
        {
            ModList val = null;
            await _modlistSemaphore.WaitAsync();
            try
            {
                var modlistText = File.ReadAllText(ModListDirectory.FullName);
                val = JsonConvert.DeserializeObject<ModList>(modlistText);
            }
            finally
            {
                _modlistSemaphore.Release();
            }

            if (val == null)
            {
                throw new InvalidOperationException("GetModlist returned NULL Mod List.");
            }

            return val;
        }

        public void SaveModList(ModList ml)
        {
            _modlistSemaphore.Wait();
            File.WriteAllText(ModListDirectory.FullName, JsonConvert.SerializeObject(ml, Formatting.Indented));
            _modlistSemaphore.Release();
        }
        public async Task SaveModListAsync(ModList ml)
        {
            _modlistSemaphore.WaitAsync();
            File.WriteAllText(ModListDirectory.FullName, JsonConvert.SerializeObject(ml, Formatting.Indented));
            _modlistSemaphore.Release();
        }

        public async Task DeleteAllFilesAddedByTexTools()
        {
            var modList = GetModList();
            var modsToRemove = modList.Mods.Where(x => x.data.modOffset == x.data.originalOffset);

            // Delete user files first.
            foreach(var mod in modsToRemove)
            {
                if (mod.IsInternal()) continue;
                await DeleteMod(mod.fullPath);
            }

            // Then delete system files.
            foreach (var mod in modsToRemove)
            {
                if (mod.IsInternal()) continue;
                await DeleteMod(mod.fullPath, true);
            }
        }
        /// <summary>
        /// Creates the Mod List that is used to keep track of mods.
        /// </summary>
        public void CreateModlist()
        {
            if (File.Exists(ModListDirectory.FullName))
            {
                return;
            }

            var modList = new ModList
            {
                version = _modlistVersion.ToString(),
                modCount = 0,
                modPackCount = 0,
                emptyCount = 0,
                ModPacks = new List<ModPack>(),
                Mods = new List<Mod>()
            };

            SaveModList(modList);
        }

        /// <summary>
        /// Tries to get the mod entry for the given internal file path, return null otherwise
        /// </summary>
        /// <param name="internalFilePath">The internal file path to find</param>
        /// <returns>The mod entry if found, null otherwise</returns>
        public Task<Mod> TryGetModEntry(string internalFilePath)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(internalFilePath))
                {
                    return null;
                }

                return Task.Run(() =>
                {
                    internalFilePath = internalFilePath.Replace("\\", "/");

                    var modList = GetModList();

                    if (modList == null) return null;

                    foreach (var modEntry in modList.Mods)
                    {
                        if (modEntry.fullPath.Equals(internalFilePath))
                        {
                            return modEntry;
                        }
                    }

                    return null;
                });
            } catch(Exception ex)
            {
                return null;
            }
        }

        /// <summary>
        /// Checks to see whether the mod is currently enabled
        /// </summary>
        /// <param name="internalPath">The internal path of the file</param>
        /// <param name="dataFile">The data file to check in</param>
        /// <param name="indexCheck">Flag to determine whether to check the index file or just the modlist</param>
        /// <returns></returns>
        public async Task<XivModStatus> IsModEnabled(string internalPath, bool indexCheck)
        {
            if (!File.Exists(ModListDirectory.FullName))
            {
                return XivModStatus.Original;
            }

            if (indexCheck)
            {
                var index = new Index(_gameDirectory);

                var modEntry = await TryGetModEntry(internalPath);

                if (modEntry == null)
                {
                    return XivModStatus.Original;
                }

                var originalOffset = modEntry.data.originalOffset;
                var moddedOffset = modEntry.data.modOffset;

                var offset = await index.GetDataOffset(
                    HashGenerator.GetHash(Path.GetDirectoryName(internalPath).Replace("\\", "/")),
                    HashGenerator.GetHash(Path.GetFileName(internalPath)),
                    XivDataFiles.GetXivDataFile(modEntry.datFile));

                if (offset.Equals(originalOffset))
                {
                    return XivModStatus.Disabled;
                }

                if (offset.Equals(moddedOffset))
                {
                    return XivModStatus.Enabled;
                }

                throw new Exception("Offset in Index does not match either original or modded offset in modlist.");
            }
            else
            {
                var modEntry = await TryGetModEntry(internalPath);

                if (modEntry == null)
                {
                    return XivModStatus.Original;
                }

                return modEntry.enabled ? XivModStatus.Enabled : XivModStatus.Disabled;
            }
        }

        /// <summary>
        /// Toggles the mod on or off
        /// </summary>
        /// <param name="internalFilePath">The internal file path of the mod</param>
        /// <param name="enable">The status of the mod</param>
        public async Task<bool> ToggleModStatus(string internalFilePath, bool enable)
        {
            var index = new Index(_gameDirectory);

            if (string.IsNullOrEmpty(internalFilePath))
            {
                throw new Exception("File Path missing, unable to toggle mod.");
            }
            var modList = GetModList();

            var modEntry = modList.Mods.FirstOrDefault(x => x.fullPath == internalFilePath);

            var result = await ToggleModUnsafe(enable, modEntry, false, true);
            if(!result)
            {
                return result;
            }

            var modListDirectory = new DirectoryInfo(Path.Combine(_gameDirectory.Parent.Parent.FullName, XivStrings.ModlistFilePath));


            SaveModList(modList);

            return result;
        }

        /// <summary>
        /// Toggles the mod on or off
        /// </summary>
        /// <param name="internalFilePath">The internal file path of the mod</param>
        /// <param name="enable">The status of the mod</param>
        public async Task ToggleModPackStatus(string modPackName, bool enable)
        {
            var index = new Index(_gameDirectory);

            var workerState = XivCache.CacheWorkerEnabled;
            XivCache.CacheWorkerEnabled = false;
            try
            {
                var modList = GetModList();
                var modListDirectory = new DirectoryInfo(Path.Combine(_gameDirectory.Parent.Parent.FullName, XivStrings.ModlistFilePath));
                List<Mod> mods = null;

                if (modPackName.Equals("Standalone (Non-ModPack)"))
                {
                    mods = (from mod in modList.Mods
                            where mod.modPack == null
                            select mod).ToList();
                }
                else
                {
                    mods = (from mod in modList.Mods
                            where mod.modPack != null && mod.modPack.name.Equals(modPackName)
                            select mod).ToList();
                }


                if (mods == null)
                {
                    throw new Exception("Unable to find mods with given Mod Pack Name in modlist.");
                }

                foreach (var modEntry in mods)
                {
                    await ToggleModUnsafe(enable, modEntry, false, true);
                }

                SaveModList(modList);
            }
            finally
            {
                XivCache.CacheWorkerEnabled = workerState;
            }
        }

        /// <summary>
        /// Performs the most low-level mod enable/disable functions, without saving the modlist,
        /// ergo this should only be called by functions which will handle saving the modlist after
        /// they're done performing all modlist operations.
        /// </summary>
        /// <param name="enable"></param>
        /// <param name="mod"></param>
        /// <returns></returns>
        public async Task<bool> ToggleModUnsafe(bool enable, Mod mod, bool includeInternal, bool updateCache)
        {
            if (mod == null) return false;
            if (string.IsNullOrEmpty(mod.name)) return false;
            if (string.IsNullOrEmpty(mod.fullPath)) return false;

            if (mod.data.originalOffset <= 0 && !enable)
            {
                throw new Exception("Cannot disable mod with invalid original offset.");
            }

            if (enable && mod.data.modOffset <= 0)
            {
                throw new Exception("Cannot enable mod with invalid mod offset.");
            }
            
            if(mod.IsInternal() && !includeInternal)
            {
                // Don't allow toggling internal mods unless we were specifically told to.
                return false;
            }

            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);

            if (mod.IsCustomFile())
            {
                // Added file.
                if (enable && !mod.enabled)
                {
                    await index.AddFileDescriptor(mod.fullPath, mod.data.modOffset, IOUtil.GetDataFileFromPath(mod.fullPath), updateCache);
                    mod.enabled = true;

                    // Check if we're re-enabling a metadata mod.
                    var ext = Path.GetExtension(mod.fullPath);
                    if (ext == ".meta")
                    {
                        // Retreive the uncompressed meta entry we just enabled.
                        var data = await dat.GetType2Data(mod.fullPath, false);
                        var meta = await ItemMetadata.Deserialize(data);

                        meta.Validate(mod.fullPath);

                        // And write that metadata to the actual constituent files.
                        await ItemMetadata.ApplyMetadata(meta);
                    }
                }
                else if (!enable && mod.enabled)
                {

                    // Delete file descriptor handles removing metadata as needed on its own.
                    await index.DeleteFileDescriptor(mod.fullPath, IOUtil.GetDataFileFromPath(mod.fullPath), updateCache);
                    mod.enabled = false;
                }
                
            }
            else
            {
                // Standard mod.
                if (enable && !mod.enabled)
                {
                    await index.UpdateDataOffset(mod.data.modOffset, mod.fullPath, updateCache);
                    mod.enabled = true;
                }
                else if (!enable && mod.enabled)
                {
                    await index.UpdateDataOffset(mod.data.originalOffset, mod.fullPath, updateCache);
                    mod.enabled = false;
                }
            }
            return true;
        }

        /// <summary>
        /// Toggles all mods on or off
        /// </summary>
        /// <param name="enable">The status to switch the mods to True if enable False if disable</param>
        public async Task ToggleAllMods(bool enable, IProgress<(int current, int total, string message)> progress = null)
        {
            var index = new Index(_gameDirectory);

            var modList = GetModList();

            if (modList == null || modList.modCount == 0) return;

            // If we're doing a full disable, remove even internal subfiles so we don't 
            // potentially pollute our index backups.
            bool includeInternal = enable == false;


            var modNum = 0;
            foreach (var modEntry in modList.Mods)
            {
                // Save disabling these for last.
                if (modEntry.IsInternal()) continue;

                await ToggleModUnsafe(enable, modEntry, false, false);
                progress?.Report((++modNum, modList.Mods.Count, string.Empty));
            }

            if (includeInternal && !enable)
            {
                // Disable these last.
                var internalEntries = modList.Mods.Where(x => x.IsInternal());
                foreach (var modEntry in internalEntries)
                {
                    await ToggleModUnsafe(enable, modEntry, true, false);
                }
            }


            SaveModList(modList);

            if (includeInternal && !enable)
            {
                // Now go ahead and delete the internal files, to prevent them
                // being accidentally re-enabled (They should be re-built by the metadata file imports)
                var internalEntries = modList.Mods.Where(x => x.IsInternal());
                foreach (var modEntry in internalEntries)
                {
                    await DeleteMod(modEntry.fullPath, true);
                }
            }


            // Do these as a batch query at the end.
            progress?.Report((++modNum, modList.Mods.Count, "Adding modified files to Cache Queue..."));
            var allPaths = modList.Mods.Select(x => x.fullPath).Where(x => !String.IsNullOrEmpty(x)).ToList();
            XivCache.QueueDependencyUpdate(allPaths);
        }

        /// <summary>
        /// Purges any invalid empty mod blocks from the modlist.
        /// </summary>
        /// <returns></returns>
        public async Task<int> PurgeInvalidEmptyBlocks()
        {
            int removed = 0;
            var modList = await GetModListAsync();
            var emptyBlocks = modList.Mods.Where(x => x.name == string.Empty);
            var toRemove = emptyBlocks.Where(x => x.data.modOffset <= 0).ToList();

            foreach(var block in toRemove)
            {
                modList.Mods.Remove(block);
                removed++;
            }

            if (removed > 0)
            {
                SaveModList(modList);
            }
            return removed;
        } 

        /// <summary>
        /// Deletes a mod from the modlist
        /// </summary>
        /// <param name="modItemPath">The mod item path of the mod to delete</param>
        public async Task DeleteMod(string modItemPath, bool allowInternal = false)
        {
            var modList = GetModList();

            var modToRemove = (from mod in modList.Mods
                where mod.fullPath.Equals(modItemPath)
                select mod).FirstOrDefault();

            // Mod doesn't exist in the modlist.
            if (modToRemove == null) return;

            if(modToRemove.IsInternal() && !allowInternal)
            {
                throw new Exception("Cannot delete internal data without explicit toggle.");
            }

            if (modToRemove.IsCustomFile())
            {
                var index = new Index(_gameDirectory);
                await index.DeleteFileDescriptor(modItemPath, XivDataFiles.GetXivDataFile(modToRemove.datFile));
            }
            if (modToRemove.enabled)
            {
                await ToggleModStatus(modItemPath, false);
            }

            modToRemove.name = string.Empty;
            modToRemove.category = string.Empty;
            modToRemove.fullPath = string.Empty;
            modToRemove.source = string.Empty;
            modToRemove.modPack = null;
            modToRemove.enabled = false;
            modToRemove.data.originalOffset = 0;
            modToRemove.data.dataType = 0;

            modList.modCount -= 1;

            if(modToRemove.data.modOffset <= 0)
            {
                // Something was wrong with this mod frame.  Purge the entire thing from the list.
                modList.Mods.Remove(modToRemove);
            } else
            {
                modList.emptyCount += 1;
            }


            SaveModList(modList);
        }

        /// <summary>
        /// Deletes a Mod Pack and all its mods from the modlist
        /// </summary>
        /// <param name="modPackName">The name of the Mod Pack to be deleted</param>
        public async Task DeleteModPack(string modPackName)
        {
            var modList = GetModList();

            var modPackItem = (from modPack in modList.ModPacks
                where modPack.name.Equals(modPackName)
                select modPack).FirstOrDefault();

            // Modpack doesn't exist in the modlist.
            if (modPackItem == null) return;

            var cacheState = XivCache.CacheWorkerEnabled;
            XivCache.CacheWorkerEnabled = false;
            try
            {
                modList.ModPacks.Remove(modPackItem);

                var modsToRemove = (from mod in modList.Mods
                                    where mod.modPack != null && mod.modPack.name.Equals(modPackName)
                                    select mod).ToList();

                var modRemoveCount = modsToRemove.Count;

                foreach (var modToRemove in modsToRemove)
                {
                    if (modToRemove.data.originalOffset == modToRemove.data.modOffset)
                    {
                        var index = new Index(_gameDirectory);
                        await index.DeleteFileDescriptor(modToRemove.fullPath, XivDataFiles.GetXivDataFile(modToRemove.datFile));
                    }
                    if (modToRemove.enabled)
                    {
                        await ToggleModStatus(modToRemove.fullPath, false);
                    }

                    modToRemove.name = string.Empty;
                    modToRemove.category = string.Empty;
                    modToRemove.fullPath = string.Empty;
                    modToRemove.source = string.Empty;
                    modToRemove.modPack = null;
                    modToRemove.enabled = false;
                    modToRemove.data.originalOffset = 0;
                    modToRemove.data.dataType = 0;
                }

                modList.emptyCount += modRemoveCount;
                modList.modCount -= modRemoveCount;
                modList.modPackCount -= 1;

                SaveModList(modList);
            } finally
            {
                XivCache.CacheWorkerEnabled = cacheState;
            }
        }
    }
}