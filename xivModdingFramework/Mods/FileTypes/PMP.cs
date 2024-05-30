﻿using HelixToolkit.SharpDX.Core.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using SharpDX.D3DCompiler;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General;
using xivModdingFramework.General.DataContainers;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Mods.Interfaces;
using xivModdingFramework.Variants.DataContainers;
using Ionic.Zip;
using xivModdingFramework.Helpers;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.SqPack.FileTypes;
using SharpDX.Direct2D1;
using xivModdingFramework.Variants.FileTypes;
using System.Runtime.CompilerServices;
using static xivModdingFramework.Mods.TTMPWriter;
using System.Security.Cryptography;
using JsonSubTypes;

namespace xivModdingFramework.Mods.FileTypes.PMP
{
    /// <summary>
    /// Class for handling Penumbra Modpacks
    /// </summary>
    public static class PMP
    {
        private static bool _ImportActive = false;
        private static string _Source = null;


        // List of meta files that have already been loaded from source during import.
        // Used for metadata compilation.
        private static HashSet<string> _MetaFiles;
        private static HashSet<uint> _RgspRaceGenders;

        private static async Task<string> ResolvePMPBasePath(string path, bool jsonsOnly = false)
        {
            if (path.EndsWith(".json"))
            {
                // PMP Folder by Json reference at root level.
                path = Path.GetDirectoryName(path);
            }
            else if (path.EndsWith(".pmp"))
            {
                // Compressed PMP file.  Decompress it first.
                var tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

                // Run Zip extract on a new thread.
                await Task.Run(async () =>
                {
                    if (!jsonsOnly)
                    {
                        // Extract everything.
                        System.IO.Compression.ZipFile.ExtractToDirectory(path, tempFolder);
                    } else
                    {
                        // Just JSON files.
                        using(var zip = new Ionic.Zip.ZipFile(path)) {
                            var jsons = zip.Entries.Where(x => x.FileName.ToLower().EndsWith(".json"));
                            foreach(var e in jsons)
                            {
                                e.Extract(tempFolder);
                            }
                        }
                    }
                });
                path = tempFolder;
            }
            return path;
        }

        public static async Task<(PMPJson pmp, string path)> LoadPMP(string path, bool jsonOnly = false)
        {
            var gameDir = XivCache.GameInfo.GameDirectory;

            path = await ResolvePMPBasePath(path, jsonOnly);
            var defModPath = Path.Combine(path, "default_mod.json");
            var metaPath = Path.Combine(path, "meta.json");

            var text = File.ReadAllText(metaPath);
            var meta = JsonConvert.DeserializeObject<PMPMetaJson>(text);


            var defaultOption = JsonConvert.DeserializeObject<PMPOptionJson>(File.ReadAllText(defModPath));

            var groups = new List<PMPGroupJson>();

            var files = Directory.GetFiles(path);

            foreach (var file in files)
            {
                if (Path.GetFileName(file).StartsWith("group_"))
                {
                    groups.Add(JsonConvert.DeserializeObject<PMPGroupJson>(File.ReadAllText(file)));
                }
            }

            var pmp = new PMPJson()
            {
                Meta = meta,
                DefaultMod = defaultOption,
                Groups = groups
            };

            return (pmp, path);
        }

        /// <summary>
        /// Imports a given PMP File, Penumbra Folder, or Penumbra Folder Root JSON into the game files.
        /// </summary>
        /// <param name="path">System path to .PMP, .JSON, or Folder</param>
        /// <returns></returns>
        public static async Task<(List<string> Imported, List<string> NotImported, float Duration)> ImportPMP(string path, ModPackImportSettings settings = null, ModTransaction tx = null)
        {
            path = await ResolvePMPBasePath(path);
            var pmpData = await LoadPMP(path);
            try
            {
                return await ImportPMP(pmpData.pmp, pmpData.path, settings, tx);
            }
            finally
            {
                IOUtil.DeleteTempDirectory(pmpData.path);
            }
        }

        /// <summary>
        /// Imports a given PMP File into the game.
        /// Requires unzipping the corpus of the PMP data to the given unzip path before hand (Ex. By calling LoadPMP)
        /// 
        /// Will automatically clean up unzippedPath after if it is in the user's TEMP directory.
        /// </summary>
        /// <returns></returns>
        public static async Task<(List<string> Imported, List<string> NotImported, float Duration)> ImportPMP(PMPJson pmp, string unzippedPath, ModPackImportSettings settings = null, ModTransaction tx = null)
        {

            if (_ImportActive)
            {
                throw new Exception("Cannot import multiple Modpacks simultaneously.");
            }

            if (settings == null)
            {
                settings = new ModPackImportSettings();
            }    

            var startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var needsCleanup = false;

            var progress = settings.ProgressReporter;
            var GetRootConversionsFunction = settings.RootConversionFunction;
            _Source = settings.SourceApplication;


            var imported = new Dictionary<string, TxFileState>();
            var boiler = TxBoiler.BeginWrite(ref tx, true);
            try
            {

                progress?.Report((0, 0, "Loading Modpack..."));


                if (unzippedPath.EndsWith(".json"))
                {
                    unzippedPath = Path.GetDirectoryName(unzippedPath);
                }

                if (unzippedPath.EndsWith(".pmp"))
                {
                    // File was not fully unzipped, and needs to be unzipped still.
                    needsCleanup = true;
                    var info = await LoadPMP(unzippedPath);
                    unzippedPath = info.path;
                }

                var notImported = new HashSet<string>();
                _ImportActive = true;
                _MetaFiles = new HashSet<string>();
                _RgspRaceGenders = new HashSet<uint>();

                var modPack = new ModPack(null);
                modPack.Name = pmp.Meta.Name;
                modPack.Author = pmp.Meta.Author;
                modPack.Version = pmp.Meta.Version;
                modPack.Url = pmp.Meta.Website;

                tx.ModPack = modPack;

                if (pmp.Groups == null || pmp.Groups.Count == 0)
                {
                    // No options, just default.
                    var groupRes = await ImportOption(pmp.DefaultMod, unzippedPath, tx, progress);
                    UnionDict(imported, groupRes.Imported);
                    notImported.UnionWith(groupRes.NotImported);
                }
                else
                {
                    // Order groups by Priority, Lowest => Highest, tiebreaker default order
                    var orderedGroups = pmp.Groups.OrderBy(x => x.Priority);
                    var optionIdx = 0;
                    foreach (var group in orderedGroups)
                    {
                        if (group.Options == null || group.Options.Count == 0)
                        {
                            // No valid options.
                            continue;
                        }

                        // Get Default selection.
                        var selected = group.DefaultSettings;

                        // If the user selected custom settings, use those.
                        if (group.SelectedSettings >= 0)
                        {
                            selected = group.SelectedSettings;
                        }

                        if (group.Type == "Single")
                        {
                            if (selected < 0 || selected >= group.Options.Count)
                            {
                                selected = 0;
                            }
                            var groupRes = await ImportOption(group.Options[selected], unzippedPath, tx, progress, optionIdx);
                            UnionDict(imported, groupRes.Imported);
                            notImported.UnionWith(groupRes.NotImported);
                        }
                        else if(group.Type == "Multi")
                        {
                            // Bitmask options.
                            for (int i = 0; i < group.Options.Count; i++)
                            {
                                var value = 1 << i;
                                if ((selected & value) > 0)
                                {
                                    var groupRes = await ImportOption(group.Options[i], unzippedPath, tx, progress, optionIdx);
                                    UnionDict(imported, groupRes.Imported);
                                    notImported.UnionWith(groupRes.NotImported);
                                    optionIdx++;
                                }
                            }

                        } else if(group.Type == "Imc")
                        {
                            // Could do with popping this out to its own function.
                            var imcGroup = group as PMPImcGroupJson;
                            var xivImc = imcGroup.DefaultEntry.ToXivImc();

                            bool disabled = false;
                            // Bitmask options.
                            for (int i = 0; i < group.Options.Count; i++)
                            {
                                var value = 1 << i;
                                if ((selected & value) > 0)
                                {
                                    var disableOpt = group.Options[i] as PmpDisableImcOptionJson;
                                    if (disableOpt != null)
                                    {
                                        // No options allowed >:|
                                        disabled = true;
                                        break;
                                    }

                                    var opt = group.Options[i] as PmpImcOptionJson;
                                    optionIdx++;

                                    xivImc.AttributeMask |= opt.AttributeMask;
                                }
                            }

                            if (!disabled)
                            {
                                var root = imcGroup.GetRoot();
                                var metaData = await GetImportMetadata(imported, root, tx);

                                if (metaData.ImcEntries.Count <= imcGroup.Identifier.Variant)
                                {
                                    while(metaData.ImcEntries.Count <= imcGroup.Identifier.Variant)
                                    {
                                        metaData.ImcEntries.Add((XivImc)xivImc.Clone());
                                    }
                                }
                                else
                                {
                                    metaData.ImcEntries[(int)imcGroup.Identifier.Variant] = xivImc;
                                }
                                await ItemMetadata.SaveMetadata(metaData, _Source, tx);
                                await ItemMetadata.ApplyMetadata(metaData, tx);

                            }
                        }

                    }
                }

                var preRootTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                long rootDuration = 0;
                if (GetRootConversionsFunction != null)
                {
                    var files = new HashSet<string>(imported.Keys);
                    rootDuration = await TTMP.HandleRootConversion(files, imported, tx, settings, modPack);
                    if (rootDuration < 0)
                    {
                        await boiler.Cancel(true, imported);
                        return (null, null, -1);
                    }
                }

                var afterRoot = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                // Pre-Dawntrail Files
                if (pmp.Meta.FileVersion <= 3)
                {
                    progress?.Report((0, 0, "Updating Pre-Dawntrail Files..."));
                    await TTMP.FixPreDawntrailImports(imported.Keys, _Source, imported, progress, tx);
                }

                if (boiler.OwnTx)
                {
                    progress?.Report((0, 0, "Committing Transaction..."));
                }
                await boiler.Commit();

                progress?.Report((0, 0, "Job Done!"));

                // Successful Import.
                var endTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                var duration = (preRootTime - startTime) + rootDuration + (endTime - afterRoot);
                var floatDuration = duration / 1000.0f;

                var res = (imported.Keys.ToList(), notImported.ToList(), floatDuration);

                return res;
            }
            catch
            {
                await boiler.Catch(imported);
                throw;
            }
            finally
            {
                if (needsCleanup)
                {
                    IOUtil.DeleteTempDirectory(unzippedPath);
                }
                _MetaFiles = null;
                _RgspRaceGenders = null;
                _Source = null;
                _ImportActive = false;
            }
        }

