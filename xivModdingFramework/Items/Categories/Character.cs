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
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.Items.Categories
{
    public class Character
    {

        private readonly DirectoryInfo _gameDirectory;

        public Character(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
        }

        /// <summary>
        /// Gets the List to be displayed under the Character category
        /// </summary>
        /// <returns>A list containing XivCharacter data</returns>
        public List<XivCharacter> GetCharacterList()
        {
            var characterList = new List<XivCharacter>
            {
                new XivCharacter {Name = XivStrings.Body, Category = XivStrings.Character},
                new XivCharacter {Name = XivStrings.Face, Category = XivStrings.Character},
                new XivCharacter {Name = XivStrings.Hair, Category = XivStrings.Character},
                new XivCharacter {Name = XivStrings.Tail, Category = XivStrings.Character},
                new XivCharacter {Name = XivStrings.Face_Paint, Category = XivStrings.Character},
                new XivCharacter {Name = XivStrings.Equipment_Decals, Category = XivStrings.Character}
            };

            return characterList;
        }

        /// <summary>
        /// Gets the Races and Numbers available for textures in the given Character Item
        /// </summary>
        /// <remarks>
        /// This gives you the race and the numbers available for it
        /// <example>
        ///  Body, [Hyur Midlander Male, int[] {1, 4, 91, 250}
        /// </example>
        /// This means there is 4 body textures for Hyur Midlander Male with those numbers
        /// </remarks>
        /// <param name="charaItem">The Character Item</param>
        /// <returns>A Dictionary containing the race and the numbers available for it</returns>
        public Dictionary<XivRace, int[]> GetRacesAndNumbersForTextures(XivCharacter charaItem)
        {
            var availableRacesAndNumbers = new Dictionary<XivRace, int[]>();

            var index = new Index(_gameDirectory);

            var folder = "";

            if (charaItem.ItemCategory == XivStrings.Hair)
            {
                folder = XivStrings.HairMtrlFolder;
            }
            else if (charaItem.ItemCategory == XivStrings.Face)
            {
                folder = XivStrings.FaceMtrlFolder;
            }
            else if (charaItem.ItemCategory == XivStrings.Body)
            {
                folder = XivStrings.BodyMtrlFolder;
            }
            else if (charaItem.ItemCategory == XivStrings.Tail)
            {
                folder = XivStrings.TailMtrlFolder;
            }


            foreach (var race in IDRaceDictionary)
            {
                var testDictionary = new Dictionary<int, int>();

                for (var i = 1; i <= 300; i++)
                {
                    var mtrl = string.Format(folder, race.Key, i.ToString().PadLeft(4, '0'));

                    testDictionary.Add(HashGenerator.GetHash(mtrl), i);
                }

                var numList = index.GetFolderExistsList(testDictionary, XivDataFile._04_Chara);

                if (numList.Count > 0)
                {
                    availableRacesAndNumbers.Add(race.Value, numList.ToArray());
                }
            }

            return availableRacesAndNumbers;
        }


        /// <summary>
        /// Gets the Races and Numbers available for models in the given Character Item
        /// </summary>
        /// <remarks>
        /// This gives you the race and the numbers available for it
        /// <example>
        ///  Body, [Hyur Midlander Male, int[] {1, 2, 3, 5, 6}
        /// </example>
        /// This means there is 5 body models for Hyur Midlander Male with those numbers
        /// </remarks>
        /// <param name="charaItem">The Character Item</param>
        /// <returns>A Dictionary containing the race and the numbers available for it</returns>
        public Dictionary<XivRace, int[]> GetRacesAndNumbersForModels(XivCharacter charaItem)
        {
            var availableRacesAndNumbers = new Dictionary<XivRace, int[]>();

            var index = new Index(_gameDirectory);

            var folder = "";

            if (charaItem.ItemCategory == XivStrings.Hair)
            {
                folder = XivStrings.HairMDLFolder;
            }
            else if (charaItem.ItemCategory == XivStrings.Face)
            {
                folder = XivStrings.FaceMDLFolder;
            }
            else if (charaItem.ItemCategory == XivStrings.Body)
            {
                folder = XivStrings.BodyMDLFolder;
            }
            else if (charaItem.ItemCategory == XivStrings.Tail)
            {
                folder = XivStrings.TailMDLFolder;
            }


            foreach (var race in IDRaceDictionary)
            {
                var testDictionary = new Dictionary<int, int>();

                for (var i = 1; i <= 300; i++)
                {
                    var mtrl = string.Format(folder, race.Key, i.ToString().PadLeft(4, '0'));

                    testDictionary.Add(HashGenerator.GetHash(mtrl), i);
                }

                var numList = index.GetFolderExistsList(testDictionary, XivDataFile._04_Chara);

                if (numList.Count > 0)
                {
                    availableRacesAndNumbers.Add(race.Value, numList.ToArray());
                }
            }

            return availableRacesAndNumbers;
        }

        /// <summary>
        /// Gets the Type and Part for a given Character Item
        /// </summary>
        /// <remarks>
        /// Only Hair and Face Character Items have Types
        /// </remarks>
        /// <param name="charaItem">The character item</param>
        /// <param name="race">The race</param>
        /// <param name="num">The character item number</param>
        /// <returns>A dictionary containging [</returns>
        public Dictionary<string, char[]> GetTypePartForTextures(XivCharacter charaItem, XivRace race, int num)
        {
            var index = new Index(_gameDirectory);
            var typePartDictionary = new Dictionary<string, char[]>();

            var folder = "";
            var file = "";
            var typeDict = HairSlotAbbreviationDictionary;

            var parts = new[] {'a', 'b', 'c', 'd', 'e', 'f'};

            if (charaItem.ItemCategory == XivStrings.Hair)
            {
                folder = string.Format(XivStrings.HairMtrlFolder, race.GetRaceCode(), num.ToString().PadLeft(4, '0'));
                file = XivStrings.HairMtrlFile;
            }
            else if (charaItem.ItemCategory == XivStrings.Face)
            {
                folder = string.Format(XivStrings.FaceMtrlFolder, race.GetRaceCode(), num.ToString().PadLeft(4, '0'));
                typeDict = FaceSlotAbbreviationDictionary;
                file = XivStrings.FaceMtrlFile;
            }

            var fileList = index.GetAllHashedFilesInFolder(HashGenerator.GetHash(folder), XivDataFile._04_Chara);
 
            foreach (var type in typeDict)
            {
                var partList = new List<char>();

                foreach (var part in parts)
                {
                    var mtrlFile = string.Format(file, race.GetRaceCode(), num.ToString().PadLeft(4, '0'), type.Value, part);

                    if (fileList.Contains(HashGenerator.GetHash(mtrlFile)))
                    {
                        partList.Add(part);
                    }
                }

                if (partList.Count > 0)
                {
                    typePartDictionary.Add(type.Key, partList.ToArray());
                }
            }

            return typePartDictionary;
        }

        /// <summary>
        /// Gets the Part for a given Character Item
        /// </summary>
        /// <remarks>
        /// For Body and Tail Character Items since they don't have Types
        /// </remarks>
        /// <param name="charaItem">The character item</param>
        /// <param name="race">The race</param>
        /// <param name="num">The character item number</param>
        /// <returns>A dictionary containging [</returns>
        public char[] GetPartForTextures(XivCharacter charaItem, XivRace race, int num)
        {
            var index = new Index(_gameDirectory);

            var folder = "";
            var file = "";

            var parts = new[] { 'a', 'b', 'c', 'd', 'e', 'f' };

            if (charaItem.ItemCategory == XivStrings.Body)
            {
                folder = string.Format(XivStrings.BodyMtrlFolder, race.GetRaceCode(), num.ToString().PadLeft(4, '0'));
                file = XivStrings.BodyMtrlFile;
            }
            else if (charaItem.ItemCategory == XivStrings.Tail)
            {
                folder = string.Format(XivStrings.TailMtrlFolder, race.GetRaceCode(), num.ToString().PadLeft(4, '0'));
                file = XivStrings.TailMtrlFile;
            }

            var fileList = index.GetAllHashedFilesInFolder(HashGenerator.GetHash(folder), XivDataFile._04_Chara);

            return (from part in parts let mtrlFile = string.Format(file, race.GetRaceCode(), num.ToString().PadLeft(4, '0'), part)
                where fileList.Contains(HashGenerator.GetHash(mtrlFile)) select part).ToArray();
        }

        /// <summary>
        /// Gets the Type of models for a given Character Item
        /// </summary>
        /// <param name="charaItem">The character item</param>
        /// <param name="race">The race</param>
        /// <param name="num">The character item number</param>
        /// <returns>A dictionary containging [</returns>
        public List<string> GetTypeForModels(XivCharacter charaItem, XivRace race, int num)
        {
            var index = new Index(_gameDirectory);

            var folder = "";
            var file = "";
            var typeDict = HairSlotAbbreviationDictionary;

            if (charaItem.ItemCategory == XivStrings.Body)
            {
                folder = string.Format(XivStrings.BodyMDLFolder, race.GetRaceCode(), num.ToString().PadLeft(4, '0'));
                typeDict = BodySlotAbbreviationDictionary;
                file = XivStrings.BodyMDLFile;
            }
            else if (charaItem.ItemCategory == XivStrings.Hair)
            {
                folder = string.Format(XivStrings.HairMDLFolder, race.GetRaceCode(), num.ToString().PadLeft(4, '0'));
                typeDict = HairSlotAbbreviationDictionary;
                file = XivStrings.HairMDLFile;
            }
            else if (charaItem.ItemCategory == XivStrings.Face)
            {
                folder = string.Format(XivStrings.FaceMDLFolder, race.GetRaceCode(), num.ToString().PadLeft(4, '0'));
                typeDict = FaceSlotAbbreviationDictionary;
                file = XivStrings.FaceMDLFile;
            }
            else if (charaItem.ItemCategory == XivStrings.Tail)
            {
                folder = string.Format(XivStrings.TailMDLFolder, race.GetRaceCode(), num.ToString().PadLeft(4, '0'));
                typeDict = TailSlotAbbreviationDictionary;
                file = XivStrings.TailMDLFile;
            }

            var fileList = index.GetAllHashedFilesInFolder(HashGenerator.GetHash(folder), XivDataFile._04_Chara);

            var typeList = new List<string>();
            foreach (var type in typeDict)
            {
                var mdlFile = string.Format(file, race.GetRaceCode(), num.ToString().PadLeft(4, '0'), type.Value);

                if (fileList.Contains(HashGenerator.GetHash(mdlFile)))
                {
                    typeList.Add(type.Key);
                }
            }

            return typeList;
        }

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
        private static readonly Dictionary<string, string> FaceSlotAbbreviationDictionary = new Dictionary<string, string>
        {
            {XivStrings.Face, "fac"},
            {XivStrings.Iris, "iri"},
            {XivStrings.Etc, "etc"}

        };

        /// <summary>
        /// A dictionary containing slot data in the format [Slot Name, Slot abbreviation]
        /// </summary>
        private static readonly Dictionary<string, string> HairSlotAbbreviationDictionary = new Dictionary<string, string>
        {
            {XivStrings.Accessory, "acc"},
            {XivStrings.Hair, "hir"}
        };

        /// <summary>
        /// A dictionary containing slot data in the format [Slot Name, Slot abbreviation]
        /// </summary>
        private static readonly Dictionary<string, string> BodySlotAbbreviationDictionary = new Dictionary<string, string>
        {
            {XivStrings.Head, "met"},
            {XivStrings.Hands, "glv"},
            {XivStrings.Legs, "dwn"},
            {XivStrings.Feet, "sho"},
            {XivStrings.Body, "top"}
        };

        /// <summary>
        /// A dictionary containing slot data in the format [Slot Name, Slot abbreviation]
        /// </summary>
        private static readonly Dictionary<string, string> TailSlotAbbreviationDictionary = new Dictionary<string, string>
        {
            {XivStrings.Tail, "til"},
            {XivStrings.Etc, "etc"}
        };
    }
}