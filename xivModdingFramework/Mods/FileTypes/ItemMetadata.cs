using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Variants.DataContainers;
using xivModdingFramework.Variants.FileTypes;

namespace xivModdingFramework.Mods.FileTypes
{
    /// <summary>
    /// .meta files are an arbitrarily created fake file type used for storing and managing item metadata.
    /// 
    /// A .meta "file" is composed of three elements.
    /// 
    /// 1. An EQP entry (Part Hiding Information)
    /// 2. A Set of EQDP Entries (Racial Model Availability)
    /// 3. A Set of IMC Entries (IMC Part Hiding mask, etc.)
    /// 
    /// .meta files must be capable of being serialized and deserialized to a pure binary representation, 
    /// for storage within DAT files or Modpack files if needed.
    /// </summary>
    public class ItemMetadata
    {
        /// <summary>
        /// The dependency root that this item meta data entry refers to.
        /// </summary>
        public readonly XivDependencyRoot Root;

        /// <summary>
        /// The available IMC entries for this item. (May be length 0)
        /// </summary>
        public List<XivImc> ImcEntries = new List<XivImc>();


        /// <summary>
        /// The available EQDP entries for the item.  (May be length 0)
        /// </summary>
        public Dictionary<XivRace, EquipmentDeformationParameter> EqdpEntries = new Dictionary<XivRace, EquipmentDeformationParameter>();

        /// <summary>
        /// The available EQP entry for the item.  (May be null)
        /// </summary>
        public EquipmentParameter EqpEntry = null;

        public ItemMetadata(XivDependencyRoot root)
        {
            Root = root;
        }

        /// <summary>
        /// Gets the metadata file for an IItem entry
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public static async Task<ItemMetadata> GetMetadata(IItem item)
        {
            return await GetMetadata(item.GetRoot());
        }

        /// <summary>
        /// Gets the metadata file from an internal file path.
        /// Can be the raw path to the meta file, the file root, or any file in the root's file tree.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public static async Task<ItemMetadata> GetMetadata(string internalFilePath)
        {
            var root = await XivCache.GetFirstRoot(internalFilePath);
            return await GetMetadata(root);
        }

        /// <summary>
        /// Gets the metadata file for a given root.
        /// </summary>
        /// <param name="root"></param>
        /// <returns></returns>
        public static async Task<ItemMetadata> GetMetadata(XivDependencyRoot root)
        {

            var _modding = new Modding(XivCache.GameInfo.GameDirectory);
            var _dat = new Dat(XivCache.GameInfo.GameDirectory);
            var filePath = root.Info.GetRootFile();

            var mod = await _modding.TryGetModEntry(filePath);

            if(mod != null && mod.enabled)
            {
                // We have modded metadata stored in the .meta file in the DAT we can use.
                var data = await _dat.GetType2Data(filePath, false);

                // Run it through the binary deserializer and we're good.
                return await Deserialize(data);
            } else
            {
                // This is the fun part where we get to pull the Metadata from all the disparate files around the FFXIV File System.
                return await CreateFromRaw(root);
            }
        }


        /// <summary>
        /// Creates a new ItemMetaData entry from the constituent files around the FFXIV file system.
        /// </summary>
        /// <param name="root"></param>
        /// <returns></returns>
        private static async Task<ItemMetadata> CreateFromRaw(XivDependencyRoot root, bool forceDefault = false)
        {
            if(forceDefault == true)
            {
                throw new NotImplementedException("Not Yet Implemented.");
            }

            var _eqp = new Eqp(XivCache.GameInfo.GameDirectory);
            var _imc = new Imc(XivCache.GameInfo.GameDirectory);

            // These functions generate the path::offset to each of our
            // contiguous metadata entries.
            var imcPaths = await root.GetImcEntryPaths();
            var eqpPath = root.GetEqpEntryPath();
            var eqdpPaths = root.GetEqdpEntryPaths();

            var ret = new ItemMetadata(root);

            ret.ImcEntries = await _imc.GetEntries(imcPaths);
            ret.EqpEntry = await _eqp.GetEqpEntry(eqpPath);
            ret.EqdpEntries = await _eqp.GetEqdpEntries(eqdpPaths);

            return ret;
        }


        /// <summary>
        /// Applies this Metadata object to the FFXIV file system.
        /// This should only called by Dat.WriteToDat() / RestoreDefaultMetadata()
        /// </summary>
        public static async Task ApplyMetadata(ItemMetadata meta)
        {

        }


