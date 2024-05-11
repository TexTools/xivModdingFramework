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
using SharpDX.Direct3D11;
using SharpDX.Text;
using SharpDX.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

using Index = xivModdingFramework.SqPack.FileTypes.Index;

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

            // Create modlist if we don't already have one.
            CreateModlist();
        }

        private static ModList _CachedModList;
        private static DateTime _ModListLastModifiedTime;
        public async Task<ModList> GetModList()
        {
            await _modlistSemaphore.WaitAsync();
            try
            {
                var lastUpdatedTime = new FileInfo(ModListDirectory.FullName).LastWriteTimeUtc;

                if (_CachedModList == null)
                {
                    // First access
                    var modlistText = File.ReadAllText(ModListDirectory.FullName);
                    _CachedModList = JsonConvert.DeserializeObject<ModList>(modlistText);
                    _ModListLastModifiedTime = lastUpdatedTime;
                } else if (lastUpdatedTime > _ModListLastModifiedTime)
                {
                    // Cache is stale.
                    var modlistText = File.ReadAllText(ModListDirectory.FullName);
                    _CachedModList = JsonConvert.DeserializeObject<ModList>(modlistText);
                    _ModListLastModifiedTime = lastUpdatedTime;
                }
            }
            finally
            {
                _modlistSemaphore.Release();
            }

            if (_CachedModList == null)
            {
                throw new InvalidOperationException("GetModlist returned NULL Mod List.");
            }

            return _CachedModList;
        }

        public void SaveModList(ModList ml)
        {
            _modlistSemaphore.Wait();

            try
            {
                File.WriteAllText(ModListDirectory.FullName, JsonConvert.SerializeObject(ml, Formatting.Indented));
            }
            finally
            {
                _modlistSemaphore.Release();
            }
        }
        public async Task SaveModListAsync(ModList ml)
        {
            await _modlistSemaphore.WaitAsync();

            try
            {
                File.WriteAllText(ModListDirectory.FullName, JsonConvert.SerializeObject(ml, Formatting.Indented));
            }
            finally
            {
                _modlistSemaphore.Release();
            }
        }

        public async Task DeleteAllFilesAddedByTexTools()
        {
            var modList = await GetModList();
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
        /// Creates a blank ModList file if one does not already exist.
        /// </summary>
        public void CreateModlist()
        {
            if (File.Exists(ModListDirectory.FullName))
            {
                return;
            }

            var modList = new ModList(true)
            {
                version = _modlistVersion.ToString(),
            };

            SaveModList(modList);
        }

        /// <summary>
        /// Tries to get the mod entry for the given internal file path, return null otherwise
        /// </summary>
        /// <param name="internalFilePath">The internal file path to find</param>
        /// <returns>The mod entry if found, null otherwise</returns>
        public async Task<Mod> TryGetModEntry(string internalFilePath)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(internalFilePath))
                {
                    return null;
                }

                return await Task.Run(async () =>
                {
                    internalFilePath = internalFilePath.Replace("\\", "/");

                    var modList = await GetModList();

                    if (modList == null) return null;

                    modList.ModDictionary.TryGetValue(internalFilePath, out var mod);
                    return mod;
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
        public async Task<XivModStatus> IsModEnabled(string internalPath, bool indexCheck, ModTransaction tx = null)
        {
            if (!File.Exists(ModListDirectory.FullName))
            {
                return XivModStatus.Original;
            }

            if(tx == null)
            {
                // Read only TX so we can be lazy about manually disposing it.
                tx = ModTransaction.BeginTransaction(true);
            }
            var modList = await tx.GetModList();

            if (indexCheck)
            {
                modList.ModDictionary.TryGetValue(internalPath, out var modEntry);
                if (modEntry == null)
                {
                    return XivModStatus.Original;
                }

                var originalOffset = modEntry.data.originalOffset;
                var moddedOffset = modEntry.data.modOffset;
                var offset = await tx.Get8xDataOffset(internalPath);

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
                modList.ModDictionary.TryGetValue(internalPath, out var modEntry);

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
        public async Task<bool> ToggleModStatus(string internalFilePath, bool enable, ModTransaction tx = null)
        {
            if (XivCache.GameInfo.UseLumina)
            {
                throw new Exception("TexTools mods cannot be altered in Lumina mode.");
            }

            var index = new Index(_gameDirectory);

            if (string.IsNullOrEmpty(internalFilePath))
            {
                throw new Exception("File Path missing, unable to toggle mod.");
            }

            var doSave = false;
            if (tx == null)
            {
                tx = ModTransaction.BeginTransaction();
                doSave = true;
            }
            try
            {
                var modList = await tx.GetModList();
                modList.ModDictionary.TryGetValue(internalFilePath, out var modEntry);
                var result = await ToggleModUnsafe(enable, modEntry, false, true, tx);

                // If we were unable to toggle the mod, and we have a local transaction...
                if(!result)
                {
                    if (doSave)
                    {
                        // Cancel and return;
                        ModTransaction.CancelTransaction(tx);
                    }

                    return false;
                }

                // Successfully toggled mod.
                if (doSave)
                {
                    await ModTransaction.CommitTransaction(tx);
                }
                return true;
            }
            catch
            {
                if (doSave)
                {
                    ModTransaction.CancelTransaction(tx);
                }
                throw;
            }
        }

        /// <summary>
        /// Toggles the mod on or off
        /// </summary>
        /// <param name="internalFilePath">The internal file path of the mod</param>
        /// <param name="enable">The status of the mod</param>
        public async Task ToggleModPackStatus(string modPackName, bool enable, ModTransaction tx = null)
        {
            if (XivCache.GameInfo.UseLumina)
            {
                throw new Exception("TexTools mods cannot be altered in Lumina mode.");
            }

            var ownTransaction = false;
            if(tx == null)
            {
                ownTransaction = true;
                tx = ModTransaction.BeginTransaction();
            }
            try
            {

                var modList = await tx.GetModList();
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

                await ToggleMods(enable, mods.Select(x => x.fullPath), null, tx);

                if (ownTransaction)
                {
                    await ModTransaction.CommitTransaction(tx);
                }
            }
            catch
            {
                if(ownTransaction)
                {
                    ModTransaction.CancelTransaction(tx);
                }
            }
        }

        /// <summary>
        /// Performs the most low-level mod enable/disable functions, without saving the modlist,
        /// ergo this should only be called by functions which will handle saving the modlist after
        /// they're done performing all modlist operations.
        /// 
        /// NOTE: If a transaction is supplied, metadata files will not be expanded/reset, as it is assumed the higher level function will handle it.
        /// </summary>
        /// <param name="enable"></param>
        /// <param name="mod"></param>
        /// <returns></returns>
        public async Task<bool> ToggleModUnsafe(bool enable, Mod mod, bool includeInternal, bool updateCache, ModTransaction tx = null)
        {
            if (XivCache.GameInfo.UseLumina)
            {
                throw new Exception("TexTools mods cannot be altered in Lumina mode.");
            }

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

            var df = IOUtil.GetDataFileFromPath(mod.fullPath);


            bool commit = tx == null;
            if(tx == null)
            {
                tx = ModTransaction.BeginTransaction(false, mod.modPack);
            }
            try
            {
                var index = await tx.GetIndexFile(df);

                // Added file.
                if (enable)
                {
                    index.SetDataOffset(mod.fullPath, mod.data.modOffset);
                    mod.enabled = true;

                    if (commit)
                    {
                        // Check if we're re-enabling a metadata mod.
                        var ext = Path.GetExtension(mod.fullPath);
                        if (ext == ".meta")
                        {
                            await ItemMetadata.ApplyMetadata(mod.fullPath, false, tx);
                        }
                        else if (ext == ".rgsp")
                        {
                            await CMP.ApplyRgspFile(mod.fullPath, tx);
                        }
                    }
                }
                else if (!enable)
                {
                    // Removing a file.
                    if (mod.IsCustomFile())
                    {
                        index.SetDataOffset(mod.fullPath, 0);
                    }
                    else
                    {
                        index.SetDataOffset(mod.fullPath, mod.data.originalOffset);
                    }

                    if(commit)
                    {
                        // This is a metadata entry being deleted, we'll need to restore the metadata entries back to default.
                        if (mod.fullPath.EndsWith(".meta"))
                        {
                            var root = await XivCache.GetFirstRoot(mod.fullPath);
                            await ItemMetadata.RestoreDefaultMetadata(root, tx);
                        }

                        if (mod.fullPath.EndsWith(".rgsp"))
                        {
                            await CMP.RestoreDefaultScaling(mod.fullPath, tx);
                        }
                    }

                    mod.enabled = false;
                }

                if (commit)
                {
                    await ModTransaction.CommitTransaction(tx);
                }
            }
            catch(Exception ex)
            {
                if (commit)
                {
                    ModTransaction.CancelTransaction(tx);
                }
                throw;
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
        public async Task ToggleAllMods(bool enable, IProgress<(int current, int total, string message)> progress = null, ModTransaction tx = null)
        {
            if(XivCache.GameInfo.UseLumina)
            {
                throw new Exception("TexTools mods cannot be toggled in Lumina mode.");
            }

            var ownTransaction = false;
            if (tx == null)
            {
                ownTransaction = true;
                tx = ModTransaction.BeginTransaction();
            }
            try
            {
                var modList = await tx.GetModList();
                await ToggleMods(enable, modList.Mods.Select(x => x.fullPath), progress, tx);

                if(ownTransaction)
                {
                    await ModTransaction.CommitTransaction(tx);
                }
            } catch
            {
                if(ownTransaction)
                {
                    ModTransaction.CancelTransaction(tx);
                }
            }
        }

        public async Task ToggleMods(bool enable, IEnumerable<string> filePaths, IProgress<(int current, int total, string message)> progress = null, ModTransaction tx = null)
        {
            if (XivCache.GameInfo.UseLumina)
            {
                throw new Exception("TexTools mods cannot be toggled in Lumina mode.");
            }

            var _index = new Index(_gameDirectory);

            var modList = await GetModList();

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

            var ownTransaction = false;
            if(tx == null)
            {
                ownTransaction = true;
                tx = ModTransaction.BeginTransaction();
            }
            try { 
                foreach (var modEntry in mods)
                {
                    // Save disabling these for last.
                    if (modEntry.IsInternal()) continue;
                    await ToggleModUnsafe(enable, modEntry, false, false, tx);
                    progress?.Report((++modNum, modList.Mods.Count, string.Empty));
                }

                if (fullToggle && !enable)
                {
                    // If we're doing a full mod toggle, we should purge our internal files as well.
                    var internalEntries = modList.Mods.Where(x => x.IsInternal()).ToList();
                    foreach (var modEntry in internalEntries)
                    {
                        var df = IOUtil.GetDataFileFromPath(modEntry.fullPath);
                        await ToggleModUnsafe(enable, modEntry, true, false, tx);
                        modList.RemoveMod(modEntry);
                    }
                }
                else if (enable)
                {
                    progress?.Report((0, 0, "Expanding Metadata Entries..."));

                    // Batch and group apply the metadata entries.
                    var metadataEntries = mods.Where(x => x.fullPath.EndsWith(".meta")).ToList();
                    var _dat = new Dat(XivCache.GameInfo.GameDirectory);

                    Dictionary<XivDataFile, List<ItemMetadata>> metadata = new Dictionary<XivDataFile, List<ItemMetadata>>();
                    foreach (var mod in metadataEntries)
                    {
                        var df = IOUtil.GetDataFileFromPath(mod.fullPath);
                        var data = await _dat.ReadSqPackType2(mod.data.modOffset, df, tx);
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
                        await ItemMetadata.ApplyMetadataBatched(dkv.Value, tx);
                    }

                    var rgspEntries = mods.Where(x => x.fullPath.EndsWith(".rgsp")).ToList();
                    foreach (var mod in rgspEntries)
                    {
                        await CMP.ApplyRgspFile(mod.fullPath, tx);
                    }
                }
                else
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
                        await ItemMetadata.ApplyMetadataBatched(dkv.Value, tx);
                    }

                    var rgspEntries = mods.Where(x => x.fullPath.EndsWith(".rgsp")).ToList();
                    foreach (var mod in rgspEntries)
                    {
                        await CMP.RestoreDefaultScaling(mod.fullPath, tx);
                    }

                }

                if (ownTransaction)
                {
                    await ModTransaction.CommitTransaction(tx);
                }

                // Do these as a batch query at the end.
                progress?.Report((++modNum, modList.Mods.Count, "Adding modified files to Cache Queue..."));
                var allPaths = mods.Select(x => x.fullPath).ToList();
                XivCache.QueueDependencyUpdate(allPaths);
            }
            catch
            {
                if (ownTransaction)
                {
                    ModTransaction.CancelTransaction(tx);
                }
            }


        }

        /// <summary>
        /// Purges any invalid empty mod blocks from the modlist.
        /// </summary>
        /// <returns></returns>
        public async Task<int> PurgeInvalidEmptyBlocks(ModTransaction tx)
        {
            int removed = 0;
            var modList = await tx.GetModList();
            var emptyBlocks = modList.Mods.Where(x => x.name == string.Empty);
            var toRemove = emptyBlocks.Where(x => x.data.modOffset <= 0).ToList();

            foreach(var block in toRemove)
            {
                modList.RemoveMod(block);
                removed++;
            }

            return removed;
        } 

        /// <summary>
        /// Deletes a mod from the modlist
        /// </summary>
        /// <param name="modItemPath">The mod item path of the mod to delete</param>
        public async Task DeleteMod(string modItemPath, bool allowInternal = false, ModTransaction tx = null)
        {
            if (XivCache.GameInfo.UseLumina)
            {
                throw new Exception("TexTools mods cannot be altered in Lumina mode.");
            }

            var doSave = false;
            var _index = new Index(_gameDirectory);
            if (tx == null)
            {
                doSave = true;
                tx = ModTransaction.BeginTransaction();
            }
            try
            {
                var modList = await tx.GetModList();

                var modToRemove = (from mod in modList.Mods
                                   where mod.fullPath.Equals(modItemPath)
                                   select mod).FirstOrDefault();

                // Mod doesn't exist in the modlist.
                if (modToRemove == null) return;

                if (modToRemove.IsInternal() && !allowInternal)
                {
                    throw new Exception("Cannot delete internal data without explicit toggle.");
                }

                await ToggleModUnsafe(false, modToRemove, allowInternal, true, tx);


                // This is a metadata entry being deleted, we'll need to restore the metadata entries back to default.
                if (modToRemove.fullPath.EndsWith(".meta"))
                {
                    var root = await XivCache.GetFirstRoot(modToRemove.fullPath);
                    await ItemMetadata.RestoreDefaultMetadata(root, tx);
                }

                if (modToRemove.fullPath.EndsWith(".rgsp"))
                {
                    await CMP.RestoreDefaultScaling(modToRemove.fullPath, tx);
                }

                modList.RemoveMod(modToRemove);

                if (doSave)
                {
                    await ModTransaction.CommitTransaction(tx);
                }
            }
            catch
            {
                if (doSave)
                {
                    ModTransaction.CancelTransaction(tx);
                }
                throw;
            }
        }

        /// <summary>
        /// Deletes a Mod Pack and all its mods from the modlist
        /// </summary>
        /// <param name="modPackName">The name of the Mod Pack to be deleted</param>
        public async Task DeleteModPack(string modPackName)
        {
            if (XivCache.GameInfo.UseLumina)
            {
                throw new Exception("TexTools mods cannot be altered in Lumina mode.");
            }


            using (var tx = ModTransaction.BeginTransaction())
            {
                var modList = await tx.GetModList();
                var modsToRemove = (from mod in modList.Mods
                                    where mod.modPack != null && mod.modPack.name.Equals(modPackName)
                                    select mod).ToList();

                var modRemoveCount = modsToRemove.Count;

                // Disable all the mods.
                await ToggleMods(false, modsToRemove.Select(x => x.fullPath), null, tx);

                // Then remove them from the modlist.
                modList.RemoveMods(modsToRemove);

                await ModTransaction.CommitTransaction(tx);
            }
        }


        /// <summary>
        /// Cleans up the Modlist file, performing the following operations.
        /// 
        /// 1.  Fix up the Item Names and Categories of all mods to be consistent.
        /// 2.  Remove all empty mod slots.
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task CleanUpModlist(IProgress<(int Current, int Total, string Message)> progressReporter = null, ModTransaction tx = null)
        {
            if (XivCache.GameInfo.UseLumina)
            {
                throw new Exception("TexTools mods cannot be altered in Lumina mode.");
            }

            progressReporter?.Report((0, 0, "Loading Modlist file..."));

            var ownTransaction = false;
            if (tx == null)
            {
                ownTransaction = true;
                tx = ModTransaction.BeginTransaction();
            }
            try
            {
                var modlist = await tx.GetModList();

                var _imc = new Imc(XivCache.GameInfo.GameDirectory);

                // IMC entries cache so we're not having to constantly re-load the IMC data.
                Dictionary<XivDependencyRoot, List<XivImc>> ImcEntriesCache = new Dictionary<XivDependencyRoot, List<XivImc>>();

                var totalMods = modlist.Mods.Count;


                var count = 0;
                List<Mod> toRemove = new List<Mod>();
                foreach (var mod in modlist.Mods)
                {
                    progressReporter?.Report((count, totalMods, "Fixing up item names..."));
                    count++;

                    if (String.IsNullOrWhiteSpace(mod.fullPath))
                    {
                        toRemove.Add(mod);
                        continue;
                    }

                    if (mod.IsInternal()) continue;

                    XivDependencyRoot root = null;
                    try
                    {
                        root = await XivCache.GetFirstRoot(mod.fullPath);
                        if (root == null)
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
                            var data = await ResolveMtrlModInfo(_imc, mod.fullPath, root, ImcEntriesCache, tx);
                            mod.name = data.Name;
                            mod.category = data.Category;

                        }
                        else if (Imc.UsesImc(root) && ext == ".tex")
                        {
                            // For textures, get the first material using them, and list them under that material's item.
                            var parents = await XivCache.GetParentFiles(mod.fullPath);
                            if (parents.Count == 0)
                            {
                                item = root.GetFirstItem();
                                mod.name = item.GetModlistItemName();
                                mod.category = item.GetModlistItemCategory();
                                continue;
                            }

                            var parent = parents[0];

                            var data = await ResolveMtrlModInfo(_imc, parent, root, ImcEntriesCache, tx);
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
                    }
                    catch
                    {
                        mod.name = Path.GetFileName(mod.fullPath);
                        mod.category = "Raw Files";
                        continue;
                    }
                }


                progressReporter?.Report((0, 0, "Removing empty mod slots..."));

                // Remove all empty mod frames.
                modlist.RemoveMods(toRemove);

                progressReporter?.Report((0, 0, "Saving Modlist file..."));

                await ModTransaction.CommitTransaction(tx);
            }
            catch
            {
                if (ownTransaction)
                {
                    ModTransaction.CancelTransaction(tx);
                }
            }
        }
        private async Task<(string Name, string Category)> ResolveMtrlModInfo(Imc _imc, string path, XivDependencyRoot root, Dictionary<XivDependencyRoot, List<XivImc>> ImcEntriesCache, ModTransaction tx = null)
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
                    if (tx == null)
                    {
                        // Read only TX if none supplied.
                        tx = ModTransaction.BeginTransaction(true);
                    }

                    imcEntries = await _imc.GetEntries(await root.GetImcEntryPaths(), false, tx);
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
        /// 
        /// NOT TRANSACTION SAFE.
        /// </summary>
        /// <returns></returns>
        public async Task<long> DefragmentModdedDats(IProgress<(int Current, int Total, string Message)> progressReporter = null)
        {
            if (XivCache.GameInfo.UseLumina)
            {
                throw new Exception("TexTools mods cannot be altered in Lumina mode.");
            }

            var modlist = await GetModList();
            var _dat = new Dat(XivCache.GameInfo.GameDirectory);
            var _index = new Index(XivCache.GameInfo.GameDirectory);

            var offsets = new Dictionary<string, (long oldOffset, long newOffset, uint size)>();

            var toRemove = modlist.Mods.Where(x => String.IsNullOrWhiteSpace(x.fullPath));
            modlist.RemoveMods(toRemove);

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

                            var data = _dat.GetCompressedData(mod.data.modOffset, df, size);
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
                    indexFiles[dKv.Key].Save();
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