        private static async Task<(Dictionary<string, TxFileState> Imported, HashSet<string> NotImported)> ImportOption(PMPOptionJson baseOption, string basePath, ModTransaction tx, IProgress<(int, int, string)> progress = null, int optionIdx = 0)
        {
            var imported = new Dictionary<string, TxFileState>();
            var notImported = new HashSet<string>();

            var option = baseOption as PmpStandardOptionJson;

            if(option == null)
            {
                throw new NotImplementedException();
            }

            // Import files.
            var i = 0;
            foreach (var file in option.Files)
            {
                var internalPath = file.Key;
                var externalPath = Path.Combine(basePath, file.Value);
                progress?.Report((i, option.Files.Count, "Importing New Files from Option " + (optionIdx+1) + "..."));

                // Safety checks.
                if (!CanImport(file.Key))
                {
                    notImported.Add(file.Key);
                    i++;
                    continue;
                }
                if (!File.Exists(externalPath))
                {
                    notImported.Add(file.Key);
                    i++;
                    continue;
                }

                try
                {
                    // Save original state
                    if (!imported.ContainsKey(internalPath))
                    {
                        imported.Add(internalPath, await tx.SaveFileState(internalPath));
                    }

                    // Import the file...
                    await SmartImport.Import(externalPath, internalPath, _Source, tx);
                    i++;
                }
                catch (Exception ex)
                {
                    // If we failed to import a file, just continue.
                    notImported.Add(file.Key);
                    i++;
                    continue;
                }
            }


            i = 0;
            foreach (var kv in option.FileSwaps)
            {

                progress?.Report((i, option.FileSwaps.Count, "Importing File Swaps from Option " + (optionIdx + 1) + "..."));

                var src = kv.Value.Replace("\\", "/");
                var dest = kv.Key.Replace("\\", "/");

                if (!CanImport(src) || !CanImport(dest))
                {
                    notImported.Add(dest);
                    i++;
                    continue;
                }

                // Save original state
                if (!imported.ContainsKey(dest))
                {
                    imported.Add(dest, await tx.SaveFileState(dest));
                }

                byte[] data;
                // Get original file
                data = await tx.ReadFile(src, true, false);

                // Write it back to TX for our update.
                // Could potentially just do an index redirect, but this is safer.
                await Dat.WriteModFile(data, dest, _Source, null, tx, false);
                i++;
            }


            // Setup Metadata.
            if (option.Manipulations != null && option.Manipulations.Count > 0)
            {
                var data = await ManipulationsToMetadata(option.Manipulations, tx, imported);

                foreach(var meta in data.Metadatas)
                {
                    await ItemMetadata.SaveMetadata(meta, _Source, tx);
                }

                foreach(var rgsp in data.Rgsps)
                {
                    await CMP.SaveScalingParameter(rgsp, _Source, tx);
                }
            }

            return (imported, notImported);
        }

        private static async Task<ItemMetadata> GetImportMetadata(Dictionary<string, TxFileState> imported, XivDependencyRoot root, ModTransaction tx)
        {
            var metaPath = root.Info.GetRootFile();

            // Save initial state.
            if (!imported.ContainsKey(metaPath))
            {
                imported.Add(metaPath, await tx.SaveFileState(metaPath));
            }

            ItemMetadata metaData;
            if (!_MetaFiles.Contains(metaPath))
            {
                // If this is the first time we're seeing the metadata entry during this import sequence, then start from the clean base game version.
                metaData = await ItemMetadata.GetMetadata(metaPath, true, tx);
                _MetaFiles.Add(metaPath);
            }
            else
            {
                // Otherwise use the current transaction metadata state for metadata compilation.
                metaData = await ItemMetadata.GetMetadata(metaPath, false, tx);
            }

            return metaData;
        }
        private static async Task<RacialGenderScalingParameter> GetImportRgsp(Dictionary<string, TxFileState> imported, XivSubRace race, XivGender gender, ModTransaction tx)
        {
            var key = PMPRspManipulationJson.GetRaceGenderHash(race, gender);
            var path = CMP.GetRgspPath(race, gender);

            // Save initial state.
            if (!imported.ContainsKey(path))
            {
                imported.Add(path, await tx.SaveFileState(path));
            }

            RacialGenderScalingParameter rgsp;
            if (!_RgspRaceGenders.Contains(key))
            {
                // If this is the first time we're seeing the metadata entry during this import sequence, then start from the clean base game version.
                rgsp = await CMP.GetScalingParameter(race, gender, true, tx);
                _RgspRaceGenders.Add(key);
            }
            else
            {
                // Otherwise use the current transaction metadata state for metadata compilation.
                rgsp = await CMP.GetScalingParameter(race, gender, false, tx);
            }

            return rgsp;
        }

        private static bool CanImport(string internalFilePath)
        {
            bool foundDat = false;
            foreach (XivDataFile df in Enum.GetValues(typeof(XivDataFile)))
            {
                if (internalFilePath.StartsWith(XivDataFiles.GetFolderKey(df)))
                {
                    foundDat = true;
                    break;
                }
            }

            if (!foundDat)
            {
                return false;
            }

            return true;
        }



        /// <summary>
        /// Creates a simple single-option PMP from a given dictionary of file information at the target filepath.
        /// </summary>
        public static async Task CreateSimplePmp(string destination, BaseModpackData modpackMeta, Dictionary<string, FileStorageInformation> fileInfos, bool zip = true)
        {
            if (!destination.ToLower().EndsWith(".pmp") && zip)
            {
                throw new Exception("PMP Export must have .pmp extension.");
            }

            var workingPath = destination;
            if (zip)
            {
                workingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            }
            try
            {
                Directory.CreateDirectory(workingPath);

                var pmp = new PMPJson()
                {
                    Meta = new PMPMetaJson(),
                    Groups = new List<PMPGroupJson>(),
                    DefaultMod = new PMPOptionJson(),
                };


                var files = FileIdentifier.IdentifierListFromDictionary(fileInfos);
                await PMPExtensions.ResolveDuplicates(files);

                pmp.DefaultMod = await CreatePmpOption(workingPath, "Default", "The only option.", files.Values);

                pmp.Meta.Author = modpackMeta.Author;
                pmp.Meta.Name = modpackMeta.Name;
                pmp.Meta.Description = modpackMeta.Description;
                pmp.Meta.FileVersion = 3;
                pmp.Meta.Version = modpackMeta.Version.ToString();
                pmp.Meta.Website = modpackMeta.Url;


                await WritePmp(pmp, workingPath, zip ? destination : null);
            }
            finally
            {
                IOUtil.DeleteTempDirectory(workingPath);
            }
        }

