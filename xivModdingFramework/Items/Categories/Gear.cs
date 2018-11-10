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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Exd.Enums;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.General.DataContainers;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Variants.FileTypes;

namespace xivModdingFramework.Items.Categories
{
    /// <summary>
    /// This class is used to obtain a list of available gear
    /// This includes equipment, accessories, and weapons
    /// Food is a special case as it is within the chara/weapons directory
    /// </summary>
    public class Gear
    {
        private readonly DirectoryInfo _gameDirectory;
        private readonly XivLanguage _xivLanguage;
        public Gear(DirectoryInfo gameDirectory, XivLanguage xivLanguage)
        {
            _gameDirectory = gameDirectory;
            _xivLanguage = xivLanguage;
        }

        /// <summary>
        /// A getter for available gear in the Item exd files
        /// </summary>
        /// <returns>A list containing XivGear data</returns>
        public List<XivGear> GetGearList()
        {
            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int modelDataCheckOffset = 31;
            const int dataLength = 160;
            const int nameDataOffset = 14;
            const int modelDataOffset = 24;
            const int iconDataOffset = 136;
            const int slotDataOffset = 154;

            var xivGearList = new List<XivGear>();

            xivGearList.AddRange(GetMissingGear());

            var ex = new Ex(_gameDirectory, _xivLanguage);
            var itemDictionary = ex.ReadExData(XivEx.item);

            // Loops through all the items in the item exd files
            // Item files start at 0 and increment by 500 for each new file
            // Item_0, Item_500, Item_1000, etc.
            foreach (var item in itemDictionary.Values)
            {
                // This checks whether there is any model data present in the current item
                if (item[modelDataCheckOffset] <= 0) continue;

                // Gear can have 2 separate models (MNK weapons for example)
                var primaryMi = new XivModelInfo();
                var secondaryMi = new XivModelInfo();

                var xivGear = new XivGear
                {
                    Category = XivStrings.Gear,
                    ModelInfo = primaryMi,
                    SecondaryModelInfo = secondaryMi
                };

                /* Used to determine if the given model is a weapon
                 * This is important because the data is formated differently
                 * The model data is a 16 byte section separated into two 8 byte parts (primary model, secondary model)
                 * Format is 8 bytes in length with 2 bytes per data point [short, short, short, short]
                 * Gear: primary model [blank, blank, variant, ID] nothing in secondary model
                 * Weapon: primary model [blank, variant, body, ID] secondary model [blank, variant, body, ID]
                */
                var isWeapon = false;

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(item)))
                {
                    br.BaseStream.Seek(nameDataOffset, SeekOrigin.Begin);
                    var nameOffset = br.ReadInt16();

                    // Model Data
                    br.BaseStream.Seek(modelDataOffset, SeekOrigin.Begin);

                    // Primary Blank
                    primaryMi.Unused = br.ReadInt16();

                    // Primary Variant for weapon, blank otherwise
                    var weaponVariant = br.ReadInt16();

                    if (weaponVariant != 0)
                    {
                        primaryMi.Variant = weaponVariant;
                        isWeapon = true;
                    }

                    // Primary Body if weapon, Variant otherwise
                    if (isWeapon)
                    {
                        primaryMi.Body = br.ReadInt16();
                    }
                    else
                    {
                        primaryMi.Variant = br.ReadInt16();
                    }

                    // Primary Model ID
                    primaryMi.ModelID = br.ReadInt16();

                    // Secondary Blank
                    secondaryMi.Unused = br.ReadInt16();

                    // Secondary Variant for weapon, blank otherwise
                    weaponVariant = br.ReadInt16();

                    if (weaponVariant != 0)
                    {
                        secondaryMi.Variant = weaponVariant;
                        isWeapon = true;
                    }

                    // Secondary Body if weapon, Variant otherwise
                    if (isWeapon)
                    {
                        secondaryMi.Body = br.ReadInt16();
                    }
                    else
                    {
                        secondaryMi.Variant = br.ReadInt16();
                    }

                    // Secondary Model ID
                    secondaryMi.ModelID = br.ReadInt16();

                    // Icon
                    br.BaseStream.Seek(iconDataOffset, SeekOrigin.Begin);
                    xivGear.IconNumber = br.ReadUInt16();

                    // Gear Slot/Category
                    br.BaseStream.Seek(slotDataOffset, SeekOrigin.Begin);
                    int slotNum = br.ReadByte();

                    // Waist items do not have texture or model data
                    if (slotNum == 6) continue;

                    xivGear.ItemCategory = _slotNameDictionary.ContainsKey(slotNum) ? _slotNameDictionary[slotNum] : "Unknown";

                    // Gear Name
                    var gearNameOffset = dataLength + nameOffset;
                    var gearNameLength = item.Length - gearNameOffset;
                    br.BaseStream.Seek(gearNameOffset, SeekOrigin.Begin);
                    var nameString = Encoding.UTF8.GetString(br.ReadBytes(gearNameLength)).Replace("\0", "");
                    xivGear.Name = new string(nameString.Where(c => !char.IsControl(c)).ToArray());

                    xivGearList.Add(xivGear);
                }
            }
            xivGearList.Sort();

