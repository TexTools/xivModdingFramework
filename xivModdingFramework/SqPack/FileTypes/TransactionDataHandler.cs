using SharpDX.Direct2D1.Effects;
using SharpDX.Direct2D1;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Mods;
using static xivModdingFramework.SqPack.FileTypes.TransactionDataHandler;
using System.Globalization;
using xivModdingFramework.Helpers;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.FileTypes;
using System.ComponentModel.Design;
using System.Linq;
using System.Diagnostics;
using xivModdingFramework.Mods.FileTypes.PMP;
using xivModdingFramework.Mods.Interfaces;

namespace xivModdingFramework.SqPack.FileTypes
{
    public enum EFileStorageType
    {
        ReadOnly,

        // Stored in SqPack format, in one large blob file, as in TTMP files.
        CompressedBlob,

        // Stored in SqPack format, in individual files.
        CompressedIndividual,
        
        // Stored in uncompressed/un-SqPacked format, in one large blob file.
        UncompressedBlob,

        // Stored in uncompressed format, in individual files, as in Penumbra/PMP files.
        UncompressedIndividual,
    }

    public struct FileStorageInformation
    {
        public EFileStorageType StorageType;
        public long RealOffset;
        public string RealPath;
        public int FileSize;

        public bool IsCompressed
        {
            get {
                return StorageType == EFileStorageType.CompressedBlob || StorageType == EFileStorageType.CompressedIndividual;
            }
        }
        public bool IsBlob
        {
            get
            {
                return StorageType == EFileStorageType.UncompressedBlob || StorageType == EFileStorageType.CompressedBlob;
            }
        }
        public bool LiveGameFile
        {
            get
            {
                if (RealPath == null) return false;
                return RealPath.StartsWith(XivCache.GameInfo.GameDirectory.FullName) && RealPath.EndsWith(Dat.DatExtension) && StorageType == EFileStorageType.CompressedBlob;
            }
        }

    }

    /// <summary>
    /// Class that handles wrapping access to the core DAT files, during transactionary states.
    /// In particular, this maps DataFile+8xDataOffsets to transactionary data stores, and implements access to them.
    /// </summary>
    public class TransactionDataHandler : IDisposable
    {
        internal readonly EFileStorageType DefaultType;
        internal readonly string DefaultPathRoot;
        internal readonly string DefaultBlobName;

        private Dictionary<XivDataFile, Dictionary<long, FileStorageInformation>> OffsetMapping = new Dictionary<XivDataFile, Dictionary<long, FileStorageInformation>>();

        public bool IsTempOffset(XivDataFile df, long offset)
        {
            if(OffsetMapping.ContainsKey(df) && OffsetMapping[df].ContainsKey(offset))
            {
                return true;
            }
            return false;
        }

        private bool disposedValue;

        public TransactionDataHandler(EFileStorageType defaultType = EFileStorageType.ReadOnly, string defaultPath = null) {
            foreach (XivDataFile df in Enum.GetValues(typeof(XivDataFile))) {
                OffsetMapping.Add(df, new Dictionary<long, FileStorageInformation>());
            }


            DefaultType = defaultType;
            DefaultPathRoot = defaultPath;
            DefaultBlobName = Guid.NewGuid().ToString();

            if (DefaultType != EFileStorageType.ReadOnly)
            {
                // Readonly Handlers do not technically need to be IDsposable.Disposed, as they never create a file storage.
                if (DefaultPathRoot == null)
                {
                    // Create a temp folder if we weren't supplied one.
                    var tempFolder = IOUtil.GetFrameworkTempSubfolder("TX_");
                    DefaultPathRoot = tempFolder;
                }
                Directory.CreateDirectory(DefaultPathRoot);
            }
        }

