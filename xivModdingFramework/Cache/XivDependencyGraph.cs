using HelixToolkit.SharpDX.Core.Model.Scene2D;
using SharpDX.Toolkit.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using xivModdingFramework.Models.ModelTextures;
using xivModdingFramework.Mods;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.FileTypes;
using xivModdingFramework.Variants.FileTypes;

namespace xivModdingFramework.Cache
{
    public enum DependencyLevel
    {
        Root,
        Meta,
        Model,
        Material,
        Texture,
        Invalid
    }


    public class XivDependencyRoot
    {
        // Only types with actual dependency structures are supported.
        // This means Equipment, Accessory, Monster, and Demihuman.
        public readonly XivItemType Type;

        // In Abbreviated internal format -NOT- local language format.
        // Slot may not exist for all types.
        public readonly string? Slot;

        // All roots have at least a primary set Id.
        public readonly int PrimaryId;

        // Secondary Id may not exist for all types.
        public readonly int? SecondaryId;

        private readonly XivDependencyGraph _graph;

        // Set ID => Subset ID => Slot
        private static readonly Dictionary<XivItemType, string> SimpleModelNameFormats = new Dictionary<XivItemType, string>()
        {
            { XivItemType.weapon, "model/w{0}b{1}.mdl" },
            { XivItemType.monster, "model/m{0}b{1}.mdl" },
            { XivItemType.demihuman, "model/d{0}e{1}_{2}.mdl" }
        };

        // Race Code => Equipment Set ID => Slot
        private static readonly Dictionary<XivItemType, string> RacialModelNameFormats = new Dictionary<XivItemType, string>()
        {
            { XivItemType.equipment, "model/c{0}e{1}_{2}.mdl" },
            { XivItemType.accessory, "model/c{0}a{1}_{2}.mdl" }
        };


        private static readonly Dictionary<XivItemType, string> RootFolderFormats = new Dictionary<XivItemType, string>()
        {
            { XivItemType.equipment, "chara/equipment/e{0}/" },
            { XivItemType.accessory, "chara/accessory/a{0}/" },
            { XivItemType.weapon, "chara/weapon/w{0}/obj/body/b{1}/" },
            { XivItemType.monster, "chara/monster/m{0}/obj/body/b{1}/" },
            { XivItemType.demihuman, "chara/demihuman/d{0}/obj/equipment/e{1}/" }
        };

        private static readonly Dictionary<XivItemType, string> ImcFileFormats = new Dictionary<XivItemType, string>()
        {
            { XivItemType.equipment, "e{0}.imc" },
            { XivItemType.accessory, "a{0}.imc" },
            { XivItemType.weapon, "b{1}.imc" },
            { XivItemType.monster, "b{1}.imc" },
            { XivItemType.demihuman, "e{1}.imc" }
        };

        public XivDependencyRoot(XivDependencyGraph graph, XivItemType type, int pid, int? sid = null, string slot = null)
        {
            Type = type;
            Slot = slot;
            PrimaryId = pid;
            SecondaryId = sid;
            _graph = graph;
        }


        /// <summary>
        /// Converts this dependency root into a raw string entry.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            var path = "%" + Type.ToString() + "-" + PrimaryId.ToString();
            if(SecondaryId != null)
            {
                path += "-" + SecondaryId.ToString();
            }
            path += "%";
            if(Slot != null)
            {
                path += Slot + "%";
            }
            return path;
        }

        public string GetRootFolder()
        {
            var pId = PrimaryId.ToString().PadLeft(4, '0');
            var sId = SecondaryId != null ? SecondaryId.ToString().PadLeft(4, '0') : "0000";
            var slt = Slot != null ? Slot : "";
            return String.Format(RootFolderFormats[Type], new string[]{ pId, sId, slt });
        }

