﻿using HelixToolkit.SharpDX.Core.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.Enums;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Variants.DataContainers;
using xivModdingFramework.Variants.FileTypes;

using Index = xivModdingFramework.SqPack.FileTypes.Index;

namespace xivModdingFramework.Mods.FileTypes
{
    /// <summary>
    /// .meta files are an arbitrarily created fake file type used for storing and managing item metadata.
    /// 
    /// A .meta "file" is composed of five elements.
    /// 
    /// 1. An EQP entry (Part Hiding Information)
    /// 2. A Set of EQDP Entries (Racial Model Availability)
    /// 3. A Set of IMC Entries (IMC Part Hiding mask, etc.)
    /// 4. A set of EST Table Entries (Extra Skeleton References)
    /// 5. A GMP Entry (Gimmick/Visor Information)
    /// 
    /// .meta files must be capable of being serialized and deserialized to a pure binary representation, 
    /// for storage within DAT files or Modpack files if needed.
    /// </summary>
    public class ItemMetadata
    {
        /// <summary>
        /// The dependency root that this item meta data entry refers to.
        /// </summary>
        public XivDependencyRoot Root;

        /// <summary>
        /// Returns if this metadata object actually contains any metadata or not.
        /// </summary>
        public bool AnyMetadata
        {
            get
            {
                bool anyData = false;

                anyData = anyData | ImcEntries.Count > 0;
                anyData = anyData | EqdpEntries.Count > 0;
                anyData = anyData | EstEntries.Count > 0;
                anyData = anyData | GmpEntry != null;
                anyData = anyData | EqpEntry != null;

                return anyData;
            }
        }

        /// <summary>
        /// The available IMC entries for this item. (May be length 0)
        /// </summary>
        public List<XivImc> ImcEntries = new List<XivImc>();

        /// <summary>
        /// The available EQDP entries for the item.  (May be length 0)
        /// </summary>
        public Dictionary<XivRace, EquipmentDeformationParameter> EqdpEntries = new Dictionary<XivRace, EquipmentDeformationParameter>();

        /// <summary>
        /// The available Extra Skeleton Table entries for the item.  (May be length 0);
        /// </summary>
        public Dictionary<XivRace, ExtraSkeletonEntry> EstEntries = new Dictionary<XivRace, ExtraSkeletonEntry>();

        /// <summary>
        /// The available Gimmick Paramater for the item. (May be null)
        /// </summary>
        public GimmickParameter GmpEntry;

        /// <summary>
        /// The available EQP entry for the item.  (May be null)
        /// </summary>
        public EquipmentParameter EqpEntry = null;

        public ItemMetadata(XivDependencyRoot root)
        {
            Root = root;
        }

        /// <summary>
        /// Validates this metadata file to ensure it is being written to the correct location.
        /// </summary>
        public void Validate(string path)
        {
            const string prefix = "INVALID METADATA ERROR: ";
            if (Root == null)
            {
                throw new InvalidDataException(prefix + "Internal Root is NULL.");
            }

            if(Root.Info.GetRootFile() != path)
            {
                throw new InvalidDataException(prefix + "Internal file path not match destination file path.");
            }

            foreach(var entry in EstEntries)
            {
                if(entry.Value.SetId != Root.Info.PrimaryId && entry.Value.SetId != Root.Info.SecondaryId)
                {
                    throw new InvalidDataException(prefix + "Extra Skeleton Table entries do not match internal set number.");
                }
            }
        }