        /// <summary>
        /// Retrieves a given file from the data store, based on 8x Data Offset(With Dat# Embed) and data file the file would exist in.
        /// Decompresses the file from the file store if necessary.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="offset8x"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        public async Task<byte[]> GetUncompressedFile(XivDataFile dataFile, long offset8x)
        {
            if (!OffsetMapping.ContainsKey(dataFile))
            {
                throw new FileNotFoundException("Invalid Data File: "  + dataFile.ToString());
            }

            FileStorageInformation info;
            if (OffsetMapping[dataFile].ContainsKey(offset8x))
            {
                // Load info from mapping
                info = OffsetMapping[dataFile][offset8x];
            }
            else
            {
                // Shouldn't normally get here, but... Return empty data I guess?
                if(offset8x == 0)
                {
                    throw new InvalidDataException("Cannot read data from base game files with invalid offset.");
                }

                // Create standard Game DAT file request info.
                info = IOUtil.MakeGameStorageInfo(dataFile, offset8x);
            }

            return await GetUncompressedFile(info);
        }

        public static async Task<byte[]> GetUncompressedFile(FileStorageInformation info)
        {
            using (var fs = File.OpenRead(info.RealPath))
            {
                using (var br = new BinaryReader(fs))
                {
                    if (!info.IsCompressed)
                    {
                        // This is the simplest one.  Just read the file back.
                        br.BaseStream.Seek(info.RealOffset, SeekOrigin.Begin);
                        if (info.IsBlob)
                        {
                            if(info.FileSize <= 0)
                            {
                                throw new FileNotFoundException("Cannot read back uncompressed file with real file size 0.");
                            }

                            return br.ReadBytes(info.FileSize);
                        } else
                        {
                            return br.ReadAllBytes();
                        }
                    } else
                    {
                        // Open file, navigate to the offset(or start of file), read and decompress SQPack file.
                        br.BaseStream.Seek(info.RealOffset, SeekOrigin.Begin);
                        return await Dat.ReadSqPackFile(br, info.RealOffset);
                    }
                }
            }
        }
        internal async Task<BinaryReader> GetUncompressedFileStream(XivDataFile dataFile, long offset8x)
        {
            if (!OffsetMapping.ContainsKey(dataFile))
            {
                throw new FileNotFoundException("Invalid Data File: " + dataFile.ToString());
            }

            if (offset8x < 0)
            {
                throw new InvalidDataException("Cannot retrieve uncompressed file at Invalid Offset");
            }

            FileStorageInformation info;
            if (OffsetMapping[dataFile].ContainsKey(offset8x))
            {
                // Load info from mapping
                info = OffsetMapping[dataFile][offset8x];
            }
            else
            {
                // Create standard Game DAT file request info.
                info = IOUtil.MakeGameStorageInfo(dataFile, offset8x);
            }

            return await GetUncompressedFileStream(info);
        }
        public static async Task<BinaryReader> GetUncompressedFileStream(FileStorageInformation info)
        {
            if (!info.IsCompressed)
            {
                // We can return the raw file reader and save memory here, yay!
                var br = new BinaryReader(File.OpenRead(info.RealPath));
                br.BaseStream.Seek(info.RealOffset, SeekOrigin.Begin);
                return br;
            }
            else
            {
                // This is a bit clunky.  We have to decompress the file.
                byte[] data;
                using (var br = new BinaryReader(File.OpenRead(info.RealPath)))
                {
                    // Open file, navigate to the offset(or start of file), read and decompress SQPack file.
                    br.BaseStream.Seek(info.RealOffset, SeekOrigin.Begin);
                    data = await Dat.ReadSqPackFile(br, info.RealOffset);
                }
                return new BinaryReader(new MemoryStream(data));
            }
        }

        public FileStorageInformation GetStorageInfo(XivDataFile dataFile, long offset8x)
        {
            FileStorageInformation info;
            if (OffsetMapping[dataFile].ContainsKey(offset8x))
            {
                // Load info from mapping
                info = OffsetMapping[dataFile][offset8x];
            }
            else
            {
                // Create standard Game DAT file request info.
                info = IOUtil.MakeGameStorageInfo(dataFile, offset8x);
            }
            return info;
        }

