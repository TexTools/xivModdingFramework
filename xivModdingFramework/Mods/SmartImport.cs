﻿using HelixToolkit.SharpDX.Core.Helper;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.Processing;
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
using xivModdingFramework.Models.Helpers;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.Enums;
using xivModdingFramework.Textures.FileTypes;
using xivModdingFramework.Variants.FileTypes;

namespace xivModdingFramework.Mods
{

    public class SmartImportOptions : ICloneable
    {
        public XivTexFormat TextureFormat = XivTexFormat.INVALID;
        public int MaxImageSize = 0;
        public ModelImportOptions ModelOptions = null;
        public bool CompressFiles = false;

        public object Clone()
        {
            var op = (SmartImportOptions) MemberwiseClone();
            if (ModelOptions != null)
            {
                var mops = (ModelImportOptions)ModelOptions.Clone();
                op.ModelOptions = mops;
            }
            return op;
        }
    }

    /// <summary>
    /// Static class that handles importing arbitrary files, and dynamically calling the appropriate functions, 
    /// to convert those files into the FFXIV format files, and/or SQPack them as needed, before ultimately
    /// writing them to a transaction.
    /// </summary>
    public static class SmartImport
    {


        /// <summary>
        /// Imports an arbitrary subset of files to the given transaction target.
        /// Syntactic wrapper for opening/closing a transaction around a batch import.
        /// </summary>
        /// <param name="files"></param>
        /// <param name="target"></param>
        /// <param name="targetPath"></param>
        /// <param name="modpackName"></param>
        /// <param name="modpackAuthor"></param>
        /// <returns></returns>
        public static async Task ImportBatch(List<(string ExternalPath, string InternalPath)> files, ModTransactionSettings settings, string sourceApplication = "Unknown", string modpackName = "Unknown Batch Import", string modpackAuthor = "Unknown")
        {
            var modPack = new ModPack(null)
            {
                Name = modpackName,
                Author = modpackAuthor,
                Version = "1.0"
            };

            var tx = await ModTransaction.BeginTransaction(true, modPack, settings);
            try
            {
                await ImportBatch(files, sourceApplication, tx);
                await ModTransaction.CommitTransaction(tx);
            }
            catch
            {
                await ModTransaction.CancelTransaction(tx);
            }
        }

        /// <summary>
        /// Imports an arbitrary subset of files to the given transaction, or to the base game files, if no transaction is provided.
        /// </summary>
        /// <param name=""></param>
        /// <param name=""></param>
        /// <param name="sourceApplication"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        public static async Task ImportBatch(List<(string ExternalPath, string InternalPath)> files, string sourceApplication = "Unknown", ModTransaction tx = null)
        {

            var modPack = new ModPack(null)
            {
                Name = "Unknown Batch Import",
                Author = "Unknown",
                Version = "1.0",
            };

            var boiler = await TxBoiler.BeginWrite(tx, true, modPack);
            tx = boiler.Transaction;
            try
            {
                foreach(var file in files)
                {
                    await Import(file.ExternalPath, file.InternalPath, sourceApplication, tx);
                }

                await boiler.Commit();
            }
            catch
            {
                await boiler.Catch();
                throw;
            }
        }

