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
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.FileTypes;
using xivModdingFramework.Variants.FileTypes;

namespace xivModdingFramework.Cache
{
    /// <summary>
    /// The levels of the dependency graph.
    /// </summary>
    public enum XivDependencyLevel
    {
        Invalid,
        Root,
        Meta,
        Model,
        Material,
        Texture
    }


    /// <summary>
    /// The entire selection of "File" Types which exist within the dependency graph.
    /// These are intentionally lower case so they can be compared easily against file extensions
    /// if desired.
    /// </summary>
    public enum XivDependencyFileType
    {
        invalid,
        root,
        eqp,
        eqdp,
        imc,
        mdl,
        mtrl,
        tex
    }



    /// <summary>
    /// A class representing a top level dependency root.  This is in effect, a collection of
    /// five simple values [Type, Primary Id, Secondary Type, Secondary ID, Slot]
    /// All Entries have at least Primary Type and Id.
    /// From these five populated values, we can effectively generate the entire dependency tree downwards,
    /// and these five values can be generated from any child file via XivDependencyGraph::GetDependencyRoot(internalFilePath)
    /// 
    /// These five values can also be used to collect the entire subset of FFXIV "Items" that live within this
    /// dependency root.
    /// 
    /// This class also overrides ToString() with an arbitrary string based representation of these five values,
    /// which can be used in the various functions which take path strings.
    /// </summary>
    public class XivDependencyRoot
    {
        // Only types with actual dependency structures are supported.
        // This means Equipment, Accessory, Monster, and Demihuman.
        public readonly XivItemType PrimaryType;


        // All roots have at least a primary set Id.
        public readonly int PrimaryId;

        /// <summary>
        /// Secondary types are optional.  Human Equipment in particular has no secondary type; they're just
        /// set as Equipment primary.
        /// </summary>
        public readonly XivItemType? SecondaryType;

        // Secondary Id may not exist for all types.
        public readonly int? SecondaryId;

        // In Abbreviated internal format -NOT- local language format.
        // Slot may not exist for all types.
        public readonly string? Slot;

        // Internal dependency graph reference so we can leach data and functions from it as needed.
        private readonly XivDependencyGraph _graph;


        // Set ID => Subset ID => Slot
        private static readonly string ModelNameFormatWithSlot = "model/{0}{1}{2}{3}_{4}.mdl";
        private static readonly string ModelNameFormatNoSlot = "model/{0}{1}{2}{3}.mdl";

        // Type -> Typecode -> Id
        private static readonly string RootFolderFormatPrimary = "chara/{0}/{1}{2}/";

        // Type -> TypeCode -> Id
        private static readonly string RootFolderFormatSecondary = "obj/{0}/{1}{2}/";

        private static readonly string ImcFileFormat = "{0}{1}.imc";


        public XivDependencyRoot(XivDependencyGraph graph, XivItemType type, int pid, XivItemType? secondaryType = null, int? sid = null, string slot = null)
        {
            PrimaryType = type;
            Slot = slot;
            PrimaryId = pid;
            SecondaryType = secondaryType;
            SecondaryId = sid;
            _graph = graph;


            // Exception handling time!
            // These item subtypes at root level only have one slot
            // it's only at the Material level they're allowed to have other "slots", 
            // and those are simply defined by the name references in the MDL files.

            // Essentially, they're cross-referenced materials that don't actually have a parent tree, so they
            // should belong to the base tree for those item types.
            if(secondaryType == XivItemType.face)
            {
                Slot = "fac";
            } else if(secondaryType == XivItemType.ear)
            {
                Slot = "ear";
            } else if (secondaryType == XivItemType.tail)
            {
                Slot = "til";
            } else if(secondaryType == XivItemType.hair)
            {
                Slot = "hir";
            }
        }


        public static bool operator ==(XivDependencyRoot obj1, XivDependencyRoot obj2)
        {
            
            if (object.ReferenceEquals(obj1, null) && object.ReferenceEquals(obj2, null)) return true;
            if (object.ReferenceEquals(obj1, null) || object.ReferenceEquals(obj2, null)) return false;

            return obj1.ToString() == obj2.ToString();
        }

