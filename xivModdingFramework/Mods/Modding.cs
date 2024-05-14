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
using SharpDX.IO;
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
    public static class Modding
    {
        private static readonly Version _modlistVersion = new Version(1, 0);
        private static SemaphoreSlim _modlistSemaphore = new SemaphoreSlim(1);

        public static string ModListDirectory
        {
            get
            {
                return Path.Combine(XivCache.GameInfo.GameDirectory.Parent.Parent.FullName, XivStrings.ModlistFilePath);
            }
        }

        static Modding()
        {
            // Create modlist if we don't already have one.
            CreateModlist();
        }

        private static ModList _CachedModList;
        private static DateTime _ModListLastModifiedTime;
        internal static async Task<ModList> GetModList()
        {
            await _modlistSemaphore.WaitAsync();
            try
            {
                var lastUpdatedTime = new FileInfo(ModListDirectory).LastWriteTimeUtc;

                if (_CachedModList == null)
                {
                    // First access
                    var modlistText = File.ReadAllText(ModListDirectory);
                    _CachedModList = JsonConvert.DeserializeObject<TransactionModList>(modlistText);
                    _ModListLastModifiedTime = lastUpdatedTime;
                } else if (lastUpdatedTime > _ModListLastModifiedTime)
                {
                    // Cache is stale.
                    var modlistText = File.ReadAllText(ModListDirectory);
                    _CachedModList = JsonConvert.DeserializeObject<TransactionModList>(modlistText);
                    _ModListLastModifiedTime = lastUpdatedTime;
                }
                _CachedModList.RebuildModPackList();
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

        internal static void SaveModList(ModList ml)
        {
            _modlistSemaphore.Wait();

            try
            {
                File.WriteAllText(ModListDirectory, JsonConvert.SerializeObject(ml, Formatting.Indented));
            }
            finally
            {
                _modlistSemaphore.Release();
            }
        }
        internal static async Task SaveModListAsync(ModList ml)
        {
            await _modlistSemaphore.WaitAsync();

            try
            {
                File.WriteAllText(ModListDirectory, JsonConvert.SerializeObject(ml, Formatting.Indented));
            }
            finally
            {
                _modlistSemaphore.Release();
            }
        }

        /// <summary>
        /// Creates a blank ModList file if one does not already exist.
        /// </summary>
        internal static void CreateModlist()
        {
            if (File.Exists(ModListDirectory))
            {
                // Try to parse it.
                try
                {
                    var modlistText = File.ReadAllText(ModListDirectory);
                    var res = JsonConvert.DeserializeObject<ModList>(modlistText);
                    if(res == null)
                    {
                        throw new Exception("Null Modlist");
                    }
                }
                catch(Exception ex) 
                {
                    // Broken.  Regenerate modlist.
                    var newModList = new ModList(true)
                    {
                        Version = _modlistVersion.ToString(),
                    };

                    SaveModList(newModList);
                }
                

                return;
            }

            var modList = new ModList(true)
            {
                Version = _modlistVersion.ToString(),
            };

            SaveModList(modList);
        }

        /// <summary>
        /// Checks to see whether the mod is currently enabled
        /// </summary>
        /// <param name="internalPath">The internal path of the file</param>
        /// <param name="dataFile">The data file to check in</param>
        /// <param name="indexCheck">Flag to determine whether to check the index file or just the modlist</param>
        /// <returns></returns>
        public static async Task<EModState> GetModState(string internalPath, ModTransaction tx = null)
        {
            if(tx == null)
            {
                // Read only TX so we can be lazy about manually disposing it.
                tx = ModTransaction.BeginTransaction();
            }
            var modList = await tx.GetModList();

            var nmod = modList.GetMod(internalPath);
            if (nmod == null)
            {
                return EModState.UnModded;
            }
            var mod = nmod.Value;

            var originalOffset = mod.OriginalOffset8x;
            var moddedOffset = mod.ModOffset8x;
            var offset = await tx.Get8xDataOffset(internalPath);

            if (offset.Equals(originalOffset))
            {
                return EModState.Disabled;
            }

            if (offset.Equals(moddedOffset))
            {
                return EModState.Enabled;
            }

            return EModState.Invalid;
        }

        /// <summary>
        /// Toggles the mod on or off
        /// </summary>
        /// <param name="internalFilePath">The internal file path of the mod</param>
        /// <param name="enable">The status of the mod</param>
        public static async Task<bool> ToggleModStatus(string internalFilePath, bool enable, ModTransaction tx = null)
        {
            var index = new Index(XivCache.GameInfo.GameDirectory);

            if (string.IsNullOrEmpty(internalFilePath))
            {
                throw new Exception("File Path missing, unable to toggle mod.");
            }

            var doSave = false;
            if (tx == null)
            {
                tx = ModTransaction.BeginTransaction(true);
                doSave = true;
            }
            try
            {
                var modList = await tx.GetModList();
                var mod = await tx.GetMod(internalFilePath);
                if(mod == null)
                {
                    return false;
                }

                var result = await ToggleModUnsafe(enable, mod.Value, false, true, tx);

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
        public static async Task ToggleModPackStatus(string modPackName, bool enable, ModTransaction tx = null)
        {
            var ownTransaction = false;
            if(tx == null)
            {
                ownTransaction = true;
                tx = ModTransaction.BeginTransaction(true);
            }
            try
            {

                var modList = await tx.GetModList();
                var modPack = modList.GetModPack(modPackName);
                if(modPack == null)
                {
                    return;
                }

                var mods = modPack.Value.Mods;

                if (mods.Count == 0)
                {
                    // Should never hit this, but you never know.
                    return;
                }

                await ToggleMods(enable, mods, null, tx);

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
        public static async Task<bool> ToggleModUnsafe(bool enable, Mod mod, bool includeInternal, bool updateCache, ModTransaction tx = null)
        {
            if (mod == null) return false;
            if (string.IsNullOrEmpty(mod.ItemName)) return false;
            if (string.IsNullOrEmpty(mod.FilePath)) return false;

            if (mod.OriginalOffset8x < 0 && !enable)
            {
                throw new Exception("Cannot disable mod with invalid original offset.");
            }

            if (enable && mod.ModOffset8x < 0)
            {
                throw new Exception("Cannot enable mod with invalid mod offset.");
            }
            
            if(mod.IsInternal() && !includeInternal)
            {
                // Don't allow toggling internal mods unless we were specifically told to.
                return false;
            }

            var df = IOUtil.GetDataFileFromPath(mod.FilePath);


            bool commit = tx == null;
            if(tx == null)
            {
                tx = ModTransaction.BeginTransaction(true);
            }
            try
            {
                var modList = await tx.GetModList();
                var index = await tx.GetIndexFile(df);

                tx.ModPack = mod.GetModPack(modList);

                if (enable)
                {
                    // Enabling Mod
                    index.Set8xDataOffset(mod.FilePath, mod.ModOffset8x);

                    if (commit)
                    {
                        // Check if we're re-enabling a metadata mod.
                        var ext = Path.GetExtension(mod.FilePath);
                        if (ext == ".meta")
                        {
                            await ItemMetadata.ApplyMetadata(mod.FilePath, false, tx);
                        }
                        else if (ext == ".rgsp")
                        {
                            await CMP.ApplyRgspFile(mod.FilePath, tx);
                        }
                    }
                }
                else if (!enable)
                {
                    // Disabling mod.
                    index.Set8xDataOffset(mod.FilePath, mod.OriginalOffset8x);

                    if (commit)
                    {
                        // This is a metadata entry being deleted, we'll need to restore the metadata entries back to default.
                        if (mod.FilePath.EndsWith(".meta"))
                        {
                            var root = await XivCache.GetFirstRoot(mod.FilePath);
                            await ItemMetadata.RestoreDefaultMetadata(root, tx);
                        }

                        if (mod.FilePath.EndsWith(".rgsp"))
                        {
                            await CMP.RestoreDefaultScaling(mod.FilePath, tx);
                        }
                    }
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
                XivCache.QueueDependencyUpdate(mod.FilePath);
            }

            return true;
        }

        /// <summary>
        /// Toggles all mods on or off
        /// </summary>
        /// <param name="enable">The status to switch the mods to True if enable False if disable</param>
        public static async Task ToggleAllMods(bool enable, IProgress<(int current, int total, string message)> progress = null, ModTransaction tx = null)
        {
            var ownTransaction = false;
            if (tx == null)
            {
                ownTransaction = true;
                tx = ModTransaction.BeginTransaction(true);
            }
            try
            {
                var modList = await tx.GetModList();
                await ToggleMods(enable, modList.Mods.Select(x => x.Value.FilePath), progress, tx);

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

        public static async Task ToggleMods(bool enable, IEnumerable<string> filePaths, IProgress<(int current, int total, string message)> progress = null, ModTransaction tx = null)
        {
            var _index = new Index(XivCache.GameInfo.GameDirectory);

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
            var mods = modList.GetMods(x => filePaths.Contains(x.FilePath)).ToList();

            if (mods.Count == 0) return;

            // If we're disabling all of our standard files, then this is a full toggle.

            var fullToggle = mods.Count >= modList.Mods.Count;

            var ownTransaction = false;
            if(tx == null)
            {
                ownTransaction = true;
                tx = ModTransaction.BeginTransaction(true);
            }
            try { 
                foreach (var modEntry in mods)
                {
                    // Save disabling these for last.
                    if (modEntry.IsInternal()) continue;
                    await ToggleModUnsafe(enable, modEntry, false, false, tx);
                    progress?.Report((++modNum, mods.Count, string.Empty));
                }

                if (fullToggle && !enable)
                {
                    // If we're doing a full mod toggle, we should purge our internal files as well.
                    var internalEntries = modList.GetMods(x => x.IsInternal()).ToList();
                    foreach (var modEntry in internalEntries)
                    {
                        var df = IOUtil.GetDataFileFromPath(modEntry.FilePath);
                        await ToggleModUnsafe(enable, modEntry, true, false, tx);
                        modList.RemoveMod(modEntry);
                    }
                }
                else if (enable)
                {
                    progress?.Report((0, 0, "Expanding Metadata Entries..."));

                    // Batch and group apply the metadata entries.
                    var metadataEntries = modList.GetMods(x => x.FilePath.EndsWith(".meta")).ToList();
                    var _dat = new Dat(XivCache.GameInfo.GameDirectory);

                    Dictionary<XivDataFile, List<ItemMetadata>> metadata = new Dictionary<XivDataFile, List<ItemMetadata>>();
                    foreach (var mod in metadataEntries)
                    {
                        var df = IOUtil.GetDataFileFromPath(mod.FilePath);
                        var data = await _dat.ReadSqPackType2(mod.ModOffset8x, df, tx);
                        var meta = await ItemMetadata.Deserialize(data);

                        meta.Validate(mod.FilePath);

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

                    var rgspEntries = modList.GetMods(x => x.FilePath.EndsWith(".rgsp")).ToList();
                    foreach (var mod in rgspEntries)
                    {
                        await CMP.ApplyRgspFile(mod.FilePath, tx);
                    }
                }
                else
                {
                    progress?.Report((0, 0, "Restoring Metadata Entries..."));
                    var metadataEntries = modList.GetMods(x => x.FilePath.EndsWith(".meta")).ToList();
                    var _dat = new Dat(XivCache.GameInfo.GameDirectory);

                    Dictionary<XivDataFile, List<ItemMetadata>> metadata = new Dictionary<XivDataFile, List<ItemMetadata>>();
                    foreach (var mod in metadataEntries)
                    {
                        var root = await XivCache.GetFirstRoot(mod.FilePath);
                        var df = IOUtil.GetDataFileFromPath(mod.FilePath);
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

                    var rgspEntries = modList.GetMods(x => x.FilePath.EndsWith(".rgsp")).ToList();
                    foreach (var mod in rgspEntries)
                    {
                        await CMP.RestoreDefaultScaling(mod.FilePath, tx);
                    }

                }

                if (ownTransaction)
                {
                    await ModTransaction.CommitTransaction(tx);
                }

                // Do these as a batch query at the end.
                progress?.Report((++modNum, modList.Mods.Count, "Adding modified files to Cache Queue..."));
                var allPaths = mods.Select(x => x.FilePath).ToList();
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
        /// Deletes a mod from the modlist
        /// </summary>
        /// <param name="modItemPath">The mod item path of the mod to delete</param>
        public static async Task DeleteMod(string modItemPath, bool allowInternal = false, ModTransaction tx = null)
        {
            var doSave = false;
            if (tx == null)
            {
                doSave = true;
                tx = ModTransaction.BeginTransaction(true);
            }
            try
            {
                var modList = await tx.GetModList();

                var nmod = modList.GetMod(modItemPath);
                // Mod doesn't exist in the modlist.
                if (nmod == null) return;

                var modToRemove = nmod.Value;

                if (modToRemove.IsInternal() && !allowInternal)
                {
                    throw new Exception("Cannot delete internal data without explicit toggle.");
                }

                await ToggleModUnsafe(false, modToRemove, allowInternal, true, tx);


                // This is a metadata entry being deleted, we'll need to restore the metadata entries back to default.
                if (modToRemove.FilePath.EndsWith(".meta"))
                {
                    var root = await XivCache.GetFirstRoot(modToRemove.FilePath);
                    await ItemMetadata.RestoreDefaultMetadata(root, tx);
                }

                if (modToRemove.FilePath.EndsWith(".rgsp"))
                {
                    await CMP.RestoreDefaultScaling(modToRemove.FilePath, tx);
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
        public static async Task DeleteModPack(string modPackName)
        {
            using (var tx = ModTransaction.BeginTransaction(true))
            {
                var modList = await tx.GetModList();
                var mp = modList.GetModPack(modPackName);
                if(mp == null)
                {
                    return;
                }

                var modPack = mp.Value;
                var modsToRemove = modPack.Mods;

                var modRemoveCount = modsToRemove.Count;

                // Disable all the mods.
                await ToggleMods(false, modsToRemove, null, tx);

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
        public static async Task CleanUpModlist(IProgress<(int Current, int Total, string Message)> progressReporter = null, ModTransaction tx = null)
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
                tx = ModTransaction.BeginTransaction(true);
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
                var mods = modlist.GetMods();
                var newMods = new List<Mod>();
                foreach (var baseMod in mods)
                {
                    progressReporter?.Report((count, totalMods, "Fixing up item names..."));
                    count++;
                    var mod = baseMod;

                    if (mod.IsInternal()) continue;

                    XivDependencyRoot root = null;
                    try
                    {
                        root = await XivCache.GetFirstRoot(mod.FilePath);
                        if (root == null)
                        {
                            var cmpName = CMP.GetModFileNameFromRgspPath(mod.FilePath);
                            if (cmpName != null)
                            {
                                mod.ItemName = cmpName;
                                mod.ItemCategory = "Racial Scaling";
                            }
                            else if (mod.FilePath.StartsWith("ui/"))
                            {
                                mod.ItemName = Path.GetFileName(mod.FilePath);
                                mod.ItemCategory = "UI";
                            }
                            else
                            {
                                mod.ItemName = Path.GetFileName(mod.FilePath);
                                mod.ItemCategory = "Raw Files";
                            }
                            newMods.Add(mod);
                            continue;
                        }

                        var ext = Path.GetExtension(mod.FilePath);
                        IItem item = null;
                        if (Imc.UsesImc(root) && ext == ".mtrl")
                        {
                            var data = await ResolveMtrlModInfo(_imc, mod.FilePath, root, ImcEntriesCache, tx);
                            mod.ItemName = data.Name;
                            mod.ItemCategory = data.Category;

                        }
                        else if (Imc.UsesImc(root) && ext == ".tex")
                        {
                            // For textures, get the first material using them, and list them under that material's item.
                            var parents = await XivCache.GetParentFiles(mod.FilePath);
                            if (parents.Count == 0)
                            {
                                item = root.GetFirstItem();
                                mod.ItemName = item.GetModlistItemName();
                                mod.ItemCategory = item.GetModlistItemCategory();
                                continue;
                            }

                            var parent = parents[0];

                            var data = await ResolveMtrlModInfo(_imc, parent, root, ImcEntriesCache, tx);
                            mod.ItemName = data.Name;
                            mod.ItemCategory = data.Category;
                        }
                        else
                        {
                            item = root.GetFirstItem();
                            mod.ItemName = item.GetModlistItemName();
                            mod.ItemCategory = item.GetModlistItemCategory();
                            newMods.Add(mod);
                            continue;
                        }
                    }
                    catch
                    {
                        mod.ItemName = Path.GetFileName(mod.FilePath);
                        mod.ItemCategory = "Raw Files";
                        newMods.Add(mod);
                        continue;
                    }
                }

                modlist.AddOrUpdateMods(newMods);


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
        private static async Task<(string Name, string Category)> ResolveMtrlModInfo(Imc _imc, string path, XivDependencyRoot root, Dictionary<XivDependencyRoot, List<XivImc>> ImcEntriesCache, ModTransaction tx = null)
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
                        tx = ModTransaction.BeginTransaction();
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
        public static async Task<long> GetTotalModDataSize()
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
        public static async Task<long> DefragmentModdedDats(IProgress<(int Current, int Total, string Message)> progressReporter = null)
        {
            if (!Dat.AllowDatAlteration)
            {
                throw new Exception("Cannot defragment DATs while DAT writing is disabled.");
            }

            var modlist = await GetModList();
            var _dat = new Dat(XivCache.GameInfo.GameDirectory);
            var _index = new Index(XivCache.GameInfo.GameDirectory);

            var offsets = new Dictionary<string, (long oldOffset, long newOffset, uint size)>();

            var modsByDf = modlist.GetMods().GroupBy(x => IOUtil.GetDataFileFromPath(x.FilePath));
            var indexFiles = new Dictionary<XivDataFile, IndexFile>();

            var count = 0;
            var total = modlist.Mods.Count();

            var originalSize = await GetTotalModDataSize();

            var workerStatus = XivCache.CacheWorkerEnabled;
            XivCache.CacheWorkerEnabled = false;
            try
            {
                var newMods = new List<Mod>();
                // Copy files over into contiguous data blocks in the new dat files.
                foreach (var dKv in modsByDf)
                {
                    var df = dKv.Key;
                    indexFiles.Add(df, await _index.GetIndexFile(df));

                    foreach (var oMod in dKv)
                    {
                        var mod = oMod;
                        progressReporter?.Report((count, total, "Writing mod data to temporary DAT files..."));

                        try
                        {
                            var parts = IOUtil.Offset8xToParts(mod.ModOffset8x);
                            byte[] data;
                            using(var br = new BinaryReader(File.OpenRead(Dat.GetDatPath(df, parts.DatNum))))
                            {
                                // Check size.
                                br.BaseStream.Seek(parts.Offset, SeekOrigin.Begin);
                                var size = Dat.GetCompressedFileSize(br);

                                // Pull entire file.
                                br.BaseStream.Seek(parts.Offset, SeekOrigin.Begin);
                                data = br.ReadBytes(size);
                            }

                            var newOffset = await WriteToTempDat(data, df);

                            if (mod.IsCustomFile())
                            {
                                mod.OriginalOffset8x = newOffset;
                            }
                            mod.ModOffset8x = newOffset;
                            indexFiles[df].Set8xDataOffset(mod.FilePath, newOffset);
                            newMods.Add(mod);
                        }
                        catch (Exception except)
                        {
                            throw;
                        }

                        count++;
                    }
                }

                modlist.AddOrUpdateMods(newMods);

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

        private static async Task<long> WriteToTempDat(byte[] data, XivDataFile df)
        {
            var _dat = new Dat(XivCache.GameInfo.GameDirectory);
            var moddedDats = await _dat.GetModdedDatList(df);
            var tempDats = moddedDats.Select(x => x + ".temp");
            var maxSize = Dat.GetMaximumDatSize();

            var rex = new Regex("([0-9])\\.temp$");
            string targetDatFile = null;
            foreach(var file in tempDats)
            {
                var datId = Int32.Parse(rex.Match(targetDatFile).Groups[1].Value);

                if (!File.Exists(file))
                {
                    using (var stream = new BinaryWriter(File.Create(file)))
                    {
                        stream.Write(Dat.MakeCustomDatSqPackHeader());
                        stream.Write(Dat.MakeDatHeader(datId, 0));
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