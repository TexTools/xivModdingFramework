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

using Ionic.Zip;
using Newtonsoft.Json;
using SharpDX;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.General;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Materials.FileTypes;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.Interfaces;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;

using Index = xivModdingFramework.SqPack.FileTypes.Index;

namespace xivModdingFramework.Mods.FileTypes
{
    public class TTMP
    {
        public enum EModpackType
        {
            Invalid,
            TtmpOriginal,
            TtmpSimple,
            TtmpWizard,
            TtmpBackup,
            Pmp
        };

        // These file types are forbidden from being included in Modpacks or being imported via modpacks.
        // This is because these file types are re-built from constituent smaller files, and thus importing
        // a complete file would bash the user's current file state in unpredictable ways.
        public static readonly HashSet<string> ForbiddenModTypes = new HashSet<string>()
        {
            ".cmp", ".imc", ".eqdp", ".eqp", ".gmp", ".est"
        };

#if DAWNTRAIL
        internal const string _currentTTMPVersion = "2.0";
#else
        internal const string _currentTTMPVersion = "1.8";
#endif

        internal const char _typeCodeSimple = 's';
        internal const char _typeCodeWizard = 'w';
        internal const char _typeCodeBackup = 'b';

        internal const string _minimumAssembly = "1.3.0.0";

        private readonly DirectoryInfo _modPackDirectory;

        public TTMP(DirectoryInfo modPackDirectory)
        {
            _modPackDirectory = modPackDirectory;
        }

        public static EModpackType GetModpackType(string path)
        {
            if (path.EndsWith(".pmp") || path.EndsWith(".json"))
            {
                return EModpackType.Pmp;
            }
            else if (path.EndsWith(".ttmp"))
            {
                return EModpackType.TtmpOriginal;
            }
            else if (!path.EndsWith(".ttmp2")) {
                return EModpackType.Invalid;
            }

            // Deserialize the mpl from the .ttmp2 file and check the type.
            using (var zf = ZipFile.Read(path))
            {

                var mpl = zf.Entries.First(x => x.FileName.EndsWith(".mpl"));
                using (var streamReader = new StreamReader(mpl.OpenReader()))
                {
                    var mpj = JsonConvert.DeserializeObject<ModPackJson>(streamReader.ReadToEnd());

                    // Sanity Check
                    if (mpj == null)
                    {
                        return EModpackType.Invalid; 
                    }

                    // Version Check
                    Version ver;
                    bool success = Version.TryParse(mpj.MinimumFrameworkVersion, out ver);
                    if (!success)
                    {
                        // Versions from before this variable existed are fine.
                        var frameworkVersion = typeof(XivCache).Assembly.GetName().Version;
                        if (ver > frameworkVersion)
                        {
                            return EModpackType.Invalid;
                        }
                    }

                    // Modpack Type
                    if (mpj.TTMPVersion.EndsWith("w")) {
                        return EModpackType.TtmpWizard;
                    } else if(mpj.TTMPVersion.EndsWith("s")) {
                        return EModpackType.TtmpSimple;
                    } else if (mpj.TTMPVersion.EndsWith("b")) {
                        return EModpackType.TtmpBackup;
                    }
                    return EModpackType.Invalid;
                }
            }
        }

        // Adapts the progress updates from TTMPWriter to one that is compatible with the pre-existing CreateWizardModPack API
        private class WizardProgressWrapper : IProgress<(int current, int total, string message)>
        {
            IProgress<double> _adaptedProgress;

            public WizardProgressWrapper(IProgress<double> adaptedProgress)
            {
                _adaptedProgress = adaptedProgress;
            }

            public void Report((int current, int total, string message) value)
            {
                if (_adaptedProgress != null)
                    _adaptedProgress.Report((double)value.current / (double)value.total);
            }
        }

