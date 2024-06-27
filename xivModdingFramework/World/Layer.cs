using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using xivModdingFramework.Helpers;

namespace xivModdingFramework.World
{
    public class SgbLayer
    {
        public uint LayerId;
        public string Name;
        public int InstanceObjectsOffset;
        public int InstanceObjectCount;
        public byte ToolModeVisible;
        public byte ToolModeReadOnly;
        public byte IsBushLayer;
        public byte PS3Visible;
        public int LayerSetReferencedListOffset;
        public ushort FestivalID;
        public ushort FestivalPhaseID;
        public byte IsTemporary;
        public byte IsHousing;
        public ushort VersionMask;
        private uint _padding;
        public int ObSetReferencedList;
        public int ObSetReferencedListCount;
        public int ObSetEnableReferencedList;
        public int ObSetEnableReferencedListCount;
        public LayerSetReferencedList LayerSetReferencedList;

        public InstanceObject[] InstanceObjects;

        public LayerSetReferenced[] LayerSetReferences;
        public OBSetReferenced[] OBSetReferencedList;
        public OBSetEnableReferenced[] OBSetEnableReferencedList;


        public static SgbLayer Read(BinaryReader br)
        {
            var ret = new SgbLayer();
            var start = br.BaseStream.Position;

            ret.LayerId = br.ReadUInt32();
            ret.Name = IOUtil.ReadOffsetString(br, start);
            ret.InstanceObjectsOffset = br.ReadInt32();
            ret.InstanceObjectCount = br.ReadInt32();
            ret.ToolModeVisible = br.ReadByte();
            ret.ToolModeReadOnly = br.ReadByte();
            ret.IsBushLayer = br.ReadByte();
            ret.PS3Visible = br.ReadByte();
            ret.LayerSetReferencedListOffset = br.ReadInt32();
            ret.FestivalID = br.ReadUInt16();
            ret.FestivalPhaseID = br.ReadUInt16();
            ret.IsTemporary = br.ReadByte();
            ret.IsHousing = br.ReadByte();
            ret.VersionMask = br.ReadUInt16();
            ret._padding = br.ReadUInt32();
            ret.ObSetReferencedList = br.ReadInt32();
            ret.ObSetReferencedListCount = br.ReadInt32();
            ret.ObSetEnableReferencedList = br.ReadInt32();
            ret.ObSetEnableReferencedListCount = br.ReadInt32();

            br.BaseStream.Position = start + ret.LayerSetReferencedListOffset;
            ret.LayerSetReferencedList = LayerSetReferencedList.Read(br);


            br.BaseStream.Position = start + ret.InstanceObjectsOffset;
            var instanceOffsets = new List<int>(ret.InstanceObjectCount);
            for (int i = 0; i < ret.InstanceObjectCount; i++)
            {
                instanceOffsets.Add(br.ReadInt32());
            }

            ret.InstanceObjects = new InstanceObject[ret.InstanceObjectCount];
            for (var i = 0; i < ret.InstanceObjectCount; i++)
            {
                br.BaseStream.Position = start + ret.InstanceObjectsOffset + instanceOffsets[i];
                ret.InstanceObjects[i] = InstanceObject.Read(br);
            }

            ret.LayerSetReferences = new LayerSetReferenced[ret.LayerSetReferencedList.LayerSetCount];
            br.BaseStream.Position = start + ret.LayerSetReferencedList.LayerSets;
            for (var i = 0; i < ret.LayerSetReferencedList.LayerSetCount; i++)
                ret.LayerSetReferences[i] = LayerSetReferenced.Read(br);

            ret.OBSetReferencedList = new OBSetReferenced[ret.ObSetReferencedListCount];
            br.BaseStream.Position = start + ret.ObSetReferencedList;
            for (var i = 0; i < ret.ObSetReferencedListCount; i++)
                ret.OBSetReferencedList[i] = OBSetReferenced.Read(br);

            ret.OBSetEnableReferencedList = new OBSetEnableReferenced[ret.ObSetEnableReferencedListCount];
            br.BaseStream.Position = start + ret.ObSetEnableReferencedList;
            for (var i = 0; i < ret.ObSetEnableReferencedListCount; i++)
                ret.OBSetEnableReferencedList[i] = OBSetEnableReferenced.Read(br);

            return ret;
        }
    }
    public struct LayerSetReferencedList
    {
        public LayerSetReferencedType ReferencedType;
        public int LayerSets;
        public int LayerSetCount;

        public static LayerSetReferencedList Read(BinaryReader br)
        {
            var ret = new LayerSetReferencedList();
            var start = br.BaseStream.Position;

            ret.ReferencedType = (LayerSetReferencedType)br.ReadInt32();
            ret.LayerSets = br.ReadInt32();
            ret.LayerSetCount = br.ReadInt32();

            return ret;
        }
    }
    public struct LayerSetReferenced
    {
        public uint LayerSetId;

        public static LayerSetReferenced Read(BinaryReader br)
        {
            var ret = new LayerSetReferenced();
            var start = br.BaseStream.Position;

            ret.LayerSetId = br.ReadUInt32();

            return ret;
        }
    }

    public struct OBSetReferenced
    {
        public LayerEntryType AssetType;
        public uint InstanceId;
        public string OBSetAssetPath;

        public static OBSetReferenced Read(BinaryReader br)
        {
            var ret = new OBSetReferenced();
            var start = br.BaseStream.Position;

            ret.AssetType = (LayerEntryType)br.ReadInt32();
            ret.InstanceId = br.ReadUInt32();
            ret.OBSetAssetPath = IOUtil.ReadOffsetString(br, start);

            return ret;
        }
    }

    public struct OBSetEnableReferenced
    {
        public LayerEntryType AssetType;
        public uint InstanceId;
        public byte OBSetEnable;
        public byte OBSetEmissiveEnable;
        private byte[] _padding00; //[2]

        public static OBSetEnableReferenced Read(BinaryReader br)
        {
            var ret = new OBSetEnableReferenced();
            var start = br.BaseStream.Position;

            ret.AssetType = (LayerEntryType)br.ReadInt32();
            ret.InstanceId = br.ReadUInt32();
            ret.OBSetEnable = br.ReadByte();
            ret.OBSetEmissiveEnable = br.ReadByte();
            ret._padding00 = br.ReadBytes(2);

            return ret;
        }
    }


}