            return xivGearList;
        }

        /// <summary>
        /// Gets any missing gear that must be added manualy as it does not exist in the items exd
        /// </summary>
        /// <returns>The list of missing gear</returns>
        private List<XivGear> GetMissingGear()
        {
            var xivGearList = new List<XivGear>();

            var xivGear = new XivGear
            {
                Name = "SmallClothes Body",
                Category = XivStrings.Gear,
                ItemCategory = _slotNameDictionary[4],
                ModelInfo = new XivModelInfo { ModelID = 0, Variant = 1, Body = 0}
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Legs",
                Category = XivStrings.Gear,
                ItemCategory = _slotNameDictionary[7],
                ModelInfo = new XivModelInfo { ModelID = 0, Variant = 1, Body = 0 }
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Feet",
                Category = XivStrings.Gear,
                ItemCategory = _slotNameDictionary[8],
                ModelInfo = new XivModelInfo { ModelID = 0, Variant = 1, Body = 0 }
            };

            xivGearList.Add(xivGear);

            return xivGearList;
        }


        /// <summary>
        /// Gets the available races that contain texture data for the given gear
        /// </summary>
        /// <remarks>
        /// This checks to see if the mtrl file for each race exists in the mtrl folder
        /// It creates a list of the races which do have an available mtrl folder
        /// </remarks>
        /// <param name="xivGear">A gear item</param>
        /// <returns>A list of XivRace data</returns>
        public List<XivRace> GetRacesForTextures(XivGear xivGear, XivDataFile dataFile)
        {
            // Get the material version for the item from the imc file
            var imc = new Imc(_gameDirectory, dataFile);
            var gearVersion = imc.GetImcInfo(xivGear, xivGear.ModelInfo).Version.ToString().PadLeft(4, '0');

            var modelID = xivGear.ModelInfo.ModelID.ToString().PadLeft(4, '0');

            var raceList = new List<XivRace>();

            var index = new Index(_gameDirectory);
            var itemType = ItemType.GetItemType(xivGear);
            string mtrlFolder;

            if (itemType == XivItemType.weapon)
            {
                return new List<XivRace>{XivRace.All_Races};
            }

            switch (itemType)
            {
                case XivItemType.equipment:
                    mtrlFolder = $"chara/{itemType}/e{modelID}/material/v{gearVersion}";
                    break;
                case XivItemType.accessory:
                    mtrlFolder = $"chara/{itemType}/a{modelID}/material/v{gearVersion}";
                    break;
                default:
                    mtrlFolder = "";
                    break;
            }

            var testFilesDictionary = new Dictionary<int, string>();

            // loop through each race ID to create a dictionary containing [Hashed file name, race ID]
            foreach (var ID in IDRaceDictionary.Keys)
            {
                string mtrlFile;

                switch (itemType)
                {
                    case XivItemType.equipment:
                        mtrlFile = $"mt_c{ID}e{modelID}_{SlotAbbreviationDictionary[xivGear.ItemCategory]}_a.mtrl";
                        break;
                    case XivItemType.accessory:
                        mtrlFile = $"mt_c{ID}a{modelID}_{SlotAbbreviationDictionary[xivGear.ItemCategory]}_a.mtrl";
                        break;
                    default:
                        mtrlFile = "";
                        break;
                }

                testFilesDictionary.Add(HashGenerator.GetHash(mtrlFile), ID);
            }

            // get the list of hashed file names from the mtrl folder
            var files = index.GetAllHashedFilesInFolder(HashGenerator.GetHash(mtrlFolder), dataFile);

            // Loop through each entry in the dictionary
            foreach (var testFile in testFilesDictionary)
            {
                // if the file in the dictionary entry is contained in the list of files from the folder
                // add that race to the race list
                if (files.Contains(testFile.Key))
                {
                    raceList.Add(IDRaceDictionary[testFile.Value]);
                }
            }

            return raceList;
        }


        /// <summary>
        /// Gets the available races that contain model data for the given gear
        /// </summary>
        /// <remarks>
        /// This checks to see if the mdl file for each race exists in the mdl folder
        /// It creates a list of the races which do have an available mdl file
        /// </remarks>
        /// <param name="xivGear">A gear item</param>
        /// <returns>A list of XivRace data</returns>
        public List<XivRace> GetRacesForModels(XivGear xivGear, XivDataFile dataFile)
        {
            var itemType = ItemType.GetItemType(xivGear);

            var modelID = xivGear.ModelInfo.ModelID.ToString().PadLeft(4, '0');

            var raceList = new List<XivRace>();

            if (itemType == XivItemType.weapon)
            {
                return new List<XivRace> { XivRace.All_Races };
            }

            var index = new Index(_gameDirectory);

            string mdlFolder;
            var id = xivGear.ModelInfo.ModelID.ToString().PadLeft(4, '0');

            switch (itemType)
            {
                case XivItemType.equipment:
                    mdlFolder = $"chara/{itemType}/e{id}/model";
                    break;
                case XivItemType.accessory:
                    mdlFolder = $"chara/{itemType}/a{id}/model";
                    break;
                default:
                    mdlFolder = "";
                    break;
            }


            var testFilesDictionary = new Dictionary<int, string>();

            // loop through each race ID to create a dictionary containing [Hashed file name, race ID]
            foreach (var ID in IDRaceDictionary.Keys)
            {
                string mdlFile;

                switch (itemType)
                {
                    case XivItemType.equipment:
                        mdlFile = $"c{ID}e{modelID}_{SlotAbbreviationDictionary[xivGear.ItemCategory]}.mdl";
                        break;
                    case XivItemType.accessory:
                        mdlFile = $"c{ID}a{modelID}_{SlotAbbreviationDictionary[xivGear.ItemCategory]}.mdl";
                        break;
                    default:
                        mdlFile = "";
                        break;
                }

                testFilesDictionary.Add(HashGenerator.GetHash(mdlFile), ID);
            }

            // get the list of hashed file names from the mtrl folder
            var files = index.GetAllHashedFilesInFolder(HashGenerator.GetHash(mdlFolder), dataFile);

            // Loop through each entry in the dictionary
            foreach (var testFile in testFilesDictionary)
            {
                // if the file in the dictionary entry is contained in the list of files from the folder
                // add that race to the race list
                if (files.Contains(testFile.Key))
                {
                    raceList.Add(IDRaceDictionary[testFile.Value]);
                }
            }

            return raceList;
        }

        /// <summary>
        /// Searches for items given a model ID and item type
        /// </summary>
        /// <param name="modelID"> The model id used for searching</param>
        /// <param name="type">The type of item</param>
        /// <returns>A list of SearchResults objects</returns>
        public List<SearchResults> SearchGearByModelID(int modelID, string type)
        {
            var searchResultsList = new List<SearchResults>();
            var index = new Index(_gameDirectory);

            var t1 = Task.Run(() =>
            {
                var equipmentSlots = new string[] { "met", "glv", "dwn", "sho", "top", };
                var accessorySlots = new string[] { "ear", "nek", "rir", "ril", "wrs" };
                var parts = new string[] { "a", "b", "c", "d", "e", "f" };

                var id = modelID.ToString().PadLeft(4, '0');
                var folder = "";
                var file = "";

                if (type.Equals("Equipment"))
                {
                    folder = $"chara/equipment/e{id}/material/v";
                }
                else if (type.Equals("Accessory"))
                {
                    folder = $"chara/accessory/a{id}/material/v";
                }
                else if (type.Equals("Weapon"))
                {
                    folder = $"chara/weapon/w{id}/obj/body/b";
                }


                var bodyVariantDictionary = new Dictionary<int, List<int>>();
                List<int> variantList = null;

                if (type.Equals("Weapon"))
                {
                    for (var i = 1; i < 200; i++)
                    {
                        var folderHashDictionary = new Dictionary<int, int>();

                        var wFolder = $"{folder}{i.ToString().PadLeft(4, '0')}/material/v";

                        for (var j = 1; j < 200; j++)
                        {
                            folderHashDictionary.Add(HashGenerator.GetHash($"{wFolder}{j.ToString().PadLeft(4, '0')}"), j);
                        }

                        variantList = index.GetFolderExistsList(folderHashDictionary, XivDataFile._04_Chara);

                        if (variantList.Count > 0)
                        {
                            variantList.Sort();
                            bodyVariantDictionary.Add(i, variantList);
                        }
                    }
                }
                else
                {
                    var folderHashDictionary = new Dictionary<int, int>();

                    for (var i = 1; i < 200; i++)
                    {
                        folderHashDictionary.Add(HashGenerator.GetHash($"{folder}{i.ToString().PadLeft(4, '0')}"), i);
                    }

                    variantList = index.GetFolderExistsList(folderHashDictionary, XivDataFile._04_Chara);
                }


                if (!type.Equals("Weapon"))
                {
                    foreach (var variant in variantList)
                    {
                        var mtrlFolder = $"{folder}{variant.ToString().PadLeft(4, '0')}";
                        var mtrlFile = "";

                        var mtrlFolderHashes =
                            index.GetAllHashedFilesInFolder(HashGenerator.GetHash(mtrlFolder), XivDataFile._04_Chara);

                        foreach (var race in IDRaceDictionary.Keys)
                        {
                            string[] slots = null;

                            if (type.Equals("Equipment"))
                            {
                                slots = equipmentSlots;
                            }
                            else if (type.Equals("Accessory"))
                            {
                                slots = accessorySlots;
                            }

                            foreach (var slot in slots)
                            {
                                foreach (var part in parts)
                                {
                                    if (type.Equals("Equipment"))
                                    {
                                        mtrlFile = $"mt_c{race}e{id}_{slot}_{part}.mtrl";
                                    }
                                    else if (type.Equals("Accessory"))
                                    {
                                        mtrlFile = $"mt_c{race}a{id}_{slot}_{part}.mtrl";
                                    }

                                    if (mtrlFolderHashes.Contains(HashGenerator.GetHash(mtrlFile)))
                                    {
                                        searchResultsList.Add(new SearchResults { Body = "-", Slot = AbbreviationSlotDictionary[slot], Variant = variant.ToString() });
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    foreach (var bodyVariant in bodyVariantDictionary)
                    {
                        foreach (var variant in bodyVariant.Value)
                        {
                            searchResultsList.Add(new SearchResults { Body = bodyVariant.Key.ToString(), Slot = XivStrings.Main_Hand, Variant = variant.ToString() });
                        }
                    }
                }

            });

            t1.Wait();
            return searchResultsList;
        }

        // A dictionary containg <Slot ID, Gear Category>
        private readonly Dictionary<int, string> _slotNameDictionary = new Dictionary<int, string>
        {
            {0, XivStrings.Food },
            {1, XivStrings.Main_Hand },
            {2, XivStrings.Off_Hand },
            {3, XivStrings.Head },
            {4, XivStrings.Body },
            {5, XivStrings.Hands },
            {6, XivStrings.Waist },
            {7, XivStrings.Legs },
            {8, XivStrings.Feet },
            {9, XivStrings.Ears },
            {10, XivStrings.Neck },
            {11, XivStrings.Wrists },
            {12, XivStrings.Rings },
            {13, XivStrings.Two_Handed },
            {14, XivStrings.Main_Off },
            {15, XivStrings.Head_Body },
            {16, XivStrings.Body_Hands_Legs_Feet },
            {17, XivStrings.Soul_Crystal },
            {18, XivStrings.Legs_Feet },
            {19, XivStrings.All },
            {20, XivStrings.Body_Hands_Legs },
            {21, XivStrings.Body_Legs_Feet }
        };

        /// <summary>
        /// A dictionary containing race data in the format [Race ID, XivRace]
        /// </summary>
        private static readonly Dictionary<string, XivRace> IDRaceDictionary = new Dictionary<string, XivRace>
        {
            {"0101", XivRace.Hyur_Midlander_Male},
            {"0104", XivRace.Hyur_Midlander_Male_NPC},
            {"0201", XivRace.Hyur_Midlander_Female},
            {"0204", XivRace.Hyur_Midlander_Female_NPC},
            {"0301", XivRace.Hyur_Highlander_Male},
            {"0304", XivRace.Hyur_Highlander_Male_NPC},
            {"0401", XivRace.Hyur_Highlander_Female},
            {"0404", XivRace.Hyur_Highlander_Female_NPC},
            {"0501", XivRace.Elezen_Male},
            {"0504", XivRace.Elezen_Male_NPC},
            {"0601", XivRace.Elezen_Female},
            {"0604", XivRace.Elezen_Female_NPC},
            {"0701", XivRace.Miqote_Male},
            {"0704", XivRace.Miqote_Male_NPC},
            {"0801", XivRace.Miqote_Female},
            {"0804", XivRace.Miqote_Female_NPC},
            {"0901", XivRace.Roegadyn_Male},
            {"0904", XivRace.Roegadyn_Male_NPC},
            {"1001", XivRace.Roegadyn_Female},
            {"1004", XivRace.Roegadyn_Female_NPC},
            {"1101", XivRace.Lalafell_Male},
            {"1104", XivRace.Lalafell_Male_NPC},
            {"1201", XivRace.Lalafell_Female},
            {"1204", XivRace.Lalafell_Female_NPC},
            {"1301", XivRace.AuRa_Male},
            {"1304", XivRace.AuRa_Male_NPC},
            {"1401", XivRace.AuRa_Female},
            {"1404", XivRace.AuRa_Female_NPC},
            {"9104", XivRace.NPC_Male},
            {"9204", XivRace.NPC_Female}
        };

        /// <summary>
        /// A dictionary containing slot data in the format [Slot Name, Slot abbreviation]
        /// </summary>
        private static readonly Dictionary<string, string> SlotAbbreviationDictionary = new Dictionary<string, string>
        {
            {XivStrings.Head, "met"},
            {XivStrings.Hands, "glv"},
            {XivStrings.Legs, "dwn"},
            {XivStrings.Feet, "sho"},
            {XivStrings.Body, "top"},
            {XivStrings.Ears, "ear"},
            {XivStrings.Neck, "nek"},
            {XivStrings.Rings, "rir"},
            {XivStrings.Wrists, "wrs"},
            {XivStrings.Head_Body, "top"},
            {XivStrings.Body_Hands, "top"},
            {XivStrings.Body_Hands_Legs, "top"},
            {XivStrings.Body_Legs_Feet, "top"},
            {XivStrings.Body_Hands_Legs_Feet, "top"},
            {XivStrings.Legs_Feet, "top"},
            {XivStrings.All, "top"},
            {XivStrings.Face, "fac"},
            {XivStrings.Iris, "iri"},
            {XivStrings.Etc, "etc"},
            {XivStrings.Accessory, "acc"},
            {XivStrings.Hair, "hir"}
        };

        /// <summary>
        /// A dictionary containing slot data in the format [Slot abbreviation, Slot Name]
        /// </summary>
        private static readonly Dictionary<string, string> AbbreviationSlotDictionary = new Dictionary<string, string>
        {
            {"met", XivStrings.Head},
            {"glv", XivStrings.Hands},
            {"dwn", XivStrings.Legs},
            {"sho", XivStrings.Feet},
            {"top", XivStrings.Body},
            {"ear", XivStrings.Ears},
            {"nek", XivStrings.Neck},
            {"rir", XivStrings.Rings},
            {"wrs", XivStrings.Wrists},
        };
    }
}