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

using System;
using System.Collections.Generic;
using System.IO;
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
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Variants.DataContainers;

using Index = xivModdingFramework.SqPack.FileTypes.Index;

namespace xivModdingFramework.Variants.FileTypes
{
    public enum ImcType : short {
        Unknown = 0,
        NonSet = 1,
        Set = 31
    }

    /// <summary>
    /// This class contains the methods that deal with the .imc file type 
    /// </summary>
    public static class Imc
    {
        private const string ImcExtension = ".imc";
        public static bool UsesImc(IItemModel item)
        {
            var root = item.GetRoot();
            if (root == null) return false;
            return UsesImc(root);

        }
        public static bool UsesImc(XivDependencyRoot root)
        {
            if (root == null) return false;
            return UsesImc(root.Info);
        }
        public static bool UsesImc(XivDependencyRootInfo root)
        {

            if (root.PrimaryType == XivItemType.human)
            {
                return false;
            }
            else if (root.PrimaryType == XivItemType.indoor || root.PrimaryType == XivItemType.outdoor
                || root.PrimaryType == XivItemType.fish || root.PrimaryType == XivItemType.painting)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// A simple function that retrieves the material set ID of an item,
        /// whether via IMC or default value.
        /// 
        /// A value of -1 indicates that material sets are not used at all on this item.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public static async Task<int> GetMaterialSetId(IItemModel item, bool forceOriginal = false, ModTransaction tx = null)
        {
            var root = item.GetRoot();
            if (root == null) return -1;

            if(root.Info.PrimaryType == XivItemType.human)
            {
                if(root.Info.SecondaryType == XivItemType.hair
                    || root.Info.SecondaryType == XivItemType.tail)
                {
                    // These use material sets (always set 1), but have no IMC file.
                    return 1;
                } else
                {
                    return -1;
                }
            } else if(root.Info.PrimaryType == XivItemType.indoor || root.Info.PrimaryType == XivItemType.outdoor)
            {
                return -1;
            } else
            {
                try
                {
                    var entry = await GetImcInfo(item, forceOriginal, tx);
                    if(entry == null)
                    {
                        return -1;
                    }
                    return entry.MaterialSet;
                } catch
                {
                    return -1;
                }
            }
        }

        /// <summary>
        /// Gets the relevant IMC information for a given item
        /// </summary>
        /// <param name="item">The item to get the version for</param>
        /// <param name="modelInfo">The model info of the item</param>
        /// <returns>The XivImc Data</returns>
        public static async Task<XivImc> GetImcInfo(IItemModel item, bool forceOriginal = false, ModTransaction tx = null)
        {
            if (item == null || !Imc.UsesImc(item))
            {
                return null;
            }

            var info = await GetFullImcInfo(item, forceOriginal, tx);
            if(info == null)
            {
                return null;
            }
            var slot = item.GetItemSlotAbbreviation();

            var result = info.GetEntry(item.ModelInfo.ImcSubsetID, slot);
            return result;
        }

        public static async Task<FullImcInfo> GetFullImcInfo(IItemModel item, bool forceOriginal = false, ModTransaction tx = null)
        {
            if(item == null || !UsesImc(item))
            {
                return null;
            }
            if (tx == null)
            {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginTransaction();
            }

            var imcPath = await GetImcPath(item, tx);
            var path = imcPath.Folder + "/" + imcPath.File;


            if(!await tx.FileExists(path, forceOriginal))
            {
                return null;
            }
            else
            {
                return await GetFullImcInfo(path, forceOriginal, tx);
            }
        }


        private static readonly Regex _pathOnlyRegex = new Regex("^(.*)" + Constants.BinaryOffsetMarker + ".*$");
        private static readonly Regex _binaryOffsetRegex = new Regex(Constants.BinaryOffsetMarker + "([0-9]+)$");

        /// <summary>
        /// Retrieves an arbitrary selection of IMC entries based on their path::binaryoffset.
        /// </summary>
        /// <param name="pathsWithOffsets"></param>
        /// <returns></returns>
        public static async Task<List<XivImc>> GetEntries(List<string> pathsWithOffsets, bool forceDefault = false, ModTransaction tx = null)
        {
            var entries = new List<XivImc>();

            var lastPath = "";
            byte[] imcByteData = new byte[0];


            foreach (var combinedPath in pathsWithOffsets)
            {
                var binaryMatch = _binaryOffsetRegex.Match(combinedPath);
                var pathMatch = _pathOnlyRegex.Match(combinedPath);

                // Invalid format.
                if (!pathMatch.Success || !binaryMatch.Success) continue;

                long offset = Int64.Parse(binaryMatch.Groups[1].Value) / 8;
                string path = pathMatch.Groups[1].Value;

                // Only reload this data if we need to.
                if (path != lastPath)
                {
                    imcByteData = await Dat.ReadSqPackType2(path, forceDefault, tx);
                }
                lastPath = path;

                // Offset would run us past the end of the file.
                const int entrySize = 6;
                if (offset > imcByteData.Length - entrySize) continue;


                using (var br = new BinaryReader(new MemoryStream(imcByteData)))
                {
                    var subsetCount = br.ReadInt16();
                    var identifier = (ImcType)br.ReadInt16();

                    br.BaseStream.Seek(offset, SeekOrigin.Begin);
                    entries.Add(new XivImc
                    {
                        MaterialSet = br.ReadByte(),
                        Decal = br.ReadByte(),
                        Mask = br.ReadUInt16(),
                        Vfx = br.ReadByte(),
                        Animation = br.ReadByte()
                    });
                }

            }
            return entries;
        }

        /// <summary>
        /// Saves a set of IMC entries to file.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="entries"></param>
        /// <returns></returns>
        internal static async Task SaveEntries(string path, string slot, List<XivImc> entries, IItem referenceItem = null, ModTransaction tx = null)
        {
            if(tx == null)
            {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginTransaction();
            }

            var exists = await tx.FileExists(path);

            FullImcInfo info;
            if(exists)
            {
                info = await GetFullImcInfo(path, false, tx);
            } else
            {
                var ri = XivDependencyGraph.ExtractRootInfo(path);
                if (ri.SecondaryType == null)
                {
                    info = new FullImcInfo()
                    {
                        DefaultSubset = new List<XivImc>() { new XivImc(), new XivImc(), new XivImc(), new XivImc(), new XivImc() },
                        SubsetList = new List<List<XivImc>>(),
                        TypeIdentifier = ImcType.Set
                    };
                } else
                {
                    info = new FullImcInfo()
                    {
                        DefaultSubset = new List<XivImc>() { new XivImc() },
                        SubsetList = new List<List<XivImc>>(),
                        TypeIdentifier = ImcType.NonSet
                    };
                }
            }


            for(int i = 0; i < entries.Count; i++)
            {
                XivImc e;
                if (i >= info.SubsetCount + 1)
                {
                    e = new XivImc();
                }
                else
                {
                    e = info.GetEntry(i, slot);
                }
                e.Mask = entries[i].Mask;
                e.Decal = entries[i].Decal;
                e.Vfx = entries[i].Vfx;
                e.Animation = entries[i].Animation;
                e.MaterialSet = entries[i].MaterialSet;

                if (i >= info.SubsetCount + 1)
                {
                    info.SetEntry(e, i, slot, true);
                }
            }

            // Save the modified info.
            await SaveFullImcInfo(info, path, Constants.InternalModSourceName, referenceItem, tx);
        }

        public static byte[] SerializeEntry(XivImc entry)
        {

            List<byte> bytes = new List<byte>(6);
            bytes.Add((byte)entry.MaterialSet);
            bytes.Add((byte)entry.Decal);
            bytes.AddRange(BitConverter.GetBytes((ushort)entry.Mask));
            bytes.Add((byte)entry.Vfx);
            bytes.Add((byte)entry.Animation);
            return bytes.ToArray();
        }

        public static XivImc DeserializeEntry(byte[] data)
        {

            using (var br = new BinaryReader(new MemoryStream(data)))
            {
                byte variant = br.ReadByte();
                byte unknown = br.ReadByte();
                ushort mask = br.ReadUInt16();
                byte vfx = br.ReadByte();
                byte anim = br.ReadByte();
                return new XivImc
                {
                    MaterialSet = variant,
                    Decal = unknown,
                    Mask = mask,
                    Vfx = vfx,
                    Animation = anim
                };

            }
        }


        /// <summary>
        /// Gets the full IMC information for a given item
        /// </summary>
        /// <param name="item"></param>
        /// <param name="useSecondary">Determines if the SecondaryModelInfo should be used instead.(XivGear only)</param>
        /// <returns>The ImcData data</returns>
        public static async Task<FullImcInfo> GetFullImcInfo(string path, bool forceDefault = false, ModTransaction tx = null)
        {

            var imcByteData = await Dat.ReadSqPackType2(path, forceDefault, tx);

            return await Task.Run(() =>
            {
                using (var br = new BinaryReader(new MemoryStream(imcByteData)))
                {
                    var subsetCount = br.ReadInt16();
                    var identifier = br.ReadInt16();
                    var imcData = new FullImcInfo()
                    {
                        TypeIdentifier = (ImcType) identifier,
                        DefaultSubset = new List<XivImc>(),
                        SubsetList = new List<List<XivImc>>(subsetCount)
                    };

                    //weapons and monsters do not have variant sets
                    if (imcData.TypeIdentifier == ImcType.NonSet)
                    {
                        // This type uses the first short for both Variant and VFX.
                        byte variant = br.ReadByte();
                        byte unknown = br.ReadByte();
                        ushort mask = br.ReadUInt16();
                        byte vfx = br.ReadByte();
                        byte anim = br.ReadByte();

                        imcData.DefaultSubset.Add(new XivImc
                        {
                            MaterialSet = variant,
                            Decal = unknown,
                            Mask = mask,
                            Vfx = variant,
                            Animation = anim
                        });

                        for (var i = 0; i < subsetCount; i++)
                        {
                            variant = br.ReadByte();
                            unknown = br.ReadByte();
                            mask = br.ReadUInt16();
                            vfx = br.ReadByte();
                            anim = br.ReadByte();

                            var newEntry = new XivImc
                            {
                                MaterialSet = variant,
                                Decal = unknown,
                                Mask = mask,
                                Vfx = vfx,
                                Animation = anim
                            };
                            var subset = new List<XivImc>() { newEntry };
                            imcData.SubsetList.Add(subset);
                        }
                    }
                    else if(imcData.TypeIdentifier == ImcType.Set)
                    {
                        // Identifier used by Equipment.
                        imcData.DefaultSubset = new List<XivImc>()
                        {
                            new XivImc
                                {MaterialSet = br.ReadByte(), Decal = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadByte(), Animation = br.ReadByte()},
                            new XivImc
                                {MaterialSet = br.ReadByte(), Decal = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadByte(), Animation = br.ReadByte()},
                            new XivImc
                                {MaterialSet = br.ReadByte(), Decal = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadByte(), Animation = br.ReadByte()},
                            new XivImc
                                {MaterialSet = br.ReadByte(), Decal = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadByte(), Animation = br.ReadByte()},
                            new XivImc
                                {MaterialSet = br.ReadByte(), Decal = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadByte(), Animation = br.ReadByte()},
                        };

                        for (var i = 0; i < subsetCount; i++)
                        {
                            // gets the data for each slot in the current variant set
                            var imcGear = new List<XivImc>()
                            {
                                new XivImc
                                {MaterialSet = br.ReadByte(), Decal = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadByte(), Animation = br.ReadByte()},
                                new XivImc
                                {MaterialSet = br.ReadByte(), Decal = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadByte(), Animation = br.ReadByte()},
                                new XivImc
                                {MaterialSet = br.ReadByte(), Decal = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadByte(), Animation = br.ReadByte()},
                                new XivImc
                                {MaterialSet = br.ReadByte(), Decal = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadByte(), Animation = br.ReadByte()},
                                new XivImc
                                {MaterialSet = br.ReadByte(), Decal = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadByte(), Animation = br.ReadByte()},
                            };
                            imcData.SubsetList.Add(imcGear);
                        }
                    } else
                    {
                        throw new NotSupportedException("Unknown IMC Type Identifier. (Please report this item in the TexTools Discord #bug_reports channel.)");
                    }

                    return imcData;
                }
            });
        }


        public static async Task SaveFullImcInfo(FullImcInfo info, string path, string source, IItem referenceItem = null, ModTransaction tx = null)
        {
            if (info == null || info.TypeIdentifier != ImcType.Set && info.TypeIdentifier != ImcType.NonSet)
            {
                throw new InvalidDataException("Cannot save invalid IMC file.");
            }
            var data = new List<byte>();


            // 4 Header bytes.
            data.AddRange(BitConverter.GetBytes((short) info.SubsetCount));
            data.AddRange(BitConverter.GetBytes((short) info.TypeIdentifier));

            // The rest of this is easy, it's literally just post all the sets in order.
            foreach(var entry in info.DefaultSubset)
            {
                data.AddRange(entry.GetBytes(info.TypeIdentifier));
            }

            foreach(var set in info.SubsetList)
            {
                foreach (var entry in set)
                {
                    data.AddRange(entry.GetBytes(info.TypeIdentifier));
                }
            }

            // That's it.
            source ??= "Unknown";

            await Dat.ImportType2Data(data.ToArray(), path, source, referenceItem, tx);
        }

        /// <summary>
        /// Gets the IMC internal path for the given model info
        /// </summary>
        /// <param name="modelInfo">The model info of the item</param>
        /// <param name="itemType">The type of the item</param>
        /// <returns>A touple containing the Folder and File strings</returns>
        private static async Task<(string Folder, string File)> GetImcPath(IItemModel item, ModTransaction tx)
        {
            string imcFolder = item.GetItemRootFolder();
            string imcFile;

            var primaryId = item.ModelInfo.PrimaryID.ToString().PadLeft(4, '0');
            var secondaryId = item.ModelInfo.SecondaryID.ToString().PadLeft(4, '0');
            var itemType = item.GetPrimaryItemType();

            switch (itemType)
            {
                case XivItemType.equipment:
                    imcFile = $"e{primaryId}{ImcExtension}";
                    break;
                case XivItemType.accessory:
                    imcFile = $"a{primaryId}{ImcExtension}";
                    break;
                case XivItemType.weapon:
                    imcFile = $"b{secondaryId}{ImcExtension}";
                    break;
                case XivItemType.monster:
                    imcFile = $"b{secondaryId}{ImcExtension}";
                    break;
                case XivItemType.demihuman:
                    imcFile = $"e{secondaryId}{ImcExtension}";
                    break;
                default:
                    imcFolder = "";
                    imcFile = "";
                    break;
            }

            var exists = await tx.FileExists(imcFolder + "/" + imcFile);

            if (!exists)
            {
                // Offhands sometimes use their mainhand's path.
                var gear = item as XivGear;
                if (gear != null && gear.PairedItem != null)
                {
                    return await GetImcPath(gear.PairedItem, tx);
                }
            }

            return (imcFolder, imcFile);
        }

        /// <summary>
        /// A dictionary containing slot offset data in format [Slot Abbreviation, Offset within variant set]
        /// </summary>
        public static readonly Dictionary<string, int> SlotOffsetDictionary = new Dictionary<string, int>
        {
            {"met", 0},
            {"top", 1},
            {"glv", 2},
            {"dwn", 3},
            {"sho", 4},
            {"ear", 0},
            {"nek", 1},
            {"wrs", 2},
            {"rir", 3},
            {"ril", 4}
        };
        /// <summary>
        /// A dictionary containing slot offset data in format [Slot Abbreviation, Offset within variant set]
        /// </summary>
        public static readonly Dictionary<string, int> EquipmentSlotOffsetDictionary = new Dictionary<string, int>
        {
            {"met", 0},
            {"top", 1},
            {"glv", 2},
            {"dwn", 3},
            {"sho", 4},
        };
        /// <summary>
        /// A dictionary containing slot offset data in format [Slot Abbreviation, Offset within variant set]
        /// </summary>
        public static readonly Dictionary<string, int> AccessorySlotOffsetDictionary = new Dictionary<string, int>
        {
            {"ear", 0},
            {"nek", 1},
            {"wrs", 2},
            {"rir", 3},
            {"ril", 4}
        };

        /// <summary>
        /// Class containing the information for and IMC file
        /// </summary>
        public class FullImcInfo
        {
            /// <summary>
            /// Get the number of subsets.
            ///  -NOT- the same as number of material variants.
            /// </summary>
            public short SubsetCount { get
                {
                    return (short)SubsetList.Count;
                }
                set {
                    throw new NotSupportedException("Attempted to directly set SubsetCount.");
                }
            }

            /// <summary>
            /// Get the size of each subset (Either 1 or 5)
            /// </summary>
            public int SubsetSize
            {
                get
                {
                    return DefaultSubset.Count;
                }
                set
                {
                    throw new NotSupportedException("Attempted to directly set SubsetSize.");
                }
            }

            /// <summary>
            /// Unknown Value
            /// </summary>
            public ImcType TypeIdentifier { get; set; }

            /// <summary>
            /// Total # of Gear Subsets.
            /// NOT the same as number of material variants.
            /// IItemModel->ImcSubsetID can be used as an index accessory in this list.
            /// </summary>
            public List<List<XivImc>> SubsetList { get; set; }

            /// <summary>
            /// The default variant set for the item, always the variant immediatly following the header
            /// </summary>
            public List<XivImc> DefaultSubset { get; set; }

            // Gets all (non-default) IMC entries for a given slot.
            public List<XivImc> GetAllEntries(string slot = "", bool includeDefault = true)
            {
                var ret = new List<XivImc>(SubsetList.Count);
                if (includeDefault)
                {
                    for (int i = 0; i <= SubsetList.Count; i++)
                    {
                        ret.Add(GetEntry(i, slot));
                    }
                }
                else
                {
                    for (int i = 1; i <= SubsetList.Count; i++)
                    {
                        ret.Add(GetEntry(i, slot));
                    }
                }


                return ret;
            }

            /// <summary>
            /// Retrieve a given IMC info. Zero or Negative values retrieve the default set.
            /// </summary>
            /// <param name="index">IMC Variant/Subset ID</param>
            /// <param name="slot">Slot Abbreviation</param>
            /// <returns></returns>
            public XivImc GetEntry(int subsetID = -1, string slot = "")
            {
                // Variant IDs are 1 based, not 0 based.
                var index = subsetID - 1;

                // Invalid Index, return default.
                if (index >= SubsetCount || index < 0)
                {
                    index = -1;
                }

                // Test for getting default set.
                var subset = DefaultSubset;
                if(index >= 0)
                {
                    subset = SubsetList[index];
                }

                // Get which offset the slot uses.
                var idx = 0;
                if(slot != null && SlotOffsetDictionary.ContainsKey(slot) && SlotOffsetDictionary[slot] < subset.Count)
                {
                    idx = SlotOffsetDictionary[slot];
                }

                return subset[idx];
            }

            public void SetEntry(XivImc info, int subsetID = -1, string slot = "", bool allowNew = false)
            {
                // Variant IDs are 1 based, not 0 based.
                var index = subsetID - 1;

                // Invalid Index, return default.
                if ((index >= SubsetCount && !allowNew) || index < 0)
                {
                    index = -1;
                }

                // Test for getting default set.
                var subset = DefaultSubset;
                if (index >= 0)
                {
                    if (index >= SubsetCount)
                    {
                        subset = new List<XivImc>();
                        if(TypeIdentifier == ImcType.Set)
                        {
                            // Five entries for set types.
                            subset.Add(new XivImc());
                            subset.Add(new XivImc());
                            subset.Add(new XivImc());
                            subset.Add(new XivImc());
                            subset.Add(new XivImc());
                        } else
                        {
                            // One entry for nonset types.
                            subset.Add(info);
                        }
                        SubsetList.Add(subset);
                    }
                    else
                    {
                        subset = SubsetList[index];
                    }
                }

                // Get which offset the slot uses.
                var idx = 0;
                if (slot != null && SlotOffsetDictionary.ContainsKey(slot) && SlotOffsetDictionary[slot] < subset.Count)
                {
                    idx = SlotOffsetDictionary[slot];
                }

                // Assign info.
                subset[idx] = info;
            }
        }

    }
}