using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items;
using xivModdingFramework.Items.Categories;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Materials.FileTypes;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Mods;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Variants.FileTypes;
using static xivModdingFramework.Cache.XivCache;

namespace xivModdingFramework.Cache
{

    /// <summary>
    /// Holder for extension methods for IItem from the dependency graph.
    /// Shouldn't really ever need to be accessed directly.
    /// </summary>
    public static class ItemRootExtensions
    {
        public static XivDependencyRoot GetRoot(this IItem item)
        {
            return XivDependencyGraph.CreateDependencyRoot(GetRootInfo(item));
        }
        public static XivDependencyRootInfo GetRootInfo(this IItem item)
        {
            var rootFolder = item.GetItemRootFolder();
            var info = XivDependencyGraph.ExtractRootInfo(rootFolder);
            info.Slot = item.GetItemSlotAbbreviation();

            if (String.IsNullOrWhiteSpace(info.Slot))
            {
                info.Slot = null;
            }
            return info;
        }

        public static async Task<List<IItemModel>> GetSharedImcSubsetItems(this IItemModel item)
        {
            var root = item.GetRoot();
            if (root != null)
            {
                var items = await root.GetAllItems(item.ModelInfo.ImcSubsetID);
                items = items.OrderBy(x => x.Name, new ItemNameComparer()).ToList();
                return items;
            }
            else
            {
                return new List<IItemModel>() { (IItemModel)item.Clone() };
            }

        }
        public static async Task<List<IItemModel>> GetSharedMaterialItems(this IItemModel item)
        {
            var sameModelItems = new List<IItemModel>();
            sameModelItems = await item.GetSharedModelItems();

            try
            {
                var sameMaterialItems = new List<IItemModel>();

                var imc = new Imc(XivCache.GameInfo.GameDirectory);
                var originalInfo = await imc.GetImcInfo(item);
                foreach (var i in sameModelItems)
                {
                    var info = await imc.GetImcInfo(i);
                    if (info.Variant == originalInfo.Variant)
                    {
                        sameMaterialItems.Add(i);
                    }
                }

                sameMaterialItems = sameMaterialItems.OrderBy(x => x.Name, new ItemNameComparer()).ToList();
                return sameMaterialItems;
            } catch
            {
                // No IMC file exists for this item.  
                // In this case, it affects all items in the same root.
                return sameModelItems;
            }
        }
        public static async Task<List<IItemModel>> GetSharedModelItems(this IItemModel item)
        {
            var root = item.GetRoot();
            var items = new List<IItemModel>();
            if (root != null) {
                items = await root.GetAllItems();
            }
            if (items.Count == 0) { 
                items.Add((IItemModel)item.Clone());
            }

            items = items.OrderBy(x => x.Name, new ItemNameComparer()).ToList();
            return items;
        }

    }

    /// <summary>
    /// The levels of the dependency graph.
    /// </summary>
    public enum XivDependencyLevel
    {
        Invalid,
        Root,
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
        meta,
        eqp,
        eqdp,
        imc,
        mdl,
        mtrl,
        tex
    }


    // A naive representation of a dependency root/root folder in FFXIV's
    // File System.  Provides basic calculated fields, however, more extensive
    // calculations expect this item to be fully qualified and properly contained
    // in an actual dependency root object, accessible via .ToRoot()
    public struct XivDependencyRootInfo :ICloneable
    {
        // Only types with actual dependency structures are supported.
        // This means Equipment, Accessory, Monster, and Demihuman.
        public XivItemType PrimaryType;


        // All roots have at least a primary set Id.
        public int PrimaryId;

        /// <summary>
        /// Secondary types are optional.  Human Equipment in particular has no secondary type; they're just
        /// set as Equipment primary.
        /// </summary>
        public XivItemType? SecondaryType;

        // Secondary Id may not exist for all types.
        public int? SecondaryId;

        // In Abbreviated internal format -NOT- local language format.
        // Slot may not exist for all types.
        public string? Slot;

        /// <summary>
        /// Converts this dependency root into a raw string entry.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return GetRootFile();
        }
        public object Clone()
        {
            return this.MemberwiseClone();
        }

        public bool IsValid()
        {
            return PrimaryType != XivItemType.unknown && PrimaryType != XivItemType.none;
        }

        // Type -> Typecode -> Id
        private static readonly string RootFolderFormatPrimary = "chara/{0}/{1}{2}/";

        // Type -> TypeCode -> Id
        private static readonly string RootFolderFormatSecondary = "obj/{0}/{1}{2}/";

        private static readonly string HousingRootFolderFormat = "bgcommon/hou/{0}/general/{1}/";


        // pPrefix => pId => sPrefix => sId => Slot
        private static readonly string BaseFileFormatWithSlot = "{0}{1}{2}{3}_{4}";
        private static readonly string BaseFileFormatNoSlot = "{0}{1}{2}{3}";

        // {0} = BaseFileFormat
        private static readonly string ModelNameFormat = "{0}.mdl";

        /// <summary>
        /// Gets the file name base for this root.
        /// Ex c0101f0001_fac
        /// </summary>
        /// <returns></returns>
        public string GetBaseFileName()
        {
            var pId = PrimaryId.ToString().PadLeft(4, '0');
            var pPrefix = XivItemTypes.GetSystemPrefix(PrimaryType);
            var sId = "";
            var sPrefix = "";
            if (SecondaryType != null)
            {
                sId = SecondaryId.ToString().PadLeft(4, '0');
                sPrefix = XivItemTypes.GetSystemPrefix((XivItemType)SecondaryType);
            }

            if (Slot != null)
            {
                return String.Format(BaseFileFormatWithSlot, new string[] { pPrefix, pId, sPrefix, sId, Slot });
            }
            else
            {
                return String.Format(BaseFileFormatNoSlot, new string[] { pPrefix, pId, sPrefix, sId });
            }
        }

        public string GetRootFile()
        {
            return GetRootFolder() + GetBaseFileName() + ".meta";
        }

        /// <summary>
        /// Gets the root folder for this depenedency root.
        /// </summary>
        /// <returns></returns>
        public string GetRootFolder()
        {
            if (PrimaryType == XivItemType.indoor || PrimaryType == XivItemType.outdoor)
            {
                // BGCommon Dat stuff.
                var pId = PrimaryId.ToString().PadLeft(4, '0');
                return String.Format(HousingRootFolderFormat, new string[] { XivItemTypes.GetSystemName(PrimaryType), pId });
            }
            else
            {
                // All the Dat 4 stuff.
                var pId = PrimaryId.ToString().PadLeft(4, '0');
                var primary = String.Format(RootFolderFormatPrimary, new string[] { XivItemTypes.GetSystemName(PrimaryType), XivItemTypes.GetSystemPrefix(PrimaryType), pId });

                var secondary = "";
                if (SecondaryType != null)
                {
                    var sId = SecondaryId.ToString().PadLeft(4, '0');
                    var sType = (XivItemType)SecondaryType;
                    secondary = String.Format(RootFolderFormatSecondary, new string[] { XivItemTypes.GetSystemName(sType), XivItemTypes.GetSystemPrefix(sType), sId });
                }

                return primary + secondary;
            }
        }

