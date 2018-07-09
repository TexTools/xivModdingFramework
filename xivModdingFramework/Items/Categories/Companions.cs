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
using xivModdingFramework.Exd.Enums;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.General;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Resources;

namespace xivModdingFramework.Items.Categories
{
    /// <summary>
    /// This class contains getters for the diffrent type of companions
    /// </summary>
    public class Companions
    {
        private readonly DirectoryInfo _gameDirectory;
        private readonly XivLanguage _xivLanguage;
        
        public Companions(DirectoryInfo gameDirectory, XivLanguage xivLanguage)
        {
            _gameDirectory = gameDirectory;
            _xivLanguage = xivLanguage;
        }

        /// <summary>
        /// Gets the list to be displayed in the Minion category
        /// </summary>
        /// <remarks>
        /// The model data for the minion is held separately in modelchara_0.exd
        /// The data within companion_0 exd contains a reference to the index for lookup in modelchara
        /// </remarks>
        /// <returns>A list containing XivMinion data</returns>
        public List<XivMinion> GetMinionList()
        {
            var minionList = new List<XivMinion>();

            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int dataLength = 48;
            const int nameDataOffset = 6;
            const int modelCharaIndexOffset = 16;

            var ex = new Ex(_gameDirectory, _xivLanguage);
            var minionEx = ex.ReadExData(XivEx.companion);

            // Loops through all available minions in the companion exd files
            // At present only one file exists (companion_0)
            foreach (var minion in minionEx.Values)
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
                    var nameString = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Encoding.UTF8.GetString(br.ReadBytes(nameOffset)).Replace("\0", ""));
                    xivMinion.Name = new string(nameString.Where(c => !char.IsControl(c)).ToArray());
                }

                if (modelCharaIndex == 0) continue;

                // This will get the model data using the index obtained for the current minion
                xivMinion.PrimaryModelInfo = XivModelChara.GetModelInfo(_gameDirectory, modelCharaIndex);

                minionList.Add(xivMinion);
            }

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
        public List<XivMount> GetMountList()
        {
            var mountList = new List<XivMount>();

            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int dataLength = 76;
            const int nameDataOffset = 6;
            const int modelCharaIndexOffset = 30;

            var ex = new Ex(_gameDirectory, _xivLanguage);
            var mountEx = ex.ReadExData(XivEx.mount);

            // Loops through all available mounts in the mount exd files
            // At present only one file exists (mount_0)
            foreach (var mount in mountEx.Values)
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
                    var nameString = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Encoding.UTF8.GetString(br.ReadBytes(nameOffset)).Replace("\0", ""));
                    xivMount.Name = new string(nameString.Where(c => !char.IsControl(c)).ToArray());
                }

                if(modelCharaIndex == 0 || xivMount.Name.Equals("")) continue;

                // This will get the model data using the index obtained for the current mount
                xivMount.PrimaryModelInfo = XivModelChara.GetModelInfo(_gameDirectory, modelCharaIndex);

                mountList.Add(xivMount);
            }

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
        public List<XivPet> GetPetList()
        {
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
                {7002, XivStrings.Carbuncle },
                {7003, XivStrings.Ifrit_Egi },
                {7004, XivStrings.Titan_Egi },
                {7005, XivStrings.Garuda_Egi },
                {7006, XivStrings.Ramuh_Egi },
                {7007, XivStrings.Sephirot_Egi },
                {7102, XivStrings.Bahamut_Egi },
                {7103, XivStrings.Placeholder_Egi },
            };

            // Loops through the list of indices containing Pet model data
            foreach (var petIndex in petModelIndexList)
            {
                var xivPet = new XivPet
                {
                    Category = XivStrings.Companions,
                    ItemCategory = XivStrings.Pets,
                };

                // Gets the model info from modelchara for the given index
                var modelInfo = XivModelChara.GetModelInfo(_gameDirectory, petIndex);

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
                            xivPet.Name = modelInfo.Body == 1 ? XivStrings.Rook_Autoturret : XivStrings.Bishop_Autoturret;
                            break;
                        default:
                            xivPet.Name = "Unknown";
                            break;
                    }
                }

                xivPet.ModelInfo = modelInfo;

                petList.Add(xivPet);
            }

            return petList;
        }
    }
}