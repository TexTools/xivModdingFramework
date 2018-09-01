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
using xivModdingFramework.Exd.Enums;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
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
            const int dataLength = 152;
            const int nameDataOffset = 14;
            const int modelDataOffset = 24;
            const int iconDataOffset = 128;
            const int slotDataOffset = 146;

            var xivGearList = new List<XivGear>();

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
                    PrimaryModelInfo = primaryMi,
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
            var gearVersion = imc.GetImcInfo(xivGear, xivGear.PrimaryModelInfo).Version.ToString().PadLeft(4, '0');

            var modelID = xivGear.PrimaryModelInfo.ModelID.ToString().PadLeft(4, '0');

            var raceList = new List<XivRace>();

            var index = new Index(_gameDirectory);
            var itemType = ItemType.GetItemType(xivGear);
            string mtrlFolder;

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
        /// Gets the list of available mtrl parts for a given item
        /// </summary>
        /// <param name="itemModel">An item that contains model data</param>
        /// <param name="xivRace">The race for the requested data</param>
        /// <returns>A list of part characters</returns>
        public List<char> GetTexturePartList(IItemModel itemModel, XivRace xivRace, XivDataFile dataFile)
        {
            // Get the mtrl version for the given item from the imc file
            var imc = new Imc(_gameDirectory, dataFile);
            var version = imc.GetImcInfo(itemModel, itemModel.PrimaryModelInfo).Version.ToString().PadLeft(4, '0');

            var id = itemModel.PrimaryModelInfo.ModelID.ToString().PadLeft(4, '0');
            var bodyVer = itemModel.PrimaryModelInfo.Body.ToString().PadLeft(4, '0');
            var parts = new[] { 'a', 'b', 'c', 'd', 'e', 'f' };
            var race = xivRace.GetRaceCode();

            var index = new Index(_gameDirectory);

            var itemType = ItemType.GetItemType(itemModel);
            string mtrlFolder = "", mtrlFile = "";

            switch (itemType)
            {
                case XivItemType.equipment:
                    mtrlFolder = $"chara/{itemType}/e{id}/material/v{version}";
                    mtrlFile = $"mt_c{race}e{id}_{SlotAbbreviationDictionary[itemModel.ItemCategory]}_";
                    break;
                case XivItemType.accessory:
                    mtrlFolder = $"chara/{itemType}/a{id}/material/v{version}";
                    mtrlFile = $"mt_c{race}a{id}_{SlotAbbreviationDictionary[itemModel.ItemCategory]}_";
                    break;
                case XivItemType.weapon:
                    mtrlFolder = $"chara/{itemType}/w{id}/obj/body/b{bodyVer}/material/v{version}";
                    mtrlFile = $"mt_w{id}_b{bodyVer}_";
                    break;
                case XivItemType.monster:
                    mtrlFolder = $"chara/{itemType}/m{id}/obj/body/b{bodyVer}/material/v{version}";
                    mtrlFile = $"mt_m{id}_b{bodyVer}_";
                    break;
                case XivItemType.demihuman:
                    mtrlFolder = $"chara/{itemType}/d{id}/obj/body/e{bodyVer}/material/v{version}";
                    mtrlFile = $"mt_d{id}_e{bodyVer}_";
                    break;
                case XivItemType.human:
                    if (itemModel.ItemCategory.Equals(XivStrings.Body))
                    {
                        mtrlFolder = $"chara/{itemType}/c{id}/obj/body/b{bodyVer}/material/v{version}";
                        mtrlFile = $"mt_c{id}b{bodyVer}_";
                    }
                    else if (itemModel.ItemCategory.Equals(XivStrings.Hair))
                    {
                        mtrlFolder = $"chara/{itemType}/c{id}/obj/body/h{bodyVer}/material/v{version}";
                        mtrlFile = $"mt_c{id}h{bodyVer}_{SlotAbbreviationDictionary[itemModel.ItemCategory]}_";
                    }
                    else if (itemModel.ItemCategory.Equals(XivStrings.Face))
                    {
                        mtrlFolder = $"chara/{itemType}/c{id}/obj/body/f{bodyVer}/material/v{version}";
                        mtrlFile = $"mt_c{id}f{bodyVer}_{SlotAbbreviationDictionary[itemModel.ItemCategory]}_";
                    }
                    else if (itemModel.ItemCategory.Equals(XivStrings.Tail))
                    {
                        mtrlFolder = $"chara/{itemType}/c{id}/obj/body/t{bodyVer}/material/v{version}";
                        mtrlFile = $"mt_c{id}t{bodyVer}_";
                    }
                    break;
                default:
                    mtrlFolder = "";
                    break;
            }

            // Get a list of hashed mtrl files that are in the given folder
            var files = index.GetAllHashedFilesInFolder(HashGenerator.GetHash(mtrlFolder), dataFile);

            // append the part char to the mtrl file and see if its hashed value is within the files list
            // returns the list of parts that exist within the mtrl folder
            return (from part in parts let mtrlCheck = mtrlFile + part + ".mtrl" where files.Contains(HashGenerator.GetHash(mtrlCheck)) select part).ToList();
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

            var modelID = xivGear.PrimaryModelInfo.ModelID.ToString().PadLeft(4, '0');

            var raceList = new List<XivRace>();


            var index = new Index(_gameDirectory);

            string mdlFolder;
            var id = xivGear.PrimaryModelInfo.ModelID.ToString().PadLeft(4, '0');

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
    }
}