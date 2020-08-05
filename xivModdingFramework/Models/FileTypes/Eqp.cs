using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.Models.FileTypes
{
    public class Eqp
    {
        public const string EquipmentParameterExtension = "edp";
        public const string EquipmentParameterFile = "chara/xls/equipmentparameter/equipmentparameter.eqp";
        public const string EquipmentDeformerParameterExtension = "eqdp";
        public const string EquipmentDeformerParameterRootPath = "chara/xls/charadb/equipmentdeformerparameter/";
        public const string AccessoryDeformerParameterRootPath = "chara/xls/charadb/accessorydeformerparameter/";

        private readonly DirectoryInfo _gameDirectory;
        private readonly DirectoryInfo _modListDirectory;

        // Full EQP entries are 8 bytes long.
        public const int EquipmentParameterEntrySize = 8;

        // Full EQDP entries are 2 bytes long.
        public const int EquipmentDeformerParameterEntrySize = 2;
        public const int EquipmentDeformerParameterHeaderLength = 320;


        // The subset list of races that actually have deformation files.
        public static readonly List<XivRace> DeformationAvailableRaces = new List<XivRace>()
        {
            XivRace.Hyur_Midlander_Male,
            XivRace.Hyur_Midlander_Female,
            XivRace.Hyur_Highlander_Male,
            XivRace.Hyur_Highlander_Female,
            XivRace.Elezen_Male,
            XivRace.Elezen_Female,
            XivRace.Miqote_Male,
            XivRace.Miqote_Female,
            XivRace.Roegadyn_Male,
            XivRace.Roegadyn_Female,
            XivRace.Lalafell_Male,
            XivRace.Lalafell_Female,
            XivRace.AuRa_Male,
            XivRace.AuRa_Female,
            XivRace.Hrothgar,
            XivRace.Viera,
        };

        private Dat _dat;

        public Eqp(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
            _dat = new Dat(_gameDirectory);
            _modListDirectory = new DirectoryInfo(Path.Combine(gameDirectory.Parent.Parent.FullName, XivStrings.ModlistFilePath));
        }

        public async Task<EquipmentParameterSet> GetEquipmentParameters(int equipmentId)
        {
            throw new NotImplementedException("Not Yet Implemented.");
        }

        private static readonly Regex _eqpBinaryOffsetRegex = new Regex(Constants.BinaryOffsetMarker + "([0-9]+)$");
        public async Task<EquipmentParameter> GetEqpEntry(string pathWithOffset)
        {
            var match = _eqpBinaryOffsetRegex.Match(pathWithOffset);
            if (!match.Success) return null;

            var bitOffset = Int32.Parse(match.Groups[1].Value);
            var byteOffset = bitOffset / 8;

            var setId = byteOffset / EquipmentParameterEntrySize;
            var slotOffset = byteOffset % EquipmentParameterEntrySize;

            var slotKv = EquipmentParameterSet.EntryOffsets.Reverse().First(x => x.Value <= slotOffset);
            var slot = slotKv.Key;
            var slotByteOffset = slotKv.Value;

            var size = EquipmentParameterSet.EntrySizes[slot];

            var offset = (setId * EquipmentParameterEntrySize) + slotByteOffset;

            var file = await LoadEquipmentParameterFile();

            var bytes = file.Skip(offset).Take(size);

            return new EquipmentParameter(slot, bytes.ToArray());

        }

        /// <summary>
        /// Get the raw bytes for the equipment parameters for a given equipment set.
        /// </summary>
        /// <param name="equipmentId"></param>
        /// <returns></returns>
        public async Task<BitArray> GetRawEquipmentParameters(int equipmentId)
        {
            var data = await LoadEquipmentParameterFile();
            var start = (equipmentId * EquipmentParameterEntrySize);
            var parameters = new byte[EquipmentParameterEntrySize];


            // This item doesn't have equipment parameters.
            if(start >= data.Length)
            {
                return null;
            }

            // 8 Bytes
            for (var idx = 0; idx < EquipmentParameterEntrySize; idx++)
            {
                parameters[idx] = data[start + idx];
            }

            return new BitArray(parameters);
        }

        /// <summary>
        /// Gets the raw equipment parameter file.
        /// </summary>
        /// <returns></returns>
        private async Task<byte[]> LoadEquipmentParameterFile()
        {
            return await _dat.GetType2Data(EquipmentParameterFile, false);
        }


        public async Task SaveRawEquipmentParameters(int equipmentId, BitArray entry) {
            var file = new List<byte>(await _dat.GetType2Data(EquipmentParameterFile, false)).ToArray();


            var start = (equipmentId * EquipmentParameterEntrySize);
            entry.CopyTo(file, start);

            await SaveEquipmentParameterFile(file);


        }

        private async Task SaveEquipmentParameterFile(byte[] file)
        {

            await _dat.ImportType2Data(file, "EquipmentParameterFile", EquipmentParameterFile, "Internal", "TexTools");

            return;
        }




        private static readonly Regex _eqdpBinaryOffsetRegex = new Regex("^(.*c([0-9]{4}).*)" + Constants.BinaryOffsetMarker + "([0-9]+)$");

        /// <summary>
        /// Retrieves the raw EQDP entries from an arbitrary selection of files/offsets.
        /// </summary>
        /// <param name="pathsWithOffsets"></param>
        /// <returns></returns>
        public async Task<Dictionary<XivRace, EquipmentDeformationParameter>> GetEqdpEntries(List<string> pathsWithOffsets)
        {
            var ret = new Dictionary<XivRace, EquipmentDeformationParameter>();
            foreach(var path in pathsWithOffsets)
            {
                var match = _eqdpBinaryOffsetRegex.Match(path);

                // Invalid format.
                if (!match.Success) continue;

                var file = match.Groups[1].Value;
                var race = XivRaces.GetXivRace(match.Groups[2].Value);
                var bitOffset = Int32.Parse(match.Groups[3].Value);

                // 2 Bytes per set.
                int setId = bitOffset / (EquipmentDeformerParameterEntrySize * 8);

                var slotOffset = (bitOffset % (EquipmentDeformerParameterEntrySize * 8)) / EquipmentDeformerParameterEntrySize;
                var accessory = file.Contains("accessory");
                var list = EquipmentDeformationParameterSet.SlotsAsList(accessory);
                var slot = list[slotOffset];

                var set = await GetEquipmentDeformationSet(setId, race, accessory);

                ret.Add(race, set.Parameters[slot]);

            }
            return ret;
        }

        /// <summary>
        /// Get all the available models for a given piece of equipment.
        /// </summary>
        /// <param name="equipmentId"></param>
        /// <param name="accessory"></param>
        /// <returns></returns>
        public async Task<List<XivRace>> GetAvailableRacialModels(int equipmentId, string slot)
        {
            var isAccessory = EquipmentDeformationParameterSet.SlotsAsList(true).Contains(slot);

            if(!isAccessory)
            {
                var slotOk = EquipmentDeformationParameterSet.SlotsAsList(false).Contains(slot);
                if(!slotOk)
                {
                    throw new InvalidDataException("Attempted to get racial models for invalid slot.");
                }
            }

            var sets = await GetAllEquipmentDeformationSets(equipmentId, isAccessory);
            var races = new List<XivRace>();

            if (sets != null)
            {
                foreach (var kv in sets)
                {
                    var race = kv.Key;
                    var set = kv.Value;
                    var entry = set.Parameters[slot];

                    // Bit0 has unknown purpose currently.
                    if (entry.bit1)
                    {
                        races.Add(race);
                    }
                }
            } else
            {
                var _index = new Index(_gameDirectory);

                // Ok, at this point we're in a somewhat unusual item; it's an item that is effectively non-set.
                // It has an item set ID in the multiple thousands (ex. 5000/9000), so it does not use the EQDP table.
                // In these cases, there's nothing to do but hard check the model paths, until such a time as we know
                // how these are resolved.
                foreach (var race in DeformationAvailableRaces)
                {
                    var path = "";
                    if (!isAccessory)
                    {
                        path = String.Format(_EquipmentModelPathFormat, equipmentId.ToString().PadLeft(4, '0'), race.GetRaceCode(), slot);
                    }
                    else
                    {
                        path = String.Format(_AccessoryModelPathFormat, equipmentId.ToString().PadLeft(4, '0'), race.GetRaceCode(), slot);
                    }
                    if(await _index.FileExists(path))
                    {
                        races.Add(race);
                    }
                }

            }

            return races;
        }

        /// <summary>
        /// Gets all of the equipment or accessory deformation sets for a given equipment id.
        /// </summary>
        /// <param name="equipmentId"></param>
        /// <param name="accessory"></param>
        /// <returns></returns>
        private async Task<Dictionary<XivRace, EquipmentDeformationParameterSet>> GetAllEquipmentDeformationSets(int equipmentId, bool accessory)
        {
            var sets = new Dictionary<XivRace, EquipmentDeformationParameterSet>();

            foreach (var race in DeformationAvailableRaces)
            {
                var result = await GetEquipmentDeformationSet(equipmentId, race, accessory);
                if (result != null) {
                    sets.Add(race, result);
                } else
                {
                    return null;
                }
            }

            return sets;
        }


        /// <summary>
        /// Get the equipment or accessory deformation set for a given item and race.
        /// Null if the set information doesn't exist.
        /// </summary>
        /// <param name="equipmentId"></param>
        /// <param name="race"></param>
        /// <param name="accessory"></param>
        /// <returns></returns>
        private async Task<EquipmentDeformationParameterSet> GetEquipmentDeformationSet(int equipmentId, XivRace race, bool accessory = false)
        {
            var raw = await GetRawEquipmentDeformationParameters(equipmentId, race, accessory);
            if(raw == null)
            {
                return null;
            }

            var set = new EquipmentDeformationParameterSet(accessory);

            var list = EquipmentDeformationParameterSet.SlotsAsList(accessory);

            // Pull the value apart two bits at a time.
            // Last 6 bits are not used.
            for (var idx = 0; idx < 5; idx++)
            {
                var entry = new EquipmentDeformationParameter();

                entry.bit0 = (raw & 1) != 0;
                raw = (ushort)(raw >> 1);
                entry.bit1 = (raw & 1) != 0;
                raw = (ushort)(raw >> 1);

                var key = list[idx];
                set.Parameters[key] = entry;
            }


            return set;
        }

        private string _EquipmentModelPathFormat = "chara/equipment/e{0}/model/c{1}e{0}_{2}.mdl";
        private string _AccessoryModelPathFormat = "chara/equipment/a{0}/model/c{1}a{0}_{2}.mdl";
        /// <summary>
        /// Get the raw bytes for the equipment or accessory deformation parameters for a given equipment set and race.
        /// </summary>
        /// <param name="equipmentId"></param>
        /// <param name="race"></param>
        /// <returns></returns>
        private async Task<ushort?> GetRawEquipmentDeformationParameters(int equipmentId, XivRace race, bool accessory = false)
        {
            var data = await LoadEquipmentDeformationFile(race, accessory);
            var start = EquipmentDeformerParameterHeaderLength + (equipmentId * EquipmentDeformerParameterEntrySize);
            var parameters = new byte[EquipmentParameterEntrySize];

            if(start >= data.Count)
            {

                return null;
            }

            for (var idx = 0; idx < EquipmentDeformerParameterEntrySize; idx++)
            {
                parameters[idx] = data[start + idx];
            }

            return BitConverter.ToUInt16(parameters, 0);
        }

        /// <summary>
        /// Gets the raw equipment or accessory deformation parameters file for a given race.
        /// </summary>
        /// <returns></returns>
        private async Task<List<byte>> LoadEquipmentDeformationFile(XivRace race, bool accessory = false)
        {
            var rootPath = accessory ? AccessoryDeformerParameterRootPath : EquipmentDeformerParameterRootPath;
            var fileName = rootPath + "c" + race.GetRaceCode() + "." + EquipmentDeformerParameterExtension;
            return new List<byte>(await _dat.GetType2Data(fileName, false));
        }

    }
}
