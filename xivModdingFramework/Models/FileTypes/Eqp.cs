using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.Models.FileTypes
{
    public class Eqp
    {
        public const string EquipmentParameterExtension = "edp";
        public const string EquipmentParameterFile = "chara/xls/equipmentparameter/equipmentparameter.eqp";
        public const string GimmickParameterFile = "chara/xls/equipmentparameter/gimmickparameter.gmp";
        public const string EquipmentDeformerParameterExtension = "eqdp";
        public const string EquipmentDeformerParameterRootPath = "chara/xls/charadb/equipmentdeformerparameter/";
        public const string AccessoryDeformerParameterRootPath = "chara/xls/charadb/accessorydeformerparameter/";

        public static int _EQP_GAP_1_END = -1;
        public static int _EQP_GAP_2_END = -1;

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

        // The subset list of races that actually have deformation files.
        public static readonly List<XivRace> DeformationAvailableRacesWithNPCs = new List<XivRace>()
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
            XivRace.Hyur_Midlander_Male_NPC,
            XivRace.Hyur_Midlander_Female_NPC,
            XivRace.Hyur_Highlander_Male_NPC,
            XivRace.Hyur_Highlander_Female_NPC,
            XivRace.Elezen_Male_NPC,
            XivRace.Elezen_Female_NPC,
            XivRace.Miqote_Male_NPC,
            XivRace.Miqote_Female_NPC,
            XivRace.Roegadyn_Male_NPC,
            XivRace.Roegadyn_Female_NPC,
            XivRace.Lalafell_Male_NPC,
            XivRace.Lalafell_Female_NPC,
            XivRace.AuRa_Male_NPC,
            XivRace.AuRa_Female_NPC,
            XivRace.Hrothgar_NPC,
            XivRace.Viera_NPC,
        };

        private Dat _dat;

        public Eqp(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
            _dat = new Dat(_gameDirectory);
            _modListDirectory = new DirectoryInfo(Path.Combine(gameDirectory.Parent.Parent.FullName, XivStrings.ModlistFilePath));
        }

        public async Task SaveGimmickParameter(int equipmentId, GimmickParameter param)
        {
            var data = await LoadGimmickParameterFile(false);

            var offset = ResolveEqpEntryOffset(data, equipmentId);

            if (offset == -1)
            {
                // Fug.  Gotta write expansion function.
            }

            IOUtil.ReplaceBytesAt(data, param.GetBytes(), offset);

            await SaveGimmickParameterFile(data);
        }

        public async Task<GimmickParameter> GetGimmickParameter(IItem item, bool forceDefault = false)
        {
            if(item == null)
            {
                return null;
            }

            return await GetGimmickParameter(item.GetRoot(), forceDefault);
        }
        public async Task<GimmickParameter> GetGimmickParameter(XivDependencyRoot root, bool forceDefault = false)
        {
            if(root == null)
            {
                return null;
            }

            return await GetGimmickParameter(root.Info, forceDefault);

        }
        public async Task<GimmickParameter> GetGimmickParameter(XivDependencyRootInfo root, bool forceDefault = false) {

            if(root.PrimaryType != XivItemType.equipment || root.Slot != "met")
            {
                return null;
            }

            return await GetGimmickParameter(root.PrimaryId, forceDefault);
        }
        public async Task<GimmickParameter> GetGimmickParameter(int equipmentId, bool forceDefault = false)
        {
            if(equipmentId < 0 || equipmentId > 10000)
            {
                throw new InvalidDataException("Unable to resolve GMP information for invalid equipment ID.");
            }

            var data = await LoadGimmickParameterFile(forceDefault);

            // The GMP files use the same format as the EQP files.
            var offset = ResolveEqpEntryOffset(data, equipmentId);
            if(offset < 0)
            {
                // Parameter is in a compressed/empty block, so just return default.
                return new GimmickParameter();
            }

            var bytes = new byte[5];
            Array.Copy(data, offset, bytes, 0, 5);

            var param = new GimmickParameter(bytes);

            return param;
        }

        private async Task SaveGimmickParameterFile(byte[] bytes)
        {
            await _dat.ImportType2Data(bytes, "_GMP_INTERNAL_", GimmickParameterFile, Constants.InternalMetaFileSourceName, Constants.InternalMetaFileSourceName);
        }
        private async Task<byte[]> LoadGimmickParameterFile(bool forceDefault = false)
        {
            return await _dat.GetType2Data(GimmickParameterFile, forceDefault);
        }

        /// <summary>
        /// Saves the given Equipment Parameter information to the main EQP file for the given set.
        /// </summary>
        /// <param name="equipmentId"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public async Task SaveEqpEntry(int equipmentId, EquipmentParameter data)
        {
            if (equipmentId < 0)
            {
                throw new InvalidDataException("Unable to resolve EQP information for invalid equipment ID.");
            }

            if(equipmentId == 0)
            {
                throw new InvalidDataException("Cannot write EQP data for Set 0. (Use Set 1)");
            }

            var file = (await LoadEquipmentParameterFile(false));
            var offset = ResolveEqpEntryOffset(file, equipmentId);

            if(offset < 0)
            {
                // Not bothering to write a function to decompress empty EQP blocks, since it seems like SE keeps every equipment block with actual equipment in it decompressed.
                // I suppose in theory we could use it for adding custom items in the e4000 range or something.
                throw new Exception("Cannot write compressed EQP Entry. (Please report this in Discord!)");
            }

            var slotOffset = EquipmentParameterSet.EntryOffsets[data.Slot];

            offset += slotOffset;


            var bytes = data.GetBytes();

            IOUtil.ReplaceBytesAt(file, bytes, offset);

            await _dat.ImportType2Data(file.ToArray(), "_EQP_INTERNAL_", EquipmentParameterFile, Constants.InternalMetaFileSourceName, Constants.InternalMetaFileSourceName);
        }


        /// <summary>
        /// Retrieves the EQP Entry for a given Item.
        /// </summary>
        public async Task<EquipmentParameter> GetEqpEntry(IItem item, bool forceDefault = false)
        {
            if (item == null)
            {
                return null;
            }

            var root = item.GetRoot();
            if(root == null)
            {
                return null;
            }

            return await GetEqpEntry(root, forceDefault);
        }

        /// <summary>
        /// Retrieves the EQP Entry for a given Root.
        /// </summary>
        public async Task<EquipmentParameter> GetEqpEntry(XivDependencyRoot root, bool forceDefault = false)
        {
            if(root == null)
            {
                return null;
            }

            return await GetEqpEntry(root.Info, forceDefault);
        }

        /// <summary>
        /// Retrieves the EQP Entry for a given Root.
        /// </summary>
        public async Task<EquipmentParameter> GetEqpEntry(XivDependencyRootInfo root, bool forceDefault = false)
        {
            if(root.PrimaryType != XivItemType.equipment)
            {
                return null;
            }

            return await GetEqpEntry(root.PrimaryId, root.Slot, forceDefault);
        }

        /// <summary>
        /// Retrieves the EQP Entry for a given set/slot.
        /// </summary>
        /// <param name="equipmentId"></param>
        /// <param name="slot"></param>
        /// <param name="forceDefault"></param>
        /// <returns></returns>
        public async Task<EquipmentParameter> GetEqpEntry(int equipmentId, string slot, bool forceDefault = false)
        {

            if(!EquipmentDeformationParameterSet.SlotsAsList(false).Contains(slot))
            {
                throw new InvalidDataException("Unable to resolve EQP information for invalid slot.");
            }

            if(equipmentId < 0)
            {
                throw new InvalidDataException("Unable to resolve EQP information for invalid equipment ID.");
            }

            // Set 0 is a special case which SE hard-coded to share with set 1.
            // In practice, we don't allow users to read or edit it via set 0 to avoid mod conflicts.  Instead, Set 1 must be used.
            if(equipmentId == 0)
            {
                return null;
            }



            var file = await LoadEquipmentParameterFile(forceDefault);

            var offset = ResolveEqpEntryOffset(file, equipmentId);

            var slotOffset = EquipmentParameterSet.EntryOffsets[slot];
            var size = EquipmentParameterSet.EntrySizes[slot];

            // Can't resolve the EQP information currently.
            if (offset == -1)
            {
                return new EquipmentParameter(slot, new byte[size]);
            }


            offset += slotOffset;

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
        private async Task<byte[]> LoadEquipmentParameterFile(bool forceDefault = false)
        {
            return await _dat.GetType2Data(EquipmentParameterFile, forceDefault);
        }


        public async Task SaveRawEquipmentParameters(int equipmentId, BitArray entry) {
            var file = new List<byte>(await _dat.GetType2Data(EquipmentParameterFile, false)).ToArray();


            var start = (equipmentId * EquipmentParameterEntrySize);
            entry.CopyTo(file, start);

            await SaveEquipmentParameterFile(file);


        }

        private async Task SaveEquipmentParameterFile(byte[] file)
        {

            await _dat.ImportType2Data(file, "_EQP_INTERNAL_", EquipmentParameterFile, Constants.InternalMetaFileSourceName, Constants.InternalMetaFileSourceName);

            return;
        }


        /// <summary>
        /// Resolves the offset to the given EQP data entry.
        /// </summary>
        /// <param name="eqpData"></param>
        /// <param name="equipmentId"></param>
        /// <returns></returns>
        private int ResolveEqpEntryOffset (byte[] file, int equipmentId)
        {
            const int blockSize = 160;
            // 160 Entry blocks.
            var blockId = equipmentId / blockSize;

            var byteNum = blockId / 8;

            var bit = 1 << (blockId % 8);

            if((file[byteNum] & bit) == 0)
            {
                // Block is currently compressed.
                return -1;
            }

            // Okay, now we have to go through and find out many uncompressed blocks
            // exist before our block.
            int uncompressedBlocks = 0;

            // Loop bytes
            for(int i = 0; i <= byteNum; i++)
            {
                var byt = file[i];
                // Loop bits
                for(int b = 0; b < 8; b++)
                {
                    if (i == byteNum && b == (blockId % 8))
                    {
                        // Done seeking.
                        break;
                    }

                    var bt = 1 << b;
                    var on = (byt & bt) != 0;
                    if(on)
                    {
                        uncompressedBlocks++;
                    }
                }
            }

            var baseOffset = uncompressedBlocks * blockSize;
            var remainder = equipmentId % blockSize;
            var offset = (baseOffset + remainder) * EquipmentParameterEntrySize;
            return offset;
        }


        #region Equipment Deformation

        public async Task SaveEqdpEntries(uint primaryId, string slot, Dictionary<XivRace, EquipmentDeformationParameter> parameters)
        {
            var isAccessory = EquipmentDeformationParameterSet.SlotsAsList(true).Contains(slot);

            if (!isAccessory)
            {
                var slotOk = EquipmentDeformationParameterSet.SlotsAsList(false).Contains(slot);
                if (!slotOk)
                {
                    throw new InvalidDataException("Attempted to save racial models for invalid slot.");
                }
            }
            var races = parameters.Keys.ToList();

            var original = new Dictionary<XivRace, EquipmentDeformationParameter>();
            foreach (var race in races)
            {
                var set = await GetEquipmentDeformationSet((int)primaryId, race, isAccessory);
                original.Add(race, set.Parameters[slot]);
            }

            var slotIdx = EquipmentDeformationParameterSet.SlotsAsList(isAccessory).IndexOf(slot);

            var byteOffset = slotIdx / 4;
            var bitOffset = (slotIdx * 2);




            foreach (var race in races)
            {
                // Don't change races we weren't given information for.
                if (!parameters.ContainsKey(race)) continue;
                var entry = parameters[race];

                var rootPath = isAccessory ? AccessoryDeformerParameterRootPath : EquipmentDeformerParameterRootPath;
                var fileName = rootPath + "c" + race.GetRaceCode() + "." + EquipmentDeformerParameterExtension;

                // Load the file and flip the bits as needed.
                var data = await LoadEquipmentDeformationFile(race, isAccessory, false);

                var offset = ResolveEqdpEntryOffset(data, (int)primaryId);

                if (offset < 0)
                {
                    // Expand the data block, then get the offset again.
                    data = ExpandEqdpBlock(data, (int)primaryId);
                    offset = ResolveEqdpEntryOffset(data, (int)primaryId);

                    if(offset < 0)
                    {
                        throw new Exception("Failed to expand EQDP Data block.");
                    }
                }


                var byteToModify = data[offset + byteOffset];

                
                if(entry.bit0)
                {
                    byteToModify = (byte)(byteToModify | (1 << bitOffset));
                } else
                {
                    byteToModify = (byte)(byteToModify & ~(1 << bitOffset));
                }

                if (entry.bit1)
                {
                    byteToModify = (byte)(byteToModify | (1 << (bitOffset + 1)));
                }
                else
                {
                    byteToModify = (byte)(byteToModify & ~(1 << (bitOffset + 1)));
                }

                data[offset + byteOffset] = byteToModify;

                await _dat.ImportType2Data(data, "_EQDP_INTERNAL_", fileName, Constants.InternalMetaFileSourceName, Constants.InternalMetaFileSourceName);
            }
        }

        #region Public EQDP Accessors

        /// <summary>
        /// Get all the available models for a given piece of equipment.
        /// </summary>
        public async Task<List<XivRace>> GetAvailableRacialModels(IItem item, bool forceDefault = false, bool includeNPCs = false)
        {
            var root = item.GetRoot();
            if(root == null)
            {
                throw new InvalidDataException("Cannot get EQDP information for rootless item.");
            }

            return await GetAvailableRacialModels(root.Info, forceDefault, includeNPCs);
        }

        /// <summary>
        /// Get all the available models for a given piece of equipment.
        /// </summary>
        public async Task<List<XivRace>> GetAvailableRacialModels(XivDependencyRoot root, bool forceDefault = false, bool includeNPCs = false)
        {
            return await GetAvailableRacialModels(root.Info, forceDefault, includeNPCs);
        }

        /// <summary>
        /// Get all the available models for a given piece of equipment.
        /// </summary>
        public async Task<List<XivRace>> GetAvailableRacialModels(XivDependencyRootInfo root, bool forceDefault = false, bool includeNPCs = false)
        {
            if(root.PrimaryType != XivItemType.equipment && root.PrimaryType != XivItemType.accessory)
            {
                throw new InvalidDataException("Cannot get EQDP information for invalid item type.");
            }

            return await GetAvailableRacialModels(root.PrimaryId, root.Slot, forceDefault, includeNPCs);
        }

        /// <summary>
        /// Get all the available models for a given piece of equipment.
        /// </summary>
        public async Task<List<XivRace>> GetAvailableRacialModels(int equipmentId, string slot, bool forceDefault = false, bool includeNPCs = false)
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

            var root = new XivDependencyRootInfo();
            root.PrimaryId = equipmentId;
            root.Slot = slot;
            root.PrimaryType = isAccessory ? XivItemType.accessory : XivItemType.equipment;

            var dict = await GetEquipmentDeformationParameters(root, forceDefault, includeNPCs);

            List<XivRace> races = new List<XivRace>();
            foreach(var kv in dict)
            {
                // Bit 0 has unknown purpose currently.
                if(kv.Value.bit1)
                {
                    races.Add(kv.Key);
                }
            }

            return races;
        }

        /// <summary>
        /// Retrieves the raw EQDP entries for a given root with slot.
        /// </summary>
        public async Task<Dictionary<XivRace, EquipmentDeformationParameter>> GetEquipmentDeformationParameters(IItem item, bool forceDefault = false, bool includeNPCs = false)
        {
            var root = item.GetRoot();

            if(root == null)
            {
                return new Dictionary<XivRace, EquipmentDeformationParameter>();
            }


            return await GetEquipmentDeformationParameters(root.Info, forceDefault, includeNPCs);
        }

        /// <summary>
        /// Retrieves the raw EQDP entries for a given root with slot.
        /// </summary>
        public async Task<Dictionary<XivRace, EquipmentDeformationParameter>> GetEquipmentDeformationParameters(XivDependencyRoot root, bool forceDefault = false, bool includeNPCs = false)
        {
            return await GetEquipmentDeformationParameters(root.Info, forceDefault, includeNPCs);
        }

        /// <summary>
        /// Retrieves the raw EQDP entries for a given root with slot.
        /// </summary>
        public async Task<Dictionary<XivRace, EquipmentDeformationParameter>> GetEquipmentDeformationParameters(XivDependencyRootInfo root, bool forceDefault = false, bool includeNPCs = false)
        {
            if (root.PrimaryType != XivItemType.equipment && root.PrimaryType != XivItemType.accessory)
            {
                return new Dictionary<XivRace, EquipmentDeformationParameter>();
            }

            return await GetEquipmentDeformationParameters(root.PrimaryId, root.Slot, root.PrimaryType == XivItemType.accessory, forceDefault, includeNPCs);
        }

        /// <summary>
        /// Retrieves the raw EQDP entries for a given root with slot.
        /// </summary>
        public async Task<Dictionary<XivRace, EquipmentDeformationParameter>> GetEquipmentDeformationParameters(int equipmentId, string slot, bool isAccessory, bool forceDefault = false, bool includeNPCs = false)
        {
            var slotOK = false;
            if (isAccessory)
            {
                slotOK = slotOK || EquipmentDeformationParameterSet.SlotsAsList(true).Contains(slot);
            }
            slotOK = slotOK || EquipmentDeformationParameterSet.SlotsAsList(false).Contains(slot);

            if (!slotOK)
            {
                throw new InvalidDataException("Attempted to get EQDP Information for invalid slot.");
            }


            var sets = await GetAllEquipmentDeformationSets(equipmentId, isAccessory, forceDefault, includeNPCs);
            var races = new List<XivRace>();

            Dictionary<XivRace, EquipmentDeformationParameter> ret = new Dictionary<XivRace, EquipmentDeformationParameter>();

            foreach (var kv in sets)
            {
                var race = kv.Key;
                var set = kv.Value;
                var entry = set.Parameters[slot];
                ret.Add(race, entry);
            }


            return ret;

        }



        #endregion

        #region Raw Internal Functions

        /// <summary>
        /// Gets all of the equipment or accessory deformation sets for a given equipment id.
        /// </summary>
        /// <param name="equipmentId"></param>
        /// <param name="accessory"></param>
        /// <returns></returns>
        private async Task<Dictionary<XivRace, EquipmentDeformationParameterSet>> GetAllEquipmentDeformationSets(int equipmentId, bool accessory, bool forceDefault = false, bool includeNPCs = false)
        {
            var sets = new Dictionary<XivRace, EquipmentDeformationParameterSet>();

            var races = includeNPCs ? DeformationAvailableRacesWithNPCs : DeformationAvailableRaces;
            foreach (var race in races)
            {
                var result = await GetEquipmentDeformationSet(equipmentId, race, accessory, forceDefault);
                sets.Add(race, result);
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
        private async Task<EquipmentDeformationParameterSet> GetEquipmentDeformationSet(int equipmentId, XivRace race, bool accessory = false, bool forceDefault = false)
        {
            var raw = await GetRawEquipmentDeformationParameters(equipmentId, race, accessory, forceDefault);

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

        /// <summary>
        /// Get the raw bytes for the equipment or accessory deformation parameters for a given equipment set and race.
        /// </summary>
        /// <param name="equipmentId"></param>
        /// <param name="race"></param>
        /// <returns></returns>
        private async Task<ushort> GetRawEquipmentDeformationParameters(int equipmentId, XivRace race, bool accessory = false, bool forceDefault = false)
        {
            var data = (await LoadEquipmentDeformationFile(race, accessory, forceDefault)).ToArray();

            var offset = ResolveEqdpEntryOffset(data, equipmentId);

            // Some sets don't have entries.  In this case, they're treated as blank/full 0's.
            if(offset < 0)
            {
                return 0;
            }

            var parameters = new byte[EquipmentParameterEntrySize];


            for (var idx = 0; idx < EquipmentParameterEntrySize; idx++)
            {
                parameters[idx] = data[offset + idx];
            }

            return BitConverter.ToUInt16(parameters, 0);
        }

        /// <summary>
        /// Gets the raw equipment or accessory deformation parameters file for a given race.
        /// </summary>
        /// <returns></returns>
        private async Task<byte[]> LoadEquipmentDeformationFile(XivRace race, bool accessory = false, bool forceDefault = false)
        {
            var rootPath = accessory ? AccessoryDeformerParameterRootPath : EquipmentDeformerParameterRootPath;
            var fileName = rootPath + "c" + race.GetRaceCode() + "." + EquipmentDeformerParameterExtension;
            return await _dat.GetType2Data(fileName, forceDefault);
        }

        /// <summary>
        /// Resolves the entry offset to a given SetID within an EQDP File.
        /// </summary>
        /// <param name="race"></param>
        /// <param name="setId"></param>
        /// <param name="accessory"></param>
        /// <param name="forceDefault"></param>
        /// <returns></returns>
        private async Task<int> ResolveEqdpEntryOffset(XivRace race, int setId, bool accessory = false, bool forceDefault = false)
        {
            var data = await LoadEquipmentDeformationFile(race, accessory, forceDefault);
            return ResolveEqdpEntryOffset(data.ToArray(), setId);

        }

        /// <summary>
        /// Resolves the entry offset to a given SetId within an EQDP File.
        /// </summary>
        /// <param name="eqdpData"></param>
        /// <param name="setId"></param>
        /// <returns></returns>
        private int ResolveEqdpEntryOffset(byte[] eqdpData, int setId)
        {
            const ushort basicHeaderSize = 6;
            const ushort blockHeaderSize = 2;

            var unknown = BitConverter.ToUInt16(eqdpData, 0);
            var blockSize = BitConverter.ToUInt16(eqdpData, 2);
            var blockCount = BitConverter.ToUInt16(eqdpData, 4);

            var headerEntryId = setId / blockSize;
            var subEntryId = setId % blockSize;


            // Offset to the block table entry.
            var headerEntryOffset = basicHeaderSize + (blockHeaderSize * (setId / blockSize));

            // This gets us the offset after the full header to the start of the data block.
            var baseDataOffset = BitConverter.ToUInt16(eqdpData, headerEntryOffset);

            // If the data offset is MAX-SHORT, that data block was omitted from the file.
            if (baseDataOffset == 65535) return -1;

            // 6 Byte basic header, then block table.
            var fullHeaderLength = basicHeaderSize + (blockHeaderSize * blockCount);

            // Start of the data block our entry lives in.
            var blockStart = fullHeaderLength + (baseDataOffset * EquipmentDeformerParameterEntrySize);

            // Then move the appropriate number of entries in.
            var dataOffset = blockStart + (subEntryId * EquipmentDeformerParameterEntrySize);

            return dataOffset;
        }


        /// <summary>
        /// Expands a collapsed block within an EQDP file to allow for writing information about a given set.
        /// </summary>
        /// <param name="eqdpData"></param>
        /// <param name="setId"></param>
        /// <returns></returns>
        private byte[] ExpandEqdpBlock(byte[] eqdpData, int setId)
        {
            const ushort basicHeaderSize = 6;
            const ushort blockHeaderSize = 2;

            var unknown = BitConverter.ToUInt16(eqdpData, 0);
            var blockSize = BitConverter.ToUInt16(eqdpData, 2);
            var blockCount = BitConverter.ToUInt16(eqdpData, 4);

            var headerEntryId = setId / blockSize;
            var subEntryId = setId % blockSize;


            // Offset to the block table entry.
            var headerEntryOffset = basicHeaderSize + (blockHeaderSize * (setId / blockSize));

            // This gets us the offset after the full header to the start of the data block.
            var baseDataOffset = BitConverter.ToUInt16(eqdpData, headerEntryOffset);

            // If the data offset is not MAX-SHORT, the block is already expanded.
            if (baseDataOffset != 65535) return eqdpData;

            // Okay, at this point we know we need to expand the block.  We have to do a few things.
            // 1.  Establish what offset the data should be in.  This means looking back to determine what the last used block's offset was.
            // 2.  Insert our new offset.
            // 3.  Update all the following offsets to account for us.
            // 4.  Copy all the data to a new array, injecting a fresh block at the appropriate offset.

            var lastOffset = -1;
            for(int i = 6; i < headerEntryOffset; i+=2) {
                var offset = BitConverter.ToUInt16(eqdpData, i);
                if(offset != 65535)
                {
                    lastOffset = offset;
                }
            }

            ushort nextOffset = (ushort)(lastOffset > 0 ? (lastOffset + blockSize) : 0);
            IOUtil.ReplaceBytesAt(eqdpData, BitConverter.GetBytes(nextOffset), headerEntryOffset);

            // 6 Byte basic header, then block table.
            var fullHeaderLength = basicHeaderSize + (blockHeaderSize * blockCount);

            for (int i = headerEntryOffset + blockHeaderSize; i < fullHeaderLength; i += 2)
            {
                var offset = BitConverter.ToUInt16(eqdpData, i);
                if (offset != 65535)
                {
                    IOUtil.ReplaceBytesAt(eqdpData, BitConverter.GetBytes((ushort)(offset + blockSize)), i);
                }
            }

            var usedBlocks = 0;
            for (int i = 6; i < fullHeaderLength; i += 2)
            {
                var offset = BitConverter.ToUInt16(eqdpData, i);
                if (offset != 65535)
                {
                    usedBlocks++;
                }
            }

            // Total Size of the data.
            var totalDataSize = fullHeaderLength + (usedBlocks * blockSize * EquipmentDeformerParameterEntrySize);
            
            // Pad out to nearest 512 bytes.
            var padding = totalDataSize % 512 == 0 ? 0 : 512 - (totalDataSize % 512);

            var finalSize = totalDataSize + padding;

            // Okay, we've now fully updated the initial block table.   We need to copy all the data over.
            var newDataByteOffset = fullHeaderLength + (nextOffset * 2);

            var newData = new byte[finalSize];

            // Copy the first half of the data in, then a blank block, then the second half of the data.
            Array.Copy(eqdpData, 0, newData, 0, newDataByteOffset);
            var rem = eqdpData.Length - newDataByteOffset;
            var rem2 = newData.Length - newDataByteOffset - (blockSize * EquipmentDeformerParameterEntrySize);

            // Don't let us try to write padding data off the end of the file.
            rem = rem > rem2 ? rem2 : rem;

            Array.Copy(eqdpData, newDataByteOffset, newData, newDataByteOffset + (blockSize * EquipmentDeformerParameterEntrySize), rem);

            // Return new array.
            return newData;
        }

        #endregion

        #endregion
    }
}
