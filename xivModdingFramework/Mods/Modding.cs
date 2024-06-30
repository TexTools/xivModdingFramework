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
using static xivModdingFramework.Cache.FrameworkExceptions;
using Index = xivModdingFramework.SqPack.FileTypes.Index;

namespace xivModdingFramework.Mods
{
    /// <summary>
    /// This class contains the methods that deal with the .modlist file
    /// </summary>
    public static class Modding
    {
        #region Constants and Private Vars

        private static readonly Version _modlistVersion = new Version(2, 0);
        private static SemaphoreSlim _modlistSemaphore = new SemaphoreSlim(1);

        internal static string ModListDirectory
        {
            get
            {
                return Path.Combine(XivCache.GameInfo.GameDirectory.Parent.Parent.FullName, XivStrings.ModlistFilePath);
            }
        }

        private static ModList _CachedModList;
        private static DateTime _ModListLastModifiedTime;

        #endregion

        #region Modlist Accessors

        static Modding()
        {
            // Create modlist if we don't already have one.
            CreateModlist();
        }

        /// <summary>
        /// Creates a blank ModList file if one does not already exist.
        /// </summary>
        internal static void CreateModlist(bool reset = false)
        {
            if (!reset)
            {
                if (File.Exists(ModListDirectory))
                {
                    // Try to parse it.
                    try
                    {
                        var modlistText = File.ReadAllText(ModListDirectory);
                        var res = JsonConvert.DeserializeObject<ModList>(modlistText);
                        if (res != null)
                        {
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Broken.  Regenerate modlist.
                    }
                }
            }

            var modList = new ModList(true)
            {
                Version = _modlistVersion.ToString(),
            };

            File.WriteAllText(ModListDirectory, JsonConvert.SerializeObject(modList, Formatting.Indented));
        }

        /// <summary>
        /// Retrieve the base modlist file.
        /// Should not be called directly unless you really know what you're doing.
        /// 
        /// </summary>
        /// <param name="writeEnabled"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>

        internal static async Task<ModList> INTERNAL_GetModList(bool writeEnabled)
        {
            await _modlistSemaphore.WaitAsync();
            try
            {
                if (writeEnabled)
                {
                    // Always get a clean file when doing write actions so it doesn't pollute our cached copy.
                    var modlistText = File.ReadAllText(ModListDirectory);
                    var modList = JsonConvert.DeserializeObject<TransactionModList>(modlistText);
                    modList.RebuildModPackList();
                    return modList;
                }
                else
                {
                    var lastUpdatedTime = new FileInfo(ModListDirectory).LastWriteTimeUtc;
                    if (_CachedModList == null || lastUpdatedTime > _ModListLastModifiedTime)
                    {
                        // First access or cache is stale.
                        var modlistText = File.ReadAllText(ModListDirectory);
                        _CachedModList = JsonConvert.DeserializeObject<TransactionModList>(modlistText);
                        _ModListLastModifiedTime = lastUpdatedTime;
                    }
                    _CachedModList.RebuildModPackList();
                }
            }
            catch(Exception ex)
            {
                throw new FileNotFoundException("Failedto find or parse modlist file.\n\n" + ex.Message);
            }
            finally
            {
                _modlistSemaphore.Release();
            }

            return _CachedModList;
        }

        /// <summary>
        /// Save the base modlist file.  Should not be called unless you really know what you're doing.
        /// </summary>
        /// <param name="ml"></param>
        internal static async Task INTERNAL_SaveModlist(ModList ml)
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


        #endregion

        #region Mod State Getters 

        /// <summary>
        /// Retrieves the current mod state of a file.
        /// Mostly a useful wrapper for when you don't know if the mod exists or not.
        /// </summary>
        /// <param name="internalPath">The internal path of the file</param>
        /// <returns></returns>
        public static async Task<EModState> GetModState(string internalPath, ModTransaction tx = null)
        {
            if(tx == null)
            {
                // Read only TX so we can be lazy about manually disposing it.
                tx = ModTransaction.BeginReadonlyTransaction();
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

        #endregion

        #region Mod State Setters

        /// <summary>
        /// Performs a standard state change for the mod at a given path.
        /// Returns true if the value was actually changed.
        /// </summary>
        /// <param name="enable"></param>
        /// <param name="mod"></param>
        /// <returns></returns>
        public static async Task<bool> SetModState(EModState state, Mod mod, ModTransaction tx = null)
        {
            return await SetModState(state, mod.FilePath, tx);
        }

        /// <summary>
        /// Performs a standard state change for the mod at a given path.
        /// Returns true if the value was actually changed.
        /// </summary>
        /// <param name="enable"></param>
        /// <param name="mod"></param>
        /// <returns></returns>
        public static async Task<bool> SetModState(EModState state, string path, ModTransaction tx = null)
        {
            return await INTERNAL_SetModState(state, path, false, tx);
        }

        /// <summary>
        /// Performs a standard state change for the mod at a given path.
        /// Returns true if the value was actually changed.
        /// </summary>
        /// <param name="enable"></param>
        /// <param name="mod"></param>
        /// <returns></returns>
        private static async Task<bool> INTERNAL_SetModState(EModState state, string path, bool allowInternal = false, ModTransaction tx = null)
        {
            if(state == EModState.Invalid)
            {
                throw new Exception("Cannot intentionally set Mod State to Invalid.");
            }

            var boiler = await TxBoiler.BeginWrite(tx);
            tx = boiler.Transaction;
            try
            {
                var nMod = await tx.GetMod(path);
                if (nMod == null)
                {
                    if(state != EModState.UnModded)
                    {
                        throw new Exception("Cannot change state for non-existent mod.");
                    }

                    // No change.
                    return false;
                }
                var mod = nMod.Value;
                var curState = await mod.GetState(tx);

                if (state == curState)
                {
                    await boiler.Cancel(true);
                    return false;
                }

                if (mod.OriginalOffset8x < 0 && state == EModState.Disabled)
                {
                    throw new Exception("Cannot disable mod with invalid original offset.");
                }

                if (state == EModState.Enabled && mod.ModOffset8x < 0)
                {
                    throw new Exception("Cannot enable mod with invalid mod offset.");
                }
            
                if(mod.IsInternal() && !allowInternal)
                {
                    // Don't allow toggling internal mods unless we were specifically told to.
                    await boiler.Cancel(true);
                    return false;
                }

                var df = IOUtil.GetDataFileFromPath(mod.FilePath);


                var modList = await tx.GetModList();
                var index = await tx.GetIndexFile(df);

                tx.ModPack = mod.GetModPack(modList);

                if (state == EModState.Enabled)
                {
                    // Enabling Mod
                    index.Set8xDataOffset(mod.FilePath, mod.ModOffset8x);
                    var ext = Path.GetExtension(mod.FilePath);
                    if (ext == ".meta")
                    {
                        await ItemMetadata.ApplyMetadata(mod.FilePath, false, tx);
                    }
                    else if (ext == ".rgsp")
                    {
                        await CMP.ApplyRgspFile(mod.FilePath, false, tx);
                    }
                }
                else if (state == EModState.Disabled)
                {
                    // Disabling mod.
                    index.Set8xDataOffset(mod.FilePath, mod.OriginalOffset8x);

                    // Restore original metadata.
                    if (mod.FilePath.EndsWith(".meta"))
                    {
                        var root = await XivCache.GetFirstRoot(mod.FilePath);
                        await ItemMetadata.RestoreDefaultMetadata(root, tx);
                    }

                    if (mod.FilePath.EndsWith(".rgsp"))
                    {
                        await CMP.RestoreDefaultScaling(mod.FilePath, tx);
                    }
                } else if(state == EModState.UnModded)
                {
                    await INTERNAL_DeleteMod(path, allowInternal, tx);
                }

                XivCache.QueueDependencyUpdate(path);
                await boiler.Commit();
            }
            catch(Exception ex)
            {
                await boiler.Catch();
                throw;
            }


            return true;
        }

        /// <summary>
        /// Deletes a mod from the modlist, if it exists.
        /// </summary>
        public static async Task DeleteMod(Mod mod, ModTransaction tx = null)
        {
            await INTERNAL_DeleteMod(mod.FilePath, false, tx);
        }

        /// <summary>
        /// Deletes a mod from the modlist, if it exists.
        /// </summary>
        public static async Task DeleteMod(string path, ModTransaction tx = null)
        {
            await INTERNAL_DeleteMod(path, false, tx);
        }

        /// <summary>
        /// Deletes a mod from the modlist, if it exists.
        /// </summary>
        private static async Task INTERNAL_DeleteMod(string path, bool allowInternal = false, ModTransaction tx = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Deleted Mod Path cannot be blank.");
            }

            var boiler = await TxBoiler.BeginWrite(tx);
            tx = boiler.Transaction;
            try
            {
                var modList = await tx.GetModList();

                var nmod = modList.GetMod(path);

                // Mod doesn't exist in the modlist.
                if (nmod == null) return;

                var modToRemove = nmod.Value;

                if (modToRemove.IsInternal() && !allowInternal)
                {
                    throw new Exception("Cannot delete internal data without explicit toggle.");
                }

                if(!Dat.IsOffsetSane(nmod.Value.DataFile, nmod.Value.OriginalOffset8x, true))
                {
                    throw new OffsetException("Mod does not have a valid original offset, cannot safely delete mod.");
                }

                await INTERNAL_SetModState(EModState.Disabled, path, allowInternal, tx);
                modList.RemoveMod(modToRemove);

                await boiler.Commit();
            }
            catch
            {
                await boiler.Catch();
                throw;
            }
        }

        /// <summary>
        /// Sets a given Modpack's state.
        /// </summary>
        /// <param name="state"></param>
        /// <param name="modPack"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        public static async Task SetModpackState(EModState state, ModPack modPack, ModTransaction tx = null)
        {
            await SetModpackState(state, modPack.Name, tx);
        }

        /// <summary>
        /// Sets a given Modpack's state, if it exists.
        /// </summary>
        /// <param name="state"></param>
        /// <param name="modPack"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        public static async Task SetModpackState(EModState state, string modPackName, ModTransaction tx = null)
        {

            var boiler = await TxBoiler.BeginWrite(tx);
            tx = boiler.Transaction;
            try
            {
                var ml = await tx.GetModList();
                var mp = ml.GetModPack(modPackName);
                if(mp == null)
                {
                    return;
                }

                var pack = mp.Value;
                var mods = pack.Mods;

                foreach(var mod in mods)
                {
                    await SetModState(state, mod, tx);
                }

                if(state == EModState.UnModded)
                {
                    ml.RemoveModpack(pack);
                }

                await boiler.Commit();
            }
            catch
            {
                await boiler.Catch();
                throw;
            }

        }

        /// <summary>
        /// Deletes a Mod Pack and all its mods from the modlist
        /// </summary>
        /// <param name="modPackName">The name of the Mod Pack to be deleted</param>
        public static async Task DeleteModPack(ModPack modPack, ModTransaction tx = null)
        {
            await DeleteModPack(modPack.Name, tx);
        }

        /// <summary>
        /// Deletes a Mod Pack and all its mods from the modlist
        /// </summary>
        /// <param name="modPackName">The name of the Mod Pack to be deleted</param>
        public static async Task DeleteModPack(string modPackName, ModTransaction tx = null)
        {
            await SetModpackState(EModState.UnModded, modPackName, tx);
        }


        /// <summary>
        /// Set the state of all mods.
        /// </summary>
        /// <param name="enable">The status to switch the mods to True if enable False if disable</param>
        public static async Task SetAllModStates(EModState state, IProgress<(int current, int total, string message)> progress = null, ModTransaction tx = null)
        {
            if(state == EModState.Invalid)
            {
                // This would error down chain anyways, but we can catch it early for some perf.
                throw new Exception("Cannot intentionally set Invalid Mod State.");
            }

            var boiler = await TxBoiler.BeginWrite(tx);
            tx = boiler.Transaction;
            try
            {
                var modList = await tx.GetModList();
                var toRemove = modList.Mods.Where(x => !x.Value.IsInternal()).Select(x => x.Value.FilePath);
                await SetModStates(state, toRemove, progress, tx);

                toRemove = modList.Mods.Where(x => x.Value.IsInternal()).Select(x => x.Value.FilePath);

                // If we're clearing or disabling everything, delete all our internal files.
                // This both helps reduce IMC bloat and helps ensure clean states are actually /clean/
                if(state == EModState.UnModded || state == EModState.Disabled)
                {
                    foreach (var mod in toRemove) {
                        await INTERNAL_DeleteMod(mod, true, tx);
                    }

                }


                await boiler.Commit();

                if (boiler.OwnTx && (state == EModState.UnModded || state == EModState.Disabled))
                {
                    // This is a little cheatyface here, but if we just disabled everything, we can do a
                    // soft 'start over' and adjust the index counts back so our final indexes are bytewise identical to originals.
                    Index.UNSAFE_ResetAllIndexDatCounts();
                }
            }
            catch
            {
                await boiler.Catch();
                throw;
            }
        }

        /// <summary>
        /// Set the states of a given subset of mods.
        /// </summary>
        /// <param name="state"></param>
        /// <param name="filePaths"></param>
        /// <param name="progress"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        public static async Task SetModStates(EModState state, IEnumerable<string> filePaths, IProgress<(int current, int total, string message)> progress = null, ModTransaction tx = null)
        {
            var boiler = await TxBoiler.BeginWrite(tx);
            tx = boiler.Transaction;
            try
            {
                var total = 0;
                if (progress != null)
                {
                    // Only enumerate here if it actually matters.
                    total = filePaths.Count();
                }

                var verb = "Enabling";
                switch (state)
                {
                    case EModState.Enabled:
                        verb = "Enabling ";
                        break;
                    case EModState.Disabled:
                        verb = "Disabling ";
                        break;
                    case EModState.UnModded:
                        verb = "Deleting ";
                        break;
                }

                var count = 0;
                foreach (var file in filePaths)
                {
                    await SetModState(state, file, tx);

                    progress?.Report((count, total, verb + " Mods..."));
                    count++;
                }

                await boiler.Commit();
            }
            catch
            {
                await boiler.Catch();
            }

        }


        /// <summary>
        /// Determines if any mods, at all, are enabled currently.
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        public static async Task<bool> AnyModsEnabled(ModTransaction tx = null)
        {
            if (tx == null)
            {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginReadonlyTransaction();
            }
            var ml = await tx.GetModList();

            var mods = ml.GetMods().ToList();
            if (mods.Count == 0) return false;

            foreach (var mod in mods)
            {
                var state = await mod.GetState(tx);
                if (state == EModState.Enabled || state == EModState.Invalid)
                {
                    return true;
                }
            }
            return false;
        }


        /// <summary>
        /// Retrieves the list of enabled or invalid state mods.
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        public static async Task<List<Mod>> GetActiveMods(ModTransaction tx = null)
        {
            if (tx == null)
            {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginReadonlyTransaction();
            }
            var ml = await tx.GetModList();
            var mods = ml.GetMods().ToList();

            var active = new List<Mod>();
            foreach (var x in mods)
            {
                var state = await x.GetState(tx);
                if (state == EModState.Enabled || state == EModState.Invalid)
                {
                    active.Add(x);
                }
            }
            return active;
        }

        #endregion

        #region One-Off Functions 

        /// <summary>
        /// Fixes the Item Names and Item Categories of all mods to be consistent.
        /// </summary>
        /// <returns></returns>
        public static async Task CleanUpModlistItems(IProgress<(int Current, int Total, string Message)> progressReporter = null, ModTransaction tx = null)
        {
            if (!XivCache.GameWriteEnabled)
            {
                throw new Exception("Cannot alter game files while FFXIV file writing is disabled.");
            }

            progressReporter?.Report((0, 0, "Loading Modlist file..."));

            var boiler = await TxBoiler.BeginWrite(tx);
            tx = boiler.Transaction;
            try
            {
                var modlist = await tx.GetModList();

                // IMC entries cache so we're not having to constantly re-load the IMC data.
                Dictionary<XivDependencyRoot, List<XivImc>> ImcEntriesCache = new Dictionary<XivDependencyRoot, List<XivImc>>();

                var totalMods = modlist.Mods.Count;


                var count = 0;
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
                            var data = await ResolveMtrlModInfo(mod.FilePath, root, ImcEntriesCache, tx);
                            mod.ItemName = data.Name;
                            mod.ItemCategory = data.Category;

                        }
                        else if (Imc.UsesImc(root) && ext == ".tex")
                        {
                            // For textures, get the first material using them, and list them under that material's item.
                            var parents = await XivCache.GetParentFiles(mod.FilePath, tx);
                            if (parents.Count == 0)
                            {
                                item = root.GetFirstItem();
                                mod.ItemName = item.GetModlistItemName();
                                mod.ItemCategory = item.GetModlistItemCategory();
                                continue;
                            }

                            var parent = parents[0];

                            var data = await ResolveMtrlModInfo(parent, root, ImcEntriesCache, tx);
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


                if (boiler.OwnTx)
                {
                    progressReporter?.Report((0, 0, "Saving Modlist file..."));
                }

                await boiler.Commit();
            }
            catch
            {
                await boiler.Cancel();
            }
        }

        /// <summary>
        /// Fluff function for resolving item name/category for MTRLs based on their material set folder.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="root"></param>
        /// <param name="ImcEntriesCache"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        private static async Task<(string Name, string Category)> ResolveMtrlModInfo(string path, XivDependencyRoot root, Dictionary<XivDependencyRoot, List<XivImc>> ImcEntriesCache, ModTransaction tx = null)
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
                        tx = ModTransaction.BeginReadonlyTransaction();
                    }

                    imcEntries = await Imc.GetEntries(await root.GetImcEntryPaths(tx), false, tx);
                    ImcEntriesCache.Add(root, imcEntries);
                }
                var mSetId = Int32.Parse(match.Groups[1].Value);

                // The list from this function is already sorted.
                var allItems = await root.GetAllItems(-1, tx);


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

        #endregion

    }

}