        /// <summary>
        /// Creates a mod pack that uses a wizard for installation
        /// </summary>
        /// <param name="modPackData">The data that will go into the mod pack</param>
        /// <param name="progress">The progress of the mod pack creation</param>
        /// <returns>The number of pages created for the mod pack</returns>
        public async Task<int> CreateWizardModPack(ModPackData modPackData, IProgress<double> progress, bool overwriteModpack)
        {
            return await Task.Run(async () =>
            {
                using var ttmpWriter = new TTMPWriter(modPackData, _typeCodeWizard);

                // Build the JSON representation of the modpack
                foreach (var modPackPage in modPackData.ModPackPages)
                {
                    var page = ttmpWriter.AddPage(modPackPage);
                    foreach (var modGroup in modPackPage.ModGroups)
                    {
                        var group = ttmpWriter.AddGroup(page, modGroup);
                        foreach (var modOption in modGroup.OptionList)
                        {
                            var option = ttmpWriter.AddOption(group, modOption);
                            foreach (var modOptionMod in modOption.Mods)
                                ttmpWriter.AddFile(option, modOptionMod.Key, modOptionMod.Value);
                        }
                    }
                }

                // Actually executes the work of deduplicating mods and writing the TTMP file
                await ttmpWriter.Write(new WizardProgressWrapper(progress), _modPackDirectory, overwriteModpack);
                return ttmpWriter.PageCount;
            });
        }

        /// <summary>
        /// Creates a mod pack that uses simple installation
        /// </summary>
        /// <param name="modPackData">The data that will go into the mod pack</param>
        /// <param name="gameDirectory">The game directory</param>
        /// <param name="progress">The progress of the mod pack creation</param>
        /// <returns>The number of mods processed for the mod pack</returns>
        public async Task<int> CreateSimpleModPack(SimpleModPackData modPackData, IProgress<(int current, int total, string message)> progress = null, bool overwriteModpack = false, ModTransaction tx = null)
        {

            if(progress == null)
            {
                progress = IOUtil.NoOpImportProgress;
            }
            var fullSize = modPackData.SimpleModDataList.Sum(x => x.ModSize);

            return await Task.Run(async () =>
            {
                if (tx == null)
                {
                    // Readonly TX if we don't have one already.
                    tx = ModTransaction.BeginTransaction(true);
                }

                using var writer = new TTMPWriter(modPackData, _typeCodeSimple);

                var mp = new ModPack
                {
                    Name = modPackData.Name,
                    Author = modPackData.Author,
                    Version = modPackData.Version.ToString(),
                    Url = modPackData.Url
                };

                foreach (var mod in modPackData.SimpleModDataList)
                {
                    var modJson = writer.AddFile(mod, tx);
                    // This field is intended for backup modpacks, but TexTools started writing it in to simple modpacks as well at some point
                    if (modJson != null)
                        modJson.ModPackEntry = mp;
                }

                await writer.Write(progress, _modPackDirectory, overwriteModpack);
                return writer.ModCount;
            });
        }

        /// <summary>
        /// Creates a backup modpack which retains the original modpacks on import
        /// </summary>
        /// <param name="backupModpackData">The data that will go into the mod pack</param>
        /// <param name="gameDirectory">The game directory</param>
        /// <param name="progress">The progress of the mod pack creation</param>
        /// <param name="overwriteModpack">Whether or not to overwrite an existing modpack with the same name</param>
        /// <returns>The number of mods processed for the mod pack</returns>
        public async Task<int> CreateBackupModpack(BackupModPackData backupModpackData, IProgress<(int current, int total, string message)> progress, bool overwriteModpack, ModTransaction tx = null)
        {
            if (tx == null)
            {
                // Readonly TX if we don't have one already.
                tx = ModTransaction.BeginTransaction(true);
            }
            return await Task.Run(async () =>
            {
                using var writer = new TTMPWriter(backupModpackData, _typeCodeBackup);

                foreach (var mod in backupModpackData.ModsToBackup)
                {
                    var modJson = writer.AddFile(mod.SimpleModData, tx);
                    if (modJson != null)
                        modJson.ModPackEntry = mod.ModPack;
                }

                await writer.Write(progress, _modPackDirectory, overwriteModpack);
                return writer.ModCount;
            });
        }

