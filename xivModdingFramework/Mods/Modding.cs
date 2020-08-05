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
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.Enums;
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

        // Caching information for modlists.
        private static Dictionary<string, DateTime> _cachedModlistsModTime = new Dictionary<string, DateTime>();
        private static Dictionary<string, ModList> _cachedModlists = new Dictionary<string, ModList>();
        public ModList GetModList()
        {
            ModList val;
            _modlistSemaphore.Wait();
            try
            {
                var modTime = File.GetLastWriteTime(ModListDirectory.FullName);
                if (_cachedModlists.ContainsKey(ModListDirectory.FullName) && modTime <= _cachedModlistsModTime[ModListDirectory.FullName])
                {
                    // We have a cached file, and the modList file has not been touched since we cached it.
                    // Return the cached copy (Skip full read & Json deserialize)
                    val = _cachedModlists[ModListDirectory.FullName];
                }
                else
                {
                    // Cache was either stale or missing, load the file from disk and update cache.
                    val = JsonConvert.DeserializeObject<ModList>(File.ReadAllText(ModListDirectory.FullName));
                    modTime = File.GetLastWriteTime(ModListDirectory.FullName);

                    _cachedModlists[ModListDirectory.FullName] = val;
                    _cachedModlistsModTime[ModListDirectory.FullName] = modTime;

                }
            } finally {
                _modlistSemaphore.Release();
            }
            return val;
        }
        public async Task<ModList> GetModListAsync()
        {
            ModList val;
            await _modlistSemaphore.WaitAsync();
            try
            {
                var modTime = File.GetLastWriteTime(ModListDirectory.FullName);
                if (_cachedModlists.ContainsKey(ModListDirectory.FullName) && modTime <= _cachedModlistsModTime[ModListDirectory.FullName])
                {
                    // We have a cached file, and the modList file has not been touched since we cached it.
                    // Return the cached copy (Skip full read & Json deserialize)
                    val = _cachedModlists[ModListDirectory.FullName];
                }
                else
                {
                    // Cache was either stale or missing, load the file from disk and update cache.
                    val = JsonConvert.DeserializeObject<ModList>(File.ReadAllText(ModListDirectory.FullName));
                    modTime = File.GetLastWriteTime(ModListDirectory.FullName);

                    _cachedModlists[ModListDirectory.FullName] = val;
                    _cachedModlistsModTime[ModListDirectory.FullName] = modTime;

                }
            }
            finally
            {
                _modlistSemaphore.Release();
            }
            return val;
        }

        public void SaveModList(ModList ml)
        {
            _modlistSemaphore.Wait();
            File.WriteAllText(ModListDirectory.FullName, JsonConvert.SerializeObject(ml, Formatting.Indented));
            _modlistSemaphore.Release();
        }
        public void SaveModListAsync(ModList ml)
        {
            _modlistSemaphore.WaitAsync();
            File.WriteAllText(ModListDirectory.FullName, JsonConvert.SerializeObject(ml, Formatting.Indented));
            _modlistSemaphore.Release();
        }

        public async Task DeleteAllFilesAddedByTexTools()
        {
            var modList = GetModList();
            var modsToRemove = modList.Mods.Where(it => it.source == "FilesAddedByTexTools");
            foreach(var mod in modsToRemove)
            {
                await DeleteMod(mod.fullPath);
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
        public async Task ToggleModStatus(string internalFilePath, bool enable)
        {
            var index = new Index(_gameDirectory);

            if (string.IsNullOrEmpty(internalFilePath))
            {
                throw new Exception("File Path missing, unable to toggle mod.");
            }

            var modEntry = await TryGetModEntry(internalFilePath);

            if (modEntry == null)
            {
                throw new Exception("Unable to find mod entry in modlist.");
            }

            // Matadd textures have the same mod offset as original so nothing to toggle
            if (modEntry.data.originalOffset == modEntry.data.modOffset)
            {
                // Added file.
                if (enable && !modEntry.enabled)
                {
                    await index.AddFileDescriptor(modEntry.fullPath, modEntry.data.modOffset, IOUtil.GetDataFileFromPath(modEntry.fullPath));
                }
                else if (!enable && modEntry.enabled)
                {
                    await index.DeleteFileDescriptor(modEntry.fullPath, IOUtil.GetDataFileFromPath(modEntry.fullPath));
                }

            }
            else
            { 
                // Standard mod (file replacement)
                if (enable)
                {
                    await index.UpdateDataOffset(modEntry.data.modOffset, internalFilePath);
                }
                else
                {
                    await index.UpdateDataOffset(modEntry.data.originalOffset, internalFilePath);
                }
            }

            var modListDirectory = new DirectoryInfo(Path.Combine(_gameDirectory.Parent.Parent.FullName, XivStrings.ModlistFilePath));

            var modList = GetModList();

            var entryEnableUpdate = (from entry in modList.Mods
                where entry.fullPath.Equals(modEntry.fullPath)
                select entry).FirstOrDefault();

            entryEnableUpdate.enabled = enable;

            SaveModList(modList);
        }

        /// <summary>
        /// Toggles the mod on or off
        /// </summary>
        /// <param name="internalFilePath">The internal file path of the mod</param>
        /// <param name="enable">The status of the mod</param>
        public async Task ToggleModPackStatus(string modPackName, bool enable)
        {
            var index = new Index(_gameDirectory);

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
                if (modEntry.name.Equals(string.Empty)) continue;
                // Matadd textures have the same mod offset as original so nothing to toggle
                if (modEntry.data.originalOffset == modEntry.data.modOffset)
                {
                    // Added file.
                    if (enable && !modEntry.enabled)
                    {
                        await index.AddFileDescriptor(modEntry.fullPath, modEntry.data.modOffset, IOUtil.GetDataFileFromPath(modEntry.fullPath));
                    }
                    else if (!enable && modEntry.enabled)
                    {
                        await index.DeleteFileDescriptor(modEntry.fullPath, IOUtil.GetDataFileFromPath(modEntry.fullPath));
                    }

                }
                else
                {
                    // Standard file (file replacement)
                    if (enable)
                    {
                        await index.UpdateDataOffset(modEntry.data.modOffset, modEntry.fullPath);
                        modEntry.enabled = true;
                    }
                    else
                    {
                        await index.UpdateDataOffset(modEntry.data.originalOffset, modEntry.fullPath);
                        modEntry.enabled = false;
                    }
                }
            }

            SaveModList(modList);
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

            var modNum = 0;
            foreach (var modEntry in modList.Mods)
            {
                if(string.IsNullOrEmpty(modEntry.name)) continue;
                if(string.IsNullOrEmpty(modEntry.fullPath)) continue;
                if (modEntry.data.modOffset == modEntry.data.originalOffset)
                {
                    // Added file.
                    if (enable && !modEntry.enabled)
                    {
                        await index.AddFileDescriptor(modEntry.fullPath, modEntry.data.modOffset, IOUtil.GetDataFileFromPath(modEntry.fullPath));
                        modEntry.enabled = true;
                    }
                    else if (!enable && modEntry.enabled)
                    {
                        await index.DeleteFileDescriptor(modEntry.fullPath, IOUtil.GetDataFileFromPath(modEntry.fullPath));
                        modEntry.enabled = false;
                    }

                }
                else
                {
                    // Standard mod.
                    if (enable && !modEntry.enabled)
                    {
                        await index.UpdateDataOffset(modEntry.data.modOffset, modEntry.fullPath);
                        modEntry.enabled = true;
                    }
                    else if (!enable && modEntry.enabled)
                    {
                        await index.UpdateDataOffset(modEntry.data.originalOffset, modEntry.fullPath);
                        modEntry.enabled = false;
                    }
                }

                progress?.Report((++modNum, modList.Mods.Count, string.Empty));
            }

            SaveModList(modList);
        }

        /// <summary>
        /// Disables all mods from older modlist
        /// </summary>
        public async Task DisableOldModList(DirectoryInfo oldModListDirectory)
        {
            var index = new Index(_gameDirectory);

            using (var sr = new StreamReader(oldModListDirectory.FullName))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    var modEntry = JsonConvert.DeserializeObject<OriginalModList>(line);

                    if (!string.IsNullOrEmpty(modEntry.fullPath))
                    {
                        try
                        {
                            await index.UpdateDataOffset(modEntry.originalOffset, modEntry.fullPath);
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"Unable to disable {modEntry.name} | {modEntry.fullPath}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Deletes a mod from the modlist
        /// </summary>
        /// <param name="modItemPath">The mod item path of the mod to delete</param>
        public async Task DeleteMod(string modItemPath)
        {
            var modList = GetModList();

            var modToRemove = (from mod in modList.Mods
                where mod.fullPath.Equals(modItemPath)
                select mod).FirstOrDefault();
            if (modToRemove.source == "FilesAddedByTexTools")
            {
                var index = new Index(_gameDirectory);
                var success = await index.DeleteFileDescriptor(modItemPath, XivDataFiles.GetXivDataFile(modToRemove.datFile));
                if(!success)
                {
                    throw new Exception("Failed to delete file descriptor.");
                }
                success = await index.DeleteFileDescriptor($"{modItemPath}.flag", XivDataFiles.GetXivDataFile(modToRemove.datFile));
                if (!success)
                {
                    throw new Exception("Failed to delete file descriptor.");
                }
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

            modList.emptyCount += 1;
            modList.modCount -= 1;


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

            modList.ModPacks.Remove(modPackItem);

            var modsToRemove = (from mod in modList.Mods
                where mod.modPack != null && mod.modPack.name.Equals(modPackName)
                select mod).ToList();

            var modRemoveCount = modsToRemove.Count;

            foreach (var modToRemove in modsToRemove)
            {
                if (modToRemove.source == "FilesAddedByTexTools")
                {
                    var index = new Index(_gameDirectory);
                    var success = await index.DeleteFileDescriptor(modToRemove.fullPath, XivDataFiles.GetXivDataFile(modToRemove.datFile));
                    if (!success)
                    {
                        throw new Exception("Failed to delete file descriptor.");
                    }
                    success = await index.DeleteFileDescriptor($"{modToRemove.fullPath}.flag", XivDataFiles.GetXivDataFile(modToRemove.datFile));
                    if (!success)
                    {
                        throw new Exception("Failed to delete file descriptor.");
                    }
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
        }
    }
}