        /// <summary>
        /// Alter this metdata to properly point to a new root.
        /// </summary>
        /// <param name="newRoot"></param>
        /// <exception cref="InvalidDataException"></exception>
        public void AlterRoot(XivDependencyRoot newRoot)
        {
            if (newRoot.Info.PrimaryType != Root.Info.PrimaryType 
                || newRoot.Info.SecondaryType != Root.Info.SecondaryType 
                || newRoot.Info.Slot != Root.Info.Slot)
            {
                throw new InvalidDataException("Cannot alter metadata to different item type.");
            }

            Root = newRoot;

            // Swap EST pointers.
            foreach (var entry in EstEntries)
            {
                if (newRoot.Info.SecondaryId == null)
                {
                    entry.Value.SetId = (ushort)newRoot.Info.PrimaryId;
                } else
                {
                    entry.Value.SetId = (ushort)newRoot.Info.SecondaryId;
                }
            }
        }

        /// <summary>
        /// Gets the metadata file for an IItem entry
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public static async Task<ItemMetadata> GetMetadata(IItem item, bool forceDefault = false, ModTransaction tx = null)
        {
            return await GetMetadata(item.GetRoot(), forceDefault, tx);
        }

        /// <summary>
        /// Gets the metadata file from an internal file path.
        /// Can be the raw path to the meta file, the file root, or any file in the root's file tree.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public static async Task<ItemMetadata> GetMetadata(string internalFilePath, bool forceDefault = false, ModTransaction tx = null)
        {
            var root = await XivCache.GetFirstRoot(internalFilePath);
            return await GetMetadata(root, forceDefault, tx);
        }

        /// <summary>
        /// Gets the metadata file for a given root.
        /// </summary>
        /// <param name="root"></param>
        /// <returns></returns>
        public static async Task<ItemMetadata> GetMetadata(XivDependencyRoot root, bool forceOriginal = false, ModTransaction tx = null)
        {
            if(root == null)
            {
                return null;
            }

            if (tx == null) {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginReadonlyTransaction();
            }

            var path = root.Info.GetRootFile();

            if(await tx.FileExists(path, forceOriginal))
            {
                // If we have a valid meta file, read that.
                var data = await tx.ReadFile(path, forceOriginal);
                var meta = await Deserialize(data);
                meta.Validate(path);
                return meta;
            }
            else
            {
                // Meta file doesn't exist, create from live state.
                return await CreateFromRaw(root, forceOriginal, tx);
            }

        }


        /// <summary>
        /// Creates a new ItemMetaData entry from the constituent files around the FFXIV file system.
        /// </summary>
        /// <param name="root"></param>
        /// <returns></returns>
        private static async Task<ItemMetadata> CreateFromRaw(XivDependencyRoot root, bool forceDefault = false, ModTransaction tx = null)
        {

            if (tx == null) {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginReadonlyTransaction();
            }


            var _eqp = new Eqp();

            // These functions generate the path::offset to each of our
            // contiguous metadata entries.

            var ret = new ItemMetadata(root);
            if (Imc.UsesImc(root))
            {
                var baseImc = root.GetRawImcFilePath();
                if (await tx.FileExists(baseImc, forceDefault))
                {
                    var imcPaths = await root.GetImcEntryPaths(tx);
                    if (imcPaths.Count > 0)
                    {
                        ret.ImcEntries = await Imc.GetEntries(imcPaths, forceDefault, tx);
                    }
                } else
                {
                    // IMC file did not exist, so no entries.
                }
            }

            ret.EqpEntry = await _eqp.GetEqpEntry(root.Info, forceDefault, tx);

            ret.EqdpEntries = await _eqp.GetEquipmentDeformationParameters(root.Info, forceDefault, false, tx);

            ret.EstEntries = await Est.GetExtraSkeletonEntries(root, forceDefault, tx);

            ret.GmpEntry = await _eqp.GetGimmickParameter(root, forceDefault, tx);

            return ret;
        }