        /// <summary>
        /// Retrieves the deserialized MPL file from a modpack.
        /// </summary>
        /// <param name="modPackDirectory"></param>
        /// <returns></returns>
        public static async Task<ModPackJson> GetModpackList(string path)
        {
            return await Task.Run(() =>
            {
                ModPackJson modPackJson = null;
                using (var zf = ZipFile.Read(path))
                {
                    var mpl = zf.Entries.First(x => x.FileName.EndsWith(".mpl"));
                    using (var streamReader = new StreamReader(mpl.OpenReader()))
                    {
                        var jsonString = streamReader.ReadToEnd();

                        modPackJson = JsonConvert.DeserializeObject<ModPackJson>(jsonString);
                    }
                }

                return modPackJson;
            });
        }
        /// <summary>
        /// Unzips the images from a zip/modpack file, unloading them into a temporary directory and returning the path.
        /// File paths match their modpack path structure.
        /// </summary>
        /// <returns></returns>
        public static async Task<string> GetModpackImages(string path)
        {
            return await Task.Run(() =>
            {
                var tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                using (var zf = ZipFile.Read(path))
                {
                    Directory.CreateDirectory(tempFolder);
                    var images = zf.Entries.Where(x => x.FileName.EndsWith(".png") || x.FileName.EndsWith(".jpg") || x.FileName.EndsWith(".bmp") || x.FileName.EndsWith(".jpeg") || x.FileName.EndsWith(".gif") || x.FileName.StartsWith("images/"));
                    foreach(var image in images)
                    {
                        image.Extract(tempFolder);
                    }
                }

                return tempFolder;
            });
        }

        /// <summary>
        /// LEGACY: Only used by the Create Modpack Wizard in TT, which should be reworked to not rely on these pre-read image files.
        /// Gets the data from a mod pack including images if present
        /// </summary>
        /// <param name="modPackDirectory">The directory of the mod pack</param>
        /// <returns>A tuple containing the mod pack json data and a dictionary of images if any</returns>
        public static Task<(ModPackJson ModPackJson, Dictionary<string, Image> ImageDictionary)> LEGACY_GetModPackJsonData(DirectoryInfo modPackDirectory)
        {
            return Task.Run(() =>
            {
                ModPackJson modPackJson = null;
                var imageDictionary = new Dictionary<string, Image>();

                using (var zf = ZipFile.Read(modPackDirectory.FullName))
                {
                    var images = zf.Entries.Where(x => x.FileName.EndsWith(".png") || x.FileName.StartsWith("images/"));
                    var mpl = zf.Entries.First(x => x.FileName.EndsWith(".mpl"));

                    using (var streamReader = new StreamReader(mpl.OpenReader()))
                    {
                        var jsonString = streamReader.ReadToEnd();

                        modPackJson = JsonConvert.DeserializeObject<ModPackJson>(jsonString);
                    }

                    foreach(var imgEntry in images)
                    {
                        imageDictionary.Add(imgEntry.FileName, Image.Load(imgEntry.OpenReader()));
                    }
                }

                return (modPackJson, imageDictionary);
            });
        }

        public static Task<byte[]> GetModPackData(DirectoryInfo modPackDirectory)
        {
            return Task.Run(() =>
            {
                using (var zf = ZipFile.Read(modPackDirectory.FullName))
                {
                    var mpd = zf.Entries.First(x => x.FileName.EndsWith(".mpd"));

                    using (var ms = new MemoryStream())
                    {
                        mpd.Extract(ms);
                        return ms.ToArray();
                    }
                }
                //using (var stream = new ZipInputStream(modPackDirectory.FullName))
                //{
                //    while (stream.GetNextEntry() is var entry)
                //    {
                //        if (entry.FileName.EndsWith(".mpd"))
                //        {
                //            stream.Read(buffer, 0, modJson.ModSize);
                //            stream.Read(buffer, 0, modJson.ModSize);
                //            stream.Read(buffer, 0, modJson.ModSize);
                //            break;
                //        }
                //    }
                //};
            });
        }

