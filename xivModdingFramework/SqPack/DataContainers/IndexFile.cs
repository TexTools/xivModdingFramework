using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using xivModdingFramework.Helpers;

namespace xivModdingFramework.SqPack.DataContainers
{
    public enum IndexType
    {
        Invalid,
        Index1,
        Index2
    }
    public class IndexFile
    {
        public byte[] Header;
        public uint TotalSegmentHeaderSize;
        public List<IndexSegment> Segments;
        public IndexType Type;

        
        public byte[] GetBytes()
        {
            var totalSize = Header.Length;
            totalSize += (int)TotalSegmentHeaderSize;

            // Calculate total file size.
            foreach (var segment in Segments)
            {
                totalSize += segment.SegmentSize;
            }

            var bytes = new byte[totalSize];

            IOUtil.ReplaceBytesAt(bytes, Header, 0);

            var segmentOffsets = new List<int>();

            // Calculate Segment Offsets.
            int offset = Header.Length + (int)TotalSegmentHeaderSize;
            foreach (var segment in Segments)
            {
                segmentOffsets.Add(offset);
                offset += segment.SegmentSize;
            }

            // Write Segment data.
            IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes(TotalSegmentHeaderSize), Header.Length);
            var headerOffset = Header.Length + 4;
            for (int si = 0; si < Segments.Count; si++)
            {
                var segment = Segments[si];

                // Sort before calculating SHA.
                segment.Sort();

                var segmentOffset = segmentOffsets[si];
                var segBytes = new byte[segment.SegmentSize];
                var i = 0;
                foreach (var entry in segment.Entries)
                {
                    IOUtil.ReplaceBytesAt(segBytes, entry.GetBytes(), i);
                    i += segment.EntrySize;
                }

                segment.RecalculateSha(segBytes);


                if(segment.SegmentSize == 0)
                {
                    segmentOffset = 0;
                }

                // Write Segment Header
                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes(segment.Unknown), headerOffset);
                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes(segmentOffset), headerOffset + 4);
                IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes(segment.SegmentSize), headerOffset + 8);
                IOUtil.ReplaceBytesAt(bytes, segment.ShaBlock, headerOffset + 12);

                // Write segment data block.
                IOUtil.ReplaceBytesAt(bytes, segBytes, segmentOffset);

                headerOffset += (int)segment.HeaderSize;
            }

            // Calculate Main Header SHA
            var sh = SHA1.Create();
            var shBytes = sh.ComputeHash(bytes, 0, (int)Header.Length - 64);
            IOUtil.ReplaceBytesAt(bytes, shBytes, (int)(Header.Length - 64));

            // Calculate Segment Header SHA
            shBytes = sh.ComputeHash(bytes, Header.Length, (int)TotalSegmentHeaderSize - 64);
            IOUtil.ReplaceBytesAt(bytes, shBytes, (int)(Header.Length + TotalSegmentHeaderSize - 64));


            return bytes;
        }
    }

    /// <summary>
    /// Class representing a single Segment block of an index file.
    /// </summary>
    public class IndexSegment
    {
        public int Unknown;
        public byte[] ShaBlock;
        public bool KeepIntact;
        public IndexType Type;

        // Padding Length at the end of the segment header.
        public uint HeaderSize;

        public int EntrySize
        {
            get
            {
                return Type == IndexType.Index2 ? 8 : 16;
            } 
        }

        public int SegmentSize
        {
            get
            {
                return Entries.Count * EntrySize;
            }
        }

        public void RecalculateSha(byte[] bytes = null)
        {
            if (KeepIntact)
            {
                return;
            }

            if (bytes == null)
            {
                bytes = new byte[SegmentSize];

                var i = 0;
                foreach (var entry in Entries)
                {
                    IOUtil.ReplaceBytesAt(bytes, entry.GetBytes(), i);
                    i += EntrySize;
                }
            }

            var shaCalc = SHA1.Create();
            ShaBlock = shaCalc.ComputeHash(bytes);
        }

        public void Sort()
        {
            if (KeepIntact)
            {
                return;
            }
            Entries.Sort();
        }

        public List<IndexEntry> Entries = new List<IndexEntry>();
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
        private int FullPathHash;
        private uint FileOffset;

        /// <summary>
        /// Base data offset * 8.  Includes DAT number reference information still.
        /// </summary>
        public long ModifiedOffset
        {
            get { return ((long)FileOffset) * 8; }
        }

        /// <summary>
        /// Dat Number this file's data resides in.
        /// </summary>
        public int DatNum
        {
            get
            {
                return ((int)(FileOffset & 0x0F) / 2);
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
            IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(FullPathHash), 0);
            IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(FileOffset), 4);

            return b;
        }

        public override void SetBytes(byte[] b)
        {
            FullPathHash = BitConverter.ToInt32(b, 0);
            FileOffset = BitConverter.ToUInt32(b, 4);
        }


        public override int CompareTo(object obj)
        {
            if (obj == null) return 1;
            if (obj.GetType() != typeof(FileIndex2Entry)) throw new Exception("Invalid Index Data Comparison");

            var other = (FileIndex2Entry)obj;
            var mine = (uint)FullPathHash;
            var theirs = (uint)other.FullPathHash;

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
        private uint FileOffset;
        private int FileNameHash;
        private int FolderPathHash;

        /// <summary>
        /// Base data offset * 8.  Includes DAT number reference information still.
        /// </summary>
        public long ModifiedOffset
        {
            get { return ((long)FileOffset) * 8; }
        }

        /// <summary>
        /// Dat Number this file's data resides in.
        /// </summary>
        public int DatNum
        {
            get
            {
                return ((int)(FileOffset & 0x0F) / 2);
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
            byte[] b = new byte[16];
            IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(FileNameHash), 0);
            IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(FolderPathHash), 4);
            IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(FileOffset), 8);

            return b;
        }

        public override void SetBytes(byte[] b)
        {
            FileNameHash = BitConverter.ToInt32(b, 0);
            FolderPathHash = BitConverter.ToInt32(b, 4);
            FileOffset = BitConverter.ToUInt32(b, 8);
        }


        public override int CompareTo(object obj)
        {
            if (obj == null) return 1;
            if (obj.GetType() != typeof(FileIndexEntry)) throw new Exception("Invalid Index Data Comparison");

            var other = (FileIndexEntry)obj;
            if (FolderPathHash != other.FolderPathHash)
            {
                var mine = (uint)FolderPathHash;
                var theirs = (uint)other.FolderPathHash;

                if (mine < theirs) return -1;
                return 1;
            }
            else
            {
                var mine = (uint)FileNameHash;
                var theirs = (uint)other.FileNameHash;

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
        private int FolderPathHash;
        private uint IndexEntriesOffset;
        private uint TotalIndexEntriesSize;

        public uint FileCount
        {
            get
            {
                return TotalIndexEntriesSize / 16;
            }
        }

        public override byte[] GetBytes()
        {
            byte[] b = new byte[16];
            IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(FolderPathHash), 0);
            IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(IndexEntriesOffset), 4);
            IOUtil.ReplaceBytesAt(b, BitConverter.GetBytes(TotalIndexEntriesSize), 8);

            return b;
        }

        public override void SetBytes(byte[] b)
        {
            FolderPathHash = BitConverter.ToInt32(b, 0);
            IndexEntriesOffset = BitConverter.ToUInt32(b, 4);
            TotalIndexEntriesSize = BitConverter.ToUInt32(b, 8);
        }


        public override int CompareTo(object obj)
        {
            if (obj == null) return 1;
            if (obj.GetType() != typeof(FolderIndexEntry)) throw new Exception("Invalid Index Data Comparison");

            var other = (FolderIndexEntry)obj;

            var mine = (uint)FolderPathHash;
            var theirs = (uint)other.FolderPathHash;

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
