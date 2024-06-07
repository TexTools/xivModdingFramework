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

using HelixToolkit.SharpDX.Core;
using Ionic.Zip;
using Newtonsoft.Json;
using SharpDX;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
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
using xivModdingFramework.Textures.FileTypes;
using Index = xivModdingFramework.SqPack.FileTypes.Index;

namespace xivModdingFramework.Mods.FileTypes
{
    public class ModPackImportSettings
    {
        /// <summary>
        /// The source application that should be considered as the owner of the mod files.
        /// </summary>
        public string SourceApplication = "Unknown";

        /// <summary>
        /// Should skin auto-assignment be processed for these files?
        /// </summary>
        public bool AutoAssignSkinMaterials = true;

        /// <summary>
        /// Should the Materials/Models be processed for Dawntrail updates?
        /// </summary>
        public bool UpdateEndwalkerFiles = true;

        /// <summary>
        /// Function that should be called to determine root conversions based on the incoming file paths.
        /// Will be called during import once the final file list has been resolved.
        /// </summary>
        public Func<HashSet<string>, ModTransaction, Task<Dictionary<XivDependencyRoot, (XivDependencyRoot Root, int Variant)>>> RootConversionFunction;

        /// <summary>
        /// Progress reporter, if desired.  May not always get reported to depending on the function in question.
        /// </summary>
        public IProgress<(int current, int total, string message)> ProgressReporter = null;
    }

    public static class TTMP
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
        /// Extremely simple function for creating a modpack from a single file and its children.
        /// </summary>
        /// <param name="rootFile"></param>
        /// <param name="includeChildren"></param>
        /// <param name="tx"></param>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static async Task<int> CreateModpackFromFile(string rootFile, string destination, bool includeChildren = true, ModPack? settings = null, ModTransaction tx = null)
        {
            var files = new List<string>();

            if (includeChildren)
            {
                files.AddRange(await XivCache.GetChildrenRecursive(rootFile, tx));
            }
            else
            {
                files.Add(rootFile);
            }

            var mp = new SimpleModPackData()
            {
                Name = settings != null ? settings.Value.Name : Path.GetFileNameWithoutExtension(rootFile),
                Author = settings != null ? settings.Value.Author : "Unknown", 
                Version = settings != null ? new Version(settings.Value.Version) : new Version("1.0"),
                Url = settings != null ? settings.Value.Url : "",
                Description = "A simple modpack export created from the file: " + rootFile,
                SimpleModDataList = new List<SimpleModData>(),
            };

            var root = await XivCache.GetFirstRoot(rootFile);
            
            var itemName = "Unknown Item";
            var itemCategory = "Unknown Category";

            if(root != null)
            {
                var item = root.GetFirstItem();
                itemName = item.Name;
                itemCategory = item.SecondaryCategory;
            }
            
            if(tx == null)
            {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginTransaction();
            }

            foreach(var file in files)
            {
                var md = new SimpleModData()
                {
                    Name = itemName,
                    Category = itemCategory,
                    DatFile = IOUtil.GetDataFileFromPath(rootFile).GetFileName(),
                    FullPath = file,
                    ModOffset = await tx.Get8xDataOffset(file),
                    // Size is plucked by the TTMP creator, we don't need to set them here.
                };
                mp.SimpleModDataList.Add(md);
            }

            return await TTMP.CreateSimpleModPack(mp, destination, null, true, tx);
        }

        /// <summary>
        /// Creates a mod pack that uses a wizard for installation
        /// </summary>
        /// <param name="modPackData">The data that will go into the mod pack</param>
        /// <param name="progress">The progress of the mod pack creation</param>
        /// <returns>The number of pages created for the mod pack</returns>
        public static async Task<int> CreateWizardModPack(ModPackData modPackData, string destination, IProgress<double> progress, bool overwriteModpack)
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
                await ttmpWriter.Write(new WizardProgressWrapper(progress), destination, overwriteModpack);
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
        public static async Task<int> CreateSimpleModPack(SimpleModPackData modPackData, string destination, IProgress<(int current, int total, string message)> progress = null, bool overwriteModpack = false, ModTransaction tx = null)
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
                    tx = ModTransaction.BeginTransaction();
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