        public static bool operator !=(XivDependencyRoot obj1, XivDependencyRoot obj2)
        {
            if (object.ReferenceEquals(obj1, null) && object.ReferenceEquals(obj2, null)) return false;
            if (object.ReferenceEquals(obj1, null) || object.ReferenceEquals(obj2, null)) return true;

            return obj1.ToString() != obj2.ToString();
        }

        public override bool Equals(object obj)
        {
            try
            {
                XivDependencyRoot other = (XivDependencyRoot)obj;
                return this == other;
            } catch
            {
                return false;
            }
        }
        public override int GetHashCode()
        {
            return this.ToString().GetHashCode();
        }

        /// <summary>
        /// Converts this dependency root into a raw string entry.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            var path = "%" + PrimaryType.ToString() + "-" + PrimaryId.ToString() + "%";

            if (SecondaryId != null)
            {
                path += SecondaryType.ToString() + "-" + SecondaryId.ToString() + "%";
            }

            if (Slot != null)
            {
                path += Slot + "%";
            }
            return path;
        }

        public string GetRootFolder()
        {
            var pId = PrimaryId.ToString().PadLeft(4, '0');
            var primary =  String.Format(RootFolderFormatPrimary, new string[]{ PrimaryType.ToString(), XivItemTypes.GetFilePrefix(PrimaryType), pId });

            var secondary = "";
            if (SecondaryId != null)
            {
                var sId = SecondaryId.ToString().PadLeft(4, '0');
                var sType = (XivItemType)SecondaryId;
                secondary = String.Format(RootFolderFormatSecondary, new string[] { sType.ToString(), XivItemTypes.GetFilePrefix(sType), sId });
            }

            return primary + secondary;
        }

        public string GetSimpleModelName()
        {
            if(SecondaryType == null)
            {
                throw new NotSupportedException("Cannot generate simple model name for this type. EQDP file must Be used.");
            }

            var pPrefix = XivItemTypes.GetFilePrefix(PrimaryType);
            var pId = PrimaryId.ToString().PadLeft(4, '0');
            var sPrefix = XivItemTypes.GetFilePrefix((XivItemType) SecondaryType);
            var sId = SecondaryId.ToString().PadLeft(4, '0');

            if (Slot != null)
            {
                return String.Format(ModelNameFormatWithSlot, new string[] { pPrefix, pId, sPrefix, sId, Slot });
            }
            else
            {
                return String.Format(ModelNameFormatNoSlot, new string[] { pPrefix, pId, sPrefix, sId });

            }
        }

        public string GetRacialModelName(XivRace race)
        {
            if (SecondaryType != null)
            {
                throw new NotSupportedException("Cannot generate Racial Model name - Item Type does not use Racial Models.");
            }

            // Racial models essentially treat the item as if it had a primary type of 
            // Human to start, of the appropriate human type.
            var pId = race.GetRaceCode();
            var pPrefix = "c";

            var sId = PrimaryId.ToString().PadLeft(4, '0');
            var sPrefix = XivItemTypes.GetFilePrefix(PrimaryType);

            if (Slot != null)
            {
                return String.Format(ModelNameFormatWithSlot, new string[] { pPrefix, pId, sPrefix, sId, Slot });
            }
            else
            {
                return String.Format(ModelNameFormatNoSlot, new string[] { pPrefix, pId, sPrefix, sId });
            }
        }

        /// <summary>
        /// Gets all the meta files in this dependency chain.
        /// </summary>
        /// <returns></returns>
        public async Task<List<string>> GetMetaFiles()
        {
            var maybeMetas = await _graph.Cache.GetChildFiles(this.ToString());

            // Our direct children might be meta level files, or they might be
            // MDL files directly.  So check them.
            if(maybeMetas == null || maybeMetas.Count == 0)
            {
                return maybeMetas;
            }
            var level = _graph.GetDependencyLevel(maybeMetas[0]);
            return level == XivDependencyLevel.Meta ? maybeMetas : new List<string>();
        }