        /// <summary>
        /// Writes out the fully completed PMP json files, and optionally zips the final folder into a pmp.
        /// </summary>
        /// <param name="pmp"></param>
        /// <param name="workingDirectory"></param>
        /// <param name="zipPath"></param>
        /// <returns></returns>
        private static async Task WritePmp(PMPJson pmp, string workingDirectory, string zipPath = null)
        {
            var metapath = Path.Combine(workingDirectory, "meta.json");
            var defaultModPath = Path.Combine(workingDirectory, "default_mod.json");

            var metaString = JsonConvert.SerializeObject(pmp.Meta, Formatting.Indented);
            File.WriteAllText(metapath, metaString);

            var defaultModString = JsonConvert.SerializeObject(pmp.DefaultMod, Formatting.Indented);
            File.WriteAllText(defaultModPath, defaultModString);

            for(int i = 0; i < pmp.Groups.Count; i++)
            {
                var gName = pmp.Groups[i].Name.ToLower();
                var groupPath = Path.Combine(workingDirectory, "group_" + i.ToString("D3") + "_" + gName);
                var groupString = JsonConvert.SerializeObject(gName, Formatting.Indented);
                File.WriteAllText(groupPath, groupString);
            }

            if(zipPath != null)
            {
                File.Delete(zipPath);
                System.IO.Compression.ZipFile.CreateFromDirectory(workingDirectory, zipPath);
            }
        }

        public static async Task<PMPOptionJson> CreatePmpOption(string workingPath, string name, string description, IEnumerable<FileIdentifier> files)
        {
            var opt = new PmpStandardOptionJson()
            {
                Name = name,
                Description = description,
                Files = new Dictionary<string, string>(),
                FileSwaps = new Dictionary<string, string>(),
                Manipulations = new List<PMPMetaManipulationJson>(),
            };

            // TODO - Could paralell this? Unsure how big the gains would really be though,
            // since the primary tasks are already paralelled internally, and there's little else heavy going on.
            foreach(var fi in files)
            {
                var data = await TransactionDataHandler.GetUncompressedFile(fi.Info);
                if (fi.Path.EndsWith(".meta"))
                {
                    var meta = await ItemMetadata.Deserialize(data);
                    opt.Manipulations.AddRange(PMPExtensions.MetadataToManipulations(meta));
                } 
                else if (fi.Path.EndsWith(".rgsp"))
                {
                    var rgsp = new RacialGenderScalingParameter(data);
                    opt.Manipulations.AddRange(PMPExtensions.RgspToManipulations(rgsp));
                } 
                else if (IOUtil.IsMetaInternalFile(fi.Path))
                {
                    // We don't allow writing these out directly, as it rapidly becomes chaos.
                    continue;
                } 
                else
                { 
                    var writePath = Path.Combine(workingPath, fi.PmpPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(writePath));
                    File.WriteAllBytes(writePath, data);

                    // Penumbra likes backslashes?  Or do they write with system separator?
                    // Path.DirectorySeparatorChar
                    opt.Files.Add(fi.Path, fi.PmpPath.Replace("/", "\\"));
                    //opt.Files.Add(fi.Path, fi.PmpPath.Replace('/', Path.DirectorySeparatorChar));
                }

            }
            return opt;
        }


        private static void UnionDict<TKey, TValue>(Dictionary<TKey, TValue> original, Dictionary<TKey, TValue> additional)
        {
            foreach (var entry in additional)
            {
                if (original.ContainsKey(entry.Key))
                    continue;
                original.Add(entry.Key, entry.Value);
            }
        }

        /// <summary>
        /// Unpacks a PMP into a single dictionary of [File Path] => [File Storage Info]
        /// Only works if the PMP has no options/a single option.
        /// </summary>
        /// <param name="modpackPath"></param>
        /// <returns></returns>
        internal static async Task<Dictionary<string, FileStorageInformation>> UnpackPMP(string modpackPath, bool includeData = true, ModTransaction tx = null)
        {
            try
            {
                var pmpAndPath = await LoadPMP(modpackPath, true);
                var pmp = pmpAndPath.pmp;

                var defMod = pmp.DefaultMod as PmpStandardOptionJson;
                PmpStandardOptionJson option = null;
                if (defMod != null && (defMod.FileSwaps.Count > 0 || defMod.Manipulations.Count > 0 || defMod.Files.Count > 0))
                {
                    // Valid Default Mod Option
                    option = defMod;
                }
                else
                {
                    if (pmp.Groups.Count == 1)
                    {
                        var group = pmp.Groups[0];
                        if (group.Options.Count == 1)
                        {
                            option = group.Options[0] as PmpStandardOptionJson;
                        }
                    }
                }

                if (option == null)
                {
                    // Too may options or no options.
                    return null;
                }


                return await UnpackPmpOption(option, modpackPath, null, tx);
            }
            catch (Exception ex)
            {
                return null;
            }
        }


        /// <summary>
        /// Unzips and Unpacks a given PMP option into a dictionary of [Internal File Path] => [File Storage Information]
        /// </summary>
        public static async Task<Dictionary<string, FileStorageInformation>> UnpackPmpOption(PMPOptionJson baseOption, string zipArchivePath = null, string unzipPath = null, ModTransaction tx = null)
        {

            var option = baseOption as PmpStandardOptionJson;

            if (option == null)
            {
                // TODO - Convert IMC file to MetaFile here.
                throw new NotImplementedException();
            }

            var includeData = zipArchivePath != null;

            bool alreadyUnzipped = false;
            if (zipArchivePath != null)
            {
                if (zipArchivePath.ToLower().EndsWith(".json"))
                {
                    zipArchivePath = Path.GetDirectoryName(zipArchivePath);
                }

                if (IOUtil.IsDirectory(zipArchivePath))
                {
                    alreadyUnzipped = true;
                    unzipPath = zipArchivePath;
                }
            }

            if (!alreadyUnzipped && unzipPath == null)
            {
                if (tx != null && !tx.ReadOnly && unzipPath == null)
                {
                    // Use TX data store if we have one.
                    unzipPath = tx.UNSAFE_GetTransactionStore();
                } else
                {
                    // Make our own temp path.
                    unzipPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                }
            }

            // Task wrapper since we might be doing some heavy lifting.
            await Task.Run(async () =>
            {
                // Resolve the base path we're working, and unzip if needed...
                if (includeData)
                {
                    await IOUtil.UnzipFiles(zipArchivePath, unzipPath, option.Files.Values);
                }
            });

            var ret = new Dictionary<string, FileStorageInformation>();

            if (tx == null)
            {
                // Grab a readonly TX here to read base game files when needed.
                tx = ModTransaction.BeginTransaction();
            }


            // Custom Files from the .pmp Zip Archive.
            foreach (var file in option.Files)
            {
                var internalPath = file.Key;
                // Safety checks.
                if (!CanImport(file.Key))
                {
                    continue;
                }

                var externalPath = Path.Combine(unzipPath, file.Value);
                var fileInfo = new FileStorageInformation()
                {
                    StorageType = EFileStorageType.UncompressedIndividual,
                    RealPath = externalPath,
                    RealOffset = 0,
                    FileSize = 0,
                };

                ret.Add(internalPath, fileInfo);
            }

            // File Swaps from base game files.
            foreach (var kv in option.FileSwaps)
            { 
                // For some reason the destination value is backslashed instead of forward-slashed.                
                var src = kv.Value.Replace("\\", "/");
                var dest = kv.Key.Replace("\\", "/");

                if (!CanImport(src) || !CanImport(dest))
                {
                    continue;
                }

                if (!includeData)
                {
                    ret.Add(src, new FileStorageInformation());
                    continue;
                }

                var df = IOUtil.GetDataFileFromPath(src);
                var offset = await tx.Get8xDataOffset(src, true);
                var fileInfo = IOUtil.MakeGameStorageInfo(df, offset);

                ret.Add(src, fileInfo);
            }

            // Metadata files.
            if (option.Manipulations != null && option.Manipulations.Count > 0)
            {
                var manips = await ManipulationsToMetadata(option.Manipulations, tx);

                foreach(var meta in manips.Metadatas)
                {
                    var metaPath = meta.Root.Info.GetRootFile();

                    // These are a bit weird, and basically have to be written to disk or some kind of memory store.
                    var data = await ItemMetadata.Serialize(meta);
                    var tempFilePath = Path.GetTempFileName();
                    File.WriteAllBytes(tempFilePath, data);

                    var fileInfo = new FileStorageInformation()
                    {
                        FileSize = data.Length,
                        StorageType = EFileStorageType.UncompressedIndividual,
                        RealPath = tempFilePath,
                        RealOffset = 0,
                    };

                    ret.Add(metaPath, fileInfo);
                }

                foreach(var rgsp in manips.Rgsps)
                {
                    var path = CMP.GetRgspPath(rgsp.Race, rgsp.Gender);
                    // These are a bit weird, and basically have to be written to disk or some kind of memory store.
                    var data = rgsp.GetBytes();
                    var tempFilePath = Path.GetTempFileName();
                    File.WriteAllBytes(tempFilePath, data);

                    var fileInfo = new FileStorageInformation()
                    {
                        FileSize = data.Length,
                        StorageType = EFileStorageType.UncompressedIndividual,
                        RealPath = tempFilePath,
                        RealOffset = 0,
                    };

                    ret.Add(path, fileInfo);
                }
            }

            return ret;
        }


