using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.FileTypes;

namespace xivModdingFramework.Helpers
{
    public static class EndwalkerUpgrade
    {


        /// <summary>
        /// Reads an uncompressed v5 MDL and retrieves the offsets to the bone lists, in order to update them.
        /// </summary>
        /// <param name="br"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static bool FastMdlv6Upgrade(BinaryReader br, BinaryWriter bw, long offset = -1)
        {
#if ENDWALKER
            return false;
#endif

            if(offset < 0)
            {
                offset = br.BaseStream.Position;
            }

            br.BaseStream.Seek(offset, SeekOrigin.Begin);
            bw.BaseStream.Seek(offset, SeekOrigin.Begin);

            var version = br.ReadUInt16();
            if (version != 5)
            {
                return false;
            }


            br.BaseStream.Seek(offset + 12, SeekOrigin.Begin);
            var meshCount = br.ReadUInt16();

            if(meshCount == 0)
            {
                // Uhh..?
                return false;
            }

            br.BaseStream.Seek(offset + 64, SeekOrigin.Begin);
            var lodOffset = br.BaseStream.Position;
            var lods = br.ReadByte();

            var endOfVertexHeaders = offset + Mdl._MdlHeaderSize + (Mdl._VertexDataHeaderSize * meshCount);

            br.BaseStream.Seek(endOfVertexHeaders + 4, SeekOrigin.Begin);
            var pathBlockSize = br.ReadUInt32();

            br.ReadBytes((int)pathBlockSize);

            // Mesh data block.
            var mdlDataOffset = br.BaseStream.Position;
            var mdlData = MdlModelData.Read(br);

            if(mdlData.BoneSetCount == 0 || mdlData.BoneCount == 0)
            {
                // Not 100% sure how to update boneless meshes to v6 yet, so don't upgrade for safety.
                return false;
            }

            br.ReadBytes(mdlData.ElementIdCount * 32);

            // LoD Headers
            br.ReadBytes(60 * 3);

            if ((mdlData.Flags2 & EMeshFlags2.HasExtraMeshes) != 0)
            {
                // Extra Mesh Info Block.
                br.ReadBytes(60);
            }


            // Mesh Group headers.
            br.ReadBytes(36 * meshCount);

            // Attribute pointers.
            br.ReadBytes(4 * mdlData.AttributeCount);

            // Mesh Part information.
            br.ReadBytes(16 * mdlData.MeshPartCount);

            // Show Mesh Part Information.
            br.ReadBytes(12 * mdlData.TerrainShadowPartCount);

            // Material Pointers.
            br.ReadBytes(4 * mdlData.MaterialCount);

            // Bone Pointers.
            br.ReadBytes(4 * mdlData.BoneCount);


            var bonesetStart = br.BaseStream.Position;
            var boneSets = new List<(long Offset, ushort BoneCount, byte[] data)>();
            for (int i = 0; i < mdlData.BoneSetCount; i++)
            {
                // Bone List
                var data = br.ReadBytes(64 * 2);

                // Bone List Size.
                var countOffset = br.BaseStream.Position;
                var count = br.ReadUInt32();

                boneSets.Add((countOffset, (ushort)count, data));
            }

            // Write version information.
            bw.BaseStream.Seek(offset, SeekOrigin.Begin);
            bw.Write((ushort)6);


            // Write LoD count.
            bw.BaseStream.Seek(lodOffset, SeekOrigin.Begin);
            bw.Write((byte)1);


            // Write bone set size.
            short boneSetSize = (short) (64 * mdlData.BoneSetCount);
            mdlData.BoneSetSize = (short)boneSetSize;
            mdlData.LoDCount = 1;
            bw.BaseStream.Seek(mdlDataOffset, SeekOrigin.Begin);
            mdlData.Write(bw);

            // Upgrade bone sets to v6 format.
            bw.BaseStream.Seek(bonesetStart, SeekOrigin.Begin);
            List<long> headerOffsets = new List<long>();
            foreach (var bs in boneSets)
            {
                headerOffsets.Add(bw.BaseStream.Position);
                bw.Write((short)0);
                bw.Write((short)bs.BoneCount);
            }

            var idx = 0;
            foreach(var bs in boneSets)
            {
                var headerOffset = headerOffsets[idx];
                var distance = (short)((bw.BaseStream.Position - headerOffset) / 4);

                bw.Write(bs.data, 0, bs.BoneCount * 2);

                if(bs.BoneCount % 2 != 0)
                {
                    // Expected Padding
                    bw.Write((short)0);
                }


                var pos = bw.BaseStream.Position;
                bw.BaseStream.Seek(headerOffset, SeekOrigin.Begin);
                bw.Write(distance);
                bw.BaseStream.Seek(pos, SeekOrigin.Begin);

                idx++;
            }

            var end = bonesetStart + boneSetSize;
            while(bw.BaseStream.Position < end)
            {
                // Fill out the remainder of the block with 0s.
                bw.Write((byte)0);
            }

            return true;
        }

    }
}