        /// <summary>
        /// Gets all the model files in this dependency chain.
        /// </summary>
        /// <returns></returns>
        public async Task<List<string>> GetModelFiles()
        {
            var metas = await GetMetaFiles();
            var models = new HashSet<string>();
            if (metas != null && metas.Count > 0)
            {
                return await _graph.Cache.GetChildFiles(metas[0]);
            }

            // Some chains have no meta entries, and jump straight to models.
            var folder = GetRootFolder();
            var modelPath = folder + GetSimpleModelName();
            return new List<string>() { modelPath };
        }

        /// <summary>
        /// Gets all the unique material files in this depency chain.
        /// Subsets of this data may be accessed with XivDependencyGraph::GetChildFiles(internalFilePath).
        /// </summary>
        /// <returns></returns>
        public async Task<List<string>> GetMaterialFiles()
        {
            var models = await GetModelFiles();
            var materials = new HashSet<string>();
            if (models != null && models.Count > 0)
            {
                foreach(var model in models)
                {
                    var mdlMats = await _graph.Cache.GetChildFiles(model);
                    foreach(var mat in mdlMats)
                    {
                        materials.Add(mat);
                    }
                }
            }
            return materials.ToList();
        }

        /// <summary>
        /// Gets all of the unique texture files in this depency chain.
        /// Subsets of this data may be accessed with XivDependencyGraph::GetChildFiles(internalFilePath).
        /// </summary>
        /// <returns></returns>
        public async Task<List<string>> GetTextureFiles()
        {
            var materials = await GetMaterialFiles();
            var textures = new HashSet<string>();
            if (materials != null && materials.Count > 0)
            {
                foreach (var mat in materials)
                {
                    var mtrlTexs = await _graph.Cache.GetChildFiles(mat);
                    foreach (var tex in mtrlTexs)
                    {
                        textures.Add(tex);
                    }
                }
            }
            return textures.ToList();
        }