        /// <summary>
        /// Saves this metadata file to the FFXIV file system.
        /// </summary>
        /// <param name="meta"></param>
        /// <returns></returns>
        public static async Task SaveMetadata(ItemMetadata meta, string source, ModTransaction tx = null, bool fillMissing = false)
        {

            var path = meta.Root.Info.GetRootFile();
            var item = meta.Root.GetFirstItem();

            var boiler = await TxBoiler.BeginWrite(tx, true);
            tx = boiler.Transaction;
            try
            {

                await Dat.ImportType2Data(await Serialize(meta), path, source, item, tx);

                if (fillMissing)
                {
                    await meta.FillMissingFiles(source, tx);
                }
                await boiler.Commit();
            }
            catch(Exception ex)
            {
                await boiler.Catch();
                throw;
            }
        }

        /// <summary>
        /// Fills any files that are missing for this metadata.
        /// In specific, this adds additional materials for missing sets,
        /// and adds additional models for missing models.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        public async Task FillMissingFiles(string source, ModTransaction tx)
        {

            foreach (var kv in EqdpEntries)
            {
                if (kv.Value.HasModel == false) continue;
                // Here we have a new race, we need to create a model for it.
                await Mdl.AddRacialModel(Root.Info.PrimaryId, Root.Info.Slot, kv.Key, source, tx);
            }

            if (ImcEntries.Count > 0)
            {
                // We need to validate the material paths.

                // First find the base files to copy. (Just always copy from set 1 for simplicity)
                var copySource = await Root.GetMaterialFiles(1, tx);
                var item = Root.GetFirstItem();

                var newMaterialSetMax = ImcEntries.Select(x => x.MaterialSet).Max();

                for (int i = 1; i <= newMaterialSetMax; i++)
                {
                    foreach (var material in copySource)
                    {
                        var dest = material.Replace("v0001", "v" + i.ToString().PadLeft(4, '0'));
                        if (await tx.FileExists(dest))
                        {
                            // File exists.  No need to modify.
                            return;
                        }

                        var mod = await tx.GetMod(dest);
                        if (mod != null)
                        {
                            // Mod exists.  Just re-enable it.
                            await Modding.SetModState(EModState.Enabled, mod.Value, tx);
                            return;
                        }

                        // Material doesn't exist at all, have to copy in a new one.
                        await Dat.CopyFile(material, dest, source, false, item, tx);
                    }
                }
            }
        }
        internal static async Task ApplyMetadata(string internalPath, bool forceOriginal = false, ModTransaction tx = null)
        {
            var exists = await tx.FileExists(internalPath, forceOriginal);
            if(!exists)
            {
                // If the metadata file does not exist, we're always restoring to default state.
                forceOriginal = true;
            }
            
            var meta = await GetMetadata(internalPath, forceOriginal, tx);
            meta.Validate(internalPath);
            await ApplyMetadata(meta, tx);
        }