        /// <summary>
        /// Converts a list of PMP Manipulation entries into fully validated Metadata/RGSP entries.
        /// If the Imported list is included, the original states of the files are added if needed,
        /// and bases are populated through the appropriate functions to maintain TX consistency.
        /// If the Imported list is NULL, base game files are used as the starter point.
        /// </summary>
        /// <param name="manipulations"></param>
        /// <param name="tx"></param>
        /// <param name="imported"></param>
        /// <returns></returns>
        public static async Task<(List<ItemMetadata> Metadatas, List<RacialGenderScalingParameter> Rgsps)> ManipulationsToMetadata(List<PMPMetaManipulationJson> manipulations, ModTransaction tx, Dictionary<string, TxFileState> imported = null)
        {

            // Setup Metadata.
            if (manipulations != null || manipulations.Count == 0)
            {
                return (new List<ItemMetadata>(), new List<RacialGenderScalingParameter>());
            }

            Dictionary<string, ItemMetadata> seenMetadata = new Dictionary<string, ItemMetadata>();
            Dictionary<uint, RacialGenderScalingParameter> seenRgsps = new Dictionary<uint, RacialGenderScalingParameter>();


            // RSP Options resolve by race/gender pairing.
            var rspOptions = manipulations.Select(x => x.GetManipulation() as PMPRspManipulationJson).Where(x => x != null);
            var byRg = rspOptions.GroupBy(x => x.GetRaceGenderHash());

            var total = byRg.Count();
            foreach (var group in byRg)
            {
                var rg = group.First().GetRaceGender();
                RacialGenderScalingParameter cmp;
                if (!seenRgsps.ContainsKey(group.Key))
                {
                    // If this our first time seeing this race/gender pairing in this import sequence, use the original game clean version of the file.
                    if (imported != null)
                    {
                        cmp = await GetImportRgsp(imported, rg.Race, rg.Gender, tx);
                    } else
                    {
                        cmp = await CMP.GetScalingParameter(rg.Race, rg.Gender, true, tx);
                    }

                    seenRgsps.Add(group.Key, cmp);
                }
                else
                {
                    cmp = seenRgsps[group.Key];
                }

                foreach (var effect in group)
                {
                    effect.ApplyScaling(cmp);
                }
            }

            // Metadata.
            var metaOptions = manipulations.Select(x => x.GetManipulation() as IPMPItemMetadata).Where(x => x != null);

            var byRoot = metaOptions.GroupBy(x => x.GetRoot());

            total = byRoot.Count();
            // Apply Metadata in order by Root.
            foreach (var group in byRoot)
            {
                var root = group.Key;
                var path = root.Info.GetRootFile();

                ItemMetadata metaData;
                if (!seenMetadata.ContainsKey(path))
                {
                    // If this our first time seeing this race/gender pairing in this import sequence, use the original game clean version of the file.
                    if (imported != null)
                    {
                        metaData = await ItemMetadata.GetMetadata(path, true, tx);
                    }
                    else
                    {
                        metaData = await PMP.GetImportMetadata(imported, root, tx);
                    }

                    seenMetadata.Add(path, metaData);
                }
                else
                {
                    metaData = seenMetadata[path];
                }

                foreach (var meta in group)
                {
                    meta.ApplyToMetadata(metaData);
                }
            }


            return (seenMetadata.Values.ToList(), seenRgsps.Values.ToList());
        }

    }


    #region Penumbra Enums and Extensions
    // https://github.com/Ottermandias/Penumbra.GameData/blob/main/Enums/Race.cs
    // Penumbra Enums are all serialized as strings.

    [JsonConverter(typeof(StringEnumConverter))]
    public enum PMPObjectType : byte
    {
        Unknown,
        Vfx,
        DemiHuman,
        Accessory,
        World,
        Housing,
        Monster,
        Icon,
        LoadingScreen,
        Map,
        Interface,
        Equipment,
        Character,
        Weapon,
        Font,
    }
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PMPModelRace : byte
    {
        Unknown,
        Midlander,
        Highlander,
        Elezen,
        Lalafell,
        Miqote,
        Roegadyn,
        AuRa,
        Hrothgar,
        Viera,
    }
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PMPSubRace : byte
    {
        Unknown,
        Midlander,
        Highlander,
        Wildwood,
        Duskwight,
        Plainsfolk,
        Dunesfolk,
        SeekerOfTheSun,
        KeeperOfTheMoon,
        Seawolf,
        Hellsguard,
        Raen,
        Xaela,
        Helion,
        Lost,
        Rava,
        Veena,
    }
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PMPGender : byte
    {
        Unknown,
        Male,
        Female,
        MaleNpc,
        FemaleNpc,
    }
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PMPGenderRace : ushort
    {
        Unknown = 0,
        MidlanderMale = 0101,
        MidlanderMaleNpc = 0104,
        MidlanderFemale = 0201,
        MidlanderFemaleNpc = 0204,
        HighlanderMale = 0301,
        HighlanderMaleNpc = 0304,
        HighlanderFemale = 0401,
        HighlanderFemaleNpc = 0404,
        ElezenMale = 0501,
        ElezenMaleNpc = 0504,
        ElezenFemale = 0601,
        ElezenFemaleNpc = 0604,
        MiqoteMale = 0701,
        MiqoteMaleNpc = 0704,
        MiqoteFemale = 0801,
        MiqoteFemaleNpc = 0804,
        RoegadynMale = 0901,
        RoegadynMaleNpc = 0904,
        RoegadynFemale = 1001,
        RoegadynFemaleNpc = 1004,
        LalafellMale = 1101,
        LalafellMaleNpc = 1104,
        LalafellFemale = 1201,
        LalafellFemaleNpc = 1204,
        AuRaMale = 1301,
        AuRaMaleNpc = 1304,
        AuRaFemale = 1401,
        AuRaFemaleNpc = 1404,
        HrothgarMale = 1501,
        HrothgarMaleNpc = 1504,
        HrothgarFemale = 1601,
        HrothgarFemaleNpc = 1604,
        VieraMale = 1701,
        VieraMaleNpc = 1704,
        VieraFemale = 1801,
        VieraFemaleNpc = 1804,
        UnknownMaleNpc = 9104,
        UnknownFemaleNpc = 9204,
    }
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PMPEquipSlot : byte
    {
        Unknown = 0,
        MainHand = 1,
        OffHand = 2,
        Head = 3,
        Body = 4,
        Hands = 5,
        Belt = 6,
        Legs = 7,
        Feet = 8,
        Ears = 9,
        Neck = 10,
        Wrists = 11,
        RFinger = 12,
        BothHand = 13,
        LFinger = 14, // Not officially existing, means "weapon could be equipped in either hand" for the game.
        HeadBody = 15,
        BodyHandsLegsFeet = 16,
        SoulCrystal = 17,
        LegsFeet = 18,
        FullBody = 19,
        BodyHands = 20,
        BodyLegsFeet = 21,
        ChestHands = 22,
        Nothing = 23,
        All = 24, // Not officially existing
    }
    [JsonConverter(typeof(StringEnumConverter))]
    public enum RspAttribute : byte
    {
        MaleMinSize,
        MaleMaxSize,
        MaleMinTail,
        MaleMaxTail,
        FemaleMinSize,
        FemaleMaxSize,
        FemaleMinTail,
        FemaleMaxTail,
        BustMinX,
        BustMinY,
        BustMinZ,
        BustMaxX,
        BustMaxY,
        BustMaxZ,
        NumAttributes,
    }

    public static class PMPExtensions
    {
        public static Dictionary<string, string> PenumbraTypeToGameType = new Dictionary<string, string>();


        /// <summary>
        /// Incomplete listing, only used for IMC-using types.
        /// </summary>
        public static Dictionary<XivItemType, PMPObjectType> XivItemTypeToPenumbraObject = new Dictionary<XivItemType, PMPObjectType>()
        {
            { XivItemType.weapon, PMPObjectType.Weapon },
            { XivItemType.equipment, PMPObjectType.Equipment },
            { XivItemType.accessory, PMPObjectType.Accessory },
            { XivItemType.demihuman, PMPObjectType.DemiHuman },
            { XivItemType.monster, PMPObjectType.Monster },
        };

        public static XivDependencyRootInfo GetRootFromPenumbraValues(PMPObjectType objectType, uint primaryId, PMPObjectType bodySlot, uint secondaryId, PMPEquipSlot slot)
        {
            var info = new XivDependencyRootInfo();
            info.PrimaryId = (int)primaryId;
            info.SecondaryId = (int)secondaryId;

            if (objectType != PMPObjectType.Unknown)
            {
                info.PrimaryType = XivItemTypes.FromSystemName(objectType.ToString().ToLower());
            }

            if (bodySlot != PMPObjectType.Unknown)
            {
                info.SecondaryType = XivItemTypes.FromSystemName(bodySlot.ToString().ToLower());
            }

            info.Slot = PenumbraSlotToGameSlot[slot];


            return info;
        }

