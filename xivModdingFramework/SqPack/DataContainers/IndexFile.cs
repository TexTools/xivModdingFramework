using HelixToolkit.SharpDX.Core.Core2D;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;

namespace xivModdingFramework.SqPack.DataContainers
{
    public enum IndexType
    {
        Invalid,
        Index1,
        Index2
    }

    /// <summary>
    /// A high level representation of an index file, capable of being
    /// re-serialzied into a valid index file automatically.
    /// </summary>
    public class IndexFile
    {
        public bool ReadOnlyMode
        {
            get; private set;
        }

        // Header bytes (1024 in length usually)
        private byte[] Index1Header;

        // Header bytes (1024 in length usually)
        private byte[] Index2Header;

        // Total size of the segment header block (usually 1024)
        private uint Index1TotalSegmentHeaderSize;

        // Total size of the segment header block (usually 1024)
        private uint Index2TotalSegmentHeaderSize;

        // The segment blocks we should copy/paste as-is into the file, which have unknown purpose.
        private List<byte[]> Index1ExtraSegments = new List<byte[]>();

        // The segment blocks we should copy/paste as-is into the file, which have unknown purpose.
        private List<byte[]> Index2ExtraSegments = new List<byte[]>();

        // The unknown ints in the segment headers.
        private List<int> Index1SegmentUnknowns = new List<int>();

        // The unknown ints in the segment headers.
        private List<int> Index2SegmentUnknowns = new List<int>();

        // Index1 entries.  Keyed by [Folder Hash, File Hash] => Entry
        private Dictionary<uint, Dictionary<uint, FileIndexEntry>> Index1Entries = new Dictionary<uint, Dictionary<uint, FileIndexEntry>>();

        // Index2 entries.  Keyed by [Full Hash] => Entry
        private Dictionary<uint, FileIndex2Entry> Index2Entries = new Dictionary<uint, FileIndex2Entry>();

        // The data file this Index file refers to.
        public readonly XivDataFile DataFile;

        public IndexFile(XivDataFile dataFile, byte[] index1Data, byte[] index2Data) : this(dataFile, new BinaryReader(new MemoryStream(index1Data)), new BinaryReader(new MemoryStream(index2Data)), true)
        {

        }

        /// <summary>
        /// Standard constructor.
        /// If disposeStreams is set to true, the binary streams will be disposed after use.
        /// </summary>
        public IndexFile(XivDataFile dataFile, BinaryReader index1Stream, BinaryReader index2Stream, bool disposeStreams = false)
        {
            DataFile = dataFile;

            ReadIndex1File(index1Stream);

            if (index2Stream != null)
            {
                ReadIndex2File(index2Stream);
                ReadOnlyMode = false;
            } else
            {
                ReadOnlyMode = true;
            }

            if(disposeStreams)
            {
                index1Stream.Dispose();
                if (index2Stream != null)
                {
                    index2Stream.Dispose();
                }
            }
        }

        public void Save(BinaryWriter index1Stream, BinaryWriter index2Stream, bool disposeStreams = false)
        {
            if (ReadOnlyMode) throw new InvalidDataException("Index Files loaded in Read Only Mode cannot be saved to file.");

            WriteIndex1File(index1Stream);
            WriteIndex2File(index2Stream);

            if (disposeStreams)
            {
                index1Stream.Dispose();
                index2Stream.Dispose();
            }
        }

