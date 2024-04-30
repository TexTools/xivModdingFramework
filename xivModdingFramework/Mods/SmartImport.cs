using HelixToolkit.SharpDX.Core.Helper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.FileTypes;

namespace xivModdingFramework.Mods
{
    public static class SmartImport
    {
        /// <summary>
        /// Handles importing an arbitrary external file into the game.
        /// NOTE: If a Cached index/modlist is provided, the resulting index/modlist changes will not be automatically saved,
        /// as it is assumed to be part of a batch write that will be handled later.
        /// </summary>
        /// <param name="externalPath"></param>
        /// <param name="internalPath"></param>
        /// <param name="sourceApplication">Application name to use when writing to the modlist.</param>
        /// <param name="modPackBinding">Modpack this mod entry should be attached to.</param>
        /// <param name="cachedIndexFile"></param>
        /// <param name="cachedModList"></param>
        /// <returns></returns>
        public static async Task Import(string externalPath, string internalPath, string sourceApplication = "Unknown", ModPack modPackBinding = null, IndexFile cachedIndexFile = null, ModList cachedModList = null)
        {
            if (!File.Exists(externalPath))
            {
                throw new FileNotFoundException("Requested import file does not exist or was inaccessible.");
            }

            await Task.Run(async () =>
            {
                var data = await CreateCompressedFile(externalPath, internalPath);
                var _dat = new Dat(XivCache.GameInfo.GameDirectory);

                // Establish an item to associate the mod import with, if one exists.
                var root = await XivCache.GetFirstRoot(internalPath);
                IItem item = null;
                if (root != null) {
                    item = root.GetFirstItem();
                }

                await _dat.WriteModFile(data, internalPath, sourceApplication, item, cachedIndexFile, cachedModList, modPackBinding);
            });
        }


        /// <summary>
        /// Handles full path creation of compressed SQPack-Ready data from an external file source of arbitrary type.
        /// </summary>
        /// <param name="externalPath"></param>
        /// <param name="internalPath"></param>
        /// <returns></returns>
        public static async Task<byte[]> CreateCompressedFile(string externalPath, string internalPath)
        {
            return await CreateCompressedFile(await CreateUncompressedFile(externalPath, internalPath));
        }

        /// <summary>
        /// Takes an unknown file in the form of an external path, and converts it into an Uncompressed File ready to be compressed and added to the TT file system.
        /// 
        /// Primarily this will take FBX/Model Files and DDS/PNG/Image Files and convert them to MDLs and DDS files.
        /// Existing Compressed files will be kept in-tact, and binary data will be kept in-tact in order to be injected as Type 2 data.
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static async Task<byte[]> CreateUncompressedFile(string externalPath, string internalPath)
        {
            ulong magic;
            uint magic32;
            uint magic16;
            string magic20b, magic16b;

            // Pull the magic info from the file.
            using(var f = File.OpenRead(externalPath))
            {
                using(var br = new BinaryReader(f))
                {
                    var header = br.ReadBytes(20);
                    magic = BitConverter.ToUInt64(header, 0);
                    magic32 = BitConverter.ToUInt32(header, 0);
                    magic16 = BitConverter.ToUInt16(header, 0);
                    magic20b = BitConverter.ToString(header, 0, 20);
                    magic16b = BitConverter.ToString(header, 0, 16);
                }
            }

            var _mdl = new Mdl(XivCache.GameInfo.GameDirectory);
            var _tex = new Tex(XivCache.GameInfo.GameDirectory);


            byte[] result = null;
            if(magic16 == BMPMagic || magic == PNGMagic || magic32 == DDSMagic)
            {
                // Convert the image to DDS if necessary.
                return await _tex.MakeTexData(internalPath, externalPath);
            } else if(magic20b == FBXMagic || magic16b == SQLiteMagic)
            {
                // Do Model import.
                return await _mdl.FileToModelBytes(externalPath, internalPath);
            }

            // This is either a binary file for type 2 compression or an already compressed file.
            // Either way, just send it on as is.

            return File.ReadAllBytes(externalPath);
        }


        /// <summary>
        /// Takes an unknown file in the form of uncompressed byte data and compresses it if necessary, turning it into an SQPack-friendly file if it is not already.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static async Task<byte[]> CreateCompressedFile(byte[] data)
        {
            const uint _SaneHeaderMaximum = 128 * 100;

            // There's no way someone's importing a single file over 500MB... right?
            const uint _SaneFileSizeMaximum = 500000000;

            uint[] _ValidFileTypes = new uint[] { 1, 2, 3, 4 };

            var possiblyHeaderSize = BitConverter.ToUInt32(data, 0);
            var possiblyFileType = BitConverter.ToUInt32(data, 4);
            var possiblyFileSize = BitConverter.ToUInt32(data, 8);

            // This might be an already compressed SQPack file.
            if (possiblyHeaderSize <= _SaneHeaderMaximum && possiblyHeaderSize % 128 == 0)
            {
                if (_ValidFileTypes.Contains(possiblyFileType))
                {
                    if (possiblyFileSize <= _SaneFileSizeMaximum)
                    {
                        // There's an extremely high probability this is an already compressed file.
                        // Just ship it on through.
                        return data;
                    }
                }
            }


            var _mdl = new Mdl(XivCache.GameInfo.GameDirectory);
            var _tex = new Tex(XivCache.GameInfo.GameDirectory);
            var _dat = new Dat(XivCache.GameInfo.GameDirectory);

            // At this point, there's only 2 file types that fall through.
            // Uncompressed .MDL data, and binary files. (Type 2)

            // So just confirm if it's an MDL.
            var possiblySignatureA = BitConverter.ToUInt16(data, 0);
            var possiblySignatureB = BitConverter.ToUInt16(data, 0);

            // Signatures for MDL version 5 and 6.
            if(possiblySignatureB == 256 && (possiblySignatureA == 5 || possiblySignatureA == 6))
            {
                return await _mdl.CompressMdlFile(data);
            }

            // This some kind of binary data to get type 2 compressed.
            return await _dat.CompressType2Data(data);
        }


        const ushort BMPMagic = 0x424D;
        const uint DDSMagic = 0x20534444;
        const ulong PNGMagic = 0x89504E470D0A1A0A;
        const string FBXMagic = "Kaydara FBX Binary  ";
        const string SQLiteMagic = "SQLite format 3\0";
    }
}