        // We only really care about the ones used in IMC entries here.
        public static Dictionary<PMPEquipSlot, string> PenumbraSlotToGameSlot = new Dictionary<PMPEquipSlot, string>()
        {
            // Main
            { PMPEquipSlot.Head, "met" },
            { PMPEquipSlot.Body, "top" },
            { PMPEquipSlot.Hands, "glv" },
            { PMPEquipSlot.Legs, "dwn" },
            { PMPEquipSlot.Feet, "sho" },

            // Accessory
            { PMPEquipSlot.Ears, "ear" },
            { PMPEquipSlot.Neck, "nek" },
            { PMPEquipSlot.Wrists, "wrs" },
            { PMPEquipSlot.RFinger, "rir" },
            { PMPEquipSlot.LFinger, "ril" },
        };

        public static XivRace GetRaceFromPenumbraValue(PMPModelRace race, PMPGender gender)
        {
            // We can get a little cheesy here.
            var rGenderSt = race.ToString() + gender.ToString();
            var rGender = Enum.Parse(typeof(PMPGenderRace), rGenderSt);
            var intVal = (int)((ushort)rGender);

            // Can just direct cast this now.
            return (XivRace)intVal;
        }

        public static (PMPModelRace Race, PMPGender Gender) GetPMPRaceGenderFromXivRace(XivRace xivRace)
            => xivRace switch
            {
                XivRace.Hyur_Midlander_Male => (PMPModelRace.Midlander, PMPGender.Male),
                XivRace.Hyur_Midlander_Female => (PMPModelRace.Midlander, PMPGender.Female),

                XivRace.Hyur_Highlander_Male => (PMPModelRace.Highlander, PMPGender.Male),
                XivRace.Hyur_Highlander_Female => (PMPModelRace.Highlander, PMPGender.Female),

                XivRace.Elezen_Male => (PMPModelRace.Elezen, PMPGender.Male),
                XivRace.Elezen_Female => (PMPModelRace.Elezen, PMPGender.Female),

                XivRace.Roegadyn_Male => (PMPModelRace.Roegadyn, PMPGender.Male),
                XivRace.Roegadyn_Female => (PMPModelRace.Roegadyn, PMPGender.Female),

                XivRace.Miqote_Male => (PMPModelRace.Miqote, PMPGender.Male),
                XivRace.Miqote_Female => (PMPModelRace.Miqote, PMPGender.Female),

                XivRace.Lalafell_Male => (PMPModelRace.Lalafell, PMPGender.Male),
                XivRace.Lalafell_Female => (PMPModelRace.Lalafell, PMPGender.Female),

                XivRace.AuRa_Male => (PMPModelRace.AuRa, PMPGender.Male),
                XivRace.AuRa_Female => (PMPModelRace.AuRa, PMPGender.Female),

                XivRace.Viera_Male => (PMPModelRace.Viera, PMPGender.Male),
                XivRace.Viera_Female => (PMPModelRace.Viera, PMPGender.Female),

                XivRace.Hrothgar_Male => (PMPModelRace.Hrothgar, PMPGender.Male),
#if DAWNTRAIL
                XivRace.Hrothgar_Female => (PMPModelRace.Hrothgar, PMPGender.Female),
#endif

                _ => (PMPModelRace.Unknown, PMPGender.Unknown)
            };


        public static Dictionary<PMPGender, XivGender> PMPGenderToXivGender = new Dictionary<PMPGender, XivGender>()
        {
            { PMPGender.Male, XivGender.Male },
            { PMPGender.Female, XivGender.Female }
        };

        public static Dictionary<PMPSubRace, XivSubRace> PMPSubraceToXivSubrace = new Dictionary<PMPSubRace, XivSubRace>()
        {
            { PMPSubRace.Midlander, XivSubRace.Hyur_Midlander },
            { PMPSubRace.Highlander, XivSubRace.Hyur_Highlander },

            { PMPSubRace.SeekerOfTheSun, XivSubRace.Miqote_Seeker },
            { PMPSubRace.KeeperOfTheMoon, XivSubRace.Miqote_Keeper },

            { PMPSubRace.Wildwood, XivSubRace.Elezen_Wildwood },
            { PMPSubRace.Duskwight, XivSubRace.Elezen_Duskwight },

            { PMPSubRace.Seawolf, XivSubRace.Roegadyn_SeaWolf},
            { PMPSubRace.Hellsguard, XivSubRace.Roegadyn_Hellsguard },

            { PMPSubRace.Dunesfolk, XivSubRace.Lalafell_Dunesfolk },
            { PMPSubRace.Plainsfolk, XivSubRace.Lalafell_Plainsfolk },

            { PMPSubRace.Raen, XivSubRace.AuRa_Raen },
            { PMPSubRace.Xaela, XivSubRace.AuRa_Xaela },

            { PMPSubRace.Veena, XivSubRace.Viera_Veena },
            { PMPSubRace.Rava, XivSubRace.Viera_Rava },

            { PMPSubRace.Lost, XivSubRace.Hrothgar_Lost },
            { PMPSubRace.Helion, XivSubRace.Hrothgar_Helion },
        };


        public static PMPGender ToPMPGender(this RspAttribute attribute)
            => attribute switch
            {
                RspAttribute.MaleMinSize => PMPGender.Male,
                RspAttribute.MaleMaxSize => PMPGender.Male,
                RspAttribute.MaleMinTail => PMPGender.Male,
                RspAttribute.MaleMaxTail => PMPGender.Male,
                RspAttribute.FemaleMinSize => PMPGender.Female,
                RspAttribute.FemaleMaxSize => PMPGender.Female,
                RspAttribute.FemaleMinTail => PMPGender.Female,
                RspAttribute.FemaleMaxTail => PMPGender.Female,
                RspAttribute.BustMinX => PMPGender.Female,
                RspAttribute.BustMinY => PMPGender.Female,
                RspAttribute.BustMinZ => PMPGender.Female,
                RspAttribute.BustMaxX => PMPGender.Female,
                RspAttribute.BustMaxY => PMPGender.Female,
                RspAttribute.BustMaxZ => PMPGender.Female,
                _ => PMPGender.Unknown,
            };

        public static List<PMPMetaManipulationJson> RgspToManipulations(RacialGenderScalingParameter rgsp)
        {
            var ret = new List<PMPMetaManipulationJson>();
            var entries = PMPRspManipulationJson.FromRgspEntry(rgsp);
            foreach(var e in entries)
            {
                var entry = new PMPRspManipulationWrapperJson() { Type = "Rsp" };
                entry.SetManipulation(e);
                ret.Add(entry);
            }
            return ret;
        }
        public static List<PMPMetaManipulationJson> MetadataToManipulations(ItemMetadata m)
        {
            var ret = new List<PMPMetaManipulationJson>();
            var root = m.Root.Info;

            if (m.GmpEntry != null)
            {
                var entry = new PMPGmpManipulationWrapperJson() { Type = "Gmp" };
                entry.SetManipulation(PMPGmpManipulationJson.FromGmpEntry(m.GmpEntry, root));
                ret.Add(entry);
            }

            if(m.EqpEntry != null)
            {
                var entry = new PMPEqpManipulationWrapperJson() { Type = "Eqp" };
                entry.SetManipulation(PMPEqpManipulationJson.FromEqpEntry(m.EqpEntry, root));
                ret.Add(entry);
            }

            if(m.EstEntries != null && m.EstEntries.Count > 0)
            {
                foreach(var est in m.EstEntries)
                {
                    var entry = new PMPEstManipulationWrapperJson() { Type = "Est" };
                    entry.SetManipulation(PMPEstManipulationJson.FromEstEntry(est.Value, root.Slot));
                    ret.Add(entry);
                }
            }

            if(m.EqdpEntries != null && m.EqdpEntries.Count > 0)
            {
                foreach (var eqdp in m.EqdpEntries) {
                    var entry = new PMPEqdpManipulationWrapperJson() { Type = "Eqdp" };
                    entry.SetManipulation(PMPEqdpManipulationJson.FromEqdpEntry(eqdp.Value, root, eqdp.Key));
                    ret.Add(entry);
                }
            }

            if(m.ImcEntries != null && m.ImcEntries.Count > 0)
            {
                for(int i = 0; i < m.ImcEntries.Count; i++)
                {
                    var entry = new PMPImcManipulationWrapperJson() { Type = "Imc" };
                    entry.SetManipulation(PMPImcManipulationJson.FromImcEntry(m.ImcEntries[i], i, root));
                    ret.Add(entry);
                }
            }

            return ret;
        }


