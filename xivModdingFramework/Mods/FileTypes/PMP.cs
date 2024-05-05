using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpDX.D3DCompiler;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.DataContainers;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Mods.Interfaces;
using xivModdingFramework.Variants.DataContainers;

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

            return pmp;
        }

        /// <summary>
        /// Imports a given PMP File, Penumbra Folder, or Penumbra Folder Root JSON into the game files.
        /// Triggers various callbacks during the process to allow displaying data to users or otherwise altering results.
        /// </summary>
        /// <param name="path">System path to .PMP, .JSON, or Folder</param>
        /// <param name="SelectOptions">Function called to select the desired options from the PMP's available ones.  Should set SelectedOption on each option parameter, if desired.  Return false to cancel import.</param>
        /// <returns></returns>
        public static async Task<int> ImportPMP(string path, Func<PMPJson, bool> SelectOptions = null, string sourceApplication = "Unknown")
        {
            if (_ImportActive)
            {
                throw new Exception("Cannot import multiple Modpacks simultaneously.");
            }

            var files = new HashSet<string>();
            try
            {
                _ImportActive = true;
                _Source = String.IsNullOrWhiteSpace(sourceApplication) ? "Unknown" : sourceApplication;

                path = await ResolvePMPBasePath(path);
                var pmp = await LoadPMP(path);

                using (var tx = ModTransaction.BeginTransaction())
                {

                    var res = SelectOptions?.Invoke(pmp);
                    if (res == false)
                    {
                        // User cancelled import.
                        return -1;
                    }

                    if (pmp.Groups == null || pmp.Groups.Count == 0)
                    {
                        // No options, just default.
                        files.UnionWith(await ImportOption(pmp.DefaultMod, path, tx));
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

                            // If the user selected custom settings, use those.
                            if(group.SelectedSettings >= 0)
                            {
                                selected = group.SelectedSettings;
                            }

                            files.UnionWith(await ImportOption(group.Options[selected], path, tx));
                        }
                    }
                    await ModTransaction.CommitTransaction(tx);
                }

                // Pre-Dawntrail Modpack.
                if (pmp.Meta.FileVersion <= 3)
                {
                    // Transaction for fixing up our files.
                    using (var tx = ModTransaction.BeginTransaction())
                    {
                        await TTMP.FixPreDawntrailImports(files, true, sourceApplication, null, tx);
                        await ModTransaction.CommitTransaction(tx);
                    }
                }
            }
            finally
            {
                _Source = null;
                _ImportActive = false;
            }

            // Successful Import.
            return files.Count;
        }

        private static async Task<HashSet<string>> ImportOption(PMPOptionJson option, string basePath, ModTransaction tx)
        {
            var imported = new HashSet<string>();
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
                    imported.Add(internalPath);
                } catch (Exception ex) {
                    // If we failed to import a file, just continue.
                    continue;
                }
            }
            return imported;
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
        public Dictionary<string, object> FileSwaps;
        public List<PMPMetaManipulationJson> Manipulations;
    }

    public class PMPMetaManipulationJson
    {
        public string Type;
        public object Manipulation;
    }

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

    public class PMPEstManipulationJson
    {
        public uint Entry = 0;
        public string Gender;
        public string Race;
        public uint SetId;
        public string Slot;
        
        public ExtraSkeletonEntry ToExtraSkeletonEntry()
        {
            throw new NotImplementedException();
        }
        public static PMPEstManipulationJson FromEstEntry(ExtraSkeletonEntry entry)
        {
            throw new NotImplementedException();
        }
    }
    public class PMPImcManipulationJson
    {
        public struct PMPImcEntry
        {
            public uint AttributeAndSound;
            public uint MaterialId;
            public uint DecalId;
            public uint VfxId;
            public uint MaterialAnimationId;
            public uint AttributeMask;
            public uint SoundId;
        }
        public PMPImcEntry Entry;
        public uint PrimaryId;
        public uint SecondaryId;
        public uint Variant;
        public string ObjectType;
        public string EquipSlot;
        public string BodySlot;

        public XivImc ToImcEntry()
        {
            throw new NotImplementedException();
        }
        public static PMPImcManipulationJson FromImcEntry(XivImc entry)
        {
            throw new NotImplementedException();
        }
    }
    public class PMPEqdpManipulationJson
    {
        public uint Entry;
        public string Gender;
        public string Race;
        public uint SetId;
        public string Slot;

        public EquipmentDeformationParameter ToEqdpEntry()
        {
            throw new NotImplementedException();
        }
        public static PMPEqdpManipulationJson FromEqdpEntry(EquipmentDeformationParameter entry)
        {
            throw new NotImplementedException();
        }
    }
    public class PMPEqpManipulationJson
    {
        public ulong Entry;
        public uint SetId;
        public string Slot;

        public EquipmentParameter ToEqpEntry()
        {
            throw new NotImplementedException();
        }
        public static PMPEqpManipulationJson FromEqpEntry(EquipmentParameter entry)
        {
            throw new NotImplementedException();
        }
    }
    public class PMPGmpManipulationJson
    {
        public bool Enabled;
        public bool Animated;
        public float RotationA;
        public float RotationB;
        public float RotationC;

        // Not sure data sizes on these.
        public uint UnknownA;
        public uint UnknownB;
        public uint UnknownTotal;
        public ulong Value;

        public GimmickParameter ToGmpEntry()
        {
            throw new NotImplementedException();
        }
        public static PMPGmpManipulationJson FromGmpEntry(GimmickParameter entry)
        {
            throw new NotImplementedException();
        }
    }
    public class PMPRspManipulationJson
    {
        public float Entry;
        public string SubRace;
        public string Attribute;

        public RacialGenderScalingParameter ToRgspEntry()
        {
            throw new NotImplementedException();
        }

        public static PMPRspManipulationJson FromRgspEntry(RacialGenderScalingParameter entry)
        {
            throw new NotImplementedException();
        }
    }

}
