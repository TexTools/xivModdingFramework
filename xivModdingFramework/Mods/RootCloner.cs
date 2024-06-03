using SharpDX.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Materials.FileTypes;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Models.Helpers;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.FileTypes;
using xivModdingFramework.Variants.DataContainers;
using xivModdingFramework.Variants.FileTypes;

using Index = xivModdingFramework.SqPack.FileTypes.Index;

namespace xivModdingFramework.Mods
{
    public static class RootCloner
    {

        public static bool IsSupported(XivDependencyRoot root)
        {
            if (root.Info.PrimaryType == XivItemType.weapon) return true;
            if (root.Info.PrimaryType == XivItemType.equipment) return true;
            if (root.Info.PrimaryType == XivItemType.accessory) return true;
            if (root.Info.PrimaryType == XivItemType.human && root.Info.SecondaryType == XivItemType.hair) return true;

            return false;
        }

        /// <summary>
        /// Copies the entirety of a given root to a new root.  
        /// </summary>
        /// <param name="Source">Original Root to copy from.</param>
        /// <param name="Destination">Destination root to copy to.</param>
        /// <param name="ApplicationSource">Application to list as the source for the resulting mod entries.</param>
        /// <returns>Returns a Dictionary of all the file conversion</returns>
        public static async Task<Dictionary<string, string>> CloneRoot(XivDependencyRoot Source, XivDependencyRoot Destination, string ApplicationSource, int singleVariant = -1, string saveDirectory = null, IProgress<string> ProgressReporter = null, ModTransaction tx = null)
        {
            if(!IsSupported(Source) || !IsSupported(Destination))
            {
                throw new InvalidDataException("Cannot clone unsupported root.");
            }

            if (ProgressReporter != null)
            {
                ProgressReporter.Report("Stopping Cache Worker...");
            }

            var boiler = TxBoiler.BeginWrite(ref tx);
            try
            {
                var destItem = Destination.GetFirstItem();
                var srcItem = (await Source.GetAllItems(singleVariant, tx))[0];
                var iCat = destItem.SecondaryCategory;
                var iName = destItem.Name;

                var modPack = new ModPack(null) { Author = "System", Name = "Item Copy - " + srcItem.Name + " to " + iName, Url = "", Version = "1.0" };
                boiler.OwnModpack = modPack;


                var df = IOUtil.GetDataFileFromPath(Source.ToString());
                var index = await tx.GetIndexFile(df);
                var modlist = await tx.GetModList();



                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Analyzing items and variants...");
                }

                // First, try to get everything, to ensure it's all valid.
                ItemMetadata originalMetadata = await GetCachedMetadata(Source, tx);


                var originalModelPaths = await Source.GetModelFiles(tx);
                var originalMaterialPaths = await Source.GetMaterialFiles(-1, tx, false);
                var originalTexturePaths = await Source.GetTextureFiles(-1, tx);

                var originalVfxPaths = new HashSet<string>();
                if (Imc.UsesImc(Source) && Imc.UsesImc(Destination))
                {
                    var avfxSets = originalMetadata.ImcEntries.Select(x => x.Vfx).Distinct();
                    foreach (var avfx in avfxSets)
                    {
                        var avfxStuff = await ATex.GetVfxPath(Source.Info, avfx);
                        if (String.IsNullOrEmpty(avfxStuff.Folder) || String.IsNullOrEmpty(avfxStuff.File)) continue;

                        var path = avfxStuff.Folder + "/" + avfxStuff.File;
                        if (index.FileExists(path))
                        {
                            originalVfxPaths.Add(path);
                        }
                    }
                }

                // Time to start editing things.
                bool crossTypeSwap = false;

                // First, get a new, clean copy of the metadata, pointed at the new root.
                ItemMetadata newMetadata;
                ItemMetadata originalDestinationMetadata = null;

                try
                {
                    originalDestinationMetadata = await GetCachedMetadata(Destination, tx);
                }
                catch
                {
                    originalDestinationMetadata = new ItemMetadata(Destination);
                }

                if (Source.Info.PrimaryType == Destination.Info.PrimaryType)
                {
                    newMetadata = await GetCachedMetadata(Source, tx);
                    newMetadata.Root = Destination.Info.ToFullRoot();

                    // Set 0 needs special handling.
                    if (Source.Info.PrimaryType == XivItemType.equipment && Source.Info.PrimaryId == 0)
                    {
                        var set1Root = new XivDependencyRoot(Source.Info.PrimaryType, 1, null, null, Source.Info.Slot);
                        var set1Metadata = await GetCachedMetadata(set1Root, tx);

                        newMetadata.EqpEntry = set1Metadata.EqpEntry;

                        if (Source.Info.Slot == "met")
                        {
                            newMetadata.GmpEntry = set1Metadata.GmpEntry;
                        }
                    }
                    else if (Destination.Info.PrimaryType == XivItemType.equipment && Destination.Info.PrimaryId == 0)
                    {
                        newMetadata.EqpEntry = null;
                        newMetadata.GmpEntry = null;
                    }

                }
                else if((Source.Info.PrimaryType == XivItemType.equipment || Source.Info.PrimaryType == XivItemType.accessory) && Destination.Info.PrimaryType == XivItemType.accessory)
                {
                    crossTypeSwap = true;

                    // For this we have to work from a fresh copy the destination and alter it to resemble the source.
                    newMetadata = await ItemMetadata.GetMetadata(Destination, true, tx);
                    var sourceMetadata = await ItemMetadata.GetMetadata(Source, false, tx);

                    // Accessories only have these two types of metadata, which don't internally care about where they came from.
                    newMetadata.ImcEntries = sourceMetadata.ImcEntries;
                    newMetadata.EqdpEntries = sourceMetadata.EqdpEntries;
                } else
                {
                    throw new InvalidDataException("Cannot convert non-same-type root other than Equipment => Accessory.");
                }


                // Now figure out the path names for all of our new paths.
                // These dictionarys map Old Path => New Path
                Dictionary<string, string> newModelPaths = new Dictionary<string, string>();
                Dictionary<string, string> newMaterialPaths = new Dictionary<string, string>();
                Dictionary<string, string> newMaterialFileNames = new Dictionary<string, string>();
                Dictionary<string, string> newTexturePaths = new Dictionary<string, string>();
                Dictionary<string, string> newAvfxPaths = new Dictionary<string, string>();

                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Calculating files to copy...");
                }

