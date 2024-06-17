using HelixToolkit.SharpDX.Core.Model;
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
using SharpDX.Win32;
using static HelixToolkit.SharpDX.Core.Model.Metadata;

namespace xivModdingFramework.Mods.FileTypes.PMP
{
    /// <summary>
    /// Class for handling Penumbra Modpacks
    /// </summary>
    public static class PMP
    {
        public const int _WriteFileVersion = 3;
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
                var tempFolder = Path.Combine(IOUtil.GetFrameworkTempFolder(), Guid.NewGuid().ToString());

                // Run Zip extract on a new thread.
                await Task.Run(async () =>
                {
                    if (!jsonsOnly)
                    {
                        // Unzip everything.
                        await IOUtil.UnzipFiles(path, tempFolder);
                    } else
                    {
                        // Just JSON files.
                        await IOUtil.UnzipFiles(path, tempFolder, (file) =>
                        {
                            return file.EndsWith(".json");
                        });
                    }
                });
                path = tempFolder;
            }
            return path;
        }

        public static async Task<(PMPJson pmp, string path, string headerImage)> LoadPMP(string path, bool jsonOnly = false, bool includeImages = false)
        {
            var gameDir = XivCache.GameInfo.GameDirectory;

            var originalPath = path;

            var alreadyUnzipped = !path.ToLower().EndsWith(".pmp");

            path = await ResolvePMPBasePath(path, jsonOnly);
            var defModPath = Path.Combine(path, "default_mod.json");
            var metaPath = Path.Combine(path, "meta.json");

            var text = File.ReadAllText(metaPath);
            var meta = JsonConvert.DeserializeObject<PMPMetaJson>(text);

            string image = null;



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


            if (includeImages)
            {
                await IOUtil.UnzipFiles(originalPath, path, (file) =>
                {
                    return file.EndsWith(".png");
                });
            }



            return (pmp, path, image);
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
            var boiler = await TxBoiler.BeginWrite(tx, true);
            tx = boiler.Transaction;
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
                    var groupIdx = 0;
                    foreach (var group in orderedGroups)
                    {
                        if (group.Options == null || group.Options.Count == 0)
                        {
                            // No valid options.
                            groupIdx++;
                            continue;
                        }
                        var optionIdx = 0;

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
                            var groupRes = await ImportOption(group.Options[selected], unzippedPath, tx, progress, groupIdx, optionIdx);
                            UnionDict(imported, groupRes.Imported);
                            notImported.UnionWith(groupRes.NotImported);
                        }
                        else if(group.Type == "Multi")
                        {
                            var ordered = group.Options.OrderBy(x => ((PmpStandardOptionJson)x).Priority);

                            // Bitmask options.  Install in priority order.
                            foreach(var op in ordered)
                            {
                                var i = group.Options.IndexOf(op);
                                var value = 1 << i;
                                if ((selected & value) > 0)
                                {
                                    var groupRes = await ImportOption(group.Options[i], unzippedPath, tx, progress, groupIdx, optionIdx);
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

                                if (imcGroup.AllVariants)
                                {
                                    for (int i = 0; i < metaData.ImcEntries.Count; i++)
                                    {
                                        metaData.ImcEntries[i] = (XivImc)xivImc.Clone();
                                    }
                                }

                                await ItemMetadata.SaveMetadata(metaData, _Source, tx);
                                await ItemMetadata.ApplyMetadata(metaData, tx);

                            }
                        }
                        groupIdx++;
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
                        await boiler.Cancel(true);
                        return (null, null, -1);
                    }
                }

                var afterRoot = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                // Pre-Dawntrail Files
                if (settings.UpdateEndwalkerFiles)
                {
                    progress?.Report((0, 0, "Updating Pre-Dawntrail Files..."));
                    await EndwalkerUpgrade.UpdateEndwalkerFiles(imported.Keys, _Source, imported, progress, tx);
                }

                XivCache.QueueDependencyUpdate(imported.Keys);

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
                await boiler.Catch();
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

        private static async Task<(Dictionary<string, TxFileState> Imported, HashSet<string> NotImported)> ImportOption(PMPOptionJson baseOption, string basePath, ModTransaction tx, IProgress<(int, int, string)> progress = null, int groupIdx = 0, int optionIdx = 0)
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
                progress?.Report((i, option.Files.Count, $"Importing New Files from Group {groupIdx+1} - Option {optionIdx+1}..."));

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

                // Data we don't know how to import/use, or can't use in TexTools.
                foreach(var manip in data.OtherManipulations)
                {
                    notImported.Add(manip.Type + " Manipulation");
                }
            }

            XivCache.QueueDependencyUpdate(imported.Keys);

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
        public static async Task CreateSimplePmp(string destination, BaseModpackData modpackMeta, Dictionary<string, FileStorageInformation> fileInfos, IEnumerable<PMPManipulationWrapperJson> otherManipulations = null, bool zip = true)
        {
            if (!destination.ToLower().EndsWith(".pmp") && zip)
            {
                throw new Exception("PMP Export must have .pmp extension.");
            }

            var workingPath = destination;
            if (zip)
            {
                workingPath = Path.Combine(IOUtil.GetFrameworkTempFolder(), Guid.NewGuid().ToString());
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


                var files = await FileIdentifier.IdentifierListFromDictionary(fileInfos);

                pmp.DefaultMod = await CreatePmpStandardOption(workingPath, "Default", "The only option.", files, otherManipulations);

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
        public static async Task WritePmp(PMPJson pmp, string workingDirectory, string zipPath = null)
        {
            var metapath = Path.Combine(workingDirectory, "meta.json");
            var defaultModPath = Path.Combine(workingDirectory, "default_mod.json");

            var metaString = JsonConvert.SerializeObject(pmp.Meta, Formatting.Indented);
            File.WriteAllText(metapath, metaString);

            var defaultModString = JsonConvert.SerializeObject(pmp.DefaultMod, Formatting.Indented);
            File.WriteAllText(defaultModPath, defaultModString);

            for(int i = 0; i < pmp.Groups.Count; i++)
            {
                var gName = IOUtil.MakePathSafe(pmp.Groups[i].Name.ToLower());
                var groupPath = Path.Combine(workingDirectory, "group_" + i.ToString("D3") + "_" + gName + ".json");
                var groupString = JsonConvert.SerializeObject(pmp.Groups[i], Formatting.Indented);
                File.WriteAllText(groupPath, groupString);
            }

            if(zipPath != null)
            {
                File.Delete(zipPath);
                System.IO.Compression.ZipFile.CreateFromDirectory(workingDirectory, zipPath);
            }
        }

        public static async Task<PmpStandardOptionJson> CreatePmpStandardOption(string workingPath, string name, string description, IEnumerable<FileIdentifier> files, IEnumerable<PMPManipulationWrapperJson> otherManipulations = null, string imagePath = null, int priority = 0)
        {
            var opt = new PmpStandardOptionJson()
            {
                Name = name,
                Description = description,
                Files = new Dictionary<string, string>(),
                FileSwaps = new Dictionary<string, string>(),
                Manipulations = new List<PMPManipulationWrapperJson>(),
                Priority = priority,
            };

            // TODO - Could paralell this? Unsure how big the gains would really be though,
            // since the primary tasks are already paralelled internally, and there's little else heavy going on.
            if (files != null)
            {
                foreach (var fi in files)
                {
                    if(!File.Exists(fi.Info.RealPath))
                    {
                        // Sometimes poorly behaved penumbra folders don't actually have the files they claim they do.
                        // Remove them in this case.
                        continue;
                    }

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
            }

            if (otherManipulations != null)
            {
                foreach (var manip in otherManipulations)
                {
                    opt.Manipulations.Add(manip);
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
        /// 
        /// NOTE - This discards manipulation data that cannot be packed into TT-Usable filetypes.
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
                        else if (group.Options.Count > 1)
                        {
                            return null;
                        }
                    } else if(pmp.Groups.Count > 1)
                    {
                        return null;
                    }
                }

                if (option == null)
                {
                    // Empty mod.
                    return new Dictionary<string, FileStorageInformation>();
                }

                // Manipulation which cannot be converted into .meta or .rgsp are discarded here.
                return (await UnpackPmpOption(option, modpackPath, null, true, tx)).Files;
            }
            catch (Exception ex)
            {
                return null;
            }
        }


        /// <summary>
        /// Unzips and Unpacks a given PMP option into a dictionary of [Internal File Path] => [File Storage Information]
        /// </summary>
        public static async Task<(Dictionary<string, FileStorageInformation> Files, List<PMPManipulationWrapperJson> OtherManipulations)> UnpackPmpOption(PMPOptionJson baseOption, string zipArchivePath = null, string unzipPath = null, bool mergeManipulations = true, ModTransaction tx = null)
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
            }

            if (zipArchivePath == null || IOUtil.IsDirectory(zipArchivePath))
            {
                alreadyUnzipped = true;
                if (zipArchivePath != null)
                {
                    unzipPath = zipArchivePath;
                }
            }

            if (!alreadyUnzipped && unzipPath == null)
            {
                if (tx != null && !tx.ReadOnly && unzipPath == null)
                {
                    // Use TX data store if we have one.
                    unzipPath = Path.Combine(tx.UNSAFE_GetTransactionStore(), Guid.NewGuid().ToString());
                } else
                {
                    // Make our own temp path.
                    unzipPath = Path.Combine(IOUtil.GetFrameworkTempFolder(), Guid.NewGuid().ToString());
                }
            }

            // Task wrapper since we might be doing some heavy lifting.
            if (!alreadyUnzipped)
            {
                await Task.Run(async () =>
                {
                    // Resolve the base path we're working, and unzip if needed...
                    if (includeData)
                    {
                        await IOUtil.UnzipFiles(zipArchivePath, unzipPath, option.Files.Values);
                    }
                });
            }

            var ret = new Dictionary<string, FileStorageInformation>();

            if (tx == null)
            {
                // Grab a readonly TX here to read base game files when needed.
                tx = ModTransaction.BeginReadonlyTransaction();
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

                var df = IOUtil.GetDataFileFromPath(src);
                var offset = await tx.Get8xDataOffset(src, true);
                if (offset <= 0)
                {
                    // Invalid game file swap.
                    continue;
                }

                if (!includeData)
                {
                    ret.Add(src, new FileStorageInformation());
                    continue;
                }

                var fileInfo = IOUtil.MakeGameStorageInfo(df, offset);

                ret.Add(dest, fileInfo);
            }

            List<PMPManipulationWrapperJson> otherManips = null;
            // Metadata files.
            if (option.Manipulations != null && option.Manipulations.Count > 0)
            {
                if (mergeManipulations)
                {
                    var manips = await ManipulationsToMetadata(option.Manipulations, tx);

                    foreach (var meta in manips.Metadatas)
                    {
                        var metaPath = meta.Root.Info.GetRootFile();

                        // These are a bit weird, and basically have to be written to disk or some kind of memory store.
                        var data = await ItemMetadata.Serialize(meta);
                        var tempFilePath = IOUtil.GetFrameworkTempFile();
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

                    foreach (var rgsp in manips.Rgsps)
                    {
                        var path = CMP.GetRgspPath(rgsp.Race, rgsp.Gender);
                        // These are a bit weird, and basically have to be written to disk or some kind of memory store.
                        var data = rgsp.GetBytes();
                        var tempFilePath = IOUtil.GetFrameworkTempFile();
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

                    otherManips = manips.OtherManipulations;
                } else
                {
                    otherManips = option.Manipulations;
                }

            }

            return (ret, otherManips);
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
        public static async Task<(List<ItemMetadata> Metadatas, List<RacialGenderScalingParameter> Rgsps, List<PMPManipulationWrapperJson> OtherManipulations)> ManipulationsToMetadata(List<PMPManipulationWrapperJson> manipulations, ModTransaction tx, Dictionary<string, TxFileState> imported = null)
        {

            // Setup Metadata.
            if (manipulations == null || manipulations.Count == 0)
            {
                return (new List<ItemMetadata>(), new List<RacialGenderScalingParameter>(), new List<PMPManipulationWrapperJson>());
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
                    if (imported == null)
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


            var otherManipulations = manipulations.Where(x =>
            {
                var manip = x.GetManipulation();
                if(manip as IPMPItemMetadata != null)
                {
                    return false;
                }
                if(manip as PMPRspManipulationJson != null)
                {
                    return false;
                }
                return true;
            }).ToList();


            return (seenMetadata.Values.ToList(), seenRgsps.Values.ToList(), otherManipulations);
        }

    }



    #region Penumbra Simple JSON Classes
    public class PMPJson
    {
        public PMPMetaJson Meta { get; set; }
        public PMPOptionJson DefaultMod { get; set; }
        public List<PMPGroupJson> Groups { get; set; }

        public string GetHeaderImage()
        {
            if (!string.IsNullOrWhiteSpace(Meta.Image))
            {
                return Meta.Image;
            }

            if (DefaultMod != null && !string.IsNullOrWhiteSpace(DefaultMod.Image))
            {
                return DefaultMod.Image;
            }

            foreach (var g in Groups)
            {
                if (!string.IsNullOrWhiteSpace(g.Image))
                {
                    return g.Image;
                }
                foreach (var o in g.Options)
                {
                    if (!string.IsNullOrWhiteSpace(o.Image))
                    {
                        return o.Image;
                    }
                }
            }
            return null;
        }
    }

    public class PMPMetaJson
    {
        public int FileVersion;
        public string Name;
        public string Author;
        public string Description;
        public string Version;
        public string Website;
        public string Image;

        // These exist.
        public List<string> Tags;
    }

    [JsonConverter(typeof(JsonSubtypes), "Type")]
    [JsonSubtypes.KnownSubType(typeof(PMPImcGroupJson), "Imc")]
    public class PMPGroupJson
    {
        public string Name;
        public string Description;
        public int Priority;
        public string Image;
        public int Page;

        // "Multi", "Single", or "Imc"
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
        public bool AllVariants;

        public XivDependencyRoot GetRoot()
        {
            var root = PMPExtensions.GetRootFromPenumbraValues(Identifier.ObjectType, Identifier.PrimaryId, Identifier.BodySlot, Identifier.SecondaryId, Identifier.EquipSlot);
            return new XivDependencyRoot(root);
        }
    }

    public class PmpIdentifierJson
    {
        public PMPObjectType ObjectType;
        public ushort PrimaryId;
        public ushort SecondaryId;
        public byte Variant;
        public PMPEquipSlot EquipSlot;
        public PMPObjectType BodySlot;

        public XivDependencyRoot GetRoot()
        {
            var root = PMPExtensions.GetRootFromPenumbraValues(ObjectType, PrimaryId, BodySlot, SecondaryId, EquipSlot);
            return new XivDependencyRoot(root);
        }

        public static PmpIdentifierJson FromRoot(XivDependencyRootInfo root, int variant = 1)
        {
            var pEntry = new PmpIdentifierJson();
            if (PMPExtensions.XivItemTypeToPenumbraObject.ContainsKey(root.PrimaryType))
            {
                pEntry.ObjectType = PMPExtensions.XivItemTypeToPenumbraObject[root.PrimaryType];
                pEntry.BodySlot = root.SecondaryType == null ? PMPObjectType.Unknown : PMPExtensions.XivItemTypeToPenumbraObject[root.SecondaryType.Value];
                pEntry.PrimaryId = (ushort)root.PrimaryId;
                pEntry.SecondaryId = (ushort)(root.SecondaryId == null ? 0 : root.SecondaryId);
                pEntry.Variant = (byte)variant;
            }

            if (root.Slot != null)
            {
                pEntry.EquipSlot = PMPExtensions.PenumbraSlotToGameSlot.FirstOrDefault(x => x.Value == root.Slot).Key;
            } else
            {
                pEntry.EquipSlot = PMPEquipSlot.Unknown;
            }

            return pEntry;
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
        public string Image;
    }

    public class PmpStandardOptionJson : PMPOptionJson
    {
        public Dictionary<string, string> Files;
        public Dictionary<string, string> FileSwaps;
        public List<PMPManipulationWrapperJson> Manipulations;
        public int Priority;
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

}
