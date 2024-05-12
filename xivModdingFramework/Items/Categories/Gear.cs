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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.Exd.Enums;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.General;
using xivModdingFramework.General.DataContainers;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.Enums;
using xivModdingFramework.Variants.FileTypes;

using Index = xivModdingFramework.SqPack.FileTypes.Index;

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
        private readonly Index _index;
        private static object _gearLock = new object();

        public Gear(DirectoryInfo gameDirectory, XivLanguage xivLanguage)
        {
            _gameDirectory = gameDirectory;
            _xivLanguage = xivLanguage;
            _index = new Index(_gameDirectory);
        }
        public async Task<List<XivGear>> GetGearList(string substring = null)
        {
            return await XivCache.GetCachedGearList(substring);
        }


        /// <summary>
        /// A getter for available gear in the Item exd files
        /// </summary>
        /// <returns>A list containing XivGear data</returns>
        public async Task<List<XivGear>> GetUnCachedGearList()
        {
            var ex = new Ex(_gameDirectory, _xivLanguage);
            var itemDictionary = await ex.ReadExData(XivEx.item);



            var xivGearList = new List<XivGear>();

            xivGearList.AddRange(GetMissingGear());

            if (itemDictionary.Count == 0)
                return xivGearList;

            // Loops through all the items in the item exd files
            // Item files start at 0 and increment by 500 for each new file
            // Item_0, Item_500, Item_1000, etc.
            await Task.Run(() => Parallel.ForEach(itemDictionary, (item) =>
            {
                var row = item.Value;
                try
                {
                    var primaryInfo = (ulong)row.GetColumnByName("PrimaryInfo");
                    var secondaryInfo = (ulong) row.GetColumnByName("SecondaryInfo");

                    // Check if item can be equipped.
                    if (primaryInfo == 0 && secondaryInfo == 0)
                        return;

                    // Belts. No longer exist in game + have no model despite having a setId.
                    var slotNum = (byte)row.GetColumnByName("SlotNum");
                    if (slotNum == 6) return;

                    // Has to have a valid name.
                    var name = (string)row.GetColumnByName("Name");
                    if (String.IsNullOrEmpty(name))
                        return;

                    var icon = (ushort)row.GetColumnByName("Icon");

                    var primaryMi = new XivGearModelInfo();
                    var secondaryMi = new XivGearModelInfo();
                    var xivGear = new XivGear
                    {
                        Name = name,
                        ExdID = item.Key,
                        PrimaryCategory = XivStrings.Gear,
                        ModelInfo = primaryMi,
                        IconNumber = icon,
                    };


                    xivGear.EquipSlotCategory = slotNum;
                    xivGear.SecondaryCategory = _slotNameDictionary.ContainsKey(slotNum) ? _slotNameDictionary[slotNum] : "Unknown";

                    // Model information is stored in a short-array format.
                    var primaryQuad = Quad.Read(BitConverter.GetBytes(primaryInfo), 0);
                    var secondaryQuad = Quad.Read(BitConverter.GetBytes(secondaryInfo), 0);

                    // If the model has a 3rd value, 2nd is body ID and variant ID is pushed to 3rd slot.
                    bool hasBodyId = primaryQuad.Values[2] > 0 ? true : false;
                    bool hasOffhand = secondaryQuad.Values[0] > 0 ? true : false;

                    primaryMi.PrimaryID = primaryQuad.Values[0];
                    secondaryMi.PrimaryID = secondaryQuad.Values[0];
                    if (hasBodyId)
                    {
                        primaryMi.SecondaryID = primaryQuad.Values[1];
                        primaryMi.ImcSubsetID = primaryQuad.Values[2];
                        secondaryMi.SecondaryID = secondaryQuad.Values[1];
                        secondaryMi.ImcSubsetID = secondaryQuad.Values[2];
                    }
                    else
                    {
                        primaryMi.ImcSubsetID = primaryQuad.Values[1];
                        secondaryMi.ImcSubsetID = secondaryQuad.Values[1];
                    }

                    XivGear secondaryItem = null;
                    if (secondaryMi.PrimaryID != 0)
                    {
                        // Make an entry for the offhand model.
                        secondaryItem = (XivGear)xivGear.Clone();
                        secondaryItem.ModelInfo = secondaryMi;
                        xivGear.Name += " - " + XivStrings.Main_Hand;
                        secondaryItem.Name += " - " + XivStrings.Off_Hand;
                        xivGear.PairedItem = secondaryItem;
                        secondaryItem.PairedItem = xivGear;
                        xivGear.SecondaryCategory = XivStrings.Dual_Wield;
                        secondaryItem.SecondaryCategory = XivStrings.Dual_Wield;

                    } else if(slotNum == 12)
                    {
                        // Make this the Right ring, and create the Left Ring entry.
                        secondaryItem = (XivGear)xivGear.Clone();

                        xivGear.Name += " - " + XivStrings.Right;
                        secondaryItem.Name += " - " + XivStrings.Left;

                        xivGear.PairedItem = secondaryItem;
                        secondaryItem.PairedItem = xivGear;
                    }

                    lock (_gearLock)
                    {
                        xivGearList.Add(xivGear);
                        if (secondaryItem != null)
                        {
                            xivGearList.Add(secondaryItem);
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw;
                }
            }));

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
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[4],
                ModelInfo = new XivGearModelInfo { PrimaryID = 0, ImcSubsetID = 1, SecondaryID = 0}
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Hands",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[5],
                ModelInfo = new XivGearModelInfo { PrimaryID = 0, ImcSubsetID = 1, SecondaryID = 0 }
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Legs",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[7],
                ModelInfo = new XivGearModelInfo { PrimaryID = 0, ImcSubsetID = 1, SecondaryID = 0 }
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Feet",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[8],
                ModelInfo = new XivGearModelInfo { PrimaryID = 0, ImcSubsetID = 1, SecondaryID = 0 }
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Body (NPC)",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[4],
                ModelInfo = new XivGearModelInfo { PrimaryID = 9903, ImcSubsetID = 1, SecondaryID = 0 }
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Hands (NPC)",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[5],
                ModelInfo = new XivGearModelInfo { PrimaryID = 9903, ImcSubsetID = 1, SecondaryID = 0 }
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Legs (NPC)",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[7],
                ModelInfo = new XivGearModelInfo { PrimaryID = 9903, ImcSubsetID = 1, SecondaryID = 0 }
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Feet (NPC)",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[8],
                ModelInfo = new XivGearModelInfo { PrimaryID = 9903, ImcSubsetID = 1, SecondaryID = 0 }
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Feet 2 (NPC)",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[8],
                ModelInfo = new XivGearModelInfo { PrimaryID = 9901, ImcSubsetID = 1, SecondaryID = 0 }
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
        public async Task<List<XivRace>> GetRacesForTextures(XivGear xivGear, XivDataFile dataFile)
        {
            // Get the material version for the item from the imc file
            var imc = new Imc(_gameDirectory);
            var gearVersion = (await imc.GetImcInfo(xivGear)).MaterialSet.ToString().PadLeft(4, '0');

            var modelID = xivGear.ModelInfo.PrimaryID.ToString().PadLeft(4, '0');

            var raceList = new List<XivRace>();

            var itemType = ItemType.GetPrimaryItemType(xivGear);
            string mtrlFolder;

            if (itemType == XivItemType.weapon)
            {
                return new List<XivRace> { XivRace.All_Races };
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
                        mtrlFile = $"mt_c{ID}e{modelID}_{xivGear.GetItemSlotAbbreviation()}_a.mtrl";
                        break;
                    case XivItemType.accessory:
                        mtrlFile = $"mt_c{ID}a{modelID}_{xivGear.GetItemSlotAbbreviation()}_a.mtrl";
                        break;
                    default:
                        mtrlFile = "";
                        break;
                }

                testFilesDictionary.Add(HashGenerator.GetHash(mtrlFile), ID);
            }

            // get the list of hashed file names from the mtrl folder
            var files = await _index.GetAllHashedFilesInFolder(HashGenerator.GetHash(mtrlFolder), dataFile);

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
        public async Task<List<XivRace>> GetRacesForModels(XivGear xivGear, XivDataFile dataFile)
        {
            var itemType = xivGear.GetPrimaryItemType();

            var modelID = xivGear.ModelInfo.PrimaryID.ToString().PadLeft(4, '0');

            var raceList = new List<XivRace>();

            if (itemType == XivItemType.weapon)
            {
                return new List<XivRace> { XivRace.All_Races };
            }

            string mdlFolder;
            var id = xivGear.ModelInfo.PrimaryID.ToString().PadLeft(4, '0');

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
                        mdlFile = $"c{ID}e{modelID}_{xivGear.GetItemSlotAbbreviation()}.mdl";
                        break;
                    case XivItemType.accessory:
                        mdlFile = $"c{ID}a{modelID}_{xivGear.GetItemSlotAbbreviation()}.mdl";
                        break;
                    default:
                        mdlFile = "";
                        break;
                }

                testFilesDictionary.Add(HashGenerator.GetHash(mdlFile), ID);
            }

            // get the list of hashed file names from the mtrl folder
            var files = await _index.GetAllHashedFilesInFolder(HashGenerator.GetHash(mdlFolder), dataFile);

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



        // A dictionary containing <Slot ID, Gear Category>
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
            {9, XivStrings.Earring },
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
            {21, XivStrings.Body_Legs_Feet },
            {22, XivStrings.Body_Hands }
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
            {"1501", XivRace.Hrothgar_Male},
            {"1504", XivRace.Hrothgar_Male_NPC},
            {"1601", XivRace.Hrothgar_Female},
            {"1604", XivRace.Hrothgar_Female_NPC},
            {"1701", XivRace.Viera_Male},
            {"1704", XivRace.Viera_Male_NPC},
            {"1801", XivRace.Viera_Female},
            {"1804", XivRace.Viera_Female_NPC},
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
            {XivStrings.Earring, "ear"},
            {XivStrings.Neck, "nek"},
            {XivStrings.Rings, "rir"},
            {XivStrings.Wrists, "wrs"},
            {XivStrings.Head_Body, "top"},
            {XivStrings.Body_Hands, "top"},
            {XivStrings.Body_Hands_Legs, "top"},
            {XivStrings.Body_Legs_Feet, "top"},
            {XivStrings.Body_Hands_Legs_Feet, "top"},
            {XivStrings.Legs_Feet, "dwn"},
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
            {"ear", XivStrings.Earring},
            {"nek", XivStrings.Neck},
            {"rir", XivStrings.Rings},
            {"wrs", XivStrings.Wrists},
        };
    }
}