        /// <summary>
        /// Applies this Metadata object to the FFXIV file system.
        /// This should only called by Dat.WriteToDat() / RestoreDefaultMetadata()
        /// and sufficiently advanced/complex functions that are doing raw data writes (Ex. Modpack imports)
        /// </summary>
        internal static async Task ApplyMetadata(ItemMetadata meta, ModTransaction tx)
        {
            var _eqp = new Eqp();
            var df = IOUtil.GetDataFileFromPath(meta.Root.Info.GetRootFile());

            var dummyItem = new XivGenericItemModel();
            dummyItem.Name = Constants.InternalModSourceName;
            dummyItem.SecondaryCategory = Constants.InternalModSourceName;


            var boiler = await TxBoiler.BeginWrite(tx);
            tx = boiler.Transaction;
            try
            {
                var index = await tx.GetIndexFile(df);
                var modlist = await tx.GetModList();


                if (meta.ImcEntries.Count > 0)
                {
                    var imcPath = meta.Root.GetRawImcFilePath();
                    await Imc.SaveEntries(imcPath, meta.Root.Info.Slot, meta.ImcEntries, dummyItem, tx);
                }
                else
                {
                    var imcPath = meta.Root.GetRawImcFilePath();
                    if (Imc.UsesImc(meta.Root) && await tx.FileExists(imcPath))
                    {
                        var mod = await tx.GetMod(imcPath);
                        if (mod != null)
                        {
                            // Remove IMC file if we have 0 entries and it's a file we created.
                            await tx.Set8xDataOffset(imcPath, 0);
                        }
                    }
                }

                var preOffset = (await tx.GetIndexFile(df)).Get8xDataOffset(Eqp.EquipmentParameterFile);
                // Applying EQP data via set 0 is not allowed, as it is a special set hard-coded to use Set 1's data.
                if (meta.EqpEntry != null && !(meta.Root.Info.PrimaryType == Items.Enums.XivItemType.equipment && meta.Root.Info.PrimaryId == 0))
                {
                    await _eqp.SaveEqpEntry(meta.Root.Info.PrimaryId, meta.EqpEntry, dummyItem, tx);
                }

                //var postOffset = (await tx.GetIndexFile(df)).Get8xDataOffset(Eqp.EquipmentParameterFile);
                if (meta.EqdpEntries.Count > 0)
                {
                    await _eqp.SaveEqdpEntries((uint)meta.Root.Info.PrimaryId, meta.Root.Info.Slot, meta.EqdpEntries, dummyItem, tx);
                }

                if (meta.EstEntries.Count > 0)
                {
                    var type = Est.GetEstType(meta.Root);
                    var entries = meta.EstEntries.Values.ToList();
                    await Est.SaveExtraSkeletonEntries(type, entries, dummyItem, tx);
                }

                if (meta.GmpEntry != null)
                {
                    await _eqp.SaveGimmickParameter(meta.Root.Info.PrimaryId, meta.GmpEntry, dummyItem, tx);
                }

                await boiler.Commit();
            }
            catch
            {
                await boiler.Cancel();
                throw;
            }
        }


        /// <summary>
        /// Restores the original SE metadata for this root.
        /// This should be called when setting the offset to 0 for a file.
        /// </summary>
        public static async Task RestoreDefaultMetadata(XivDependencyRoot root, ModTransaction tx = null)
        {
            var original = await GetMetadata(root, true, tx);
            await ApplyMetadata(original, tx);
        }


        #region Binary Serialization/Deserialization

        // ==================================================== //
        //              SERIALIZATION DOCUMENTATION             //
        //                                                      //
        //  Metadata is serialized as such:                     //
        //  HEADER                                              //
        //      UINT   - METADATA VERSION #                     //
        //      0 TERMINATED ASCII STRING - FILE PATH           //
        //      UINT   - # of Header Entries                    //
        //      UINT   - Per-Header Size                        //
        //      UINT   - Header Entries start offset            //
        //          For each header entry (12 bytes)            //
        //              UINT   - METADATA TYPE                  //
        //              UINT   - METADATA START OFFSET          //
        //              UINT   - METADATA SIZE                  //
        //                                                      //
        //  [ DATA entries start at [Data Start Offset]         //
        //  Data format follows standard XIV format for their   //
        //  various data types.                                 //
        //                                                      //
        //                                                      //
        //  This format was chosen to give us some leeway       //
        //   for the future, should the format of the metadata  //
        //   change, or should we wish to add more metadata     //
        //   information into the files, while still            //
        //   maintaining backwards compatability.               //
        // ==================================================== //

        /// <summary>
        /// Binary enum types for usage when serializing/deserializing the data in question.
        /// These values may be added to but should --NEVER BE CHANGED--, as existing Metadata
        /// entries will depend on the values of these enums.
        /// </summary>
        private enum MetaDataType : uint
        {
            Invalid = 0,
            Imc = 1,
            Eqdp = 2,
            Eqp = 3,
            Est = 4,
            Gmp = 5,
        };

        const uint _METADATA_VERSION = 2;

        // Version History
        // 1 - Initial introduction (EQP, IMC, EQDP Files)
        // 2 - Addition of EST/GMP files.

        const uint _METADATA_HEADER_SIZE = 12;

