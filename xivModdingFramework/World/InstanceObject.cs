using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using xivModdingFramework.Helpers;

namespace xivModdingFramework.World
{
    public struct LuminaVector3
    {
        public float X, Y, Z;

        public static LuminaVector3 Read(BinaryReader br)
        {
            return new LuminaVector3 { X = br.ReadSingle(), Y = br.ReadSingle(), Z = br.ReadSingle() };
        }

        public static LuminaVector3 Read16(BinaryReader br)
        {
            return new LuminaVector3 { X = (float)br.ReadUInt16() / 0xFFFF, Y = (float)br.ReadUInt16() / 0xFFFF, Z = (float)br.ReadUInt16() / 0xFFFF };
        }
    };

    public struct LuminaTransformation
    {
        public LuminaVector3 Translation, Rotation, Scale;

        public static LuminaTransformation Read(BinaryReader br)
        {
            return new LuminaTransformation
            {
                Translation = LuminaVector3.Read(br),
                Rotation = LuminaVector3.Read(br),
                Scale = LuminaVector3.Read(br)
            };
        }
    };

    public struct InstanceObject
    {
        public LayerEntryType AssetType;
        public uint InstanceId;
        public string Name;
        public LuminaTransformation Transform;
        public object Object;

        public static InstanceObject Read(BinaryReader br)
        {
            var ret = new InstanceObject();
            var start = br.BaseStream.Position;

            ret.AssetType = (LayerEntryType)br.ReadInt32();
            ret.InstanceId = br.ReadUInt32();
            ret.Name = IOUtil.ReadOffsetString(br, start);

            ret.Transform = LuminaTransformation.Read(br);

            switch (ret.AssetType)
            {
                case LayerEntryType.BG: ret.Object = BGInstanceObject.Read(br); break; //0x1
                    /*
                    case LayerEntryType.LayLight: ret.Object = LightInstanceObject.Read(br); break; //0x3
                    case LayerEntryType.VFX: ret.Object = VFXInstanceObject.Read(br); break; //0x4
                    case LayerEntryType.PositionMarker: ret.Object = PositionMarkerInstanceObject.Read(br); break; //0x5
                    case LayerEntryType.SharedGroup: ret.Object = SharedGroupInstanceObject.Read(br); break; //0x6
                    case LayerEntryType.Sound: ret.Object = SoundInstanceObject.Read(br); break; //0x7
                    case LayerEntryType.EventNPC: ret.Object = ENPCInstanceObject.Read(br); break; //0x8
                    case LayerEntryType.BattleNPC: ret.Object = BNPCInstanceObject.Read(br); break; //0x9
                    case LayerEntryType.Aetheryte: ret.Object = AetheryteInstanceObject.Read(br); break; //0xC
                    case LayerEntryType.EnvSet: ret.Object = EnvSetInstanceObject.Read(br); break; //0xD
                    case LayerEntryType.Gathering: ret.Object = GatheringInstanceObject.Read(br); break; //0x#
                    case LayerEntryType.Treasure: ret.Object = TreasureInstanceObject.Read(br); break; //0x10
                    case LayerEntryType.PopRange: ret.Object = PopRangeInstanceObject.Read(br); break; //0x28
                    case LayerEntryType.ExitRange: ret.Object = ExitRangeInstanceObject.Read(br); break; //0x29
                    case LayerEntryType.MapRange: ret.Object = MapRangeInstanceObject.Read(br); break; //0x2B
                    case LayerEntryType.EventObject: ret.Object = EventInstanceObject.Read(br); break; //0x2D
                    case LayerEntryType.EnvLocation: ret.Object = EnvLocationInstanceObject.Read(br); break; //0x2F
                    case LayerEntryType.EventRange: ret.Object = EventRangeInstanceObject.Read(br); break; //0x31
                    case LayerEntryType.QuestMarker: ret.Object = QuestMarkerInstanceObject.Read(br); break; //0x33
                    case LayerEntryType.CollisionBox: ret.Object = CollisionBoxInstanceObject.Read(br); break; //0x39
                    case LayerEntryType.LineVFX: ret.Object = LineVFXInstanceObject.Read(br); break; //0x3B
                    case LayerEntryType.ClientPath: ret.Object = ClientPathInstanceObject.Read(br); break; //0x41
                    case LayerEntryType.ServerPath: ret.Object = ServerPathInstanceObject.Read(br); break; //0x42
                    case LayerEntryType.GimmickRange: ret.Object = GimmickRangeInstanceObject.Read(br); break; //0x43
                    case LayerEntryType.TargetMarker: ret.Object = TargetMarkerInstanceObject.Read(br); break; //0x44
                    case LayerEntryType.ChairMarker: ret.Object = ChairMarkerInstanceObject.Read(br); break; //0x45
                    case LayerEntryType.PrefetchRange: ret.Object = PrefetchRangeInstanceObject.Read(br); break; //0x47
                    case LayerEntryType.FateRange: ret.Object = FateRangeInstanceObject.Read(br); break; //0x48
                    default:
                        Debug.WriteLine($"Unknown asset {ret.AssetType.ToString()} @ {br.BaseStream.Position}.");
                        break;*/
            }

            //                Debug.WriteLine($"Read {ret.Object.GetType().FullName}");

            return ret;
        }
    }

    public class BGInstanceObject
    {
        public string AssetPath;
        public string CollisionAssetPath;

        public ModelCollisionType CollisionType;
        public uint AttributeMask;
        public uint Attribute;
        public int CollisionConfig;   //TODO: read CollisionConfig
        public byte IsVisible;
        public byte RenderShadowEnabled;
        public byte RenderLightShadowEnabled;
        public byte Padding00;
        public float RenderModelClipRange;

        public static BGInstanceObject Read(BinaryReader br)
        {
            var ret = new BGInstanceObject();

            var start = br.BaseStream.Position;
            ret.AssetPath = IOUtil.ReadOffsetString(br, start - 48);
            ret.CollisionAssetPath = IOUtil.ReadOffsetString(br, start - 48);
            ret.CollisionType = (ModelCollisionType)br.ReadInt32();
            ret.AttributeMask = br.ReadUInt32();
            ret.Attribute = br.ReadUInt32();
            ret.CollisionConfig = br.ReadInt32();
            ret.IsVisible = br.ReadByte();
            ret.RenderShadowEnabled = br.ReadByte();
            ret.RenderLightShadowEnabled = br.ReadByte();
            ret.Padding00 = br.ReadByte();
            ret.RenderModelClipRange = br.ReadSingle();

            return ret;
        }
    }
}