        public string GetSimpleModelName()
        {
            if(!SimpleModelNameFormats.ContainsKey(Type))
            {
                throw new NotSupportedException("Cannot generate simple model name for this type. EQDP file must Be used.");
            }
            var pId = PrimaryId.ToString().PadLeft(4, '0');
            var sId = SecondaryId != null ? SecondaryId.ToString().PadLeft(4, '0') : "0000";
            var slt = Slot != null ? Slot : "";
            return String.Format(SimpleModelNameFormats[Type], new string[] { pId, sId, slt });
        }

        public string GetRacialModelName(XivRace race)
        {
            if (!RacialModelNameFormats.ContainsKey(Type))
            {
                throw new NotSupportedException("Cannot generate Racial Model name - Item Type does not use Racial Models.");
            }
            if(Slot == null)
            {
                throw new NotSupportedException("Cannot get Racial Model path without Slot Name.");
            }
            var pId = PrimaryId.ToString().PadLeft(4, '0');
            var slt = (string) Slot;
            var rCode = race.GetRaceCode();
            return String.Format(RacialModelNameFormats[Type], new string[] { rCode, pId, slt });
        }

        /// <summary>
        /// Returns the full list of child files for this dependency root.
        /// </summary>
        /// <returns></returns>
        public async Task<List<string>> GetChildFiles()
        {
            var children = new List<string>();
            var eqp = GetEqpEntryPath();
            if(eqp != null)
            {
                children.Add(eqp);
            }
            children.AddRange(GetEqdpEntryPaths());
            children.AddRange(await GetImcEntryPaths());

            if(children.Count == 0)
            {
                // This is the rare case of a Dual Wield offhand or other weapon/monster/demi that has no IMC file.
                // In that case, direct straight onto the MDL file.
                // Weapons, Monsters, Demihumans Equipments only have a single model.
                var folder = GetRootFolder();
                var modelPath = folder + GetSimpleModelName();
                return new List<string>() { modelPath };
            }

            return children;
        }


        /// <summary>
        /// Gets all IMC Entries associated with this root node.
        /// </summary>
        /// <returns></returns>
        public async Task<List<string>> GetImcEntryPaths()
        {
            // We need to locate and open the IMC file, and then check how many
            // actual sets are in it, and calculate the pointers to our associated
            // Set + Slot entries.
            // Then return them in the format of <ImcPath>::<Offset>
            var imcEntries = new List<string>();
            var pId = PrimaryId.ToString().PadLeft(4, '0');
            var sId = SecondaryId != null ? SecondaryId.ToString().PadLeft(4, '0') : "0000";
            var slt = Slot != null ? Slot : "";
            var imcPath = String.Format(RootFolderFormats[Type], new string[] { pId, sId, slt }) + String.Format(ImcFileFormats[Type], new string[] { pId, sId, slt });


            var _gameDirectory = _graph.GameInfo.GameDirectory;
            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);
            var imcOffset = await index.GetDataOffset(imcPath);

            if (imcOffset == 0)
            {
                // Some chains don't have IMC files.
                return imcEntries;
            } 

            var imcByteData = await dat.GetType2Data(imcOffset, IOUtil.GetDataFileFromPath(imcPath));

            var subsetCount = 0;
            ImcType identifier = ImcType.Unknown;
            using (var br = new BinaryReader(new MemoryStream(imcByteData)))
            {
                subsetCount = br.ReadInt16();
                identifier = (ImcType)br.ReadInt16();
            };

            if (identifier == ImcType.Unknown)
            {
                return imcEntries;
            }

            const int startingOffset = 4;
            const int subEntrySize = 6;
            var entrySize = identifier == ImcType.NonSet ? subEntrySize : subEntrySize * 5;
            var subOffset = 0;
            if(Slot != null && Imc.SlotOffsetDictionary.ContainsKey(Slot))
            {
                subOffset = Imc.SlotOffsetDictionary[Slot] * subEntrySize;
            }

            var offset = startingOffset + subOffset;
            imcEntries.Add(imcPath + Constants.BinaryOffsetMarker + (offset * 8).ToString());

