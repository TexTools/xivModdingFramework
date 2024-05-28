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
            var meta = JsonConvert.DeserializeObject<PMPMetaJson>(text, new PMPMetaManipulationConverter());
            var defaultOption = JsonConvert.DeserializeObject<PMPOptionJson>(File.ReadAllText(defModPath), new PMPMetaManipulationConverter());

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
                        if (selected < 0 || selected >= group.Options.Count)
                        {
                            selected = 0;
                        }

                        // If the user selected custom settings, use those.
                        if (group.SelectedSettings >= 0)
                        {
                            selected = group.SelectedSettings;
                        }

                        if (group.Type == "Single")
                        {
                            var groupRes = await ImportOption(group.Options[selected], unzippedPath, tx, progress, optionIdx);
                            UnionDict(imported, groupRes.Imported);
                            notImported.UnionWith(groupRes.NotImported);
                        }
                        else
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

        private static async Task<(Dictionary<string, TxFileState> Imported, HashSet<string> NotImported)> ImportOption(PMPOptionJson option, string basePath, ModTransaction tx, IProgress<(int, int, string)> progress = null, int optionIdx = 0)
        {
            var imported = new Dictionary<string, TxFileState>();
            var notImported = new HashSet<string>();

            // File swaps first.
            var i = 0;
            foreach(var kv in option.FileSwaps)
            {

                progress?.Report((i, option.FileSwaps.Count, "Importing File Swaps from Option " + (optionIdx + 1) + "..."));

                var src = kv.Key;

                // For some reason the destination value is backslashed instead of forward-slashed.
                var dest = kv.Value.Replace("\\", "/");

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

                // Get original SqPacked file.
                var data = await tx.ReadFile(src, true, true);

                // Write it back to TX.
                var newOffset = await tx.WriteFile(dest, data, _Source);
                i++;
            }

            // Import files.
            i = 0;
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

            // Setup Metadata.
            if(option.Manipulations != null && option.Manipulations.Count > 0)
            {
                // RSP Options resolve by race/gender pairing.
                var rspOptions = option.Manipulations.Select(x => x.Manipulation as PMPRspManipulationJson).Where(x => x != null);
                var byRg = rspOptions.GroupBy(x => x.GetRaceGenderHash());

                i = 0;
                var total = byRg.Count();
                foreach (var group in byRg)
                {
                    progress?.Report((i, total, "Importing Race Scaling Changes from Option " + (optionIdx + 1) + "..."));
                    var rg = group.First().GetRaceGender();
                    RacialGenderScalingParameter cmp;
                    if (_RgspRaceGenders.Contains(group.Key))
                    {
                        // If this our first time seeing this race/gender pairing in this import sequence, use the original game clean version of the file.
                        cmp = await CMP.GetScalingParameter(rg.Race, rg.Gender, true, tx);
                        _RgspRaceGenders.Add(group.Key);
                    } else
                    {
                        cmp = await CMP.GetScalingParameter(rg.Race, rg.Gender, false, tx);
                    }

                    // Save initial state.
                    var path = CMP.GetRgspPath(cmp.Race, cmp.Gender);
                    if (!imported.ContainsKey(path))
                    {
                        imported.Add(path, await tx.SaveFileState(path));
                    }

                    foreach (var effect in group)
                    {
                        effect.ApplyScaling(cmp);
                    }

                    await CMP.SaveScalingParameter(cmp, _Source, tx);
                    i++;
                }

                // Metadata.
                var metaOptions = option.Manipulations.Select(x => x.Manipulation as IPMPItemMetadata).Where(x => x != null);

                var byRoot = metaOptions.GroupBy(x => x.GetRoot());

                total = byRoot.Count();
                // Apply Metadata in order by Root.
                foreach (var group in byRoot)
                {
                    progress?.Report((i, total, "Importing Metadata Changes from Option " + (optionIdx + 1) + "..."));
                    var root = group.Key;
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
                    } else
                    {
                        // Otherwise use the current transaction metadata state for metadata compilation.
                        metaData = await ItemMetadata.GetMetadata(metaPath, false, tx);
                    }

                    foreach (var meta in group)
                    {
                        meta.ApplyToMetadata(metaData);
                    }

                    await ItemMetadata.SaveMetadata(metaData, _Source, tx);
                    await ItemMetadata.ApplyMetadata(metaData, tx);
                    i++;
                }
            }

            return (imported, notImported);
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
        /// Write 
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static Task CreatePMP(string destination, IModPackData data)
        {
            // PMPs are either composed of a single option as a 'DefaultMod'
            // or a collection of Groups, which each 
            throw new NotImplementedException();
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
        /// Makes all slashes into backslashes for consistency.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static string ReplaceSlashes(string path)
        {
            return path.Replace("/", "\\");
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

                var defMod = pmp.DefaultMod;
                PMPOptionJson option = null;
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
                            option = group.Options[0];
                        }
                    }
                }

                if (option == null)
                {
                    // Too may options or no options.
                    return null;
                }


                return await UnpackPmpOption(option, modpackPath, includeData, tx);
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        /// <summary>
        /// Unzips and Unpacks a given PMP option into a dictionary of [Internal File Path] => [File Storage Information]
        /// </summary>
        /// <param name="option"></param>
        /// <param name="pmpPath"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static async Task<Dictionary<string, FileStorageInformation>> UnpackPmpOption(PMPOptionJson option, string pmpPath, bool includeData = true, ModTransaction tx = null)
        {
            // Task wrapper since we might be doing some heavy lifting.
            return await Task.Run(async () =>
            {
                // Resolve the base path we're working, and unzip if needed...
                HashSet<string> files = new HashSet<string>();
                foreach(var kv in option.Files)
                {
                    files.Add(ReplaceSlashes(kv.Value));
                }
                var basePath = pmpPath;

                if (pmpPath.EndsWith(".json"))
                {
                    // PMP Folder by Json reference at root level.
                    basePath = Path.GetDirectoryName(pmpPath);
                }
                else if (pmpPath.EndsWith(".pmp"))
                {
                    // Compressed PMP file.  Decompress it first.
                    if (tx != null)
                    {
                        // Unzip to TX store if we have one.
                        basePath = tx.UNSAFE_GetTransactionStore();
                    }
                    else
                    {
                        basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                    }

                    if (includeData)
                    {
                        // Run Zip extract on a new thread.
                        await Task.Run(async () =>
                        {
                            // Just JSON files.
                            using (var zip = new Ionic.Zip.ZipFile(pmpPath))
                            {
                                var toUnzip = zip.Entries.Where(x => files.Contains(ReplaceSlashes(x.FileName)));
                                foreach (var e in toUnzip)
                                {
                                    e.Extract(basePath);
                                }
                            }
                        });
                    }
                } else
                {
                    // Already unzipped folder path.
                    basePath = pmpPath;
                }

                var ret = new Dictionary<string, FileStorageInformation>();

                if (tx == null)
                {
                    // Grab a readonly TX here to read base game files when needed.
                    tx = ModTransaction.BeginTransaction();
                }


                // File Swaps from base game files.
                foreach (var kv in option.FileSwaps)
                {
                    var src = kv.Key;

                    // For some reason the destination value is backslashed instead of forward-slashed.
                    var dest = kv.Value.Replace("\\", "/");

                    if (!CanImport(src) || !CanImport(dest))
                    {
                        continue;
                    }

                    if (!includeData)
                    {
                        ret.Add(src, new FileStorageInformation());
                        continue;
                    }


                    // This is dangerous to hand potentially unknown code a direct file pointer to raw game files.
                    // So we'll copy it to a temp file instead.

                    // Get original SqPacked file.
                    var data = await tx.ReadFile(src, true, true);
                    var tempFilePath = Path.GetTempFileName();
                    File.WriteAllBytes(tempFilePath, data);

                    var fileInfo = new FileStorageInformation()
                    {
                        FileSize = data.Length,
                        StorageType = EFileStorageType.CompressedIndividual,
                        RealPath = tempFilePath,
                        RealOffset = 0,
                    };

                    ret.Add(src, fileInfo);
                }

                // Custom Files from the .pmp Zip Archive.
                foreach (var file in option.Files)
                {
                    var internalPath = file.Key;
                    var externalPath = Path.Combine(basePath, file.Value);
                    // Safety checks.
                    if (!CanImport(file.Key))
                    {
                        continue;
                    }

                    if (!includeData)
                    {
                        ret.Add(internalPath, new FileStorageInformation());
                        continue;
                    }

                    if (!File.Exists(externalPath))
                    {
                        continue;
                    }


                    var fSize = new FileInfo(externalPath).Length;

                    var fileInfo = new FileStorageInformation()
                    {
                        FileSize = (int) fSize,
                        StorageType = EFileStorageType.UncompressedIndividual,
                        RealPath = externalPath,
                        RealOffset = 0,
                    };

                    ret.Add(internalPath, fileInfo);
                }

                // Metadata files.
                if (option.Manipulations != null && option.Manipulations.Count > 0)
                {
                    // RSP Options resolve by race/gender pairing.
                    var rspOptions = option.Manipulations.Select(x => x.Manipulation as PMPRspManipulationJson).Where(x => x != null);
                    var byRg = rspOptions.GroupBy(x => x.GetRaceGenderHash());

                    var total = byRg.Count();
                    foreach (var group in byRg)
                    {
                        var rg = group.First().GetRaceGender();

                        var path = CMP.GetRgspPath(rg.Race, rg.Gender);
                        if (!includeData)
                        {
                            ret.Add(path, new FileStorageInformation());
                            continue;
                        }

                        var cmp = await CMP.GetScalingParameter(rg.Race, rg.Gender, true, tx);

                        var data = cmp.GetBytes();

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

                    // Metadata.
                    var metaOptions = option.Manipulations.Select(x => x.Manipulation as IPMPItemMetadata).Where(x => x != null);
                    var byRoot = metaOptions.GroupBy(x => x.GetRoot());
                    total = byRoot.Count();

                    // Create the metadata files and save them to disk.
                    foreach (var group in byRoot)
                    {
                        var root = group.Key;
                        var metaPath = root.Info.GetRootFile();
                        if (!includeData)
                        {
                            ret.Add(metaPath, new FileStorageInformation());
                            continue;
                        }

                        var metaData = await ItemMetadata.GetMetadata(metaPath, true, tx);

                        foreach (var meta in group)
                        {
                            meta.ApplyToMetadata(metaData);
                        }

                        var data = await ItemMetadata.Serialize(metaData);
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
                }

                return ret;
            });
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
            { PMPEquipSlot.Wrists, "wri" },
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

    public class PMPOptionJson
    {
        public string Name;
        public string Description;

        public Dictionary<string, string> Files;
        public Dictionary<string, string> FileSwaps;
        public List<PMPMetaManipulationJson> Manipulations;
    }

    public class PMPMetaManipulationJson
    {
        public string Type;
        public object Manipulation;
    }

    /// <summary>
    /// Simple JSON converter that dynamically converts the meta entries to the correct object type.
    /// </summary>
    public class PMPMetaManipulationConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            Debug.WriteLine(objectType.FullName);
            if(objectType == typeof(PMPMetaManipulationJson))
            {
                return true;
            }
            return false;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }
            JObject jo = JObject.Load(reader);

            var obj = new PMPMetaManipulationJson();

            obj.Type = (string) jo.Properties().FirstOrDefault(x => x.Name == "Type");

            var manip = jo.Properties().FirstOrDefault(x => x.Name == "Manipulation").Value as JObject;

            var parent = manip.Parent as JObject;


            // Convert the manipulation to the appropriate internal type.
            switch (obj.Type)
            {
                case "Eqp":
                    obj.Manipulation = manip.ToObject<PMPEqpManipulationJson>();
                    break;
                case "Eqdp":
                    obj.Manipulation = manip.ToObject<PMPEqdpManipulationJson>();
                    break;
                case "Gmp":
                    obj.Manipulation = manip.ToObject<PMPGmpManipulationJson>();
                    break;
                case "Imc":
                    obj.Manipulation = manip.ToObject<PMPImcManipulationJson>();
                    break;
                case "Est":
                    obj.Manipulation = manip.ToObject<PMPEstManipulationJson>();
                    break;
                case "Rsp":
                    obj.Manipulation = manip.ToObject<PMPRspManipulationJson>();
                    break;
            }

            return obj;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            // Just use normal JSON writer, don't use this converter for JSON writing.
            throw new NotImplementedException("Use standard JSON Converter for writing PMP files, not PMPMetaManipulationConverter.");
        }
    }

    #endregion

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

        public static List<PMPImcManipulationJson> FromFullImcEntry(Imc.FullImcInfo fullInfo, XivDependencyRootInfo root)
        {
            var ret = new List<PMPImcManipulationJson>();
            for(int i = 0; i < fullInfo.SubsetCount; i++)
            {
                var subset = fullInfo.GetEntry(i, root.Slot);
                var entry = FromImcEntry(subset, i, root);
                ret.Add(entry);
            }
            return ret;
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
            ulong value = BitConverter.ToUInt64(entry.GetBytes(), 0);
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
            uint hash = 0;
            hash |= ((uint) rg.Race) << 8;
            hash |= ((uint) rg.Gender);

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


}
