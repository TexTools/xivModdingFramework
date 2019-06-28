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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
using xivModdingFramework.Variants.FileTypes;

namespace xivModdingFramework.Items.Categories
{
    /// <summary>
    /// This class contains getters for the diffrent type of companions
    /// </summary>
    public class Companions
    {
        private readonly DirectoryInfo _gameDirectory;
        private readonly XivLanguage _xivLanguage;
        private readonly Ex _ex;

        public Companions(DirectoryInfo gameDirectory, XivLanguage xivLanguage)
        {
            _gameDirectory = gameDirectory;
            _xivLanguage = xivLanguage;
            _ex = new Ex(_gameDirectory, _xivLanguage);
        }

        /// <summary>
        /// Gets the list to be displayed in the Minion category
        /// </summary>
        /// <remarks>
        /// The model data for the minion is held separately in modelchara_0.exd
        /// The data within companion_0 exd contains a reference to the index for lookup in modelchara
        /// </remarks>
        /// <returns>A list containing XivMinion data</returns>
        public async Task<List<XivMinion>> GetMinionList()
        {
            var minionLock = new object();
            var minionList = new List<XivMinion>();

            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int dataLength = 48;
            const int nameDataOffset = 6;
            const int modelCharaIndexOffset = 16;

            var minionEx = await _ex.ReadExData(XivEx.companion);
            var modelCharaEx = await XivModelChara.GetModelCharaData(_gameDirectory);

            // Loops through all available minions in the companion exd files
            // At present only one file exists (companion_0)
            await Task.Run(() => Parallel.ForEach(minionEx.Values, (minion) =>
            {
                var xivMinion = new XivMinion
                {
                    Category = XivStrings.Companions,
                    ItemCategory = XivStrings.Minions
                };

                int modelCharaIndex;

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(minion)))
                {
                    br.BaseStream.Seek(nameDataOffset, SeekOrigin.Begin);
                    var nameOffset = br.ReadInt16();

                    br.BaseStream.Seek(modelCharaIndexOffset, SeekOrigin.Begin);
                    modelCharaIndex = br.ReadInt16();

                    br.BaseStream.Seek(dataLength, SeekOrigin.Begin);
                    var nameString =
                        CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Encoding.UTF8
                            .GetString(br.ReadBytes(nameOffset)).Replace("\0", ""));
                    xivMinion.Name = new string(nameString.Where(c => !char.IsControl(c)).ToArray());
                }

                if (modelCharaIndex == 0) return;

                // This will get the model data using the index obtained for the current minion
                xivMinion.ModelInfo = XivModelChara.GetModelInfo(modelCharaEx, modelCharaIndex);

                lock (minionLock)
                {
                    minionList.Add(xivMinion);
                }
            }));

            minionList.Sort();

            return minionList;
        }

        /// <summary>
        /// Gets the list to be displayed in the Mounts category
        /// </summary>
        /// <remarks>
        /// The model data for the minion is held separately in modelchara_0.exd
        /// The data within mount_0 exd contains a reference to the index for lookup in modelchara
        /// </remarks>
        /// <returns>A list containing XivMount data</returns>
        public async Task<List<XivMount>> GetMountList()
        {
            var mountLock = new object();
            var mountList = new List<XivMount>();

            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int dataLength = 76;
            const int nameDataOffset = 6;
            const int modelCharaIndexOffset = 30;

            var mountEx = await _ex.ReadExData(XivEx.mount);
            var modelCharaEx = await XivModelChara.GetModelCharaData(_gameDirectory);

            // Loops through all available mounts in the mount exd files
            // At present only one file exists (mount_0)
            await Task.Run(() => Parallel.ForEach(mountEx.Values, (mount) =>
            {
                var xivMount = new XivMount
                {
                    Category = XivStrings.Companions,
                    ItemCategory = XivStrings.Mounts,
                };

                int modelCharaIndex;

                //Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(mount)))
                {
                    br.BaseStream.Seek(nameDataOffset, SeekOrigin.Begin);
                    var nameOffset = br.ReadInt16();

                    br.BaseStream.Seek(modelCharaIndexOffset, SeekOrigin.Begin);
                    modelCharaIndex = br.ReadInt16();

                    br.BaseStream.Seek(dataLength, SeekOrigin.Begin);
                    var nameString =
                        CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Encoding.UTF8
                            .GetString(br.ReadBytes(nameOffset)).Replace("\0", ""));
                    xivMount.Name = new string(nameString.Where(c => !char.IsControl(c)).ToArray());
                }

                if (modelCharaIndex == 0 || xivMount.Name.Equals("")) return;

                // This will get the model data using the index obtained for the current mount
                xivMount.ModelInfo = XivModelChara.GetModelInfo(modelCharaEx, modelCharaIndex);

                lock (mountLock)
                {
                    mountList.Add(xivMount);
                }
            }));

            mountList.Sort();

            return mountList;
        }

        /// <summary>
        /// Gets the list to be displayed in the Pets category
        /// </summary>
        /// <remarks>
        /// The pet_0 exd does not contain the model ID or a reference to a modelchara, it contains names and other data
        /// Because of this, the Pet data is hardcoded until a better way of obtaining it is found.
        /// </remarks>
        /// <returns>A list containing XivMount data</returns>
        public async Task<List<XivPet>> GetPetList()
        {
            var petLock = new object();
            var petList = new List<XivPet>();

            // A list of indices in modelchara that contain Pet model data
            var petModelIndexList = new List<int>()
            {
                407, 408, 409, 410, 411, 412, 413,
                414, 415, 416, 417, 418, 537, 618,
                1027, 1028, 1430, 1930, 2023
            };

            // A dictionary consisting of <model ID, Pet Name>
            // All pet IDs are in the 7000 range
            // This list does not contain Pets that are separate but share the same ID (Fairy, Turrets)
            var petModelDictionary = new Dictionary<int, string>()
            {
                {7002, XivStrings.Carbuncle},
                {7003, XivStrings.Ifrit_Egi},
                {7004, XivStrings.Titan_Egi},
                {7005, XivStrings.Garuda_Egi},
                {7006, XivStrings.Ramuh_Egi},
                {7007, XivStrings.Sephirot_Egi},
                {7102, XivStrings.Bahamut_Egi},
                {7103, XivStrings.Placeholder_Egi},
            };

            var modelCharaEx = await XivModelChara.GetModelCharaData(_gameDirectory);

            // Loops through the list of indices containing Pet model data
            await Task.Run(() => Parallel.ForEach(petModelIndexList, (petIndex) =>
            {
                var xivPet = new XivPet
                {
                    Category = XivStrings.Companions,
                    ItemCategory = XivStrings.Pets,
                };

                // Gets the model info from modelchara for the given index
                var modelInfo = XivModelChara.GetModelInfo(modelCharaEx, petIndex);

                // Finds the name of the pet using the model ID and the above dictionary
                if (petModelDictionary.ContainsKey(modelInfo.ModelID))
                {
                    var petName = petModelDictionary[modelInfo.ModelID];

                    if (modelInfo.Variant > 1)
                    {
                        petName += $" {modelInfo.Variant - 1}";
                    }

                    xivPet.Name = petName;
                }
                // For cases where there are separate pets under the same model ID with different body IDs
                else
                {
                    switch (modelInfo.ModelID)
                    {
                        case 7001:
                            xivPet.Name = modelInfo.Body == 1 ? XivStrings.Eos : XivStrings.Selene;
                            break;
                        case 7101:
                            xivPet.Name = modelInfo.Body == 1
                                ? XivStrings.Rook_Autoturret
                                : XivStrings.Bishop_Autoturret;
                            break;
                        default:
                            xivPet.Name = "Unknown";
                            break;
                    }
                }

                xivPet.ModelInfo = modelInfo;

                lock (petLock)
                {
                    petList.Add(xivPet);
                }
            }));

            petList.Sort();

            return petList;
        }

        public async Task<Dictionary<string, char[]>> GetDemiHumanMountTextureEquipPartList(IItemModel itemModel)
        {
            var parts = new[] { 'a', 'b', 'c', 'd', 'e', 'f' };

            var equipPartDictionary = new Dictionary<string, char[]>();

            var index = new Index(_gameDirectory);
            var imc = new Imc(_gameDirectory, XivDataFile._04_Chara);
            var version = (await imc.GetImcInfo(itemModel, itemModel.ModelInfo)).Version.ToString().PadLeft(4, '0');

            var id = itemModel.ModelInfo.ModelID.ToString().PadLeft(4, '0');
            var bodyVer = itemModel.ModelInfo.Body.ToString().PadLeft(4, '0');

            var mtrlFolder = $"chara/demihuman/d{id}/obj/equipment/e{bodyVer}/material/v{version}";

            var files = await index.GetAllHashedFilesInFolder(HashGenerator.GetHash(mtrlFolder), XivDataFile._04_Chara);

            foreach (var slotAbr in SlotAbbreviationDictionary)
            {
                var charList =
                    (from part in parts
                        let mtrlFile = $"mt_d{id}e{bodyVer}_{slotAbr.Value}_{part}.mtrl"
                        where files.Contains(HashGenerator.GetHash(mtrlFile))
                        select part).ToList();

                if (charList.Count > 0)
                {
                    equipPartDictionary.Add(slotAbr.Key, charList.ToArray());
                }
            }

            return equipPartDictionary;
        }

        public async Task<List<string>> GetDemiHumanMountModelEquipPartList(IItemModel itemModel)
        {
            var equipPartList = new List<string>();

            var index = new Index(_gameDirectory);

            var id = itemModel.ModelInfo.ModelID.ToString().PadLeft(4, '0');
            var bodyVer = itemModel.ModelInfo.Body.ToString().PadLeft(4, '0');

            var mdlFolder = $"chara/demihuman/d{id}/obj/equipment/e{bodyVer}/model";

            var files = await index.GetAllHashedFilesInFolder(HashGenerator.GetHash(mdlFolder), XivDataFile._04_Chara);

            foreach (var slotAbr in SlotAbbreviationDictionary)
            {
                var mdlFile = $"d{id}e{bodyVer}_{slotAbr.Value}.mdl";

                if (files.Contains(HashGenerator.GetHash(mdlFile)))
                {
                    equipPartList.Add(slotAbr.Key);
                }
            }

            return equipPartList;
        }

        /// <summary>
        /// Searches for monsters with the given model ID
        /// </summary>
        /// <param name="modelID">The ID of the monster model</param>
        /// <param name="type">The type of monster to look for</param>
        /// <returns>A list of Search Results</returns>
        public async Task<List<SearchResults>> SearchMonstersByModelID(int modelID, XivItemType type)
        {
            var monsterSearchLock = new object();
            var monsterSearchLock1 = new object();
            var searchResultsList = new List<SearchResults>();
            var index = new Index(_gameDirectory);
            var id = modelID.ToString().PadLeft(4, '0');

            var bodyVariantDictionary = new Dictionary<int, List<int>>();

            if (type == XivItemType.monster)
            {
                var folder = $"chara/monster/m{id}/obj/body/b";

                await Task.Run(() => Parallel.For(0, 100, (i) =>
                {
                    var folderHashDictionary = new Dictionary<int, int>();

                    var mtrlFolder = $"{folder}{i.ToString().PadLeft(4, '0')}/material/v";

                    for (var j = 1; j < 100; j++)
                    {
                        lock (monsterSearchLock)
                        {
                            folderHashDictionary.Add(HashGenerator.GetHash($"{mtrlFolder}{j.ToString().PadLeft(4, '0')}"),
                                j);
                        }
                    }

                    var variantList = index.GetFolderExistsList(folderHashDictionary, XivDataFile._04_Chara).Result;

                    if (variantList.Count > 0)
                    {
                        lock (monsterSearchLock1)
                        {
                            variantList.Sort();
                            bodyVariantDictionary.Add(i, variantList);
                        }

                    }
                }));
            }
            else if (type == XivItemType.demihuman)
            {
                var folder = $"chara/demihuman/d{id}/obj/equipment/e";

                await Task.Run(() => Parallel.For(0, 100, (i) =>
                {
                    var folderHashDictionary = new Dictionary<int, int>();

                    var mtrlFolder = $"{folder}{i.ToString().PadLeft(4, '0')}/material/v";

                    for (var j = 1; j < 100; j++)
                    {
                        lock (monsterSearchLock)
                        {
                            folderHashDictionary.Add(HashGenerator.GetHash($"{mtrlFolder}{j.ToString().PadLeft(4, '0')}"),
                                j);
                        }
                    }

                    var variantList = index.GetFolderExistsList(folderHashDictionary, XivDataFile._04_Chara).Result;

                    if (variantList.Count > 0)
                    {
                        lock (monsterSearchLock1)
                        {
                            variantList.Sort();
                            bodyVariantDictionary.Add(i, variantList);
                        }
                    }
                }));
            }

            foreach (var bodyVariant in bodyVariantDictionary)
            {
                foreach (var variant in bodyVariant.Value)
                {
                    searchResultsList.Add(new SearchResults { Body = bodyVariant.Key.ToString(), Slot = XivStrings.Monster, Variant = variant });
                }
            }

            searchResultsList.Sort();

            return searchResultsList;
        }

        /// <summary>
        /// A dictionary containing the slot abbreviations in the format [equipment slot, slot abbreviation]
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
        };
    }
}