        /// <summary>
        /// Resolves duplicate files and assigns PMP zip paths to all of the file Identifiers.
        /// </summary>
        /// <param name="files"></param>
        /// <param name="defaultStorageType"></param>
        /// <returns></returns>
        internal static async Task ResolveDuplicates(Dictionary<Guid, FileIdentifier> files, EFileStorageType defaultStorageType = EFileStorageType.CompressedIndividual)
        {
            using var sha1 = SHA1.Create();

            // Internal Path => Sha Key
            var pmpPathDict = new Dictionary<string, TTMPWriter.SHA1HashKey>();
            // Sha Key => Out File.
            var seenFiles = new Dictionary<TTMPWriter.SHA1HashKey, string>();

            var idx = 1;
            foreach(var fkv in files)
            {
                var f = fkv.Value;
                var path = f.Path;
                var info = f.Info;
                var id = f.Id;

                byte[] data;
                if (defaultStorageType == EFileStorageType.CompressedIndividual || defaultStorageType == EFileStorageType.CompressedBlob)
                {
                    // Which we use here doesn't ultimately matter, but one will be faster than the other, depending on the way *most* files were stored.
                    data = await TransactionDataHandler.GetCompressedFile(info);
                } else
                {
                    data = await TransactionDataHandler.GetUncompressedFile(info);
                }

                var dedupeHash = new SHA1HashKey(sha1.ComputeHash(data));
                var pmpPath = f.OptionPrefix + path;

                if (seenFiles.ContainsKey(dedupeHash))
                {
                    // Shift the target path into the common folder if we're used in multiple places.
                    if (!seenFiles[dedupeHash].StartsWith("common/"))
                    {
                        seenFiles[dedupeHash] = "common/" + idx.ToString() + "/" + Path.GetFileName(seenFiles[dedupeHash]);
                        idx++;
                    }
                }
                else
                {
                    seenFiles.Add(dedupeHash, pmpPath);
                }
                pmpPath = seenFiles[dedupeHash];
                pmpPathDict.Add(path, dedupeHash);
            }


            // Re-loop to assign the final paths.
            // Could do this more efficiently, but whatever.  Perf impact is de minimis.
            foreach(var fkv in files)
            {
                var hash = pmpPathDict[fkv.Value.Path];
                var pmpPath = seenFiles[hash];
                files[fkv.Key].PmpPath = pmpPath;
            }
        }
    }
    public class FileIdentifier
    {
        public FileStorageInformation Info;
        public string Path;
        public string PmpPath;
        public Guid Id = Guid.NewGuid();
        public string OptionPrefix = "";

        public static Dictionary<Guid, FileIdentifier> IdentifierListFromDictionary(Dictionary<string, FileStorageInformation> files, string optionPrefix = "")
        {
            var dict = new Dictionary<Guid, FileIdentifier>(files.Count);
            foreach (var f in files)
            {
                var fi = new FileIdentifier()
                {
                    Path = f.Key,
                    Info = f.Value,
                    OptionPrefix = optionPrefix,
                };
                dict.Add(fi.Id, fi);
            }
            return dict;
        }
    }
#endregion

    #region Penumbra Simple JSON Classes
    public class PMPJson
    {
        public PMPMetaJson Meta { get; set; }
        public PMPOptionJson DefaultMod { get; set; }
        public List<PMPGroupJson> Groups { get; set; }
    }

    public class PMPMetaJson
    {
        public int FileVersion;
        public string Name;
        public string Author;
        public string Description;
        public string Version;
        public string Website;


        // These exist.
        List<string> Tags;
    }

    [JsonConverter(typeof(JsonSubtypes), "Type")]
    [JsonSubtypes.KnownSubType(typeof(PMPImcGroupJson), "Imc")]
    public class PMPGroupJson
    {
        public string Name;
        public string Description;
        public int Priority;

        // "Multi" or "Single"
        public string Type;

        // Only used internally when the user is selecting options during install/application.
        [JsonIgnore] public int SelectedSettings = -1;

        // Either single Index or Bitflag.
        public int DefaultSettings;
        
        public List<PMPOptionJson> Options = new List<PMPOptionJson>();
    }

    public class PMPImcGroupJson : PMPGroupJson
    {
        public PMPImcManipulationJson.PMPImcEntry DefaultEntry;
        public PmpIdentifierJson Identifier;

        public XivDependencyRoot GetRoot()
        {
            var root = PMPExtensions.GetRootFromPenumbraValues(Identifier.ObjectType, Identifier.PrimaryId, Identifier.BodySlot, Identifier.SecondaryId, Identifier.EquipSlot);
            return new XivDependencyRoot(root);
        }
    }

    public class PmpIdentifierJson
    {
        public PMPObjectType ObjectType;
        public uint PrimaryId;
        public uint SecondaryId;
        public ushort Variant;
        public PMPEquipSlot EquipSlot;
        public PMPObjectType BodySlot;

        public XivDependencyRoot GetRoot()
        {
            var root = PMPExtensions.GetRootFromPenumbraValues(ObjectType, PrimaryId, BodySlot, SecondaryId, EquipSlot);
            return new XivDependencyRoot(root);
        }
    }

    [JsonConverter(typeof(JsonSubtypes))]
    [JsonSubtypes.KnownSubTypeWithProperty(typeof(PmpStandardOptionJson), "Files")]
    [JsonSubtypes.KnownSubTypeWithProperty(typeof(PmpDisableImcOptionJson), "IsDisableSubMod")]
    [JsonSubtypes.KnownSubTypeWithProperty(typeof(PmpImcOptionJson), "AttributeMask")]
    public class PMPOptionJson
    {
        public string Name;
        public string Description;
    }

    public class PmpStandardOptionJson : PMPOptionJson
    {
        public Dictionary<string, string> Files;
        public Dictionary<string, string> FileSwaps;
        public List<PMPMetaManipulationJson> Manipulations;
    }

    public class PmpDisableImcOptionJson : PMPOptionJson
    {
        public bool IsDisableSubMod;
    }
    public class PmpImcOptionJson : PMPOptionJson
    {
        public ushort AttributeMask;
    }

    #endregion

    #region Metadata Manipulations

    [JsonConverter(typeof(JsonSubtypes), "Type")]
    [JsonSubtypes.KnownSubType(typeof(PMPImcManipulationWrapperJson), "Imc")]
    [JsonSubtypes.KnownSubType(typeof(PMPEstManipulationWrapperJson), "Est")]
    [JsonSubtypes.KnownSubType(typeof(PMPEqpManipulationWrapperJson), "Eqp")]
    [JsonSubtypes.KnownSubType(typeof(PMPEqdpManipulationWrapperJson), "Eqdp")]
    [JsonSubtypes.KnownSubType(typeof(PMPGmpManipulationWrapperJson), "Gmp")]
    [JsonSubtypes.KnownSubType(typeof(PMPRspManipulationWrapperJson), "Rsp")]
    public class PMPMetaManipulationJson
    {
        public string Type;

        public virtual object GetManipulation()
        {
            return null;
        }
        public virtual void SetManipulation(object o)
        {
            throw new NotImplementedException();
            return;
        }
    }

    public class PMPImcManipulationWrapperJson : PMPMetaManipulationJson
    {
        public PMPImcManipulationJson Manipulation;
        public override object GetManipulation()
        {
            return Manipulation;
        }
        public override void SetManipulation(object o)
        {
            Manipulation = o as PMPImcManipulationJson;
        }
    }
    public class PMPEstManipulationWrapperJson : PMPMetaManipulationJson
    {
        public PMPEstManipulationJson Manipulation;
        public override object GetManipulation()
        {
            return Manipulation;
        }
        public override void SetManipulation(object o)
        {
            Manipulation = o as PMPEstManipulationJson;
        }
    }
    public class PMPEqpManipulationWrapperJson : PMPMetaManipulationJson
    {
        public PMPEqpManipulationJson Manipulation;
        public override object GetManipulation()
        {
            return Manipulation;
        }
        public override void SetManipulation(object o)
        {
            Manipulation = o as PMPEqpManipulationJson;
        }
    }
    public class PMPEqdpManipulationWrapperJson : PMPMetaManipulationJson
    {
        public PMPEqdpManipulationJson Manipulation;
        public override object GetManipulation()
        {
            return Manipulation;
        }
        public override void SetManipulation(object o)
        {
            Manipulation = o as PMPEqdpManipulationJson;
        }
    }
    public class PMPGmpManipulationWrapperJson : PMPMetaManipulationJson
    {
        public PMPGmpManipulationJson Manipulation;
        public override object GetManipulation()
        {
            return Manipulation;
        }
        public override void SetManipulation(object o)
        {
            Manipulation = o as PMPGmpManipulationJson;
        }
    }
    public class PMPRspManipulationWrapperJson : PMPMetaManipulationJson
    {
        public PMPRspManipulationJson Manipulation;
    }