        private void ReadIndex1File(BinaryReader stream)
        {
            stream.BaseStream.Seek(12, SeekOrigin.Begin);
            int headerSize = stream.ReadInt32();

            stream.BaseStream.Seek(headerSize, SeekOrigin.Begin);
            Index1TotalSegmentHeaderSize = stream.ReadUInt32();

            for (int segmentId = 0; segmentId < 4; segmentId++)
            {
                // For some reason segment 0 has 4 bytes more padding
                var offset = (segmentId * 72) + (headerSize + 4);
                if (segmentId > 0)
                {
                    offset += 4;
                }

                stream.BaseStream.Seek(offset, SeekOrigin.Begin);

                // 12 Bytes of metadata
                Index1SegmentUnknowns.Add(stream.ReadInt32());
                int segmentOffset = stream.ReadInt32();
                int segmentSize = stream.ReadInt32();

                // Next 20 bytes is the SHA-1 of the segment header.
                // (Don't need to read b/c we recalculat it on writing anyways)

                // Time to read the actual segment data.
                stream.BaseStream.Seek(segmentOffset, SeekOrigin.Begin);

                if (segmentId == 0)
                {
                    for (int x = 0; x < segmentSize; x += 16)
                    {
                        FileIndexEntry entry;
                        entry = new FileIndexEntry();

                        var bytes = stream.ReadBytes(16);
                        entry.SetBytes(bytes);

                        if (!Index1Entries.ContainsKey(entry.FolderPathHash))
                        {
                            Index1Entries.Add(entry.FolderPathHash, new Dictionary<uint, FileIndexEntry>());
                        }
                        if (!Index1Entries[entry.FolderPathHash].ContainsKey(entry.FileNameHash))
                        {
                            Index1Entries[entry.FolderPathHash].Add(entry.FileNameHash, entry);
                        } else
                        {
                            var z = "z";
                        }
                    }
                } else if(segmentId == 1 || segmentId == 2)
                {
                    // Segment 4 is regenerated when writing, so we don't need to store it.
                    Index1ExtraSegments.Add(stream.ReadBytes(segmentSize));
                }
            }

            // Copy the original header in so we have it for later.
            stream.BaseStream.Seek(0, SeekOrigin.Begin);
            var header = stream.ReadBytes(headerSize);

            Index1Header = header;
        }
        private void ReadIndex2File(BinaryReader stream)
        {
            stream.BaseStream.Seek(12, SeekOrigin.Begin);
            int headerSize = stream.ReadInt32();

            stream.BaseStream.Seek(headerSize, SeekOrigin.Begin);
            Index2TotalSegmentHeaderSize = stream.ReadUInt32();

            for (int segmentId = 0; segmentId < 4; segmentId++)
            {
                // For some reason segment 0 has 4 bytes more padding
                var offset = (segmentId * 72) + (headerSize + 4);
                if (segmentId > 0)
                {
                    offset += 4;
                }

                stream.BaseStream.Seek(offset, SeekOrigin.Begin);

                // 12 Bytes of metadata
                Index2SegmentUnknowns.Add(stream.ReadInt32());
                int segmentOffset = stream.ReadInt32();
                int segmentSize = stream.ReadInt32();

                // Next 20 bytes is the SHA-1 of the segment header.
                // (Don't need to read b/c we recalculat it on writing anyways)

                // Time to read the actual segment data.
                stream.BaseStream.Seek(segmentOffset, SeekOrigin.Begin);

                if (segmentId == 0)
                {
                    for (int x = 0; x < segmentSize; x += 8)
                    {
                        FileIndex2Entry entry;
                        entry = new FileIndex2Entry();

                        var bytes = stream.ReadBytes(8);
                        entry.SetBytes(bytes);

                        if (!Index2Entries.ContainsKey(entry.FullPathHash))
                        {
                            Index2Entries.Add(entry.FullPathHash, entry);
                        }
                    }
                }
                else if (segmentId == 1 || segmentId == 2 || segmentId == 3)
                {
                       Index2ExtraSegments.Add(stream.ReadBytes(segmentSize));
                }
            }

            // Copy the original header in so we have it for later.
            stream.BaseStream.Seek(0, SeekOrigin.Begin);
            var header = stream.ReadBytes(headerSize);

            Index2Header = header;
        }