        /// <summary>
        /// Serializes a Metadata object into a byte representation for storage in dat or mod files.
        /// </summary>
        /// <param name="meta"></param>
        /// <returns></returns>
        public static async Task<byte[]> Serialize(ItemMetadata meta)
        {
            List<byte> bytes = new List<byte>();


            // Write general header.
            bytes.AddRange(BitConverter.GetBytes(_METADATA_VERSION));
            var path = meta.Root.Info.GetRootFile();
            bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(path));
            bytes.Add(0);

            uint entries = 0;
            bool hasImc = false, hasEqp = false, hasEqdp = false, hasEst = false, hasGmp = false;
            if(meta.ImcEntries.Count > 0)
            {
                entries++;
                hasImc = true;
            }

            // Set 0 is a unique exception where SE hard-coded it to use Set 1's EQP data.  As such, we don't allow that data to be altered
            // via writing to set 0, just for safety purposes.
            if(meta.EqpEntry != null && !(meta.Root.Info.PrimaryType == Items.Enums.XivItemType.equipment && meta.Root.Info.PrimaryId == 0))
            {
                entries++;
                hasEqp = true;
            }

            if(meta.EqdpEntries.Count > 0)
            {
                entries++;
                hasEqdp = true;
            }

            if (meta.EstEntries.Count > 0)
            {
                entries++;
                hasEst = true;
            }
            if (meta.GmpEntry != null)
            {
                entries++;
                hasGmp = true;
            }

            bytes.AddRange(BitConverter.GetBytes(entries));
            bytes.AddRange(BitConverter.GetBytes(_METADATA_HEADER_SIZE));
            bytes.AddRange(BitConverter.GetBytes((uint) bytes.Count + 4));

            

            // Write per part header; save the offset for the metadata we need to fill in later.
            int imcHeaderInfoOffset = 0;
            if(hasImc)
            {
                imcHeaderInfoOffset = bytes.Count;
                bytes.AddRange(BitConverter.GetBytes((uint)MetaDataType.Imc));
                bytes.AddRange(BitConverter.GetBytes(0));
                bytes.AddRange(BitConverter.GetBytes(0));
            }

            int eqpHeaderInfoOffset = 0;
            if (hasEqp)
            {
                eqpHeaderInfoOffset = bytes.Count;
                bytes.AddRange(BitConverter.GetBytes((uint)MetaDataType.Eqp));
                bytes.AddRange(BitConverter.GetBytes(0));
                bytes.AddRange(BitConverter.GetBytes(0));
            }

            int eqdpHeaderInfoOffset = 0;
            if (hasEqdp)
            {
                eqdpHeaderInfoOffset = bytes.Count;
                bytes.AddRange(BitConverter.GetBytes((uint)MetaDataType.Eqdp));
                bytes.AddRange(BitConverter.GetBytes(0));
                bytes.AddRange(BitConverter.GetBytes(0));
            }

            int estHeaderInfoOffset = 0;
            if (hasEst)
            {
                estHeaderInfoOffset = bytes.Count;
                bytes.AddRange(BitConverter.GetBytes((uint)MetaDataType.Est));
                bytes.AddRange(BitConverter.GetBytes(0));
                bytes.AddRange(BitConverter.GetBytes(0));
            }

            int gmpHeaderOffset = 0;
            if (hasGmp)
            {
                gmpHeaderOffset = bytes.Count;
                bytes.AddRange(BitConverter.GetBytes((uint)MetaDataType.Gmp));
                bytes.AddRange(BitConverter.GetBytes(0));
                bytes.AddRange(BitConverter.GetBytes(0));
            }




