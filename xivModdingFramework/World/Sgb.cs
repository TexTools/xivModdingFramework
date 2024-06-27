using SharpDX.Direct2D1;
using SharpDX.WIC;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using xivModdingFramework.Helpers;
using static xivModdingFramework.World.Sgb;

namespace xivModdingFramework.World
{
    // ===== SGB / WORLD ASSET FILE HANDLING ==== //
    //
    // The majority of this code is pulled directly from Lumina, or otherwise
    // code written off of knowledge gleaned from Lumina.  No reason to reinvent
    // the wheel.  Ultimately though, TexTools really only cares about the SGD
    // files for resolving BG models to pull out of it.   Which we used to do
    // by just scannning the string block.
    // 
    // ========================================== //
    public class XivSgb
    {
        public int FileSize;
        public int ChunkCount;
        public SgbChunkHeader ChunkHeader;

        public HashSet<string> GetModels()
        {
            var ret = new HashSet<string>();

            foreach(var lg in ChunkHeader.LayerGroups)
            {
                foreach(var l in lg.Layers)
                {
                    foreach(var o in l.InstanceObjects)
                    {
                        if(o.AssetType == LayerEntryType.BG)
                        {
                            var bgo = o.Object as BGInstanceObject;
                            if (bgo != null)
                            {
                                if (!string.IsNullOrWhiteSpace(bgo.AssetPath) && bgo.AssetPath.EndsWith(".mdl"))
                                {
                                    ret.Add(bgo.AssetPath);
                                }
                            }
                        }
                    }
                }
            }

            return ret;
        }
    }

    public static class Sgb
    {
        public static XivSgb GetXivSgb(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            {
                using (var br = new BinaryReader(ms))
                {
                    return GetXivSgb(br);
                }
            }
        }
        public static XivSgb GetXivSgb(BinaryReader br)
        {
            var sgb = new XivSgb();
            ReadHeader(br, sgb);



            return sgb;
        }


        private static void ReadHeader(BinaryReader br, XivSgb sgb)
        {
            // SGB1
            var magic = br.ReadChars(4);
            sgb.FileSize = br.ReadInt32();
            sgb.ChunkCount = br.ReadInt32();

            sgb.ChunkHeader = SgbChunkHeader.Read(br);
        }


        public class SgbChunkHeader
        {
            // 4
            public char[] ChunkID;
            public int ChunkSize;

            public SgbLayerGroup[] LayerGroups;

            public int Unknown10;
            public int Unknown14;
            public int Unknown18;
            public int Unknown1C;
            public int Unknown20;
            public int Unknown24;
            public int Unknown28;
            public int Unknown2C;
            public int Unknown30;
            public int Unknown34;
            public int Unknown38;
            // 3
            public int Padding3C;
            public int Padding40;
            public int Padding44;

            public static SgbChunkHeader Read(BinaryReader br)
            {
                SgbChunkHeader ret = new SgbChunkHeader();
                long start = br.BaseStream.Position;

                ret.ChunkID = br.ReadChars(4);
                ret.ChunkSize = br.ReadInt32();

                long rewind = br.BaseStream.Position;
                int layerGroupOffset = br.ReadInt32();
                int layerGroupCount = br.ReadInt32();

                ret.Unknown10 = br.ReadInt32();
                ret.Unknown14 = br.ReadInt32();
                ret.Unknown18 = br.ReadInt32();
                ret.Unknown1C = br.ReadInt32();
                ret.Unknown20 = br.ReadInt32();
                ret.Unknown24 = br.ReadInt32();
                ret.Unknown28 = br.ReadInt32();
                ret.Unknown2C = br.ReadInt32();
                ret.Unknown30 = br.ReadInt32();
                ret.Unknown34 = br.ReadInt32();
                ret.Unknown38 = br.ReadInt32();

                ret.Padding3C = br.ReadInt32();
                ret.Padding40 = br.ReadInt32();
                ret.Padding44 = br.ReadInt32();

                // read layer groups
                br.BaseStream.Seek(start + layerGroupOffset, 0);
                ret.LayerGroups = new SgbLayerGroup[layerGroupCount];
                for (int i = 0; i < layerGroupCount; ++i)
                {
                    br.BaseStream.Seek(rewind + layerGroupOffset + i * 4, SeekOrigin.Begin);
                    ret.LayerGroups[i] = SgbLayerGroup.Read(br);
                }
                return ret;
            }
        }

        public class SgbLayerGroup
        {
            public uint LayerGroupID;
            public string Name;
            public SgbLayer[] Layers;

            public static SgbLayerGroup Read(BinaryReader br)
            {
                SgbLayerGroup ret = new SgbLayerGroup();

                long start = br.BaseStream.Position;
                ret.LayerGroupID = br.ReadUInt32();
                ret.Name = IOUtil.ReadOffsetString(br, start);


                int layerOffsetsStart = br.ReadInt32();
                int layerOffsetsCount = br.ReadInt32();
                ret.Layers = new SgbLayer[layerOffsetsCount];

                // reset stream to end of current struct once done reading layers
                long end = br.BaseStream.Position;
                for (int i = 0; i < layerOffsetsCount; ++i)
                {
                    br.BaseStream.Seek(start + layerOffsetsStart + i * 4, SeekOrigin.Begin);
                    br.BaseStream.Seek(start + layerOffsetsStart + br.ReadInt32(), SeekOrigin.Begin);
                    ret.Layers[i] = SgbLayer.Read(br);
                }
                br.BaseStream.Seek(end, SeekOrigin.Begin);

                return ret;
            }
        }

    }
}
