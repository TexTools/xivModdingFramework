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

        private static Dictionary<string, int> DataLengthByPatch = new Dictionary<string, int>()
        {
            { "5.3", 160 },
            { "5.4", 168 },
            { "5.5", 160 },
        };

        private static Dictionary<string, int> SlotDataOffsetByPatch = new Dictionary<string, int>()
        {
            { "5.3", 154 },
            { "5.4", 156 },
            { "5.5", 154 },
        };


        /// <summary>
        /// A getter for available gear in the Item exd files
        /// </summary>
        /// <returns>A list containing XivGear data</returns>
        public async Task<List<XivGear>> GetUnCachedGearList()
        {
            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int modelDataCheckOffset = 30;
            int dataLength = DataLengthByPatch["5.5"];
            const int nameDataOffset = 14;
            const int modelDataOffset = 24;
            const int iconDataOffset = 136;
            int slotDataOffset = SlotDataOffsetByPatch["5.5"];


            if( _xivLanguage == XivLanguage.Korean)
            {
                dataLength = DataLengthByPatch["5.5"];
                slotDataOffset = SlotDataOffsetByPatch["5.5"];
            }
            else if (_xivLanguage == XivLanguage.Chinese)
            {
                dataLength = DataLengthByPatch["5.5"];
                slotDataOffset = SlotDataOffsetByPatch["5.5"];
            }

            var xivGearList = new List<XivGear>();

            xivGearList.AddRange(GetMissingGear());

            var ex = new Ex(_gameDirectory, _xivLanguage);
            var itemDictionary = await ex.ReadExData(XivEx.item);

            // Loops through all the items in the item exd files
            // Item files start at 0 and increment by 500 for each new file
            // Item_0, Item_500, Item_1000, etc.
            await Task.Run(() => Parallel.ForEach(itemDictionary, (item) =>
            {
                try
                {
                    // This checks whether there is any model data present in the current item
                    if (item.Value[modelDataCheckOffset] <= 0 && item.Value[modelDataCheckOffset + 1] <= 0) return;

                    var primaryMi = new XivGearModelInfo();
                    var secondaryMi = new XivGearModelInfo();
                    var hasSecondary = false;

                    var xivGear = new XivGear
                    {
                        ExdID = item.Key,
                        PrimaryCategory = XivStrings.Gear,
                        ModelInfo = primaryMi,
                    };

                    /* Used to determine if the given model is a weapon
                     * This is important because the data is formatted differently
                     * The model data is a 16 byte section separated into two 8 byte parts (primary model, secondary model)
                     * Format is 8 bytes in length with 2 bytes per data point [short, short, short, short]
                     * Gear: primary model [blank, blank, variant, ID] nothing in secondary model
                     * Weapon: primary model [blank, variant, body, ID] secondary model [blank, variant, body, ID]
                    */
                    var isWeapon = false;

                    // Big Endian Byte Order 
                    using (var br = new BinaryReaderBE(new MemoryStream(item.Value)))
                    {
                        br.BaseStream.Seek(nameDataOffset, SeekOrigin.Begin);
                        var nameOffset = br.ReadInt16();

                        // Model Data
                        br.BaseStream.Seek(modelDataOffset, SeekOrigin.Begin);

                        // Primary Model Key
                        primaryMi.ModelKey = Quad.Read(br.ReadBytes(8), 0);
                        br.BaseStream.Seek(-8, SeekOrigin.Current);

                        // Primary Blank
                        var unused = br.ReadInt16();

                        // Primary Variant for weapon, blank otherwise
                        var weaponVariant = br.ReadInt16();

                        if (weaponVariant != 0)
                        {
                            primaryMi.ImcSubsetID = weaponVariant;
                            primaryMi.IsWeapon = true;
                            isWeapon = true;
                        }

                        // Primary Body if weapon, Variant otherwise
                        if (isWeapon)
                        {
                            primaryMi.SecondaryID = br.ReadInt16();
                        }
                        else
                        {
                            primaryMi.ImcSubsetID = br.ReadInt16();
                        }

                        // Primary Model ID
                        primaryMi.PrimaryID = br.ReadInt16();

                        // Secondary Model Key
                        isWeapon = false;
                        secondaryMi.ModelKey = Quad.Read(br.ReadBytes(8), 0);
                        br.BaseStream.Seek(-8, SeekOrigin.Current);

                        // Secondary Blank
                        var unused2 = br.ReadInt16();

                        // Secondary Variant for weapon, blank otherwise
                        weaponVariant = br.ReadInt16();

                        if (weaponVariant != 0)
                        {
                            secondaryMi.ImcSubsetID = weaponVariant;
                            secondaryMi.IsWeapon = true;
                            isWeapon = true;
                        }

                        // Secondary Body if weapon, Variant otherwise
                        if (isWeapon)
                        {
                            secondaryMi.SecondaryID = br.ReadInt16();
                        }
                        else
                        {
                            secondaryMi.ImcSubsetID = br.ReadInt16();
                        }

                        // Secondary Model ID
                        secondaryMi.PrimaryID = br.ReadInt16();

                        // Icon
                        br.BaseStream.Seek(iconDataOffset, SeekOrigin.Begin);
                        xivGear.IconNumber = br.ReadUInt16();

                        // Gear Slot/Category
                        br.BaseStream.Seek(slotDataOffset, SeekOrigin.Begin);
                        int slotNum = br.ReadByte();

                        // Waist items do not have texture or model data
                        if (slotNum == 6) return;

                        xivGear.EquipSlotCategory = slotNum;
                        xivGear.SecondaryCategory = _slotNameDictionary.ContainsKey(slotNum) ? _slotNameDictionary[slotNum] : "Unknown";

                        // Gear Name
                        var gearNameOffset = dataLength + nameOffset;
                        var gearNameLength = item.Value.Length - gearNameOffset;
                        br.BaseStream.Seek(gearNameOffset, SeekOrigin.Begin);
                        var nameString = Encoding.UTF8.GetString(br.ReadBytes(gearNameLength)).Replace("\0", "");
                        xivGear.Name = new string(nameString.Where(c => !char.IsControl(c)).ToArray());
                        xivGear.Name = xivGear.Name.Trim();

                        // If we have a secondary model

                        XivGear secondaryItem = null;
                        if (secondaryMi.PrimaryID != 0)
                        {
                            // Make a new item for it.
                            secondaryItem = (XivGear)xivGear.Clone();
                            secondaryItem.ModelInfo = secondaryMi;
                            xivGear.Name += " - " + XivStrings.Main_Hand;
                            secondaryItem.Name += " - " + XivStrings.Off_Hand;
                            xivGear.PairedItem = secondaryItem;
                            secondaryItem.PairedItem = xivGear;
                            xivGear.SecondaryCategory = XivStrings.Dual_Wield;
                            secondaryItem.SecondaryCategory = XivStrings.Dual_Wield;
                        }

                        // Rings
                        if(slotNum == 12)
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
                } catch(Exception ex)
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
            {16, XivStrings.Body_Hands },
            {17, XivStrings.Body_Hands_Legs },
            {18, XivStrings.Body_Legs_Feet },
            {19, XivStrings.Body_Hands_Legs_Feet },
            {20, XivStrings.Legs_Feet },
            {21, XivStrings.Soul_Crystal },
            {22, XivStrings.All }
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