            // Write the actual data.
            if (hasImc)
            {
                // Serialize IMC Data here.
                var imcData = SerializeImcData(meta);
                var offset = bytes.Count;
                bytes.AddRange(imcData);

                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes((uint)offset), imcHeaderInfoOffset + 4);
                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes((uint)imcData.Length), imcHeaderInfoOffset + 8);
            }

            if (hasEqp)
            {
                // Serialize EQP Data here.
                var eqpData = SerializeEqpData(meta);
                var offset = bytes.Count;
                bytes.AddRange(eqpData);

                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes((uint)offset), eqpHeaderInfoOffset + 4);
                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes((uint)eqpData.Length), eqpHeaderInfoOffset + 8);
            }

            if (hasEqdp)
            {
                // Serialize EQP Data here.
                var eqdpData = SerializeEqdpData(meta);
                var offset = bytes.Count;
                bytes.AddRange(eqdpData);

                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes((uint)offset), eqdpHeaderInfoOffset + 4);
                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes((uint)eqdpData.Length), eqdpHeaderInfoOffset + 8);
            }

            if(hasEst)
            {
                // Serialize EST Data here.
                var estData = SerializeEstData(meta);
                var offset = bytes.Count;
                bytes.AddRange(estData);

                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes((uint)offset), estHeaderInfoOffset + 4);
                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes((uint)estData.Length), estHeaderInfoOffset + 8);
            }

            if(hasGmp)
            {
                // Serialize EST Data here.
                var gmpData = SerializeGmpData(meta);
                var offset = bytes.Count;
                bytes.AddRange(gmpData);

                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes((uint)offset), gmpHeaderOffset + 4);
                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes((uint)gmpData.Length), gmpHeaderOffset + 8);

            }


            return bytes.ToArray();
        }
        private static byte[] SerializeGmpData(ItemMetadata meta)
        {
            // GMP data is relatively straight forward.  Just stick the 5 btes in the file.
            return meta.GmpEntry.GetBytes();
        }

        private static byte[] SerializeEstData(ItemMetadata meta)
        {
            // 6 Bytes per entry.  The entries already contain their Racial information, so there's no need to re-include that.
            var data = new byte[(meta.EstEntries.Count * 6)];


            var idx = 0;
            foreach (var kv in meta.EstEntries)
            {
                var offset = (idx * 6);
                IOUtil.ReplaceBytesAt(data, BitConverter.GetBytes((ushort)kv.Value.Race.GetRaceCodeInt()), offset);
                IOUtil.ReplaceBytesAt(data, BitConverter.GetBytes((ushort)kv.Value.SetId), offset + 2);
                IOUtil.ReplaceBytesAt(data, BitConverter.GetBytes((ushort)kv.Value.SkelId), offset + 4);
                idx++;
            }
            return data;
        }


        /// <summary>
        /// Serializes the IMC data entries for the given meta file.
        /// </summary>
        /// <param name="meta"></param>
        /// <returns></returns>
        private static byte[] SerializeImcData(ItemMetadata meta)
        {
            // IMC Serialization is pretty straight forward, it's just
            // write the binary data from the IMC entries in sequence.

            // IMC entries are a static 6 bytes long, and ordered in simple
            // straight consecutive order, so our index order matches subeset ID.

            List<byte> bytes = new List<byte>();
            foreach(var entry in meta.ImcEntries)
            {
                bytes.AddRange(Imc.SerializeEntry(entry));
            }

            return bytes.ToArray();
        }


        /// <summary>
        /// Deserializes the binary IMC data into a list of IMC entries.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private static List<XivImc> DeserializeImcData(byte[] data, XivDependencyRoot root, uint dataVersion)
        {
            const int ImcSubEntrySize = 6;
            var entries = data.Length / ImcSubEntrySize;

            List<XivImc> ret = new List<XivImc>();
            for (int i = 0; i < entries; i++)
            {
                var entryData = data.Skip(i * ImcSubEntrySize).Take(ImcSubEntrySize).ToArray();
                ret.Add(Imc.DeserializeEntry(entryData));
            }

            return ret;
        }

        /// <summary>
        /// Serializes the EQDP data entries for the given meta file.
        /// </summary>
        /// <param name="meta"></param>
        /// <returns></returns>
        private static byte[] SerializeEqdpData(ItemMetadata meta)
        {
            // EQDP Serialization is fairly simple.
            // [uint] Race ID [byte] EQDP entry data (in the two least significant bits)

            List<byte> bytes = new List<byte>();
            foreach (var kv in meta.EqdpEntries)
            {
                bytes.AddRange(BitConverter.GetBytes((uint)Int32.Parse(kv.Key.GetRaceCode())));
                bytes.Add(kv.Value.GetByte());
            }

            return bytes.ToArray();
        }

        /// <summary>
        /// Deserializes the binary EQDP data into a dictionary of EQDP entries.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private static Dictionary<XivRace, EquipmentDeformationParameter> DeserializeEqdpData(byte[] data, XivDependencyRoot root, uint dataVersion)
        {
            const int eqdpEntrySize = 5;
            var entries = data.Length / eqdpEntrySize;

            var ret = new Dictionary<XivRace, EquipmentDeformationParameter>();

            var read = 0;
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                while (read < entries)
                {
                    var raceCode = reader.ReadInt32();
                    var race = XivRaces.GetXivRace(raceCode.ToString().PadLeft(4, '0'));

                    var eqpByte = reader.ReadByte();
                    var entry = EquipmentDeformationParameter.FromByte(eqpByte);

                    ret.Add(race, entry);

                    read++;
                }
            }

            // Catch for cases where for some reason the EQP doesn't have all races,
            // for example, SE adding more races in the future, and we're
            // reading old metadata entries.
            foreach (var race in Eqp.PlayableRaces)
            {
                if (!ret.ContainsKey(race))
                {
                    ret.Add(race, new EquipmentDeformationParameter());
                }
            }

            return ret;
        }

        /// <summary>
        /// Deserializes the binary EQP data into a EQP entry.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private static EquipmentParameter DeserializeEqpData(byte[] data, XivDependencyRoot root, uint dataVersion)
        {
            // This one's relatively simple for the moment.
            // Though managing this data may grow more complex if SE decides to add more 
            // bytes into the EQP entries at some point in the future.
            var ret = new EquipmentParameter(root.Info.Slot, data);
            return ret;
        }


        /// <summary>
        /// Serializes the EQP data entries for the given meta file.
        /// </summary>
        /// <param name="meta"></param>
        /// <returns></returns>
        private static byte[] SerializeEqpData(ItemMetadata meta)
        {
            return meta.EqpEntry.GetBytes();
        }



        private static async Task<Dictionary<XivRace, ExtraSkeletonEntry>> DeserializeEstData(byte[] data, XivDependencyRoot root, uint dataVersion)
        {

            if(dataVersion == 1)
            {
                // Version 1 didn't include EST data, so just get the defaults.
                return await Est.GetExtraSkeletonEntries(root);
            }


            // 6 Bytes per entry.
            var count = data.Length / 6;
            var ret = new Dictionary<XivRace, ExtraSkeletonEntry>(count);

            for(int i = 0; i < count; i++)
            {
                var offset = i * 6;
                var raceCode = BitConverter.ToUInt16(data, offset);
                var setId = BitConverter.ToUInt16(data, offset + 2);
                var skelId = BitConverter.ToUInt16(data, offset + 4);

                var race = XivRaces.GetXivRace(raceCode);

                ret.Add(race, new ExtraSkeletonEntry(race, setId, skelId));
            }

            return ret;
        }

        private static async Task<GimmickParameter> DeserializeGmpData(byte[] data, XivDependencyRoot root, uint dataVersion)
        {
            if(dataVersion == 1)
            {
                // Version 1 didn't have GMP data, so include the default GMP data.
                var _eqp = new Eqp();
                return await _eqp.GetGimmickParameter(root, true);
            }
            // 5 Bytes to parse, ezpz lemon sqzy
            return new GimmickParameter(data);
        }



        /// <summary>
        /// Deserializes binary byte data into an IteMetadata object.
        /// </summary>
        /// <param name="internalPath"></param>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static async Task<ItemMetadata> Deserialize(byte[] data)
        {
            using(var reader = new BinaryReader(new MemoryStream(data))) {

                uint version = reader.ReadUInt32();

                // File path name.
                var path = "";
                char c;
                while((c = reader.ReadChar()) != '\0')
                {
                    path += c;
                }

                var root = await XivCache.GetFirstRoot(path);
                var ret = new ItemMetadata(root);

                // General header data.
                uint headerCount = reader.ReadUInt32();
                uint perHeaderSize = reader.ReadUInt32();
                uint headerEntryStart = reader.ReadUInt32();

                // Per-Segment Header data.
                reader.BaseStream.Seek(headerEntryStart, SeekOrigin.Begin);

                List<(MetaDataType type, uint offset, uint size)> entries = new List<(MetaDataType type, uint size, uint offset)>();

                for(int i = 0; i < headerCount; i++)
                {
                    // Save offset.
                    var currentOffset = reader.BaseStream.Position;

                    // Read data.
                    MetaDataType type = (MetaDataType) reader.ReadUInt32();
                    uint offset = reader.ReadUInt32();
                    uint size = reader.ReadUInt32();

                    entries.Add((type, offset, size));

                    // Seek to next.
                    reader.BaseStream.Seek(currentOffset + perHeaderSize, SeekOrigin.Begin);
                }


                var imc = entries.FirstOrDefault(x => x.type == MetaDataType.Imc);
                if(imc.type != MetaDataType.Invalid)
                {
                    reader.BaseStream.Seek(imc.offset, SeekOrigin.Begin);
                    var bytes = reader.ReadBytes((int) imc.size);

                    // Deserialize IMC entry bytes here.
                    ret.ImcEntries = DeserializeImcData(bytes, root, version);
                }

                var eqp = entries.FirstOrDefault(x => x.type == MetaDataType.Eqp);
                if (eqp.type != MetaDataType.Invalid)
                {
                    reader.BaseStream.Seek(eqp.offset, SeekOrigin.Begin);
                    var bytes = reader.ReadBytes((int)eqp.size);

                    // Deserialize EQP entry bytes here.
                    ret.EqpEntry = DeserializeEqpData(bytes, root, version);
                }

                var eqdp = entries.FirstOrDefault(x => x.type == MetaDataType.Eqdp);
                if (eqdp.type != MetaDataType.Invalid)
                {
                    reader.BaseStream.Seek(eqdp.offset, SeekOrigin.Begin);
                    var bytes = reader.ReadBytes((int)eqdp.size);

                    // Deserialize EQDP entry bytes here.
                    ret.EqdpEntries = DeserializeEqdpData(bytes, root, version);
                }

                var est = entries.FirstOrDefault(x => x.type == MetaDataType.Est);
                if (est.type != MetaDataType.Invalid)
                {
                    reader.BaseStream.Seek(est.offset, SeekOrigin.Begin);
                    var bytes = reader.ReadBytes((int)est.size);

                    // Deserialize EQDP entry bytes here.
                    ret.EstEntries = await DeserializeEstData(bytes, root, version);
                }

                var gmp = entries.FirstOrDefault(x => x.type == MetaDataType.Gmp);
                if (gmp.type != MetaDataType.Invalid)
                {
                    reader.BaseStream.Seek(gmp.offset, SeekOrigin.Begin);
                    var bytes = reader.ReadBytes((int)gmp.size);

                    // Deserialize EQDP entry bytes here.
                    ret.GmpEntry = await DeserializeGmpData(bytes, root, version);
                }


                // Done deserializing all the parts.
                return ret;
            }
        }

        #endregion
    }
}