        /// <summary>
        /// Retrieves a given SqPacked file from the data store, based on 8x Data Offset(With Dat# Embed) and data file the file would exist in.
        /// Compresses the file from the file store if necessary.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="offset8x"></param>
        /// <param name="forceType2"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        public async Task<byte[]> GetCompressedFile(XivDataFile dataFile, long offset8x, bool forceType2 = false)
        {
            if (!OffsetMapping.ContainsKey(dataFile))
            {
                throw new FileNotFoundException("Invalid Data File: " + dataFile.ToString());
            }

            if(offset8x < 0)
            {
                throw new InvalidDataException("Cannot retrieve compressed file at Invalid Offset");
            }

            FileStorageInformation info;
            if (OffsetMapping[dataFile].ContainsKey(offset8x))
            {
                // Load info from mapping
                info = OffsetMapping[dataFile][offset8x];
            }
            else
            {
                // Create standard Game DAT file request info.
                info = IOUtil.MakeGameStorageInfo(dataFile, offset8x);
            }

            return await GetCompressedFile(info, forceType2);
        }
        public static async Task<byte[]> GetCompressedFile(FileStorageInformation info, bool forceType2 = false)
        {
            using (var fs = File.OpenRead(info.RealPath))
            {
                using (var br = new BinaryReader(fs))
                {

                    br.BaseStream.Seek(info.RealOffset, SeekOrigin.Begin);
                    if (info.IsCompressed)
                    {
                        if (info.IsBlob && info.FileSize == 0)
                        {
                            // If we don't have the compressed file size already, check it.
                            // (This is mostly the case when reading game DATs, for perf reasons)
                            if (info.FileSize == 0)
                            {
                                info.FileSize = Dat.GetCompressedFileSize(br, info.RealOffset);
                            }

                            br.BaseStream.Seek(info.RealOffset, SeekOrigin.Begin);
                        }
                        else if (!info.IsBlob)
                        {
                            // We can be a cheatyface here regardless of if we have the size or not and just dump the entire file back.
                            return br.ReadAllBytes();
                        }
                    }


                    byte[] data;
                    if (info.IsBlob)
                    {
                        data = br.ReadBytes(info.FileSize);
                    } else
                    {
                        data = br.ReadAllBytes();
                    }

                    if (!info.IsCompressed)
                    {
                        // This is a bit clunky.  We have to ship this to the smart compressor.
                        data = await SmartImport.CreateCompressedFile(data, forceType2);
                    }
                    return data;
                }
            }
        }
        public async Task<BinaryReader> GetCompressedFileStream(XivDataFile dataFile, long offset8x, bool forceType2 = false)
        {
            if (!OffsetMapping.ContainsKey(dataFile))
            {
                throw new FileNotFoundException("Invalid Data File: " + dataFile.ToString());
            }

            FileStorageInformation info;
            if (OffsetMapping[dataFile].ContainsKey(offset8x))
            {
                // Load info from mapping
                info = OffsetMapping[dataFile][offset8x];
            }
            else
            {
                // Create standard Game DAT file request info.
                info = IOUtil.MakeGameStorageInfo(dataFile, offset8x);
            }

            return await GetCompressedFileStream(info, forceType2);
        }
        public static async Task<BinaryReader> GetCompressedFileStream(FileStorageInformation info, bool forceType2 = false)
        {
            if (info.IsCompressed)
            {
                // We can return the raw file reader and save memory here, yay!
                var br = new BinaryReader(File.OpenRead(info.RealPath));
                br.BaseStream.Seek(info.RealOffset, SeekOrigin.Begin);
                return br;
            }
            else
            {
                // This is a bit clunky.  We have to compress the file.
                byte[] data;
                using (var br = new BinaryReader(File.OpenRead(info.RealPath)))
                {
                    br.BaseStream.Seek(info.RealOffset, SeekOrigin.Begin);
                    if (info.IsBlob)
                    {
                        data = br.ReadBytes(info.FileSize);
                    } else
                    {
                        data = br.ReadAllBytes();
                    }
                }
                data = await SmartImport.CreateCompressedFile(data, forceType2);
                return new BinaryReader(new MemoryStream(data));
            }
        }

