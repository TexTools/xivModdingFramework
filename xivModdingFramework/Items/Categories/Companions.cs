﻿// xivModdingFramework
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
using System.Text.RegularExpressions;
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
        public async Task<List<XivMinion>> GetMinionList(string substring = null)
        {
            return await XivCache.GetCachedMinionsList(substring);
        }

        private static Dictionary<string, int> MountDataLengthByPatch = new Dictionary<string, int>()
        {
            { "6.0", 76 },
            { "6.1", 79 },
        };

        private static Dictionary<string, int> OrnamentDataLengthByPatch = new Dictionary<string, int>()
        {
            { "6.0", 28 },
            { "6.1", 31 },
        };



        /// <summary>
        /// Gets the list to be displayed in the Minion category
        /// </summary>
        /// <remarks>
        /// The model data for the minion is held separately in modelchara_0.exd
        /// The data within companion_0 exd contains a reference to the index for lookup in modelchara
        /// </remarks>
        /// <returns>A list containing XivMinion data</returns>
        public async Task<List<XivMinion>> GetUncachedMinionList()
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
                    PrimaryCategory = XivStrings.Companions,
                    SecondaryCategory = XivStrings.Minions
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

        public async Task<List<XivMount>> GetMountList(string substring = null, string category = null)
        {
            return await XivCache.GetCachedMountList(substring, category);
        }

        /// <summary>
        /// Gets the list to be displayed in the Mounts category
        /// </summary>
        /// <remarks>
        /// The model data for the minion is held separately in modelchara_0.exd
        /// The data within mount_0 exd contains a reference to the index for lookup in modelchara
        /// </remarks>
        /// <returns>A list containing XivMount data</returns>
        public async Task<List<XivMount>> GetUncachedMountList()
        {
            var mountLock = new object();
            var mountList = new List<XivMount>();

            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            int dataLength = MountDataLengthByPatch["6.1"];
            const int nameDataOffset = 6;
            const int modelCharaIndexOffset = 30;

            if (_xivLanguage == XivLanguage.Korean)
            {
                dataLength = MountDataLengthByPatch["6.0"];
            }
            else if (_xivLanguage == XivLanguage.Chinese)
            {
                dataLength = MountDataLengthByPatch["6.0"];
            }


            var mountEx = await _ex.ReadExData(XivEx.mount);
            var modelCharaEx = await XivModelChara.GetModelCharaData(_gameDirectory);

            // Loops through all available mounts in the mount exd files
            // At present only one file exists (mount_0)
            await Task.Run(() => Parallel.ForEach(mountEx.Values, (mount) =>
            {
                var xivMount = new XivMount
                {
                    PrimaryCategory = XivStrings.Companions,
                    SecondaryCategory = XivStrings.Mounts,
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
        /// Gets the list to be displayed in the Ornaments category
        /// </summary>
        /// <remarks>
        /// The model data for the ornament is held separately in modelchara_0.exd
        /// The data within Ornament exd contains a reference to the index for lookup in modelchara
        /// The data format used by Ornaments is identical to mounts so XivMount can be used to store the data
        /// </remarks>
        /// <returns>A list containing XivMount data</returns>
        public async Task<List<XivMount>> GetUncachedOrnamentList()
        {
            var mountLock = new object();
            var ornamentList = new List<XivMount>();

            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            int dataLength = OrnamentDataLengthByPatch["6.1"];
            const int nameDataOffset = 6;
            const int modelCharaIndexOffset = 16;

            if (_xivLanguage == XivLanguage.Korean)
            {
                dataLength = OrnamentDataLengthByPatch["6.0"];
            }
            else if (_xivLanguage == XivLanguage.Chinese)
            {
                dataLength = OrnamentDataLengthByPatch["6.0"];
            }

            var ornamentEx = await _ex.ReadExData(XivEx.ornament);
            var modelCharaEx = await XivModelChara.GetModelCharaData(_gameDirectory);

            // Loops through all available mounts in the mount exd files
            // At present only one file exists (mount_0)
            await Task.Run(() => Parallel.ForEach(ornamentEx.Values, (ornament) =>
            {
                var xivOrnament = new XivMount
                {
                    PrimaryCategory = XivStrings.Companions,
                    SecondaryCategory = XivStrings.Ornaments,
                };

                int modelCharaIndex;

                //Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(ornament)))
                {
                    br.BaseStream.Seek(nameDataOffset, SeekOrigin.Begin);
                    var nameOffset = br.ReadInt16();

                    br.BaseStream.Seek(modelCharaIndexOffset, SeekOrigin.Begin);
                    modelCharaIndex = br.ReadInt16();

                    br.BaseStream.Seek(dataLength, SeekOrigin.Begin);
                    var nameString =
                        CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Encoding.UTF8
                            .GetString(br.ReadBytes(nameOffset)).Replace("\0", ""));
                    xivOrnament.Name = new string(nameString.Where(c => !char.IsControl(c)).ToArray());
                }

                if (modelCharaIndex == 0 || xivOrnament.Name.Equals("")) return;

                // This will get the model data using the index obtained for the current mount
                xivOrnament.ModelInfo = XivModelChara.GetModelInfo(modelCharaEx, modelCharaIndex);

                lock (mountLock)
                {
                    ornamentList.Add(xivOrnament);
                }
            }));

            ornamentList.Sort();

            return ornamentList;
        }


        public async Task<List<XivPet>> GetPetList(string substring = null)
        {
            return await XivCache.GetCachedPetList(substring);

        }

        /// <summary>
        /// Gets the list to be displayed in the Pets category
        /// </summary>
        /// <remarks>
        /// The pet_0 exd does not contain the model ID or a reference to a modelchara, it contains names and other data
        /// Because of this, the Pet data is hardcoded until a better way of obtaining it is found.
        /// </remarks>
        /// <returns>A list containing XivMount data</returns>
        public async Task<List<XivPet>> GetUncachedPetList()
        {
            var petLock = new object();
            var petList = new List<XivPet>();

            // A list of indices in modelchara that contain Pet model data
            var petModelIndexList = new List<int>()
            {
                407, 408, 409, 410, 411, 412, 413,
                414, 415, 416, 417, 418, 537, 618,
                1027, 1028, 1430, 1930, 2023, 2619
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
                {7105, XivStrings.Seraph}
            };

            var modelCharaEx = await XivModelChara.GetModelCharaData(_gameDirectory);

            // Loops through the list of indices containing Pet model data
            await Task.Run(() => Parallel.ForEach(petModelIndexList, (petIndex) =>
            {
                var xivPet = new XivPet
                {
                    PrimaryCategory = XivStrings.Companions,
                    SecondaryCategory = XivStrings.Pets,
                };

                // Gets the model info from modelchara for the given index
                var modelInfo = XivModelChara.GetModelInfo(modelCharaEx, petIndex);

                // Finds the name of the pet using the model ID and the above dictionary
                if (petModelDictionary.ContainsKey(modelInfo.PrimaryID))
                {
                    var petName = petModelDictionary[modelInfo.PrimaryID];

                    if (modelInfo.ImcSubsetID > 1)
                    {
                        petName += $" {modelInfo.ImcSubsetID - 1}";
                    }

                    xivPet.Name = petName;
                }
                // For cases where there are separate pets under the same model ID with different body IDs
                else
                {
                    switch (modelInfo.PrimaryID)
                    {
                        case 7001:
                            xivPet.Name = modelInfo.SecondaryID == 1 ? XivStrings.Eos : XivStrings.Selene;
                            break;
                        case 7101:
                            xivPet.Name = modelInfo.SecondaryID == 1
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
            var parts = Constants.Alphabet;

            var equipPartDictionary = new Dictionary<string, char[]>();

            var index = new Index(_gameDirectory);
            var imc = new Imc(_gameDirectory);
            var version = (await imc.GetImcInfo(itemModel)).MaterialSet.ToString().PadLeft(4, '0');

            var id = itemModel.ModelInfo.PrimaryID.ToString().PadLeft(4, '0');
            var bodyVer = itemModel.ModelInfo.SecondaryID.ToString().PadLeft(4, '0');

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

            var id = itemModel.ModelInfo.PrimaryID.ToString().PadLeft(4, '0');
            var bodyVer = itemModel.ModelInfo.SecondaryID.ToString().PadLeft(4, '0');
            var root = itemModel.GetRoot();


            var mdlFolder = $"chara/demihuman/d{id}/obj/equipment/e{bodyVer}/model";

            var files = await index.GetAllHashedFilesInFolder(HashGenerator.GetHash(mdlFolder), XivDataFile._04_Chara);

            if (root == null || root.Info.Slot == null)
            {
                foreach (var slotAbr in SlotAbbreviationDictionary)
                {
                    var mdlFile = $"d{id}e{bodyVer}_{slotAbr.Value}.mdl";

                    if (files.Contains(HashGenerator.GetHash(mdlFile)))
                    {
                        equipPartList.Add(slotAbr.Key);
                    }
                }
            } else
            {
                var niceSlotName = SlotAbbreviationDictionary.FirstOrDefault(x => x.Value == root.Info.Slot).Key;
                if (!string.IsNullOrEmpty(niceSlotName))
                {
                    equipPartList.Add(niceSlotName);
                }
            }

            return equipPartList;
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
            {XivStrings.Earring, "ear"},
            {XivStrings.Neck, "nek"},
            {XivStrings.Rings, "rir"},
            {XivStrings.Wrists, "wrs"},
        };
    }
}