        private void WriteIndex1File(BinaryWriter stream)
        {
            var sh = SHA1.Create();

            // First, we need to create the actual sorted lists of the file entries and folder entries.
            var fileListing = new List<FileIndexEntry>();
            var folderListing = new Dictionary<uint, FolderIndexEntry>();

            var sortedFolders = Index1Entries.Keys.OrderBy(x => x);
            var currentFileOffset = (uint)(Index1Header.Length + Index1TotalSegmentHeaderSize);
            foreach (var folderKey in sortedFolders)
            {
                folderListing.Add(folderKey, new FolderIndexEntry(folderKey, 0, 0));

                var sortedFiles = Index1Entries[folderKey].Keys.OrderBy(x => x);
                foreach (var fileKey in sortedFiles)
                {
                    var entry = Index1Entries[folderKey][fileKey];

                    // Don't include null files.
                    if (entry.DataOffset == 0) continue;

                    // Set the olfer start offset if we haven't yet.
                    if (folderListing[folderKey].IndexEntriesOffset == 0)
                    {
                        folderListing[folderKey].IndexEntriesOffset = currentFileOffset;
                    }

                    folderListing[folderKey].FileCount++;
                    if (entry.FileNameHash != fileKey || entry.FolderPathHash != folderKey)
                    {
                        throw new Exception("Attempted to save Index file with invalid structure.");
                    }
                    fileListing.Add(entry);

                    currentFileOffset += 16;
                }

                // Don't include empty folders.
                if(folderListing[folderKey].FileCount == 0)
                {
                    folderListing.Remove(folderKey);
                }

            }


            var totalSize = Index1Header.Length;
            totalSize += (int)Index1TotalSegmentHeaderSize;

            var fileSegmentSize = fileListing.Count * 16;
            var folderSegmentSize = folderListing.Count * 16;
            var otherSegmentSize = Index1ExtraSegments[0].Length + Index1ExtraSegments[1].Length;

            // Total size is Headers + segment sizes.
            totalSize += fileSegmentSize + folderSegmentSize + otherSegmentSize;



            // Calculate individual sizes.
            var segmentSizes = new List<int>()
            {
                fileListing.Count * 16,
                Index1ExtraSegments[0].Length,
                Index1ExtraSegments[1].Length,
                folderListing.Count * 16,
            };

            // Calculate offsets.
            var segmentOffsets = new List<int>();

            int offset = Index1Header.Length + (int)Index1TotalSegmentHeaderSize;
            for (int i = 0; i < 4; i++)
            {
                if (segmentSizes[i] != 0)
                {
                    segmentOffsets.Add(offset);
                } else
                {
                    segmentOffsets.Add((int)0);
                }
                offset += segmentSizes[i];
            }


            int[] SegmentShaOffsets = new int[4];
            byte[] SegmentHeaderBlock = new byte[Index1TotalSegmentHeaderSize];

            Array.Copy(BitConverter.GetBytes(Index1TotalSegmentHeaderSize), 0, SegmentHeaderBlock, 0, 4);

            offset = 4;
            for (int i = 0; i < 4; i++)
            {
                // Write headers
                Array.Copy(BitConverter.GetBytes(Index1SegmentUnknowns[i]), 0, SegmentHeaderBlock, offset, 4);
                Array.Copy(BitConverter.GetBytes(segmentOffsets[i]), 0, SegmentHeaderBlock, offset + 4, 4);
                Array.Copy(BitConverter.GetBytes(segmentSizes[i]), 0, SegmentHeaderBlock, offset  + 8, 4);

                SegmentShaOffsets[i] = offset + 12;

                offset += (i == 0) ? 76 : 72;
            }

            List<byte[]> SegmentData = new List<byte[]>();


            // Write the actual segment data.
            for(int i = 0; i < 4; i++)
            {
                offset = 0;
                var len = segmentSizes[i];
                var data = new byte[len];
                SegmentData.Add(data);

                if(i == 0)
                {
                    // File listing.
                    foreach(var file in fileListing)
                    {
                        Array.Copy(file.GetBytes(), 0, data, offset, 16);
                        offset += 16;
                    }

                } else if( i == 1 || i == 2)
                {
                    var d = Index1ExtraSegments[i - 1];
                    Array.Copy(d, 0, data, 0, len);
                    
                } else if(i == 3)
                {
                    // Folder listing.
                    foreach (var kv in folderListing)
                    {
                        Array.Copy(kv.Value.GetBytes(), 0, data, offset, 16);
                        offset += 16;
                    }
                }

                // Calculate the hash of the resultant data and write it into the header.
                var sha = sh.ComputeHash(data, 0, data.Length);
                Array.Copy(sha, 0, SegmentHeaderBlock, SegmentShaOffsets[i], sha.Length);
            }

            // Calculate SHA for the segment headers.
            var segmentHeaderSha = sh.ComputeHash(SegmentHeaderBlock, 0, SegmentHeaderBlock.Length - 64);
            Array.Copy(segmentHeaderSha, 0, SegmentHeaderBlock, SegmentHeaderBlock.Length - 64, segmentHeaderSha.Length);


            // Write the final fully composed data blocks together.
            stream.Seek(0, SeekOrigin.Begin);
            stream.Write(Index1Header);
            stream.Write(SegmentHeaderBlock);
            for(int i = 0; i < 4; i++)
            {
                stream.Write(SegmentData[i]);
            }
        }
        private void WriteIndex2File(BinaryWriter stream)
        {
            var sh = SHA1.Create();

            // First, we need to create the actual sorted lists of the file entries and folder entries.
            var fileListing = new List<FileIndex2Entry>();

            var sortedFiles = Index2Entries.Select(x => x.Value).OrderBy(x => x.FullPathHash);
            fileListing = sortedFiles.ToList();


            var totalSize = Index2Header.Length;
            totalSize += (int)Index2TotalSegmentHeaderSize;

            var fileSegmentSize = fileListing.Count * 8;
            var otherSegmentSize = Index2ExtraSegments[0].Length + Index2ExtraSegments[1].Length + Index2ExtraSegments[2].Length;

            // Total size is Headers + segment sizes.
            totalSize += fileSegmentSize + otherSegmentSize;



            // Calculate individual sizes.
            var segmentSizes = new List<int>()
            {
                fileListing.Count * 8,
                Index2ExtraSegments[0].Length,
                Index2ExtraSegments[1].Length,
                Index2ExtraSegments[2].Length,
            };

            // Calculate offsets.
            var segmentOffsets = new List<int>();

            int offset = Index2Header.Length + (int)Index2TotalSegmentHeaderSize;
            for (int i = 0; i < 4; i++)
            {
                if (segmentSizes[i] != 0)
                {
                    segmentOffsets.Add(offset);
                }
                else
                {
                    segmentOffsets.Add((int)0);
                }
                offset += segmentSizes[i];
            }


            int[] SegmentShaOffsets = new int[4];
            byte[] SegmentHeaderBlock = new byte[Index2TotalSegmentHeaderSize];

            Array.Copy(BitConverter.GetBytes(Index2TotalSegmentHeaderSize), 0, SegmentHeaderBlock, 0, 4);

            offset = 4;
            for (int i = 0; i < 4; i++)
            {
                // Write headers
                Array.Copy(BitConverter.GetBytes(Index2SegmentUnknowns[i]), 0, SegmentHeaderBlock, offset, 4);
                Array.Copy(BitConverter.GetBytes(segmentOffsets[i]), 0, SegmentHeaderBlock, offset + 4, 4);
                Array.Copy(BitConverter.GetBytes(segmentSizes[i]), 0, SegmentHeaderBlock, offset + 8, 4);

                SegmentShaOffsets[i] = offset + 12;

                offset += (i == 0) ? 76 : 72;
            }

            // Index 2 files have a random 2 here in the middle of the padding.
            // Omitting it doesn't seem to actually do anything, but might as well replicate.
            SegmentHeaderBlock[300] = 2;

            List<byte[]> SegmentData = new List<byte[]>();


            // Write the actual segment data.
            for (int i = 0; i < 4; i++)
            {
                offset = 0;
                var len = segmentSizes[i];
                var data = new byte[len];
                SegmentData.Add(data);

                if (i == 0)
                {
                    // File listing.
                    foreach (var file in fileListing)
                    {
                        Array.Copy(file.GetBytes(), 0, data, offset, 8);
                        offset += 8;
                    }

                }
                else if (i == 1 || i == 2 || i == 3)
                {
                    var d = Index2ExtraSegments[i - 1];
                    Array.Copy(d, 0, data, 0, len);

                }

                // Calculate the hash of the resultant data and write it into the header.
                var sha = sh.ComputeHash(data, 0, data.Length);
                Array.Copy(sha, 0, SegmentHeaderBlock, SegmentShaOffsets[i], sha.Length);
            }

            // Calculate SHA for the segment headers.
            var segmentHeaderSha = sh.ComputeHash(SegmentHeaderBlock, 0, SegmentHeaderBlock.Length - 64);
            Array.Copy(segmentHeaderSha, 0, SegmentHeaderBlock, SegmentHeaderBlock.Length - 64, segmentHeaderSha.Length);


            // Write the final fully composed data blocks together.
            stream.Seek(0, SeekOrigin.Begin);
            stream.Write(Index2Header);
            stream.Write(SegmentHeaderBlock);
            for (int i = 0; i < 4; i++)
            {
                stream.Write(SegmentData[i]);
            }
        }