        public string GetSimpleModelName()
        {
            if (PrimaryType == XivItemType.equipment || PrimaryType == XivItemType.accessory)
            {
                throw new NotSupportedException("Cannot generate simple model name for this type. EQDP file must Be used.");
            }

            return String.Format(ModelNameFormat, new string[] { GetBaseFileName() });
        }

        public string GetRacialModelName(XivRace race)
        {
            return GetRacialModelName(Int32.Parse(XivRaces.GetRaceCode(race)));
        }

        /// <summary>
        /// Calculates and returns the SGD file name for this root.
        /// </summary>
        /// <returns></returns>
        public string GetSgdName()
        {
            if (PrimaryType != XivItemType.indoor && PrimaryType != XivItemType.outdoor)
            {
                throw new NotSupportedException("Cannot get SGB File for Non-Furniture item type.");
            }

            var slotFake = PrimaryType == XivItemType.indoor ? "fun" : "gar";
                
            var assetFile = $"{slotFake}_b0_m{PrimaryId.ToString().PadLeft(4, '0')}.sgb";
            return assetFile;
        }

        public string GetRacialModelName(int raceRaw)
        {
            if (SecondaryType != null)
            {
                throw new NotSupportedException("Cannot generate Racial Model name - Item Type does not use Racial Models.");
            }

            // Racial models essentially treat the item as if it had a primary type of 
            // Human to start, of the appropriate human type.
            var pId = raceRaw.ToString().PadLeft(4, '0');
            var pPrefix = XivItemTypes.GetSystemPrefix(XivItemType.human);

            var sId = PrimaryId.ToString().PadLeft(4, '0');
            var sPrefix = XivItemTypes.GetSystemPrefix(PrimaryType);

            var baseName = "";
            if (Slot != null)
            {
                baseName = String.Format(BaseFileFormatWithSlot, new string[] { pPrefix, pId, sPrefix, sId, Slot });
            }
            else
            {
                baseName = String.Format(BaseFileFormatNoSlot, new string[] { pPrefix, pId, sPrefix, sId });
            }

            return String.Format(ModelNameFormat, new string[] { baseName });
        }

        public static bool operator ==(XivDependencyRootInfo obj1, XivDependencyRootInfo obj2)
        {

            if (object.ReferenceEquals(obj1, null) && object.ReferenceEquals(obj2, null)) return true;
            if (object.ReferenceEquals(obj1, null) || object.ReferenceEquals(obj2, null)) return false;

            return obj1.ToString() == obj2.ToString();
        }

        public static bool operator !=(XivDependencyRootInfo obj1, XivDependencyRootInfo obj2)
        {
            if (object.ReferenceEquals(obj1, null) && object.ReferenceEquals(obj2, null)) return false;
            if (object.ReferenceEquals(obj1, null) || object.ReferenceEquals(obj2, null)) return true;

            return obj1.ToString() != obj2.ToString();
        }

        public override bool Equals(object obj)
        {
            try
            {
                XivDependencyRootInfo other = (XivDependencyRootInfo)obj;
                return this == other;
            }
            catch
            {
                return false;
            }
        }
        public override int GetHashCode()
        {
            return this.ToString().GetHashCode();
        }