        public async Task<int> GetCompressedFileSize(XivDataFile dataFile, long offset8x, bool forceType2 = false)
        {
            if (!OffsetMapping.ContainsKey(dataFile))
            {
                throw new FileNotFoundException("Invalid Data File: " + dataFile.ToString());
            }

            FileStorageInformation info;
            if (OffsetMapping[dataFile].ContainsKey(offset8x))
            {
                // Load info from mapping
                info = OffsetMapping[dataFile][offset8x];
            }
            else
            {
                // Create standard Game DAT file request info.
                info = IOUtil.MakeGameStorageInfo(dataFile, offset8x);
            }

            return await GetCompressedFileSize(info, forceType2);
        }
        public static async Task<int> GetCompressedFileSize(FileStorageInformation info, bool forceType2 = false)
        {
            if (info.IsCompressed)
            {
                if (info.IsBlob)
                {
                    return info.FileSize;
                }
                else
                {
                    if(info.FileSize != 0)
                    {
                        return info.FileSize;
                    }
                    return (int)new FileInfo(info.RealPath).Length;
                }
            }
            else
            {
                var data = await GetCompressedFile(info, forceType2);
                return data.Length;
            }
        }

        /// <summary>
        /// Writes a given file to the default data store, keyed to the datafile/offset pairing.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="offset8x"></param>
        /// <param name="data"></param>
        /// <param name="preCompressed"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        public async Task WriteFile(XivDataFile dataFile, long offset8x, byte[] data, bool preCompressed = false)
        {
            if(DefaultType == EFileStorageType.ReadOnly)
            {
                throw new InvalidOperationException("Cannot write to readonly data manager.");
            }

            var info = new FileStorageInformation();
            if(DefaultPathRoot == XivCache.GameInfo.GameDirectory.FullName && DefaultType == EFileStorageType.CompressedBlob)
            {
                // Special case, this is requesting a write to the live game files.
                // Do we want to allow this here?
                throw new NotImplementedException("Writing to live game DATs using wrapped handle is not implemented.");
            }

            string fileName;
            if(DefaultType == EFileStorageType.UncompressedBlob || DefaultType == EFileStorageType.CompressedBlob)
            {
                fileName = DefaultBlobName;
            }
            else
            {
                fileName = Guid.NewGuid().ToString();
            }
            info.RealPath = Path.Combine(DefaultPathRoot, fileName);
            info.StorageType = DefaultType;
            info.FileSize = data.Length;
            info.RealOffset = -1;

            await WriteFile(info, dataFile, offset8x, data, preCompressed);
        }

        /// <summary>
        /// Writes a given file to the data store, keyed to the datafile/offset pairing.
        /// Requires explicit storage information settings.
        /// </summary>
        /// <param name="storageInfo"></param>
        /// <param name="dataFile"></param>
        /// <param name="offset8x"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public async Task WriteFile(FileStorageInformation storageInfo, XivDataFile dataFile, long offset8x, byte[] data, bool preCompressed = false)
        {
            if (DefaultType == EFileStorageType.ReadOnly)
            {
                throw new InvalidOperationException("Cannot write to readonly data manager.");
            }

            if (!OffsetMapping.ContainsKey(dataFile))
            {
                throw new FileNotFoundException("Invalid Data File.");
            }

            if (storageInfo.IsCompressed && preCompressed == false)
            {
                data = await SmartImport.CreateCompressedFile(data);
            }
            else if ((!storageInfo.IsCompressed) && preCompressed == true)
            {
                data = await Dat.ReadSqPackFile(data);
            }

            if (storageInfo.IsBlob && storageInfo.RealOffset < 0)
            {
                // Negative offset means write to the end of the blob.
                var fSize = new FileInfo(storageInfo.RealPath).Length;
                storageInfo.RealOffset = fSize;
            } else if (!storageInfo.IsBlob)
            {
                // Individual file formats don't use offsets.
                storageInfo.RealOffset = 0;
            }

            storageInfo.FileSize = data.Length;

            using (var fs = File.Open(storageInfo.RealPath, FileMode.OpenOrCreate, FileAccess.Write))
            {
                using (var bw = new BinaryWriter(fs))
                {
                    bw.BaseStream.Seek(storageInfo.RealOffset, SeekOrigin.Begin);
                    bw.Write(data);
                }
            }

            if (OffsetMapping[dataFile].ContainsKey(offset8x))
            {
                OffsetMapping[dataFile].Remove(offset8x);
            }
            OffsetMapping[dataFile].Add(offset8x, storageInfo);
        }