        /// <summary>
        /// Restores the original SE metadata for this root.
        /// This should only be called by Index.DeleteFileDescriptor().
        /// </summary>
        public static async Task RestoreDefaultMetadata(XivDependencyRoot root)
        {
            var original = await ItemMetadata.CreateFromRaw(root, true);
            await ApplyMetadata(original);
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
            Eqp = 3
        };

        const uint _METADATA_VERSION = 0;
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
            for (int i = 0; i < path.Length; i++)
            {
                // TODO - FIXFIX - NEED TO VERIFY THIS IS WRITING SINGLE BYTES FOR EACH CHAR
                bytes.AddRange(BitConverter.GetBytes(path[i]));
            }

            uint entries = 0;
            bool hasImc = false, hasEqp = false, hasEqdp = false;
            if(meta.ImcEntries.Count > 0)
            {
                entries++;
                hasImc = true;
            }

            if(meta.EqpEntry != null)
            {
                entries++;
                hasEqp = true;
            }

            if(meta.EqdpEntries.Count > 0)
            {
                entries++;
                hasEqdp = true;
            }

            bytes.AddRange(BitConverter.GetBytes(entries));
            bytes.AddRange(BitConverter.GetBytes(_METADATA_HEADER_SIZE));
            bytes.AddRange(BitConverter.GetBytes((uint) bytes.Count));

            

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
            if (hasEqp)
            {
                eqdpHeaderInfoOffset = bytes.Count;
                bytes.AddRange(BitConverter.GetBytes((uint)MetaDataType.Eqdp));
                bytes.AddRange(BitConverter.GetBytes(0));
                bytes.AddRange(BitConverter.GetBytes(0));
            }


            // Write the actual data.
            if (hasImc)
            {
                // Serialize IMC Data here.
                var imcData = SerializeImcData(meta);
                var offset = bytes.Count;

                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes((uint)offset), imcHeaderInfoOffset + 4);
                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes((uint)imcData.Length), imcHeaderInfoOffset + 8);
            }

            if (hasEqp)
            {
                // Serialize EQP Data here.
                var eqpData = SerializeEqpData(meta);
                var offset = bytes.Count;

                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes((uint)offset), eqpHeaderInfoOffset + 4);
                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes((uint)eqpData.Length), eqpHeaderInfoOffset + 8);
            }

            if (hasEqdp)
            {
                // Serialize EQP Data here.
                var eqdpData = SerializeEqdpData(meta);
                var offset = bytes.Count;

                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes((uint)offset), eqdpHeaderInfoOffset + 4);
                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes((uint)eqdpData.Length), eqdpHeaderInfoOffset + 8);
            }


            return bytes.ToArray();
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
        private static List<XivImc> DeserializeImcData(byte[] data, uint dataVersion)
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
                bytes.AddRange(BitConverter.GetBytes(kv.Value.GetByte()));
            }

            return bytes.ToArray();
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


        /// <summary>
        /// Deserializes the binary EQDP data into a dictionary of EQDP entries.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private static Dictionary<XivRace, EquipmentDeformationParameter> DeserializeEqdpData(byte[] data, uint dataVersion)
        {
            const int eqdpEntrySize = 5;
            var entries = data.Length / eqdpEntrySize;

            var ret = new Dictionary<XivRace, EquipmentDeformationParameter>();

            var read = 0;
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                while(read < entries)
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
            foreach (var race in Eqp.DeformationAvailableRaces) {
                if(!ret.ContainsKey(race))
                {
                    ret.Add(race, new EquipmentDeformationParameter());
                }
            }

            return ret;
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

                List<(MetaDataType type, uint size, uint offset)> entries = new List<(MetaDataType type, uint size, uint offset)>();

                for(int i = 0; i < headerCount; i++)
                {
                    // Save offset.
                    var currentOffset = reader.BaseStream.Position;

                    // Read data.
                    MetaDataType type = (MetaDataType) reader.ReadUInt32();
                    uint offset = reader.ReadUInt32();
                    uint size = reader.ReadUInt32();

                    entries.Add((type, size, offset));

                    // Seek to next.
                    reader.BaseStream.Seek(currentOffset + perHeaderSize, SeekOrigin.Begin);
                }


                var imc = entries.FirstOrDefault(x => x.type == MetaDataType.Imc);
                if(imc.type != MetaDataType.Invalid)
                {
                    reader.BaseStream.Seek(imc.offset, SeekOrigin.Begin);
                    var bytes = reader.ReadBytes((int) imc.size);

                    // Deserialize IMC entry bytes here.
                    ret.ImcEntries = DeserializeImcData(bytes, version);
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
                    ret.EqdpEntries = DeserializeEqdpData(bytes, version);
                }

                // Done deserializing all the parts.
                return ret;
            }
        }

        #endregion
    }
}