        /// <summary>
        /// Handles importing an arbitrary external file to the given transaction, or to the base game files, if no transaction is provided.
        /// </summary>
        /// <param name="externalPath"></param>
        /// <param name="internalPath"></param>
        /// <param name="sourceApplication">Application name to use when writing to the modlist.</param>
        /// <param name="modPackBinding">Modpack this mod entry should be attached to.</param>
        /// <param name="cachedIndexFile"></param>
        /// <param name="cachedModList"></param>
        /// <returns></returns>
        public static async Task Import(string externalPath, string internalPath, string sourceApplication = "Unknown", ModTransaction tx = null, SmartImportOptions options = null)
        {
            if (!File.Exists(externalPath))
            {
                throw new FileNotFoundException("Requested import file does not exist or was inaccessible.");
            }

            await Task.Run(async () =>
            {

                // Establish an item to associate the mod import with, if one exists.
                var root = await XivCache.GetFirstRoot(internalPath);
                IItem item = null;
                if (root != null) {
                    item = root.GetFirstItem();
                }

                if (options != null && !options.CompressFiles)
                {
                    var data = await CreateCompressedFile(externalPath, internalPath, tx, options);
                    await Dat.WriteModFile(data, internalPath, sourceApplication, item, tx, true);
                }
                else
                {
                    var data = await CreateUncompressedFile(externalPath, internalPath, tx, options);
                    await Dat.WriteModFile(data, internalPath, sourceApplication, item, tx, false);
                }
                XivCache.QueueDependencyUpdate(internalPath);
            });
        }