        /// <summary>
        /// Returns the full list of child files for this dependency root.
        /// </summary>
        /// <returns></returns>
        public async Task<List<string>> GetChildFiles()
        {
            // Try to resolve Meta files first.
            var children = new List<string>();
            var eqp = GetEqpEntryPath();
            if (eqp != null)
            {
                children.Add(eqp);
            }
            children.AddRange(GetEqdpEntryPaths());
            children.AddRange(await GetImcEntryPaths());

            // If that failed, try to resolve model files.
            if (children.Count == 0)
            {
                // In some cases, there are no Meta Files.
                // This is only the case with Weapon, Monster, and Demihuman types that have no IMC file (only a single variant)
                // Even so, this is extremely rare.

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

            var imcPath = "";
            if (SecondaryType == null)
            {
                var iPrefix = XivItemTypes.GetFilePrefix(PrimaryType);
                var iId = PrimaryId.ToString().PadLeft(4, '0');
                imcPath = GetRootFolder() + String.Format(ImcFileFormat, new string[] { iPrefix, iId });
            }
            else
            {
                var iPrefix = XivItemTypes.GetFilePrefix((XivItemType)SecondaryType);
                var iId = SecondaryId.ToString().PadLeft(4, '0');
                imcPath = GetRootFolder() + String.Format(ImcFileFormat, new string[] { iPrefix, iId });
            }


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
            if (!EqpPaths.ContainsKey(PrimaryType))
                return null;

            var eqpFile = EqpPaths[PrimaryType];

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
            if (!EqdpFolder.ContainsKey(PrimaryType))
                return entries;

            var folder = EqdpFolder[PrimaryType];

            // Each entry is 16 bits long.
            const int entrySize = Eqp.EquipmentDeformerParameterEntrySize * 8;

            var slots = EquipmentDeformationParameterSet.SlotsAsList(PrimaryType == XivItemType.accessory);
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

        /// <summary>
        /// The groupings of the files in the dependency tree based on their level.
        /// </summary>
        public static readonly Dictionary<XivDependencyLevel, List<XivDependencyFileType>> DependencyLevelGroups = new Dictionary<XivDependencyLevel, List<XivDependencyFileType>>()
        {
            { XivDependencyLevel.Root, new List<XivDependencyFileType>() { XivDependencyFileType.root } },
            { XivDependencyLevel.Meta, new List<XivDependencyFileType>() { XivDependencyFileType.eqp, XivDependencyFileType.eqdp, XivDependencyFileType.imc } },
            { XivDependencyLevel.Model, new List<XivDependencyFileType>() { XivDependencyFileType.mdl } },
            { XivDependencyLevel.Material, new List<XivDependencyFileType>() { XivDependencyFileType.mtrl } },
            { XivDependencyLevel.Texture, new List<XivDependencyFileType>() { XivDependencyFileType.tex} },
        };

        public static readonly List<XivItemType> DependencySupportedTypes = new List<XivItemType>()
        {
            XivItemType.equipment,
            XivItemType.accessory,
            XivItemType.weapon,
            XivItemType.monster,
            XivItemType.demihuman,
            XivItemType.human,

            // XivItemType.body, // Needs some extra custom handling still for skin materials.

            /*
            // These types need more work for dependency suppport.

            // Furniture primarily needs the appropriate function for resolving the model files,
            // and the meta sgd(sp?) file that they have instead of an IMC file.
            XivItemType.indoor,
            XivItemType.outdoor,
            */

        };

        // Captures the file extension of a file (even if it has a binary extension)
        private static readonly Regex _extensionRegex = new Regex(".*\\.([a-z]+)(" + Constants.BinaryOffsetMarker + "[0-9]+)?$");

        // Captures the slot of a file (even if it has a binary extension)
        private static readonly Regex _slotRegex = new Regex("[a-z][0-9]{4}[a-z][0-9]{4}_([a-z]{3})?(?:_[a-z]+)?\\.[a-z]+(?:" + Constants.BinaryOffsetMarker + "[0-9]+)?$");

        // Regex for identifying internal references to an item's root node.
        private static readonly Regex RootLevelRegex = new Regex("^%([a-z]+)-([0-9]+)%(?:([a-z]+)-([0-9]+)%)?(?:([a-z]+)%)?$");

        // Regex for identifying EQP files and extracting the offset.
        private static readonly Regex EqpRegex = new Regex("^chara\\/xls\\/equipmentparameter\\/equipmentparameter\\.eqp" + Constants.BinaryOffsetMarker + "([0-9]+)$");

        // Regex for identifying EQDP files and extracting the type and offset.
        private static readonly Regex EqdpRegex = new Regex("^chara\\/xls\\/charadb\\/(equipment|accessory)deformerparameter\\/c[0-9]{4}\\.eqdp" + Constants.BinaryOffsetMarker + "([0-9]+)$");

        private static readonly Regex MtrlRegex = new Regex("^.*\\.mtrl$");

        // Group 0 == Full File path
        // Group 1 == Root Folder
        // Group 2 == PrimaryId
        // Group 3 == SecondaryId (if it exists)

        private static readonly Regex PrimaryExtractionRegex = new Regex("^chara\\/([a-z]+)\\/[a-z]([0-9]{4})\\/(?:obj\\/([a-z]+)\\/[a-z]([0-9]{4})\\/)?.*$");


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
        }

        /// <summary>
        /// Returns all parent files that this child file depends on as part of its rendering process.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public async Task<List<string>> GetParentFiles(string internalFilePath)
        {
            // This function should be written in terms of going up
            // the tree to the root, then climbing down through
            // GetChildFiles() as much as possible, for cache efficiency.

            var level = GetDependencyLevel(internalFilePath);
            if(level == XivDependencyLevel.Invalid)
            {
                return null;
            }

            if(level == XivDependencyLevel.Root)
            {
                // No parents here.
                return new List<string>();
            }

            var _modding = new Modding(GameInfo.GameDirectory);
            var _index = new Index(GameInfo.GameDirectory);
            var dataFile = IOUtil.GetDataFileFromPath(internalFilePath);
            var mod = await _modding.TryGetModEntry(internalFilePath);

            var roots = (await _cache.GetDependencyRoots(internalFilePath));

            if(roots.Count == 0)
            {
                // No root? No parents.
                return new List<string>();
            }


            if (level == XivDependencyLevel.Meta)
            {
                // Our parent is just our dependency root.
                var root = roots[0];
                return new List<string>() { root.ToString() };

            } else if(level == XivDependencyLevel.Model)
            {
                // The parent of the model is all the metadata.
                var root = roots[0];
                var metaEntries = await root.GetMetaFiles();
                if(metaEntries == null || metaEntries.Count == 0 )
                {
                    // If we have no meta entries, our parent is just the root itself.
                    return new List<string>() { root.ToString() };
                }

                return metaEntries;
                

            } else if(level == XivDependencyLevel.Material)
            {
                var root = roots[0];

                var parents = new List<string>();

                // Get all models in this depedency tree.
                var models = await root.GetModelFiles();
                foreach (var model in models)
                {
                    // And check which materials they use.
                    var materials = await _cache.GetChildFiles(model);
                    if(materials.Contains(internalFilePath))
                    {
                        // If we're used in the model, it's one of our parents.
                        parents.Add(model);
                    }
                }

                return parents;
                
            } else if(level == XivDependencyLevel.Texture)
            {
                var parents = new List<string>();

                // So, textures are the fun case where it's possible for them to have multiple roots.
                foreach (var root in roots)
                {
                    // Get all the materials in this dependency tree.
                    var materials = await root.GetMaterialFiles();
                    foreach(var mat in materials)
                    {
                        // Get all the textures they use.
                        var textures = await _cache.GetChildFiles(mat);
                        if(textures.Contains(internalFilePath))
                        {
                            parents.Add(mat);
                        }
                    }
                }

                return parents;
            }

            // Shouldn't actually be possible to get here, but null to be safe.
            return null;
        }

        /// <summary>
        /// Returns all same-level sibling files for the given sibling file.
        /// Note: This includes the file itself.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public async Task<List<string>> GetSiblingFiles(string internalFilePath)
        {
            var parents = await GetParentFiles(internalFilePath);
            var siblings = new HashSet<string>();
            foreach(var p in parents)
            {
                var children = await _cache.GetChildFiles(p);
                foreach(var c in children)
                {
                    siblings.Add(c);
                }
            }
            return siblings.ToList();
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
            if (level == XivDependencyLevel.Invalid)
            {
                return null;
            }

            if (level == XivDependencyLevel.Root)
            {
                var root = (await _cache.GetDependencyRoots(internalFilePath))[0];
                return await root.GetChildFiles();
            }
            var _modding = new Modding(GameInfo.GameDirectory);

            var dataFile = IOUtil.GetDataFileFromPath(internalFilePath);
            var _mtrl = new Mtrl(GameInfo.GameDirectory, dataFile, GameInfo.GameLanguage);
            var _mdl = new Mdl(GameInfo.GameDirectory, dataFile);

            if (level == XivDependencyLevel.Meta)
            {
                // All meta elements have the same children, so only use the
                // first item in the list for caching purposes.
                var root = (await _cache.GetDependencyRoots(internalFilePath))[0];
                var metaFiles = await root.GetMetaFiles();
                if(metaFiles[0] != internalFilePath)
                {
                    return await _cache.GetChildFiles(metaFiles[0]);
                }
                
                var _eqp = new Eqp(GameInfo.GameDirectory);
                // For items with racial models, get the racial models.
                if (root.PrimaryType == XivItemType.equipment || root.PrimaryType == XivItemType.accessory)
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
                }
                else
                {
                    // Some chains have no meta entries, and jump straight to models.
                    var folder = root.GetRootFolder();
                    var modelPath = folder + root.GetSimpleModelName();
                    return new List<string>() { modelPath };
                }
            }

            if (level == XivDependencyLevel.Model)
            {
                // Models should include skin Materials only if they're in the actual character structure.
                var roots = (await _cache.GetDependencyRoots(internalFilePath));
                if(roots == null)
                {
                    return null;
                }
                var includeSkin = roots.Any(x => x.PrimaryType == XivItemType.human && x.SecondaryType == XivItemType.body);

                var mdlChildren = await _mdl.GetReferencedMaterialPaths(internalFilePath, -1, false, includeSkin);
                return mdlChildren;
            }

            if (level == XivDependencyLevel.Material)
            {
                var mtrlChildren = await _mtrl.GetTexturePathsFromMtrlPath(internalFilePath, false);
                return mtrlChildren;
            }

            if (level == XivDependencyLevel.Texture)
            {
                // Textures have no child files.
                return new List<string>();
            }

            // Shouldn't ever get here, but if we did, null.
            return null;
        }