        /// <summary>
        /// Retrieves the uncompressed/un-SqPacked filesize of the given file.
        /// If the file is not already stored in uncompressed format, it will scan the SqPack file to math out the real size.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="offset8x"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        public int GetUncompressedSize(XivDataFile dataFile, long offset8x)
        {
            if (!OffsetMapping.ContainsKey(dataFile))
            {
                throw new FileNotFoundException("Invalid Data File: " + dataFile.ToString());
            }

            FileStorageInformation info;
            if (OffsetMapping[dataFile].ContainsKey(offset8x))
            {
                // Load info from mapping
                info = OffsetMapping[dataFile][offset8x];
            }
            else
            {
                // Create standard Game DAT file request info.
                info = IOUtil.MakeGameStorageInfo(dataFile, offset8x);
            }
            return GetUncompressedFileSize(info);
        }

        public static int GetUncompressedFileSize(FileStorageInformation info)
        {
            if (!info.IsCompressed)
            {
                if (info.IsBlob)
                {
                    return info.FileSize;
                } else
                {
                    if(info.FileSize != 0)
                    {
                        return info.FileSize;
                    }
                    return (int)new FileInfo(info.RealPath).Length;
                }

            } else
            {
                // Compressed SqPack files have information on their uncompressed size, so we can just read that and cache it.
                using (var fs = File.OpenRead(info.RealPath))
                {
                    using (var br = new BinaryReader(fs))
                    {
                        br.BaseStream.Seek(info.RealOffset + 12, SeekOrigin.Begin);
                        return br.ReadInt32();
                    }
                }
            }

        }


        /// <summary>
        /// Adds a file storage reference without writing the associated data.
        /// </summary>
        /// <param name="storageInfo"></param>
        /// <param name="dataFile"></param>
        /// <param name="offset8x"></param>
        internal void UNSAFE_AddFileInfo(FileStorageInformation storageInfo, XivDataFile dataFile, long offset8x)
        {
            OffsetMapping[dataFile].Add(offset8x , storageInfo);
        }

        /// <summary>
        /// Writes all the files in this data store to the given transaction target.
        /// If the target is the main game files, store the new offsets we write the data to.
        /// 
        /// The [openSlots] arg is only used when writing to base game files.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="targetPath"></param>
        /// <returns></returns>
        internal async Task<Dictionary<string, (long RealOffset, long TempOffset)>> WriteAllToTarget(ModTransactionSettings settings, ModTransaction tx, Dictionary<XivDataFile, Dictionary<long, uint>> openSlots = null)
        {
            var offsets = new Dictionary<string, (long RealOffset, long TempOffset)>();
            if(settings.Target == ETransactionTarget.GameFiles)
            {
                return await WriteToGameFiles(tx, settings, openSlots);
            } 
            else if(settings.Target == ETransactionTarget.FolderTree)
            {
                return await WriteToLuminaFolders(tx, settings);
            }
            else if (settings.Target == ETransactionTarget.TTMP)
            {
                return await WriteToTTMP(tx, settings);
            }
            else if (settings.Target == ETransactionTarget.PMP)
            {
                return await WriteToPMP(tx, settings);
            }
            else if (settings.Target == ETransactionTarget.PenumbraModFolder)
            {
                return await WriteToPenumbraModFolder(tx, settings);
            }
            throw new InvalidDataException("Invalid Transaction Target");
        }

        private async Task<Dictionary<string, (long RealOffset, long TempOffset)>> WriteToGameFiles(ModTransaction tx, ModTransactionSettings settings, Dictionary<XivDataFile, Dictionary<long, uint>> openSlots)
        {
            if(settings.TargetPath != XivCache.GameInfo.GameDirectory.FullName)
            {
                throw new NotImplementedException("Writing to other game installs has not been implemented. :(");
            }

            var offsets = new Dictionary<string, (long RealOffset, long TempOffset)>();

            // TODO - Could paralellize this by datafile, but pretty rare that would get any gains.
            foreach (var dkv in OffsetMapping)
            {
                var df = dkv.Key;
                var files = dkv.Value;
                if (files.Count == 0) continue;
                var index = await tx.GetIndexFile(df);

                foreach (var fkv in files)
                {
                    var tempOffset = fkv.Key;
                    var file = fkv.Value;

                    // Get the live paths this file is being used in...
                    var paths = tx.GetFilePathsFromTempOffset(df, tempOffset);
                    foreach (var path in paths)
                    {
                        if (tx.IsPrepFile(path))
                        {
                            // Prep files don't get written to final product.
                            continue;
                        }

                        // Depending on store this may already be compressed.
                        // Or it may not.
                        var forceType2 = path.EndsWith(".atex");
                        var data = await GetCompressedFile(file, forceType2);

                        // We now have everything we need for DAT writing.
                        var realOffset = (await Dat.Unsafe_WriteToDat(data, df, openSlots[df]));

                        offsets.Add(path, (realOffset, tempOffset));
                    }
                }
            }


            return offsets;

        }

