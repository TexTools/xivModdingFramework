// xivModdingFramework
// Copyright © 2018 Rafael Gonzalez - All Rights Reserved
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using HelixToolkit.SharpDX.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.Enums;
using xivModdingFramework.Textures.FileTypes;
using static xivModdingFramework.Cache.FrameworkExceptions;
using Constants = xivModdingFramework.Helpers.Constants;

namespace xivModdingFramework.SqPack.FileTypes
{
    /// <summary>
    /// The core workhorse class for working with SQPacked data, whether in an actual .DAT file or elsewhere.
    /// Contains many functions for compressing, uncompressing, and manipulating binary files.
    /// </summary>
    public static class Dat
    {
        internal const string DatExtension = ".win32.dat";
        internal const int _MAX_DATS = 8;
        static SemaphoreSlim _lock = new SemaphoreSlim(1);

        private const int _MODDED_DAT_MARK_OFFSET = 0x200;


        // Arbitrary values we write to the DATs to identify modification status.
        // Intentionally random and far apart numbers to ensure they are not accidentally set or rolled over by patching.
        internal enum DatType : int
        {
            Unmodified = 0,
            ModifiedFull = 1337,
            ModifiedPartial = 6969,
            ModifiedOldTT = 9999,
        }

        /// <summary>
        /// Universal Boolean that should be checked before allowing any alteration to DAT files.
        /// The only place this should be ignored is interanlly during the Transaction Commit phase.
        /// </summary>
        public static bool AllowDatAlteration
        {
            get
            {
                if(ModTransaction.ActiveTransaction != null)
                {
                    return false;
                }
                if (!XivCache.GameWriteEnabled)
                {
                    return false;
                }
                return true;
            }
        }

        #region Functions for managing the actual DAT files themselves (Create, update headers, etc.)

        public static long GetMaximumDatSize()
        {
            var is64b = Environment.Is64BitOperatingSystem;
            var runningIn32bMode = IntPtr.Size == 4;

            if (!is64b || runningIn32bMode)
            {
                return 2000000000;
            } else
            {
                // Check the user's FFXIV installation drive to see what the maximum file size is for their file system.
                var drive = XivCache.GameInfo.GameDirectory.FullName.Substring(0, 1);
                return GetMaximumFileSize(drive);
            }
        }

        // Returns the maximum file size in bytes on the filesystem type of the specified drive.
        private static long GetMaximumFileSize(string drive)
        {
            var driveInfo = new System.IO.DriveInfo(drive);

            switch (driveInfo.DriveFormat)
            {
                case "FAT16":
                    return 2147483647;
                case "FAT32":
                    return 4294967296;
                default:
                    // Unknown HDD Format, default to the max index limit.
                    // 2 ^35 is the maximum addressable size in the Index files. (28 precision bits, left-shifted 7 bits (increments of 128)
                    return 34359738368;
            }
        }



        /// <summary>
        /// Creates a new dat file to store modified data.
        /// </summary>
        /// <remarks>
        /// This will first find what the largest dat number is for a given data file
        /// It will then create a new dat file that is one number larger
        /// Lastly it will update the index files to reflect the new dat count
        /// </remarks>
        /// <param name="dataFile">The data file to create a new dat for.</param>
        /// <returns>The new dat number.</returns>
        private static int CreateNewDat(XivDataFile dataFile)
        {
            var nextDatNumber = GetLargestDatNumber(dataFile) + 1;

            if (nextDatNumber == 8)
            {
                return 8;
            }

            var datPath = Dat.GetDatPath(dataFile, nextDatNumber);

            using (var fs = File.Create(datPath))
            {
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write(MakeCustomDatSqPackHeader());
                    bw.Write(MakeDatHeader(nextDatNumber, 0));
                }
            }

            return nextDatNumber;
        }

        /// <summary>
        /// Gets the largest dat number for a given data file.
        /// </summary>
        /// <param name="dataFile">The data file to check.</param>
        /// <returns>The largest dat number for the given data file.</returns>
        internal static int GetLargestDatNumber(XivDataFile dataFile)
        {
            if (!Directory.Exists(dataFile.GetContainingFolder()))
            {
                return 0;
            }
            string[] allFiles = Directory.GetFiles(dataFile.GetContainingFolder());

            var dataFiles = from file in allFiles where file.Contains(dataFile.GetFileName()) && file.Contains(".dat") select file;

            try
            {
                var max = dataFiles.Select(file => int.Parse(file.Substring(file.Length - 1))).Concat(new[] { 0 }).Max();

                return max;
            }
            catch(Exception ex)
            {
                var fileList = "";
                foreach (var file in dataFiles)
                {
                    fileList += $"{file}\n";
                }

                throw new Exception($"Unable to determine Dat Number from one of the following files\n\n{fileList}");
            }

        }


        internal static DatType GetDatType(XivDataFile df, int datNumber)
        {
            var datFilePath = Dat.GetDatPath(df, datNumber);

            if (!File.Exists(datFilePath))
            {
                throw new FileNotFoundException("DAT File does not exist: " + df.ToString() + " #" + datNumber);
            }

            using (var binaryReader = new BinaryReader(File.OpenRead(datFilePath)))
            {
                binaryReader.BaseStream.Seek(_MODDED_DAT_MARK_OFFSET, SeekOrigin.Begin);
                var one = binaryReader.ReadInt32();
                var two = binaryReader.ReadInt32();

                if (one == (int)DatType.ModifiedFull && two == (int)DatType.ModifiedFull)
                {
                    return DatType.ModifiedFull;
                } else if (one == (int)DatType.ModifiedPartial && two == (int)DatType.ModifiedPartial)
                {
                    return DatType.ModifiedPartial;
                } else if(one == (int)DatType.Unmodified && two == (int)DatType.Unmodified)
                {
                    // Detection for old TexTools DATs.
                    if (IsOldTTDat(binaryReader))
                    {
                        return DatType.ModifiedOldTT;
                    }

                    return DatType.Unmodified;
                }
                else
                {
                    throw new Exception("Unknown Format or corrupt DAT: " + df.ToString());
                }
            }
        }

        public static bool IsOriginalDat(XivDataFile df, int datNumber)
        {
            var datType = GetDatType(df, datNumber);

            return datType == DatType.Unmodified;
        }

        private static bool IsOldTTDat(BinaryReader br)
        {
            var _DataSizeOffset = 1024 + 12;
            br.BaseStream.Seek(_DataSizeOffset, SeekOrigin.Begin);

            var _DataHashOffset = 1024 + 32;
            br.BaseStream.Seek(_DataHashOffset, SeekOrigin.Begin);
            var bytes = br.ReadBytes(64);

            if(bytes.Any(x => x != 0))
            {
                // No edition of TexTools anywhere writes this hash.
                // This is our safest check.
                return false;
            }

            return true;
        }

        public static void AssertOriginalOffsetIsSafe(XivDataFile df, long offset8x)
        {
            if(OriginalDats == null)
            {
                CacheOriginalDatList();
            }

            if (!OriginalDats.ContainsKey(df))
            {
                throw new InvalidDataException("Original offset pointed to a Data File which did not exist.");
            }

            if(offset8x == 0)
            {
                return;
            }

            var parts = IOUtil.Offset8xToParts(offset8x);
            if (!OriginalDats[df].Contains(parts.DatNum))
            {
                throw new InvalidDataException("Original offset points to a Modded DAT file, cannot complete unsafe mod file write.");
            }

        }

        private static Dictionary<XivDataFile, List<int>> OriginalDats;
        internal static void CacheOriginalDatList()
        {
            OriginalDats = new Dictionary<XivDataFile, List<int>>();
            foreach (XivDataFile f in Enum.GetValues(typeof(XivDataFile)))
            {
                var datList = new List<int>();
                for (var i = 0; i < 8; i++)
                {
                    var datFilePath = Dat.GetDatPath(f, i);
                    if (File.Exists(datFilePath))
                    {
                        if (IsOriginalDat(f, i))
                        {
                            datList.Add(i);
                        }
                    }
                }
                OriginalDats.Add(f, datList);
            }
        }


        /// <summary>
        /// Gets the modded dat files
        /// </summary>
        /// <param name="dataFile">The data file to check</param>
        /// <returns>A list of modded dat files</returns>
        internal static List<string> GetOriginalDatList(XivDataFile dataFile)
        {
            var datList = new List<string>();
            for (var i = 0; i < 8; i++)
            {
                var datFilePath = Dat.GetDatPath(dataFile, i);
                if (File.Exists(datFilePath))
                {
                    if (IsOriginalDat(dataFile, i))
                    {
                        datList.Add(datFilePath);
                    }
                }
            }
            return datList;
        }

        /// <summary>
        /// Gets the modded dat files
        /// </summary>
        /// <param name="dataFile">The data file to check</param>
        /// <returns>A list of modded dat files</returns>
        internal static List<string> GetModdedDatList(XivDataFile dataFile, bool includeOldTT = false)
        {
            var datList = new List<string>();
            for (var i = 0; i < 8; i++)
            {
                var datFilePath = Dat.GetDatPath(dataFile, i);
                if (File.Exists(datFilePath))
                {
                    var type = GetDatType(dataFile, i);
                    if(type == DatType.ModifiedFull || type == DatType.ModifiedPartial)
                    { 
                        datList.Add(datFilePath);
                    } else if(type == DatType.ModifiedOldTT && includeOldTT)
                    {
                        datList.Add(datFilePath);
                    }
                }
            }
            return datList;
        }

