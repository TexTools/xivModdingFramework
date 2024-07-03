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
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;

using Index = xivModdingFramework.SqPack.FileTypes.Index;

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

        public const string DawntrailTestFile = "chara/xls/charadb/equipmentdeformerparameter/c1601.eqdp";

        public static int _EQP_GAP_1_END = -1;
        public static int _EQP_GAP_2_END = -1;

        // Full EQP entries are 8 bytes long.
        public const int EquipmentParameterEntrySize = 8;

        // Full EQDP entries are 2 bytes long.
        public const int EquipmentDeformerParameterEntrySize = 2;
        public const int EquipmentDeformerParameterHeaderLength = 320;


        // The list of all playable races.
        public static readonly List<XivRace> PlayableRaces = new List<XivRace>()
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
            XivRace.Hrothgar_Male,

            XivRace.Hrothgar_Female,
            XivRace.Viera_Male,
            XivRace.Viera_Female,

        };

        // List of All Races including their NPC Versions
        public static readonly List<XivRace> PlayableRacesWithNPCs = new List<XivRace>()
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
            XivRace.Hrothgar_Male,
            XivRace.Hrothgar_Female,
            XivRace.Viera_Female,
            XivRace.Viera_Male,
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
            XivRace.Hrothgar_Male_NPC,
            XivRace.Hrothgar_Female_NPC,
            XivRace.Viera_Female_NPC,
            XivRace.Viera_Male_NPC,
            XivRace.NPC_Male,
            XivRace.NPC_Female
        };

        public Eqp()
        {
        }

        public async Task SaveGmpEntries(List<(uint PrimaryId, GimmickParameter GmpData)> entries, IItem referenceItem, ModTransaction tx = null)
        {
            foreach (var tuple in entries)
            {
                if (tuple.PrimaryId == 0)
                {
                    throw new InvalidDataException("Cannot write GMP data for Set 0. (Use Set 1)");
                }
            }

            var file = await LoadGimmickParameterFile(false, tx);


            var offsets = new Dictionary<uint, int>();
            var clean = false;

            // Resolve offsets, expanding on the first pass.
            // (GMP files use an identical file structure to EQP files)

            while (!clean)
            {
                clean = true;
                offsets.Clear();
                foreach (var tuple in entries)
                {
                    var offset = ResolveEqpEntryOffset(file, (int)tuple.PrimaryId);

                    if (offset < 0)
                    {
                        clean = false;
                        // Expand the data block, then try again.
                        file = ExpandEqpBlock(file, (int)tuple.PrimaryId);

                        offset = ResolveEqpEntryOffset(file, (int)tuple.PrimaryId);

                        if (offset < 0)
                        {
                            throw new InvalidDataException("Unable to determine EQP set offset.");
                        }
                    }

                    offsets.Add(tuple.PrimaryId, offset);
                }
            }


            foreach (var tuple in entries)
            {
                var offset = offsets[tuple.PrimaryId];
                IOUtil.ReplaceBytesAt(file, tuple.GmpData.GetBytes(), offset);
            }

            await SaveGimmickParameterFile(file, referenceItem, tx);

        }

        public async Task SaveGimmickParameter(int equipmentId, GimmickParameter param, IItem referenceItem = null, ModTransaction tx = null)
        {
            if (equipmentId == 0)
            {
                throw new InvalidDataException("Cannot write GMP data for Set 0. (Use Set 1)");
            }

            var data = await LoadGimmickParameterFile(false, tx);

            var offset = ResolveEqpEntryOffset(data, equipmentId);

            if (offset == -1)
            {
                // Expand the block, then get the offset.
                // (GMP files use an identical file structure to EQP files)
                data = ExpandEqpBlock(data, equipmentId);
                offset = ResolveEqpEntryOffset(data, equipmentId);

                if (offset <= 0)
                {
                    throw new InvalidDataException("Unable to resolve GMP data offset.");
                }
            }

            IOUtil.ReplaceBytesAt(data, param.GetBytes(), offset);

            await SaveGimmickParameterFile(data, referenceItem, tx);
        }

        public async Task<GimmickParameter> GetGimmickParameter(IItem item, bool forceDefault = false, ModTransaction tx = null)
        {
            if (item == null)
            {
                return null;
            }

            return await GetGimmickParameter(item.GetRoot(), forceDefault, tx);
        }
        public async Task<GimmickParameter> GetGimmickParameter(XivDependencyRoot root, bool forceDefault = false, ModTransaction tx = null)
        {
            if (root == null)
            {
                return null;
            }

            return await GetGimmickParameter(root.Info, forceDefault, tx);

        }
        public async Task<GimmickParameter> GetGimmickParameter(XivDependencyRootInfo root, bool forceDefault = false, ModTransaction tx = null) {

            if (root.PrimaryType != XivItemType.equipment || root.Slot != "met")
            {
                return null;
            }

            return await GetGimmickParameter(root.PrimaryId, forceDefault, tx);
        }
        public async Task<GimmickParameter> GetGimmickParameter(int equipmentId, bool forceDefault = false, ModTransaction tx = null)
        {
            if (equipmentId < 0 || equipmentId > 10000)
            {
                throw new InvalidDataException("Unable to resolve GMP information for invalid equipment ID.");
            }

            var data = await LoadGimmickParameterFile(forceDefault, tx);

            // The GMP files use the same format as the EQP files.
            var offset = ResolveEqpEntryOffset(data, equipmentId);
            if (offset < 0)
            {
                // Parameter is in a compressed/empty block, so just return default.
                return new GimmickParameter();
            }

            var bytes = new byte[5];
            Array.Copy(data, offset, bytes, 0, 5);

            var param = new GimmickParameter(bytes);

            return param;
        }

        private async Task SaveGimmickParameterFile(byte[] bytes, IItem referenceItem = null, ModTransaction tx = null)
        {
            await Dat.ImportType2Data(bytes, GimmickParameterFile, Constants.InternalModSourceName, referenceItem, tx);
        }
        private async Task<byte[]> LoadGimmickParameterFile(bool forceDefault = false, ModTransaction tx = null)
        {
            return await Dat.ReadFile(GimmickParameterFile, forceDefault, tx);
        }

        /// <summary>
        /// Saves multiple EQP entries in  batch process.
        /// </summary>
        /// <param name="entries"></param>
        /// <param name="referenceItem"></param>
        /// <returns></returns>
        public async Task SaveEqpEntries(List<(uint PrimaryId, EquipmentParameter EqpData)> entries, IItem referenceItem, ModTransaction tx = null)
        {
            foreach (var tuple in entries)
            {
                if (tuple.PrimaryId == 0)
                {
                    throw new InvalidDataException("Cannot write EQP data for Set 0. (Use Set 1)");
                }
            }

            var file = await LoadEquipmentParameterFile(false, tx);


            var offsets = new Dictionary<uint, int>();
            var clean = false;

            // Resolve offsets, expanding on the first pass.
            while (!clean)
            {
                clean = true;
                offsets.Clear();
                foreach (var tuple in entries)
                {
                    var offset = ResolveEqpEntryOffset(file, (int)tuple.PrimaryId);

                    if (offset < 0)
                    {
                        clean = false;
                        // Expand the data block, then try again.
                        file = ExpandEqpBlock(file, (int)tuple.PrimaryId);

                        offset = ResolveEqpEntryOffset(file, (int)tuple.PrimaryId);

                        if (offset < 0)
                        {
                            throw new InvalidDataException("Unable to determine EQP set offset.");
                        }
                    }

                    if (!offsets.ContainsKey(tuple.PrimaryId))
                    {
                        offsets.Add(tuple.PrimaryId, offset);
                    }
                }
            }


            foreach (var tuple in entries)
            {
                var offset = offsets[tuple.PrimaryId];
                var slotOffset = EquipmentParameterSet.EntryOffsets[tuple.EqpData.Slot];
                offset += slotOffset;
                var bytes = tuple.EqpData.GetBytes();
                IOUtil.ReplaceBytesAt(file, bytes, offset);
            }


            await Dat.ImportType2Data(file.ToArray(), EquipmentParameterFile, Constants.InternalModSourceName, referenceItem, tx);
        }

        /// <summary>
        /// Saves the given Equipment Parameter information to the main EQP file for the given set.
        /// </summary>
        /// <param name="equipmentId"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public async Task SaveEqpEntry(int equipmentId, EquipmentParameter data, IItem referenceItem = null, ModTransaction tx = null)
        {
            if (equipmentId < 0)
            {
                throw new InvalidDataException("Unable to resolve EQP information for invalid equipment ID.");
            }

            if (equipmentId == 0)
            {
                throw new InvalidDataException("Cannot write EQP data for Set 0. (Use Set 1)");
            }

            var file = (await LoadEquipmentParameterFile(false, tx));
            var offset = ResolveEqpEntryOffset(file, equipmentId);

            if (offset < 0)
            {
                // Expand the data block, then try again.
                file = ExpandEqpBlock(file, equipmentId);

                offset = ResolveEqpEntryOffset(file, equipmentId);

                if (offset < 0)
                {
                    throw new InvalidDataException("Unable to determine EQP set offset.");
                }
            }

            var slotOffset = EquipmentParameterSet.EntryOffsets[data.Slot];

            offset += slotOffset;


            var bytes = data.GetBytes();

            IOUtil.ReplaceBytesAt(file, bytes, offset);

            await Dat.ImportType2Data(file.ToArray(), EquipmentParameterFile, Constants.InternalModSourceName, referenceItem, tx);
        }


        /// <summary>
        /// Retrieves the EQP Entry for a given Item.
        /// </summary>
        public async Task<EquipmentParameter> GetEqpEntry(IItem item, bool forceDefault = false, ModTransaction tx = null)
        {
            if (item == null)
            {
                return null;
            }

            var root = item.GetRoot();
            if (root == null)
            {
                return null;
            }

            return await GetEqpEntry(root, forceDefault, tx);
        }

        /// <summary>
        /// Retrieves the EQP Entry for a given Root.
        /// </summary>
        public async Task<EquipmentParameter> GetEqpEntry(XivDependencyRoot root, bool forceDefault = false, ModTransaction tx = null)
        {
            if (root == null)
            {
                return null;
            }

            return await GetEqpEntry(root.Info, forceDefault, tx);
        }

        /// <summary>
        /// Retrieves the EQP Entry for a given Root.
        /// </summary>
        public async Task<EquipmentParameter> GetEqpEntry(XivDependencyRootInfo root, bool forceDefault = false, ModTransaction tx = null)
        {
            if (root.PrimaryType != XivItemType.equipment)
            {
                return null;
            }

            return await GetEqpEntry(root.PrimaryId, root.Slot, forceDefault, tx);
        }

        /// <summary>
        /// Retrieves the EQP Entry for a given set/slot.
        /// </summary>
        /// <param name="equipmentId"></param>
        /// <param name="slot"></param>
        /// <param name="forceDefault"></param>
        /// <returns></returns>
        public async Task<EquipmentParameter> GetEqpEntry(int equipmentId, string slot, bool forceDefault = false, ModTransaction tx = null)
        {

            if (!EquipmentDeformationParameterSet.SlotsAsList(false).Contains(slot))
            {
                throw new InvalidDataException("Unable to resolve EQP information for invalid slot.");
            }

            if (equipmentId < 0)
            {
                throw new InvalidDataException("Unable to resolve EQP information for invalid equipment ID.");
            }

            // Set 0 is a special case which SE hard-coded to share with set 1.
            // In practice, we don't allow users to read or edit it via set 0 to avoid mod conflicts.  Instead, Set 1 must be used.
            if (equipmentId == 0)
            {
                return null;
            }



            var file = await LoadEquipmentParameterFile(forceDefault, tx);

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
        public async Task<BitArray> GetRawEquipmentParameters(int equipmentId, ModTransaction tx = null)
        {
            var data = await LoadEquipmentParameterFile(false, tx);
            var start = (equipmentId * EquipmentParameterEntrySize);
            var parameters = new byte[EquipmentParameterEntrySize];


            // This item doesn't have equipment parameters.
            if (start >= data.Length)
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
        private async Task<byte[]> LoadEquipmentParameterFile(bool forceDefault = false, ModTransaction tx = null)
        {
            return await Dat.ReadFile(EquipmentParameterFile, forceDefault, tx);
        }


        public async Task SaveRawEquipmentParameters(int equipmentId, BitArray entry, ModTransaction tx = null) {
            var file = new List<byte>(await Dat.ReadFile(EquipmentParameterFile, false, tx)).ToArray();


            var start = (equipmentId * EquipmentParameterEntrySize);
            entry.CopyTo(file, start);

            await SaveEquipmentParameterFile(file, tx);


        }

        private async Task SaveEquipmentParameterFile(byte[] file, ModTransaction tx = null)
        {

            await Dat.ImportType2Data(file, EquipmentParameterFile, Constants.InternalModSourceName, null, tx);

            return;
        }


        /// <summary>
        /// Expands a compressed EQP block.
        /// </summary>
        /// <param name="eqpData"></param>
        /// <param name="setId"></param>
        /// <returns></returns>
        private byte[] ExpandEqpBlock(byte[] eqpData, int setId)
        {
            const int blockSize = 160;
            // 160 Entry blocks.
            var blockId = setId / blockSize;
            var byteNum = blockId / 8;
            var bit = 1 << (blockId % 8);

            if ((eqpData[byteNum] & bit) != 0)
            {
                // Block is already uncompressed.
                return eqpData;
            }

            // Flip the flag bit.
            eqpData[byteNum] = (byte)(eqpData[byteNum] | bit);

            // Okay, now we have to go through and find out many uncompressed blocks
            // exist before our block.
            int uncompressedBlocks = 0;
            int totalUncompressedBlocks = 0;

            // Loop bytes
            bool found = false;
            for (int i = 0; i < 8; i++)
            {
                var byt = eqpData[i];
                // Loop bits
                for (int b = 0; b < 8; b++)
                {
                    if (i == byteNum && b == (blockId % 8))
                    {
                        // Done seeking.
                        found = true;
                    }

                    var bt = 1 << b;
                    var on = (byt & bt) != 0;
                    if (on)
                    {
                        if (!found)
                        {
                            uncompressedBlocks++;
                        }
                        totalUncompressedBlocks++;
                    }
                }
            }

            // This is the offset where our new block will start.
            var baseOffset = uncompressedBlocks * blockSize;

            // Total Size of the data.
            var totalDataSize = ((totalUncompressedBlocks + 1) * blockSize * EquipmentParameterEntrySize);

            // Pad out to nearest 512 bytes.
            var padding = totalDataSize % 512 == 0 ? 0 : 512 - (totalDataSize % 512);

            var finalSize = totalDataSize + padding;

            // Okay, we've now fully updated the initial block table.   We need to copy all the data over.
            var newDataByteOffset = (baseOffset * EquipmentParameterEntrySize);

            var newData = new byte[finalSize];

            // Copy the first half of the data in, then a blank block, then the second half of the data.
            Array.Copy(eqpData, 0, newData, 0, newDataByteOffset);
            var rem = eqpData.Length - newDataByteOffset;
            var rem2 = newData.Length - newDataByteOffset - (blockSize * EquipmentParameterEntrySize);

            // Don't let us try to write padding data off the end of the file.
            rem = rem > rem2 ? rem2 : rem;

            Array.Copy(eqpData, newDataByteOffset, newData, newDataByteOffset + (blockSize * EquipmentParameterEntrySize), rem);

            // Return new array.
            return newData;
        }

        /// <summary>
        /// Resolves the offset to the given EQP data entry.
        /// </summary>
        /// <param name="eqpData"></param>
        /// <param name="equipmentId"></param>
        /// <returns></returns>
        private int ResolveEqpEntryOffset(byte[] file, int equipmentId)
        {
            const int blockSize = 160;
            // 160 Entry blocks.
            var blockId = equipmentId / blockSize;

            var byteNum = blockId / 8;

            var bit = 1 << (blockId % 8);

            if ((file[byteNum] & bit) == 0)
            {
                // Block is currently compressed.
                return -1;
            }

            // Okay, now we have to go through and find out many uncompressed blocks
            // exist before our block.
            int uncompressedBlocks = 0;

            // Loop bytes
            for (int i = 0; i <= byteNum; i++)
            {
                var byt = file[i];
                // Loop bits
                for (int b = 0; b < 8; b++)
                {
                    if (i == byteNum && b == (blockId % 8))
                    {
                        // Done seeking.
                        break;
                    }

                    var bt = 1 << b;
                    var on = (byt & bt) != 0;
                    if (on)
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

        /// <summary>
        /// Writes a batch set of EQDP entries.
        /// </summary>
        /// <param name="entries"></param>
        /// <param name="referenceItem"></param>
        /// <returns></returns>
        public async Task SaveEqdpEntries(Dictionary<XivRace, List<(uint PrimaryId, string Slot, EquipmentDeformationParameter Entry)>> entries, IItem referenceItem, ModTransaction tx = null)
        {
            // Group entries into Accessories and Non-Accessories.
            var accessories = new Dictionary<XivRace, List<(uint PrimaryId, string Slot, EquipmentDeformationParameter Entry)>>();
            var equipment = new Dictionary<XivRace, List<(uint PrimaryId, string Slot, EquipmentDeformationParameter Entry)>>();

            foreach (var raceKv in entries)
            {
                var race = raceKv.Key;
                foreach(var e in raceKv.Value)
                {
                    var isAccessory = EquipmentDeformationParameterSet.SlotsAsList(true).Contains(e.Slot);

                    if (!isAccessory)
                    {
                        var slotOk = EquipmentDeformationParameterSet.SlotsAsList(false).Contains(e.Slot);
                        if (!slotOk)
                        {
                            throw new InvalidDataException("Attempted to save racial models for invalid slot.");
                        }
                    }

                    if(isAccessory)
                    {
                        if(!accessories.ContainsKey(race))
                        {
                            accessories.Add(race, new List<(uint PrimaryId, string Slot, EquipmentDeformationParameter Entry)>());
                        }

                        accessories[race].Add(e);
                    } else
                    {
                        // Invalid race or race we don't know how to parse yet.  (Ex. Files from the future with additional races)
                        if (race == XivRace.All_Races) continue;

                        if (!equipment.ContainsKey(race))
                        {
                            equipment.Add(race, new List<(uint PrimaryId, string Slot, EquipmentDeformationParameter Entry)>());
                        }

                        equipment[race].Add(e);
                    }

                }
            }

            // First loop all races in the equipment segment.
            foreach(var raceKv in equipment)
            {
                var race = raceKv.Key;
                var fileName = EquipmentDeformerParameterRootPath + "c" + race.GetRaceCode() + "." + EquipmentDeformerParameterExtension;
                var data = await LoadEquipmentDeformationFile(race, false, false, tx);

                // Loop through until we've expanded all of the data entries that we need in order to write the data.
                bool clean = false;
                Dictionary<int, int> SetOffsets = new Dictionary<int, int>(raceKv.Value.Count);
                while(clean == false)
                {
                    SetOffsets.Clear();
                    clean = true;
                    foreach (var tuple in raceKv.Value)
                    {
                        var offset = ResolveEqdpEntryOffset(data, (int)tuple.PrimaryId);

                        if (offset < 0)
                        {
                            clean = false;
                            // Expand the data block, then get the offset again.
                            data = ExpandEqdpBlock(data, (int)tuple.PrimaryId);
                            offset = ResolveEqdpEntryOffset(data, (int)tuple.PrimaryId);

                            if (offset < 0)
                            {
                                throw new Exception("Failed to expand EQDP Data block.");
                            }
                        }

                        if (!SetOffsets.ContainsKey((int)tuple.PrimaryId))
                        {
                            SetOffsets.Add((int)tuple.PrimaryId, offset);
                        }
                    }
                }

                // Update the actual data elements.
                foreach(var tuple in raceKv.Value)
                {
                    var slotIdx = EquipmentDeformationParameterSet.SlotsAsList(false).IndexOf(tuple.Slot);

                    var byteOffset = slotIdx / 4;
                    var bitOffset = (slotIdx * 2) % 8;

                    var offset = SetOffsets[(int)tuple.PrimaryId];
                    var byteToModify = data[offset + byteOffset];


                    if (tuple.Entry.HasMaterial)
                    {
                        byteToModify = (byte)(byteToModify | (1 << bitOffset));
                    }
                    else
                    {
                        byteToModify = (byte)(byteToModify & ~(1 << bitOffset));
                    }

                    if (tuple.Entry.HasModel)
                    {
                        byteToModify = (byte)(byteToModify | (1 << (bitOffset + 1)));
                    }
                    else
                    {
                        byteToModify = (byte)(byteToModify & ~(1 << (bitOffset + 1)));
                    }

                    data[offset + byteOffset] = byteToModify;
                }

                await Dat.ImportType2Data(data, fileName, Constants.InternalModSourceName, referenceItem, tx);
            }

            // Loop Accessories
            foreach (var raceKv in accessories)
            {
                var race = raceKv.Key;
                var fileName = AccessoryDeformerParameterRootPath + "c" + race.GetRaceCode() + "." + EquipmentDeformerParameterExtension;
                var data = await LoadEquipmentDeformationFile(race, true, false);

                // Loop through until we've expanded all of the data entries that we need in order to write the data.
                bool clean = false;
                Dictionary<int, int> SetOffsets = new Dictionary<int, int>(raceKv.Value.Count);
                while (clean == false)
                {
                    SetOffsets.Clear();
                    clean = true;
                    foreach (var tuple in raceKv.Value)
                    {
                        var offset = ResolveEqdpEntryOffset(data, (int)tuple.PrimaryId);

                        if (offset < 0)
                        {
                            clean = false;
                            // Expand the data block, then get the offset again.
                            data = ExpandEqdpBlock(data, (int)tuple.PrimaryId);
                            offset = ResolveEqdpEntryOffset(data, (int)tuple.PrimaryId);

                            if (offset < 0)
                            {
                                throw new Exception("Failed to expand EQDP Data block.");
                            }
                        }

                        if (!SetOffsets.ContainsKey((int)tuple.PrimaryId))
                        {
                            SetOffsets.Add((int)tuple.PrimaryId, offset);
                        }
                    }
                }

                // Update the actual data elements.
                foreach (var tuple in raceKv.Value)
                {
                    var slotIdx = EquipmentDeformationParameterSet.SlotsAsList(true).IndexOf(tuple.Slot);

                    var byteOffset = slotIdx / 4;
                    var bitOffset = (slotIdx * 2) % 8;

                    var offset = SetOffsets[(int)tuple.PrimaryId];
                    var byteToModify = data[offset + byteOffset];


                    if (tuple.Entry.HasMaterial)
                    {
                        byteToModify = (byte)(byteToModify | (1 << bitOffset));
                    }
                    else
                    {
                        byteToModify = (byte)(byteToModify & ~(1 << bitOffset));
                    }

                    if (tuple.Entry.HasModel)
                    {
                        byteToModify = (byte)(byteToModify | (1 << (bitOffset + 1)));
                    }
                    else
                    {
                        byteToModify = (byte)(byteToModify & ~(1 << (bitOffset + 1)));
                    }

                    data[offset + byteOffset] = byteToModify;
                }
                await Dat.ImportType2Data(data, fileName, Constants.InternalModSourceName, referenceItem, tx);

            }


        }

        public async Task SaveEqdpEntries(uint primaryId, string slot, Dictionary<XivRace, EquipmentDeformationParameter> parameters, IItem referenceItem = null, ModTransaction tx = null)
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
                // Future race that this version doesn't know about (Ex. Hrothgar F on Endwalker builds)
                if (race == XivRace.All_Races) continue;

                var set = await GetEquipmentDeformationSet((int)primaryId, race, isAccessory, false, tx);
                original.Add(race, set.Parameters[slot]);
            }

            var slotIdx = EquipmentDeformationParameterSet.SlotsAsList(isAccessory).IndexOf(slot);

            var byteOffset = slotIdx / 4;
            var bitOffset = (slotIdx * 2) % 8;




            foreach (var race in races)
            {
                // Don't change races we weren't given information for.
                if (!parameters.ContainsKey(race)) continue;
                var entry = parameters[race];

                var rootPath = isAccessory ? AccessoryDeformerParameterRootPath : EquipmentDeformerParameterRootPath;
                var fileName = rootPath + "c" + race.GetRaceCode() + "." + EquipmentDeformerParameterExtension;

                // Load the file and flip the bits as needed.
                var data = await LoadEquipmentDeformationFile(race, isAccessory, false, tx);

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

                
                if(entry.HasMaterial)
                {
                    byteToModify = (byte)(byteToModify | (1 << bitOffset));
                } else
                {
                    byteToModify = (byte)(byteToModify & ~(1 << bitOffset));
                }

                if (entry.HasModel)
                {
                    byteToModify = (byte)(byteToModify | (1 << (bitOffset + 1)));
                }
                else
                {
                    byteToModify = (byte)(byteToModify & ~(1 << (bitOffset + 1)));
                }

                data[offset + byteOffset] = byteToModify;

                await Dat.ImportType2Data(data, fileName, Constants.InternalModSourceName, referenceItem, tx);
            }
        }

        #region Public EQDP Accessors

        /// <summary>
        /// Get all the available models for a given piece of equipment.
        /// </summary>
        public async Task<List<XivRace>> GetAvailableRacialModels(IItem item, bool forceDefault = false, bool includeNPCs = false, ModTransaction tx = null)
        {
            var root = item.GetRoot();
            if(root == null)
            {
                return new List<XivRace>();
            }

            return await GetAvailableRacialModels(root.Info, forceDefault, includeNPCs, tx);
        }

        /// <summary>
        /// Get all the available models for a given piece of equipment.
        /// </summary>
        public async Task<List<XivRace>> GetAvailableRacialModels(XivDependencyRoot root, bool forceDefault = false, bool includeNPCs = false, ModTransaction tx = null)
        {
            return await GetAvailableRacialModels(root.Info, forceDefault, includeNPCs, tx);
        }

        /// <summary>
        /// Get all the available models for a given piece of equipment.
        /// </summary>
        public async Task<List<XivRace>> GetAvailableRacialModels(XivDependencyRootInfo root, bool forceDefault = false, bool includeNPCs = false, ModTransaction tx = null)
        {
            if(root.PrimaryType != XivItemType.equipment && root.PrimaryType != XivItemType.accessory)
            {
                return new List<XivRace>();
            }

            return await GetAvailableRacialModels(root.PrimaryId, root.Slot, forceDefault, includeNPCs, tx);
        }

        /// <summary>
        /// Get all the available models for a given piece of equipment.
        /// </summary>
        public async Task<List<XivRace>> GetAvailableRacialModels(int equipmentId, string slot, bool forceDefault = false, bool includeNPCs = false, ModTransaction tx = null)
        {
            var isAccessory = EquipmentDeformationParameterSet.SlotsAsList(true).Contains(slot);

            if(!isAccessory)
            {
                var slotOk = EquipmentDeformationParameterSet.SlotsAsList(false).Contains(slot);
                if(!slotOk)
                {
                    return new List<XivRace>();
                }
            }

            var root = new XivDependencyRootInfo();
            root.PrimaryId = equipmentId;
            root.Slot = slot;
            root.PrimaryType = isAccessory ? XivItemType.accessory : XivItemType.equipment;

            var dict = await GetEquipmentDeformationParameters(root, forceDefault, includeNPCs, tx);

            List<XivRace> races = new List<XivRace>();
            foreach(var kv in dict)
            {
                // Bit 0 has unknown purpose currently.
                if(kv.Value.HasModel)
                {
                    races.Add(kv.Key);
                }
            }

            return races;
        }

        /// <summary>
        /// Retrieves the raw EQDP entries for a given root with slot.
        /// </summary>
        public async Task<Dictionary<XivRace, EquipmentDeformationParameter>> GetEquipmentDeformationParameters(IItem item, bool forceDefault = false, bool includeNPCs = false, ModTransaction tx = null)
        {
            var root = item.GetRoot();

            if(root == null)
            {
                return new Dictionary<XivRace, EquipmentDeformationParameter>();
            }


            return await GetEquipmentDeformationParameters(root.Info, forceDefault, includeNPCs, tx);
        }

        /// <summary>
        /// Retrieves the raw EQDP entries for a given root with slot.
        /// </summary>
        public async Task<Dictionary<XivRace, EquipmentDeformationParameter>> GetEquipmentDeformationParameters(XivDependencyRoot root, bool forceDefault = false, bool includeNPCs = false, ModTransaction tx = null)
        {
            return await GetEquipmentDeformationParameters(root.Info, forceDefault, includeNPCs, tx);
        }

        /// <summary>
        /// Retrieves the raw EQDP entries for a given root with slot.
        /// </summary>
        public async Task<Dictionary<XivRace, EquipmentDeformationParameter>> GetEquipmentDeformationParameters(XivDependencyRootInfo root, bool forceDefault = false, bool includeNPCs = false, ModTransaction tx = null)
        {
            if (root.PrimaryType != XivItemType.equipment && root.PrimaryType != XivItemType.accessory)
            {
                return new Dictionary<XivRace, EquipmentDeformationParameter>();
            }

            return await GetEquipmentDeformationParameters(root.PrimaryId, root.Slot, root.PrimaryType == XivItemType.accessory, forceDefault, includeNPCs, tx);
        }

        /// <summary>
        /// Retrieves the raw EQDP entries for a given root with slot.
        /// </summary>
        public async Task<Dictionary<XivRace, EquipmentDeformationParameter>> GetEquipmentDeformationParameters(int equipmentId, string slot, bool isAccessory, bool forceDefault = false, bool includeNPCs = false, ModTransaction tx = null)
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


            var sets = await GetAllEquipmentDeformationSets(equipmentId, isAccessory, forceDefault, includeNPCs, tx);
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


        private static async Task<bool> EqdpFileExists(XivRace race, bool accessory, ModTransaction tx = null)
        {
            if (tx == null)
            {
                tx = ModTransaction.BeginReadonlyTransaction();
            }
            var index = await tx.GetIndexFile(XivDataFile._04_Chara);
            var rootPath = accessory ? AccessoryDeformerParameterRootPath : EquipmentDeformerParameterRootPath;
            var fileName = rootPath + "c" + race.GetRaceCode() + "." + EquipmentDeformerParameterExtension;
            var exists = index.FileExists(fileName);
            return exists;
        }

        /// <summary>
        /// Gets all of the equipment or accessory deformation sets for a given equipment id.
        /// </summary>
        /// <param name="equipmentId"></param>
        /// <param name="accessory"></param>
        /// <returns></returns>
        private async Task<Dictionary<XivRace, EquipmentDeformationParameterSet>> GetAllEquipmentDeformationSets(int equipmentId, bool accessory, bool forceDefault = false, bool includeNPCs = false, ModTransaction tx = null)
        {
            var sets = new Dictionary<XivRace, EquipmentDeformationParameterSet>();

            var races = includeNPCs ? PlayableRacesWithNPCs : PlayableRaces;
            foreach (var race in races)
            {
                if (!await EqdpFileExists(race, accessory, tx)) continue;
                var result = await GetEquipmentDeformationSet(equipmentId, race, accessory, forceDefault, tx);
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
        private async Task<EquipmentDeformationParameterSet> GetEquipmentDeformationSet(int equipmentId, XivRace race, bool accessory = false, bool forceDefault = false, ModTransaction tx = null)
        {
            var raw = await GetRawEquipmentDeformationParameters(equipmentId, race, accessory, forceDefault, tx);

            var set = new EquipmentDeformationParameterSet(accessory);

            var list = EquipmentDeformationParameterSet.SlotsAsList(accessory);

            // Pull the value apart two bits at a time.
            // Last 6 bits are not used.
            for (var idx = 0; idx < 5; idx++)
            {
                var entry = new EquipmentDeformationParameter();

                entry.HasMaterial = (raw & 1) != 0;
                raw = (ushort)(raw >> 1);
                entry.HasModel = (raw & 1) != 0;
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
        private async Task<ushort> GetRawEquipmentDeformationParameters(int equipmentId, XivRace race, bool accessory = false, bool forceDefault = false, ModTransaction tx = null)
        {
            var data = (await LoadEquipmentDeformationFile(race, accessory, forceDefault, tx)).ToArray();

            var offset = ResolveEqdpEntryOffset(data, equipmentId);

            // Some sets don't have entries.  In this case, they're treated as blank/full 0's.
            if(offset < 0)
            {
                return 0;
            }

            var parameters = new byte[EquipmentDeformerParameterEntrySize];


            for (var idx = 0; idx < EquipmentDeformerParameterEntrySize; idx++)
            {
                parameters[idx] = data[offset + idx];
            }

            return BitConverter.ToUInt16(parameters, 0);
        }

        /// <summary>
        /// Gets the raw equipment or accessory deformation parameters file for a given race.
        /// </summary>
        /// <returns></returns>
        private async Task<byte[]> LoadEquipmentDeformationFile(XivRace race, bool accessory = false, bool forceDefault = false, ModTransaction tx = null)
        {
            var rootPath = accessory ? AccessoryDeformerParameterRootPath : EquipmentDeformerParameterRootPath;
            var fileName = rootPath + "c" + race.GetRaceCode() + "." + EquipmentDeformerParameterExtension;
            return await Dat.ReadFile(fileName, forceDefault, tx);
        }

        /// <summary>
        /// Resolves the entry offset to a given SetID within an EQDP File.
        /// </summary>
        /// <param name="race"></param>
        /// <param name="setId"></param>
        /// <param name="accessory"></param>
        /// <param name="forceDefault"></param>
        /// <returns></returns>
        private async Task<int> ResolveEqdpEntryOffset(XivRace race, int setId, bool accessory = false, bool forceDefault = false, ModTransaction tx = null)
        {
            var data = await LoadEquipmentDeformationFile(race, accessory, forceDefault, tx);
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

            ushort nextOffset = (ushort)(lastOffset >= 0 ? (lastOffset + blockSize) : 0);
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
