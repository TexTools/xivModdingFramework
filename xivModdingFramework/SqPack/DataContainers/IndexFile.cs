using HelixToolkit.SharpDX.Core.Core2D;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.SqPack.FileTypes;

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

        // Total size of the segment header block (usually 1024)
        protected const uint _SqPackHeaderSize = 1024;
        protected const uint _IndexHeaderSize = 1024;

        protected List<byte[]> _SqPackHeader = new List<byte[]>((int)_SqPackHeaderSize);

        // Version, typically just 1.
        protected List<uint> IndexVersion = new List<uint>();

        // Index type, either 0 or 2.
        protected List<uint> IndexType = new List<uint>();

        protected List<byte[]> SynonymTable = new List<byte[]>();

        protected List<byte[]> EmptyBlock = new List<byte[]>();

        // We regenerate the directory list manually, so we don't save it.
        //protected List<byte[]> DirBlock = new List<byte[]>();


        // Index1 entries.  Keyed by [Folder Hash, File Hash] => Entry
        protected Dictionary<uint, Dictionary<uint, FileIndexEntry>> Index1Entries = new Dictionary<uint, Dictionary<uint, FileIndexEntry>>();

        // Index2 entries.  Keyed by [Full Hash] => Entry
        protected Dictionary<uint, FileIndex2Entry> Index2Entries = new Dictionary<uint, FileIndex2Entry>();

        // The data file this Index file refers to.
        public readonly XivDataFile DataFile;


        /// <summary>
        /// Standard constructor.
        /// </summary>
        public IndexFile(XivDataFile dataFile, BinaryReader index1Stream, BinaryReader index2Stream, bool readOnly = true)
        {
            DataFile = dataFile;
            ReadIndexFile(index1Stream, 0);
            ReadIndexFile(index2Stream, 1);
        }

        public virtual void Save() {

            var dir = XivCache.GameInfo.GameDirectory;
            var index1Path = Path.Combine(dir.FullName, $"{DataFile.GetDataFileName()}{FileTypes.Index.IndexExtension}");
            var index2Path = Path.Combine(dir.FullName, $"{DataFile.GetDataFileName()}{FileTypes.Index.Index2Extension}");
            using (var index1Stream = new BinaryWriter(File.OpenWrite(index1Path)))
            {
                using (var index2Stream = new BinaryWriter(File.OpenWrite(index2Path)))
                {
                    Save(index1Stream, index2Stream);
                }
            }
        }

        public virtual void Save(BinaryWriter index1Stream, BinaryWriter index2Stream)
        {
            if (ReadOnlyMode) throw new InvalidDataException("Index Files loaded in Read Only Mode cannot be saved to file.");

            try
            {
                WriteIndexFile(index1Stream, 0);
                WriteIndexFile(index2Stream, 1);
            } catch (Exception ex)
            {
                throw;
            }
        }

        protected virtual void ReadIndexFile(BinaryReader br, int indexId = 0)
        {
            // Store the SqPack header for writing back later, though it's mostly just empty data.
            _SqPackHeader.Add(br.ReadBytes(32));
            br.BaseStream.Seek(_SqPackHeaderSize, SeekOrigin.Begin);

            var headerSize = br.ReadUInt32();
            if(headerSize != _IndexHeaderSize)
            {
                throw new Exception("Invalid index or index file format changed.");
            }

            IndexVersion.Add(br.ReadUInt32());

            if (indexId == 0)
            {
                ReadIndex1Data(br);
            } else
            {
                ReadIndex2Data(br);
            }

            // Don't need to store this since we regenerate it.
            var dataFileCount = br.ReadUInt32();

            SynonymTable.Add(ReadSegment(br));
            EmptyBlock.Add(ReadSegment(br));

            // We don't actually care about the directory data, since we regenerate it automatically.
            var directoryData = ReadSegment(br);

            IndexType.Add(br.ReadUInt32());

            // Rest of the file is padding and self-hash.
            Debug.Write("asdf");
        }
        /// <summary>
        /// Reads the index offset data in index1 format mode.
        /// </summary>
        /// <param name="br"></param>
        protected virtual void ReadIndex1Data(BinaryReader br)
        {
            int segmentOffset = br.ReadInt32();
            int segmentSize = br.ReadInt32();
            var storedOffset = br.BaseStream.Position;

            br.BaseStream.Seek(segmentOffset, SeekOrigin.Begin);
            for (int x = 0; x < segmentSize; x += 16)
            {
                FileIndexEntry entry;
                entry = new FileIndexEntry();

                var bytes = br.ReadBytes(16);
                entry.SetBytes(bytes);

                if (!Index1Entries.ContainsKey(entry.FolderPathHash))
                {
                    Index1Entries.Add(entry.FolderPathHash, new Dictionary<uint, FileIndexEntry>());
                }
                if (!Index1Entries[entry.FolderPathHash].ContainsKey(entry.FileNameHash))
                {
                    Index1Entries[entry.FolderPathHash].Add(entry.FileNameHash, entry);
                }
            }

            // Skip past the hash.
            br.BaseStream.Seek(storedOffset + 64, SeekOrigin.Begin);
        }

        /// <summary>
        /// Reads the index offset data in index2 format mode.
        /// </summary>
        /// <param name="br"></param>
        protected virtual void ReadIndex2Data(BinaryReader br)
        {
            int segmentOffset = br.ReadInt32();
            int segmentSize = br.ReadInt32();
            var storedOffset = br.BaseStream.Position;

            br.BaseStream.Seek(segmentOffset, SeekOrigin.Begin);
            for (int x = 0; x < segmentSize; x += 8)
            {
                FileIndex2Entry entry;
                entry = new FileIndex2Entry();

                var bytes = br.ReadBytes(8);
                entry.SetBytes(bytes);

                if (!Index2Entries.ContainsKey(entry.FullPathHash))
                {
                    Index2Entries.Add(entry.FullPathHash, entry);
                }
            }

            // Skip past the hash.
            br.BaseStream.Seek(storedOffset + 64, SeekOrigin.Begin);
        }


        protected virtual byte[] ReadSegment(BinaryReader br)
        {
            int segmentOffset = br.ReadInt32();
            int segmentSize = br.ReadInt32();
            var storedOffset = br.BaseStream.Position;
            br.BaseStream.Seek(segmentOffset, SeekOrigin.Begin);
            var data = br.ReadBytes(segmentSize);

            // Skip past the hash.
            br.BaseStream.Seek(storedOffset + 64, SeekOrigin.Begin);
            return data;
        }

        protected virtual void WriteIndexFile(BinaryWriter stream, int indexId)
        {
            var _dat = new Dat(XivCache.GameInfo.GameDirectory);
            var datCount = _dat.GetLargestDatNumber(DataFile) + 1;

            var sh = SHA1.Create();

            // First, we need to create the actual sorted lists of the file entries and folder entries.
            var fileListing = new List<IndexEntry>();
            var folderListing = new Dictionary<uint, FolderIndexEntry>();

            var currentFileOffset = (uint)(_SqPackHeaderSize + _IndexHeaderSize);

            if (indexId == 0)
            {
                var sortedFolders = Index1Entries.Keys.OrderBy(x => x);
                // Index 1 has to be sorted by folder.
                foreach (var folderKey in sortedFolders)
                {
                    folderListing.Add(folderKey, new FolderIndexEntry(folderKey, 0, 0));

                    var sortedFiles = Index1Entries[folderKey].Keys.OrderBy(x => x);
                    foreach (var fileKey in sortedFiles)
                    {
                        var entry = Index1Entries[folderKey][fileKey];

                        // Don't include null files.
                        if (entry.DataOffset == 0) continue;

                        // Set the folder start offset if we haven't yet.
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
                    if (folderListing[folderKey].FileCount == 0)
                    {
                        folderListing.Remove(folderKey);
                    }

                }
            } else
            {
                // Index 2 is just sorted by hash
                fileListing.AddRange(Index2Entries.Select(x => x.Value).OrderBy(x => x.FullPathHash));
            }


            // File listing.
            var fileSegmentSize = fileListing.Count * (indexId == 0 ? 16 : 8);
            var fileSegment = new byte[fileSegmentSize];
            var offset = 0;
            foreach (var file in fileListing)
            {
                var bytes = file.GetBytes();
                Array.Copy(bytes, 0, fileSegment, offset, bytes.Length);
                offset += bytes.Length;
            }

            // Folder listing.
            var folderSegmentSize = folderListing.Count * 16;
            var folderSegment = new byte[folderSegmentSize];
            offset = 0;
            foreach (var kv in folderListing)
            {
                Array.Copy(kv.Value.GetBytes(), 0, folderSegment, offset, 16);
                offset += 16;
            }


            var synTableSize = SynonymTable[indexId].Length;
            var emptyBlockSize = EmptyBlock[indexId].Length;

            // Total size is Headers + segment sizes.
            var totalSize = (int)(_SqPackHeaderSize + _IndexHeaderSize) + fileSegmentSize + folderSegmentSize + emptyBlockSize + synTableSize;

            // Calculate offsets.
            var segmentOffsets = new List<int>();

            var fileSegmentOffset = offset;
            var synTableOffset = fileSegmentOffset + fileSegmentSize;
            var emptyBlockOffset = synTableOffset + synTableSize;
            var folderSegmentOffset = emptyBlockOffset + emptyBlockSize;


            var indexHeaderBlock = new byte[_IndexHeaderSize];
            using(var ms = new MemoryStream(indexHeaderBlock))
            {
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write(BitConverter.GetBytes(_IndexHeaderSize));
                    bw.Write(BitConverter.GetBytes(IndexVersion[indexId]));

                    // Index Data Segment
                    bw.Write(BitConverter.GetBytes(fileSegmentOffset));
                    bw.Write(BitConverter.GetBytes(fileSegmentSize));
                    Write64ByteHash(bw, sh.ComputeHash(fileSegment));

                    // Dat Count - Why is it here in the middle of the other data? Who knows.
                    bw.Write(BitConverter.GetBytes(datCount));

                    // Synonym Table Data Segment
                    bw.Write(BitConverter.GetBytes(synTableOffset));
                    bw.Write(BitConverter.GetBytes(synTableSize));
                    Write64ByteHash(bw, sh.ComputeHash(SynonymTable[indexId]));

                    // Empty Block Data Segment
                    bw.Write(BitConverter.GetBytes(emptyBlockOffset));
                    bw.Write(BitConverter.GetBytes(emptyBlockSize));
                    Write64ByteHash(bw, sh.ComputeHash(EmptyBlock[indexId]));

                    // Folder Data Segment
                    bw.Write(BitConverter.GetBytes(folderSegmentOffset));
                    bw.Write(BitConverter.GetBytes(folderSegmentSize));
                    Write64ByteHash(bw, sh.ComputeHash(folderSegment));

                    // Index Type
                    bw.Write(BitConverter.GetBytes(indexId == 0 ? 0 : 2));

                    // Pad until end, minus the self-hash.
                    var diff = _IndexHeaderSize - bw.BaseStream.Position - 64;
                    bw.Write(new byte[diff]);
                }
            }

            var headerHash = sh.ComputeHash(indexHeaderBlock, 0, indexHeaderBlock.Length - 64);
            Array.Copy(headerHash, 0, indexHeaderBlock, indexHeaderBlock.Length - 64, headerHash.Length);

            // Write the final fully composed data blocks together.
            stream.Seek(0, SeekOrigin.Begin);

            var expandedHeader = new byte[_SqPackHeaderSize];
            _SqPackHeader[indexId].CopyTo(expandedHeader, 0);

            stream.Write(expandedHeader);
            stream.Write(indexHeaderBlock);
            stream.Write(fileSegment);
            stream.Write(SynonymTable[indexId]);
            stream.Write(EmptyBlock[indexId]);
            stream.Write(folderSegment);
        }

        /// <summary>
        /// Writes a given hash into a 64 byte block, padding as needed.
        /// </summary>
        /// <param name="bw"></param>
        /// <param name="hash"></param>
        protected virtual void Write64ByteHash(BinaryWriter bw, byte[] hash)
        {
            var pos = bw.BaseStream.Position;
            bw.Write(hash);
            var goal = pos + 64;
            var diff = goal - bw.BaseStream.Position;
            if(diff > 0)
            {
                bw.Write(new byte[diff]);
            }
        }

        /// <summary>
        /// Gets the raw uint data offset from the index file, with DatNumber embeded.
        /// Or 0 if the file does not exist.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public virtual uint GetRawDataOffset(string filePath)
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
                return Index2Entries[fullHash].RawOffset;
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
        public virtual long Get8xDataOffset(string filePath)
        {
            return ((long) GetRawDataOffset(filePath)) * 8L;
        }

        /// <summary>
        /// Gets the Index2 data offset in the form of (datNumber, offset Within File)
        /// Or (0, 0) if the file does not exist.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public virtual uint GetRawDataOffsetIndex2(string filePath)
        {

            var fullHash = (uint)HashGenerator.GetHash(filePath);

            if (Index2Entries.ContainsKey(fullHash))
            {
                var entry = Index2Entries[fullHash];
                return entry.RawOffset;
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
        public virtual long Get8xDataOffsetIndex2(string filePath)
        {
            return ((long)GetRawDataOffsetIndex2(filePath)) * 8L;
        }
        public virtual (uint DatNumber, long DataOffset) GetDataOffsetComplete(string filePath)
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
        public virtual uint SetDataOffset(string filePath, long new8xOffset, uint datNumber)
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
        public virtual uint SetDataOffset(string filePath, uint newRawOffset, uint datNumber)
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
        public virtual uint SetDataOffset(string filePath, long new8xOffsetWithDatNumEmbed)
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
        public virtual uint SetDataOffset(string filePath, uint newRawOffsetWithDatNumEmbed)
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
                    Index2Entries[fullHash].RawOffset = newRawOffsetWithDatNumEmbed;
                }
            }

            return originalOffset;
        }


        /// <summary>
        /// Checks to see if a given file path shows up in the Synonyms table.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public virtual bool IsSynonym(string filePath)
        {
            return false;
        }

        /// <summary>
        /// Returns the raw index entries contained in a specific folder.
        /// -- WARNING -- These are the raw index entries, modifications to them will result 
        /// in modifying the underlying index file!
        /// </summary>
        /// <param name="folderHash"></param>
        /// <returns></returns>
        public virtual List<FileIndexEntry> GetEntriesInFolder(uint folderHash)
        {
            if (!Index1Entries.ContainsKey(folderHash)) return new List<FileIndexEntry>();
            return Index1Entries[folderHash].Values.ToList();
        }


        /// <summary>
        /// Retrieves the entire universe of folder => file hashes in the index.
        /// </summary>
        /// <returns></returns>
        public virtual Dictionary<uint, HashSet<uint>> GetAllHashes()
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

        /// <summary>
        /// Clones the entire list of hashes in the index1 file.
        /// Returns a safe cloned copy of the entries by default.
        /// </summary>
        /// <returns></returns>
        public virtual List<FileIndexEntry> GetAllEntriesIndex1(bool safe = true)
        {
            var result = new List<FileIndexEntry>();
            foreach (var folderKv in Index1Entries)
            {
                foreach (var fileKv in folderKv.Value)
                {
                    if (!safe)
                    {

                        result.Add((FileIndexEntry)fileKv.Value);
                    }
                    else
                    {
                        result.Add((FileIndexEntry)fileKv.Value.Clone());
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Clones the entire list of hashes in the index1 file.
        /// Returns a safe cloned copy of the entries by default.
        /// </summary>
        /// <returns></returns>
        public virtual List<FileIndex2Entry> GetAllEntriesIndex2(bool safe = true)
        {
            var result = new List<FileIndex2Entry>();
            foreach (var kv in Index2Entries)
            {
                if (!safe)
                {

                    result.Add(kv.Value);
                }
                else
                {
                    result.Add((FileIndex2Entry)kv.Value.Clone());
                }
            }
            return result;
        }


        public virtual bool FileExists(string fullPath)
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

        public virtual bool FolderExists(uint folderHash)
        {
            if (Index1Entries.ContainsKey(folderHash) && Index1Entries[folderHash].Count != 0)
            {
                return true;
            }
            return false;
        }
        public virtual bool FolderExists(string folderPath)
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
    public abstract class IndexEntry : IComparable, ICloneable
    {
        public abstract byte[] GetBytes();
        public abstract void SetBytes(byte[] b);
        public abstract int CompareTo(object obj);
        public abstract object Clone();
        public abstract uint DatNum { get; }
        public abstract long DataOffset { get; }
        public abstract long ModifiedOffset { get; }
        public abstract uint RawOffset { get; set; }
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
        public override long ModifiedOffset
        {
            get { return ((long)_fileOffset) * 8; }
        }

        /// <summary>
        /// Dat Number this file's data resides in.
        /// </summary>
        public override uint DatNum
        {
            get
            {
                return ((uint)(_fileOffset & 0x0F) / 2);
            }
        }

        /// <summary>
        /// Data offset within the containing Data File.
        /// </summary>
        public override long DataOffset
        {
            get
            {
                return (ModifiedOffset / 128) * 128;
            }
        }
        public override uint RawOffset
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
        public override object Clone()
        {
            return MemberwiseClone();
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
        public override long ModifiedOffset
        {
            get { return ((long)_fileOffset) * 8; }
        }

        /// <summary>
        /// Dat Number this file's data resides in.
        /// </summary>
        public override uint DatNum
        {
            get
            {
                return ((_fileOffset & 0x0F) / 2);
            }
        }

        /// <summary>
        /// Data offset within the containing Data File.
        /// </summary>
        public override long DataOffset
        {
            get
            {
                return (ModifiedOffset / 128) * 128;
            }
        }

        public override uint RawOffset
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

        public override object Clone()
        {
            return MemberwiseClone();
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


        // Inhereted/Interface members.
        // DAT number might actually be resolveable? Not sure.
        public override uint DatNum { get { return 0; } }
        public override long DataOffset { get { return 0; } }
        public override long ModifiedOffset { get { return 0; } }
        public override uint RawOffset { get { return 0; } set { } }
        public override object Clone()
        {
            return MemberwiseClone();
        }
    }



}
