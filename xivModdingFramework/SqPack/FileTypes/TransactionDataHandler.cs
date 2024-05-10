﻿using SharpDX.Direct2D1.Effects;
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
    }

    /// <summary>
    /// Class that handles wrapping access to the core DAT files, during transactionary states.
    /// In particular, this maps DataFile+8xDataOffsets to transactionary data stores, and implements access to them.
    /// </summary>
    internal class TransactionDataHandler : IDisposable
    {
        private readonly EFileStorageType DefaultType;
        private readonly string DefaultPathRoot;
        private readonly string DefaultBlobName;

        private Dictionary<XivDataFile, Dictionary<long, FileStorageInformation>> OffsetMapping = new Dictionary<XivDataFile, Dictionary<long, FileStorageInformation>>();
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
                // Create standard Game DAT file request info.
                info = new FileStorageInformation();
                var parts = Dat.Offset8xToParts(offset8x);
                info.RealOffset = parts.Offset;
                info.RealPath = Dat.GetDatPath(dataFile, parts.DatNum);
                info.StorageType = EFileStorageType.CompressedBlob;
            }

            return await GetUncompressedFile(info);
        }
        private async Task<byte[]> GetUncompressedFile(FileStorageInformation info)
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
                        var _dat = new Dat(XivCache.GameInfo.GameDirectory);
                        br.BaseStream.Seek(info.RealOffset, SeekOrigin.Begin);
                        return await _dat.GetUncompressedData(br, info.RealOffset);
                    }
                }
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

            FileStorageInformation info;
            if (OffsetMapping[dataFile].ContainsKey(offset8x))
            {
                // Load info from mapping
                info = OffsetMapping[dataFile][offset8x];
            }
            else
            {
                // Create standard Game DAT file request info.
                info = new FileStorageInformation();
                var parts = Dat.Offset8xToParts(offset8x);
                info.RealOffset = parts.Offset;
                info.RealPath = Dat.GetDatPath(dataFile, parts.DatNum);
                info.StorageType = EFileStorageType.CompressedBlob;
            }

            return await GetCompressedFile(info, forceType2);
        }
        private async Task<byte[]> GetCompressedFile(FileStorageInformation info, bool forceType2 = false)
        {
            using (var fs = File.OpenRead(info.RealPath))
            {
                using (var br = new BinaryReader(fs))
                {

                    br.BaseStream.Seek(info.RealOffset, SeekOrigin.Begin);
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

            storageInfo.FileSize = data.Length;
            if ((storageInfo.StorageType == EFileStorageType.CompressedIndividual || storageInfo.StorageType == EFileStorageType.CompressedBlob) && preCompressed == false)
            {
                data = await SmartImport.CreateCompressedFile(data);
            }
            else if ((storageInfo.StorageType == EFileStorageType.UncompressedIndividual || storageInfo.StorageType == EFileStorageType.UncompressedBlob) && preCompressed == true)
            {
                var _dat = new Dat(XivCache.GameInfo.GameDirectory);
                data = await _dat.GetUncompressedData(data);
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
                // Should this also delete the temporary file?
                OffsetMapping[dataFile].Remove(offset8x);
            }
            OffsetMapping[dataFile].Add(offset8x, storageInfo);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                }

                IOUtil.DeleteTempDirectory(DefaultPathRoot);
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
