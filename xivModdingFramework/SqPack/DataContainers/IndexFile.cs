using HelixToolkit.SharpDX.Core.Core2D;
using HelixToolkit.SharpDX.Core.Helper;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
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
        public const bool _BENCHMARK_HACK = true;
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

        protected List<byte[]> EmptyBlock = new List<byte[]>();

        // We regenerate the directory list manually, so we don't save it.
        //protected List<byte[]> DirBlock = new List<byte[]>();


        // Index1 entries.  Keyed by [Folder Hash, File Hash] => Entry
        protected Dictionary<uint, Dictionary<uint, FileIndexEntry>> Index1Entries = new Dictionary<uint, Dictionary<uint, FileIndexEntry>>();

        // Index2 entries.  Keyed by [Full Hash] => Entry
        protected Dictionary<uint, FileIndex2Entry> Index2Entries = new Dictionary<uint, FileIndex2Entry>();

        // Index 1 Synonyms.  Keyed by merged [File Hash-Folder Hash] => Entries.
        internal Dictionary<ulong, List<SynonymTableEntry>> Index1Synonyms = new Dictionary<ulong, List<SynonymTableEntry>>();

        // Index 2 Synonyms.  Keyed by [Full Hash] => Entries
        internal Dictionary<uint, List<SynonymTableEntry>> Index2Synonyms = new Dictionary<uint, List<SynonymTableEntry>>();

        // The data file this Index file refers to.
        public readonly XivDataFile DataFile;


        /// <summary>
        /// Standard constructor.
        /// </summary>
        public IndexFile(XivDataFile dataFile, BinaryReader index1Stream, BinaryReader index2Stream, bool readOnly = true)
        {
            try
            {
                ReadOnlyMode = readOnly;
                DataFile = dataFile;
                ReadIndexFile(index1Stream, 0);
                ReadIndexFile(index2Stream, 1);
            } catch(Exception ex)
            {
                throw;
            }
        }

        public virtual void Save() {

            var dir = XivCache.GameInfo.GameDirectory;
            var index1Path = XivDataFiles.GetFullPath(DataFile, Index.IndexExtension);
            var index2Path = XivDataFiles.GetFullPath(DataFile, Index.Index2Extension);
            using (var index1Stream = new BinaryWriter(File.Open(index1Path, FileMode.OpenOrCreate, FileAccess.Write)))
            {
                using (var index2Stream = new BinaryWriter(File.Open(index2Path, FileMode.OpenOrCreate, FileAccess.Write)))
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
            _SqPackHeader.Add(br.ReadBytes((int)_SqPackHeaderSize));
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

            ReadSynTable(br, indexId);

            EmptyBlock.Add(ReadSegment(br));

            // We don't actually care about the directory data, since we regenerate it automatically.
            var directoryData = ReadSegment(br);

            IndexType.Add(br.ReadUInt32());

            // Rest of the file is padding and self-hash.
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
                var offset = BitConverter.ToUInt32(bytes, 4);

                if (!Index2Entries.ContainsKey(entry.FullPathHash))
                {
                    Index2Entries.Add(entry.FullPathHash, entry);
                }
            }

            // Skip past the hash.
            br.BaseStream.Seek(storedOffset + 64, SeekOrigin.Begin);
        }


        protected virtual void ReadSynTable(BinaryReader br, int indexId)
        {
            uint segmentOffset = br.ReadUInt32();
            uint segmentSize = br.ReadUInt32();
            var storedOffset = br.BaseStream.Position;
            br.BaseStream.Seek(segmentOffset, SeekOrigin.Begin);

            for (int i = 0; i < segmentSize / SynonymTableEntry.Size; i++)
            {
                var entry = SynonymTableEntry.ReadEntry(br);
                if(indexId == 0)
                {
                    ulong key = entry.FilePathHash;
                    key = key << 32;
                    key |= entry.FolderPathHash;
                    if (!Index1Synonyms.ContainsKey(key))
                    {
                        Index1Synonyms.Add(key, new List<SynonymTableEntry>());
                    }
                    Index1Synonyms[key].Add(entry);
                } else
                {
                    if (!Index2Synonyms.ContainsKey(entry.FilePathHash))
                    {
                        Index2Synonyms.Add(entry.FilePathHash, new List<SynonymTableEntry>());
                    }
                    Index2Synonyms[entry.FilePathHash].Add(entry);
                }
            }
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
            var datCount = Dat.GetLargestDatNumber(DataFile) + 1;

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
                var bytes = file.GetBytes(this);
                Array.Copy(bytes, 0, fileSegment, offset, bytes.Length);
                offset += bytes.Length;
            }

            // Synonym table
            var synTableSize = 0;
            var synonymTableSegment = new byte[0];
            offset = 0;
            if (indexId == 0)
            {
                synTableSize = Index1Synonyms.Sum(x => x.Value.Count) * SynonymTableEntry.Size;
                synonymTableSegment = new byte[synTableSize];
                foreach (var kv in Index1Synonyms)
                {
                    foreach (var entry in kv.Value)
                    {
                        Array.Copy(entry.GetBytes(), 0, synonymTableSegment, offset, SynonymTableEntry.Size);
                        offset += SynonymTableEntry.Size;
                    }
                }
            } else
            {
                synTableSize = Index2Synonyms.Sum(x => x.Value.Count) * SynonymTableEntry.Size;
                synonymTableSegment = new byte[synTableSize];
                foreach (var kv in Index2Synonyms)
                {
                    foreach (var entry in kv.Value)
                    {
                        Array.Copy(entry.GetBytes(), 0, synonymTableSegment, offset, SynonymTableEntry.Size);
                        offset += SynonymTableEntry.Size;
                    }
                }
            }


            // Folder listing
            var folderSegmentSize = folderListing.Count * 16;
            var folderSegment = new byte[folderSegmentSize];
            offset = 0;
            foreach (var kv in folderListing)
            {
                Array.Copy(kv.Value.GetBytes(this), 0, folderSegment, offset, 16);
                offset += 16;
            }


            var emptyBlockSize = EmptyBlock[indexId].Length;

            // Total size is Headers + segment sizes.
            var totalSize = (int)(_SqPackHeaderSize + _IndexHeaderSize) + fileSegmentSize + folderSegmentSize + emptyBlockSize + synTableSize;

            // Calculate offsets.
            var segmentOffsets = new List<int>();

            var fileSegmentOffset = (int)(_SqPackHeaderSize + _IndexHeaderSize);
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
                    Write64ByteHash(bw, sh.ComputeHash(synonymTableSegment));

                    // Empty Block Data Segment
                    bw.Write(BitConverter.GetBytes(emptyBlockSize == 0 ? 0 : emptyBlockOffset));
                    bw.Write(BitConverter.GetBytes(emptyBlockSize));
                    Write64ByteHash(bw, sh.ComputeHash(EmptyBlock[indexId]));

                    // Folder Data Segment
                    bw.Write(BitConverter.GetBytes(folderSegmentSize == 0 ? 0 : folderSegmentOffset));
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
            stream.Write(synonymTableSegment);
            stream.Write(EmptyBlock[indexId]);
            stream.Write(folderSegment);

            // Truncate any extra data in the file.
            stream.BaseStream.SetLength(stream.BaseStream.Position);
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
        /// Gets the raw uint data offset from the index files, with DatNumber embeded.
        /// Or 0 if the file does not exist.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public virtual uint GetRawDataOffset(string filePath)
        {
            // Check Index1
            var offset = GetRawDataOffsetIndex1(filePath);
            if(offset == 0)
            {
                // Check Index2.
                offset = GetRawDataOffsetIndex2(filePath);
            }
            return offset;
        }

        /// <summary>
        /// Gets the 8x multiplied data offset from the index files, with DatNumber embeded.
        /// Or 0 if the file does not exist.  
        /// This is primarily useful for legacy functionality.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public virtual long Get8xDataOffset(string filePath)
        {
            return ((long)GetRawDataOffset(filePath)) * 8L;
        }

        /// <summary>
        /// Gets the raw uint data offset from the index file, with DatNumber embeded.
        /// Or 0 if the file does not exist.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public virtual uint GetRawDataOffsetIndex1(string filePath)
        {

            var fileName = Path.GetFileName(filePath);
            var folderName = Path.GetDirectoryName(filePath)?.Replace("\\", "/") ?? string.Empty;
            var fileHash = (uint) HashGenerator.GetHash(fileName);
            var folderHash = (uint) HashGenerator.GetHash(folderName);

            uint offset = 0;
            // Do we have a base table entry?
            if (Index1Entries.ContainsKey(folderHash) && Index1Entries[folderHash].ContainsKey(fileHash))
            {
                var entry = Index1Entries[folderHash][fileHash];
                offset = entry.RawOffset;
            }

            // Do we have a synonym table entry?
            ulong key = fileHash;
            key = key << 32;
            key |= folderHash;
            if (Index1Synonyms.ContainsKey(key))
            {
                var entry = Index1Synonyms[key].FirstOrDefault(x => x.FilePath == filePath);
                if (entry != null)
                {
                    offset = entry.Offset;
                }
            }

            return offset;
        }

        /// <summary>
        /// Gets the 8x multiplied data offset from the index file, with DatNumber embeded.
        /// Or 0 if the file does not exist.  
        /// This is primarily useful for legacy functionality.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public virtual long Get8xDataOffsetIndex1(string filePath)
        {
            return ((long) GetRawDataOffsetIndex1(filePath)) * 8L;
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

            uint offset = 0;

            // Do we have a base entry?
            if (Index2Entries.ContainsKey(fullHash))
            {
                var entry = Index2Entries[fullHash];
                offset = entry.RawOffset;
            }

            // Do we have a synonym table entry?
            if (Index2Synonyms.ContainsKey(fullHash))
            {
                var entry = Index2Synonyms[fullHash].FirstOrDefault(x => x.FilePath == filePath);
                if(entry != null)
                {
                    offset = entry.Offset;
                }
            }

            return offset;
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

        /// <summary>
        /// Update the data offset for a given file, adding the file if needed.
        /// Returns the previous 8x Data Offset (with dat Number Embedded), or 0 if the file did not exist.
        /// 
        /// Setting a value of 0 or negative for the offset will remove the file pointer.
        /// </summary>
        public virtual long Set8xDataOffset(string filePath, long new8xOffsetWithDatNumEmbed)
        {
            if (new8xOffsetWithDatNumEmbed <= 0)
            {
                return SetRawDataOffset(filePath, 0);
            }
            else
            {
                uint val = (uint)(new8xOffsetWithDatNumEmbed / 8);
                return SetRawDataOffset(filePath, val) * 8L;
            }
        }


        public virtual uint SetRawDataOffset(string filePath, uint newRawOffsetWithDatNumEmbed)
        {
            return INTERNAL_SetDataOffset(filePath, newRawOffsetWithDatNumEmbed, false);
        }
        /// <summary>
        /// Update the data offset for a given file, adding the file if needed.
        /// Returns the previous Raw Data Offset (with dat Number Embedded), or 0 if the file did not exist.
        /// 
        /// Setting a value of 0 for the offset will remove the file pointer.
        /// </summary>
        protected virtual uint INTERNAL_SetDataOffset(string filePath, uint newRawOffsetWithDatNumEmbed, bool allowRepair = false)
        {
            if (ReadOnlyMode)
            {
                throw new Exception("Cannot write index updates to ReadOnly Index File.");
            }

            var fileName = Path.GetFileName(filePath);
            var folderName = filePath.Substring(0, filePath.LastIndexOf('/'));
            var fileHash = (uint)HashGenerator.GetHash(fileName);
            var folderHash = (uint)HashGenerator.GetHash(folderName);
            var fullHash = (uint)HashGenerator.GetHash(filePath);

            ulong key = (ulong)fileHash;
            key = key << 32;
            key |= folderHash;

            uint originalOffsetIndex1 = GetRawDataOffsetIndex1(filePath);
            uint originalOffsetIndex2 = GetRawDataOffsetIndex2(filePath);
            bool existsInIndex1 = originalOffsetIndex1 > 0;
            bool existsInIndex2 = originalOffsetIndex2 > 0;
            bool index1Syn = Index1Synonyms.ContainsKey(key);
            bool index2Syn = Index2Synonyms.ContainsKey(fullHash);

            // Create folder hash if needed.
            if (newRawOffsetWithDatNumEmbed > 0 && !Index1Entries.ContainsKey(folderHash))
            {
                Index1Entries.Add(folderHash, new Dictionary<uint, FileIndexEntry>());
            }

            if (!existsInIndex1 && Index1Entries.ContainsKey(folderHash) && Index1Entries[folderHash].ContainsKey(fileHash))
            {
                // 0 Offset value in the index table.  Remove it.
                Index1Entries[folderHash].Remove(fileHash);
            }
            if(!existsInIndex2 && Index2Entries.ContainsKey(fullHash))
            {
                // 0 Offset value in the index table.  Remove it.
                Index2Entries.Remove(fullHash);
            }


            if (originalOffsetIndex1 == originalOffsetIndex2 && (!index1Syn && !index2Syn))
            {
                // This is the typical case for updating, adding, or removing a file.
                // It exists in the same state in both indexes, with no colisions.

                if(originalOffsetIndex1 == newRawOffsetWithDatNumEmbed)
                {
                    // Updating to the same value that already exists, just return.
                    return originalOffsetIndex1;
                }

                // Deleting existing file.
                if (newRawOffsetWithDatNumEmbed == 0)
                {
                    Index1Entries[folderHash].Remove(fileHash);
                    Index2Entries.Remove(fullHash);

                    // Remove folder if empty now.
                    if(Index1Entries[folderHash].Count == 0)
                    {
                        Index1Entries.Remove(folderHash);
                    }
                    return originalOffsetIndex1;
                }
                else
                {
                    // Creating or Updating.
                    if (originalOffsetIndex1 > 0)
                    {
                        // Update existing
                        Index1Entries[folderHash][fileHash].RawOffset = newRawOffsetWithDatNumEmbed;
                        Index2Entries[fullHash].RawOffset = newRawOffsetWithDatNumEmbed;
                        return originalOffsetIndex1;
                    }
                    else
                    {
                        // Add new, non-colliding file.
                        var entry1 = new FileIndexEntry(newRawOffsetWithDatNumEmbed, fileHash, folderHash);
                        var entry2 = new FileIndex2Entry(fullHash, newRawOffsetWithDatNumEmbed);

                        Index1Entries[folderHash].Add(fileHash, entry1);
                        Index2Entries.Add(fullHash, entry2);
                        return originalOffsetIndex1;
                    }
                }
            } else if (!index1Syn && !index2Syn)
            {
                // Cases where the values between indexes did not match, while not being synonyms.
                // These are essentially all error states.
                if(originalOffsetIndex1 == 0)
                {
                    if (allowRepair)
                    {
                        // Create/Update as needed.
                        var entry1 = new FileIndexEntry(newRawOffsetWithDatNumEmbed, fileHash, folderHash);
                        Index1Entries[folderHash].Add(fileHash, entry1);
                        Index2Entries[fullHash].RawOffset = newRawOffsetWithDatNumEmbed;

                        // Values out of repair path are unused/invalid.
                        return uint.MaxValue;
                    }

                    if (_BENCHMARK_HACK)
                    {
                        // Doesn't exist in Index 1.
                        // Set value from Index 2 to Index 1 and re-call.
                        var entry1 = new FileIndexEntry(originalOffsetIndex2, fileHash, folderHash);
                        Index1Entries[folderHash].Add(fileHash, entry1);
                        return SetRawDataOffset(filePath, newRawOffsetWithDatNumEmbed);
                    }

                    // Doesn't exist in Index 1.
                    // This means we hit a -NEW- Synonym in Index 2.
                    throw new InvalidDataException("Cannot write new Synonym to Index 2 File: " + filePath + " : " + fullHash);
                } else if(originalOffsetIndex2 == 0)
                {
                    if (allowRepair)
                    {
                        // Create/Update as needed.
                        Index1Entries[folderHash][fileHash].RawOffset = newRawOffsetWithDatNumEmbed;
                        var entry2 = new FileIndex2Entry(fullHash, newRawOffsetWithDatNumEmbed);
                        Index2Entries.Add(fullHash, entry2);

                        // Values out of repair path are unused/invalid.
                        return uint.MaxValue;
                    }

                    // Doesn't exist in Index 2.
                    // This means we hit a -NEW- Synonym in Index 1.
                    throw new InvalidDataException("Cannot write new Synonym to Index 1 File: "  + filePath + " : " + fileHash + " : " + folderHash);
                } else
                {
                    if (allowRepair)
                    {
                        // Update existing
                        Index1Entries[folderHash][fileHash].RawOffset = newRawOffsetWithDatNumEmbed;
                        Index2Entries[fullHash].RawOffset = newRawOffsetWithDatNumEmbed;

                        // Values out of repair path are unused/invalid.
                        return uint.MaxValue;
                    }

                    // This is a case, where the hash exists in both indexes, but with mismatching values...
                    // While /NOT/ being a synonym in either...
                    // This means either the index is partially corrupt....
                    throw new InvalidDataException("Cannot Update non-Synonym index with mismatched Index1/Index2 Values: " + filePath);
                }
            } else
            {
                // Exists as a Synonym in one or both tables.
                
                if (index1Syn)
                {
                    // Update the synonym entry.
                    var synEntry = Index1Synonyms[key].FirstOrDefault(x => x.FilePath == filePath);
                    if(synEntry == null)
                    {
                        throw new InvalidDataException("Cannot add third Synonym Definition for Index1 Entry: " + filePath);
                    }
                    synEntry.Offset = newRawOffsetWithDatNumEmbed;
                }
                
                if(index2Syn)
                {
                    // Update the synonym entry.
                    var synEntry = Index2Synonyms[fullHash].FirstOrDefault(x => x.FilePath == filePath);
                    if (synEntry == null)
                    {
                        throw new InvalidDataException("Cannot add third Synonym Definition for Index2 Entry: " + filePath);
                    }
                    synEntry.Offset = newRawOffsetWithDatNumEmbed;
                }

                return originalOffsetIndex1;
            }
        }


        /// <summary>
        /// Attempts to repair a broken index value.
        /// </summary>
        /// <param name="filePath"></param>
        public virtual void RepairIndexValue(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            var folderName = filePath.Substring(0, filePath.LastIndexOf('/'));
            var fileHash = (uint)HashGenerator.GetHash(fileName);
            var folderHash = (uint)HashGenerator.GetHash(folderName);
            var fullHash = (uint)HashGenerator.GetHash(filePath);

            ulong key = (ulong)fileHash;
            key = key << 32;
            key |= folderHash;

            uint originalOffsetIndex1 = GetRawDataOffsetIndex1(filePath);
            uint originalOffsetIndex2 = GetRawDataOffsetIndex2(filePath);
            bool existsInIndex1 = originalOffsetIndex1 > 0;
            bool existsInIndex2 = originalOffsetIndex2 > 0;
            bool index1Syn = Index1Synonyms.ContainsKey(key);
            bool index2Syn = Index2Synonyms.ContainsKey(fullHash);

            if(originalOffsetIndex1 == originalOffsetIndex2)
            {
                // Doesn't need repair.
                return;
            }

            if (!existsInIndex1)
            {
                INTERNAL_SetDataOffset(filePath, originalOffsetIndex2, true);
            } else if(!existsInIndex2)
            {
                INTERNAL_SetDataOffset(filePath, originalOffsetIndex1, true);
            }
            else
            {
                // Exists in both indices with mismatching values.
                // Treat Index1 as the gold truth value.
                INTERNAL_SetDataOffset(filePath, originalOffsetIndex1, true);
            }

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
        /// Retrieves the entire universe of folder => file hashes in the index1.
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
            var offset = GetRawDataOffset(fullPath);
            return offset > 0;
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
        public abstract byte[] GetBytes(IndexFile indexFile);
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


        public override byte[] GetBytes(IndexFile indexFile)
        {
            var b = new byte[8];

            if (indexFile.Index2Synonyms.ContainsKey(_fileOffset))
            {
                // Synonyms just write a [1] in place of offset since they use the Synonym table.
                IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(_fullPathHash), 0);
                IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes((uint)1), 4);
            }
            else
            {
                IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(_fullPathHash), 0);
                IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(_fileOffset), 4);
            }

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


        public override byte[] GetBytes(IndexFile indexFile)
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

        public override byte[] GetBytes(IndexFile indexFile)
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


    public class SynonymTableEntry
    {
        public const int Size = 256;
        public uint FilePathHash;
        public uint FolderPathHash; // Always Seems to be 0.
        public uint Offset;
        public uint SynonymNumber; // 0 or 1
        public string FilePath;

        /// <summary>
        /// The default table-ending synonym entry.
        /// Seems to always be included?
        /// </summary>
        /// <returns></returns>
        public static SynonymTableEntry GetSynTableEndingEntry()
        {
            var entry = new SynonymTableEntry();
            entry.FilePathHash = uint.MaxValue;
            entry.FolderPathHash = uint.MaxValue;
            entry.Offset = 0;
            entry.SynonymNumber = uint.MaxValue;
            entry.FilePath = "";
            return entry;
        }

        public static SynonymTableEntry ReadEntry(BinaryReader br)
        {
            var entry = new SynonymTableEntry();
            var offset = br.BaseStream.Position;
            var end = br.BaseStream.Position + Size;
            entry.FilePathHash = br.ReadUInt32();
            entry.FolderPathHash = br.ReadUInt32();

            entry.Offset = br.ReadUInt32();
            entry.SynonymNumber = br.ReadUInt32();
            entry.FilePath = IOUtil.ReadNullTerminatedString(br);

            // Remainder is padding bytes of value 254 or 0 depending on if it is a valid or ending entry.
            br.BaseStream.Seek(end, SeekOrigin.Begin);

            return entry;
        }

        public byte[] GetBytes()
        {
            try
            {
                var bytes = new byte[Size];
                Array.Copy(BitConverter.GetBytes(FilePathHash), 0, bytes, 0, sizeof(uint));
                Array.Copy(BitConverter.GetBytes(FolderPathHash), 0, bytes, 4, sizeof(uint));
                Array.Copy(BitConverter.GetBytes(Offset), 0, bytes, 8, sizeof(uint));
                Array.Copy(BitConverter.GetBytes(SynonymNumber), 0, bytes, 12, sizeof(uint));

                var strBytes = Encoding.UTF8.GetBytes(FilePath);
                Array.Copy(strBytes, 0, bytes, 16, strBytes.Length);

                if (FilePathHash != uint.MaxValue)
                {
                    var offset = strBytes.Length + 16 + 1;
                    for (int i = offset; i < Size; i++)
                    {
                        bytes[i] = 254;
                    }
                }
                return bytes;
            }
            catch(Exception ex)
            {
                throw;
            }
        }

    }

}