        /// <summary>
        /// Gets the raw uint data offset from the index file, with DatNumber embeded.
        /// Or 0 if the file does not exist.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public uint GetRawDataOffset(string filePath)
        {

            var fileName = Path.GetFileName(filePath);
            var folderName = Path.GetDirectoryName(filePath)?.Replace("\\", "/") ?? string.Empty;
            var fileHash = (uint) HashGenerator.GetHash(fileName);
            var folderHash = (uint) HashGenerator.GetHash(folderName);

            if (Index1Entries.ContainsKey(folderHash) && Index1Entries[folderHash].ContainsKey(fileHash))
            {
                var entry = Index1Entries[folderHash][fileHash];
                return entry.RawOffset;
            }

            // BENCHMARK ONLY -- Fallback for Index 2 Reads?
            var fullHash = (uint) HashGenerator.GetHash(filePath);
            if(Index2Entries.ContainsKey(fullHash))
            {
                return Index2Entries[fullHash].RawFileOffset;
            }

            return 0;
        }

        /// <summary>
        /// Gets the 8x multiplied data offset from the index file, with DatNumber embeded.
        /// Or 0 if the file does not exist.  
        /// This is primarily useful for legacy functionality.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public long Get8xDataOffset(string filePath)
        {
            return ((long) GetRawDataOffset(filePath)) * 8L;
        }

