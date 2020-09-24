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
using SharpDX.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.Enums;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Variants.DataContainers;
using xivModdingFramework.Variants.FileTypes;

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
            await _modlistSemaphore.WaitAsync();
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

            await ToggleMods(enable, mods.Select(x => x.fullPath));
        }

        /// <summary>
        /// Performs the most low-level mod enable/disable functions, without saving the modlist,
        /// ergo this should only be called by functions which will handle saving the modlist after
        /// they're done performing all modlist operations.
        /// 
        /// If the Index and modlist are provided, the actions are only applied to those cached entries, rather
        /// than to the live files.
        /// </summary>
        /// <param name="enable"></param>
        /// <param name="mod"></param>
        /// <returns></returns>
        public async Task<bool> ToggleModUnsafe(bool enable, Mod mod, bool includeInternal, bool updateCache, IndexFile cachedIndex = null, ModList cachedModlist = null)
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

            // Added file.
            if (enable)
            {
                if (cachedIndex != null)
                {
                    cachedIndex.SetDataOffset(mod.fullPath, mod.data.modOffset);
                }
                else
                {
                    await index.UpdateDataOffset(mod.data.modOffset, mod.fullPath, false);
                }
                mod.enabled = true;

                if (cachedIndex == null)
                {
                    // Check if we're re-enabling a metadata mod.
                    var ext = Path.GetExtension(mod.fullPath);
                    if (ext == ".meta")
                    {
                        var df = IOUtil.GetDataFileFromPath(mod.fullPath);
                        // Retreive the uncompressed meta entry we just enabled.
                        var data = await dat.GetType2Data(mod.data.modOffset, df);
                        var meta = await ItemMetadata.Deserialize(data);

                        meta.Validate(mod.fullPath);

                        // And write that metadata to the actual constituent files.
                        await ItemMetadata.ApplyMetadata(meta, cachedIndex, cachedModlist);
                    } else if(ext == ".rgsp")
                    {
                        await CMP.ApplyRgspFile(mod.fullPath, cachedIndex, cachedModlist);
                    }
                }
            }
            else if (!enable)
            {
                if (mod.IsCustomFile())
                {
                    // Delete file descriptor handles removing metadata as needed on its own.
                    if (cachedIndex != null)
                    {
                        cachedIndex.SetDataOffset(mod.fullPath, 0);
                    }
                    else
                    {
                        await index.DeleteFileDescriptor(mod.fullPath, IOUtil.GetDataFileFromPath(mod.fullPath), false);
                    }
                } else
                {
                    if (cachedIndex != null)
                    {
                        cachedIndex.SetDataOffset(mod.fullPath, mod.data.originalOffset);
                    }
                    else
                    {
                        await index.UpdateDataOffset(mod.data.originalOffset, mod.fullPath, false);
                    }
                }
                mod.enabled = false;
            }

            if (updateCache)
            {
                XivCache.QueueDependencyUpdate(mod.fullPath);
            }

            return true;
        }

        /// <summary>
        /// Toggles all mods on or off
        /// </summary>
        /// <param name="enable">The status to switch the mods to True if enable False if disable</param>
        public async Task ToggleAllMods(bool enable, IProgress<(int current, int total, string message)> progress = null)
        {
            var modList = await GetModListAsync();
            await ToggleMods(enable, modList.Mods.Select(x => x.fullPath), progress);
        }

        public async Task ToggleMods(bool enable, IEnumerable<string> filePaths, IProgress<(int current, int total, string message)> progress = null)
        {
            var _index = new Index(_gameDirectory);

            var modList = await GetModListAsync();

            if (modList == null || modList.Mods.Count == 0) return;

            // Convert to hash set for speed when matching so we're not doing an O(n^2) check.
            var files = new HashSet<string>();
            foreach(var f in filePaths)
            {
                files.Add(f);
            }

            Dictionary<XivDataFile, IndexFile> indexFiles = new Dictionary<XivDataFile, IndexFile>();

            var modNum = 0;
            var mods = modList.Mods.Where(x => files.Contains(x.fullPath) && !String.IsNullOrWhiteSpace(x.fullPath)).ToList();

            if (mods.Count == 0) return;

            // If we're disabling all of our standard files, then this is a full toggle.
            var fullToggle = mods.Count >= modList.Mods.Count(x => !String.IsNullOrWhiteSpace(x.fullPath) && !x.IsInternal());

            // Pause cache worker
            var workerState = XivCache.CacheWorkerEnabled;
            XivCache.CacheWorkerEnabled = false;

            try
            {
                foreach (var modEntry in mods)
                {
                    // Save disabling these for last.
                    if (modEntry.IsInternal()) continue;

                    var df = IOUtil.GetDataFileFromPath(modEntry.fullPath);
                    if (!indexFiles.ContainsKey(df))
                    {
                        indexFiles.Add(df, await _index.GetIndexFile(df));
                    }


                    await ToggleModUnsafe(enable, modEntry, false, false, indexFiles[df], modList);
                    progress?.Report((++modNum, modList.Mods.Count, string.Empty));
                }

                if (fullToggle && !enable)
                {
                    // If we're doing a full mod toggle, we should purge our internal files as well.
                    var internalEntries = modList.Mods.Where(x => x.IsInternal()).ToList();
                    foreach (var modEntry in internalEntries)
                    {
                        var df = IOUtil.GetDataFileFromPath(modEntry.fullPath);
                        await ToggleModUnsafe(enable, modEntry, true, false, indexFiles[df], modList);
                        modList.Mods.Remove(modEntry);
                    }
                }
                else if(enable)
                {
                    progress?.Report((0, 0, "Expanding Metadata Entries..."));

                    // Batch and group apply the metadata entries.
                    var metadataEntries = mods.Where(x => x.fullPath.EndsWith(".meta")).ToList();
                    var _dat = new Dat(XivCache.GameInfo.GameDirectory);

                    Dictionary<XivDataFile, List<ItemMetadata>> metadata = new Dictionary<XivDataFile, List<ItemMetadata>>();
                    foreach (var mod in metadataEntries)
                    {
                        var df = IOUtil.GetDataFileFromPath(mod.fullPath);
                        var data = await _dat.GetType2Data(mod.data.modOffset, df);
                        var meta = await ItemMetadata.Deserialize(data);

                        meta.Validate(mod.fullPath);

                        if (!metadata.ContainsKey(df))
                        {
                            metadata.Add(df, new List<ItemMetadata>());
                        }
                        metadata[df].Add(meta);
                    }

                    foreach (var dkv in metadata)
                    {
                        var df = dkv.Key;
                        await ItemMetadata.ApplyMetadataBatched(dkv.Value, indexFiles[df], modList);
                    }

                    var rgspEntries = mods.Where(x => x.fullPath.EndsWith(".rgsp")).ToList();
                    foreach (var mod in rgspEntries)
                    {
                        await CMP.ApplyRgspFile(mod.fullPath, indexFiles[XivDataFile._04_Chara], modList);
                    }
                } else
                {
                    progress?.Report((0, 0, "Restoring Metadata Entries..."));
                    var metadataEntries = mods.Where(x => x.fullPath.EndsWith(".meta")).ToList();
                    var _dat = new Dat(XivCache.GameInfo.GameDirectory);

                    Dictionary<XivDataFile, List<ItemMetadata>> metadata = new Dictionary<XivDataFile, List<ItemMetadata>>();
                    foreach (var mod in metadataEntries)
                    {
                        var root = await XivCache.GetFirstRoot(mod.fullPath);
                        var df = IOUtil.GetDataFileFromPath(mod.fullPath);
                        var meta = await ItemMetadata.GetMetadata(root, true);
                        if (!metadata.ContainsKey(df))
                        {
                            metadata.Add(df, new List<ItemMetadata>());
                        }
                        metadata[df].Add(meta);
                    }

                    foreach (var dkv in metadata)
                    {
                        var df = dkv.Key;
                        await ItemMetadata.ApplyMetadataBatched(dkv.Value, indexFiles[df], modList);
                    }

                    var rgspEntries = mods.Where(x => x.fullPath.EndsWith(".rgsp")).ToList();
                    foreach (var mod in rgspEntries)
                    {
                        await CMP.RestoreDefaultScaling(mod.fullPath, indexFiles[XivDataFile._04_Chara], modList);
                    }

                }

                foreach (var kv in indexFiles)
                {
                    await _index.SaveIndexFile(kv.Value);
                }

                SaveModList(modList);

                // Do these as a batch query at the end.
                progress?.Report((++modNum, modList.Mods.Count, "Adding modified files to Cache Queue..."));
                var allPaths = mods.Select(x => x.fullPath).ToList();
                XivCache.QueueDependencyUpdate(allPaths);
            } finally
            {
                XivCache.CacheWorkerEnabled = workerState;
            }


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
        public async Task DeleteMod(string modItemPath, bool allowInternal = false, IndexFile index = null, ModList modList = null)
        {
            var doSave = false;
            var _index = new Index(_gameDirectory);
            if (modList == null)
            {
                doSave = true;
                modList = GetModList();
                index = await _index.GetIndexFile(IOUtil.GetDataFileFromPath(modItemPath));
            }

            var modToRemove = (from mod in modList.Mods
                where mod.fullPath.Equals(modItemPath)
                select mod).FirstOrDefault();

            // Mod doesn't exist in the modlist.
            if (modToRemove == null) return;

            if(modToRemove.IsInternal() && !allowInternal)
            {
                throw new Exception("Cannot delete internal data without explicit toggle.");
            }

            await ToggleModUnsafe(false, modToRemove, allowInternal, true, index, modList);


            // This is a metadata entry being deleted, we'll need to restore the metadata entries back to default.
            if (modToRemove.fullPath.EndsWith(".meta"))
            {
                var root = await XivCache.GetFirstRoot(modToRemove.fullPath);
                await ItemMetadata.RestoreDefaultMetadata(root, index, modList);
            }

            if (modToRemove.fullPath.EndsWith(".rgsp"))
            {
                await CMP.RestoreDefaultScaling(modToRemove.fullPath, index, modList);
            }

            modList.Mods.Remove(modToRemove);

            if (doSave)
            {
                await _index.SaveIndexFile(index);
                await SaveModListAsync(modList);
            }
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

            modList.ModPacks.Remove(modPackItem);

            var modsToRemove = (from mod in modList.Mods
                                where mod.modPack != null && mod.modPack.name.Equals(modPackName)
                                select mod).ToList();

            var modRemoveCount = modsToRemove.Count;

            // Disable all the mods.
            await ToggleMods(false, modsToRemove.Select(x => x.fullPath));

            // Then remove them from the modlist.
            foreach(var mod in modsToRemove)
            {
                modList.Mods.Remove(mod);
            }

            SaveModList(modList);
        }


        /// <summary>
        /// Cleans up the Modlist file, performing the following operations.
        /// 
        /// 1.  Fix up the Item Names and Categories of all mods to be consistent.
        /// 2.  Remove all empty mod slots.
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task CleanUpModlist(IProgress<(int Current, int Total, string Message)> progressReporter = null)
        {
            progressReporter?.Report((0, 0, "Loading Modlist file..."));
            var modlist = await GetModListAsync();

            var _imc = new Imc(XivCache.GameInfo.GameDirectory);

            // IMC entries cache so we're not having to constantly re-load the IMC data.
            Dictionary<XivDependencyRoot, List<XivImc>> ImcEntriesCache = new Dictionary<XivDependencyRoot, List<XivImc>>();

            var totalMods = modlist.Mods.Count;


            var count = 0;
            List<Mod> toRemove = new List<Mod>();
            foreach(var mod in modlist.Mods)
            {
                progressReporter?.Report((count, totalMods, "Fixing up item names..."));
                count++;

                if (String.IsNullOrWhiteSpace(mod.fullPath)) {
                    toRemove.Add(mod);
                    continue;
                }

                if (mod.IsInternal()) continue;

                XivDependencyRoot root = null;
                try
                {
                    root = await XivCache.GetFirstRoot(mod.fullPath);
                    if(root == null)
                    {
                        var cmpName = CMP.GetModFileNameFromRgspPath(mod.fullPath);
                        if (cmpName != null)
                        {
                            mod.name = cmpName;
                            mod.category = "Racial Scaling";
                        }
                        else if (mod.fullPath.StartsWith("ui/"))
                        {
                            mod.name = Path.GetFileName(mod.fullPath);
                            mod.category = "UI";
                        }
                        else
                        {
                            mod.name = Path.GetFileName(mod.fullPath);
                            mod.category = "Raw Files";
                        }
                        continue;
                    }

                    var ext = Path.GetExtension(mod.fullPath);
                    IItem item = null;
                    if (Imc.UsesImc(root) && ext == ".mtrl")
                    {
                        var data = await ResolveMtrlModInfo(_imc, mod.fullPath, root, ImcEntriesCache);
                        mod.name = data.Name;
                        mod.category = data.Category;

                    } else if(Imc.UsesImc(root) && ext == ".tex")
                    {
                        // For textures, get the first material using them, and list them under that material's item.
                        var parents = await XivCache.GetParentFiles(mod.fullPath);
                        if(parents.Count == 0)
                        {
                            item = root.GetFirstItem();
                            mod.name = item.GetModlistItemName();
                            mod.category = item.GetModlistItemCategory();
                            continue;
                        }

                        var parent = parents[0];

                        var data = await ResolveMtrlModInfo(_imc, parent, root, ImcEntriesCache);
                        mod.name = data.Name;
                        mod.category = data.Category;
                    }
                    else
                    {
                        item = root.GetFirstItem();
                        mod.name = item.GetModlistItemName();
                        mod.category = item.GetModlistItemCategory();
                        continue;
                    }
                } catch
                {
                    mod.name = Path.GetFileName(mod.fullPath);
                    mod.category = "Raw Files";
                    continue;
                }
            }


            progressReporter?.Report((0,0, "Removing empty mod slots..."));

            // Remove all empty mod frames.
            foreach (var mod in toRemove)
            {
                modlist.Mods.Remove(mod);
            }

            progressReporter?.Report((0, 0, "Removing empty modpacks..."));

            modlist.ModPacks.RemoveAll(modpack => {
                var anyMods = modlist.Mods.Any(mod => mod.modPack != null && mod.modPack.name == modpack.name);
                return !anyMods;
            });

            progressReporter?.Report((0, 0, "Saving Modlist file..."));

            await SaveModListAsync(modlist);
        }
        private async Task<(string Name, string Category)> ResolveMtrlModInfo(Imc _imc, string path, XivDependencyRoot root, Dictionary<XivDependencyRoot, List<XivImc>> ImcEntriesCache)
        {
            IItem item;
            var mSetRegex = new Regex("/v([0-9]{4})/");
            var match = mSetRegex.Match(path);
            if (match.Success)
            {
                // MTRL files should probably listed under one of the variant items as appropriate.
                List<XivImc> imcEntries = null;
                if (ImcEntriesCache.ContainsKey(root))
                {
                    imcEntries = ImcEntriesCache[root];
                }
                else
                {
                    imcEntries = await _imc.GetEntries(await root.GetImcEntryPaths());
                    ImcEntriesCache.Add(root, imcEntries);
                }
                var mSetId = Int32.Parse(match.Groups[1].Value);

                // The list from this function is already sorted.
                var allItems = await root.GetAllItems();


                var variantItem = allItems.FirstOrDefault(x =>
                {
                    var variantId = x.ModelInfo.ImcSubsetID;
                    if (imcEntries.Count <= variantId) return false;

                    return imcEntries[variantId].MaterialSet == mSetId;
                });

                item = variantItem == null ? allItems[0] : variantItem;

                return (item.GetModlistItemName(), item.GetModlistItemCategory());
            }
            else
            {
                // Invalid Material Path for this item.
                item = root.GetFirstItem();
                return (item.GetModlistItemName(), item.GetModlistItemCategory());
            }
        }


        /// <summary>
        /// Gets the sum total size in bytes of all modded dats.
        /// </summary>
        /// <returns></returns>
        public async Task<long> GetTotalModDataSize()
        {
            var _dat = new Dat(XivCache.GameInfo.GameDirectory);
            var dataFiles = Enum.GetValues(typeof(XivDataFile)).Cast<XivDataFile>();

            long size = 0;
            foreach(var df in dataFiles)
            {
                var moddedDats = await _dat.GetModdedDatList(df);
                foreach(var dat in moddedDats)
                {
                    var finfo = new FileInfo(dat);
                    size += finfo.Length;
                }
            }

            return size;
        }

        /// <summary>
        /// This function will rewrite all the mod files to new DAT entries, replacing the existing modded DAT files with new, defragmented ones.
        /// Returns the total amount of bytes recovered.
        /// </summary>
        /// <returns></returns>
        public async Task<long> DefragmentModdedDats(IProgress<(int Current, int Total, string Message)> progressReporter = null)
        {
            var modlist = await GetModListAsync();
            var _dat = new Dat(XivCache.GameInfo.GameDirectory);
            var _index = new Index(XivCache.GameInfo.GameDirectory);

            var offsets = new Dictionary<string, (long oldOffset, long newOffset, uint size)>();

            modlist.Mods.RemoveAll(x => String.IsNullOrWhiteSpace(x.fullPath));

            var modsByDf = modlist.Mods.GroupBy(x => XivDataFiles.GetXivDataFile(x.datFile));
            var indexFiles = new Dictionary<XivDataFile, IndexFile>();

            var count = 0;
            var total = modlist.Mods.Count();

            var originalSize = await GetTotalModDataSize();

            var workerStatus = XivCache.CacheWorkerEnabled;
            XivCache.CacheWorkerEnabled = false;
            try
            {
                // Copy files over into contiguous data blocks in the new dat files.
                foreach (var dKv in modsByDf)
                {
                    var df = dKv.Key;
                    indexFiles.Add(df, await _index.GetIndexFile(df));

                    foreach (var mod in dKv)
                    {
                        progressReporter?.Report((count, total, "Writing mod data to temporary DAT files..."));

                        try
                        {
                            var size = await _dat.GetCompressedFileSize(mod.data.modOffset, df);

                            if (size % 256 != 0) throw new Exception("Dicks");

                            var data = _dat.GetRawData(mod.data.modOffset, df, size);
                            var newOffset = await WriteToTempDat(_dat, data, df);

                            if (mod.IsCustomFile())
                            {
                                mod.data.originalOffset = newOffset;
                            }
                            mod.data.modOffset = newOffset;
                            indexFiles[df].SetDataOffset(mod.fullPath, newOffset);
                        }
                        catch (Exception except)
                        {
                            throw;
                        }

                        count++;
                    }
                }

                progressReporter?.Report((0, 0, "Removing old modded DAT files..."));
                foreach (var dKv in modsByDf)
                {
                    // Now we need to delete the current modded dat files.
                    var moddedDats = await _dat.GetModdedDatList(dKv.Key);
                    foreach (var file in moddedDats)
                    {
                        File.Delete(file);
                    }
                }

                // Now we need to rename our temp files.
                progressReporter?.Report((0, 0, "Renaming temporary DAT files..."));
                var finfos = XivCache.GameInfo.GameDirectory.GetFiles();
                var temps = finfos.Where(x => x.Name.EndsWith(".temp"));
                foreach (var temp in temps)
                {
                    var oldName = temp.FullName;
                    var newName = temp.FullName.Substring(0, oldName.Length - 4);
                    System.IO.File.Move(oldName, newName);
                }

                progressReporter?.Report((0, 0, "Saving updated Index Files..."));

                foreach (var dKv in modsByDf)
                {
                    await _index.SaveIndexFile(indexFiles[dKv.Key]);
                }

                progressReporter?.Report((0, 0, "Saving updated Modlist..."));

                // Save modList
                await SaveModListAsync(modlist);


                var finalSize = await GetTotalModDataSize();
                var saved = originalSize - finalSize;
                saved = saved > 0 ? saved : 0;
                return saved;
            } finally
            {
                var finfos = XivCache.GameInfo.GameDirectory.GetFiles();
                var temps = finfos.Where(x => x.Name.EndsWith(".temp"));
                foreach(var temp in temps)
                {
                    temp.Delete();
                }

                XivCache.CacheWorkerEnabled = workerStatus;
            }

        }

        private async Task<long> WriteToTempDat(Dat _dat, byte[] data, XivDataFile df)
        {
            var moddedDats = await _dat.GetModdedDatList(df);
            var tempDats = moddedDats.Select(x => x + ".temp");
            var maxSize = Dat.GetMaximumDatSize();

            string targetDatFile = null;
            foreach(var file in tempDats)
            {
                if(!File.Exists(file))
                {
                    using (var stream = new BinaryWriter(File.Create(file)))
                    {
                        stream.Write(Dat.MakeSqPackHeader());
                        stream.Write(Dat.MakeDatHeader());
                    }
                    targetDatFile = file;
                    break;
                }

                var finfo = new FileInfo(file);
                if(finfo.Length + data.Length < maxSize)
                {
                    targetDatFile = file;
                }
            }

            if (targetDatFile == null) throw new Exception("Unable to find open temp dat to write to.");

            var rex = new Regex("([0-9])\\.temp$");
            var match = rex.Match(targetDatFile);
            uint datNum = UInt32.Parse(match.Groups[1].Value);


            long baseOffset = 0;
            using(var stream = new BinaryWriter(File.Open(targetDatFile, FileMode.Append)))
            {   
                baseOffset = stream.BaseStream.Position;
                stream.Write(data);
            }

            long offset = ((baseOffset / 8) | (datNum * 2)) * 8;

            return offset;
        }
    }

}