                // For each path, replace any instances of our primary and secondary types.
                foreach (var path in originalModelPaths)
                {
                    newModelPaths.Add(path, UpdatePath(Source, Destination, path));
                }

                foreach (var path in originalMaterialPaths)
                {
                    var nPath = UpdatePath(Source, Destination, path);
                    newMaterialPaths.Add(path, nPath);
                    var fName = Path.GetFileName(path);

                    if (!newMaterialFileNames.ContainsKey(fName))
                    {
                        newMaterialFileNames.Add(fName, Path.GetFileName(nPath));
                    }
                }

                foreach (var path in originalTexturePaths)
                {
                    newTexturePaths.Add(path, UpdatePath(Source, Destination, path));
                }

                foreach (var path in originalVfxPaths)
                {
                    newAvfxPaths.Add(path, UpdatePath(Source, Destination, path));
                }



                var files = newModelPaths.Select(x => x.Value).Union(
                    newMaterialPaths.Select(x => x.Value)).Union(
                    newAvfxPaths.Select(x => x.Value)).Union(
                    newTexturePaths.Select(x => x.Value));
                var allFiles = new HashSet<string>();
                foreach (var f in files)
                {
                    allFiles.Add(f);
                }

                allFiles.Add(Destination.Info.GetRootFile());

                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Getting modlist...");
                }

                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Removing existing modifications to destination root...");
                }

                if (Destination != Source)
                {
                    var dPath = Destination.Info.GetRootFolder();
                    var allMods = modlist.GetMods().ToList();
                    foreach (var mod in allMods)
                    {
                        if (mod.FilePath.StartsWith(dPath) && !mod.IsInternal())
                        {
                            if (Destination.Info.SecondaryType != null || Destination.Info.Slot == null)
                            {
                                // If this is a slotless root, purge everything.
                                await Modding.DeleteMod(mod.FilePath, tx);
                            }
                            else if (allFiles.Contains(mod.FilePath) || mod.FilePath.Contains(Destination.Info.GetBaseFileName(true)))
                            {
                                // Otherwise, only purge the files we're replacing, and anything else that
                                // contains our slot name.
                                await Modding.DeleteMod(mod.FilePath, tx);
                            }
                        }
                    }
                }

                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Copying models...");
                }

                // Load, Edit, and resave the model files.
                foreach (var kv in newModelPaths)
                {
                    var src = kv.Key;
                    var dst = kv.Value;
                    var xmdl = await Mdl.GetXivMdl(src, false, tx);
                    var tmdl = TTModel.FromRaw(xmdl);

                    if (xmdl == null || tmdl == null)
                        continue;

                    tmdl.Source = dst;
                    xmdl.MdlPath = dst;

                    // Replace any material references as needed.
                    foreach (var m in tmdl.MeshGroups)
                    {
                        foreach (var matKv in newMaterialFileNames)
                        {
                            m.Material = m.Material.Replace(matKv.Key, matKv.Value);
                        }
                    }

                    if(crossTypeSwap && Destination.Info.PrimaryType == XivItemType.accessory)
                    {
                        // We want to remove any skin meshes here.
                        var groups = tmdl.MeshGroups.ToList();
                        var rex = ModelModifiers.SkinMaterialRegex;
                        foreach (var mg in groups)
                        {
                            var match = rex.Match(mg.Material);
                            if (match.Success)
                            {
                                tmdl.MeshGroups.Remove(mg);
                            }
                        }
                    }

                    // Save new Model.
                    var bytes = await Mdl.MakeCompressedMdlFile(tmdl, xmdl);
                    var newMdlOffset = await Dat.WriteModFile(bytes, dst, ApplicationSource, destItem, tx);
                }

                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Copying textures...");
                }

                // Raw Copy all Texture files to the new destinations to avoid having the MTRL save functions auto-generate blank textures.
                foreach (var kv in newTexturePaths)
                {
                    var src = kv.Key;
                    var dst = kv.Value;

                    await Dat.CopyFile(src, dst, ApplicationSource, true, destItem, tx);
                }


                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Copying materials...");
                }
                HashSet<string> CopiedMaterials = new HashSet<string>();

                // Load every Material file and edit the texture references to the new texture paths.
                foreach (var kv in newMaterialPaths)
                {
                    var src = kv.Key;
                    var dst = kv.Value;
                    try
                    {
                        var exists = await tx.FileExists(src);
                        if (!exists) continue;
                        var xivMtrl = await Mtrl.GetXivMtrl(src, false, tx);
                        xivMtrl.MTRLPath = dst;

                        for (int i = 0; i < xivMtrl.Textures.Count; i++)
                        {
                            foreach (var tkv in newTexturePaths)
                            {
                                xivMtrl.Textures[i].TexturePath = xivMtrl.Textures[i].TexturePath.Replace(tkv.Key, tkv.Value);
                            }
                        }

                        await Mtrl.ImportMtrl(xivMtrl, destItem, ApplicationSource, false, tx);
                        CopiedMaterials.Add(dst);
                    }
                    catch (Exception ex)
                    {
                        // Let functions later handle this mtrl then.
                    }
                }

                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Copying VFX...");
                }
                // Copy VFX files.
                foreach (var kv in newAvfxPaths)
                {
                    var src = kv.Key;
                    var dst = kv.Value;

                    await Dat.CopyFile(src, dst, ApplicationSource, true, destItem, tx);
                }

                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Creating missing variants...");
                }

                // Check to see if we need to add any variants
                var cloneNum = newMetadata.ImcEntries.Count >= 2 ? 1 : 0;
                while (originalDestinationMetadata.ImcEntries.Count > newMetadata.ImcEntries.Count)
                {
                    // Clone Variant 1 into the variants we are missing.
                    newMetadata.ImcEntries.Add((XivImc)newMetadata.ImcEntries[cloneNum].Clone());
                }


                if (singleVariant >= 0)
                {
                    if (ProgressReporter != null)
                    {
                        ProgressReporter.Report("Setting single-variant data...");
                    }

                    if (singleVariant < newMetadata.ImcEntries.Count)
                    {
                        var v = newMetadata.ImcEntries[singleVariant];

                        for(int i = 0; i < newMetadata.ImcEntries.Count; i++)
                        {
                            newMetadata.ImcEntries[i] = (XivImc)v.Clone();
                        }
                    }
                }

                // Update Skeleton references to be for the correct set Id.
                var setId = Destination.Info.SecondaryId == null ? (ushort)Destination.Info.PrimaryId : (ushort)Destination.Info.SecondaryId;
                foreach(var entry in newMetadata.EstEntries)
                {
                    entry.Value.SetId = setId;
                }

                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Copying metdata...");
                }

                // Poke through the variants and adjust any that point to null Material Sets to instead use a valid one.
                if (newMetadata.ImcEntries.Count > 0 && originalMetadata.ImcEntries.Count > 0)
                {
                    var valid = newMetadata.ImcEntries.FirstOrDefault(x => x.MaterialSet != 0).MaterialSet;
                    if (valid <= 0)
                    {
                        valid = originalMetadata.ImcEntries.FirstOrDefault(x => x.MaterialSet != 0).MaterialSet;
                    }

                    for (int i = 0; i < newMetadata.ImcEntries.Count; i++)
                    {
                        var entry = newMetadata.ImcEntries[i];
                        if (entry.MaterialSet == 0)
                        {
                            entry.MaterialSet = valid;
                        }
                    }
                }

                // Save and apply metadata
                await ItemMetadata.SaveMetadata(newMetadata, ApplicationSource, tx);
                await ItemMetadata.ApplyMetadata(newMetadata, tx);

                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Filling in missing material sets...");
                }

                // Validate all variants/material sets for valid materials, and copy materials as needed to fix.
                if (Imc.UsesImc(Destination))
                {
                    var mSets = newMetadata.ImcEntries.Select(x => x.MaterialSet).Distinct();
                    foreach (var mSetId in mSets)
                    {
                        var path = Destination.Info.GetRootFolder() + "material/v" + mSetId.ToString().PadLeft(4, '0') + "/";
                        foreach (var mkv in newMaterialFileNames)
                        {
                            // See if the material was copied over.
                            var destPath = path + mkv.Value;
                            if (CopiedMaterials.Contains(destPath)) continue;

                            string existentCopy = null;

                            // If not, find a material where one *was* copied over.
                            foreach (var mSetId2 in mSets)
                            {
                                var p2 = Destination.Info.GetRootFolder() + "material/v" + mSetId2.ToString().PadLeft(4, '0') + "/";
                                foreach (var cmat2 in CopiedMaterials)
                                {
                                    if (cmat2 == p2 + mkv.Value)
                                    {
                                        existentCopy = cmat2;
                                        break;
                                    }
                                }
                            }

                            // Shouldn't ever actually hit this, but if we do, nothing to be done about it.
                            if (existentCopy == null) continue;

                            if (!allFiles.Contains(destPath))
                            {
                                allFiles.Add(destPath);
                            }

                            // Copy the material over.
                            await Dat.CopyFile(existentCopy, destPath, ApplicationSource, true, destItem, tx);
                        }
                    }
                }

                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Updating modlist...");
                }


                List<Mod> mods = new List<Mod>();
                var mListMods = modlist.GetMods();
                foreach (var mod in mListMods)
                {
                    if (allFiles.Contains(mod.FilePath))
                    {
                        // Ensure all of our modified files are attributed correctly.
                        var nMod = mod;
                        nMod.ItemName = iName;
                        nMod.ItemCategory = iCat;
                        nMod.SourceApplication = ApplicationSource;

                        if (tx.ModPack != null)
                        {
                            nMod.ModPack = tx.ModPack.Value.Name;
                        } else
                        {
                            nMod.ModPack = modPack.Name;
                        }

                        mods.Add(nMod);
                    }
                }

                // Save the changes.
                foreach(var mod in mods)
                {
                    modlist.AddOrUpdateMod(mod);
                }

                // Commit our transaction.
                await boiler.Commit();



                XivCache.QueueDependencyUpdate(allFiles.ToList());

                if(saveDirectory != null)
                {

                    ProgressReporter.Report("Creating TTMP File...");
                    var desc = "Item Converter Modpack - " + srcItem.Name + " -> " + iName + "\nCreated at: " + DateTime.Now.ToString();
                    // Time to save the modlist to file.
                    var smpd = new SimpleModPackData()
                    {
                        Author = modPack.Author,
                        Description = desc,
                        Url = modPack.Url,
                        Version = new Version(1, 0, 0),
                        Name = modPack.Name,
                        SimpleModDataList = new List<SimpleModData>()
                    };

                    foreach(var mod in mods)
                    {
                        var smd = new SimpleModData()
                        {
                            Name = iName,
                            FullPath = mod.FilePath,
                            DatFile = df.GetFileName(),
                            Category = iCat,
                            IsDefault = false,
                            ModOffset = mod.ModOffset8x
                        };
                        smpd.SimpleModDataList.Add(smd);
                    }

                    await TTMP.CreateSimpleModPack(smpd, saveDirectory, null, true, tx);
                }



                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Root copy complete.");
                }

                // Return the final file conversion listing.
                var ret = newModelPaths.Union(newMaterialPaths).Union(newAvfxPaths).Union(newTexturePaths);
                var dict = ret.ToDictionary(x => x.Key, x => x.Value);
                dict.Add(Source.Info.GetRootFile(), Destination.Info.GetRootFile());
                return dict;

            } catch (Exception ex)
            {
                await boiler.Catch();
                throw;
            }
        }

        private static async Task<ItemMetadata> GetCachedMetadata(XivDependencyRoot root, ModTransaction tx)
        {
            if(tx == null)
            {
                tx = ModTransaction.BeginTransaction();
            }

            var metaPath = root.Info.GetRootFile();
            ItemMetadata metadata = null;

            if (!await tx.FileExists(metaPath))
            {
                metadata = await ItemMetadata.GetMetadata(root, true, tx);
            }
            else
            {
                var data = await tx.ReadFile(metaPath);
                metadata = await ItemMetadata.Deserialize(data);
            }
            return metadata;
        }

        const string CommonPath = "chara/common/";
        private static readonly Regex RemoveRootPathRegex = new Regex("chara\\/[a-z]+\\/[a-z][0-9]{4}(?:\\/obj\\/[a-z]+\\/[a-z][0-9]{4})?\\/(.+)");

        internal static string UpdatePath(XivDependencyRoot Source, XivDependencyRoot Destination, string path)
        {
            // For common path items, copy them to our own personal mimic of the path.
            if (path.StartsWith(CommonPath))
            {
                var len = CommonPath.Length;
                var afterCommon = path.Substring(len);
                path = Destination.Info.GetRootFolder() + "common/" + afterCommon;
                return path;
            }

            var file = UpdateFileName(Source, Destination, path);
            var folder = UpdateFolder(Source, Destination, path);

            return folder + "/" + file;
        }

        private static string UpdateFolder(XivDependencyRoot Source, XivDependencyRoot Destination, string path)
        {
            if(Destination.Info.PrimaryType == XivItemType.human && Destination.Info.SecondaryType == XivItemType.hair && Path.GetExtension(path) == ".mtrl")
            {
                var hairRoot = Mtrl.GetHairMaterialRoot(Destination.Info);

                // Force the race code to the appropriate one.
                var raceReplace = new Regex("/c[0-9]{4}");
                path = raceReplace.Replace(path, "/c" + hairRoot.PrimaryId.ToString().PadLeft(4, '0'));

                var hairReplace= new Regex("/h[0-9]{4}");
                path = hairReplace.Replace(path, "/h" + hairRoot.SecondaryId.ToString().PadLeft(4, '0'));

                // Hairs between 115 and 200 have forced material path sharing enabled.
                path = Path.GetDirectoryName(path);
                path = path.Replace('\\', '/');
                return path;
            }

            // So first off, just copy anything from the old root folder to the new one.
            var match = RemoveRootPathRegex.Match(path);
            if(match.Success)
            {
                // The item existed in an old root path, so we can just clone the same post-root path into the new root folder.
                var afterRootPath = match.Groups[1].Value;
                path = Destination.Info.GetRootFolder() + afterRootPath;
                path = Path.GetDirectoryName(path);
                path = path.Replace('\\', '/');
                return path;
            }

            // Okay, stuff at this point didn't actually exist in any root path, and didn't exist in the common path either.
            // Just copy this crap into our root folder.

            // The only way we can really get here is if some mod author created textures in a totally arbitrary path.
            path = Path.GetDirectoryName(Destination.Info.GetRootFolder());
            path = path.Replace('\\', '/');
            return path;
        }

        private static string UpdateFileName(XivDependencyRoot Source, XivDependencyRoot Destination, string path)
        {
            var file = Path.GetFileName(path);

            if (Destination.Info.PrimaryType == XivItemType.human && Destination.Info.SecondaryType == XivItemType.hair && Path.GetExtension(path) == ".mtrl")
            {
                var hairRoot = Mtrl.GetHairMaterialRoot(Destination.Info);

                // Force replace the root information to the correct one for this target hair.
                var raceReplace = new Regex("^mt_c[0-9]{4}h[0-9]{4}");
                file = raceReplace.Replace(file, "mt_c" + hairRoot.PrimaryId.ToString().PadLeft(4, '0') + "h" + hairRoot.SecondaryId.ToString().PadLeft(4, '0'));

                // Jam in a suffix into the MTRL to make it unique/non-colliding.
                var initialPartRex = new Regex("^(mt_c[0-9]{4}h[0-9]{4})(?:_c[0-9]{4})?(.+)$");
                var m = initialPartRex.Match(file);

                // ???
                if (!m.Success) return file;

                file = m.Groups[1].Value + "_c" + Destination.Info.PrimaryId.ToString().PadLeft(4, '0') + m.Groups[2].Value;
                return file;
            }

            var rex = new Regex("[a-z][0-9]{4}([a-z][0-9]{4})(_[a-z]{3})?");
            var match = rex.Match(file);
            if(!match.Success)
            {
                // Doesn't contain a root name reference, can just copy it through.
                return file;
            }

            if (Source.Info.SecondaryType == null)
            {
                // Equipment/Accessory items. Only replace the front half of the file names and the slot.
                var srcString = match.Groups[1].Value;
                var dstString = Destination.Info.GetBaseFileName(false);

                file = file.Replace(srcString, dstString);

                // Has slotname.
                if(match.Groups.Count >= 3 && Destination.Info.Slot != null)
                {
                    var srcSlot = match.Groups[2].Value;
                    var dstSlot = "_" + Destination.Info.Slot;

                    if(srcSlot != "_" + Source.Info.Slot)
                    {
                        // Naughty modders cross-referencing materials and such.
                        // Can't leave name intact b/c it might bash.
                        dstSlot = srcSlot + dstSlot;
                    }

                    file = file.Replace(srcSlot, dstSlot);
                }


            } else
            {
                // Replace the entire root chunk for roots that have two identifiers.
                var srcString = match.Groups[0].Value;
                var dstString = Destination.Info.GetBaseFileName(false);

                file = file.Replace(srcString, dstString);
            }

            return file;
        }


        /// <summary>
        /// Clones a set of roots to a set of destination roots, in order, as part of a larger mod import transaction.
        /// The source roots are reset back to their original offsets after cloning.
        /// </summary>
        /// <param name="roots"></param>
        /// <param name="importedFiles"></param>
        /// <param name="tx"></param>
        /// <param name="readTx"></param>
        /// <param name="sourceApplication"></param>
        /// <returns></returns>
        internal static async Task<HashSet<string>> CloneAndResetRoots(Dictionary<XivDependencyRoot, (XivDependencyRoot Root, int Variant)> roots, HashSet<string> importedFiles, ModTransaction tx, Dictionary<string, TxFileState> originalStates, string sourceApplication, IProgress<(int current, int total, string message)> progress = null)
        {
            // We currently have all the files loaded into our in-memory indices in their default locations.
            var conversionsByDf = roots.GroupBy(x => IOUtil.GetDataFileFromPath(x.Key.Info.GetRootFile()));
            var newModList = await tx.GetModList();

            HashSet<string> clearedFiles = new HashSet<string>();
            var total = roots.Count;
            var count = 0;
            foreach (var dfe in conversionsByDf)
            {
                HashSet<string> filesToReset = new HashSet<string>();
                var df = dfe.Key;
                var newIndex = await tx.GetIndexFile(df);

                foreach (var conversion in dfe)
                {
                    var source = conversion.Key;
                    var destination = conversion.Value.Root;
                    var variant = conversion.Value.Variant;


                    progress.Report((count, total, "Updating Destination Items..."));
                    var convertedFiles = await RootCloner.CloneRoot(source, destination, sourceApplication, variant, null, null, tx);

                    // We're going to reset the original files back to their pre-modpack install state after, as long as they got moved.
                    foreach (var fileKv in convertedFiles)
                    {

                        if (fileKv.Key != fileKv.Value)
                        {
                            // Note the new file that was added.
                            importedFiles.Add(fileKv.Value);
                            
                            // Reset the original file.
                            filesToReset.Add(fileKv.Key);
                        }

                    }

                    // Remove any remaining lingering files which belong to this root.
                    // Ex. Extraneous unused files in the modpack.  This helps keep
                    // any unnecessary files from poluting the original destination item post conversion.
                    foreach (var file in importedFiles.ToList())
                    {
                        var root = XivDependencyGraph.ExtractRootInfo(file);
                        if (root == source.Info)
                        {
                            if (!filesToReset.Contains(file))
                            {
                                filesToReset.Add(file);
                            }
                        }
                    }

                    count++;
                }

                // Reset the states of all the requested files, and remove them form the import list.
                foreach (var file in filesToReset)
                {
                    if (originalStates.ContainsKey(file))
                    {
                        // Restore the state of the files.
                        await tx.RestoreFileState(originalStates[file]);
                        importedFiles.Remove(file);
                    }
                    else
                    {
                        // If we got here, we have mods that weren't in the original modpack import, that got included in the root clone.
                        // This should never happen if the modpack includes the entire subset of files necessary to properly fill out its item root.
                        
                        // If it /does/ happen, it means we just copied some unknown (possibly orphaned) files into the destination item directory.
                        // Which isn't necessarily dangerous, but isn't correct, either.
                        throw new Exception("Root CloneAndReset wanted to copy more files than were provided by the modpack import.");
                    }
                }
                clearedFiles.UnionWith(filesToReset);
            }
            return clearedFiles;
        }
    }
}
