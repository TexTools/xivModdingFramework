using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Materials.FileTypes;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.FileTypes;
using xivModdingFramework.Variants.DataContainers;
using xivModdingFramework.Variants.FileTypes;

namespace xivModdingFramework.Mods
{
    public static class RootCloner
    {
        /// <summary>
        /// Copies the entirety of a given root to a new root.  
        /// </summary>
        /// <param name="Source">Original Root to copy from.</param>
        /// <param name="Destination">Destination root to copy to.</param>
        /// <param name="ApplicationSource">Application to list as the source for the resulting mod entries.</param>
        /// <returns></returns>
        public static async Task CloneRoot(XivDependencyRoot Source, XivDependencyRoot Destination, string ApplicationSource, int singleVariant = -1, IProgress<string> ProgressReporter = null)
        {

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
                var _mtrl = new Mtrl(XivCache.GameInfo.GameDirectory, df, XivCache.GameInfo.GameLanguage);
                var _modding = new Modding(XivCache.GameInfo.GameDirectory);


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
                var originalMetadata = await ItemMetadata.GetMetadata(Source);
                var originalModelPaths = await Source.GetModelFiles();
                var originalMaterialPaths = await Source.GetMaterialFiles();
                var originalTexturePaths = await Source.GetTextureFiles();

                var originalVfxPaths = new HashSet<string>();
                if (Imc.UsesImc(Source))
                {
                    var avfxSets = originalMetadata.ImcEntries.Select(x => x.Vfx).Distinct();
                    foreach (var avfx in avfxSets)
                    {
                        var avfxStuff = await ATex.GetVfxPath(Source.Info, avfx);
                        var path = avfxStuff.Folder + "/" + avfxStuff.File;
                        if (await _index.FileExists(path))
                        {
                            originalVfxPaths.Add(path);
                        }
                    }
                }

                // Time to start editing things.

                // First, get a new, clean copy of the metadata, pointed at the new root.
                var newMetadata = await ItemMetadata.GetMetadata(Source);
                newMetadata.Root = Destination.Info.ToFullRoot();
                var originalDestinationMetadata = await ItemMetadata.GetMetadata(Destination);


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

                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Getting modlist...");
                }
                var modlist = await _modding.GetModListAsync();

                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Removing existing modifications to destination root...");
                }

                var dPath = Destination.Info.GetRootFolder();
                foreach (var mod in modlist.Mods)
                {
                    if (mod.fullPath.StartsWith(dPath) && !mod.IsInternal())
                    {
                        if (Destination.Info.Slot == null)
                        {
                            // If this is a slotless root, purge everything.
                            await _modding.DeleteMod(mod.fullPath, false);
                        }
                        else if(allFiles.Contains(mod.fullPath) || mod.fullPath.Contains(Destination.Info.Slot))
                        {
                            // Otherwise, only purge the files we're replacing, and anything else that
                            // contains our slot name.
                            await _modding.DeleteMod(mod.fullPath, false);
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
                    var xmdl = await _mdl.GetRawMdlData(src);
                    var tmdl = TTModel.FromRaw(xmdl);
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
                    await _dat.WriteToDat(bytes.ToList(), null, dst, iCat, iName, df, ApplicationSource, 3);
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

                    await _dat.CopyFile(src, dst, iCat, iName, ApplicationSource, true);
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
                        var offset = await _index.GetDataOffset(src);
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

                        await _mtrl.ImportMtrl(xivMtrl, destItem, ApplicationSource);
                        CopiedMaterials.Add(dst);
                    }
                    catch (Exception ex)
                    {
                        // Clear out the mtrl and let the functions later handle it.
                        bool original = await _index.IsDefaultFilePath(dst);
                        if (!original)
                        {
                            await _index.DeleteFileDescriptor(dst, df, false);
                        }
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

                    await _dat.CopyFile(src, dst, iCat, iName, ApplicationSource, true);
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
                // Save the new Metadata file.
                await ItemMetadata.SaveMetadata(newMetadata, ApplicationSource);



                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Filling in missing material sets...");
                }
                // Validate all variants/material sets for valid materials, and copy materials as needed to fix.
                if (Imc.UsesImc(Destination))
                {
                    var mSets = newMetadata.ImcEntries.Select(x => x.Variant).Distinct();
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
                            await _dat.CopyFile(existentCopy, destPath, iCat, iName, ApplicationSource, true);
                        }
                    }
                }

                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Updating modlist...");
                }

                // Here we're going to go through and edit all the modded items to be joined together in a modpack for convenience.
                modlist = await _modding.GetModListAsync();



                var modPack = new ModPack() { author = "System", name = "Item Copy - " + srcItem.Name, url = "", version = "1.0" };
                foreach (var mod in modlist.Mods)
                {
                    if (allFiles.Contains(mod.fullPath))
                    {
                        mod.modPack = modPack;
                    }
                }

                modlist.ModPacks.Add(modPack);
                modlist.modPackCount++;

                _modding.SaveModList(modlist);

                if (ProgressReporter != null)
                {
                    ProgressReporter.Report("Root copy complete.");
                }
            } finally
            {
                XivCache.CacheWorkerEnabled = workerStatus;
            }
        }

        const string CommonPath = "chara/common/";
        private static readonly Regex RemoveRootPathRegex = new Regex("chara\\/[a-z]+\\/[a-z][0-9]{4}(?:\\/obj\\/[a-z]+\\/[a-z][0-9]{4})?\\/(.+)");

        private static string UpdatePath(XivDependencyRoot Source, XivDependencyRoot Destination, string path)
        {
            // Things that live in the common folder get to stay there/don't get copied.
            if (path.StartsWith(CommonPath)) return path;


            var file = UpdateFileName(Source, Destination, path);
            var folder = UpdateFolder(Source, Destination, path);

            return folder + "/" + file;
        }

        private static string UpdateFolder(XivDependencyRoot Source, XivDependencyRoot Destination, string path)
        {
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