            for(int i = 0; i < subsetCount; i++)
            {
                offset = startingOffset + ((i + 1) * entrySize) + subOffset;
                imcEntries.Add(imcPath + Constants.BinaryOffsetMarker + (offset * 8).ToString());
            }

            return imcEntries;
        }

        private static readonly Dictionary<XivItemType, string> EqdpFolder= new Dictionary<XivItemType, string>()
        {
            { XivItemType.equipment, "chara/xls/charadb/equipmentdeformerparameter/" },
            { XivItemType.accessory, "chara/xls/charadb/accessorydeformerparameter/" }
        };

        private static readonly Dictionary<XivItemType, string> EqpPaths = new Dictionary<XivItemType, string>()
        {
            { XivItemType.equipment, "chara/xls/equipmentparameter/equipmentparameter.eqp" }
        };

        /// <summary>
        /// Gets the EQP entry for a given Type+Set+Slot.
        /// </summary>
        /// <returns></returns>
        public string GetEqpEntryPath()
        {
            if (!EqpPaths.ContainsKey(Type))
                return null;

            var eqpFile = EqpPaths[Type];

            // Each entry is 64 bits long.
            const int entrySize = Eqp.EquipmentParameterEntrySize * 8;
            var subOffset = EquipmentParameterSet.EntryOffsets[Slot] * 8;

            long offset = (entrySize * PrimaryId) + subOffset;


            return eqpFile + Constants.BinaryOffsetMarker + offset.ToString();
        }


        /// <summary>
        /// Retrieves all of the EQDP entries for a given Type+Set+Slot.
        /// </summary>
        /// <returns></returns>
        public List<string> GetEqdpEntryPaths()
        {
            // There's an EQDP file for every race,
            // So we'll have 1 entry per race.
            var entries = new List<string>();
            if (!EqdpFolder.ContainsKey(Type))
                return entries;

            var folder = EqdpFolder[Type];

            // Each entry is 16 bits long.
            const int entrySize = Eqp.EquipmentDeformerParameterEntrySize * 8;

            var slots = EquipmentDeformationParameterSet.SlotsAsList(Type == XivItemType.accessory);
            var order = slots.IndexOf(Slot);
            var subOffset = order * 2; // 2 bits per segment.

            long offset = (entrySize * PrimaryId) + subOffset;
            foreach(var race in XivRaces.PlayableRaces)
            {
                entries.Add(folder + "c" + race.GetRaceCode() + ".eqdp" + Constants.BinaryOffsetMarker + offset.ToString());
            }

            return entries;
        }

    }

    /// <summary>
    /// File dependency tree crawler for internal files.
    /// This class is automatically spawned and made available by the XivCache class,
    /// and should generally be referenced from there, not directly instantiated otherwise.
    /// </summary>
    public class XivDependencyGraph
    {
        /*
         *  File Dependency Tree for FFXIV models works as such 
         *  
         * 
         * ROOT     - [SET AND SLOT] - Theoretical/Just a number/prefix, no associated file.
         * META     - [EQP/EQDP/IMC] - Binary entries. - Not all of these entries are available for all types, but at least one always is.
         * MODEL    - [MDL] - Files for each racial model.
         * MATERIAL - [MTRL] - Files for each Racial Model x Material Variant, roughly.
         * TEXTURE  - [TEX] - Files For each Material. May be used by multiple.
         * 
         * 
         * At each level, changing any data about the elements at that level
         * has some variety of knock-on effect to the items below.  Modifications to a level should
         * always include all lower levels when exported, to ensure an identical result on the end user
         * system.
         * 
         * Changing data of elements has no effect on elements of the same or higher level.
         * 
         * - Exception note - META level data should always be treated as a single 'file'.  
         *      - All direct children of a META level element depend on -all- META level elements.
         * 
         *   Ex. Changing an MDL file pulls some MTRL and Tex files along with it.
         *   But it does not have any affect on the IMC entries or other metadata above it.
         *      
         * - A custom path created file may deleted/removed from the system IF AND ONLY IF it has no parents.
         * 
         * - A custom Model may be deleted once no more Meta(EQDP) files point to it anymore.
         * - A custom Material may be deleted once no Models point to it anymore.
         * - A custom Texture may be deleted once no Materials point to it anymore.
         * 
         * - Non-custom files should be left in the file system for redundant safety.
        */



        private GameInfo _gameInfo;
        private XivCache _cache;
        private Modding _modding;
        private Imc _imc;
        private Tex _tex;
        private Index _index;
        private Eqp _eqp;

        public static readonly List<XivItemType> DependencySupportedTypes = new List<XivItemType>()
        {
            XivItemType.equipment,
            XivItemType.accessory,
            XivItemType.weapon,
            XivItemType.monster,
            XivItemType.demihuman
        };

        // Captures the file extension of a file (even if it has a binary extension)
        private static readonly Regex _extensionRegex = new Regex(".*\\.([a-z]+)(" + Constants.BinaryOffsetMarker + "[0-9]+)?$");

        // Captures the slot of a file (even if it has a binary extension)
        private static readonly Regex _slotRegex = new Regex("[a-z][0-9]{4}[a-z][0-9]{4}_([a-z]{3})?(?:_[a-z]+)?\\.[a-z]+(?:" + Constants.BinaryOffsetMarker + "[0-9]+)?$");

        // Regex for identifying internal references to an item's root node.
        private static readonly Regex RootLevelRegex = new Regex("^%([a-z]+)-([0-9]+)(?:-([0-9]+))?%(?:([a-z]+)%)?$");

        // Regex for identifying EQP files and extracting the offset.
        private static readonly Regex EqpRegex = new Regex("^chara\\/xls\\/equipmentparameter\\/equipmentparameter\\.eqp" + Constants.BinaryOffsetMarker + "([0-9]+)$");

        // Regex for identifying EQDP files and extracting the type and offset.
        private static readonly Regex EqdpRegex = new Regex("^chara\\/xls\\/charadb\\/(equipment|accessory)deformerparameter\\/c[0-9]{4}\\.eqdp" + Constants.BinaryOffsetMarker + "([0-9]+)$");

        // Group 0 == Full File path
        // Group 1 == Root Folder
        // Group 2 == PrimaryId
        // Group 3 == SecondaryId (if it exists)
        private static readonly Dictionary<XivItemType, Regex> RootFolderRegexes = new Dictionary<XivItemType, Regex>()
        {
            { XivItemType.equipment,  new Regex("^(chara\\/equipment\\/e([0-9]{4})\\/).*$") },
            { XivItemType.accessory,  new Regex("^(chara\\/accessory\\/a([0-9]{4})\\/).*$") },
            { XivItemType.weapon,  new Regex("^(chara\\/weapon\\/w([0-9]{4})\\/obj\\/body\\/b([0-9]{4})\\/).*$") },
            { XivItemType.monster,  new Regex("^(chara\\/monster\\/m([0-9]{4})\\/obj\\/body\\/b([0-9]{4})\\/).*$") },
            { XivItemType.demihuman,  new Regex("^(chara\\/demihuman\\/d([0-9]{4})\\/obj\\/equipment\\/e([0-9]{4})\\/).*$") },
        };


        public GameInfo GameInfo
        {
            get
            {
                return _gameInfo;
            }
        }
        public XivCache Cache
        {
            get
            {
                return _cache;
            }
        }

        public XivDependencyGraph(GameInfo gameInfo, XivCache cache)
        {
            _gameInfo = gameInfo;
            _cache = cache;
            _modding = new Modding(gameInfo.GameDirectory);
            _eqp = new Eqp(gameInfo.GameDirectory);
            _tex = new Tex(gameInfo.GameDirectory);
            _imc = new Imc(gameInfo.GameDirectory);
            _index = new Index(gameInfo.GameDirectory);
        }

        /// <summary>
        /// Returns all parent files that this child file depends on as part of its rendering process.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public async Task<List<string>> GetParentFiles(string internalFilePath)
        {
            

            var level = GetDependencyLevel(internalFilePath);
            if(level == DependencyLevel.Invalid)
            {
                return null;
            }

            if(level == DependencyLevel.Root)
            {
                // No parents here.
                return new List<string>();
            }

            var suffix = _extensionRegex.Match(internalFilePath).Groups[1].Value;
            var dataFile = IOUtil.GetDataFileFromPath(internalFilePath);
            var _mtrl = new Mtrl(GameInfo.GameDirectory, dataFile, GameInfo.GameLanguage);
            var mod = await _modding.TryGetModEntry(internalFilePath);
            var modded = mod == null ? false : mod.enabled;
            var customPath = !(await _index.IsDefaultFilePath(internalFilePath));

            if (level == DependencyLevel.Meta)
            {
                // Our parent is just our dependency root.
                var root = (await GetDependencyRoots(internalFilePath))[0];
                return new List<string>() { root.ToString() };

            } else if(level == DependencyLevel.Model)
            {
                // The parent of the model is all the metadata.
                // Aka Root's children.
                var root = (await GetDependencyRoots(internalFilePath))[0];
                return await root.GetChildFiles();
                

            } else if(level == DependencyLevel.Material)
            {
                // Todo - Identify models using this material.
                
            } else if(level == DependencyLevel.Texture)
            {
                // Todo - Identify materials using this texture.

            }


            return null;
        }

        /// <summary>
        /// Returns all same-level sibling files for the given sibling file.
        /// Note: This includes the file itself.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public async Task<List<string>> SiblingFiles(string internalFilePath)
        {
            var siblings = new List<string>();

            var level = GetDependencyLevel(internalFilePath);
            if (level == DependencyLevel.Invalid)
            {
                return null;
            }

            if (level == DependencyLevel.Root)
            {
                var root = (await GetDependencyRoots(internalFilePath))[0];

                // No siblings here.
                return new List<string>();
            }

            var suffix = _extensionRegex.Match(internalFilePath).Groups[1].Value;
            var dataFile = IOUtil.GetDataFileFromPath(internalFilePath);
            var mod = await _modding.TryGetModEntry(internalFilePath);
            var _mtrl = new Mtrl(GameInfo.GameDirectory, dataFile, GameInfo.GameLanguage);
            var modded = mod == null ? false : mod.enabled;
            var customPath = !(await _index.IsDefaultFilePath(internalFilePath));

            if (level == DependencyLevel.Meta)
            {
                // Our siblings are all of the root's children.
                var root = (await GetDependencyRoots(internalFilePath))[0];
                return await root.GetChildFiles();
            }
            else if (level == DependencyLevel.Model)
            {
                var root = (await GetDependencyRoots(internalFilePath))[0];
                // TODO - Get Root node -> Get EQDP Entry -> Check EQDP entry for all other racial models.
            }
            else if (level == DependencyLevel.Material)
            {
                // TODO - Get Parent MDLs -> Get all MTRLs in those MDLs
            }
            else if (level == DependencyLevel.Texture)
            {
                // TODO -> Get Parent MTRLs -> Get all Textures in those MTRLs
            }

            return siblings;
        }

        /// <summary>
        /// Returns all child files that depend on the given parent file path as part of their
        /// rendering process.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public async Task<List<string>> GetChildFiles(string internalFilePath)
        {

            var level = GetDependencyLevel(internalFilePath);
            if (level == DependencyLevel.Invalid)
            {
                return null;
            }

            if (level == DependencyLevel.Root)
            {
                var root = (await GetDependencyRoots(internalFilePath))[0];
                return await root.GetChildFiles();
            }

            var suffix = _extensionRegex.Match(internalFilePath).Groups[1].Value;
            var dataFile = IOUtil.GetDataFileFromPath(internalFilePath);
            var mod = await _modding.TryGetModEntry(internalFilePath);
            var _mtrl = new Mtrl(GameInfo.GameDirectory, dataFile, GameInfo.GameLanguage);
            var _mdl = new Mdl(GameInfo.GameDirectory, dataFile);
            var modded = mod == null ? false : mod.enabled;
            var customPath = !(await _index.IsDefaultFilePath(internalFilePath));

            if (level == DependencyLevel.Meta)
            {
                
                var root = (await GetDependencyRoots(internalFilePath))[0];

                // For items with racial models, get the racial models.
                if(root.Type == XivItemType.equipment || root.Type == XivItemType.accessory)
                {
                    var races = await _eqp.GetAvailableRacialModels(root.PrimaryId, root.Slot);
                    var rootPath = root.GetRootFolder();
                    var children = new List<string>();
                    foreach (var race in races)
                    {
                        var modelPath = rootPath + root.GetRacialModelName(race);
                        children.Add(modelPath);
                    }
                    return children;
                } else
                {
                    // Weapons, Monsters, Demihumans Equipments only have a single model.
                    var folder = root.GetRootFolder();
                    var modelPath = folder + root.GetSimpleModelName();
                    return new List<string>() { modelPath };
                }

            }

            if (level == DependencyLevel.Model)
            {
                var mdlChildren = await _mdl.GetReferencedMaterialPaths(internalFilePath, -1, false, false);
                return mdlChildren;
            }

            if (level == DependencyLevel.Material)
            {
                var mtrlChildren = await _mtrl.GetTexturePathsFromMtrlPath(internalFilePath, false);
                return mtrlChildren;
            }

            if (level == DependencyLevel.Texture)
            {
                // Textures have no child files.
                return new List<string>();
            }

            // Shouldn't ever get here, but if we did, null.
            return null;
        }




        /// <summary>
        /// Retreives the dependency level of a given file in the system.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public DependencyLevel GetDependencyLevel(string internalFilePath)
        {
            if (RootLevelRegex.IsMatch(internalFilePath))
            {
                // This is the root level - Type-Set-Slot entry.
                return DependencyLevel.Root;
            }

            var match = _extensionRegex.Match(internalFilePath);
            if(!match.Success)
            {
                // This is a folder or some file without an extension.
                return DependencyLevel.Invalid;
            }

            var suffix = match.Groups[1].Value;
            switch(suffix)
            {
                case "imc":
                case "eqp":
                case "eqdp":
                    // Do not allow dependency crawling for meta files that do not have proper binary offsets.
                    if (!internalFilePath.Contains(Constants.BinaryOffsetMarker))
                        return DependencyLevel.Invalid;
                    return DependencyLevel.Meta;
                case "mdl":
                    return DependencyLevel.Model;
                case "mtrl":
                    return DependencyLevel.Material;
                case "tex":
                    return DependencyLevel.Texture;
                default:
                    // Unknown extension
                    return DependencyLevel.Invalid;
            }
        }



        /// <summary>
        /// Retrieves the depenency root for an item from constituent parts.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="primaryId"></param>
        /// <param name="secondaryId"></param>
        /// <param name="slot"></param>
        /// <returns></returns>
        public XivDependencyRoot GetDependencyRoot(XivItemType type, int primaryId, int? secondaryId = null, string slot = null)
        {
            if(!DependencySupportedTypes.Contains(type))
            {
                return null;
            }
            var root = new XivDependencyRoot(this, type, primaryId, secondaryId, slot);
            return root;

        }

        /// <summary>
        /// Resovles the dependency roots for a given child file of any file type.
        /// For Non-Texture files this will always be exactly one root (or null).
        /// For Default texture files this will always be exactly one root (or null).
        /// For Custom textures this can be be multiple (or zero).
        /// 
        /// A null return indicates the item does not fall within the domain of the dependency graph.
        /// A return of Length 0 indicates this custom texture file does not have any parents remaining (and thus is no longer in the dependency graph and may be safely deleted).
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public async Task<List<XivDependencyRoot>> GetDependencyRoots(string internalFilePath)
        {
            var roots = new List<XivDependencyRoot>();

            var match = RootLevelRegex.Match(internalFilePath);
            if(match.Success)
            {
                // This is a root level node.  Just reconstruct it.
                var type = (XivItemType)Enum.Parse(typeof(XivItemType), match.Groups[1].Value);
                int pId = Int32.Parse(match.Groups[2].Value);
                int? sId = null;
                string slot = null;
                if (match.Groups[3].Success) {
                    sId = Int32.Parse(match.Groups[3].Value);
                }

                if(match.Groups[4].Success)
                {
                    slot = match.Groups[4].Value;
                }
                var root = GetDependencyRoot(type, pId, sId, slot);
                if(root == null)
                {
                    return null;
                }
                roots.Add(root);
                return roots;
            }

            match = _extensionRegex.Match(internalFilePath);
            if(match.Success && match.Groups[1].Value == "tex")
            {
                var isDefault = await _index.IsDefaultFilePath(internalFilePath);
                if(!isDefault)
                {
                    // Oh boy.  Here we have to scrape the modlist to determine what dependency roots(plural) we have.
                    throw new NotImplementedException("Custom Texture Paths Needs Special Handling still.");
                }
            }

            // Ok, at this point, we have a normal file path.  That means we can extract everything out of it, if it's one of our extractable types.
            foreach(var kv in RootFolderRegexes)
            {
                match = kv.Value.Match(internalFilePath);
                if (!match.Success) continue;

                // Ok, at this point we can generate our root.
                var type = kv.Key;
                int pId = Int32.Parse(match.Groups[2].Value);
                int? sId = null;
                string slot = null;

                if(match.Groups.Count > 3)
                {
                    sId = Int32.Parse(match.Groups[3].Value);
                }

                match = _slotRegex.Match(internalFilePath);
                if(match.Success)
                {
                    slot = match.Groups[1].Value;
                }

                var root = GetDependencyRoot(type, pId, sId, slot);
                if (root == null)
                {
                    return null;
                }
                roots.Add(root);
                break;
            }

            if(roots.Count == 0)
            {
                // So at this point, it didn't match any of our root folder extraction regex.
                // That means it's either invalid, or it's an EQP/EQDP file.  If it's one those, we have to pull the binary offset
                // and reverse-math the equipment set and slot back out.

                // For EQPs, this is 64 bits per set, then within that, we can determine the slot based on the offset internally within the full set.
                match = EqpRegex.Match(internalFilePath);
                if (match.Success)
                {
                    var offset = Int32.Parse(match.Groups[1].Value);
                    var entrySize = Eqp.EquipmentParameterEntrySize * 8;
                    var setId = offset / entrySize;

                    var subOffset = (offset % entrySize) / 8;
                    var slot = EquipmentParameterSet.EntryOffsets.First(x => x.Value == subOffset).Key;

                    return new List<XivDependencyRoot>() { GetDependencyRoot(XivItemType.equipment, setId, null, slot) };

                }

                // For EQDPs, this is 16 bits per set, then within that, we can determine the slot based on the offset internally within the full set.
                match = EqdpRegex.Match(internalFilePath);
                if (match.Success)
                {
                    var type = (XivItemType) Enum.Parse(typeof(XivItemType), match.Groups[1].Value);
                    var offset = Int32.Parse(match.Groups[2].Value);
                    var entrySize = Eqp.EquipmentDeformerParameterEntrySize * 8;
                    var setId = offset / entrySize;


                    var slotOffset = offset % entrySize;
                    var slotIndex = slotOffset / 2; // 2 Bits per slot.

                    var slots = EquipmentDeformationParameterSet.SlotsAsList(type == XivItemType.accessory);
                    var slot = slots[slotIndex];

                    // Ok, now we have everything we need to create the root object.
                    return new List<XivDependencyRoot>() { GetDependencyRoot(type, setId, null, slot) };
                    
                }

                // At this point, the only conclusion is that the file is not something in the dependency tree.
                roots = null;
            }

            return roots;
        }
    }
}
