using HelixToolkit.SharpDX.Core;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
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
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.FileTypes;
using xivModdingFramework.Variants.FileTypes;
using static xivModdingFramework.Cache.XivCache;
using Constants = xivModdingFramework.Helpers.Constants;
using Index = xivModdingFramework.SqPack.FileTypes.Index;

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
            if (item == null)
            {
                return null;
            }
            return XivDependencyGraph.CreateDependencyRoot(GetRootInfo(item));
        }
        public static XivDependencyRootInfo GetRootInfo(this IItem item)
        {
            if(item == null)
            {
                return new XivDependencyRootInfo();
            }

            var rootFolder = item.GetItemRootFolder();
            if (string.IsNullOrWhiteSpace(rootFolder))
            {
                return new XivDependencyRootInfo();
            }

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
        public static async Task<List<IItemModel>> GetSharedMaterialItems(this IItemModel item, ModTransaction tx = null)
        {
            var sameModelItems = new List<IItemModel>();
            sameModelItems = await item.GetSharedModelItems();

            try
            {
                var sameMaterialItems = new List<IItemModel>();

                var originalInfo = await Imc.GetImcInfo(item, false, tx);
                foreach (var i in sameModelItems)
                {
                    var info = await Imc.GetImcInfo(i, false, tx);
                    if (info.MaterialSet == originalInfo.MaterialSet)
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
        public static async Task<List<IItemModel>> GetSharedModelItems(this IItemModel item, ModTransaction tx = null)
        {
            var root = item.GetRoot();
            var items = new List<IItemModel>();
            if (root != null) {
                items = await root.GetAllItems(-1, tx);
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
        mdl,
        mtrl,
        tex
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
         * META     - [META] - Binary entries.
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
            { XivDependencyLevel.Root, new List<XivDependencyFileType>() { XivDependencyFileType.root, XivDependencyFileType.meta } },
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
            XivItemType.painting,
            XivItemType.fish,
        };

        // Captures the file extension of a file (even if it has a binary extension)
        private static readonly Regex _extensionRegex = new Regex(".*\\.([a-z]+)(" + Constants.BinaryOffsetMarker + "[0-9]+)?$");

        // Captures the slot of a file (even if it has a binary extension)
        private static readonly Regex _slotRegex = new Regex("[a-z][0-9]{4}(?:[a-z][0-9]{4})?_([a-z]{3})(?:_.+\\.|\\.)[a-z]+(?:" + Constants.BinaryOffsetMarker + "[0-9]+)?$");

        // Captures the binary offset of a file.
        private static readonly Regex _binaryOffsetRegex = new Regex(Constants.BinaryOffsetMarker + "([0-9]+)$");

        private static readonly Regex MtrlRegex = new Regex("^.*\\.mtrl$");

        // Group 0 == Full File path
        // Group 1 == Root Folder
        // Group 2 == PrimaryId
        // Group 3 == SecondaryId (if it exists)

        private static readonly Regex PrimaryExtractionRegex = new Regex("^chara\\/([a-z]+)\\/[a-z]([0-9]{4})(?:\\/obj\\/([a-z]+)\\/[a-z]([0-9]{4})\\/?)?.*$");


        // Group 0 == Full File path
        // Group 1 == Type (indoor/outdoor)
        // Group 2 == Furnishing/Fish/Painting (general/gyo/pic)
        // Group 2 == Primary Id
        private static readonly Regex HousingExtractionRegex = new Regex("^bgcommon\\/hou\\/([a-z]+)\\/([a-z]+)\\/([0-9]+)\\/?.*$");

        // Group 1 == Type (indoor/outdoor)
        // Group 2 == Furnishing/Fish/Painting (general/gyo/pic)
        // Group 2 == Fish/Painting Size (ta/lg/ll/sm/mi)
        // Group 2 == Primary Id
        private static readonly Regex HousingExtractionRegex2 = new Regex("^bgcommon\\/hou\\/([a-z]+)\\/([a-z]+)\\/([a-z]+)\\/([0-9]+)\\/?.*$");
        /// <summary>
        /// Returns all parent files that this child file depends on as part of its rendering process.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public static async Task<List<string>> GetParentFiles(string internalFilePath, ModTransaction tx = null)
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
                    var models = await root.GetModelFiles(tx);
                    foreach (var model in models)
                    {
                        // And make sure their child files are fully cached.
                        var materials = await XivCache.GetChildFiles(model, tx);
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
                    var materials = await root.GetMaterialFiles(-1, tx);
                    foreach(var mat in materials)
                    {
                        // And make sure their child files are fully cached.
                        var textures = await XivCache.GetChildFiles(mat, tx);
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
        public static async Task<List<string>> GetSiblingFiles(string internalFilePath, ModTransaction tx = null)
        {
            var parents = await GetParentFiles(internalFilePath);
            if (parents == null) return null;
            var siblings = new HashSet<string>();
            foreach(var p in parents)
            {
                var children = await XivCache.GetChildFiles(p, tx);
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
        public static async Task<List<string>> GetChildFiles(string internalFilePath, ModTransaction tx = null)
        {
            if(tx == null)
            {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginReadonlyTransaction();
            }

            if(string.IsNullOrEmpty(internalFilePath) || !await tx.FileExists(internalFilePath))
            {
                return new List<string>();
            }

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

                return await root.GetModelFiles(tx);
            }

            if (level == XivDependencyLevel.Model)
            {
                try
                {
                    var mdlChildren = await Mdl.GetReferencedMaterialPaths(internalFilePath, -1, false, false, tx);

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
                    var mtrlChildren = await Mtrl.GetTexturePathsFromMtrlPath(internalFilePath, false, false, tx);
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

        public static List<string> GetOrphans(string filePath)
        {
            return null;
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
            var newRoot = info.Validate();
            
            if (!newRoot.IsValid())
            {
                return null;
            }

            if(!DependencySupportedTypes.Contains(info.PrimaryType) || info.PrimaryId < 0)
            {
                return null;
            }


            return new XivDependencyRoot(newRoot);

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
            var cachedParents = XivCache.BuildListFromTable(XivCache.CacheConnectionString, "dependencies_children", wc, (reader) =>
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
        /// Extracts dependency root info from purely a file name, without modification.
        /// This generates potentially invalid roots, but is very useful for quickly and efficiently
        /// resolving information about MTRLs and MDLs from their filenames.
        /// </summary>
        /// <param name="filenameWithoutExtension"></param>
        /// <returns></returns>
        public static XivDependencyRootInfo ExtractRootInfoFilenameOnly(string filenameWithoutExtension, bool validate = true)
        {
            if(String.IsNullOrEmpty(filenameWithoutExtension))
            {
                return new XivDependencyRootInfo();
            }
            var regex = new Regex("([a-z])([0-9]{4})([a-z])([0-9]{4})_?([a-z]{3})?");
            var match = regex.Match(filenameWithoutExtension);
            if(!match.Success)
            {
                return new XivDependencyRootInfo();
            }

            var primaryPrefix = match.Groups[1].Value;
            var primaryId = Int32.Parse(match.Groups[2].Value);
            var secondaryPrefix = match.Groups[3].Value;
            var secondaryId = Int32.Parse(match.Groups[4].Value);
            string slot = null;

            if(match.Groups.Count > 5)
            {
                slot = match.Groups[5].Value;
            }

            var root = new XivDependencyRootInfo();

            root.PrimaryType = XivItemTypes.FromSystemPrefix(primaryPrefix[0]);
            root.PrimaryId = primaryId;
            root.SecondaryType = XivItemTypes.FromSystemPrefix(secondaryPrefix[0]);
            root.SecondaryId = secondaryId;
            root.Slot = slot;

            if (validate)
            {
                root = root.Validate();
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
                        // Normal indoor/outdoor furnishing.
                    info.PrimaryType = XivItemTypes.FromSystemName(match.Groups[1].Value);
                    info.PrimaryId = Int32.Parse(match.Groups[3].Value);
                }

                match = HousingExtractionRegex2.Match(internalFilePath);
                if (match.Success)
                {

                    var subtype = XivItemTypes.FromSystemName(match.Groups[2].Value);
                    var size = match.Groups[3].Value;
                    info.PrimaryId = Int32.Parse(match.Groups[4].Value);

                    // Fish/Picture
                    info.PrimaryType = subtype;

                    info.SecondaryId = XivFish.StringSizeToInt(size);
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
        public static async Task<List<XivDependencyRoot>> GetDependencyRoots(string internalFilePath, bool firstOnly = false)
        {
            var roots = new HashSet<XivDependencyRoot>();


            // First root should always be the path-extracted root.
            var info = ExtractRootInfo(internalFilePath);
            var root = CreateDependencyRoot(info);
            if(root != null)
            {
                roots.Add(root);
            }

            if(firstOnly && roots.Count > 0)
            {
                return roots.ToList();
            }

            // Tex files require special handling, because they can be referenced by modded items
            // outside of their own dependency chain potentially.
            var match = _extensionRegex.Match(internalFilePath);
            var suffix = "";
            if (match.Success)
            {
                suffix = match.Groups[1].Value;
            }
            if (suffix == "tex")
            {
                // Oh boy.  Here we have to scrape the cache to find any custom modded roots(plural) we may have.
                var customRoots = await GetModdedRoots(internalFilePath);
                foreach (var r in customRoots)
                {
                    roots.Add(r);
                }
            }


            return roots.ToList();
        }


        private static Task<List<XivDependencyRootInfo>> TestAllSubRoots(Dictionary<uint, HashSet<uint>> Hashes, XivDependencyRootInfo root)
        {
            return Task.Run(() =>
            {
                var usesImc = Imc.UsesImc(root);
                var slots = XivItemTypes.GetAvailableSlots(root.SecondaryType.Value);
                if(slots.Count == 0)
                {
                    slots.Add("");
                }
                var result = new List<XivDependencyRootInfo>(5);

                for (int s = 0; s < 10000; s++)
                {
                    root.SecondaryId = s;
                    var folder = root.GetRootFolder();
                    folder = folder.Substring(0, folder.Length - 1);

                    // If their root folder exists (has an IMC entry in it) they're valid.
                    if (usesImc)
                    {
                        // Test to see if the IMC file exists.
                        var folderHash = (uint)HashGenerator.GetHash(folder);
                        var imcName = XivItemTypes.GetSystemPrefix((XivItemType)root.SecondaryType) + root.SecondaryId.ToString().PadLeft(4, '0') + ".imc";
                        var imcHash = (uint)HashGenerator.GetHash(imcName);

                        if (Hashes.ContainsKey(folderHash) && Hashes[folderHash].Contains(imcHash))
                        {
                            foreach (var slot in slots)
                            {
                                var sl = slot == "" ? null : slot;
                                var nRoot = new XivDependencyRootInfo()
                                {
                                    PrimaryId = root.PrimaryId,
                                    PrimaryType = root.PrimaryType,
                                    SecondaryId = root.SecondaryId,
                                    SecondaryType = root.SecondaryType,
                                    Slot = sl
                                };
                                result.Add(nRoot);
                            }
                        }
                    }
                    else if (!usesImc)
                    {

                        var mfolder = folder + "/model";
                        var mfolderHash = (uint)HashGenerator.GetHash(mfolder);
                        var matFolder = folder + "/material";
                        var matFolderHash = (uint)HashGenerator.GetHash(matFolder);
                        var matFolder1 = folder + "/material/v0001";
                        var matFolder1Hash = (uint)HashGenerator.GetHash(matFolder1);
                        var texFolder = folder + "/texture";
                        var texFolderHash = (uint)HashGenerator.GetHash(texFolder);

                        // Things that don't use IMC files are basically only the human tree, which is a complete mess.
                        foreach (var slot in slots)
                        {
                            var sl = slot == "" ? null : slot;
                            var nRoot = new XivDependencyRootInfo()
                            {
                                PrimaryId = root.PrimaryId,
                                PrimaryType = root.PrimaryType,
                                SecondaryId = root.SecondaryId,
                                SecondaryType = root.SecondaryType,
                                Slot = sl
                            };

                            // If they have an MDL or MTRL we can resolve, they're valid.

                            var mdlFile = nRoot.GetBaseFileName(true) + ".mdl";
                            var mdlFileHash = (uint)HashGenerator.GetHash(mdlFile);

                            var mtrlFile = "mt_" + nRoot.GetBaseFileName(true) + "_a.mtrl";
                            if (root.SecondaryType == XivItemType.tail)
                            {
                                // Tail materials don't actually use their slot name, even though their model does,
                                // for whatever reason.
                                mtrlFile = "mt_" + nRoot.GetBaseFileName(false) + "_a.mtrl";
                            }

                            var mtrlFileHash = (uint)HashGenerator.GetHash(mtrlFile);

                            var hasModel = Hashes.ContainsKey(mfolderHash) && Hashes[mfolderHash].Contains(mdlFileHash);
                            var hasMat = Hashes.ContainsKey(matFolderHash) && Hashes[matFolderHash].Contains(mtrlFileHash);
                            var hasMat1 = Hashes.ContainsKey(matFolder1Hash) && Hashes[matFolder1Hash].Contains(mtrlFileHash);
                            var hasTex = Hashes.ContainsKey(texFolderHash);


                            if (hasMat || hasMat1 || hasModel)
                            {
                                if (root.SecondaryType == XivItemType.body)
                                {
                                    var nRoot2 = new XivDependencyRootInfo()
                                    {
                                        PrimaryId = root.PrimaryId,
                                        PrimaryType = root.PrimaryType,
                                        SecondaryId = root.SecondaryId,
                                        SecondaryType = root.SecondaryType,
                                        Slot = null
                                    };
                                    result.Add(nRoot2);
                                }
                                else
                                {
                                    foreach (var slot2 in slots)
                                    {
                                        var sl2 = slot2 == "" ? null : slot2;
                                        var nRoot2 = new XivDependencyRootInfo()
                                        {
                                            PrimaryId = root.PrimaryId,
                                            PrimaryType = root.PrimaryType,
                                            SecondaryId = root.SecondaryId,
                                            SecondaryType = root.SecondaryType,
                                            Slot = sl2
                                        };
                                        result.Add(nRoot2);
                                    }
                                }
                                break;
                            }
                        }
                    }
                }

                if (result.Count > 0)
                {
                    Console.WriteLine(root.PrimaryType.ToString() + "#" + root.PrimaryId + " \t\t" + root.SecondaryType.ToString() + "\t\tFound: " + result.Count);
                }
                if(root.PrimaryId % 100 == 0)
                {

                    Console.WriteLine(root.PrimaryType.ToString() + "#" + root.PrimaryId + " \t\t" + root.SecondaryType.ToString() + "\t\tCompleted.");
                }
                return result;
            });
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
        private static async Task<List<XivDependencyRootInfo>> TestAllRoots(Dictionary<uint, HashSet<uint>> Hashes, XivItemType primary, XivItemType secondary) {


            var result = new List<XivDependencyRootInfo>(3000);
            await Task.Run(async () => {
                try
                {
                    Console.WriteLine("Starting Search for type: " + primary.ToString() + " " + secondary.ToString());
                    var root = new XivDependencyRootInfo();
                    root.PrimaryType = primary;
                    root.SecondaryType = (secondary == XivItemType.none ? null : (XivItemType?) secondary);
                    var eqp = new Eqp();
                    var races = (XivRace[])Enum.GetValues(typeof(XivRace));
                    var slots = XivItemTypes.GetAvailableSlots(primary);
                    if(secondary != XivItemType.none)
                    {
                        slots = XivItemTypes.GetAvailableSlots(secondary);

                        if(primary == XivItemType.human && secondary == XivItemType.body)
                        {
                            slots = XivItemTypes.GetAvailableSlots(XivItemType.equipment);
                            slots.Add("");
                        }
                    }
                    
                    if(slots.Count == 0)
                    {
                        slots.Add("");
                    }

                    var usesImc = Imc.UsesImc(root);

                    var capacity = secondary == XivItemType.none ? 0 : 10000;
                    var tasks = new List<Task<List<XivDependencyRootInfo>>>(capacity);
                    for (int p = 0; p < 10000; p++)
                    {
                        root.PrimaryId = p;

                        if (secondary == XivItemType.none)
                        {
                            var folder = root.GetRootFolder();
                            folder = folder.Substring(0, folder.Length - 1);
                            if (primary == XivItemType.indoor || primary == XivItemType.outdoor)
                            {
                                // For furniture, they're valid as long as they have an assets folder we can find.
                                var assetFolder = folder + "/asset";
                                var folderHash = (uint)HashGenerator.GetHash(assetFolder);

                                var sgbName = root.GetSgbName();
                                var sgbHash = (uint)HashGenerator.GetHash(sgbName);

                                if (Hashes.ContainsKey(folderHash) && Hashes[folderHash].Contains(sgbHash))
                                {
                                    result.Add((XivDependencyRootInfo)root.Clone());
                                }
                            }
                            else
                            {
                                // Test to see if the IMC file exists.
                                var folderHash = (uint)HashGenerator.GetHash(folder);
                                var imcName = root.GetBaseFileName(false) + ".imc";
                                var imcHash = (uint)HashGenerator.GetHash(imcName);

                                if (Hashes.ContainsKey(folderHash) && Hashes[folderHash].Contains(imcHash))
                                {
                                    foreach (var slot in slots)
                                    {
                                        var sl = slot == "" ? null : slot;
                                        var nRoot = new XivDependencyRootInfo()
                                        {
                                            PrimaryId = root.PrimaryId,
                                            PrimaryType = root.PrimaryType,
                                            SecondaryId = root.SecondaryId,
                                            SecondaryType = root.SecondaryType,
                                            Slot = sl
                                        };
                                        result.Add(nRoot);
                                    }
                                }
                            }
                        }
                        else
                        {
                            tasks.Add(TestAllSubRoots(Hashes, root));
                        }
                    }

                    if(tasks.Count > 0)
                    {
                        await Task.WhenAll(tasks);
                        foreach(var task in tasks)
                        {
                            result.AddRange(task.Result);
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
            var workerStatus = XivCache.CacheWorkerEnabled;
            await XivCache.SetCacheWorkerState(false);

            try
            {

                ResetRootCache();
                // Stop the worker, in case it was reading from the file for some reason.

                // Readonly TX
                var tx = ModTransaction.BeginReadonlyTransaction();


                var hashes = await Index.GetAllHashes(XivDataFile._04_Chara, tx);
                var bgcHashes = await Index.GetAllHashes(XivDataFile._01_Bgcommon, tx);


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
                types[XivItemType.outdoor].Add(XivItemType.none);
                types[XivItemType.indoor].Add(XivItemType.none);

                var tasks = new List<Task<List<XivDependencyRootInfo>>>();
                foreach (var kv in types)
                {
                    var primary = kv.Key;
                    foreach (var secondary in kv.Value)
                    {
                        if (primary == XivItemType.indoor || primary == XivItemType.outdoor)
                        {
                            tasks.Add(TestAllRoots(bgcHashes, primary, secondary));
                        }
                        else
                        {
                            tasks.Add(TestAllRoots(hashes, primary, secondary));
                        }
                    }
                }
                try
                {
                    await Task.WhenAll(tasks.ToArray());

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    throw;
                }

                Console.WriteLine("Compiling final root list...");
                List<XivDependencyRootInfo> allRoots = new List<XivDependencyRootInfo>();
                foreach (var t in tasks)
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
                            foreach (var root in allRoots)
                            {
                                XivCache.CacheRoot(root, db, cmd);
                            }
                        }
                        transaction.Commit();
                    }
                }
            }
            finally
            {
                await XivCache.SetCacheWorkerState(workerStatus);
            }
        }

    }
}