        /// <summary>
        /// Makes the header for the SqPack portion of the dat file.
        /// </summary>
        /// <returns>byte array containing the header.</returns>
        private static byte[] MakeCustomDatSqPackHeader()
        {
            var header = new byte[1024];

            using (var bw = new BinaryWriter(new MemoryStream(header)))
            {
                var sha1 = new SHA1Managed();

                // Magic Bytes
                bw.Write(1632661843);   // 0x00
                bw.Write(27491);        // 0x04

                // Platform ID
                bw.Write(0);            // 0x08

                // Size of SQPack Header
                bw.Write(1024);         // 0x0C
                
                // Version
                bw.Write(1);            // 0x10

                // Type
                bw.Write(1);            // 0x14

                // Unknown blank data.  Possibly Padding bytes.
                bw.Write(0);            // 0x18
                bw.Write(0);            // 0x1C
                
                // End of header marker
                bw.Write(-1);           // 0x20

                // Framework-Modded Dat-Mark
                bw.Seek(_MODDED_DAT_MARK_OFFSET, SeekOrigin.Begin);
                bw.Write(1337);         // 0x200
                bw.Write(1337);         // 0x204

                bw.Seek(960, SeekOrigin.Begin);
                bw.Write(sha1.ComputeHash(header, 0, 959));
            }

            return header;
        }


        /// <summary>
        /// Makes the header for the dat file.
        /// </summary>
        /// <returns>byte array containing the header.</returns>
        private static byte[] MakeDatHeader(int datNum, long fileSize, byte[] dataHash = null)
        {
            var header = new byte[1024];

            // SqPack and Dat headers are 1024 each.
            var dataSize = (uint) ((fileSize - 2048) / 128);

            if(dataHash == null)
            {
                dataHash = new byte[64];
            }

            //var maxSize = GetMaximumDatSize();


            using (var bw = new BinaryWriter(new MemoryStream(header)))
            {
                var sha1 = new SHA1Managed();

                // Dat Header Length
                bw.Write(header.Length);        // 0x00

                // Blank
                bw.Write(0);                    // 0x04

                // Unknown
                bw.Write(16);                   // 0x08

                // Data Size divided by 128 (# of file slots?)
                bw.Write(dataSize);             // 0x0C

                // Dat Number + 1
                bw.Write((datNum + 1));         // 0x10

                // Blank
                bw.Write(0);                    // 0x14

                // (Ostensibly) Max File Size....
                // But SE just seems to keep this locked at 2m even after exceeding that value.
                // When in Rome...
                bw.Write(2000000000);           // 0x18
                //bw.Write(maxSize);            // 0x18

                // Blank
                bw.Write(0);                    // 0x1C

                // Hash of the File Data (0x800 - EOF)
                bw.Write(dataHash);             // 0x20
                
                // 0x3c0 - Hash of the DAT header.
                bw.Seek(960, SeekOrigin.Begin);
                bw.Write(sha1.ComputeHash(header, 0, 960));
            }

            return header;
        }

        /// <summary>
        /// Updates the active DAT's header, returning the stream pointer back to where it was after.
        /// </summary>
        /// <param name="bw"></param>
        /// <param name="datNum"></param>
        /// <param name="fileSize"></param>
        /// <param name="dataHash"></param>
        private static void UpdateDatHeader(BinaryWriter bw, int datNum, long fileSize, byte[] dataHash = null)
        {
            var pos = bw.BaseStream.Position;
            var header = MakeDatHeader(datNum, fileSize, dataHash);
            bw.BaseStream.Seek(1024, SeekOrigin.Begin);
            bw.Write(header);
            bw.BaseStream.Seek(pos, SeekOrigin.Begin);
        }

        #endregion


        #region Type 2 (Binary) File Importing

        /// <summary>
        /// Imports any Type 2 (Binary) data
        /// </summary>
        /// <param name="internalPath">The internal file path of the item.</param>
        /// <param name="source">The source/application that is writing to the dat.</param>
        public static async Task<long> ImportType2Data(string externalFilePath, string internalPath, string source, IItem referenceItem = null, ModTransaction tx = null)
        {
            return await ImportType2Data(File.ReadAllBytes(externalFilePath), internalPath, source, referenceItem, tx);
        }

        /// <summary>
        /// Imports type 2 (Binary) data.
        /// </summary>
        /// <param name="dataToImport">Raw data to import</param>
        /// <param name="internalPath">Internal path to update index for.</param>
        /// <param name="source">Source application making the changes/</param>
        /// <param name="referenceItem">Item to reference for name/category information, etc.</param>
        /// <returns></returns>
        public static async Task<long> ImportType2Data(byte[] dataToImport,  string internalPath, string source, IItem referenceItem = null, ModTransaction tx = null)
        {
            var newOffset = await WriteModFile(dataToImport, internalPath, source, referenceItem, tx, false);

            if (newOffset <= 0)
            {
                throw new Exception("There was an error writing to the dat file. Offset returned was 0.");
            }

            return newOffset;
        }


        /// <summary>
        /// Create compressed type 2 SqPack data from uncompressed binary data.
        /// </summary>
        /// <param name="dataToCreate">Bytes to Type 2data</param>
        /// <returns></returns>
        public static async Task<byte[]> CompressType2Data(byte[] dataToCreate)
        {
            var newData = new List<byte>();
            var headerData = new List<byte>();
            var dataBlocks = new List<byte>();

            // Header size is defaulted to 128, but may need to change if the data being imported is very large.
            headerData.AddRange(BitConverter.GetBytes(128));
            headerData.AddRange(BitConverter.GetBytes(2));
            headerData.AddRange(BitConverter.GetBytes(dataToCreate.Length));

            var dataOffset = 0;
            var totalCompSize = 0;
            var uncompressedLength = dataToCreate.Length;

            var partCount = (int)Math.Ceiling(uncompressedLength / 16000f);

            headerData.AddRange(BitConverter.GetBytes(partCount));

            var remainder = uncompressedLength;

            using (var binaryReader = new BinaryReader(new MemoryStream(dataToCreate)))
            {
                binaryReader.BaseStream.Seek(0, SeekOrigin.Begin);

                for (var i = 1; i <= partCount; i++)
                {
                    if (i == partCount)
                    {
                        var compressedData = await IOUtil.Compressor(binaryReader.ReadBytes(remainder));
                        var padding = 128 - ((compressedData.Length + 16) % 128);

                        dataBlocks.AddRange(BitConverter.GetBytes(16));
                        dataBlocks.AddRange(BitConverter.GetBytes(0));
                        dataBlocks.AddRange(BitConverter.GetBytes(compressedData.Length));
                        dataBlocks.AddRange(BitConverter.GetBytes(remainder));
                        dataBlocks.AddRange(compressedData);
                        dataBlocks.AddRange(new byte[padding]);

                        headerData.AddRange(BitConverter.GetBytes(dataOffset));
                        headerData.AddRange(BitConverter.GetBytes((short)((compressedData.Length + 16) + padding)));
                        headerData.AddRange(BitConverter.GetBytes((short)remainder));

                        totalCompSize = dataOffset + ((compressedData.Length + 16) + padding);
                    }
                    else
                    {
                        var compressedData = await IOUtil.Compressor(binaryReader.ReadBytes(16000));
                        var padding = 128 - ((compressedData.Length + 16) % 128);

                        dataBlocks.AddRange(BitConverter.GetBytes(16));
                        dataBlocks.AddRange(BitConverter.GetBytes(0));
                        dataBlocks.AddRange(BitConverter.GetBytes(compressedData.Length));
                        dataBlocks.AddRange(BitConverter.GetBytes(16000));
                        dataBlocks.AddRange(compressedData);
                        dataBlocks.AddRange(new byte[padding]);

                        headerData.AddRange(BitConverter.GetBytes(dataOffset));
                        headerData.AddRange(BitConverter.GetBytes((short)((compressedData.Length + 16) + padding)));
                        headerData.AddRange(BitConverter.GetBytes((short)16000));

                        dataOffset += ((compressedData.Length + 16) + padding);
                        remainder -= 16000;
                    }
                }
            }

            headerData.InsertRange(12, BitConverter.GetBytes(totalCompSize / 128));
            headerData.InsertRange(16, BitConverter.GetBytes(totalCompSize / 128));

            var headerSize = headerData.Count;
            var rem = headerSize % 128;
            if(rem != 0)
            {
                headerSize += (128 - rem);
            }

            headerData.RemoveRange(0, 4);
            headerData.InsertRange(0, BitConverter.GetBytes(headerSize));

            var headerPadding = rem == 0 ? 0 : 128 - rem;
            headerData.AddRange(new byte[headerPadding]);

            newData.AddRange(headerData);
            newData.AddRange(dataBlocks);
            return newData.ToArray();
        }

        #endregion


        #region SQPack Compressed File Reading/Decompressing

        public static async Task<byte[]> ReadSqPackType2(byte[] data, long offset = 0)
        {
            using (var ms = new MemoryStream(data))
            {
                using (var br = new BinaryReader(ms))
                {
                    return await ReadSqPackType2(br, offset);
                }
            }
        }
        public static async Task<byte[]> ReadSqPackType2(BinaryReader br, long offset = -1)
        {
            if (offset >= 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            }
            else
            {
                offset = br.BaseStream.Position;
            }

            var headerLength = br.ReadInt32();
            var fileType = br.ReadInt32();
            if (fileType != 2)
            {
                throw new Exception("Requested Type 2 file is not a valid type 2 file.");
            }
            var uncompSize = br.ReadInt32();
            var bufferInfoA = br.ReadInt32();
            var bufferInfoB = br.ReadInt32();

            var dataBlockCount = br.ReadInt32();

            var type2Bytes = new List<byte>(uncompSize);
            for (var i = 0; i < dataBlockCount; i++)
            {
                br.BaseStream.Seek(offset + (24 + (8 * i)), SeekOrigin.Begin);

                var dataBlockOffset = br.ReadInt32();

                br.BaseStream.Seek(offset + headerLength + dataBlockOffset, SeekOrigin.Begin);

                br.ReadBytes(8);

                var compressedSize = br.ReadInt32();
                var uncompressedSize = br.ReadInt32();

                // When the compressed size of a data block shows 32000, it is uncompressed.
                if (compressedSize == 32000)
                {
                    type2Bytes.AddRange(br.ReadBytes(uncompressedSize));
                }
                else
                {
                    var compressedData = br.ReadBytes(compressedSize);

                    var decompressedData = await IOUtil.Decompressor(compressedData, uncompressedSize);

                    type2Bytes.AddRange(decompressedData);
                }
            }
            return type2Bytes.ToArray();
        }