        private async Task<Dictionary<string, (long RealOffset, long TempOffset)>> WriteToLuminaFolders(ModTransaction tx, ModTransactionSettings settings)
        {
            foreach (var dkv in OffsetMapping)
            {
                var df = dkv.Key;
                var files = dkv.Value;
                if (files.Count == 0) continue;
                var index = await tx.GetIndexFile(df);

                foreach (var fkv in files)
                {
                    var tempOffset = fkv.Key;
                    var file = fkv.Value;

                    // Get the live paths this file is being used in...
                    var paths = tx.GetFilePathsFromTempOffset(df, tempOffset);
                    foreach (var path in paths)
                    {
                        if (tx.IsPrepFile(path))
                        {
                            // Prep files don't get written to final product.
                            continue;
                        }

                        var destinationPath = Path.Combine(settings.TargetPath, path);
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

                        // Write the file to disk.
                        File.WriteAllBytes(destinationPath, await GetUncompressedFile(df, tempOffset));
                    }
                }
            }

            // Don't have real offsets to update to, since we don't write to game files.
            return null;
        }

        private async Task<Dictionary<string, (long RealOffset, long TempOffset)>> WriteToTTMP(ModTransaction tx, ModTransactionSettings settings)
        {
            if (!settings.TargetPath.EndsWith(".ttmp2"))
            {
                // Directory instead of file path.
                var fileName = "transaction.ttmp2";
                settings.TargetPath = Path.Combine(settings.TargetPath, fileName);
            }

            var dir = Path.GetDirectoryName(settings.TargetPath);
            Directory.CreateDirectory(dir);

            var simplePack = new SimpleModPackData();

            // Use the Transaction's current modpack settings if it has any set.
            Version.TryParse(tx.ModPack == null ? "1.0" : tx.ModPack.Value.Version, out var ver);

            simplePack.Name = tx.ModPack == null ? Path.GetFileNameWithoutExtension(settings.TargetPath) : tx.ModPack.Value.Name;
            simplePack.Author = tx.ModPack == null ? "Unknown" : tx.ModPack.Value.Author;
            simplePack.Version = ver == null ? new Version("1.0") : ver;
            simplePack.SimpleModDataList = new List<SimpleModData>();

            var mList = await tx.GetModList();

            var tempFile = IOUtil.GetFrameworkTempFile();

            using (var bw = new BinaryWriter(File.Open(tempFile, FileMode.Create)))
            {
                foreach (var dkv in OffsetMapping)
                {
                    var df = dkv.Key;
                    var files = dkv.Value;
                    if (files.Count == 0) continue;
                    var index = await tx.GetIndexFile(df);

                    foreach (var fkv in files)
                    {
                        var tempOffset = fkv.Key;
                        var file = fkv.Value;

                        // Get the live paths this file is being used in...
                        var paths = tx.GetFilePathsFromTempOffset(df, tempOffset);
                        foreach (var path in paths)
                        {
                            if (tx.IsPrepFile(path))
                            {
                                // Prep files don't get written to final product.
                                continue;
                            }

                            if (IOUtil.IsMetaInternalFile(path))
                            {
                                // These don't go out in this format.
                                continue;
                            }

                            // Bind the offsets for paths/mod objects for the TTMP writer.
                            var mod = mList.GetMod(path);
                            var md = new SimpleModData();
                            md.FullPath = path;
                            md.DatFile = df.ToString();
                            md.ModOffset = tempOffset;
                            md.Category = mod != null ? mod.Value.ItemCategory : "Unknown";
                            md.Name = mod != null ? mod.Value.ItemName : "Unknown";
                            simplePack.SimpleModDataList.Add(md);
                        }
                    }
                }
            }

            // Create the actual TTMP.
            await TTMP.CreateSimpleModPack(simplePack, settings.TargetPath, null, false, tx);

            // Don't have real offsets to update to, since we don't write to game files.
            return null;
        }