        /// <summary>
        /// Gets the Index2 data offset in the form of (datNumber, offset Within File)
        /// Or (0, 0) if the file does not exist.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public uint GetRawDataOffsetIndex2(string filePath)
        {

            var fullHash = (uint)HashGenerator.GetHash(filePath);

            if (Index2Entries.ContainsKey(fullHash))
            {
                var entry = Index2Entries[fullHash];
                return entry.RawFileOffset;
            }

            return 0;
        }

        /// <summary>
        /// Gets the 8x multiplied Index2 data offset from the index file, with DatNumber embeded.
        /// Or 0 if the file does not exist.  
        /// This is primarily useful for legacy functionality.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public long Get8xDataOffsetIndex2(string filePath)
        {
            return ((long)GetRawDataOffsetIndex2(filePath)) * 8L;
        }
        public (uint DatNumber, long DataOffset) GetDataOffsetComplete(string filePath)
        {
            var raw = GetRawDataOffset(filePath);
            var datNum = (uint)((raw & 0x0F) / 2);

            // Multiply by 8 to get us in the right frame.
            long longOffset = ((long)raw) * 8;
                
            // And lop off the last 7 bits.
            var longWithout = (longOffset / 128) * 128;

            return (datNum, longWithout);
        }


        /// <summary>
        /// Update the data offset for a given file, adding the file if needed.
        /// Returns the previous Raw Data Offset (with dat Number Embedded), or 0 if the file did not exist.
        /// 
        /// Setting a value of 0 or negative for the offset will remove the file pointer.
        /// </summary>
        public uint SetDataOffset(string filePath, long new8xOffset, uint datNumber)
        {

            if (new8xOffset <= 0)
            {
                return SetDataOffset(filePath, 0);
            }
            else
            {
                uint val = (uint)(new8xOffset / 8);
                return SetDataOffset(filePath, val, datNumber);
            }
        }

        /// <summary>
        /// Update the data offset for a given file, adding the file if needed.
        /// Returns the previous Raw Data Offset (with dat Number Embedded), or 0 if the file did not exist.
        /// 
        /// Setting a value of 0 for the offset will remove the file pointer.
        /// </summary>
        public uint SetDataOffset(string filePath, uint newRawOffset, uint datNumber)
        {
            if(newRawOffset % 8 != 0)
            {
                throw new InvalidDataException("Provided offset is not a valid dat-less offset.");
            }

            if (newRawOffset == 0)
            {
                return SetDataOffset(filePath, 0);
            }
            else
            {
                byte bits = (byte)(datNumber * 2);
                var modulatedOffset = newRawOffset | bits;
                return SetDataOffset(filePath, modulatedOffset);
            }
        }