        public static async Task<byte[]> ReadSqPackType3(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            {
                using (var br = new BinaryReader(ms))
                {
                    return await ReadSqPackType3(br);
                }
            }
        }
        public static async Task<byte[]> ReadSqPackType3(BinaryReader br, long offset = -1)
        {
            if (offset >= 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            }
            else
            {
                offset = br.BaseStream.Position;
            }

            const int baseHeaderLength = 68; // start of file until after "padding"
            var headerLength = br.ReadInt32();
            var fileType = br.ReadInt32();
            var decompressedSize = br.ReadInt32();
            var buffer1 = br.ReadInt32();
            var buffer2 = br.ReadInt32();
            var version = br.ReadInt32();


            var endOfHeader = offset + headerLength;

            // Uncompressed...
            var vertexInfoSize = br.ReadInt32();
            var modelDataSize = br.ReadInt32();
            var vertexBufferSizes = Read3IntBuffer(br);
            var edgeGeometryVertexBufferSizes = Read3IntBuffer(br);
            var indexBufferSizes = Read3IntBuffer(br);

            // Compressed...
            var vertexInfoCompressedSize = br.ReadInt32();
            var modelDataCompressedSize = br.ReadInt32();
            var compressedvertexBufferSizes = Read3IntBuffer(br);
            var compressededgeGeometryVertexBufferSizes = Read3IntBuffer(br);
            var compressedindexBufferSizes = Read3IntBuffer(br);

            // Offsets....
            var vertexInfoOffset = br.ReadInt32();
            var modelDataOffset = br.ReadInt32();
            var vertexBufferOffsets = Read3IntBuffer(br);
            var edgeGeometryVertexBufferOffsets = Read3IntBuffer(br);
            var indexBufferOffsets = Read3IntBuffer(br);

            // Block Indexes....
            var vertexInfoBlockIndex = br.ReadInt16();
            var modelDataBLockIndex = br.ReadInt16();
            var vertexBufferBlockIndexs = Read3IntBuffer(br, true);
            var edgeGeometryVertexBufferBlockIndexs = Read3IntBuffer(br, true);
            var indexBufferBlockIndexs = Read3IntBuffer(br, true);

            // Block Counts....
            var vertexInfoBlockCount = br.ReadInt16();
            var modelDataBlockCount = br.ReadInt16();
            var vertexBufferBlockCounts = Read3IntBuffer(br, true);
            var edgeGeometryVertexBufferBlockCounts = Read3IntBuffer(br, true);
            var indexBufferBlockCounts = Read3IntBuffer(br, true);

            var meshCount = br.ReadUInt16();
            var materialCount = br.ReadUInt16();

            var lodCount = br.ReadByte();
            var flags = br.ReadByte();

            var padding = br.ReadBytes(2);

            var totalBlocks = vertexInfoBlockCount + modelDataBlockCount;
            totalBlocks += vertexBufferBlockCounts.Sum(x => (int)x);
            totalBlocks += edgeGeometryVertexBufferBlockCounts.Sum(x => (int)x);
            totalBlocks += indexBufferBlockCounts.Sum(x => (int)x);

            var blockSizes = new int[totalBlocks];

            for (var i = 0; i < totalBlocks; i++)
            {
                blockSizes[i] = br.ReadUInt16();
            }

            var distanceToEndOfHeader = endOfHeader - br.BaseStream.Position;
            var extraData = br.ReadBytes((int)distanceToEndOfHeader);


            // Read the compressed blocks.
            // These could technically be read as contiguous blocks typically,
            // But it's safer to actually use their offsets and validate them in the process.

            var vertexInfoData = BeginReadCompressedBlocks(br, vertexInfoBlockCount, endOfHeader + vertexInfoOffset);
            var modelInfoData = BeginReadCompressedBlocks(br, modelDataBlockCount, endOfHeader + modelDataOffset);

            const int _VertexSegments = 3;
            var vertexBuffers = new List<Task<byte[]>>[_VertexSegments];
            for (int i = 0; i < _VertexSegments; i++)
            {
                vertexBuffers[i] = BeginReadCompressedBlocks(br, (int)vertexBufferBlockCounts[i], endOfHeader + vertexBufferOffsets[i]);
            }

            var edgeBuffers = new List<Task<byte[]>>[_VertexSegments];
            for (int i = 0; i < _VertexSegments; i++)
            {
                edgeBuffers[i] = BeginReadCompressedBlocks(br, (int)edgeGeometryVertexBufferBlockCounts[i], endOfHeader + edgeGeometryVertexBufferOffsets[i]);
            }

            var indexBuffers = new List<Task<byte[]>>[_VertexSegments];
            for (int i = 0; i < _VertexSegments; i++)
            {
                var last = false;
                if(i == _VertexSegments - 1)
                {
                    last = true;
                }
                indexBuffers[i] = BeginReadCompressedBlocks(br, (int)indexBufferBlockCounts[i], endOfHeader + indexBufferOffsets[i], last);
            }

            // Reserve space at the start of the result for the header
            var decompressedData = new byte[baseHeaderLength + decompressedSize];
            int decompOffset = baseHeaderLength;

            // Need to mark these as we unzip them.
            var vertexBufferUncompressedOffsets = new uint[_VertexSegments];
            var indexBufferUncompressedOffsets = new uint[_VertexSegments];
            var vertexBufferRealSizes = new uint[_VertexSegments];
            var indexBufferRealSizes = new uint[_VertexSegments];

            // Vertex and Model Headers
            var res = await CompleteReadCompressedBlocks(vertexInfoData, decompressedData, decompOffset);
            decompressedData = res.Buffer;
            decompOffset += res.BytesWritten;
            var vInfoRealSize = res.BytesWritten;

            res = await CompleteReadCompressedBlocks(modelInfoData, decompressedData, decompOffset);
            decompressedData = res.Buffer;
            decompOffset += res.BytesWritten;
            var mInfoRealSize = res.BytesWritten;

            for (int i = 0; i < _VertexSegments; i++)
            {
                // Geometry data in LoD order.
                // Mark the real uncompressed offsets and sizes on the way through.
                vertexBufferUncompressedOffsets[i] = (uint)decompOffset;
                res = await CompleteReadCompressedBlocks(vertexBuffers[i], decompressedData, decompOffset);
                decompressedData = res.Buffer;
                decompOffset += res.BytesWritten;
                vertexBufferRealSizes[i] = (uint)res.BytesWritten;

                res = await CompleteReadCompressedBlocks(edgeBuffers[i], decompressedData, decompOffset);
                decompressedData = res.Buffer;
                decompOffset += res.BytesWritten;

                indexBufferUncompressedOffsets[i] = (uint)decompOffset;
                res = await CompleteReadCompressedBlocks(indexBuffers[i], decompressedData, decompOffset);
                decompressedData = res.Buffer;
                decompOffset += res.BytesWritten;
                indexBufferRealSizes[i] = (uint)res.BytesWritten;
            }

            var header = new List<byte>(baseHeaderLength);

            // Generated header for live/uncompressed MDL files.
            header.AddRange(BitConverter.GetBytes(version));
            header.AddRange(BitConverter.GetBytes(vInfoRealSize));
            header.AddRange(BitConverter.GetBytes(mInfoRealSize));
            header.AddRange(BitConverter.GetBytes((ushort)meshCount));
            header.AddRange(BitConverter.GetBytes((ushort)materialCount));

            Write3IntBuffer(header, vertexBufferUncompressedOffsets);
            Write3IntBuffer(header, indexBufferUncompressedOffsets);
            Write3IntBuffer(header, vertexBufferRealSizes);
            Write3IntBuffer(header, indexBufferRealSizes);

            header.Add(lodCount);
            header.Add(flags);
            header.AddRange(padding);

            // Copy the header over the reserved space at the start of decompressedData
            header.CopyTo(decompressedData, 0);

            return decompressedData;
        }


        public static async Task<byte[]> ReadSqPackType4(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            {
                using (var br = new BinaryReader(ms))
                {
                    return await ReadSqPackType4(br);
                }
            }
        }
        public static async Task<byte[]> ReadSqPackType4(BinaryReader br, long offset = -1)
        {
            if (offset >= 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            } else
            {
                offset = br.BaseStream.Position;
            }

            // Type 4 data is pretty simple.

            // Standard SQPack header.
            var headerLength = br.ReadInt32();
            var fileType = br.ReadInt32();
            var uncompressedFileSize = br.ReadInt32();
            var ikd1 = br.ReadInt32();
            var ikd2 = br.ReadInt32();

            // Count of mipmaps.
            var mipCount = br.ReadInt32();

            var endOfHeader = offset + headerLength;
            var mipMapInfoOffset = offset + 24;


            // Tex File Header
            br.BaseStream.Seek(endOfHeader, SeekOrigin.Begin);
            var texHeader = br.ReadBytes((int)Tex._TexHeaderSize);

            // Decompress Mipmap blocks ...
            var decompressedData = new byte[uncompressedFileSize];
            int decompOffset = 0;

            var mipData = new List<Task<byte[]>>[mipCount];

            // Each MipMap has a basic header of information, and a set of compressed data blocks of info.
            for (int i = 0; i < mipCount; i++)
            {
                const int _MipMapHeaderSize = 20;
                br.BaseStream.Seek(mipMapInfoOffset + (_MipMapHeaderSize * i), SeekOrigin.Begin);

                var offsetFromHeaderEnd = br.ReadInt32();
                var mipMapLength = br.ReadInt32();
                var mipMapSize = br.ReadInt32();
                var mipMapStart = br.ReadInt32();
                var mipMapParts = br.ReadInt32();

                var mipMapPartOffset = endOfHeader + offsetFromHeaderEnd;

                br.BaseStream.Seek(mipMapPartOffset, SeekOrigin.Begin);

                var last = false;
                if(i == mipCount -1)
                {
                    last = true;
                }

                mipData[i] = BeginReadCompressedBlocks(br, mipMapParts, -1, last);
            }

            for (int i = 0; i < mipCount; i++)
            {
                var res = await CompleteReadCompressedBlocks(mipData[i], decompressedData, decompOffset);
                decompOffset += res.BytesWritten;
                decompressedData = res.Buffer;
            }

            byte[] finalbytes = new byte[texHeader.Length + decompressedData.Length];
            Array.Copy(texHeader, 0, finalbytes, 0, texHeader.Length);
            Array.Copy(decompressedData, 0, finalbytes, texHeader.Length, decompressedData.Length);

            return finalbytes;
        }