                await writer.Write(progress, destination, overwriteModpack);
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
        public static async Task<int> CreateBackupModpack(BackupModPackData backupModpackData, string destination, IProgress<(int current, int total, string message)> progress, bool overwriteModpack, ModTransaction tx = null)
        {
            if (tx == null)
            {
                // Readonly TX if we don't have one already.
                tx = ModTransaction.BeginTransaction();
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

                await writer.Write(progress, destination, overwriteModpack);
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
            if(Path.GetExtension(path).ToLower() == ".ttmp")
            {
                return await GetLegacyModpackMpl(path);
            }

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
        /// Gets the data from first generation mod packs
        /// </summary>
        /// <param name="modPackDirectory">The directory of the mod pack</param>
        /// <returns>A list containing original mod pack json data</returns>
        public static async Task<ModPackJson> GetLegacyModpackMpl(string modpackPath)
        {
            if (!modpackPath.ToLower().EndsWith(".ttmp"))
            {
                throw new InvalidDataException("Legacy modpack must be .ttmp extension");
            }

            var originalJson = await Task.Run(() =>
            {
                var modPackJsonList = new List<OriginalModPackJson>();

                using (var archive = System.IO.Compression.ZipFile.OpenRead(modpackPath))
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

            var mpj = new ModPackJson()
            {
                Author = "Unknown",
                Description = "",
                Name = Path.GetFileNameWithoutExtension(modpackPath),
                Version = "1.0",
                TTMPVersion = "0.1s",
                Url = "",
                SimpleModsList = new List<ModsJson>()
            };

            foreach (var entry in originalJson)
            {
                var mj = new ModsJson();
                mj.FullPath = entry.FullPath;
                mj.DatFile = entry.DatFile;
                mj.Name = entry.Name;
                mj.Category = entry.Category;
                mj.ModSize = entry.ModSize;
                mj.ModOffset = entry.ModOffset;
                mpj.SimpleModsList.Add(mj);
            }

            return mpj;
        }


        /// <summary>
        /// Basic TTMP Unzip.
        /// Returns the folder the data was unzipped to and the MPL
        /// </summary>
        /// <param name="path"></param>
        /// <param name="targetPath"></param>
        /// <param name="mpdName"></param>
        /// <returns></returns>
        public static async Task<(ModPackJson Mpl, string UnzipFolder)> UnzipTtmp(string path, string targetPath = null, string mpdName = null)
        {
            return await Task.Run(async () =>
            {
                if (targetPath == null)
                {
                    targetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                }

                var mpl = await GetModpackList(path);

                Directory.CreateDirectory(targetPath);

                if (mpdName == null)
                {
                    using (var zf = ZipFile.Read(path))
                    {
                        zf.ExtractAll(targetPath);
                    }
                }
                else
                {
                    using (var zf = ZipFile.Read(path))
                    {
                        foreach (var f in zf.Entries)
                        {
                            if (f.FileName.ToLower().EndsWith(".mpd"))
                            {
                                using var fs = File.OpenWrite(Path.Combine(targetPath, mpdName));
                                f.Extract(fs);
                                break;
                            }
                        }
                    }
                }

                return (mpl, targetPath);
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

            if (modPackDirectory.FullName.ToLower().EndsWith(".ttmp"))
            {
                return "0.1s";
            }

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
            string modpackPath, List<ModsJson> modsJson, ModPackImportSettings settings = null, ModTransaction tx = null)
        {
            if (modsJson == null || modsJson.Count == 0) return (null, null, 0);
            if(settings == null)
            {
                settings = new ModPackImportSettings();
            }

            var progress = settings.ProgressReporter;
            var GetRootConversionsFunction = settings.RootConversionFunction;

            var boiler = TxBoiler.BeginWrite(ref tx, true);
            Dictionary<string, TxFileState> originalStates = new Dictionary<string, TxFileState>();
            try
            {

                if (progress == null)
                {
                    progress = IOUtil.NoOpImportProgress;
                }

                // Get the MPL
                var modpackMpl = await GetModpackList(modpackPath);

                var startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                long endTime = 0;
                long part1Duration = 0;
                long p2Start = 0;
                string _tempMPD;

                // Loop through all the incoming mod entries, and only take
                // the *LAST* mod json entry for each file path.
                // This keeps us from having to constantly re-query the mod list file, and filters out redundant imports.
                var filePaths = new HashSet<string>();
                var filteredModsJson = new List<ModsJson>(modsJson.Count);
                for (int i = modsJson.Count - 1; i >= 0; i--)
                {
                    var mj = modsJson[i];
                    if (filePaths.Contains(mj.FullPath))
                    {
                        // Already have a mod using this path, discard this mod entry.
                        continue;
                    }

                    // Don't allow importing forbidden mod types.
                    if (ForbiddenModTypes.Contains(Path.GetExtension(mj.FullPath))) continue;

                    filePaths.Add(mj.FullPath);
                    filteredModsJson.Add(mj);
                }

                if (filteredModsJson.Count == 0)
                {
                    return (null, null, 0);
                }

                var totalFiles = filePaths.Count;
                bool cancelled = false;
                long rootDuration = 0;

                await Task.Run(async () =>
                {

                    // We only need the actual Zip file during the initial copy stage.
                    Dictionary<string, uint> DatOffsets = new Dictionary<string, uint>();
                    Dictionary<XivDataFile, List<string>> FilesPerDf = new Dictionary<XivDataFile, List<string>>();


                    var needsTexFix = DoesModpackNeedTexFix(modpackMpl);
                    var count = 0;
                    var modList = await tx.GetModList();

                    // Extract the MPD file...
                    // It is time to do wild and crazy things...

                    // First, unzip the TTMP into our transaction data store folder.
                    var txSTorePath = tx.UNSAFE_GetTransactionStore();

                    var mpdName = Guid.NewGuid().ToString() + ".mpd";
                    await UnzipTtmp(modpackPath, txSTorePath, mpdName);
                    _tempMPD = Path.Combine(txSTorePath, mpdName);

                    // Now, we need to rip the offsets, and generate Transaction data store file handles for them.
                    var tempOffsets = new Dictionary<string, long>();
                    count = 0;

                    var seenModPacks = new HashSet<string>();
                    foreach (var modJson in filteredModsJson)
                    {
                        progress.Report((count, filteredModsJson.Count, "Writing Mod Files..."));

                        // Save the original state for the root cloner.
                        originalStates.Add(modJson.FullPath, await tx.SaveFileState(modJson.FullPath));

                        var storeInfo = new FileStorageInformation()
                        {
                            StorageType = EFileStorageType.CompressedBlob,
                            RealPath = _tempMPD,
                            RealOffset = modJson.ModOffset,
                            FileSize = modJson.ModSize
                        };

                        if (needsTexFix && modJson.FullPath.EndsWith(".tex"))
                        {
                            try
                            {
                                // Have to fix old busted textures.
                                storeInfo = await FixOldTexData(storeInfo);
                            } catch(Exception ex)
                            {
                                // File is significantly/unreadably broken.
                                Trace.WriteLine(ex);
                                continue;
                            }
                        }


                        // And get an in-system data offset for them...
                        var offset = tx.UNSAFE_AddFileInfo(storeInfo, IOUtil.GetDataFileFromPath(modJson.FullPath));

                        tempOffsets.Add(modJson.FullPath, offset);

                        // And Update the Index to point to the new file.
                        var oldOffset = await tx.Set8xDataOffset(modJson.FullPath, offset);

                        // And Update the Modlist entry.
                        var prevMod = modList.GetMod(modJson.FullPath);

                        Mod mod = new Mod();

                        if (prevMod != null)
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
                        mod.SourceApplication = settings.SourceApplication;

                        modList.AddOrUpdateMod(mod);

                        // Add the modpack if we haven't already.
                        if (modJson.ModPackEntry != null && !seenModPacks.Contains(modJson.ModPackEntry.Value.Name))
                        {
                            seenModPacks.Add(modJson.ModPackEntry.Value.Name);
                            modList.AddOrUpdateModpack(modJson.ModPackEntry.Value);
                        }

                        // Expand metadata files if needed.
                        var ext = Path.GetExtension(mod.FilePath);
                        if (ext == ".meta")
                        {
                            await ItemMetadata.ApplyMetadata(mod.FilePath, false, tx);
                        }
                        else if (ext == ".rgsp")
                        {
                            await CMP.ApplyRgspFile(mod.FilePath, false, tx);
                        }

                        count++;
                    }

                    // Aaaand, we're done.
                    // The Unzipped MPD file will remain in the transaction store until the transaction is closed or cancelled.
                    // At which point it will be removed.
                    // The transaction commit logic will handle finalizing the write to the DATs if it's needed.

                    // Everything from this point is basically user-opt-in tweaks to incoming data.

                    part1Duration = DateTimeOffset.Now.ToUnixTimeMilliseconds() - startTime;

                    // Root Alterations/Item Conversion
                    if (GetRootConversionsFunction != null && filteredModsJson.Count > 0)
                    {
                        // Modpack to list conversions under.
                        var modPack = filteredModsJson[0].ModPackEntry;
                        rootDuration = await HandleRootConversion(filePaths, originalStates, tx, settings, modPack);

                        // User cancelled the import.
                        if(rootDuration < 0)
                        {
                            cancelled = true;
                            return;
                        }
                    }

                    p2Start = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                    // Auto assign body materials
                    if (settings.AutoAssignSkinMaterials)
                    {
                        progress.Report((0, 0, "Scanning for body material corrections..."));

                        // Find all relevant models..
                        var modelFiles = filteredModsJson.Where(x => x.FullPath.EndsWith(".mdl"));
                        var usableModels = modelFiles.Where(x => Mdl.IsAutoAssignableModel(x.FullPath)).ToList();

                        if (usableModels.Any())
                        {
                            var modelCount = usableModels.Count;
                            progress.Report((0, modelCount, "Scanning and updating body models..."));

                            // Loop them to perform heuristic check.
                            var i = 0;
                            foreach (var mdlEntry in usableModels)
                            {
                                i++;
                                var file = mdlEntry.FullPath;
                                progress.Report((i, modelCount, "Scanning and updating body models..."));
                                var changed = await Mdl.CheckSkinAssignment(mdlEntry.FullPath, tx);
                            }
                        }
                    }

                    // Fix Pre-Dawntrail files.
                    if (settings.UpdateEndwalkerFiles)
                    {
                        if (modpackMpl != null && Int32.Parse(modpackMpl.TTMPVersion.Substring(0, 1)) <= 1)
                        {
                            var modPack = filteredModsJson[0].ModPackEntry;
                            await UpdateEndwalkerFiles(filePaths, settings.SourceApplication, originalStates, progress, tx);
                        }
                    }

                    count = 0;
                    progress.Report((0, 0, "Queuing Cache Updates..."));

                    // Just need to queue the files up for the cache worker.
                    var files = filePaths.Select(x => x).ToList();
                    XivCache.QueueDependencyUpdate(files);
                });

                if(cancelled)
                {
                    await boiler.Cancel(true);
                    return (null, null, -1);
                }

                if (boiler.OwnTx)
                {
                    progress.Report((0, 0, "Committing Transaction..."));
                }

                await boiler.Commit();

                progress.Report((totalFiles, totalFiles, "Job Done."));

                endTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                // Duration in ms
                var part2Duration = (endTime - p2Start);

                float seconds = (part1Duration + rootDuration + part2Duration) / 1000f;

                return (filePaths.ToList(), new List<string>(), seconds);
            } catch(Exception ex)
            {
                await boiler.Catch();
                throw;
            }
        }

        /// <summary>
        /// Parse the version out of this modpack to determine whether or not we need
        /// to recalculate and correct the compressed type 4 file sizes.
        /// </summary>
        /// <param name="modpackPath">The path to the modpack.</param>
        /// <returns>True if we must modify tex header uncompressed sizes, false otherwise.</returns>
        public static bool DoesModpackNeedTexFix(DirectoryInfo modpackPath) {

	        var ver = GetVersion(modpackPath);

            return DoesModpackNeedTexFix(ver);
        }
        public static bool DoesModpackNeedTexFix(ModPackJson mpl)
        {
            return DoesModpackNeedTexFix(mpl.Version);
        }
        public static bool DoesModpackNeedTexFix(string version)
        {
            if (string.IsNullOrEmpty(version))
                return true;

            Int32.TryParse(version.Substring(0, 1), out var v);
            return v < 2;
        }


        public static async Task UpdateEndwalkerFiles(IEnumerable<string> filePaths, string source, Dictionary<string, TxFileState> states, IProgress<(int current, int total, string message)> progress, ModTransaction tx = null)
        {
#if ENDWALKER
            return;
#endif

            var fixableMdlsRegex = new Regex("chara\\/.*\\.mdl");
            var fixableMdls = filePaths.Where(x => fixableMdlsRegex.Match(x).Success).ToList();

            var fixableMtrlsRegex = new Regex("chara\\/.*\\.mtrl");
            var fixableMtrls = filePaths.Where(x => fixableMtrlsRegex.Match(x).Success).ToList();

            await Mtrl.UpdateEndwalkerMaterials(fixableMtrls, source, tx, progress);

            var idx = 0;
            var total = fixableMdls.Count;
            foreach (var path in fixableMdls)
            {
                progress?.Report((idx, total, "Updating Endwalker Models..."));
                idx++;
                await Mdl.UpdateEndwalkerModels(path, source, tx);
            }

            progress?.Report((0, total, "Updating Endwalker partial Hair Mods..."));
            await Mtrl.CheckImportForOldHairJank(filePaths.ToList(), source, tx);
        }

        /// <summary>
        /// Handles passing control to the application supplied root conversion function, then altering any inbound mod roots as needed.
        /// Returns -1 if the user cancelled the process.
        /// </summary>
        /// <param name="filePaths"></param>
        /// <param name="originalStates"></param>
        /// <param name="tx"></param>
        /// <param name="modPack"></param>
        /// <param name="sourceApplication"></param>
        /// <param name="GetRootConversionsFunction"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        internal static async Task<long> HandleRootConversion(
            HashSet<string> filePaths,
            Dictionary<string, TxFileState> originalStates,
            ModTransaction tx,
            ModPackImportSettings settings,
            ModPack? modPack = null)
        {

            if (settings.RootConversionFunction == null)
            {
                return 0;
            }

            var progress = settings.ProgressReporter;
            if(progress == null)
            {
                progress = IOUtil.NoOpImportProgress;
            }

            Dictionary<XivDependencyRoot, (XivDependencyRoot Root, int Variant)> rootConversions = null;
            try
            {

                progress.Report((0, 0, "Waiting on Destination Item Selection..."));

                rootConversions = await settings.RootConversionFunction(filePaths, tx);
            }
            catch (OperationCanceledException ex)
            {
                return -1;
            }

            var startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (rootConversions != null && rootConversions.Count > 0)
            {
                // If we have any roots to move, move them over now.
                progress.Report((0, 0, "Updating Destination Items..."));

                tx.ModPack = modPack;
                await RootCloner.CloneAndResetRoots(rootConversions, filePaths, tx, originalStates, settings.SourceApplication, progress);
                tx.ModPack = null;
            }
            var endTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            return endTime - startTime;
        }


        public static async Task<(ModPack ModPack, string Description)> GetModpackInfo(string modpackFile)
        {
            if (!File.Exists(modpackFile))
            {
                throw new FileNotFoundException("Modpack does not exist: " + modpackFile);
            }

            var modpack = new ModPack();
            var description = "";
            if (modpackFile.EndsWith(".ttmp2") || modpackFile.EndsWith(".ttmp")) {
                var mpl = await GetModpackList(modpackFile);
                modpack.Author = mpl.Author;
                modpack.Name = mpl.Name;
                modpack.Url = mpl.Url;
                modpack.Version = mpl.Version;
                description = mpl.Description;

            } else if(modpackFile.EndsWith(".pmp") || modpackFile.EndsWith(".json") || modpackFile.EndsWith("/"))
            {
                var pmpAndPath = await PMP.PMP.LoadPMP(modpackFile, true);
                var pmp = pmpAndPath.pmp;

                modpack.Name = pmp.Meta.Name;
                modpack.Author = pmp.Meta.Author;
                modpack.Url = pmp.Meta.Website;
                modpack.Version = pmp.Meta.Version;
                description = pmp.Meta.Description;
            }
            return (modpack, description);
        }

        /// <summary>
        /// Attempts to perform the most basic merge of file data into the system.
        /// Takes a collection of file storage informations, and applies them to the associated internal file paths.
        /// </summary>
        /// <param name="modpackPath"></param>
        /// <returns></returns>
        public static async Task<bool> ImportFiles(Dictionary<string, FileStorageInformation> files, ModPack? modpack = null, ModPackImportSettings settings = null, ModTransaction tx = null)
        {
            if(settings == null)
            {
                settings = new ModPackImportSettings();
            }


            var originalStates = new Dictionary<string, TxFileState>();
            var boiler = TxBoiler.BeginWrite(ref tx, true);
            try
            {
                tx.ModPack = modpack;

                var mpName = modpack == null ? "" : modpack.Value.Name;
                var ml = await tx.GetModList();
                if (modpack != null)
                {
                    ml.AddOrUpdateModpack(modpack.Value);
                }


                var i = 0;
                foreach(var kv in files)
                {

                    settings.ProgressReporter?.Report((i, files.Count, "Writing Mod Files..."));
                    i++;
                    var internalPath = kv.Key;
                    var fileInfo = kv.Value;

                    originalStates.Add(internalPath, await tx.SaveFileState(internalPath));

                    var df = IOUtil.GetDataFileFromPath(internalPath);

                    // Inject file info to data store.
                    var offset = tx.UNSAFE_AddFileInfo(fileInfo, df);

                    // Inject Index offset pointer
                    var ogOffset = await tx.Set8xDataOffset(internalPath, offset);

                    // Get compressed file size.
                    var compSize = fileInfo.FileSize;
                    if (fileInfo.StorageType == EFileStorageType.UncompressedIndividual || fileInfo.StorageType == EFileStorageType.UncompressedBlob) {
                        compSize = await tx.GetCompressedFileSize(df, offset);
                    } else if(compSize == 0 && fileInfo.StorageType == EFileStorageType.CompressedIndividual)
                    {
                        compSize = (int) new FileInfo(fileInfo.RealPath).Length;
                    }

                    // Resolve name and category for modlist.
                    var root = await XivCache.GetFirstRoot(internalPath);
                    var itemName = "Unknown";
                    var itemCategory = "Unknown";
                    if (root != null)
                    {
                        var im = root.GetFirstItem();
                        if(im != null)
                        {
                            itemName = im.Name;
                            itemCategory = im.SecondaryCategory;
                        }
                    }

                    // Create and inject mod entry.
                    Mod mod = new Mod()
                    {
                        FilePath = internalPath,
                        ItemName = itemName,
                        ItemCategory = itemCategory,
                        FileSize = compSize,
                        ModPack = mpName,
                        ModOffset8x = offset,
                        SourceApplication = settings.SourceApplication,
                        OriginalOffset8x = ogOffset,
                    };
                    await tx.AddOrUpdateMod(mod);

                    // Expand metadata entries.
                    if (internalPath.EndsWith(".meta"))
                    {
                        await ItemMetadata.ApplyMetadata(internalPath, false, tx);
                    } else if (internalPath.EndsWith(".rgsp"))
                    {
                        await CMP.ApplyRgspFile(internalPath, false, tx);
                    }
                }

                var paths = new HashSet<string>(files.Keys);

                var res = await HandleRootConversion(paths, originalStates, tx, settings, modpack);
                if (res < 0)
                {
                    await boiler.Cancel(true);
                    return false;
                }


                if (settings.AutoAssignSkinMaterials)
                {
                    // Find all relevant models..
                    var modelFiles = paths.Where(x => x.EndsWith(".mdl"));
                    var usableModels = modelFiles.Where(x => Mdl.IsAutoAssignableModel(x)).ToList();

                    if (usableModels.Any())
                    {
                        var modelCount = usableModels.Count;
                        settings.ProgressReporter?.Report((0, modelCount, "Scanning and updating body models..."));

                        // Loop them to perform heuristic check.
                        i = 0;
                        foreach (var mdlEntry in usableModels)
                        {
                            i++;
                            settings.ProgressReporter?.Report((i, modelCount, "Scanning and updating body models..."));
                            var changed = await Mdl.CheckSkinAssignment(mdlEntry, tx);
                        }
                    }
                }

                if (settings.UpdateEndwalkerFiles)
                {
                    await UpdateEndwalkerFiles(paths, settings.SourceApplication, originalStates, settings.ProgressReporter, tx);
                }


                if (boiler.OwnTx)
                {
                    settings.ProgressReporter?.Report((0, 0, "Committing Transaction..."));
                }
                await boiler.Commit();
                return true;
            }
            catch
            {
                await boiler.Catch();
                throw;
            }
        }


        /// <summary>
        /// Takes an external modpack file, and reads it to see if it can be compiled into a single simple file list.
        /// If it can, it will unzip the modpack (if necessary), and convert it into a dictionary of
        /// [Internal file path] => [File storage information]
        /// 
        /// Returns NULL if the modpack could not be read for any reason 
        ///     or the modpack could not be converted into a simple file list. (Ex. Has multiple options)
        ///     
        /// If (includeData) is false, blank file storage information will be returned instead.
        ///     (May include compressed file size per file for TTMPs)
        ///     
        /// NOTE: A TX is not actually needed for operation here, but if one is supplied the temp files will be unzipped
        /// into the Transaction's temporary file store.  If one is not supplied, care should be taken to ensure that the files
        /// are properly deleted after usage via looping the FileStorageInformation.RealPath values.
        /// 
        /// </summary>
        /// <param name="modpackPath"></param>
        /// <returns></returns>
        public static async Task<Dictionary<string, FileStorageInformation>> ModPackToSimpleFileList(string modpackPath, bool includeData = true, ModTransaction tx = null)
        {
            if (!File.Exists(modpackPath) && !Directory.Exists(modpackPath))
                return null;

            if (modpackPath.EndsWith(".pmp") || modpackPath.EndsWith(".json") || modpackPath.EndsWith("/"))
            {
                return await PMP.PMP.UnpackPMP(modpackPath, includeData, tx);
            }
            if (!modpackPath.EndsWith(".ttmp2") && !modpackPath.EndsWith(".ttmp"))
            {
                throw new InvalidDataException("File must be .TTMP2 or .PMP");
            }


            // TTMP Path
            var modpackMpl = await GetModpackList(modpackPath);

            if(modpackMpl.SimpleModsList != null && modpackMpl.SimpleModsList.Count > 0 && !modpackMpl.Version.EndsWith("b"))
            {
                return await UnpackSimpleModlist(modpackPath, includeData, tx);
            } else if(modpackMpl.ModPackPages != null && modpackMpl.ModPackPages.Count > 0)
            {
                return await UnpackWizardModlist(modpackPath, includeData, tx);
            }
            else
            {
                // Empty Modpack
                return null;
            }
        }
        private static async Task<Dictionary<string, FileStorageInformation>> UnpackSimpleModlist(string modpackPath, bool includeData = true, ModTransaction tx = null)
        {
            // Wrapped to task since we're going to be potentially unzipping a large file.
            return await Task.Run(async () =>
            {

                var _tempMPD = "";
                ModPackJson mpl;
                if (includeData)
                {
                    string tempFolder;
                    if (tx != null)
                    {
                        // Unzip to TX store if we have one.
                        tempFolder = tx.UNSAFE_GetTransactionStore();
                    }
                    else
                    {
                        tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                    }

                    // First, unzip the TTMP into our transaction data store folder.
                    var mpdName = Guid.NewGuid().ToString() + ".mpd";
                    var res = await UnzipTtmp(modpackPath, tempFolder, mpdName);
                    mpl = res.Mpl;
                    _tempMPD = Path.Combine(tempFolder, mpdName);
                } else
                {
                    mpl = await GetModpackList(modpackPath);
                }

                var needsTexFix = DoesModpackNeedTexFix(mpl);

                return await MakeFileStorageInformationDictionary(_tempMPD, mpl.SimpleModsList, needsTexFix, includeData);
            });
        }
        private static async Task<Dictionary<string, FileStorageInformation>> UnpackWizardModlist(string modpackPath, bool includeData = true, ModTransaction tx = null)
        {
            var ret = new Dictionary<string, FileStorageInformation>();
            

            return await Task.Run(async () =>
            {

                var _tempMPD = "";

                ModPackJson mpl;
                if (includeData)
                {
                    string tempFolder;
                    if (tx != null)
                    {
                        // Unzip to TX store if we have one.
                        tempFolder = tx.UNSAFE_GetTransactionStore();
                    }
                    else
                    {
                        tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                    }

                    var mpdName = Guid.NewGuid().ToString() + ".mpd";
                    var res = await UnzipTtmp(modpackPath, tempFolder, mpdName);
                    _tempMPD = Path.Combine(res.UnzipFolder, mpdName);
                    mpl = res.Mpl;
                }
                else
                {
                    mpl = await GetModpackList(modpackPath);
                }

                if (mpl.ModPackPages.Count > 1)
                {
                    return null;
                }

                if (mpl.ModPackPages[0].ModGroups.Count > 1)
                {
                    return null;
                }

                if (mpl.ModPackPages[0].ModGroups[0].OptionList.Count > 1)
                {
                    return null;
                }

                var option = mpl.ModPackPages[0].ModGroups[0].OptionList[0];

                var needsTexFix = DoesModpackNeedTexFix(mpl);
                return await MakeFileStorageInformationDictionary(_tempMPD, option.ModsJsons, includeData, needsTexFix);

            });
        }

        private static async Task<Dictionary<string, FileStorageInformation>> MakeFileStorageInformationDictionary(string mpdPath, List<ModsJson> mods, bool needsTexFix, bool includeData = true)
        {
            var ret = new Dictionary<string, FileStorageInformation>();
            foreach (var file in mods)
            {


                if (!includeData)
                {
                    if (ret.ContainsKey(file.FullPath))
                    {
                        continue;
                    }

                    ret.Add(file.FullPath, new FileStorageInformation() { 
                        FileSize = file.ModSize
                    });
                    continue;
                }

                var storeInfo = new FileStorageInformation()
                {
                    StorageType = EFileStorageType.CompressedBlob,
                    RealPath = mpdPath,
                    RealOffset = file.ModOffset,
                    FileSize = file.ModSize
                };


                // Ancient bug issues....
                if (needsTexFix && file.FullPath.EndsWith(".tex") && includeData)
                {
                    try
                    {
                        // Have to fix old busted textures.
                        storeInfo = await FixOldTexData(storeInfo);
                    }
                    catch
                    {
                        // Hmm... What should we do about this?
                        // Skip the file?
                        continue;
                    }
                }

                if (ret.ContainsKey(file.FullPath))
                {
                    // Last addition gets priority?
                    // This is a very undefined case, but somehow people made simple modpacks with 2 different files for the same path.
                    ret.Remove(file.FullPath);
                }

                ret.Add(file.FullPath, storeInfo);
            }
            return ret;
        }


        /// <summary>
        /// Fixes up inconsistencies and errors with old TexTools texture files.
        /// In particular, their compressed and uncompressed sizes are wrong.
        /// </summary>
        /// <param name="info"></param>
        public static async Task<FileStorageInformation> FixOldTexData(FileStorageInformation info)
        {
            if(!info.IsCompressed)
            {
                // No issues if this is already being stored in unpacked format.
                return info;
            }

            // There are three possible issues.
            // 1. Uncompressed file size is wrong, which needs to be fixed in the file storage info.
            // 2. The Uncompressed file size is wrong.  This can only be validated by unzipping the file.
            // 3. The block sizes are incorrect.  This can only be fixed by unzipping and rewriting the blocks.

            var data = await TransactionDataHandler.GetUncompressedFile(info);
            var recomp = await Tex.CompressTexFile(data);

            var originalSize = info.FileSize;
            info.FileSize = recomp.Length;

            var fpath = info.RealPath;
            var offset = info.RealOffset;
            if(info.FileSize > originalSize && info.IsBlob) {
                // We can't do an in-place write here b/c we might bash something else's data.
                var baseFolder = Path.GetDirectoryName(info.RealPath);
                fpath = Path.Combine(baseFolder, Guid.NewGuid().ToString());
                offset = 0;
                info.RealPath = fpath;
                info.RealOffset = 0;
                info.StorageType = EFileStorageType.CompressedIndividual;
            }

            using (var fs = File.OpenWrite(fpath))
            {
                fs.Seek(offset, SeekOrigin.Begin);
                fs.Write(recomp, 0, recomp.Length);
            }

            return info;
        }
    }
}
