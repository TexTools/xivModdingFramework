using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public static async Task<Dictionary<string, string>> CloneRoot(XivDependencyRoot Source, XivDependencyRoot Destination, string ApplicationSource, int singleVariant = -1, string saveDirectory = null, IProgress<string> ProgressReporter = null, IndexFile index = null, ModList modlist = null, ModPack modPack = null)
        {
            if(!IsSupported(Source) || !IsSupported(Destination))
            {
                throw new InvalidDataException("Cannot clone unsupported root.");
            }

            if(XivCache.GameInfo.UseLumina)
            {
                // Using the root cloner with Lumina import mode enabled is unstable/not safe.
                // ( The state of the game files in the TT system may not match the state of the 
                // Lumina mod setup, and the modlist/etc. can get bashed as this function directly
                // modifies the modlist during the cloning process. )
                throw new Exception("Item Conversion cannot be used with Lumina import mode.");
            }


            if (ProgressReporter != null)
            {
                ProgressReporter.Report("Stopping Cache Worker...");
            }
            var workerStatus = XivCache.CacheWorkerEnabled;
            XivCache.CacheWorkerEnabled = false;
            try
            {
                var df = IOUtil.GetDataFileFromPath(Source.ToString());

                var _imc = new Imc(XivCache.GameInfo.GameDirectory);
                var _mdl = new Mdl(XivCache.GameInfo.GameDirectory, df);
                var _dat = new Dat(XivCache.GameInfo.GameDirectory);
                var _index = new Index(XivCache.GameInfo.GameDirectory);
                var _mtrl = new Mtrl(XivCache.GameInfo.GameDirectory);
                var _modding = new Modding(XivCache.GameInfo.GameDirectory);

                var doSave = false;
                if (index == null)
                {
                    doSave = true;
                    index = await _index.GetIndexFile(df);
                    modlist = await _modding.GetModListAsync();
                }


                bool locked = _index.IsIndexLocked(df);
                if(locked)
                {
                    throw new Exception("Game files currently in use.");
                }


                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Analyzing items and variants...");
                }

                // First, try to get everything, to ensure it's all valid.
                ItemMetadata originalMetadata = await GetCachedMetadata(index, modlist, Source, df, _dat);


                var originalModelPaths = await Source.GetModelFiles(index, modlist);
                var originalMaterialPaths = await Source.GetMaterialFiles(-1, index, modlist);
                var originalTexturePaths = await Source.GetTextureFiles(-1, index, modlist);

                var originalVfxPaths = new HashSet<string>();
                if (Imc.UsesImc(Source))
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

                // First, get a new, clean copy of the metadata, pointed at the new root.
                var newMetadata = await GetCachedMetadata(index, modlist, Source, df, _dat);
                newMetadata.Root = Destination.Info.ToFullRoot();
                ItemMetadata originalDestinationMetadata = null;
                try
                {
                    originalDestinationMetadata = await GetCachedMetadata(index, modlist, Destination, df, _dat);
                } catch
                {
                    originalDestinationMetadata = new ItemMetadata(Destination);
                }

                // Set 0 needs special handling.
                if(Source.Info.PrimaryType == XivItemType.equipment && Source.Info.PrimaryId == 0)
                {
                    var set1Root = new XivDependencyRoot(Source.Info.PrimaryType, 1, null, null, Source.Info.Slot);
                    var set1Metadata = await GetCachedMetadata(index, modlist, set1Root, df, _dat);

                    newMetadata.EqpEntry = set1Metadata.EqpEntry;

                    if (Source.Info.Slot == "met")
                    {
                        newMetadata.GmpEntry = set1Metadata.GmpEntry;
                    }
                } else if (Destination.Info.PrimaryType == XivItemType.equipment && Destination.Info.PrimaryId == 0)
                {
                    newMetadata.EqpEntry = null;
                    newMetadata.GmpEntry = null;
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

                var destItem = Destination.GetFirstItem();
                var srcItem = (await Source.GetAllItems(singleVariant))[0];
                var iCat = destItem.SecondaryCategory;
                var iName = destItem.Name;


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
                    var allMods = modlist.Mods.ToList();
                    foreach (var mod in allMods)
                    {
                        if (mod.fullPath.StartsWith(dPath) && !mod.IsInternal())
                        {
                            if (Destination.Info.SecondaryType != null || Destination.Info.Slot == null)
                            {
                                // If this is a slotless root, purge everything.
                                await _modding.DeleteMod(mod.fullPath, false, index, modlist);
                            }
                            else if (allFiles.Contains(mod.fullPath) || mod.fullPath.Contains(Destination.Info.GetBaseFileName(true)))
                            {
                                // Otherwise, only purge the files we're replacing, and anything else that
                                // contains our slot name.
                                await _modding.DeleteMod(mod.fullPath, false, index, modlist);
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
                    var offset = index.Get8xDataOffset(src);
                    var xmdl = await _mdl.GetRawMdlData(src, false, offset);
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

                    // Save new Model.
                    var bytes = await _mdl.MakeNewMdlFile(tmdl, xmdl, null);
                    await _dat.WriteModFile(bytes, dst, ApplicationSource, destItem, index, modlist);
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

                    await _dat.CopyFile(src, dst, ApplicationSource, true, destItem, index, modlist);
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
                        var offset = index.Get8xDataOffset(src);
                        if (offset == 0) continue;
                        var xivMtrl = await _mtrl.GetMtrlData(offset, src, 11);
                        xivMtrl.MTRLPath = dst;

                        for (int i = 0; i < xivMtrl.TexturePathList.Count; i++)
                        {
                            foreach (var tkv in newTexturePaths)
                            {
                                xivMtrl.TexturePathList[i] = xivMtrl.TexturePathList[i].Replace(tkv.Key, tkv.Value);
                            }
                        }

                        await _mtrl.ImportMtrl(xivMtrl, destItem, ApplicationSource, index, modlist);
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

                    await _dat.CopyFile(src, dst, ApplicationSource, true, destItem, index, modlist);
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

                await ItemMetadata.SaveMetadata(newMetadata, ApplicationSource, index, modlist);

                // Save the new Metadata file via the batch function so that it's only written to the memory cache for now.
                await ItemMetadata.ApplyMetadataBatched(new List<ItemMetadata>() { newMetadata }, index, modlist, false);

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

                            // Copy the material over.
                            await _dat.CopyFile(existentCopy, destPath, ApplicationSource, true, destItem, index, modlist);
                        }
                    }
                }

                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Updating modlist...");
                }

                if (modPack == null)
                {
                    modPack = new ModPack() { author = "System", name = "Item Copy - " + srcItem.Name + " to " + iName, url = "", version = "1.0" };
                }

                List<Mod> mods = new List<Mod>();
                foreach (var mod in modlist.Mods)
                {
                    if (allFiles.Contains(mod.fullPath))
                    {
                        // Ensure all of our modified files are attributed correctly.
                        mod.name = iName;
                        mod.category = iCat;
                        mod.source = ApplicationSource;
                        mod.modPack = modPack;

                        mods.Add(mod);
                    }
                }

                if (!modlist.ModPacks.Any(x => x.name == modPack.name))
                {
                    modlist.ModPacks.Add(modPack);
                }

                if(doSave)
                {
                    // Save everything.
                    await _index.SaveIndexFile(index);
                    await _modding.SaveModListAsync(modlist);
                }

                XivCache.QueueDependencyUpdate(allFiles.ToList());

                if(saveDirectory != null)
                {

                    ProgressReporter.Report("Creating TTMP File...");
                    var desc = "Item Converter Modpack - " + srcItem.Name + " -> " + iName + "\nCreated at: " + DateTime.Now.ToString();
                    // Time to save the modlist to file.
                    var dir = new DirectoryInfo(saveDirectory);
                    var _ttmp = new TTMP(dir, ApplicationSource);
                    var smpd = new SimpleModPackData()
                    {
                        Author = modPack.author,
                        Description = desc,
                        Url = modPack.url,
                        Version = new Version(1, 0, 0),
                        Name = modPack.name,
                        SimpleModDataList = new List<SimpleModData>()
                    };

                    foreach(var mod in mods)
                    {
                        var size = await _dat.GetCompressedFileSize(mod.data.modOffset, df);
                        var smd = new SimpleModData()
                        {
                            Name = iName,
                            FullPath = mod.fullPath,
                            DatFile = df.GetDataFileName(),
                            Category = iCat,
                            IsDefault = false,
                            ModSize = size,
                            ModOffset = mod.data.modOffset
                        };
                        smpd.SimpleModDataList.Add(smd);
                    }

                    await _ttmp.CreateSimpleModPack(smpd, XivCache.GameInfo.GameDirectory, null, true);
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

            } finally
            {
                XivCache.CacheWorkerEnabled = workerStatus;
            }
        }

        private static async Task<ItemMetadata> GetCachedMetadata(IndexFile index, ModList modlist, XivDependencyRoot root, XivDataFile df, Dat _dat)
        {
            var originalMetadataOffset = index.Get8xDataOffset(root.Info.GetRootFile());
            ItemMetadata originalMetadata = null;
            if (originalMetadataOffset == 0)
            {
                originalMetadata = await ItemMetadata.GetMetadata(root);
            }
            else
            {
                var data = await _dat.GetType2Data(originalMetadataOffset, df);
                originalMetadata = await ItemMetadata.Deserialize(data);
            }
            return originalMetadata;
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

            // Things that live in the common folder get to stay there/don't get copied.

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

            var rex = new Regex("[a-z][0-9]{4}([a-z][0-9]{4})");
            var match = rex.Match(file);
            if(!match.Success)
            {
                return file;
            }

            if (Source.Info.SecondaryType == null)
            {
                // Equipment/Accessory items. Only replace the back half of the file names.
                var srcString = match.Groups[1].Value;
                var dstString = Destination.Info.GetBaseFileName(false);

                file = file.Replace(srcString, dstString);
            } else
            {
                // Replace the entire root chunk for roots that have two identifiers.
                var srcString = match.Groups[0].Value;
                var dstString = Destination.Info.GetBaseFileName(false);

                file = file.Replace(srcString, dstString);
            }

            return file;
        }
    }
}