        /// <summary>
        /// Gets the data from first generation mod packs
        /// </summary>
        /// <param name="modPackDirectory">The directory of the mod pack</param>
        /// <returns>A list containing original mod pack json data</returns>
        public Task<List<OriginalModPackJson>> GetOriginalModPackJsonData(DirectoryInfo modPackDirectory)
        {
            return Task.Run(() =>
            {
                var modPackJsonList = new List<OriginalModPackJson>();

                using (var archive = System.IO.Compression.ZipFile.OpenRead(modPackDirectory.FullName))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith(".mpl"))
                        {
                            using (var streamReader = new StreamReader(entry.Open()))
                            {
                                var line = streamReader.ReadLine();
                                if (line.ToLower().Contains("version"))
                                {
                                    // Skip this line and read the next
                                    line = streamReader.ReadLine();
                                    if (line == null) return null;
                                    modPackJsonList.Add(JsonConvert.DeserializeObject<OriginalModPackJson>(line));
                                }
                                else
                                {
                                    modPackJsonList.Add(JsonConvert.DeserializeObject<OriginalModPackJson>(line));
                                }

                                while (streamReader.Peek() >= 0)
                                {
                                    line = streamReader.ReadLine();
                                    modPackJsonList.Add(JsonConvert.DeserializeObject<OriginalModPackJson>(line));
                                }
                            }
                        }
                    }
                }