        /// <summary>
        /// Retrieves the dependency file type of a given file in the system.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public XivDependencyFileType GetDependencyFileType(string internalFilePath)
        {
            if (RootLevelRegex.IsMatch(internalFilePath))
            {
                // This is the root level - Type-Set-Slot entry.
                return XivDependencyFileType.root;
            }

            var match = _extensionRegex.Match(internalFilePath);
            if (!match.Success)
            {
                // This is a folder or some file without an extension.
                return XivDependencyFileType.invalid;
            }
            var suffix = match.Groups[1].Value;
            switch (suffix)
            {
                case "imc":
                    // Do not allow dependency crawling for meta files that do not have proper binary offsets.
                    if (!internalFilePath.Contains(Constants.BinaryOffsetMarker))
                        return XivDependencyFileType.invalid;
                    return XivDependencyFileType.imc;
                case "eqp":
                    if (!internalFilePath.Contains(Constants.BinaryOffsetMarker))
                        return XivDependencyFileType.invalid;
                    return XivDependencyFileType.eqp;
                case "eqdp":
                    if (!internalFilePath.Contains(Constants.BinaryOffsetMarker))
                        return XivDependencyFileType.invalid;
                    return XivDependencyFileType.eqdp;
                case "mdl":
                    return XivDependencyFileType.mdl;
                case "mtrl":
                    return XivDependencyFileType.mtrl;
                case "tex":
                    return XivDependencyFileType.tex;
                default:
                    // Unknown extension
                    return XivDependencyFileType.invalid;
            }


        }