    public interface IPMPItemMetadata
    {
        /// <summary>
        /// Retrieves this 
        /// </summary>
        /// <returns></returns>
        public XivDependencyRoot GetRoot();

        /// <summary>
        /// Applies this metadata entry's effects to the given item's metadata.
        /// </summary>
        /// <param name="metadata"></param>
        public void ApplyToMetadata(ItemMetadata metadata);
    }
    public class PMPEstManipulationJson : IPMPItemMetadata
    {
        public ushort Entry = 0;
        public PMPGender Gender;
        public PMPModelRace Race;
        public ushort SetId;
        public PMPEquipSlot Slot;
        
        public XivDependencyRoot GetRoot()
        {
            var root = PMPExtensions.GetRootFromPenumbraValues(PMPObjectType.Equipment, SetId, PMPObjectType.Unknown, 0, Slot);
            return new XivDependencyRoot(root);
        }
        public void ApplyToMetadata(ItemMetadata metadata)
        {
            if (Gender == PMPGender.MaleNpc || Gender == PMPGender.FemaleNpc || Gender == PMPGender.Unknown)
            {
                // TexTools doesn't have handling/setup for NPC RGSP entries.
                // Could be added, but seems not worth the effort for how niche it is.
                return;
            }

            var race = PMPExtensions.GetRaceFromPenumbraValue(Race, Gender);
            var entry = metadata.EstEntries[race];

            // EST Entries are simple, they're just a single UShort value for a skel ID.
            // Penumbra and TT represent it identically.
            entry.SkelId = Entry;
        }

        public static PMPEstManipulationJson FromEstEntry(ExtraSkeletonEntry entry, string gameSlot)
        {
            if(entry.Race == XivRace.All_Races)
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(gameSlot))
            {
                return null;
            }

            var rg = PMPExtensions.GetPMPRaceGenderFromXivRace(entry.Race);
            var slot = PMPExtensions.PenumbraSlotToGameSlot.First(x => x.Value == gameSlot).Key;
            var setId = (uint) entry.SetId;

            var est = new PMPEstManipulationJson()
            {
                Entry = entry.SkelId,
                Slot = slot,
                Race = rg.Race,
                Gender = rg.Gender,
                SetId = entry.SetId
            };

