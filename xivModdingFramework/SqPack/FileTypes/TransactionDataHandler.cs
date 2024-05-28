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
        public bool LiveGameFile
        {
            get
            {
                return RealPath.StartsWith(XivCache.GameInfo.GameDirectory.FullName) && RealPath.EndsWith(Dat.DatExtension) && StorageType == EFileStorageType.CompressedBlob;
            }
        }


        private int _CompressedFileSize;
        private int _UncompressedFileSize;

        /// <summary>
        /// Retrieves the compressed file size of this file.
        /// NOTE: If the file is stored uncompressed, and has never been compressed, this function will compress the file in order to determine the resultant size.
        /// </summary>
        /// <returns></returns>
        public async Task<int> GetCompressedFileSize()
        {
            if (StorageType == EFileStorageType.CompressedIndividual || StorageType == EFileStorageType.CompressedBlob)
            {
                _CompressedFileSize = FileSize;
            } else if(_CompressedFileSize == 0)
            {
                // Nothing to be done here but just compress the file and see how large it is.
                // We can cache it though at least.
                using (var fs = File.OpenRead(RealPath))
                {
                    using (var br = new BinaryReader(fs))
                    {

                        br.BaseStream.Seek(RealOffset, SeekOrigin.Begin);
                        var data = br.ReadBytes(FileSize);

                        // This is a bit clunky.  We have to ship this to the smart compressor.
                        data = await SmartImport.CreateCompressedFile(data, false);
                        _CompressedFileSize = data.Length;
                    }
                }
            }
            return _CompressedFileSize;
        }

        /// <summary>
        /// Retrieves the uncompressed file size of this file.
        /// </summary>
        /// <returns></returns>
        public int GetUncompressedFileSize()
        {
            if (StorageType == EFileStorageType.UncompressedIndividual || StorageType == EFileStorageType.UncompressedBlob)
            {
                _UncompressedFileSize = FileSize;
            }
            else if (_UncompressedFileSize == 0)
            {
                // Compressed SqPack files have information on their uncompressed size, so we can just read that and cache it.
                using (var fs = File.OpenRead(RealPath))
                {
                    using (var br = new BinaryReader(fs))
                    {
                        br.BaseStream.Seek(RealOffset + 12, SeekOrigin.Begin);
                        _UncompressedFileSize = br.ReadInt32();
                    }
                }
            }
            return _UncompressedFileSize;
        }
    }

    /// <summary>
    /// Class that handles wrapping access to the core DAT files, during transactionary states.
    /// In particular, this maps DataFile+8xDataOffsets to transactionary data stores, and implements access to them.
    /// </summary>
    internal class TransactionDataHandler : IDisposable
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

        internal static FileStorageInformation MakeGameStorageInfo(XivDataFile df, long offset8x)
        {
            // Create standard Game DAT file request info.
            var info = new FileStorageInformation();
            var parts = IOUtil.Offset8xToParts(offset8x);
            info.RealOffset = parts.Offset;
            info.RealPath = Dat.GetDatPath(df, parts.DatNum);
            info.StorageType = EFileStorageType.CompressedBlob;

            // We could check the file size here, but since this is a temporary file handle, and we don't know if we actually need the pointer...
            // Just set it to 0, then code down the line can identify that it needs to check the file size manually if needed.
            info.FileSize = 0;

            return info;
        }

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
                    var tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
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
                info = MakeGameStorageInfo(dataFile, offset8x);
            }

            return await GetUncompressedFile(info);
        }
        internal static async Task<byte[]> GetUncompressedFile(FileStorageInformation info)
        {
            using (var fs = File.OpenRead(info.RealPath))
            {
                using (var br = new BinaryReader(fs))
                {
                    if (info.StorageType == EFileStorageType.UncompressedBlob || info.StorageType == EFileStorageType.UncompressedIndividual)
                    {
                        // This is the simplest one.  Just read the file back.
                        br.BaseStream.Seek(info.RealOffset, SeekOrigin.Begin);
                        return br.ReadBytes(info.FileSize);
                    } else
                    {
                        // Open file, navigate to the offset(or start of file), read and decompress SQPack file.
                        br.BaseStream.Seek(info.RealOffset, SeekOrigin.Begin);
                        return await Dat.GetUncompressedData(br, info.RealOffset);
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
                info = MakeGameStorageInfo(dataFile, offset8x);
            }

            return await GetUncompressedFileStream(info);
        }
        internal static async Task<BinaryReader> GetUncompressedFileStream(FileStorageInformation info)
        {
            if (info.StorageType == EFileStorageType.UncompressedBlob || info.StorageType == EFileStorageType.UncompressedIndividual)
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
                    data = await Dat.GetUncompressedData(br, info.RealOffset);
                }
                return new BinaryReader(new MemoryStream(data));
            }
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
                info = MakeGameStorageInfo(dataFile, offset8x);
            }

            return await GetCompressedFile(info, forceType2);
        }
        internal static async Task<byte[]> GetCompressedFile(FileStorageInformation info, bool forceType2 = false)
        {
            using (var fs = File.OpenRead(info.RealPath))
            {
                using (var br = new BinaryReader(fs))
                {

                    br.BaseStream.Seek(info.RealOffset, SeekOrigin.Begin);
                    if((info.StorageType == EFileStorageType.CompressedBlob) && info.FileSize == 0)
                    {
                        // If we don't have the compressed file size already, check it.
                        // (This is mostly the case when reading game DATs, for perf reasons)
                        if (info.FileSize == 0)
                        {
                            info.FileSize = Dat.GetCompressedFileSize(br, info.RealOffset);
                        }

                        br.BaseStream.Seek(info.RealOffset, SeekOrigin.Begin);
                    } else if(info.StorageType == EFileStorageType.CompressedIndividual)
                    {
                        // We can be a cheatyface here regardless of if we have the size and just dump the entire file back.
                        return br.ReadAllBytes();
                    }


                    var data = br.ReadBytes(info.FileSize);
                    if (info.StorageType == EFileStorageType.UncompressedBlob || info.StorageType == EFileStorageType.UncompressedIndividual)
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
                info = MakeGameStorageInfo(dataFile, offset8x);
            }

            return await GetCompressedFileStream(info, forceType2);
        }
        internal static async Task<BinaryReader> GetCompressedFileStream(FileStorageInformation info, bool forceType2 = false)
        {
            if (info.StorageType == EFileStorageType.CompressedBlob || info.StorageType == EFileStorageType.CompressedIndividual)
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
                    data = br.ReadBytes(info.FileSize);
                }
                data = await SmartImport.CreateCompressedFile(data, forceType2);
                return new BinaryReader(new MemoryStream(data));
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

            if ((storageInfo.StorageType == EFileStorageType.CompressedIndividual || storageInfo.StorageType == EFileStorageType.CompressedBlob) && preCompressed == false)
            {
                data = await SmartImport.CreateCompressedFile(data);
            }
            else if ((storageInfo.StorageType == EFileStorageType.UncompressedIndividual || storageInfo.StorageType == EFileStorageType.UncompressedBlob) && preCompressed == true)
            {
                data = await Dat.GetUncompressedData(data);
            }

            if(storageInfo.StorageType == EFileStorageType.CompressedIndividual || storageInfo.StorageType == EFileStorageType.UncompressedIndividual)
            {
                // Individual file formats don't use offsets.
                storageInfo.RealOffset = 0;
            } else if (storageInfo.RealOffset < 0)
            {
                // Negative offset means write to the end of the blob.
                var fSize = new FileInfo(storageInfo.RealPath).Length;
                storageInfo.RealOffset = fSize;
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
                info = MakeGameStorageInfo(dataFile, offset8x);
            }
            return info.GetUncompressedFileSize();
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
            else if(settings.Target == ETransactionTarget.LuminaFolders)
            {
                return await WriteToLuminaFolders(tx, settings);
            }
            else if (settings.Target == ETransactionTarget.TTMP)
            {
                return await WriteToTTMP(tx, settings);
            }
            if (settings.Target == ETransactionTarget.PMP)
            {
                return await WriteToTTMP(tx, settings);
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

            simplePack.Name = tx.ModPack == null ? "Transaction Modpack" : tx.ModPack.Value.Name;
            simplePack.Author = tx.ModPack == null ? "Unknown" : tx.ModPack.Value.Author;
            simplePack.Version = ver == null ? new Version("1.0") : ver;
            simplePack.SimpleModDataList = new List<SimpleModData>();

            var mList = await tx.GetModList();

            var tempFile = Path.GetTempFileName();

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

                            // Bind the offsets for paths/mod objects for the TTMP writer.
                            var mod = mList.GetMod(path);
                            var md = new SimpleModData();
                            md.FullPath = path;
                            md.DatFile = df.ToString();
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
            throw new NotImplementedException("PMP Export not yet implemented. :(");
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