        public static uint GetSqPackType(BinaryReader br, long offset = -1)
        {
            if (offset >= 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            }
            else
            {
                offset = br.BaseStream.Position;
            }

            br.BaseStream.Seek(offset + 4, SeekOrigin.Begin);
            var type = br.ReadUInt32();
            return type;
        }


        /// <summary>
        /// Syntactic wrapper for tx.ReadFile()
        /// </summary>
        public static async Task<byte[]> ReadFile(string filePath, bool forceOriginal, ModTransaction tx = null)
        {
            if(tx == null)
            {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginReadonlyTransaction();
            }

            return await tx.ReadFile(filePath, forceOriginal, false);
        }

        /// <summary>
        /// Syntactic wrapper for tx.ReadFile()
        /// </summary>
        public static async Task<byte[]> ReadFile(XivDataFile dataFile, long offset8x, ModTransaction tx = null)
        {
            if (tx == null)
            {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginReadonlyTransaction();
            }

            return await tx.ReadFile(dataFile, offset8x, false);
        }

        /// <summary>
        /// Decompresses (De-SqPacks) a given block of data.
        /// </summary>
        /// <param name="sqpackData"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static async Task<byte[]> ReadSqPackFile(byte[] sqpackData)
        {
            using (var ms = new MemoryStream(sqpackData))
            {
                using (var br = new BinaryReader(ms))
                {
                    return await ReadSqPackFile(br);
                }
            }
        }

        /// <summary>
        /// Reads an SQPack file from the given data stream.
        /// </summary>
        /// <param name="br"></param>
        /// <returns></returns>
        public static async Task<byte[]> ReadSqPackFile(BinaryReader br, long offset = -1)
        {
            if(offset >= 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            }
            else
            {
                offset = br.BaseStream.Position;
            }

            br.BaseStream.Seek(offset + 4, SeekOrigin.Begin);
            var type = br.ReadInt32();

            br.BaseStream.Seek(offset, SeekOrigin.Begin);
            if (type == 2)
            {
                return await ReadSqPackType2(br, offset);
            }
            else if (type == 3)
            {
                return await ReadSqPackType3(br, offset);
            }
            else if (type == 4)
            {
                return await ReadSqPackType4(br, offset);
            }
            throw new NotImplementedException("Unable to read invalid SQPack File Type.");
        }

        #endregion


        /// Creates the header for the compressed texture data to be imported.
        /// </summary>
        /// <param name="uncompressedLength">Length of the uncompressed texture file.</param>
        /// <param name="newMipCount">The number of mipmaps the DDS texture to be imported contains.</param>
        /// <param name="newWidth">The width of the DDS texture to be imported.</param>
        /// <param name="newHeight">The height of the DDS texture to be imported.</param>
        /// <returns>The created header data.</returns>
        internal static byte[] MakeType4DatHeader(XivTexFormat format, List<List<byte[]>> ddsParts, int uncompressedLength, int newWidth, int newHeight)
        {
            var headerData = new List<byte>();

            var mipCount = ddsParts.Count;
            var totalParts = ddsParts.Sum(x => x.Count);
            var headerSize = 24 + (mipCount * 20) + (totalParts * 2);
            var headerPadding = 128 - (headerSize % 128);

            headerData.AddRange(BitConverter.GetBytes(headerSize + headerPadding));
            headerData.AddRange(BitConverter.GetBytes(4));
            headerData.AddRange(BitConverter.GetBytes(uncompressedLength + 80));
            headerData.AddRange(BitConverter.GetBytes(0)); // Buffer info 0 apparently works fine?
            headerData.AddRange(BitConverter.GetBytes(0)); // Buffer info 0 apparently works fine?
            headerData.AddRange(BitConverter.GetBytes(mipCount));


            var dataBlockOffset = 0;
            var mipCompressedOffset = 80;
            var uncompMipSize = newHeight * newWidth;

            switch (format)
            {
                case XivTexFormat.DXT1:
                    uncompMipSize = (newWidth * newHeight) / 2;
                    break;
                case XivTexFormat.DXT5:
                case XivTexFormat.A8:
                    uncompMipSize = newWidth * newHeight;
                    break;
                case XivTexFormat.A1R5G5B5:
                case XivTexFormat.A4R4G4B4:
                    uncompMipSize = (newWidth * newHeight) * 2;
                    break;
                case XivTexFormat.L8:
                case XivTexFormat.A8R8G8B8:
                case XivTexFormat.X8R8G8B8:
                case XivTexFormat.R32F:
                case XivTexFormat.G16R16F:
                case XivTexFormat.G32R32F:
                case XivTexFormat.A16B16G16R16F:
                case XivTexFormat.A32B32G32R32F:
                case XivTexFormat.DXT3:
                case XivTexFormat.D16:
                default:
                    uncompMipSize = (newWidth * newHeight) * 4;
                    break;
            }

            for (var i = 0; i < mipCount; i++)
            {
                // Compressed Offset (Starting after sqpack header)
                headerData.AddRange(BitConverter.GetBytes(mipCompressedOffset));

                // Compressed Size
                var compressedSize = ddsParts[i].Sum(x => x.Length);
                headerData.AddRange(BitConverter.GetBytes(compressedSize));
                
                // Uncompressed Size
                var uncompressedSize = uncompMipSize > 16 ? uncompMipSize : 16;
                headerData.AddRange(BitConverter.GetBytes(uncompressedSize));

                // Data Block Offset
                headerData.AddRange(BitConverter.GetBytes(dataBlockOffset));

                // Data Block Size
                headerData.AddRange(BitConverter.GetBytes(ddsParts[i].Count));


                // Every MipMap is 1/4th the net size, so this is a easy way to recalculate it.
                uncompMipSize = uncompMipSize / 4;

                dataBlockOffset = dataBlockOffset + ddsParts[i].Count;
                mipCompressedOffset = mipCompressedOffset + compressedSize;
            }

            // This seems to be (another) listing of part sizes?
            foreach (var mip in ddsParts)
            {
                foreach(var part in mip)
                {
                    headerData.AddRange(BitConverter.GetBytes((ushort) part.Length));
                }
            }

            headerData.AddRange(new byte[headerPadding]);

            return headerData.ToArray();
        }


        /// <summary>
        /// Copies a file from a given offset to a new path in the game files.
        /// </summary>
        /// <param name="targetPath"></param>
        /// <param name="source"></param>
        /// <returns></returns>
        public static async Task<long> CopyFile(string sourcePath, string targetPath, string source = "Unknown", bool overwrite = false, IItem referenceItem = null, ModTransaction tx = null)
        {
            var boiler = await TxBoiler.BeginWrite(tx);
            tx = boiler.Transaction;
            try
            {
                var exists = await tx.FileExists(targetPath);
                if (exists && !overwrite)
                {
                    return await tx.Get8xDataOffset(targetPath);
                }

                var data = await tx.ReadFile(sourcePath, false, true);


                XivDependencyRoot root = null;

                if (referenceItem == null)
                {
                    try
                    {
                        root = await XivCache.GetFirstRoot(targetPath);
                        if (root != null)
                        {
                            var item = root.GetFirstItem();

                            referenceItem = item;
                        }
                        else
                        {
                            referenceItem = new XivGenericItemModel()
                            {
                                Name = Path.GetFileName(targetPath),
                                SecondaryCategory = "Raw File Copy"
                            };
                        }
                    }
                    catch
                    {
                        referenceItem = new XivGenericItemModel()
                        {
                            Name = Path.GetFileName(targetPath),
                            SecondaryCategory = "Raw File Copy"
                        };
                    }
                }

                var newOffset = await WriteModFile(data, targetPath, source, referenceItem, tx);
                await boiler.Commit();
                return newOffset;
            }
            catch
            {
                await boiler.Catch();
                throw;
            }
        }


        #region Core Mod File Writing