        /// <summary>
        /// Update the data offset for a given file, adding the file if needed.
        /// Returns the previous Raw Data Offset (with dat Number Embedded), or 0 if the file did not exist.
        /// 
        /// Setting a value of 0 or negative for the offset will remove the file pointer.
        /// </summary>
        public uint SetDataOffset(string filePath, long new8xOffsetWithDatNumEmbed)
        {
            if (new8xOffsetWithDatNumEmbed <= 0)
            {
                return SetDataOffset(filePath, 0);
            }
            else
            {
                uint val = (uint)(new8xOffsetWithDatNumEmbed / 8);
                return SetDataOffset(filePath, val);
            }
        }

        /// <summary>
        /// Update the data offset for a given file, adding the file if needed.
        /// Returns the previous Raw Data Offset (with dat Number Embedded), or 0 if the file did not exist.
        /// 
        /// Setting a value of 0 for the offset will remove the file pointer.
        /// </summary>
        public uint SetDataOffset(string filePath, uint newRawOffsetWithDatNumEmbed)
        {
            var fileName = Path.GetFileName(filePath);
            var folderName = filePath.Substring(0, filePath.LastIndexOf('/'));
            var fileHash = (uint)HashGenerator.GetHash(fileName);
            var folderHash = (uint)HashGenerator.GetHash(folderName);
            var fullHash = (uint)HashGenerator.GetHash(filePath);


            if (!Index1Entries.ContainsKey(folderHash))
            {
                Index1Entries.Add(folderHash, new Dictionary<uint, FileIndexEntry>());
            }

            uint originalOffset = 0;
            if (!Index1Entries[folderHash].ContainsKey(fileHash)) {

                if (newRawOffsetWithDatNumEmbed != 0)
                {
                    var entry = new FileIndexEntry(newRawOffsetWithDatNumEmbed, fileHash, folderHash);
                    Index1Entries[folderHash].Add(fileHash, entry);
                }
            } else
            {
                if (newRawOffsetWithDatNumEmbed == 0)
                {
                    Index1Entries[folderHash].Remove(fileHash);
                }
                else
                {
                    originalOffset = Index1Entries[folderHash][fileHash].RawOffset;
                    Index1Entries[folderHash][fileHash].RawOffset = newRawOffsetWithDatNumEmbed;
                }
            }

            if(!Index2Entries.ContainsKey(fullHash))
            {

                if (newRawOffsetWithDatNumEmbed != 0)
                {
                    var entry = new FileIndex2Entry(fullHash, newRawOffsetWithDatNumEmbed);
                    Index2Entries.Add(fullHash, entry);
                }
            } else
            {
                if (newRawOffsetWithDatNumEmbed == 0)
                {
                    Index2Entries.Remove(fullHash);
                } else { 
                    Index2Entries[fullHash].RawFileOffset = newRawOffsetWithDatNumEmbed;
                }
            }

            return originalOffset;
        }


        /// <summary>
        /// Returns the raw index entries contained in a specific folder.
        /// -- WARNING -- These are the raw index entries, modifications to them will result 
        /// in modifying the underlying index file!
        /// </summary>
        /// <param name="folderHash"></param>
        /// <returns></returns>
        public List<FileIndexEntry> GetEntriesInFolder(uint folderHash)
        {
            if (!Index1Entries.ContainsKey(folderHash)) return new List<FileIndexEntry>();
            return Index1Entries[folderHash].Values.ToList();
        }


        /// <summary>
        /// Retrieves the entire universe of folder => file hashes in the index.
        /// </summary>
        /// <returns></returns>
        public Dictionary<uint, HashSet<uint>> GetAllHashes()
        {
            var result = new Dictionary<uint, HashSet<uint>>();
            foreach(var folderKv in Index1Entries)
            {
                result.Add(folderKv.Key, new HashSet<uint>());
                foreach(var fileKv in folderKv.Value)
                {
                    result[folderKv.Key].Add(fileKv.Key);
                }
            }
            return result;
        }