        public XivDependencyRoot ToFullRoot()
        {
            return XivDependencyGraph.CreateDependencyRoot(this);
        }

    }

    /// <summary>
    /// A class representing a top level dependency root.  This is in effect, a collection of
    /// five simple values [Type, Primary Id, Secondary Type, Secondary ID, Slot]
    /// All Entries have at least Primary Type and Id.
    /// From these five populated values, we can effectively generate the entire dependency tree downwards,
    /// and these five values can be generated from any child file via XivDependencyGraph::GetDependencyRoot(internalFilePath)
    /// 
    /// This class wraps the child Info class with some additional sanity checks, via creation through the cache/dependency graph.
    /// A successful creation through those functions should always guarantee a valid DependencyRoot object, which can
    /// fully resolve all of its constituent parts.
    /// 
    /// Likewise, this class can be turned into an IItem with a generic name via the .ToItem() function.
    /// </summary>
    public class XivDependencyRoot
    {
        /// <summary>
        /// The actual relevant datapoints.
        /// </summary>
        public readonly XivDependencyRootInfo Info;

        // sPrefix => sId (or Primary if Secondary not available)
        private static readonly string ImcFileFormat = "{0}{1}.imc";

        public XivDependencyRoot(XivItemType type, int pid, XivItemType? secondaryType = null, int? sid = null, string slot = null) : this(new XivDependencyRootInfo()
        {
            PrimaryType = type,
            Slot = slot,
            PrimaryId = pid,
            SecondaryType = secondaryType,
            SecondaryId = sid
        })
        {
        }
        public XivDependencyRoot(XivDependencyRootInfo info)
        {
            Info = info;


            // Exception handling time!
            // These item subtypes at root level only have one slot
            // it's only at the Material level they're allowed to have other "slots", 
            // and those are simply defined by the name references in the MDL files.

            // Essentially, they're cross-referenced materials that don't actually have a parent tree, so they
            // should belong to the base tree for those item types.
            if (Info.PrimaryType == XivItemType.human)
            {
                if (Info.SecondaryType == XivItemType.face)
                {
                    Info.Slot = "fac";
                }
                else if (Info.SecondaryType == XivItemType.ear)
                {
                    Info.Slot = "zer";
                }
                else if (Info.SecondaryType == XivItemType.tail)
                {
                    Info.Slot = "til";
                }
                else if (Info.SecondaryType == XivItemType.hair)
                {
                    Info.Slot = "hir";
                } else if(Info.Slot == null)
                {
                    // Kind of a hack, but works to keep the tree together.
                    // Skin materials/textures don't have a slot associated, because they're used by all slots, so
                    // initial crawls up the tree are janky.
                    Info.Slot = "top";
                }
            } else if(Info.PrimaryType == XivItemType.indoor || Info.PrimaryType == XivItemType.outdoor)
            {
                // No slots here!
                Info.Slot = null;
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
        public override string ToString()
        {
            return Info.ToString();
        }
        public override int GetHashCode()
        {
            return Info.ToString().GetHashCode();
        }


        /// <summary>
        /// Gets all the model files in this dependency chain.
        /// </summary>
        /// <returns></returns>
        public async Task<List<string>> GetModelFiles()
        {
            // Some chains have no meta entries, and jump straight to models.
            // Try to resolve Meta files first.
            if (Info.PrimaryType == XivItemType.equipment || Info.PrimaryType == XivItemType.accessory)
            {
                var _eqp = new Eqp(XivCache.GameInfo.GameDirectory);
                var races = await _eqp.GetAvailableRacialModels(Info.PrimaryId, Info.Slot);
                var models = new List<string>();
                foreach(var race in races)
                {
                    models.Add(Info.GetRootFolder() + "model/" + Info.GetRacialModelName(race));
                }
                return models;
            } else if(Info.PrimaryType == XivItemType.indoor || Info.PrimaryType == XivItemType.outdoor)
            {
                var _housing = new Housing(XivCache.GameInfo.GameDirectory, XivCache.GameInfo.GameLanguage);
                var housingAssets = await _housing.GetFurnitureModelParts(Info.PrimaryId, Info.PrimaryType);
                return housingAssets.Select(x => x.Value).ToList();
            } else {
                // The rest of the types just have a single, calculateable model path.
                var folder = Info.GetRootFolder();
                var modelPath = folder + "model/" + Info.GetSimpleModelName();
                return new List<string>() { modelPath };
            }

        }

        private static readonly Regex _materialSetRegex = new Regex("v[0-9]{4}");

        /// <summary>
        /// Gets all the unique material files in this depency chain.
        /// Subsets of this data may be accessed with XivDependencyGraph::GetChildFiles(internalFilePath).
        /// </summary>
        /// <returns></returns>
        public async Task<List<string>> GetMaterialFiles(int materialVariant = -1)
        {

            var materials = new HashSet<string>();
            if (Info.PrimaryType == XivItemType.human && Info.SecondaryType == XivItemType.body)
            {
                // Bleargh.  So here's the exception of exception class.  Because the "models" in human body are 
                // are so sparse and all over the place, relying on them is impossible.  Thankfully, body types only ever
                // have one vanilla material.
                var primary = Info.PrimaryId.ToString().PadLeft(4, '0');
                var body = Info.SecondaryId.ToString().PadLeft(4, '0');
                var path= $"chara/human/c{primary}/obj/body/b{body}/material/v0001/mt_c{primary}b{body}_a.mtrl";

                // Just validate it exists and call it a day.
                var index = new Index(XivCache.GameInfo.GameDirectory);
                var exists = await index.FileExists(path);
                if(exists)
                {
                    materials.Add(path);
                }

                materialVariant = 1;
            }
            else
            {
                var models = await GetModelFiles();
                if (models != null && models.Count > 0)
                {
                    var dataFile = IOUtil.GetDataFileFromPath(models[0]);
                    var _mdl = new Mdl(XivCache.GameInfo.GameDirectory, dataFile);

                    foreach (var model in models)
                    {
                        var mdlMats = await XivCache.GetChildFiles(model);
                        if (materialVariant < 0)
                        {
                            foreach (var mat in mdlMats)
                            {
                                materials.Add(mat);
                            }
                        }
                        else
                        {
                            var replacement = "v" + materialVariant.ToString().PadLeft(4, '0');
                            foreach (var mat in mdlMats)
                            {
                                // Replace any material set references with the new one.
                                // The hash set will scrub us down to just a single copy.
                                // This is faster than re-scanning the MDL file.
                                // And a little more thorough than simply skipping over non-matching refs.
                                // Since some materials may not have variant references.
                                materials.Add(_materialSetRegex.Replace(mat, replacement));
                            }
                        }
                    }
                }
            }

            // Here we get to get a little fancy.
            var _modding = new Modding(XivCache.GameInfo.GameDirectory);
            var modList = _modding.GetModList();
            var rootFolder = Info.GetRootFolder();
            var variantRep = "v" + materialVariant.ToString().PadLeft(4, '0');
            foreach (var mod in modList.Mods)
            {
                if (!mod.enabled) continue;

                // We need to get all of the modded materials in this root, even if they're
                // orphaned materials.
                if(mod.fullPath.StartsWith(rootFolder) && mod.fullPath.EndsWith(".mtrl"))
                {
                    if (Info.Slot == null || mod.fullPath.Contains(Info.Slot))
                    {
                        var material = mod.fullPath;
                        if (materialVariant >= 0)
                        {
                            materials.Add(_materialSetRegex.Replace(material, variantRep));
                        }
                        else
                        {
                            materials.Add(material);
                        }
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
        public async Task<List<string>> GetTextureFiles(int materialVariant = -1)
        {
            var materials = await GetMaterialFiles(materialVariant);
            var textures = new HashSet<string>();
            if (materials != null && materials.Count > 0)
            {
                foreach (var mat in materials)
                {
                    var mtrlTexs = await XivCache.GetChildFiles(mat);
                    foreach (var tex in mtrlTexs)
                    {
                        textures.Add(tex);
                    }
                }
            }
            return textures.ToList();
        }


        public string GetRawImcFilePath()
        {
            var imcPath = "";
            if(Info.PrimaryType == XivItemType.indoor || Info.PrimaryType == XivItemType.outdoor)
            {
                // These types can never have IMC entries.
                return null;
            }
                

            if (Info.SecondaryType == null)
            {
                var iPrefix = XivItemTypes.GetSystemPrefix(Info.PrimaryType);
                var iId = Info.PrimaryId.ToString().PadLeft(4, '0');
                imcPath = Info.GetRootFolder() + String.Format(ImcFileFormat, new string[] { iPrefix, iId });
            }
            else
            {
                var iPrefix = XivItemTypes.GetSystemPrefix((XivItemType)Info.SecondaryType);
                var iId = Info.SecondaryId.ToString().PadLeft(4, '0');
                imcPath = Info.GetRootFolder() + String.Format(ImcFileFormat, new string[] { iPrefix, iId });
            }
            return imcPath;

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

            var imcPath = GetRawImcFilePath();

            if(String.IsNullOrEmpty(imcPath))
            {
                return imcEntries;
            }


            var _gameDirectory = XivCache.GameInfo.GameDirectory;
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
            if(Info.Slot != null && Imc.SlotOffsetDictionary.ContainsKey(Info.Slot))
            {
                subOffset = Imc.SlotOffsetDictionary[Info.Slot] * subEntrySize;
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
        /// Returns a raw item entry for this root.  Uses generic name, and does not contain an EXD row reference.
        /// </summary>
        /// <param name="DefaultImcSubset"></param>
        /// <returns></returns>
        public IItemModel ToRawItem(int defaultImcSubset = 0)
        {
            // If we couldn't find an item in the DB, or we failed to properly generate the item entry, just generate a default one.
            switch (Info.PrimaryType)
            {
                case XivItemType.equipment:
                case XivItemType.accessory:
                case XivItemType.weapon:
                    return XivGear.FromDependencyRoot(this, defaultImcSubset);
                case XivItemType.demihuman:
                case XivItemType.monster:
                    return XivMount.FromDependencyRoot(this, defaultImcSubset);
                case XivItemType.indoor:
                case XivItemType.outdoor:
                case XivItemType.furniture:
                    return XivFurniture.FromDependencyRoot(this);
                case XivItemType.human:
                    return XivCharacter.FromDependencyRoot(this);
            }
            return XivGenericItemModel.FromDependencyRoot(this, defaultImcSubset);
        }
        /// <summary>
        /// Creates and returns an IIteModel instance based on this root's information.
        /// This will match the IItem entry available in the main item lists, if an item exists for this root.
        /// Otherwise, it will be a generically named one.
        /// </summary>
        /// <returns></returns>
        public IItemModel GetFirstItem(int defaultImcSubset = 0)
        {
            using (var db = new SQLiteConnection(XivCache.CacheConnectionString))
            {
                db.Open();
                var rootString = Info.ToString();

                if(Info.PrimaryType == XivItemType.equipment || Info.PrimaryType == XivItemType.accessory || Info.PrimaryType == XivItemType.weapon)
                {
                    var query = "select * from items where root = $root order by name asc;";
                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        cmd.Parameters.AddWithValue("root", rootString);

                        using (var reader = new CacheReader(cmd.ExecuteReader()))
                        {
                            if (reader.NextRow())
                            {
                                // Found one.
                                return XivCache.MakeGear(reader);
                            }
                        }
                    }
                } else if(Info.PrimaryType == XivItemType.demihuman || Info.PrimaryType == XivItemType.monster)
                {
                    var query = "select * from monsters where root = $root order by name asc;";
                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        cmd.Parameters.AddWithValue("root", rootString);

                        using (var reader = new CacheReader(cmd.ExecuteReader()))
                        {
                            if (reader.NextRow())
                            {
                                // Found one.
                                return XivCache.MakeMonster(reader);
                            }
                        }
                    }

                } else if(Info.PrimaryType == XivItemType.furniture || Info.PrimaryType == XivItemType.indoor || Info.PrimaryType == XivItemType.outdoor)
                {
                    var query = "select * from furniture where root = $root order by name asc;";
                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        cmd.Parameters.AddWithValue("root", rootString);

                        using (var reader = new CacheReader(cmd.ExecuteReader()))
                        {
                            if (reader.NextRow())
                            {
                                // Found one.
                                return XivCache.MakeFurniture(reader);
                            }
                        }
                    }
                }
            }
            return ToRawItem(defaultImcSubset);

        }

        /// <summary>
        /// Retrieves all items in this root, including NPC items.
        /// If an IMC subset is supplied, the list is filtered to that subset before being returned.
        /// </summary>
        /// <param name="imcSubset"></param>
        /// <returns></returns>
        public async Task<List<IItemModel>> GetAllItems(int imcSubset = -1)
        {

            var items = new List<IItemModel>();
            using (var db = new SQLiteConnection(XivCache.CacheConnectionString))
            {
                db.Open();
                var rootString = Info.ToString();

                if (Info.PrimaryType == XivItemType.equipment || Info.PrimaryType == XivItemType.accessory || Info.PrimaryType == XivItemType.weapon)
                {
                    var query = "select * from items where root = $root order by name asc;";
                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        cmd.Parameters.AddWithValue("root", rootString);

                        using (var reader = new CacheReader(cmd.ExecuteReader()))
                        {
                            while(reader.NextRow())
                            {
                                // Found one.
                                items.Add(XivCache.MakeGear(reader));
                            }
                        }
                    }
                }
                else if (Info.PrimaryType == XivItemType.demihuman || Info.PrimaryType == XivItemType.monster)
                {
                    var query = "select * from monsters where root = $root order by name asc;";
                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        cmd.Parameters.AddWithValue("root", rootString);

                        using (var reader = new CacheReader(cmd.ExecuteReader()))
                        {
                            while (reader.NextRow())
                            {
                                // Found one.
                                items.Add(XivCache.MakeMonster(reader));
                            }
                        }
                    }
                }
                else if (Info.PrimaryType == XivItemType.furniture || Info.PrimaryType == XivItemType.indoor || Info.PrimaryType == XivItemType.outdoor)
                {
                    var query = "select * from furniture where root = $root order by name asc;";
                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        cmd.Parameters.AddWithValue("root", rootString);

                        using (var reader = new CacheReader(cmd.ExecuteReader()))
                        {
                            while (reader.NextRow())
                            {
                                // Found one.
                                items.Add(XivCache.MakeFurniture(reader));
                            }
                        }
                    }
                }
            }

            /// For these types we also want to read their IMC file to fill in any missing NPC only versions.
            if (Info.PrimaryType == XivItemType.equipment || Info.PrimaryType == XivItemType.accessory || Info.PrimaryType == XivItemType.weapon)
            {
                var imc = new Imc(XivCache.GameInfo.GameDirectory);
                var imcPaths = await GetImcEntryPaths();
                var imcEntries = await imc.GetEntries(imcPaths);

                // Need to verify all of our IMC sets are properly represented in the item list.
                for (int i = 0; i <= imcEntries.Count; i++)
                {
                    // Already in it.  All set.
                    if (items.Any(x => x.ModelInfo.ImcSubsetID == i)) continue;

                    // Need to create a new item for it.
                    var npcItem = ToRawItem(i);
                    items.Add(npcItem);
                }
            }

            if(imcSubset >= 0)
            {
                items = items.Where(x => x.ModelInfo.ImcSubsetID == imcSubset).ToList();
            }

            if (items.Count == 0) {
                // May as well make a raw item.
                items.Add(ToRawItem());
            }

            items = items.OrderBy(x => x.Name, new ItemNameComparer()).ToList();

            return items;
        }
    }

    /// <summary>
    /// File dependency tree crawler for internal files.
    /// This class is automatically spawned and made available by the XivCache class,
    /// and should generally be referenced from there, not directly instantiated otherwise.
    /// </summary>
    internal static class XivDependencyGraph
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


        /// <summary>
        /// The groupings of the files in the dependency tree based on their level.
        /// </summary>
        public static readonly Dictionary<XivDependencyLevel, List<XivDependencyFileType>> DependencyLevelGroups = new Dictionary<XivDependencyLevel, List<XivDependencyFileType>>()
        {
            { XivDependencyLevel.Root, new List<XivDependencyFileType>() { XivDependencyFileType.root, XivDependencyFileType.meta, XivDependencyFileType.eqp, XivDependencyFileType.eqdp, XivDependencyFileType.imc } },
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
            XivItemType.indoor,
            XivItemType.outdoor,
        };

        // Captures the file extension of a file (even if it has a binary extension)
        private static readonly Regex _extensionRegex = new Regex(".*\\.([a-z]+)(" + Constants.BinaryOffsetMarker + "[0-9]+)?$");

        // Captures the slot of a file (even if it has a binary extension)
        private static readonly Regex _slotRegex = new Regex("[a-z][0-9]{4}(?:[a-z][0-9]{4})?_([a-z]{3})(?:_.+\\.|\\.)[a-z]+(?:" + Constants.BinaryOffsetMarker + "[0-9]+)?$");

        // Captures the binary offset of a file.
        private static readonly Regex _binaryOffsetRegex = new Regex(Constants.BinaryOffsetMarker + "([0-9]+)$");

        // Regex for identifying EQP files and extracting the offset.
        private static readonly Regex EqpRegex = new Regex("^chara\\/xls\\/equipmentparameter\\/equipmentparameter\\.eqp" + Constants.BinaryOffsetMarker + "([0-9]+)$");

        // Regex for identifying EQDP files and extracting the type and offset.
        private static readonly Regex EqdpRegex = new Regex("^chara\\/xls\\/charadb\\/(equipment|accessory)deformerparameter\\/c[0-9]{4}\\.eqdp" + Constants.BinaryOffsetMarker + "([0-9]+)$");

        private static readonly Regex MtrlRegex = new Regex("^.*\\.mtrl$");

        // Group 0 == Full File path
        // Group 1 == Root Folder
        // Group 2 == PrimaryId
        // Group 3 == SecondaryId (if it exists)

        private static readonly Regex PrimaryExtractionRegex = new Regex("^chara\\/([a-z]+)\\/[a-z]([0-9]{4})(?:\\/obj\\/([a-z]+)\\/[a-z]([0-9]{4})\\/?)?.*$");


        // Group 0 == Full File path
        // Group 1 == Type (indoor/outdoor)
        // Group 2 == Primary Id
        private static readonly Regex HousingExtractionRegex = new Regex("^bgcommon/hou/([a-z]+)/general/([0-9]+)/?.*$");

        /// <summary>
        /// Returns all parent files that this child file depends on as part of its rendering process.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public static async Task<List<string>> GetParentFiles(string internalFilePath)
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


            var roots = (await XivCache.GetDependencyRoots(internalFilePath));

            if(roots.Count == 0)
            {
                // No root? No parents.
                return new List<string>();
            }


            if(level == XivDependencyLevel.Model)
            {
                // Our parent is just our dependency root.
                var root = roots[0];
                return new List<string>() { root.ToString() };


            } else if(level == XivDependencyLevel.Material)
            {
                var parents = new HashSet<string>();

                // For all our roots (Really just 1)
                foreach (var root in roots)
                {
                    // Get all models in this depedency tree.
                    var models = await root.GetModelFiles();
                    foreach (var model in models)
                    {
                        // And make sure their child files are fully cached.
                        var materials = await XivCache.GetChildFiles(model);
                    }
                }

                // Now we can go to the cache and pull all of our potential parents directly.
                var cachedParents = await XivCache.GetCacheParents(internalFilePath);
                foreach(var p in cachedParents)
                {
                    parents.Add(p);
                }

                return parents.ToList();
                
            } else if(level == XivDependencyLevel.Texture)
            {
                var parents = new HashSet<string>();

                // So, textures are the fun case where it's possible for them to have multiple roots.
                foreach (var root in roots)
                {
                    // Get all the materials in this dependency tree.
                    var materials = await root.GetMaterialFiles();
                    foreach(var mat in materials)
                    {
                        // And make sure their child files are fully cached.
                        var textures = await XivCache.GetChildFiles(mat);
                    }
                }

                // Now we can go to the cache and pull all of our potential parents directly.
                var cachedParents = await XivCache.GetCacheParents(internalFilePath);
                foreach (var p in cachedParents)
                {
                    parents.Add(p);
                }

                return parents.ToList();
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
        public static async Task<List<string>> GetSiblingFiles(string internalFilePath)
        {
            var parents = await GetParentFiles(internalFilePath);
            var siblings = new HashSet<string>();
            foreach(var p in parents)
            {
                var children = await XivCache.GetChildFiles(p);
                foreach(var c in children)
                {
                    siblings.Add(c);
                }
            }

            if(siblings.Count == 0)
            {
                siblings.Add(internalFilePath);
            }

            return siblings.ToList();
        }

        /// <summary>
        /// Returns all child files that depend on the given parent file path as part of their
        /// rendering process.
        /// 
        /// This function is the primary workhorse for generating child files - this function
        /// should *not* rely on any other cache/etc. information to work.  Just the file path
        /// and the indexes/dats.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public static async Task<List<string>> GetChildFiles(string internalFilePath)
        {

            var level = GetDependencyLevel(internalFilePath);
            if (level == XivDependencyLevel.Invalid)
            {
                return new List<string>();
            }

            if (level == XivDependencyLevel.Root)
            {
                // Root evaluations of root paths should never return anything other than a single,
                // valid root entry, but might as well null check it to be safe.
                var root = await XivCache.GetFirstRoot(internalFilePath);
                if (root == null) return null;

                return await root.GetModelFiles();
            }

            if (level == XivDependencyLevel.Model)
            {
                var dataFile = IOUtil.GetDataFileFromPath(internalFilePath);
                try
                {
                    var _mdl = new Mdl(XivCache.GameInfo.GameDirectory, dataFile);
                    var mdlChildren = await _mdl.GetReferencedMaterialPaths(internalFilePath, -1, false, false);
                    return mdlChildren;
                } catch
                {
                    // It's possible this model doesn't actually exist, or is corrupt, in which case, return empty.
                    return new List<string>();
                }

            } else if (level == XivDependencyLevel.Material)
            {
                try
                {
                    var dataFile = IOUtil.GetDataFileFromPath(internalFilePath);
                    var _mtrl = new Mtrl(XivCache.GameInfo.GameDirectory, dataFile, XivCache.GameInfo.GameLanguage);
                    var mtrlChildren = await _mtrl.GetTexturePathsFromMtrlPath(internalFilePath, false, false);
                    return mtrlChildren;
                } catch
                {
                    // It's possible this material doesn't actually exist, or is corrupt, in which case, return empty.
                    return new List<string>();
                }

            } else
            {
                // Textures have no child files.
                return new List<string>();
            }
        }

        /// <summary>
        /// Retrieves the dependency file type of a given file in the system.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public static XivDependencyFileType GetDependencyFileType(string internalFilePath)
        {
            if(internalFilePath == null)
                return XivDependencyFileType.invalid;

            var match = _extensionRegex.Match(internalFilePath);
            if (!match.Success)
            {
                // This is a folder or some file without an extension.
                return XivDependencyFileType.invalid;
            }
            var suffix = match.Groups[1].Value;
            switch (suffix)
            {
                case "root":
                    return XivDependencyFileType.root;
                case "meta":
                    return XivDependencyFileType.meta;
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
        public static XivDependencyLevel GetDependencyLevel(string internalFilePath)
        {
            if(String.IsNullOrWhiteSpace(internalFilePath))
            {
                return XivDependencyLevel.Invalid;
            }

            var fileType = GetDependencyFileType(internalFilePath);
            if(fileType == XivDependencyFileType.invalid)
            {
                return XivDependencyLevel.Invalid;
            }

            return DependencyLevelGroups.First(x => x.Value.Contains(fileType)).Key;
        }


        public static XivDependencyRoot CreateDependencyRoot(XivItemType primaryType, int primaryId, XivItemType? secondaryType = null, int? secondaryId = null, string slot = null)
        {
            var info = new XivDependencyRootInfo()
            {
                PrimaryType = primaryType,
                Slot = slot,
                PrimaryId = primaryId,
                SecondaryType = secondaryType,
                SecondaryId = secondaryId
            };
            return CreateDependencyRoot(info);
        }

        /// <summary>
        /// Creates the depenency root for an item from constituent parts.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="primaryId"></param>
        /// <param name="secondaryId"></param>
        /// <param name="slot"></param>
        /// <returns></returns>
        public static XivDependencyRoot CreateDependencyRoot(XivDependencyRootInfo info)
        {
            if(!DependencySupportedTypes.Contains(info.PrimaryType) || info.PrimaryId < 0)
            {
                return null;
            }

            if (info.Slot == null)
            {
                // Safety checks.  Custom-name textures can often end up with set being resolvable
                // but slot non-resolvable.  Either way it's irrelevant, as 
                // they'll have their root resolved via modlist, if one exists for them.
                if (info.PrimaryType == XivItemType.equipment
                    || info.PrimaryType == XivItemType.accessory
                    || info.PrimaryType == XivItemType.demihuman)
                {
                        return null;
                }
            }

            // Only these types can get away without a secondary type.
            if(info.SecondaryType == null) {
                if (info.PrimaryType != XivItemType.equipment && info.PrimaryType != XivItemType.accessory && info.PrimaryType != XivItemType.indoor && info.PrimaryType != XivItemType.outdoor) {
                    return null;
                }
            }

            return new XivDependencyRoot(info);

        }


        /// <summary>
        /// This crawls the cache to find what files refer to the file in question.
        /// The cache is not guaranteed to be exhaustive with regards to default files, so essentially
        /// this covers cross-root-referential files.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        private static async Task<List<XivDependencyRoot>> GetModdedRoots(string internalFilePath)
        {

            var parents = new List<string>();
            var uniqueRoots = new HashSet<XivDependencyRoot>();

            // This is a rare instance where we want to access the cache directly, because we need to do a bit of a strange query.
            // We specifically just want to know what cached files have us listed as childern; not what we have in the 
            // parents dependencies cache.
            var wc = new WhereClause() { Column = "child", Comparer = WhereClause.ComparisonType.Equal, Value = internalFilePath };
            var cachedParents = await XivCache.BuildListFromTable(XivCache.CacheConnectionString, "dependencies_children", wc, async (reader) =>
            {
                return reader.GetString("parent");
            });

            foreach (var file in cachedParents)
            {
                var matRoots = await XivCache.GetDependencyRoots(file);
                foreach(var mat in matRoots)
                {
                    uniqueRoots.Add(mat);
                }
            }

            return uniqueRoots.ToList();
        }

        /// <summary>
        /// Extracts dependency root info from purely a file name.  This is primarily useful when looking at 
        /// .mtrl files, where we want to find what actual folder they're contained in based upon their name reference
        /// in a .mdl file.
        /// </summary>
        /// <param name="filenameWithoutExtension"></param>
        /// <returns></returns>
        public static XivDependencyRootInfo ExtractRootInfoFilenameOnly(string filenameWithoutExtension)
        {
            var regex = new Regex("([a-z])([0-9]{4})([a-z])([0-9]{4})");
            var match = regex.Match(filenameWithoutExtension);
            if(!match.Success)
            {
                return new XivDependencyRootInfo();
            }

            var primaryPrefix = match.Groups[1].Value;
            var primaryId = Int32.Parse(match.Groups[2].Value);
            var secondaryPrefix = match.Groups[3].Value;
            var secondaryId = Int32.Parse(match.Groups[4].Value);

            var root = new XivDependencyRootInfo();

            root.PrimaryType = XivItemTypes.FromSystemPrefix(primaryPrefix[0]);
            root.PrimaryId = primaryId;
            root.SecondaryType = XivItemTypes.FromSystemPrefix(secondaryPrefix[0]);
            root.SecondaryId = secondaryId;

            if ((root.SecondaryType == XivItemType.equipment || root.SecondaryType == XivItemType.accessory)&& root.PrimaryType == XivItemType.human)
            {
                // Flip flop time for these.
                root.SecondaryType = null;
                root.SecondaryId = null;
                root.PrimaryType = XivItemTypes.FromSystemPrefix(secondaryPrefix[0]);
                root.PrimaryId = secondaryId;
            }

            return root;
        }

        /// <summary>
        /// Extracts the various import information pieces from an internal path.
        /// This can be used to construct a Root Node -- *If* the information is fully qualified and valid.
        /// Boolean return indicates if the information is fully qualified.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <param name="PrimaryType"></param>
        /// <param name="PrimaryId"></param>
        /// <param name="SecondaryType"></param>
        /// <param name="SecondaryId"></param>
        /// <param name="Slot"></param>
        /// <returns></returns>
        public static XivDependencyRootInfo ExtractRootInfo(string internalFilePath)
        {

            XivDependencyRootInfo info = new XivDependencyRootInfo();

            info.PrimaryType = XivItemType.unknown;
            info.PrimaryId = -1;

            // Anything that lives in an extractable root folder is considered a child of that root.
            var match = PrimaryExtractionRegex.Match(internalFilePath);
            if (match.Success)
            {
                info.PrimaryType = XivItemTypes.FromSystemName(match.Groups[1].Value);
                info.PrimaryId = Int32.Parse(match.Groups[2].Value);
                if (match.Groups[3].Success)
                {
                    info.SecondaryType = XivItemTypes.FromSystemName(match.Groups[3].Value);
                    info.SecondaryId = Int32.Parse(match.Groups[4].Value);
                }

                // Then get the slot if we have one.
                match = _slotRegex.Match(internalFilePath);
                if (match.Success)
                {
                    info.Slot = match.Groups[1].Value;
                }
            }
            else
            {
                // Might be a housing item.
                match = HousingExtractionRegex.Match(internalFilePath);
                if (match.Success)
                {
                    info.PrimaryType = XivItemTypes.FromSystemName(match.Groups[1].Value);
                    info.PrimaryId = Int32.Parse(match.Groups[2].Value);
                }
            }

            return info;
        }

        /// <summary>
        /// Resolves the dependency roots for a given child file of any file type.
        /// For Model, Meta, and Material files this will always be exactly one root (or 0).
        /// For Textures this can be more than one.
        /// 
        ///     - TECHNICALLY some Materials can be cross-root referenced in some item categories.
        ///     - Specifically in the Human group and Furniture group.
        ///     - These cases are just not supported in the graph because trying to identify them all is
        ///     - *Exceptionally* costly or effectively impossible.  As such, upward tree traversals
        ///     - for materials in those categories may be incomplete.
        /// 
        /// A return value of 0 length indicates that this file is orphaned, or lives in a directory
        /// with no calculable root info ( ex. chara/shared )
        /// 
        /// NOTE - Dependency Roots for TEXTURES cannot be 100% populated
        /// without a fully populated cache of mod file children.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public static async Task<List<XivDependencyRoot>> GetDependencyRoots(string internalFilePath)
        {
            var roots = new HashSet<XivDependencyRoot>();

            // Tex files require special handling, because they can be referenced by modded items
            // outside of their own dependency chain potentially.
            var match = _extensionRegex.Match(internalFilePath);
            var suffix = "";
            if(match.Success)
            {
                suffix = match.Groups[1].Value;
            }
            if(suffix == "tex")
            {
                // Oh boy.  Here we have to scrape the cache to find any custom modded roots(plural) we may have.
                var customRoots = await GetModdedRoots(internalFilePath);
                foreach(var r in customRoots)
                {
                    roots.Add(r);
                }
            }

            var info = ExtractRootInfo(internalFilePath);
            var root = CreateDependencyRoot(info);
            if(root != null)
            {
                roots.Add(root);
            }


            if(roots.Count == 0)
            {
                // See if this is a binary offset file.
                match = _binaryOffsetRegex.Match(internalFilePath);
                if(match.Success)
                {
                    long offset = Int64.Parse(match.Groups[1].Value);

                    if (suffix == "imc")
                    {
                        // IMCs mostly parse out correctly above, but need to have slot information pulled from their binary offset.
                        const int ImcHeaderSize = 4;
                        const int ImcSubEntrySize = 6;
                        const int IMcFullEntrySize = 30;

                        offset /= 8;
                        offset -= ImcHeaderSize;
                        offset = offset % IMcFullEntrySize;
                        var index = offset / ImcSubEntrySize;
                        if (info.PrimaryType == XivItemType.equipment || info.PrimaryType == XivItemType.demihuman)
                        {
                            info.Slot = Imc.EquipmentSlotOffsetDictionary.First(x => x.Value == index).Key;
                        }
                        else  // accesory
                        {
                            info.Slot = Imc.AccessorySlotOffsetDictionary.First(x => x.Value == index).Key;
                        }

                        root = CreateDependencyRoot(info);
                        if (root != null)
                        {
                            roots.Add(root);
                        }
                    }
                    else if (suffix == "eqp")
                    {
                        var entrySize = Eqp.EquipmentParameterEntrySize * 8;
                        var setId = (int)(offset / entrySize);

                        var subOffset = (offset % entrySize) / 8;
                        var slot = EquipmentParameterSet.EntryOffsets.First(x => x.Value == subOffset).Key;

                        root = CreateDependencyRoot(XivItemType.equipment, setId, null, null, slot);
                        if (root != null)
                        {
                            roots.Add(root);
                        }

                    } else if(suffix == "eqdp")
                    {
                        // For EQDPs, this is 16 bits per set, then within that, we can determine the slot based on the offset internally within the full set.
                        match = EqdpRegex.Match(internalFilePath);
                        if (match.Success)
                        {
                            var type = (XivItemType)Enum.Parse(typeof(XivItemType), match.Groups[1].Value);
                            var entrySize = Eqp.EquipmentDeformerParameterEntrySize * 8;
                            var setId = (int)(offset / entrySize);


                            var slotOffset = offset % entrySize;
                            var slotIndex = (int)(slotOffset / 2); // 2 Bits per slot.

                            var slots = EquipmentDeformationParameterSet.SlotsAsList(type == XivItemType.accessory);
                            var slot = slots[slotIndex];

                            // Ok, now we have everything we need to create the root object.
                            return new List<XivDependencyRoot>() { CreateDependencyRoot(type, setId, null, null, slot) };

                        }

                    }

                }
            }

            return roots.ToList();
        }


        /// <summary>
        /// Tests all roots of the given type for existence.
        /// This is an o(10,000 * 10,000) operation. Needless to say, it is very slow
        /// and should never be run during the course of normal operation.
        /// </summary>
        /// <param name="combinedHashes"></param>
        /// <param name="primary"></param>
        /// <param name="secondary"></param>
        /// <returns></returns>
        private static async Task<List<XivDependencyRootInfo>> TestAllRoots(Dictionary<string, XivDependencyRootInfo> combinedHashes, XivItemType primary, XivItemType secondary) {


            var result = new List<XivDependencyRootInfo>(3000);
            await Task.Run(() => {
                try
                {
                    Console.WriteLine("Starting Search for type: " + primary.ToString() + " " + secondary.ToString());
                    var root = new XivDependencyRootInfo();
                    root.PrimaryType = primary;
                    root.SecondaryType = (secondary == XivItemType.none ? null : (XivItemType?) secondary);
                    var eqp = new Eqp(XivCache.GameInfo.GameDirectory);
                    var races = (XivRace[])Enum.GetValues(typeof(XivRace));

                    for (int p = 0; p < 10000; p++)
                    {
                        root.PrimaryId = p;

                        if (secondary == XivItemType.none)
                        {
                            if (primary == XivItemType.indoor || primary == XivItemType.outdoor)
                            {
                                // For furniture, they're valid as long as they have an SGD file we can find.
                                var folder = root.GetRootFolder() + "asset";
                                var file = root.GetSgdName();

                                var folderHash = HashGenerator.GetHash(folder);
                                var fileHash = HashGenerator.GetHash(file);
                                var key = fileHash.ToString() + folderHash.ToString();
                                if (combinedHashes.ContainsKey(key))
                                {
                                    result.Add((XivDependencyRootInfo)root.Clone());
                                }
                            }
                            else
                            {
                                var folder = root.GetRootFolder() + "model";
                                var folderHash = HashGenerator.GetHash(folder);
                                var slots = XivItemTypes.GetAvailableSlots(root.PrimaryType);

                                // For these, just let the EDP module verify if there are any races availble for the item?
                                foreach (var slot in slots)
                                {
                                    root.Slot = slot;

                                    // Check every possible race code, not just playables?
                                    //for (int s = 0; s < 10000; s++)
                                    foreach (var race in races)
                                    {
                                        //var modelName = root.GetRacialModelName(s);
                                        var modelName = root.GetRacialModelName(race);
                                        var fileHash = HashGenerator.GetHash(modelName);
                                        var key = fileHash.ToString() + folderHash.ToString();
                                        if (combinedHashes.ContainsKey(key))
                                        {
                                            result.Add((XivDependencyRootInfo)root.Clone());

                                            // We don't care how many models there are, just that there *are* any models.
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            for (int s = 0; s < 10000; s++)
                            {
                                root.SecondaryId = s;
                                var folder = root.GetRootFolder() + "model";
                                var folderHash = HashGenerator.GetHash(folder);
                                var slots = XivItemTypes.GetAvailableSlots((XivItemType)root.SecondaryType);

                                if(primary == XivItemType.human && secondary == XivItemType.body)
                                {
                                    // Human body gets to be a special snowflake.
                                    slots = XivItemTypes.GetAvailableSlots(XivItemType.equipment);
                                }

                                if (slots.Count == 0)
                                {
                                    slots.Add("");
                                }
                                foreach (var slot in slots)
                                {
                                    if (slot == "")
                                    {
                                        root.Slot = null;
                                    }
                                    else
                                    {
                                        root.Slot = slot;
                                    }
                                    var modelName = root.GetSimpleModelName();
                                    var fileHash = HashGenerator.GetHash(modelName);
                                    var key = fileHash.ToString() + folderHash.ToString();
                                    if (combinedHashes.ContainsKey(key))
                                    {
                                        result.Add((XivDependencyRootInfo)root.Clone());
                                    }
                                }
                            }
                        }
                    }
                } catch(Exception Ex) {
                    Console.WriteLine(Ex.Message);
                    throw;
                }
            });
            Console.WriteLine("Found " + result.Count.ToString() + " Entries for type: " + primary.ToString() + " " + secondary.ToString());
            return result;
        }

        /// <summary>
        /// This is a simple function that rips through the entire index file, for all 9,999 possible
        /// primary and secondary IDs for each possible category, and verifies they have at least one
        /// model file we can identify in their folder.
        /// 
        /// The results are stored in root_cache.db (which is also purged at the start of this function)
        /// </summary>
        /// <returns></returns>
        public static async Task CacheAllRealRoots()
        {
            ResetRootCache();
            var index = new Index(XivCache.GameInfo.GameDirectory);
            var mergedHashes = await index.GetFileDictionary(XivDataFile._04_Chara);

            var mergedDict = new Dictionary<string, XivDependencyRootInfo>();
            foreach (var kv in mergedHashes)
            {
                mergedDict.Add(kv.Key, new XivDependencyRootInfo());
            }

            var bgcHashes = await index.GetFileDictionary(XivDataFile._01_Bgcommon);

            var bgcHashDict = new Dictionary<string, XivDependencyRootInfo>();
            foreach (var kv in bgcHashes)
            {
                bgcHashDict.Add(kv.Key, new XivDependencyRootInfo());
            }

            var types = new Dictionary<XivItemType, List<XivItemType>>();
            foreach (var type in DependencySupportedTypes)
            {
                types.Add(type, new List<XivItemType>());
            }
            types[XivItemType.monster].Add(XivItemType.body);
            types[XivItemType.weapon].Add(XivItemType.body);
            types[XivItemType.human].Add(XivItemType.body);
            types[XivItemType.human].Add(XivItemType.face);
            types[XivItemType.human].Add(XivItemType.hair);
            types[XivItemType.human].Add(XivItemType.tail);
            types[XivItemType.human].Add(XivItemType.ear);
            types[XivItemType.demihuman].Add(XivItemType.equipment);
            types[XivItemType.equipment].Add(XivItemType.none);
            types[XivItemType.accessory].Add(XivItemType.none);
            types[XivItemType.indoor].Add(XivItemType.none);
            types[XivItemType.outdoor].Add(XivItemType.none);



            var tasks = new List<Task<List<XivDependencyRootInfo>>>();
            foreach (var kv in types)
            {
                var primary = kv.Key;
                foreach (var secondary in kv.Value)
                {
                    if (primary == XivItemType.indoor || primary == XivItemType.outdoor)
                    {
                        tasks.Add(TestAllRoots(bgcHashDict, primary, secondary));
                    }
                    else
                    {
                        tasks.Add(TestAllRoots(mergedDict, primary, secondary));
                    }
                }
            }
            try
            {
                await Task.WhenAll(tasks.ToArray());

            } catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }

            Console.WriteLine("Compiling final root list...");
            List<XivDependencyRootInfo> allRoots = new List<XivDependencyRootInfo>();
            foreach(var t in tasks)
            {
                allRoots.AddRange(t.Result);
            }


            Console.WriteLine("Saving all valid roots...");
            using (var db = new SQLiteConnection(RootsCacheConnectionString))
            {
                db.Open();

                using (var transaction = db.BeginTransaction())
                {
                    var query = "insert into roots (primary_type, primary_id, secondary_type, secondary_id, slot, root_path) values ($primary_type, $primary_id, $secondary_type, $secondary_id, $slot, $root_path) on conflict do nothing;";
                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        foreach(var root in allRoots)
                        {
                            XivCache.CacheRoot(root, db, cmd);
                        }
                    }
                    transaction.Commit();
                }
            }
        }

    }
}
