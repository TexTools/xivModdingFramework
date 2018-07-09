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
using xivModdingFramework.General.Enums;
using xivModdingFramework.Items;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Resources;

namespace xivModdingFramework.General
{
    public class ModelInfo
    {
        private readonly DirectoryInfo _gameDirectory;
        private readonly XivDataFile _dataFile;
        private const string MdlExtension = ".mdl";


        public ModelInfo(DirectoryInfo gameDirectory, XivDataFile dataFile)
        {
            _gameDirectory = gameDirectory;
            _dataFile = dataFile;
        }




        //public List<XivRace> GetModelRaces(IItemModel itemModel)
        //{
        //    var itemType = ItemType.GetItemType(itemModel);

        //    string mdlFolder = "", mdlFile = "";
        //    var id = itemModel.PrimaryModelInfo.ModelID.ToString().PadLeft(4, '0');
        //    var bodyVer = itemModel.PrimaryModelInfo.Body.ToString().PadLeft(4, '0');

        //    switch (itemType)
        //    {
        //        case XivItemType.equipment:
        //            mdlFolder = $"chara/{itemType}/e{id}/model";
        //            break;
        //        case XivItemType.accessory:
        //            mdlFolder = $"chara/{itemType}/a{id}/model";
        //            break;
        //        case XivItemType.human:
        //            if (itemModel.ItemCategory.Equals(XivStrings.Body))
        //            {
        //                mdlFolder = $"chara/{itemType}/c{id}/obj/body/b0001/model";
        //            }
        //            else if (itemModel.ItemCategory.Equals(XivStrings.Hair))
        //            {
        //                mdlFolder = $"chara/{itemType}/c{id}/obj/body/h0001/model";
        //            }
        //            else if (itemModel.ItemCategory.Equals(XivStrings.Face))
        //            {
        //                mdlFolder = $"chara/{itemType}/c{id}/obj/body/f0001/model";
        //            }
        //            else if (itemModel.ItemCategory.Equals(XivStrings.Tail))
        //            {
        //                mdlFolder = $"chara/{itemType}/c{id}/obj/body/t0001/model";
        //            }

        //            break;
        //        default:
        //            mdlFolder = "";
        //            mdlFile = "";
        //            break;
        //    }


        //    foreach (var raceID in IDRaceDictionary.Keys)
        //    {
        //        switch (itemType)
        //        {
        //            case XivItemType.equipment:
        //                mdlFile = $"c{raceID}e{id}_{SlotAbbreviationDictionary[itemModel.ItemCategory]}{MdlExtension}";
        //                break;
        //            case XivItemType.accessory:
        //                mdlFile = $"c{raceID}a{id}_{SlotAbbreviationDictionary[itemModel.ItemCategory]}{MdlExtension}";
        //                break;
        //            case XivItemType.human:
        //                if (itemModel.ItemCategory.Equals(XivStrings.Body))
        //                {
        //                    mdlFile = $"mt_c{raceID}b0001_{SlotAbbreviationDictionary[XivStrings.Body]}{MdlExtension}";
        //                }
        //                else if (itemModel.ItemCategory.Equals(XivStrings.Hair))
        //                {
        //                    mdlFile =
        //                        $"mt_c{id}h0001_{SlotAbbreviationDictionary[itemModel.ItemCategory]}_{part}{MdlExtension}";
        //                }
        //                else if (itemModel.ItemCategory.Equals(XivStrings.Face))
        //                {
        //                    mdlFile =
        //                        $"mt_c{id}f0001_{SlotAbbreviationDictionary[itemModel.ItemCategory]}_{part}{MdlExtension}";
        //                }
        //                else if (itemModel.ItemCategory.Equals(XivStrings.Tail))
        //                {
        //                    mdlFile = $"mt_c{id}t0001_{part}{MdlExtension}";
        //                }

        //                break;
        //            default:
        //                mdlFolder = "";
        //                mdlFile = "";
        //                break;
        //        }
        //    }
        //}

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
    }
}