            return est;
        }
    }
    public class PMPImcManipulationJson : IPMPItemMetadata
    {
        public struct PMPImcEntry
        {
            public byte MaterialId;
            public byte DecalId;
            public byte VfxId;
            public byte MaterialAnimationId;

            // Are these redundantly stored?
            public ushort AttributeAndSound;
            public ushort AttributeMask;
            public byte SoundId;

            public XivImc ToXivImc()
            {
                var imc = new XivImc();
                imc.Animation = MaterialAnimationId;
                imc.AttributeMask = AttributeMask;
                imc.SoundId = SoundId;
                imc.MaterialSet = MaterialId;
                imc.Decal = DecalId;
                imc.Vfx = VfxId;
                return imc;
            }
        }
        public PMPImcEntry Entry;
        public uint PrimaryId;
        public uint SecondaryId;
        public uint Variant;
        public PMPObjectType ObjectType;
        public PMPEquipSlot EquipSlot;
        public PMPObjectType BodySlot;

        public XivDependencyRoot GetRoot()
        {
            var root = PMPExtensions.GetRootFromPenumbraValues(ObjectType, PrimaryId, BodySlot, SecondaryId, EquipSlot);
            return new XivDependencyRoot(root);
        }
        public void ApplyToMetadata(ItemMetadata metadata)
        {
            // This one's a little funky.
            // Variants are really just an index into the IMC List.
            // If we're shallow, we have to add extras.
            // But there's no strictly defined behavior for what to do with the extras.
            // Technically speaking, Penumbra doesn't even allow users to add IMC entries, but TexTools does.
            while(Variant >= metadata.ImcEntries.Count)
            {
                if (metadata.ImcEntries.Count > 0)
                {
                    // Clone Group 0 if we have one.
                    metadata.ImcEntries.Add((XivImc) metadata.ImcEntries[0].Clone());
                }
                else
                {
                    // Add a blank otherwise.
                    metadata.ImcEntries.Add(new XivImc());
                }
            }

            // Outside of variant shenanigans, TT and Penumbra store these identically.
            var imc = metadata.ImcEntries[(int) Variant];
            imc.Decal = Entry.DecalId;
            imc.Animation = Entry.MaterialAnimationId;
            imc.MaterialSet = Entry.MaterialId;
            imc.Mask = Entry.AttributeAndSound;
        }
        public static PMPImcManipulationJson FromImcEntry(XivImc entry, int variant, XivDependencyRootInfo root)
        {
            // TODO: Determine if Penumbra can handle extra IMC sets.
            var pEntry = new PMPImcManipulationJson();

            pEntry.ObjectType = PMPExtensions.XivItemTypeToPenumbraObject[root.PrimaryType];
            pEntry.BodySlot = root.SecondaryType == null ? PMPObjectType.Unknown : PMPExtensions.XivItemTypeToPenumbraObject[root.SecondaryType.Value];
            pEntry.PrimaryId = (uint) root.PrimaryId;
            pEntry.SecondaryId = (uint) (root.SecondaryId == null ? 0 : root.SecondaryId);
            pEntry.Variant = (uint)variant;

            pEntry.EquipSlot = PMPEquipSlot.Unknown;
            if (root.Slot != null) {
                pEntry.EquipSlot = PMPExtensions.PenumbraSlotToGameSlot.First(x => x.Value == root.Slot).Key;
            }

            pEntry.Entry.AttributeAndSound = entry.Mask;
            pEntry.Entry.MaterialId = entry.MaterialSet;
            pEntry.Entry.DecalId = entry.Decal;
            pEntry.Entry.VfxId = entry.Vfx;
            pEntry.Entry.MaterialAnimationId = entry.Animation;

            pEntry.Entry.SoundId = (byte)(entry.Mask >> 10);
            pEntry.Entry.AttributeMask = (ushort) (entry.Mask & 0x3FF);

            return pEntry;
        }
    }
    public class PMPEqdpManipulationJson : IPMPItemMetadata
    {
        public ushort Entry;
        public PMPGender Gender;
        public PMPModelRace Race;
        public uint SetId;
        public PMPEquipSlot Slot;

        public XivDependencyRoot GetRoot()
        {
            var root = PMPExtensions.GetRootFromPenumbraValues(PMPObjectType.Equipment, SetId, PMPObjectType.Unknown, 0, Slot);
            return new XivDependencyRoot(root);
        }
        public void ApplyToMetadata(ItemMetadata metadata)
        {
            if (Gender == PMPGender.MaleNpc || Gender == PMPGender.FemaleNpc || Gender == PMPGender.Unknown)
            {
                // TexTools doesn't have handling/setup for NPC RGSP entries.
                // Could be added, but seems not worth the effort for how niche it is.
                return;
            }

            // Get Race.
            var xivRace = PMPExtensions.GetRaceFromPenumbraValue(Race, Gender);

            // Get the slot number.
            var slot = PMPExtensions.PenumbraSlotToGameSlot[Slot];
            var isAccessory = EquipmentDeformationParameterSet.IsAccessory(slot);
            var slotNum = EquipmentDeformationParameterSet.SlotsAsList(isAccessory).IndexOf(slot);

            // Penumbra stores the data masked in-place.  We need to shift it over.
            var shift = slotNum * 2;
            var shifted = Entry >> shift;

            // Tag bits.
            metadata.EqdpEntries[xivRace].bit0 = (shifted & 0x01) != 0;
            metadata.EqdpEntries[xivRace].bit1 = (shifted & 0x02) != 0;
        }

        public static PMPEqdpManipulationJson FromEqdpEntry(EquipmentDeformationParameter entry, XivDependencyRootInfo root, XivRace race)
        {
            var isAccessory = EquipmentDeformationParameterSet.IsAccessory(root.Slot);
            var slotNum = EquipmentDeformationParameterSet.SlotsAsList(isAccessory).IndexOf(root.Slot);

            // Penumbra stores the data masked in-place.  We need to shift it over.
            var shift = slotNum * 2;
            var bits = (ushort) 0;
            if (entry.bit0)
            {
                bits |= 1;
            }
            if (entry.bit1)
            {
                bits |= 2;
            }

            bits = (ushort) (bits << shift);
            var rg = PMPExtensions.GetPMPRaceGenderFromXivRace(race);
            var slot = PMPExtensions.PenumbraSlotToGameSlot.First(x => x.Value == root.Slot).Key;
            var setId = (uint)root.PrimaryId;

            var pEntry = new PMPEqdpManipulationJson()
            {
                Entry = bits,
                Gender = rg.Gender,
                Race = rg.Race,
                SetId = setId,
                Slot = slot,
            };
            return pEntry;
        }
    }
    public class PMPEqpManipulationJson : IPMPItemMetadata
    {
        public ulong Entry;
        public uint SetId;
        public PMPEquipSlot Slot;

        public XivDependencyRoot GetRoot()
        {
            var root = PMPExtensions.GetRootFromPenumbraValues(PMPObjectType.Equipment, SetId, PMPObjectType.Unknown, 0, Slot);
            return new XivDependencyRoot(root);
        }
        public void ApplyToMetadata(ItemMetadata metadata)
        {
            // Like with EQDP data, Penumbra stores EQP data in-place, masked.
            // TT stores the data sliced up and bit shifted down to LSB.
            // So we need to determine data size, shift it down, and slice out the bytes we want.

            var slot = PMPExtensions.PenumbraSlotToGameSlot[Slot];
            var offset = EquipmentParameterSet.EntryOffsets[slot];
            var size = EquipmentParameterSet.EntrySizes[slot];

            var shifted = Entry >> (offset * 8);
            var shiftedBytes = BitConverter.GetBytes(shifted);

            var data = new byte[size];
            Array.Copy(shiftedBytes, 0, data, 0, size);

            var eqp = metadata.EqpEntry;
            eqp.SetBytes(data);
        }

        public static PMPEqpManipulationJson FromEqpEntry(EquipmentParameter entry, XivDependencyRootInfo root)
        {
            
            var slot = PMPExtensions.PenumbraSlotToGameSlot.First(x => x.Value == entry.Slot).Key;
            var offset = EquipmentParameterSet.EntryOffsets[entry.Slot];
            var size = EquipmentParameterSet.EntrySizes[entry.Slot];

            // Re-shift the value for Penumbra.
            var arr = new byte[8];
            var ebytes = entry.GetBytes();
            Array.Copy(ebytes, 0, arr, 0, ebytes.Length);

            ulong value = BitConverter.ToUInt64(arr, 0);
            value = (ulong)(value << offset * 8);

            var pEntry = new PMPEqpManipulationJson()
            {
                Entry = value,
                SetId = (uint)root.PrimaryId,
                Slot = slot
            };

            return pEntry;
        }
    }
    public class PMPGmpManipulationJson : IPMPItemMetadata
    {
        public struct PMPGmpEntry
        {
            public bool Enabled;
            public bool Animated;
            public ushort RotationA;
            public ushort RotationB;
            public ushort RotationC;

            public byte UnknownA;
            public byte UnknownB;
            public ushort UnknownTotal;

            // Full Value
            public ulong Value;
        }

        public PMPGmpEntry Entry;
        public uint SetId;

        public XivDependencyRoot GetRoot()
        {
            var root = PMPExtensions.GetRootFromPenumbraValues(PMPObjectType.Equipment, SetId, PMPObjectType.Unknown, 0, PMPEquipSlot.Head);
            return new XivDependencyRoot(root);
        }

        public void ApplyToMetadata(ItemMetadata metadata)
        {
            // We could bind the data individually...
            // Or we could just copy in the full uint Value since it's stored here.

            var gmp = new GimmickParameter(BitConverter.GetBytes(Entry.Value));
            metadata.GmpEntry = gmp;
        }
        public static PMPGmpManipulationJson FromGmpEntry(GimmickParameter entry, XivDependencyRootInfo root)
        {
            var pEntry = new PMPGmpManipulationJson();

            pEntry.SetId = (uint) root.PrimaryId;
            pEntry.Entry.Value = BitConverter.ToUInt32(entry.GetBytes(), 0);
            pEntry.Entry.Animated = entry.Animated;
            pEntry.Entry.Enabled = entry.Enabled;
            pEntry.Entry.RotationA = entry.RotationA;
            pEntry.Entry.RotationB = entry.RotationB;
            pEntry.Entry.RotationC = entry.RotationC;
            pEntry.Entry.UnknownA = entry.UnknownLow;
            pEntry.Entry.UnknownB = entry.UnknownHigh;

            return pEntry;
        }
    }
    public class PMPRspManipulationJson
    {
        public float Entry;
        public PMPSubRace SubRace;
        public RspAttribute Attribute;

        /// <summary>
        /// Simple function to return a consistent race/gender hash for grouping.
        /// </summary>
        /// <returns></returns>
        public uint GetRaceGenderHash()
        {
            var rg = GetRaceGender();

            return GetRaceGenderHash(rg.Race, rg.Gender);
        }
        public static uint GetRaceGenderHash(XivSubRace race, XivGender gender)
        {
            uint hash = 0;
            hash |= ((uint)race) << 8;
            hash |= ((uint)gender);

            return hash;
        }

        public (XivSubRace Race, XivGender Gender) GetRaceGender()
        {
            var pmpGen = Attribute.ToPMPGender();

            if (pmpGen == PMPGender.MaleNpc || pmpGen == PMPGender.FemaleNpc || pmpGen == PMPGender.Unknown)
            {
                // TexTools doesn't have handling/setup for NPC RGSP entries.
                // Could be added, but seems not worth the effort for how niche it is.
                return (XivSubRace.Invalid, XivGender.Male);
            }

            var xivRace = PMPExtensions.PMPSubraceToXivSubrace[SubRace];
            var xivGen = PMPExtensions.PMPGenderToXivGender[pmpGen];
            return (xivRace, xivGen);
        }

        /// <summary>
        /// Apply this scaling modifier to the given RacialScalingParameter.
        /// </summary>
        /// <param name="cmp"></param>
        internal void ApplyScaling(RacialGenderScalingParameter cmp)
        {
            switch(Attribute)
            {
                case RspAttribute.BustMinX:
                    cmp.BustMinX = Entry;
                    break;
                case RspAttribute.BustMaxX:
                    cmp.BustMaxX = Entry;
                    break;
                case RspAttribute.BustMinY:
                    cmp.BustMinY = Entry;
                    break;
                case RspAttribute.BustMaxY:
                    cmp.BustMaxY = Entry;
                    break;
                case RspAttribute.BustMinZ:
                    cmp.BustMinZ = Entry;
                    break;
                case RspAttribute.BustMaxZ:
                    cmp.BustMaxZ = Entry;
                    break;
                case RspAttribute.MaleMinTail:
                case RspAttribute.FemaleMinTail:
                    cmp.MinTail = Entry;
                    break;
                case RspAttribute.MaleMaxTail:
                case RspAttribute.FemaleMaxTail:
                    cmp.MaxTail = Entry;
                    break;
                case RspAttribute.MaleMinSize:
                case RspAttribute.FemaleMinSize:
                    cmp.MinSize = Entry;
                    break;
                case RspAttribute.MaleMaxSize:
                case RspAttribute.FemaleMaxSize:
                    cmp.MaxSize = Entry;
                    break;
            }
            return;
        }


        private static PMPRspManipulationJson GetManip(PMPSubRace sr, RspAttribute attribute, float value)
        {
            return new PMPRspManipulationJson()
            {
                Attribute = attribute,
                Entry = value,
                SubRace = sr,
            };
        }

        public static List<PMPRspManipulationJson> FromRgspEntry(RacialGenderScalingParameter fullEntry)
        {
            var sr = PMPExtensions.PMPSubraceToXivSubrace.First(x => x.Value == fullEntry.Race).Key;
            var male = fullEntry.Gender == XivGender.Male;

            var manips = new List<PMPRspManipulationJson>();

            if (male)
            {
                manips.Add(GetManip(sr, RspAttribute.MaleMinTail, fullEntry.MinTail));
                manips.Add(GetManip(sr, RspAttribute.MaleMaxTail, fullEntry.MaxTail));
                manips.Add(GetManip(sr, RspAttribute.MaleMinSize, fullEntry.MinSize));
                manips.Add(GetManip(sr, RspAttribute.MaleMaxSize, fullEntry.MaxSize));
            }
            else
            {
                manips.Add(GetManip(sr, RspAttribute.BustMinX, fullEntry.BustMinX));
                manips.Add(GetManip(sr, RspAttribute.BustMaxX, fullEntry.BustMaxX));
                manips.Add(GetManip(sr, RspAttribute.BustMinY, fullEntry.BustMinY));
                manips.Add(GetManip(sr, RspAttribute.BustMaxY, fullEntry.BustMaxY));
                manips.Add(GetManip(sr, RspAttribute.BustMinZ, fullEntry.BustMinZ));
                manips.Add(GetManip(sr, RspAttribute.BustMaxY, fullEntry.BustMaxZ));
                manips.Add(GetManip(sr, RspAttribute.FemaleMinTail, fullEntry.MinTail));
                manips.Add(GetManip(sr, RspAttribute.FemaleMaxTail, fullEntry.MaxTail));
                manips.Add(GetManip(sr, RspAttribute.FemaleMinSize, fullEntry.MinSize));
                manips.Add(GetManip(sr, RspAttribute.FemaleMaxSize, fullEntry.MaxSize));
            }
            return manips;
        }

    }

    #endregion

}