        public bool FileExists(string fullPath)
        {
            var fileName = Path.GetFileName(fullPath);
            var folderName = fullPath.Substring(0, fullPath.LastIndexOf('/'));
            var fileHash = (uint)HashGenerator.GetHash(fileName);
            var folderHash = (uint)HashGenerator.GetHash(folderName);

            if(Index1Entries.ContainsKey(folderHash) && Index1Entries[folderHash].ContainsKey(fileHash))
            {
                var entry = Index1Entries[folderHash][fileHash];
                return entry.RawOffset != 0;
            }
            return false;
        }

        public bool FolderExists(uint folderHash)
        {
            if (Index1Entries.ContainsKey(folderHash) && Index1Entries[folderHash].Count != 0)
            {
                return true;
            }
            return false;
        }
        public bool FolderExists(string folderPath)
        {
            var folderHash = (uint)HashGenerator.GetHash(folderPath);
            if (Index1Entries.ContainsKey(folderHash) && Index1Entries[folderHash].Count != 0)
            {
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Class to represent a single entry in an index segment.
    /// </summary>
    public abstract class IndexEntry : IComparable
    {
        public abstract byte[] GetBytes();
        public abstract void SetBytes(byte[] b);
        public abstract int CompareTo(object obj);
    }

    public class FileIndex2Entry : IndexEntry
    {
        private uint _fullPathHash;
        private uint _fileOffset;

        public uint FullPathHash
        {
            get
            {
                return _fullPathHash;
            }
        }

        public uint RawFileOffset
        {
            get
            {
                return _fileOffset;
            }
            set
            {
                _fileOffset = value;
            }
        }

        public FileIndex2Entry()
        {

        }

        public FileIndex2Entry(uint fullPathHash, uint dataOffset)
        {
            _fullPathHash = fullPathHash;
            _fileOffset = dataOffset;
        }

        /// <summary>
        /// Base data offset * 8.  Includes DAT number reference information still.
        /// </summary>
        public long ModifiedOffset
        {
            get { return ((long)_fileOffset) * 8; }
        }

        /// <summary>
        /// Dat Number this file's data resides in.
        /// </summary>
        public int DatNum
        {
            get
            {
                return ((int)(_fileOffset & 0x0F) / 2);
            }
        }

        /// <summary>
        /// Data offset within the containing Data File.
        /// </summary>
        public long DataOffset
        {
            get
            {
                return (ModifiedOffset / 128) * 128;
            }
        }

        public override byte[] GetBytes()
        {
            byte[] b = new byte[8];
            IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(_fullPathHash), 0);
            IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(_fileOffset), 4);

            return b;
        }

        public override void SetBytes(byte[] b)
        {
            _fullPathHash = BitConverter.ToUInt32(b, 0);
            _fileOffset = BitConverter.ToUInt32(b, 4);
        }


        public override int CompareTo(object obj)
        {
            if (obj == null) return 1;
            if (obj.GetType() != typeof(FileIndex2Entry)) throw new Exception("Invalid Index Data Comparison");

            var other = (FileIndex2Entry)obj;
            var mine = (uint)_fullPathHash;
            var theirs = (uint)other._fullPathHash;

            if (mine == theirs) return 0;
            if (mine < theirs) return -1;
            return 1;
        }
    }

    /// <summary>
    /// File type index entry.
    /// </summary>
    public class FileIndexEntry : IndexEntry
    {
        private uint _fileOffset;
        private uint _fileNameHash;
        private uint _folderPathHash;

        public FileIndexEntry()
        {

        }
        public FileIndexEntry(uint fileOffset, uint fileNameHash, uint folderHash)
        {
            _fileOffset = fileOffset;
            _fileNameHash = fileNameHash;
            _folderPathHash = folderHash;
        }

        public uint FileNameHash {
            get
            {
                return _fileNameHash;
            }
        }

        public uint FolderPathHash
        {
            get
            {
                return _folderPathHash;
            }
        }