        /// <summary>
        /// Retreives the dependency level of a given file in the system.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public XivDependencyLevel GetDependencyLevel(string internalFilePath)
        {
            var fileType = GetDependencyFileType(internalFilePath);
            if(fileType == XivDependencyFileType.invalid)
            {
                return XivDependencyLevel.Invalid;
            }

            return DependencyLevelGroups.First(x => x.Value.Contains(fileType)).Key;
        }

        /// <summary>
        /// Creates the depenency root for an item from constituent parts.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="primaryId"></param>
        /// <param name="secondaryId"></param>
        /// <param name="slot"></param>
        /// <returns></returns>
        public XivDependencyRoot CreateDependencyRoot(XivItemType type, int primaryId, XivItemType? secondaryType = null, int? secondaryId = null, string slot = null)
        {
            if(!DependencySupportedTypes.Contains(type))
            {
                return null;
            }
            var root = new XivDependencyRoot(this, type, primaryId, secondaryType, secondaryId, slot);
            return root;

        }


        /// <summary>
        /// Creates a dependency root object from a string representation.
        /// </summary>
        /// <param name="rootPlaceholder"></param>
        /// <returns></returns>
        public XivDependencyRoot CreateDependencyRoot(string rootPlaceholder)
        {
            var match = RootLevelRegex.Match(rootPlaceholder);

            if (!match.Success) return null;

            // This is a root level node.  Just reconstruct it.
            var type = (XivItemType)Enum.Parse(typeof(XivItemType), match.Groups[1].Value);
            int pId = Int32.Parse(match.Groups[2].Value);
            XivItemType? secondaryType = null;
            int? sId = null;
            string slot = null;
            if (match.Groups[3].Success)
            {
                secondaryType = (XivItemType)Enum.Parse(typeof(XivItemType), match.Groups[3].Value);
                sId = Int32.Parse(match.Groups[4].Value);
            }

            if (match.Groups[5].Success)
            {
                slot = match.Groups[5].Value;
            }
            var root = CreateDependencyRoot(type, pId, secondaryType, sId, slot);
            return root;
        }
        /// <summary>
        /// So this is by far the shittiest case for resolution of file dependency info.
        /// Because custom textures both can have any name they want, and exist in any folder they want,
        /// We have to crawl the cache to find what custom MTRL refers to them, if any.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        private async Task<List<XivDependencyRoot>> GetCustomTextureDependencyRoots(string internalFilePath)
        {

            var parents = new List<string>();
            var uniqueRoots = new HashSet<XivDependencyRoot>();

            var cachedParents = await _cache.GetCachedParentFiles(internalFilePath);

            foreach (var file in cachedParents)
            {
                var matRoots = await _cache.GetDependencyRoots(file);
            }

            return uniqueRoots.ToList();
        }