        /// <summary>
        /// Writes a given block of data to the DAT files or Transaction data store, updates the index to point to it for the given file path,
        /// creates or updates the modlist entry for the item, and triggers metadata expansion if needed.
        /// 
        /// This is the main workhorse function for mod writing, and /almost/ all methods of writing data ultimately drill down to this function.
        /// If a write call does not end up here, it is injected into the TX data store directly via the ModTransaction.UNSAFE_ functions.
        /// </summary>
        /// <param name="fileData"></param>
        /// <param name="internalFilePath"></param>
        /// <param name="sourceApplication"></param>
        /// <returns></returns>
        public static async Task<long> WriteModFile(byte[] fileData, string internalFilePath, string sourceApplication, IItem referenceItem = null, ModTransaction tx = null, bool compressed = true)
        {
            Trace.WriteLine("Writing mod file: " + internalFilePath);

            var df = IOUtil.GetDataFileFromPath(internalFilePath);

            // Open a transaction if we don't have one.
            var doDatSave = false;
            if (tx == null)
            {
                if (!AllowDatAlteration)
                {
                    throw new ModdingFrameworkException("Cannot write file while DAT Writing is disabled.");
                }

                doDatSave = true;
                tx = await ModTransaction.BeginTransaction(true);
            }

            var ownBatch = false;
            if (sourceApplication != Constants.InternalModSourceName && !tx.IsBatchingNotifications)
            {
                ownBatch = true;
                tx.INTERNAL_BeginBatchingNotifications();
            }

            try
            {
                var modList = await tx.GetModList();
                var index = await tx.GetIndexFile(IOUtil.GetDataFileFromPath(internalFilePath));

                var nullMod = modList.GetMod(internalFilePath);

                // Resolve Item to attach to.
                string itemName = "Unknown";
                string category = "Unknown";
                if (referenceItem == null)
                {
                    try
                    {
                        var root = await XivCache.GetFirstRoot(internalFilePath);
                        if (root != null)
                        {
                            var item = root.GetFirstItem();

                            referenceItem = item;
                            itemName = referenceItem.GetModlistItemName();
                            category = referenceItem.GetModlistItemCategory();
                        }
                    }
                    catch
                    {
                        itemName = Path.GetFileName(internalFilePath);
                        category = "Raw File";
                    }
                }
                else
                {
                    itemName = referenceItem.GetModlistItemName();
                    category = referenceItem.GetModlistItemCategory();
                }

                // TODO: Should we manually pad the file to 256 increments or 128 increments here?

                // Write to the Data store and update the index with the temporary offset.
                var offset8x = await tx.UNSAFE_WriteData(df, fileData, compressed);

                var originalOffset = await tx.Get8xDataOffset(internalFilePath, true);
                await tx.Set8xDataOffset(internalFilePath, offset8x);
                
                ModPack? modPack;
                if(tx.ModPack != null)
                {
                    modList.AddOrUpdateModpack(tx.ModPack.Value);
                }

                Mod mod;
                if (nullMod == null)
                {
                    // Determine if this is an original game file or not.
                    var fileAdditionMod = originalOffset == 0;

                    mod = new Mod()
                    {
                        ItemName = itemName,
                        ItemCategory = category,
                        SourceApplication = sourceApplication,
                        FilePath = internalFilePath,
                    };
                    mod.ModOffset8x = offset8x;
                    mod.OriginalOffset8x = originalOffset;
                    mod.FilePath = internalFilePath;

                    // If we don't have a specified modpack, but this file is already modded, retain its modpack association.
                    var mp = tx.ModPack == null ? "" : tx.ModPack.Value.Name;
                    mod.ModPack = mod.IsInternal() ? null : mp;
                }
                else
                {
                    mod = nullMod.Value;

                    var mp = tx.ModPack == null ? mod.ModPack : tx.ModPack.Value.Name;
                    var fileAdditionMod = originalOffset == 0 || mod.IsCustomFile();
                    if (fileAdditionMod)
                    {
                        mod.OriginalOffset8x = 0;
                    }
                    mod.ModOffset8x = offset8x;
                    mod.FilePath = internalFilePath;
                    mod.ModPack = mod.IsInternal() ? null : mp;
                    mod.ItemName = itemName;
                    mod.ItemCategory = category;
                    mod.SourceApplication = sourceApplication;
                }

                Dat.AssertOriginalOffsetIsSafe(IOUtil.GetDataFileFromPath(internalFilePath), mod.OriginalOffset8x);

                modList.AddOrUpdateMod(mod);

                // Always expand metadata.
                await ExpandMetadata(fileData, internalFilePath, tx, compressed);

                XivCache.QueueDependencyUpdate(internalFilePath);



                if (doDatSave)
                {
                    // Commit the transaction if we're doing a single file save.
                    await ModTransaction.CommitTransaction(tx);
                }

                // Job done.
                return offset8x;
            }
            catch
            {
                if (doDatSave)
                {
                    await ModTransaction.CancelTransaction(tx);
                }
                throw;
            }
            finally
            {
                if (ownBatch && tx != null && tx.IsBatchingNotifications)
                {
                    // Ship the combined notifications only once the metadata expansion is complete, and all the data updated.
                    tx.INTERNAL_EndBatchingNotifications();
                }
            }
        }

        /// <summary>
        /// Expands the given .meta or .rgsp file.
        /// Simplified wrapper for use in WriteModFile() in the cases where we already have the data on hand.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="internalPath"></param>
        /// <returns></returns>
        private static async Task ExpandMetadata(byte[] data, string internalPath, ModTransaction tx = null, bool compressed = true)
        {
            // Perform metadata expansion if needed.
            var ext = Path.GetExtension(internalPath);

            if (ext == ".meta")
            {
                byte[] metaRaw;
                if (compressed)
                {
                    metaRaw = (await ReadSqPackType2(data)).ToArray();
                } else
                {
                    metaRaw = data;
                }

                var meta = await ItemMetadata.Deserialize(metaRaw);
                meta.Validate(internalPath);

                await ItemMetadata.ApplyMetadata(meta, tx);
            }
            else if (ext == ".rgsp")
            {
                byte[] rgspRaw;
                if (compressed)
                {
                    rgspRaw = (await ReadSqPackType2(data)).ToArray();
                }
                else
                {
                    rgspRaw = data;
                }
                // Expand the racial scaling file.
                await CMP.ApplyRgspFile(rgspRaw, tx);
            }

        }

