using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpDX.D3DCompiler;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;

namespace xivModdingFramework.Mods.FileTypes.PMP
{
    /// <summary>
    /// Class for handling Penumbra Modpacks
    /// </summary>
    public static class PMP
    {
        private static bool _ImportActive = false;
        private static string _Source = null;

        private static async Task<string> ResolvePMPBasePath(string path)
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
                    ZipFile.ExtractToDirectory(path, tempFolder);
                });
                path = tempFolder;
            }
            return path;
        }
        public static async Task<PMPJson> LoadPMP(string path) {
            var gameDir = XivCache.GameInfo.GameDirectory;

            path = await ResolvePMPBasePath(path);

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

            return pmp;
        }

        /// <summary>
        /// Imports a given PMP File, Penumbra Folder, or Penumbra Folder Root JSON into the game files.
        /// Triggers various callbacks during the process to allow displaying data to users or otherwise altering results.
        /// </summary>
        /// <param name="path">System path to .PMP, .JSON, or Folder</param>
        /// <param name="PreviewMeta">Function to preview the modpack.  Return FALSE to cancel import.</param>
        /// <param name="SelectOption">Function select option index.  Return negative value to cancel import.</param>
        /// <returns></returns>
        public static async Task<int> ImportPMP(string path, Func<PMPMetaJson, bool> PreviewMeta = null, Func<PMPGroupJson, int, int> SelectOption = null, string sourceApplication = "Unknown")
        {
            if (_ImportActive)
            {
                throw new Exception("Cannot import multiple Modpacks simultaneously.");
            }

            var imported = 0;
            try
            {
                _ImportActive = true;
                _Source = String.IsNullOrWhiteSpace(sourceApplication) ? "Unknown" : sourceApplication;

                using (var tx = ModTransaction.BeginTransaction())
                {
                    path = await ResolvePMPBasePath(path);
                    var pmp = await LoadPMP(path);

                    var res = PreviewMeta?.Invoke(pmp.Meta);
                    if (res == false)
                    {
                        // User cancelled import.
                        return -1;
                    }

                    if (pmp.Groups == null || pmp.Groups.Count == 0)
                    {
                        // No options, just default.
                        imported += await ImportOption(pmp.DefaultMod, path, tx);
                    }
                    else
                    {
                        foreach (var group in pmp.Groups)
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


                            if (SelectOption != null)
                            {
                                var value = SelectOption.Invoke(group, selected);
                                if (value < 0 || value >= group.Options.Count)
                                {
                                    // User cancelled import.
                                    return -1;
                                }

                                selected = value;
                            }

                            imported += await ImportOption(group.Options[selected], path, tx);
                        }
                    }
                    await ModTransaction.CommitTransaction(tx);
                }
            }
            finally
            {
                _Source = null;
                _ImportActive = false;
            }

            // Successful Import.
            return imported;
        }

        private static async Task<int> ImportOption(PMPOptionJson option, string basePath, ModTransaction tx)
        {
            var imports = 0;
            foreach(var file in option.Files)
            {
                var internalPath = file.Key;
                var externalPath = Path.Combine(basePath, file.Value);

                // Safety checks.
                if (!CanImport(file.Key))
                {
                    continue;
                }
                if (!File.Exists(externalPath))
                {
                    continue;
                }

                try
                {
                    // Import the file...
                    await SmartImport.Import(externalPath, internalPath, _Source, tx);
                    imports++;
                } catch (Exception ex) {
                    // If we failed to import a file, just continue.
                    continue;
                }
            }
            return imports;
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
    }

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

        // Not sure what data format these are yet.
        List<object> Tags;
    }

    public class PMPGroupJson
    {
        public string Name;
        public string Description;
        public int Priority;

        // Enum?
        public string Type;

        // Index
        public int DefaultSettings;
        
        public List<PMPOptionJson> Options = new List<PMPOptionJson>();
    }

    public class PMPOptionJson
    {
        public string Name;
        public string Description;

        public Dictionary<string, string> Files;
        public Dictionary<string, object> FileSwaps;
        public List<PMPMetaManipulationJson> Manipulations;
    }

    public class PMPMetaManipulationJson
    {
        public string Type;
        public Dictionary<string, object> Manipuation;
    }
}