        /// <summary>
        /// Base data offset * 8.  Includes DAT number reference information still.
        /// </summary>
        public long ModifiedOffset
        {
            get { return ((long)_fileOffset) * 8; }
        }

        /// <summary>
        /// Dat Number this file's data resides in.
        /// </summary>
        public uint DatNum
        {
            get
            {
                return ((_fileOffset & 0x0F) / 2);
            }
        }

        /// <summary>
        /// Data offset within the containing Data File.
        /// </summary>
        public long DataOffset
        {
            get
            {
                return (ModifiedOffset / 128) * 128;
            }
        }

        public uint RawOffset
        {
            get
            {
                return _fileOffset;
            }
            set
            {
                _fileOffset = value;
            }
        }


        public override byte[] GetBytes()
        {
            byte[] b = new byte[16];
            IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(_fileNameHash), 0);
            IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(_folderPathHash), 4);
            IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(_fileOffset), 8);

            return b;
        }

        public override void SetBytes(byte[] b)
        {
            _fileNameHash = BitConverter.ToUInt32(b, 0);
            _folderPathHash = BitConverter.ToUInt32(b, 4);
            _fileOffset = BitConverter.ToUInt32(b, 8);
        }


        public override int CompareTo(object obj)
        {
            if (obj == null) return 1;
            if (obj.GetType() != typeof(FileIndexEntry)) throw new Exception("Invalid Index Data Comparison");

            var other = (FileIndexEntry)obj;
            if (_folderPathHash != other._folderPathHash)
            {
                var mine = (uint)_folderPathHash;
                var theirs = (uint)other._folderPathHash;

                if (mine < theirs) return -1;
                return 1;
            }
            else
            {
                var mine = (uint)_fileNameHash;
                var theirs = (uint)other._fileNameHash;

                if (mine == theirs) return 0;
                if (mine < theirs) return -1;
                return 1;
            }
        }
    }


    /// <summary>
    /// Folder type index entry.
    /// </summary>
    public class FolderIndexEntry : IndexEntry
    {
        private uint _folderPathHash;
        private uint _indexEntriesOffset;
        private uint _totalFolderSize;

        public uint FileCount
        {
            get
            {
                return _totalFolderSize / 16;
            } set
            {
                _totalFolderSize = value * 16;
            }
        }

        public uint IndexEntriesOffset
        {
            get
            {
                return _indexEntriesOffset;
            }
            set
            {
                _indexEntriesOffset = value;
            }
        } 

        public FolderIndexEntry()
        {

        }

        public FolderIndexEntry(uint folderHash, uint entriesOffset, uint totalSize)
        {
            _folderPathHash = folderHash;
            IndexEntriesOffset = entriesOffset;
            _totalFolderSize = totalSize;
        }

        public override byte[] GetBytes()
        {
            byte[] b = new byte[16];
            IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(_folderPathHash), 0);
            IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(IndexEntriesOffset), 4);
            IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(_totalFolderSize), 8);

            return b;
        }

        public override void SetBytes(byte[] b)
        {
            _folderPathHash = BitConverter.ToUInt32(b, 0);
            IndexEntriesOffset = BitConverter.ToUInt32(b, 4);
            _totalFolderSize = BitConverter.ToUInt32(b, 8);
        }


        public override int CompareTo(object obj)
        {
            if (obj == null) return 1;
            if (obj.GetType() != typeof(FolderIndexEntry)) throw new Exception("Invalid Index Data Comparison");

            var other = (FolderIndexEntry)obj;

            var mine = (uint)_folderPathHash;
            var theirs = (uint)other._folderPathHash;

            if (mine == theirs) return 0;
            if (mine < theirs) return -1;
            return 1;
        }
    }


    /// <summary>
    /// Raw unsortable type index entry.
    /// </summary>
    public class RawIndexEntry : IndexEntry
    {
        private byte[] bytes;
        public override byte[] GetBytes()
        {
            return bytes;
        }

        public override void SetBytes(byte[] b)
        {
            bytes = b;
        }


        public override int CompareTo(object obj)
        {
            if (obj == null) return 1;
            if (obj.GetType() != typeof(RawIndexEntry)) throw new Exception("Invalid Index Data Comparison");

            return 0;
        }
    }


}