        /// <summary>
        /// Writes a new block of data to the given data file, without changing
        /// the indexes.  Returns the offset8x to the new data.
        ///
        /// A target offset of 0 or negative will append to the end of the first data file with space.
        /// </summary>
        /// <param name="importData"></param>
        /// <param name="dataFile"></param>
        /// <returns></returns>
        internal static async Task<long> Unsafe_WriteToDat(byte[] importData, XivDataFile dataFile, Dictionary<long, uint> openSlots)
        {

            Trace.WriteLine("Performing actual DAT file write...");

            // Perform basic validation.
            if (importData == null || importData.Length < 8)
            {
                throw new Exception("Attempted to write NULL data to DAT files.");
            }

            var fileType = BitConverter.ToInt32(importData, 4);
            if (fileType < 2 || fileType > 4)
            {
                throw new Exception("Attempted to write Invalid data to DAT files.");
            }

            await _lock.WaitAsync();
            try
            {
                // Get our new target offset...
                var offset = GetWritableOffset(dataFile, importData.Length, openSlots);

                var parts = IOUtil.Offset8xToParts(offset);

                var datPath = Dat.GetDatPath(dataFile, parts.DatNum);

                using (var bw = new BinaryWriter(File.OpenWrite(datPath)))
                {
                    // Seek to the target location.
                    bw.BaseStream.Seek(parts.Offset, SeekOrigin.Begin);

                    // Write data.
                    bw.Write(importData);

                    // Write out remaining padding as needed.
                    while ((bw.BaseStream.Position % 256) != 0)
                    {
                        bw.Write((byte)0);
                    }

                    var datSize = bw.BaseStream.Length;
                    UpdateDatHeader(bw, parts.DatNum, datSize);
                }


                return offset;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Computes a dictionary listing of all the open space in a given dat file (within the modded dats only),
        /// which can be safely written to without disrupting existing files, even if the commit goes badly.
        /// </summary>
        /// <param name="df"></param>
        /// <returns></returns>
        internal static async Task<Dictionary<long, uint>> ComputeOpenSlots(XivDataFile df, ModList modlist)
        {

            var moddedDats = Dat.GetModdedDatList(df);
            var slots = new Dictionary<long, uint>();

            // Readonly TX against base file system state.
            var tx = ModTransaction.BeginReadonlyTransaction();

            var allModsByOffset = modlist.GetModsByOffset();
            if (!allModsByOffset.ContainsKey(df))
            {
                return slots;
            }

            // Make sure they're sorted.

            var groupedByFile = allModsByOffset[df].Select(x => x.Value).GroupBy(x => IOUtil.Offset8xToParts(x.ModOffset8x).DatNum);

            var groupedAndOrderedMods = groupedByFile.ToDictionary(x => x.Key, x => x.OrderBy(x => x.ModOffset8x));


            const uint _DAT_PADDING_VALUE = 256;

            long lastEndPoint = 2048;
            foreach (var datNumKv in groupedAndOrderedMods)
            {
                var datNum = datNumKv.Key;
                var mods = datNumKv.Value;
                foreach (var mod in mods)
                {
                    var parts = IOUtil.Offset8xToParts(mod.ModOffset8x);
                    // If there's a space between previous mod and this one, it's from end of previous file to this offset.
                    var slotSize = parts.Offset - lastEndPoint;
                    if (slotSize > _DAT_PADDING_VALUE)
                    {
                        var mergedStart = IOUtil.PartsTo8xDataOffset(lastEndPoint, datNum);
                        slots.Add(mergedStart, (uint) slotSize);
                    }


                    uint size = (uint)await tx.GetCompressedFileSize(mod.DataFile, mod.ModOffset8x);
                    if (size <= 0)
                    {
                        // Force filesize read.
                        using (var br = new BinaryReader(File.OpenRead(Dat.GetDatPath(df, parts.DatNum))))
                        {
                            // Check size.
                            br.BaseStream.Seek(parts.Offset, SeekOrigin.Begin);
                            Dat.GetCompressedFileSize(br, parts.Offset);
                        }
                    }

                    // Account for required DAT padding.
                    if (size % _DAT_PADDING_VALUE != 0)
                    {
                        size += (_DAT_PADDING_VALUE - (size % _DAT_PADDING_VALUE));
                    }

                    // Mark the actual file end.
                    lastEndPoint = parts.Offset + size;


                }
            }


            return slots;
        }

        /// <summary>
        /// Gets a writable offset for a file of the given size in the target data file.
        /// Used when committing a transaction to the game files to resolve an efficient write location per file.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="fileSize"></param>
        /// <param name="availableSlots"></param>
        /// <returns></returns>
        private static long GetWritableOffset(XivDataFile dataFile, int fileSize, Dictionary<long, uint> availableSlots)
        {
            if (fileSize < 0)
            {
                throw new InvalidDataException("Cannot check space for a negative size file.");
            }

            fileSize = Dat.Pad(fileSize, 256);

            // Scan available slots first.
            var slot = availableSlots.FirstOrDefault(x => x.Value >= fileSize);
            if (slot.Key > 0 && slot.Value > 0)
            {
                // Take the slot.
                availableSlots.Remove(slot.Key);

                var slotParts = IOUtil.Offset8xToParts(slot.Key);
                var remainingSize = slot.Value - fileSize;

                // We have extra usable space remaining.
                if (remainingSize > 256)
                {
                    // Write the remainder slot back.
                    var newSlotOffset = IOUtil.PartsTo8xDataOffset(slotParts.Offset + fileSize, slotParts.DatNum);
                    availableSlots.Add(newSlotOffset, (uint)remainingSize);

                }

                return slot.Key;
            }

            // Otherwise we're looking to find a modded dat with space at the end.

            // Scan all the dat numbers...
            var datWithSpace = -1;
            long offset = -1;
            for (int i = 0; i < 8; i++)
            {
                var datPath = Dat.GetDatPath(dataFile, i);
                if (!File.Exists(datPath))
                    continue;

                var original = IsOriginalDat(dataFile, i);

                // Don't let us inject to original dat files.
                if (original) continue;


                var fInfo = new FileInfo(datPath);

                // If the DAT doesn't exist at all, we can assume we need to create a new DAT.
                if (fInfo == null || !fInfo.Exists) break;


                // Offsets must be on 256 byte intervals.
                var datSize = Dat.Pad(fInfo.Length, 256);

                // Dat is too large to fit this file, we can't write to it.
                if (datSize + fileSize >= GetMaximumDatSize()) continue;

                // Found an existing dat that has space.
                offset = IOUtil.PartsTo8xDataOffset(datSize, i);
                return offset;
            }


            // Didn't find a DAT file with space, gotta create a new one.
            datWithSpace = CreateNewDat(dataFile);

            if (datWithSpace > 7 || datWithSpace < 0)
            {
                throw new NotSupportedException("Maximum data size limit reached for DAT: " + dataFile.GetFileName());
            }

            // Offsets start at 2048
            offset = IOUtil.PartsTo8xDataOffset(2048, datWithSpace);

            return offset;
        }

        #endregion


        #region RAW DAT File Manipulation Functions (Defragment, Delete Empty, etc.)

        /// <summary>
        /// This function will rewrite all the mod files to new DAT entries, replacing the existing modded DAT files with new, defragmented ones.
        /// Returns the total amount of bytes recovered.
        /// 
        /// NOT TRANSACTION SAFE.
        /// </summary>
        /// <returns></returns>
        public static async Task<long> DefragmentModdedDats(IProgress<(int Current, int Total, string Message)> progressReporter = null)
        {
            if (!Dat.AllowDatAlteration)
            {
                throw new Exception("Cannot defragment DATs while DAT writing is disabled.");
            }

            var modlist = await Modding.INTERNAL_GetModList(true);

            var offsets = new Dictionary<string, (long oldOffset, long newOffset, uint size)>();

            var modsByDf = modlist.GetMods().GroupBy(x => IOUtil.GetDataFileFromPath(x.FilePath));
            var indexFiles = new Dictionary<XivDataFile, IndexFile>();

            var count = 0;
            var total = modlist.Mods.Count();

            var originalSize = GetTotalModDataSize();


            var workerStatus = XivCache.CacheWorkerEnabled;
            await XivCache.SetCacheWorkerState(false);
            try
            {
                var newMods = new List<Mod>();
                // Copy files over into contiguous data blocks in the new dat files.
                foreach (var dKv in modsByDf)
                {
                    var df = dKv.Key;
                    indexFiles.Add(df, await Index.INTERNAL_GetIndexFile(df));

                    foreach (var oMod in dKv)
                    {
                        var mod = oMod;
                        progressReporter?.Report((count, total, "Writing mod data to temporary DAT files..."));

                        try
                        {
                            var parts = IOUtil.Offset8xToParts(mod.ModOffset8x);
                            byte[] data;
                            using (var br = new BinaryReader(File.OpenRead(Dat.GetDatPath(df, parts.DatNum))))
                            {
                                // Check size.
                                br.BaseStream.Seek(parts.Offset, SeekOrigin.Begin);
                                var size = Dat.GetCompressedFileSize(br);

                                // Pull entire file.
                                br.BaseStream.Seek(parts.Offset, SeekOrigin.Begin);
                                data = br.ReadBytes(size);
                            }

                            var newOffset = WriteToTempDat(data, df);

                            mod.ModOffset8x = newOffset;
                            indexFiles[df].Set8xDataOffset(mod.FilePath, newOffset);
                            newMods.Add(mod);
                        }
                        catch (Exception except)
                        {
                            throw;
                        }

                        count++;
                    }
                }

                modlist.AddOrUpdateMods(newMods);

                progressReporter?.Report((0, 0, "Removing old modded DAT files..."));
                foreach (var dKv in modsByDf)
                {
                    // Now we need to delete the current modded dat files.
                    var moddedDats = Dat.GetModdedDatList(dKv.Key);
                    foreach (var file in moddedDats)
                    {
                        File.Delete(file);
                    }
                }

                // Now we need to rename our temp files.
                progressReporter?.Report((0, 0, "Renaming temporary DAT files..."));
                var finfos = XivCache.GameInfo.GameDirectory.GetFiles();
                var temps = finfos.Where(x => x.Name.EndsWith(".temp"));
                foreach (var temp in temps)
                {
                    var oldName = temp.FullName;
                    var newName = temp.FullName.Substring(0, oldName.Length - 4);
                    System.IO.File.Move(oldName, newName);
                }

                progressReporter?.Report((0, 0, "Saving updated Index Files..."));

                foreach (var dKv in modsByDf)
                {
                    indexFiles[dKv.Key].Save();
                }

                progressReporter?.Report((0, 0, "Saving updated Modlist..."));

                // Save modList
                await Modding.INTERNAL_SaveModlist(modlist);


                var finalSize = GetTotalModDataSize();
                var saved = originalSize - finalSize;
                saved = saved > 0 ? saved : 0;
                return saved;
            }
            finally
            {
                var finfos = XivCache.GameInfo.GameDirectory.GetFiles();
                var temps = finfos.Where(x => x.Name.EndsWith(".temp"));
                foreach (var temp in temps)
                {
                    temp.Delete();
                }

                await XivCache.SetCacheWorkerState(workerStatus);
            }

        }

        private static long WriteToTempDat(byte[] data, XivDataFile df)
        {
            if (!Dat.AllowDatAlteration)
            {
                throw new Exception("Cannot defragment DATs while DAT writing is disabled.");
            }

            var moddedDats = Dat.GetModdedDatList(df);
            var tempDats = moddedDats.Select(x => x + ".temp");
            var maxSize = Dat.GetMaximumDatSize();

            var rex = new Regex("([0-9])\\.temp$");
            string targetDatFile = null;
            foreach (var file in tempDats)
            {
                var datMatch = rex.Match(file);
                var datId = Int32.Parse(datMatch.Groups[1].Value);

                if (!File.Exists(file))
                {
                    using (var stream = new BinaryWriter(File.Create(file)))
                    {
                        stream.Write(Dat.MakeCustomDatSqPackHeader());
                        stream.Write(Dat.MakeDatHeader(datId, 0));
                    }
                    targetDatFile = file;
                    break;
                }

                var finfo = new FileInfo(file);
                if (finfo.Length + data.Length < maxSize)
                {
                    targetDatFile = file;
                }
            }

            if (targetDatFile == null) throw new Exception("Unable to find open temp dat to write to.");

            var match = rex.Match(targetDatFile);
            uint datNum = UInt32.Parse(match.Groups[1].Value);


            long baseOffset = 0;
            using (var stream = new BinaryWriter(File.Open(targetDatFile, FileMode.Append)))
            {
                baseOffset = stream.BaseStream.Position;
                stream.Write(data);
            }

            long offset = ((baseOffset / 8) | (datNum * 2)) * 8;

            return offset;
        }

        /// <summary>
        /// Gets the sum total size in bytes of all modded dats.
        /// Just used for user display purposes.
        /// </summary>
        /// <returns></returns>
        public static long GetTotalModDataSize()
        {
            var dataFiles = Enum.GetValues(typeof(XivDataFile)).Cast<XivDataFile>();

            long size = 0;
            foreach (var df in dataFiles)
            {
                var moddedDats = Dat.GetModdedDatList(df);
                foreach (var dat in moddedDats)
                {
                    var finfo = new FileInfo(dat);
                    size += finfo.Length;
                }
            }

            return size;
        }


        /// <summary>
        /// Removes any empty DAT files from the game files.
        /// Not Transaction safe, but also not TX relevant typically and will not allow running during TX.
        /// </summary>
        public static async Task RemoveEmptyDats(XivDataFile dataFile)
        {
            if (!XivCache.GameWriteEnabled)
            {
                throw new Exception("Cannot alter game files while DAT writing is disabled.");
            }

            if (ModTransaction.ActiveTransaction != null)
            {
                // Safety check here to prevent any misuse or weird bugs from assuming this would be based on post-transaction state.
                throw new Exception("Cannot sanely perform DAT file checks with an open write-enabled transaction.");
            }


            await Task.Run(() =>
            {
                var largestDatNum = GetLargestDatNumber(dataFile) + 1;
                var emptyList = new List<string>();

                // Never delete dat0.
                if (largestDatNum == 0) return emptyList;

                for (var i = 0; i < largestDatNum; i++)
                {
                    var datPath = Dat.GetDatPath(dataFile, i);
                    var fileInfo = new FileInfo(datPath);

                    var list = Dat.GetModdedDatList(dataFile);

                    // Do not allow deleting non-mod-dats.
                    if (!list.Contains(datPath))
                        continue;


                    if (fileInfo.Length == 0)
                    {
                        emptyList.Add(datPath);
                    }
                }
                foreach (var f in emptyList)
                {
                    File.Delete(f);
                }
                return emptyList;
            });
        }

        #endregion


        #region Handling for old/bad compressed type 4 file sizes
        public static async Task<uint> GetReportedType4UncompressedSize(string path, bool forceOrginal = false, ModTransaction tx = null)
        {
            if (tx == null)
            {
                tx = ModTransaction.BeginReadonlyTransaction();
            }
            var offset = await tx.Get8xDataOffset(path, forceOrginal);
            var df = IOUtil.GetDataFileFromPath(path);
            return await GetReportedType4UncompressedSize(df, offset, tx);

        }
        public static async Task<uint> GetReportedType4UncompressedSize(XivDataFile df, long offset8x, ModTransaction tx = null)
        {
            if (tx == null)
            {
                tx = ModTransaction.BeginReadonlyTransaction();
            }
            using (var br = await tx.GetFileStream(df, offset8x, true))
            {
                br.BaseStream.Seek(8, SeekOrigin.Current);
                var size = br.ReadUInt32();
                return size;
            }
        }

        /// <summary>
        /// Updates the compressed file size of the file at the given file storage information offset.
        /// Returns -1 if the request is invalid, otherwise returns the real file size.
        /// </summary>
        /// <param name="br"></param>
        /// <param name="bw"></param>
        /// <param name="offset"></param>
        /// <exception cref="Exception"></exception>
        public static int UpdateCompressedSize(ref FileStorageInformation info)
        {
            if (info.StorageType == EFileStorageType.UncompressedIndividual || info.StorageType == EFileStorageType.UncompressedBlob)
            {
                return -1;
            }

            int realSize = 0;
            using (var br = new BinaryReader(File.OpenRead(info.RealPath)))
            {
                br.BaseStream.Seek(info.RealOffset, SeekOrigin.Begin);
                realSize = Dat.GetCompressedFileSize(br, info.RealOffset);
            }

            using (var bw = new BinaryWriter(File.OpenWrite(info.RealPath)))
            {
                bw.BaseStream.Seek(info.RealOffset + 8, SeekOrigin.Begin);
                bw.Write(BitConverter.GetBytes(realSize));
            }

            info.FileSize = realSize;

            return realSize;

        }

        /// <summary>
        /// Validates that an offset points to an existing DAT file.
        /// </summary>
        /// <param name="df"></param>
        /// <param name="offset8x"></param>
        /// <returns></returns>
        public static bool IsOffsetSane(XivDataFile df, long offset8x, bool originalDatsOnly = false)
        {
            if(offset8x == 0 )
            {
                // A 0 Offset is considered a valid offset (Removed file)
                return true;
            }

            var parts = IOUtil.Offset8xToParts(offset8x);

            if (originalDatsOnly)
            {
                var maxDat = GetOriginalDatList(df).Count - 1;
                return parts.DatNum <= maxDat;
            }
            else
            {
                var maxDat = GetLargestDatNumber(df);
                return parts.DatNum <= maxDat;
            }
        }

        /// <summary>
        /// This is a very specific fixer-function designed to handle a very specific error caused by very old TexTools builds that would
        /// generate invalid compressed file sizes for texture files.
        /// 
        /// Returns true if the file was modified.
        /// </summary>
        /// <returns></returns>
        public static async Task<bool> UpdateType4UncompressedSize(string path, XivDataFile dataFile, long offset, ModTransaction tx = null, string sourceApplication = "Unknown")
        {

            var boiler = await TxBoiler.BeginWrite(tx);
            tx = boiler.Transaction;
            try
            {
                var reportedSize = await tx.GetCompressedFileSize(dataFile, offset);
                var data = await tx.ReadFile(dataFile, offset);
                var realSize = data.Length;

                if (reportedSize == realSize)
                {
                    await boiler.Cancel(true);
                    return false;
                }

                // Write the corrected size and save file.
                Array.Copy(BitConverter.GetBytes(realSize), 0, data, 8, sizeof(uint));
                await tx.WriteFile(path, data, sourceApplication);

                await boiler.Commit();
                return true;
            }
            catch (Exception ex)
            {
                await boiler.Catch();
                throw;
            }
        }

        #endregion


        #region Basic File Writing Assistance Functions (Pad, Zip Compression, etc.)
        /// <summary>
        /// Pads a given byte list to the target padding interval with empty bytes.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        /// <param name="paddingTarget"></param>
        /// <param name="forcePadding"></param>
        public static void Pad<T>(List<T> data, int paddingTarget, bool forcePadding = false)
        {
            var pad = paddingTarget - (data.Count % paddingTarget);
            if (pad == paddingTarget && !forcePadding)
            {
                return;
            }
            data.AddRange(new T[pad]);
        }

        /// <summary>
        /// Pads a given byte array to the target padding interval with empty bytes.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        /// <param name="paddingTarget"></param>
        /// <param name="forcePadding"></param>
        public static T[] PadArray<T>(T[] data, int paddingTarget, bool forcePadding = false)
        {
            var pad = paddingTarget - (data.Length % paddingTarget);
            if (pad == paddingTarget && !forcePadding)
            {
                return data;
            }
            var res = new T[data.Length + pad];
            data.CopyTo(res, 0);
            return res;
        }


        /// <summary>
        /// Pads a given int length to the next padding interval.
        /// </summary>
        /// <param name="size"></param>
        /// <param name="paddingTarget"></param>
        /// <returns></returns>
        public static int Pad(int size, int paddingTarget, bool forcePadding = false)
        {
            var pad = paddingTarget - (size % paddingTarget);
            if (pad == paddingTarget && !forcePadding)
            {
                return size;
            }
            return size + pad;
        }

        /// <summary>
        /// Pads a given int length to the next padding interval.
        /// </summary>
        /// <param name="size"></param>
        /// <param name="paddingTarget"></param>
        /// <returns></returns>
        public static long Pad(long size, long paddingTarget, bool forcePadding = false)
        {
            var pad = paddingTarget - (size % paddingTarget);
            if (pad == paddingTarget && !forcePadding)
            {
                return size;
            }
            return size + pad;
        }

        /// <summary>
        /// Compresses a single data block, returning the singular compressed byte array.
        /// For blocks larger than 16,000, use CompressData() instead.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private static async Task<byte[]> CompressSmallData(byte[] data)
        {
            if (data.Length > 16000)
            {
                throw new Exception("CompressSmallData() data is too large.");
            }

            // Vertex Info Compression
            var compressedData = await IOUtil.Compressor(data);

            var pad = 128 - ((compressedData.Length + 16) % 128);
            if (pad == 128)
            {
                pad = 0;
            }

            var paddedSize = 16 + compressedData.Length + pad;

            // Pre-allocate the array.
            List<byte> result = new List<byte>(paddedSize);
            result.AddRange(BitConverter.GetBytes(16));
            result.AddRange(BitConverter.GetBytes(0));
            result.AddRange(BitConverter.GetBytes(compressedData.Length));
            result.AddRange(BitConverter.GetBytes(data.Length));
            result.AddRange(compressedData);
            result.AddRange(new byte[pad]);

            return result.ToArray();
        }

        /// <summary>
        /// Zip-Compresses data, returning the compressed byte arrays in parts.
        /// Used in SqPacking files.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        internal static async Task<List<byte[]>> CompressData(List<byte> data)
        {
            var partCount = (int)Math.Ceiling(data.Count / 16000f);
            var partSizes = new List<int>(partCount);
            var remainingDataSize = data.Count;

            for (var i = 0; i < partCount; i++)
            {
                if (remainingDataSize >= 16000)
                {
                    partSizes.Add(16000);
                    remainingDataSize -= 16000;
                }
                else
                {
                    partSizes.Add(remainingDataSize);
                }
            }

            var parts = new List<byte[]>();

            var compressionTasks = new List<Task<byte[]>>();
            for (var i = 0; i < partCount; i++)
            {
                // Hand the compression task to the thread scheduler.
                var start = i * 16000;
                var size = partSizes[i];
                compressionTasks.Add(Task.Run(async () => {
                    return await CompressSmallData(data.GetRange(start, size).ToArray());
                }));
            }
            await Task.WhenAll(compressionTasks);

            foreach (var task in compressionTasks)
            {
                parts.Add(task.Result);
            }

            return parts;
        }

        /// <summary>
        /// Partially parses (without decompressing) the SQPack file in order to determine its total length.
        /// </summary>
        /// <param name="br"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        internal static int GetCompressedFileSize(BinaryReader br, long offset = -1)
        {
            if (offset >= 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            }
            else
            {
                offset = br.BaseStream.Position;
            }

            var headerLength = br.ReadInt32();
            var fileType = br.ReadInt32();
            var uncompSize = br.ReadInt32();
            var unknown = br.ReadInt32();
            var maxBufferSize = br.ReadInt32();
            var blockCount = br.ReadInt16();

            var endOfHeader = offset + headerLength;

            if (fileType != 2 && fileType != 3 && fileType != 4)
            {
                throw new NotSupportedException("Cannot get compressed file size of unknown type.");
            }

            int compSize = 0;

            // Ok, time to parse the block headers and figure out how long the compressed data runs.
            if (fileType == 2)
            {
                br.BaseStream.Seek(endOfHeader + 4, SeekOrigin.Begin);
                var lastSize = 0;
                var lastOffset = 0;
                for (int i = 0; i < blockCount; i++)
                {
                    br.BaseStream.Seek(offset + (24 + (8 * i)), SeekOrigin.Begin);
                    var blockOffset = br.ReadInt32();
                    var blockCompressedSize = br.ReadUInt16();

                    lastOffset = blockOffset;
                    lastSize = blockCompressedSize + 16;    // 16 bytes of header data per block.
                }

                // Pretty straight forward.  Header + Total size of the compressed data.
                compSize = headerLength + lastOffset + lastSize;

            }
            else if (fileType == 3)
            {

                // 24 byte header, then 88 bytes to the first chunk offset.
                br.BaseStream.Seek(offset + 112, SeekOrigin.Begin);
                var firstOffset = br.ReadInt32();

                // 24 byte header, then 178 bytes to the start of the block count.
                br.BaseStream.Seek(offset + 178, SeekOrigin.Begin);

                var totalBlocks = 0;
                for (var i = 0; i < 11; i++)
                {
                    // 11 Segments.  Vertex Info, Model Data, [Vertex Data x3], [Edge Data x3], [Index Data x3]
                    totalBlocks += br.ReadUInt16();
                }


                // 24 byte header, then 208 bytes to the list of block sizes.
                br.BaseStream.Seek(offset + 208, SeekOrigin.Begin);

                var blockSizes = new int[totalBlocks];
                for (var i = 0; i < totalBlocks; i++)
                {
                    blockSizes[i] = br.ReadUInt16();
                }

                int totalCompressedSize = 0;
                foreach (var size in blockSizes)
                {
                    totalCompressedSize += size;
                }


                // Header + Chunk headers + compressed data.
                compSize = headerLength + firstOffset + totalCompressedSize;
            }
            else if (fileType == 4)
            {
                br.BaseStream.Seek(endOfHeader + 4, SeekOrigin.Begin);
                // Textures.
                var lastOffset = 0;
                var lastSize = 0;
                var mipMapInfoOffset = offset + 24;
                for (int i = 0, j = 0; i < blockCount; i++)
                {
                    br.BaseStream.Seek(mipMapInfoOffset + j, SeekOrigin.Begin);

                    j = j + 20;

                    var offsetFromHeaderEnd = br.ReadInt32();
                    var mipMapCompressedSize = br.ReadInt32();


                    lastOffset = offsetFromHeaderEnd;
                    lastSize = mipMapCompressedSize;
                }

                // Pretty straight forward.  Header + Total size of the compressed data.
                compSize = headerLength + lastOffset + lastSize;

            }


            // Round out to the nearest 256 bytes.
            if (compSize % 256 != 0)
            {
                var padding = 256 - (compSize % 256);
                compSize += padding;
            }
            return compSize;

        }

        internal static string GetDatPath(XivDataFile dataFile, int datNumber)
        {
            var datPath = XivDataFiles.GetFullPath(dataFile, $"{Dat.DatExtension}{datNumber}");
            return datPath;
        }

        public static void Write3IntBuffer(List<byte> bufferTarget, uint[] dataToAdd)
        {
            bufferTarget.AddRange(BitConverter.GetBytes(dataToAdd[0]));
            bufferTarget.AddRange(BitConverter.GetBytes(dataToAdd[1]));
            bufferTarget.AddRange(BitConverter.GetBytes(dataToAdd[2]));
        }
        public static uint[] Read3IntBuffer(BinaryReader br, bool shortOnly = false)
        {
            uint[] data = new uint[3];
            if (shortOnly)
            {
                data[0] = br.ReadUInt16();
                data[1] = br.ReadUInt16();
                data[2] = br.ReadUInt16();
            }
            else
            {
                data[0] = br.ReadUInt32();
                data[1] = br.ReadUInt32();
                data[2] = br.ReadUInt32();
            }
            return data;
        }
        public static uint[] Read3IntBuffer(byte[] body, int offset)
        {
            uint[] data = new uint[3];
            data[0] = BitConverter.ToUInt32(body, offset);
            data[1] = BitConverter.ToUInt32(body, offset + 4);
            data[2] = BitConverter.ToUInt32(body, offset + 8);
            return data;
        }

        // Begins decompressing a sequence of blocks in parallel
        // Returns a list of tasks that should be passed to CompleteReadCompressedBlocks()
        public static List<Task<byte[]>> BeginReadCompressedBlocks(BinaryReader br, int blockCount, long offset = -1, bool lastInFile = false)
        {
            if (blockCount == 0)
            {
                return new();
            }

            if (offset > 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            }

            var tasks = new List<Task<byte[]>>();

            for (int i = 0; i < blockCount; i++)
            {
                byte[] data;

                var start = br.BaseStream.Position;

                // Some variety of magic numbers presumably?
                var sixTeen = br.ReadByte();

                // This is a shitty catch for old improperly spaced blocks generated by Endwalker and earlier TexTools.
                while (sixTeen != 16 && sixTeen == 0)
                {
                    sixTeen = br.ReadByte();
                }
                var zeros = br.ReadBytes(3);

                var zero = br.ReadInt32();

                if (sixTeen != 16 || zero != 0 || zeros.Any(x => x != 0))
                {
                    throw new Exception("Unable to locate valid compressed block header.");
                }

                // Relevant info.
                var partCompSize = br.ReadInt32();
                var partDecompSize = br.ReadInt32();

                Task<byte[]> task;

                void readBlockPadding()
                {
                    var end = br.BaseStream.Position;
                    var length = end - start;
                    var targetLength = Pad((int)length, 128);
                    var remaining = targetLength - length;

                    var paddingData = br.ReadBytes((int)remaining);

                    var sixTeenIndex = Array.IndexOf(paddingData, (byte)16);

                    // Ugh.  This is an old broken TexTools import that has improper block spacing.
                    // We have to rewind the stream to the start of the next block.
                    if (sixTeenIndex != -1)
                    {
                        var rewind = paddingData.Length - sixTeenIndex;
                        br.BaseStream.Position = br.BaseStream.Position - rewind;
                    }
                    else if (paddingData.Any(x => x != 0))
                    {
                        if (lastInFile && i != blockCount - 1)
                        {
                           throw new Exception("Unexpected real data in compressed data block padding section.");
                        }
                    }
                }

                if (partCompSize == 32000)
                {
                    data = br.ReadBytes(partDecompSize);
                    readBlockPadding();
                    var completedTask = new TaskCompletionSource<byte[]>();
                    completedTask.SetResult(data);
                    task = completedTask.Task;
                }
                else
                {
                    data = br.ReadBytes(partCompSize);
                    readBlockPadding();

                    // Not 100% sure this really needs to be shipped as Task.Run,
                    // but Task.Run should ensure that we actually get scheduled on the thread pool
                    // for potential new threads.
                    task = Task.Run(async () =>
                    {
                        return await IOUtil.Decompressor(data, partDecompSize);
                    });
                }

                tasks.Add(task);
            }

            return tasks;
        }

        // Completes all provided tasks from BeginReadCompressedBlocks and writes them sequentially in to destBuffer
        // Returns the number of bytes written in to destBuffer
        public static async Task<(int BytesWritten, byte[] Buffer)> CompleteReadCompressedBlocks(List<Task<byte[]>> tasks, byte[] destBuffer, int destOffset)
        {
            
            int currentOffset = destOffset;
            foreach (var task in tasks)
            {
                await task;
                var result = task.Result;

                if(currentOffset + result.Length >= destBuffer.Length)
                {
                    var newSize = currentOffset + result.Length;
                    var newArray = new byte[newSize];
                    Array.Copy(destBuffer, 0, newArray, 0, destBuffer.Length);
                    destBuffer = newArray;
                }
                result.CopyTo(destBuffer, currentOffset);
                currentOffset += result.Length;
            }
            var written = currentOffset - destOffset;
            return (written, destBuffer);
        }

        public static async Task<byte[]> ReadCompressedBlock(BinaryReader br, long offset = -1)
        {
            if (offset > 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            }

            var start = br.BaseStream.Position;


            byte[] data;

            // Some variety of magic numbers presumably?
            var sixTeen = br.ReadByte();

            // This is a shitty catch for old improperly spaced blocks generated by Endwalker and earlier TexTools.
            while (sixTeen != 16 && sixTeen == 0)
            {
                sixTeen = br.ReadByte();
            }
            var zeros = br.ReadBytes(3);

            var zero = br.ReadInt32();

            if (sixTeen != 16 || zero != 0 || zeros.Any(x => x != 0))
            {
                throw new Exception("Unable to locate valid compressed block header.");
            }

            // Relevant info.
            var partCompSize = br.ReadInt32();
            var partDecompSize = br.ReadInt32();

            if (partCompSize == 32000)
            {
                data = br.ReadBytes(partDecompSize);
            }
            else
            {
                data = await IOUtil.Decompressor(br.ReadBytes(partCompSize), partDecompSize);
            }

            var end = br.BaseStream.Position;
            var length = end - start;

            var target = Pad((int)length, 128);
            var remaining = target - length;

            var paddingData = br.ReadBytes((int)remaining);

            var sixTeenIndex = Array.IndexOf(paddingData, (byte)16);

            // Ugh.  This is an old broken TexTools import that has improper block spacing.
            // We have to rewind the stream to the start of the next block.
            if (sixTeenIndex != -1)
            {
                var rewind = paddingData.Length - sixTeenIndex;
                br.BaseStream.Position = br.BaseStream.Position - rewind;
            }
            else if (paddingData.Any(x => x != 0))
            {
                throw new Exception("Unexpected real data in compressed data block padding section.");
            }

            return data;
        }

        public static async Task<byte[]> ReadCompressedBlocks(BinaryReader br, int blockCount, long offset = -1)
        {
            if (blockCount == 0)
            {
                return new byte[0];
            }

            if (offset > 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            }

            var ret = (IEnumerable<byte>)new List<byte>();
            for (int i = 0; i < blockCount; i++)
            {
                var data = await ReadCompressedBlock(br);
                ret = ret.Concat(data);
            }
            return ret.ToArray();
        }

        #endregion
    }
}