                return modPackJsonList;
            });
        }

        /// <summary>
        /// Gets the version from a mod pack
        /// </summary>
        /// <param name="packPath">Path to the overlay pack file.</param>
        /// <param name="modifierFunction">Intermediate function that performs any user requested modifications to the overlay pack.  Returns true if the import should be aborted.</param>
        /// <param name="source">Standard import source name</param>
        /// <returns>The version of the mod pack as a string</returns>
        public static string GetVersion(DirectoryInfo modPackDirectory)
        {
            ModPackJson modPackJson = null;

            using (var archive = System.IO.Compression.ZipFile.OpenRead(modPackDirectory.FullName))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith(".mpl"))
                    {
                        using (var streamReader = new StreamReader(entry.Open()))
                        {
                            var jsonString = streamReader.ReadToEnd();
                            try
                            {
                                modPackJson = JsonConvert.DeserializeObject<ModPackJson>(jsonString);
                            } catch(Exception ex)
                            {
                                return "1.0";
                            }
                        }
                    }
                }
            }

            return modPackJson.TTMPVersion;
        }


        /// <summary>
        /// Imports a mod pack asynchronously 
        /// </summary>
        /// <param name="modpackPath">The directory of the mod pack</param>
        /// <param name="modsJson">The list of mods to be imported</param>
        /// <param name="progress">The progress of the import</param>
        /// <param name="GetRootConversionsFunction">Function called part-way through import to resolve rood conversions, if any are desired.  Function takes a List of files, the in-progress modified index and modlist files, and returns a dictionary of conversion data.  If this function throws and OperationCancelledException, the import is cancelled.</param>
        /// <param name="AutoAssignBodyMaterials">Whether models should be scanned for auto material assignment or not.</param>
        /// <returns>The number of total mods imported</returns>
        public static async Task<(List<string> Imported, List<string> NotImported, float Duration)> ImportModPackAsync(
            string modpackPath, List<ModsJson> modsJson, string sourceApplication, IProgress<(int current, int total, string message)> progress = null, 
            Func<HashSet<string>, ModTransaction, Task<Dictionary<XivDependencyRoot, (XivDependencyRoot Root, int Variant)>>>  GetRootConversionsFunction = null,
            bool AutoAssignBodyMaterials = false, bool fixPreDawntrailMods = true)
        {
            if (modsJson == null || modsJson.Count == 0) return (null, null, 0);

            if(progress == null)
            {
                progress = IOUtil.NoOpImportProgress;
            }

            // Get the MPL
            var modpackMpl = await GetModpackList(modpackPath);

            var startTime = DateTime.Now.Ticks;
            long endTime = 0;
            long part1Duration = 0;
            long part2Duration = 0;
            string _tempMPD;

            var dat = new Dat(XivCache.GameInfo.GameDirectory);

            // Disable the cache woker while we're installing multiple items at once, so that we don't process queue items mid-import.
            // (Could result in improper parent file calculations, as the parent files may not be actually imported yet)
            var workerEnabled = XivCache.CacheWorkerEnabled;
            XivCache.CacheWorkerEnabled = false;


            // Loop through all the incoming mod entries, and only take
            // the *LAST* mod json entry for each file path.
            // This keeps us from having to constantly re-query the mod list file, and filters out redundant imports.
            var filePaths = new HashSet<string>();
            var filteredModsJson = new List<ModsJson>(modsJson.Count);
            for(int i = modsJson.Count -1; i >= 0; i--)
            {
                var mj = modsJson[i];
                if(filePaths.Contains(mj.FullPath))
                {
                    // Already have a mod using this path, discard this mod entry.
                    continue;
                }

                // Don't allow importing forbidden mod types.
                if (ForbiddenModTypes.Contains(Path.GetExtension(mj.FullPath))) continue;

                filePaths.Add(mj.FullPath);
                filteredModsJson.Add(mj);
            }

            if(filteredModsJson.Count == 0)
            {
                return (null, null, 0);
            }

            var totalFiles = filePaths.Count;

            try
            {
                await Task.Run(async () =>
                {

                    // Okay, we need to do a few things here.
                    // 0 - Extract the MPD file (tests so far with streaming the ZIP data have all failed)
                    // 1 - Copy all the mod data to the DAT files.
                    // 2 - Update all the indices.
                    // 3 - Update the Modlist
                    // 4 - Expand Metadata
                    // 5 - Queue Cache Updates.

                    // We only need the actual Zip file during the initial copy stage.

                    Dictionary<string, uint> DatOffsets = new Dictionary<string, uint>();
                    Dictionary<XivDataFile, List<string>> FilesPerDf = new Dictionary<XivDataFile, List<string>>();


                    var needsTexFix = DoesTexNeedFixing(new DirectoryInfo(modpackPath));
                    var count = 0;

                    // Begin CORE TRANSACTION
                    using (var tx = ModTransaction.BeginTransaction())
                    {
                        var modList = await tx.GetModList();
                        var originalData = new Dictionary<string, (Mod? mod, long originalOffset)>();

                        // 0 - Extract the MPD file.
                        // It is time to do wild and crazy things...

                        // First, unzip the TTMP into our transaction data store folder.
                        var txSTorePath = tx.UNSAFE_GetTransactionStore();
                        using (var zf = ZipFile.Read(modpackPath))
                        {
                            progress.Report((0, 0, "Unzipping TTMP File to Transaction Data Store..."));
                            var mpd = zf.Entries.First(x => x.FileName.EndsWith(".mpd"));

                            _tempMPD = Path.Combine(txSTorePath, Guid.NewGuid().ToString());

                            using (var fs = new FileStream(_tempMPD, FileMode.Create))
                            {
                                mpd.Extract(fs);
                            }
                        }


                        // Now, we need to rip the offsets, and generate Transaction data store file handles for them.
                        var tempOffsets = new Dictionary<string, long>();
                        count = 0;
                        foreach (var modJson in filteredModsJson)
                        {
                            progress.Report((count, filteredModsJson.Count, "Writing Mod Files..."));
                            var storeInfo = new FileStorageInformation()
                            {
                                StorageType = EFileStorageType.CompressedBlob,
                                RealPath = _tempMPD,
                                RealOffset = modJson.ModOffset,
                                FileSize = modJson.ModSize
                            };

                            // And get an in-system data offset for them...
                            var offset = tx.UNSAFE_AddFileInfo(storeInfo, IOUtil.GetDataFileFromPath(modJson.FullPath));

                            tempOffsets.Add(modJson.FullPath, offset);

                            // And Update the Index to point to the new file.
                            var oldOffset = await tx.Set8xDataOffset(modJson.FullPath, offset);

                            // And Update the Modlist entry.
                            var prevMod = modList.GetMod(modJson.FullPath);

                            // Save this stuff for the root cloner.
                            originalData.Add(modJson.FullPath, (prevMod, oldOffset));

                            Mod mod = new Mod();

                            if(prevMod != null)
                            {
                                oldOffset = prevMod.Value.OriginalOffset8x;
                            }

                            mod.ItemName = modJson.Name;
                            mod.ItemCategory = modJson.Category;
                            mod.FilePath = modJson.FullPath;
                            mod.FileSize = modJson.ModSize;
                            mod.ModOffset8x = offset;
                            mod.OriginalOffset8x = oldOffset;
                            mod.ModPack = modJson.ModPackEntry == null ? "" : modJson.ModPackEntry.Value.Name;

                            modList.AddOrUpdateMod(mod);
                            count++;
                        }

                        // Aaaand, we're done.
                        // The Unzipped MPD file will remain in the transaction store until the transaction is closed or cancelled.
                        // At which point it will be removed.
                        // The transaction commit logic will handle finalizing the write to the DATs if it's needed.


                        // Root Alterations/Item Conversion
                        if (GetRootConversionsFunction != null && filteredModsJson.Count > 0)
                        {
                            Dictionary<XivDependencyRoot, (XivDependencyRoot Root, int Variant)> rootConversions = null;
                            try
                            {
                                progress.Report((count, totalFiles, "Waiting on Destination Item Selection..."));

                                endTime = DateTime.Now.Ticks;

                                // Duration in ms
                                part1Duration = (endTime - startTime) / 10000;


                                rootConversions = await GetRootConversionsFunction(filePaths, tx);
                            }
                            catch (OperationCanceledException ex)
                            {
                                // User cancelled the function or otherwise a critical error happened in the conversion function.
                                totalFiles = 0;
                                throw new OperationCanceledException();
                            }

                            startTime = DateTime.Now.Ticks;

                            if (rootConversions != null && rootConversions.Count > 0)
                            {
                                // Modpack to list conversions under.
                                var modPack = filteredModsJson[0].ModPackEntry;

                                // If we have any roots to move, move them over now.
                                progress.Report((0, 0, "Updating Destination Items..."));

                                tx.ModPack = modPack;
                                var resetFiles = await RootCloner.CloneAndResetRoots(rootConversions, filePaths, tx, originalData, sourceApplication);
                                tx.ModPack = null;
                            }
                        }



                        progress.Report((0, 0, "Committing Transaction..."));
                        await ModTransaction.CommitTransaction(tx);
                    }
                    // end CORE TRANSACTION


                    var totalMetadataEntries = filePaths.Count(x => x.EndsWith(".meta"));
                    count = 0;
                    progress.Report((count, totalMetadataEntries, "Expanding Metadata Files..."));

                    // For Metadata expansion we can use a standard mod transaction
                    using (var tx = ModTransaction.BeginTransaction())
                    {
                        // ModList is updated now.  Time to expand the Metadata files.
                        List<ItemMetadata> metadataEntries = new List<ItemMetadata>();
                        foreach (var file in filePaths)
                        {
                            var ext = Path.GetExtension(file);
                            if (ext == ".meta")
                            {
                                // Load all the files we just wrote and validate them.
                                var metaRaw = await dat.ReadSqPackType2(file, false, tx);
                                var meta = await ItemMetadata.Deserialize(metaRaw);

                                meta.Validate(file);

                                metadataEntries.Add(meta);
                                count++;
                                progress.Report((count, totalMetadataEntries, "Expanding Metadata Files..."));
                            }
                            else if (ext == ".rgsp")
                            {
                                // No batching for these, just write them immediately.
                                await CMP.ApplyRgspFile(file, tx);
                            }
                        }

                        try
                        {
                            await ItemMetadata.ApplyMetadataBatched(metadataEntries, tx);
                        } catch (Exception Ex)
                        {
                            throw new Exception("An error occured when attempting to expand the metadata entries.\n\nError:" + Ex.Message);
                        }

                        if (AutoAssignBodyMaterials)
                        {
                            progress.Report((0, 0, "Scanning for body material corrections..."));

                            // Find all relevant models..
                            var modelFiles = filteredModsJson.Where(x => x.FullPath.EndsWith(".mdl"));
                            var usableModels = modelFiles.Where(x => Mdl.IsAutoAssignableModel(x.FullPath)).ToList();

                            if (usableModels.Any())
                            {
                                var modelCount = usableModels.Count;
                                progress.Report((0, modelCount, "Scanning and updating body models..."));
                                var _mdl = new Models.FileTypes.Mdl(XivCache.GameInfo.GameDirectory);

                                // Loop them to perform heuristic check.
                                var i = 0;
                                foreach (var mdlEntry in usableModels)
                                {
                                    i++;
                                    var file = mdlEntry.FullPath;
                                    progress.Report((i, modelCount, "Scanning and updating body models..."));
                                    var changed = await _mdl.CheckSkinAssignment(mdlEntry.FullPath, tx);
                                }
                            }
                        }

                        // If we have a Pre Dawntrail Modpack, we need to fix things up.
                        if (fixPreDawntrailMods)
                        {
                            if (modpackMpl != null && Int32.Parse(modpackMpl.TTMPVersion.Substring(0, 1)) <= 1)
                            {
                                var modPack = filteredModsJson[0].ModPackEntry;
                                await FixPreDawntrailImports(filePaths, sourceApplication, progress, tx);
                            }
                        }

                        // Commit the metadata transaction
                        await ModTransaction.CommitTransaction(tx);
                    }




                    count = 0;
                    progress.Report((0, 0, "Queuing Cache Updates..."));

                    // Metadata files expanded, last thing is to queue everthing up for the Cache.
                    var files = filePaths.Select(x => x).ToList();
                    try
                    {
                        XivCache.QueueDependencyUpdate(files);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("An error occured while trying to update the Cache.\n\n" + ex.Message + "\n\nThe mods were still imported successfully, however, the Cache should be rebuilt.");
                    }
                });

                progress.Report((totalFiles, totalFiles, "Job Done."));
            } finally
            {
                XivCache.CacheWorkerEnabled = workerEnabled;
            }

            endTime = DateTime.Now.Ticks;

            // Duration in ms
            part2Duration = (endTime - startTime) / 10000;

            float seconds = (part1Duration + part2Duration) / 1000f;

            return (filePaths.ToList(), new List<string>(), seconds);
        }

        /// <summary>
        /// Parse the version out of this modpack to determine whether or not we need
        /// to add 80 to the uncompressed size of the Tex files contained within.
        /// </summary>
        /// <param name="mpd">The path to the modpack.</param>
        /// <returns>True if we must modify tex header uncompressed sizes, false otherwise.</returns>
        private static bool DoesTexNeedFixing(DirectoryInfo mpd) {

	        var ver = GetVersion(mpd);
	        if (string.IsNullOrEmpty(ver))
		        return true;

	        var newVer = ver;

	        var lastChar = ver.Substring(ver.Length - 1)[0];
	        if (char.IsLetter(lastChar))
		        newVer = ver.Substring(0, ver.Length - 1);

	        double.TryParse(newVer, out var verDouble);

	        return verDouble < 1.3;
        }

        /// <summary>
        /// Fix xivModdingFramework TEX quirks.
        /// </summary>
        /// <param name="tex">The TEX data to be fixed up.</param>
        public static void FixupTextoolsTex(byte[] tex) {

	        // Read the uncompressed size from the file
	        var size = BitConverter.ToInt32(tex, 8);
	        var newSize = size + 80;
	        
	        byte[] buffer = BitConverter.GetBytes(newSize);
	        tex[8] = buffer[0];
	        tex[9] = buffer[1];
	        tex[10] = buffer[2];
	        tex[11] = buffer[3];
        }

        public static async Task FixPreDawntrailImports(HashSet<string> filePaths, string source, IProgress<(int current, int total, string message)> progress, ModTransaction tx = null)
        {
#if ENDWALKER
            return;
#endif

            var fixableMdlsRegex = new Regex("chara\\/.*\\.mdl");
            var fixableMdls = filePaths.Where(x => fixableMdlsRegex.Match(x).Success).ToList();

            var fixableMtrlsRegex = new Regex("chara\\/.*\\.mtrl");
            var fixableMtrls = filePaths.Where(x => fixableMtrlsRegex.Match(x).Success).ToList();

            var _mtrl = new Mtrl(XivCache.GameInfo.GameDirectory);
            var _mdl = new Mdl(XivCache.GameInfo.GameDirectory);

            await _mtrl.FixPreDawntrailMaterials(fixableMtrls, source, tx, progress);

            var idx = 0;
            var total = fixableMdls.Count;
            foreach (var path in fixableMdls)
            {
                progress?.Report((idx, total, "Fixing Pre-Dawntrail Models..."));
                await _mdl.FixPreDawntrailMdl(path, source, tx);
            }
        }
    }
}