        /// <summary>
        /// Resolves the dependency roots for a given child file of any file type.
        /// For Non-Texture files this will always be exactly one root (or null).
        /// For textures this can be be multiple (valid), zero (orphaned), or null (not in dependency graph).
        /// 
        /// A null return indicates the item does not fall within the domain of the dependency graph.
        /// A return of Length 0 indicates this custom texture file does not have any parents remaining.
        ///   - This can still change due to mods beind re-enabled.  A secondary check function should 
        ///   - be written to validate if an orphaned file can be safely deleted or if it still has
        ///   - lingering disabled references in the Modlist.
        ///   
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public async Task<List<XivDependencyRoot>> GetDependencyRoots(string internalFilePath)
        {
            var roots = new HashSet<XivDependencyRoot>();

            var match = RootLevelRegex.Match(internalFilePath);
            if(match.Success)
            {
                // This is a root placeholder item, just reconstruct it and ship it back.
                var root = CreateDependencyRoot(internalFilePath);
                if(root == null)
                {
                    return null;
                }
                return new List<XivDependencyRoot>() { root };
            }

            // Tex files require special handling.
            match = _extensionRegex.Match(internalFilePath);
            if(match.Success && match.Groups[1].Value == "tex")
            {
                // Oh boy.  Here we have to scrape the modlist to find any custom modded roots(plural) we may have.
                var customRoots = await GetCustomTextureDependencyRoots(internalFilePath);
                foreach(var r in customRoots)
                {
                    roots.Add(r);
                }
            }

            // Anything that lives in an extractable root folder is considered a child of that root.
            match = PrimaryExtractionRegex.Match(internalFilePath);
            if (match.Success)
            {
                // Ok, at this point, we have a normal file path.  That means we can extract everything out of it.
                match = PrimaryExtractionRegex.Match(internalFilePath);
                if (match.Success)
                {
                    // Ok, at this point we can generate our root.  First extract all the data from the root path.
                    var type = (XivItemType)Enum.Parse(typeof(XivItemType), match.Groups[1].Value);
                    int pId = Int32.Parse(match.Groups[2].Value);
                    XivItemType? secondaryType = null;
                    int? sId = null;
                    if (match.Groups[3].Success)
                    {

                        secondaryType = (XivItemType)Enum.Parse(typeof(XivItemType), match.Groups[3].Value);
                        sId = Int32.Parse(match.Groups[4].Value);
                    }

                    string slot = null;

                    // Then get the slot if we have one.
                    match = _slotRegex.Match(internalFilePath);
                    if (match.Success)
                    {
                        slot = match.Groups[1].Value;
                    }

                    var root = CreateDependencyRoot(type, pId, secondaryType, sId, slot);
                    if (root == null)
                    {
                        return null;
                    }
                    roots.Add(root);
                }
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

                    return new List<XivDependencyRoot>() { CreateDependencyRoot(XivItemType.equipment, setId, null, null, slot) };

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
                    return new List<XivDependencyRoot>() { CreateDependencyRoot(type, setId, null, null, slot) };
                    
                }

                // At this point, the only conclusion is that the file is not something in the dependency tree.
                return null;
            }

            return roots.ToList();
        }

    }
}