        private async Task<Dictionary<string, (long RealOffset, long TempOffset)>> WriteToPMP(ModTransaction tx, ModTransactionSettings settings)
        {
            if (!settings.TargetPath.EndsWith(".pmp"))
            {
                // Directory instead of file path.
                var fileName = "transaction.pmp";
                settings.TargetPath = Path.Combine(settings.TargetPath, fileName);
            }

            var dir = Path.GetDirectoryName(settings.TargetPath);
            Directory.CreateDirectory(dir);

            var dict = await GetFinalWriteList(tx);

            var mpack = new BaseModpackData()
            {
                Author = "Unknown",
                Name = Path.GetFileNameWithoutExtension(settings.TargetPath),
                Version = new Version("1.0"),
                Description = "A Penumbra Modpack created from a TexTools transaction."
            };

            await PMP.CreateSimplePmp(settings.TargetPath, mpack, dict, null, true);


            // Don't have real offsets to update to, since we don't write to game files.
            return null;
        }

        private async Task<Dictionary<string, (long RealOffset, long TempOffset)>> WriteToPenumbraModFolder(ModTransaction tx, ModTransactionSettings settings)
        {

            var dir = settings.TargetPath;
            Directory.CreateDirectory(dir);

            var di = new DirectoryInfo(dir);

            var dict = await GetFinalWriteList(tx);

            var pathName = di.Name;
            var mpack = new BaseModpackData()
            {
                Author = "Unknown",
                Name = di.Name,
                Version = new Version("1.0"),
                Description = "A Penumbra Modpack created from a TexTools transaction."
            };

            await PMP.CreateSimplePmp(settings.TargetPath, mpack, dict, null, false);

            await PenumbraAPI.ReloadMod(di.Name);
            if (XivCache.FrameworkSettings.PenumbraRedrawMode == FrameworkSettings.EPenumbraRedrawMode.RedrawAll)
                await PenumbraAPI.Redraw();
            else if (XivCache.FrameworkSettings.PenumbraRedrawMode == FrameworkSettings.EPenumbraRedrawMode.RedrawSelf)
                await PenumbraAPI.RedrawSelf();

            // Don't have real offsets to update to, since we don't write to game files.
            return null;
        }
        protected async virtual Task<Dictionary<string, FileStorageInformation>> GetFinalWriteList(ModTransaction tx)
        {
            var dict = new Dictionary<string, FileStorageInformation>();

            foreach (var dkv in OffsetMapping)
            {
                var df = dkv.Key;
                var files = dkv.Value;
                if (files.Count == 0) continue;
                var index = await tx.GetIndexFile(df);

                foreach (var fkv in files)
                {
                    var tempOffset = fkv.Key;
                    var file = fkv.Value;

                    // Get the live paths this file is being used in...
                    var paths = tx.GetFilePathsFromTempOffset(df, tempOffset);
                    foreach (var path in paths)
                    {
                        if (!tx.IsModifiedFile(path))
                        {
                            // If the file doesn't exist in the original states array,
                            // it's either a prep file or was reset at some point.
                        }

                        if (tx.IsPrepFile(path))
                        {
                            // Prep files don't get written to final product.
                            continue;
                        }

                        if (IOUtil.IsMetaInternalFile(path))
                        {
                            // These don't go out in this format.
                            continue;
                        }

                        if (file.IsCompressed)
                        {
                            Trace.WriteLine("DEBUG");
                        }

                        dict.Add(path, file);
                    }
                }
            }
            return dict;
        }


        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                }

                try
                {
                    IOUtil.DeleteTempDirectory(DefaultPathRoot);
                }
                catch(Exception ex)
                {
                    // Well, fuck.  Can't do anything about that, other than make sure we don't throw in a finalizer.
                    Trace.WriteLine(ex);
                }
                disposedValue = true;
            }
        }
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