        /// <summary>
        /// Handles full path creation of compressed SQPack-Ready data from an external file source of arbitrary type.
        /// </summary>
        /// <param name="externalPath"></param>
        /// <param name="internalPath"></param>
        /// <param name="tx">Active transaction.  Used down in the guts of texture generation to match DDS type to the current modded file.</param>
        /// <returns></returns>
        public static async Task<byte[]> CreateCompressedFile(string externalPath, string internalPath, ModTransaction tx = null, SmartImportOptions options = null)
        {
            // ATex files are just .tex files but forced into a type 2 wrapper.
            var forceType2 = internalPath.EndsWith(".atex");

            return await CreateCompressedFile(await CreateUncompressedFile(externalPath, internalPath, tx, options), forceType2);
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
        public static async Task<byte[]> CreateUncompressedFile(string externalPath, string internalPath, ModTransaction tx = null, SmartImportOptions options = null)
        {
            if (options == null)
            {
                options = new SmartImportOptions();
            }

            ulong magic;
            uint magic32;
            uint magic16;
            string magic20b, magic16b;

            // Pull the magic info from the file.
            using (var f = File.OpenRead(externalPath))
            {
                using (var br = new BinaryReader(f))
                {
                    var header = br.ReadBytes(20);
                    magic = BitConverter.ToUInt64(header, 0);
                    magic32 = BitConverter.ToUInt32(header, 0);
                    magic16 = BitConverter.ToUInt16(header, 0);
                    magic20b = ASCIIEncoding.ASCII.GetString(header, 0, 20);
                    magic16b = ASCIIEncoding.ASCII.GetString(header, 0, 16);
                }
            }

            var asAscii = ASCIIEncoding.ASCII.GetString(BitConverter.GetBytes(magic));
            var pngMagicAsAscii = ASCIIEncoding.ASCII.GetString(BitConverter.GetBytes(PNGMagic));


            var modelExtentions = Mdl.GetAvailableExporters().Select(x => "." + x);
            var imageExtensions = new List<string>() { ".tga", ".png", ".bmp", ".dds" };


            byte[] result = null;
            // Targa is horrible to detect by bytes, so only allow it for file endings.
            if(magic16 == BMPMagic || magic == PNGMagic || magic32 == DDSMagic || imageExtensions.Any(x => externalPath.ToLower().EndsWith(x)))
            {
                var wasDds = true;
                var ddsPath = externalPath;
                if (magic32 != DDSMagic)
                {
                    wasDds = false;
                    if (options.MaxImageSize > 0)
                    {
                        // We have to load and check if the image is over-size first.
                        using (var img = Image.Load(externalPath))
                        {
                            var w = img.Width;
                            var h = img.Height;
                            var oversize = false;

                            while (w > options.MaxImageSize || h > options.MaxImageSize)
                            {
                                oversize = true;
                                w /= 2;
                                h /= 2;
                            }

                            if (oversize)
                            {
                                var resizeOptions = new ResizeOptions
                                {
                                    Size = new Size(w, h),
                                    PremultiplyAlpha = false,
                                    Mode = ResizeMode.Stretch,
                                };
                                img.Mutate(x => x.Resize(resizeOptions));

                                var tempPath = IOUtil.GetFrameworkTempFile() + ".tga";
                                externalPath = tempPath;

                                img.Save(tempPath, new TgaEncoder() { BitsPerPixel = TgaBitsPerPixel.Pixel32, Compression = TgaCompression.None });
                            }
                        }
                    }


                    // Our DDS Converter can't operate on Streams, so...
                    await Task.Run(async () =>
                    {
                        ddsPath = await Tex.ConvertToDDS(externalPath, internalPath, options.TextureFormat, tx);
                    });
                }

                var texData = Tex.DDSToUncompressedTex(ddsPath);
                if(wasDds && options.MaxImageSize > 0)
                {
                    // This part is slightly annoying.
                    var tex = XivTex.FromUncompressedTex(texData);
                    if(tex.Width > options.MaxImageSize || tex.Height > options.MaxImageSize)
                    {
                        // Blergh.
                        var w = tex.Width;
                        var h = tex.Height;

                        while (w > options.MaxImageSize || h > options.MaxImageSize)
                        {
                            w /= 2;
                            h /= 2;
                        }
                        await Tex.ResizeXivTx(tex, w, h);
                        texData = tex.ToUncompressedTex();
                    }
                }


                return texData;
            } else if(magic20b == FBXMagic || magic16b == SQLiteMagic || modelExtentions.Any(x => externalPath.ToLower().EndsWith(x)))
            {
                // Do Model import.
                return await Mdl.FileToUncompressedMdl(externalPath, internalPath, options.ModelOptions, tx);
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
        public static async Task<byte[]> CreateCompressedFile(byte[] data, bool forceType2 = false)
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


            if (forceType2)
            {
                return await Dat.CompressType2Data(data);
            }


            // At this point, there's only 2 file types that fall through.
            // This is either... 
            // - An uncompressed MDL File (Easy to check)
            // - An uncompressed Texture File (Kind of hard to check)
            // - An uncompressed Binary File (Impossible to check)
            // So we go in order.

            // So just confirm if it's an MDL.
            var possiblySignatureA = BitConverter.ToUInt16(data, 0);
            var possiblySignatureB = BitConverter.ToUInt16(data, 2);

            // Signatures for MDL version 5 and 6.
            if(possiblySignatureB == 256 && (possiblySignatureA == 5 || possiblySignatureA == 6))
            {
                return await Mdl.CompressMdlFile(data);
            }

            // Try our best to tell if it's an uncompressed Texture.
            var possiblyFormat = BitConverter.ToInt32(data, 4);
            var possiblyWidth = BitConverter.ToUInt16(data, 8);
            var possiblyHeight = BitConverter.ToUInt16(data, 10);
            var possiblyMipCount = data[12] & 0xF;

            const ushort _SaneMaxImageSize = 16384;

            if (Enum.IsDefined(typeof(XivTexFormat), possiblyFormat)) {
                if((IOUtil.IsPowerOfTwo(possiblyWidth) || possiblyMipCount == 1) && possiblyWidth <= _SaneMaxImageSize)
                {
                    if ((IOUtil.IsPowerOfTwo(possiblyHeight) || possiblyMipCount == 1) && possiblyHeight <= _SaneMaxImageSize)
                    {
                        // There's an extremely high chance this is an uncompressed tex file.
                        return await Tex.CompressTexFile(data);
                    }
                }
            }


            // This some kind of binary data to get type 2 compressed.
            return await Dat.CompressType2Data(data);
        }


        const ushort BMPMagic = 0x424D;
        const uint DDSMagic = 0x20534444;
        const ulong PNGMagic = 0x0A1A0A0D474E5089;
        const string FBXMagic = "Kaydara FBX Binary  ";
        const string SQLiteMagic = "SQLite format 3\0";
    }
}
