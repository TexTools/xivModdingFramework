using JsonSubTypes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using xivModdingFramework.Cache;
using xivModdingFramework.General.DataContainers;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Mods.FileTypes.PMP;
using xivModdingFramework.Variants.DataContainers;

namespace xivModdingFramework.Mods.FileTypes
{
    #region Metadata Manipulations

    [JsonConverter(typeof(JsonSubtypes), "Type")]
    [JsonSubtypes.FallBackSubType(typeof(PMPUnknownManipulationWrapperJson))]
    [JsonSubtypes.KnownSubType(typeof(PMPImcManipulationWrapperJson), "Imc")]
    [JsonSubtypes.KnownSubType(typeof(PMPEstManipulationWrapperJson), "Est")]
    [JsonSubtypes.KnownSubType(typeof(PMPEqpManipulationWrapperJson), "Eqp")]
    [JsonSubtypes.KnownSubType(typeof(PMPEqdpManipulationWrapperJson), "Eqdp")]
    [JsonSubtypes.KnownSubType(typeof(PMPGmpManipulationWrapperJson), "Gmp")]
    [JsonSubtypes.KnownSubType(typeof(PMPRspManipulationWrapperJson), "Rsp")]
    [JsonSubtypes.KnownSubType(typeof(PMPGlobalEqpManipulationWrapperJson), "GlobalEqp")]
    public class PMPManipulationWrapperJson
    {
        public string Type;

        public virtual object GetManipulation()
        {
            return null;
        }
        public virtual void SetManipulation(object o)
        {
            throw new NotImplementedException();
        }

        public virtual string GetNiceName()
        {
            return Type + " Manipulation";
        }
    }

    public class PMPUnknownManipulationWrapperJson : PMPManipulationWrapperJson
    {
        public object Manipulation;
        public override object GetManipulation()
        {
            return Manipulation;
        }
        public override void SetManipulation(object o)
        {
            Manipulation = o;
        }
    }

    public class PMPImcManipulationWrapperJson : PMPManipulationWrapperJson
    {
        public PMPImcManipulationJson Manipulation;
        public override object GetManipulation()
        {
            return Manipulation;
        }
        public override void SetManipulation(object o)
        {
            Manipulation = o as PMPImcManipulationJson;
        }
        public override string GetNiceName()
        {
            return base.GetNiceName() + " - " + Manipulation.ObjectType.ToString() + " #" + Manipulation.PrimaryId.ToString() + " " + Manipulation.EquipSlot + " v" + Manipulation.Variant;
        }
    }
    public class PMPEstManipulationWrapperJson : PMPManipulationWrapperJson
    {
        public PMPEstManipulationJson Manipulation;
        public override object GetManipulation()
        {
            return Manipulation;
        }
        public override void SetManipulation(object o)
        {
            Manipulation = o as PMPEstManipulationJson;
        }
        public override string GetNiceName()
        {
            return base.GetNiceName() + " - e" + Manipulation.SetId + " " + Manipulation.Slot + " " + Manipulation.Race + " " + Manipulation.Gender;
        }
    }
    public class PMPEqpManipulationWrapperJson : PMPManipulationWrapperJson
    {
        public PMPEqpManipulationJson Manipulation;
        public override object GetManipulation()
        {
            return Manipulation;
        }
        public override void SetManipulation(object o)
        {
            Manipulation = o as PMPEqpManipulationJson;
        }
        public override string GetNiceName()
        {
            return base.GetNiceName() + " - " + Manipulation.SetId + " " + Manipulation.Slot;
        }
    }
    public class PMPEqdpManipulationWrapperJson : PMPManipulationWrapperJson
    {
        public PMPEqdpManipulationJson Manipulation;
        public override object GetManipulation()
        {
            return Manipulation;
        }
        public override void SetManipulation(object o)
        {
            Manipulation = o as PMPEqdpManipulationJson;
        }
        public override string GetNiceName()
        {
            return base.GetNiceName() + " - " + Manipulation.SetId + " " + Manipulation.Slot + " " + Manipulation.Race.ToString() + " " + Manipulation.Gender;
        }
    }
    public class PMPGmpManipulationWrapperJson : PMPManipulationWrapperJson
    {
        public PMPGmpManipulationJson Manipulation;
        public override object GetManipulation()
        {
            return Manipulation;
        }
        public override void SetManipulation(object o)
        {
            Manipulation = o as PMPGmpManipulationJson;
        }
        public override string GetNiceName()
        {
            return base.GetNiceName() + " - e" + Manipulation.SetId;
        }
    }
    public class PMPRspManipulationWrapperJson : PMPManipulationWrapperJson
    {
        public PMPRspManipulationJson Manipulation;
        public override object GetManipulation()
        {
            return Manipulation;
        }
        public override void SetManipulation(object o)
        {
            Manipulation = o as PMPRspManipulationJson;
        }
        public override string GetNiceName()
        {
            return base.GetNiceName() + " - " + Manipulation.SubRace + " " + Manipulation.Attribute;
        }
    }
    public class PMPGlobalEqpManipulationWrapperJson : PMPManipulationWrapperJson
    {
        public PMPGlobalEqpManipulationJson Manipulation;
        public override object GetManipulation()
        {
            return Manipulation;
        }
        public override void SetManipulation(object o)
        {
            Manipulation = o as PMPGlobalEqpManipulationJson;
        }
        public override string GetNiceName()
        {
            return base.GetNiceName() + " - " + Manipulation.Type + " " + Manipulation.Condition;
        }
    }


    public interface IPMPItemMetadata
    {
        /// <summary>
        /// Retrieves this 
        /// </summary>
        /// <returns></returns>
        public XivDependencyRoot GetRoot();

        /// <summary>
        /// Applies this metadata entry's effects to the given item's metadata.
        /// </summary>
        /// <param name="metadata"></param>
        public void ApplyToMetadata(ItemMetadata metadata);
    }
    public class PMPEstManipulationJson : IPMPItemMetadata
    {
        public ushort Entry = 0;
        public PMPGender Gender;
        public PMPModelRace Race;
        public ushort SetId;
        public PMPEquipSlot Slot;

        public XivDependencyRoot GetRoot()
        {
            var root = PMPExtensions.GetRootFromPenumbraValues(PMPObjectType.Equipment, SetId, PMPObjectType.Unknown, 0, Slot);
            return new XivDependencyRoot(root);
        }
        public void ApplyToMetadata(ItemMetadata metadata)
        {
            if (Gender == PMPGender.MaleNpc || Gender == PMPGender.FemaleNpc || Gender == PMPGender.Unknown)
            {
                // TexTools doesn't have handling/setup for NPC RGSP entries.
                // Could be added, but seems not worth the effort for how niche it is.
                return;
            }

            var race = PMPExtensions.GetRaceFromPenumbraValue(Race, Gender);
            var entry = metadata.EstEntries[race];

            // EST Entries are simple, they're just a single UShort value for a skel ID.
            // Penumbra and TT represent it identically.
            entry.SkelId = Entry;
        }

        public static PMPEstManipulationJson FromEstEntry(ExtraSkeletonEntry entry, string gameSlot)
        {
            if (entry.Race == XivRace.All_Races)
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(gameSlot))
            {
                return null;
            }

            var rg = PMPExtensions.GetPMPRaceGenderFromXivRace(entry.Race);
            var slot = PMPExtensions.PenumbraSlotToGameSlot.First(x => x.Value == gameSlot).Key;
            var setId = (uint)entry.SetId;

            var est = new PMPEstManipulationJson()
            {
                Entry = entry.SkelId,
                Slot = slot,
                Race = rg.Race,
                Gender = rg.Gender,
                SetId = entry.SetId
            };

            return est;
        }
    }
    public class PMPImcManipulationJson : IPMPItemMetadata
    {
        public struct PMPImcEntry
        {
            public byte MaterialId;
            public byte DecalId;
            public byte VfxId;
            public byte MaterialAnimationId;

            // Are these redundantly stored?
            public ushort AttributeAndSound;
            public ushort AttributeMask;
            public byte SoundId;

            public XivImc ToXivImc()
            {
                var imc = new XivImc();
                imc.Animation = MaterialAnimationId;
                imc.AttributeMask = AttributeMask;
                imc.SoundId = SoundId;
                imc.MaterialSet = MaterialId;
                imc.Decal = DecalId;
                imc.Vfx = VfxId;
                return imc;
            }
            public static PMPImcEntry FromXivImc(XivImc imc)
            {
                var entry = new PMPImcEntry();
                entry.MaterialAnimationId = imc.Animation;
                entry.AttributeMask  = imc.AttributeMask;
                entry.SoundId = imc.SoundId;
                entry.MaterialId = imc.MaterialSet;
                entry.DecalId = imc.Decal;
                entry.VfxId = imc.Vfx;
                return entry;
            }
        }
        public PMPImcEntry Entry;
        public uint PrimaryId;
        public uint SecondaryId;
        public uint Variant;
        public PMPObjectType ObjectType;
        public PMPEquipSlot EquipSlot;
        public PMPObjectType BodySlot;

        public XivDependencyRoot GetRoot()
        {
            var root = PMPExtensions.GetRootFromPenumbraValues(ObjectType, PrimaryId, BodySlot, SecondaryId, EquipSlot);
            return new XivDependencyRoot(root);
        }
        public void ApplyToMetadata(ItemMetadata metadata)
        {
            // This one's a little funky.
            // Variants are really just an index into the IMC List.
            // If we're shallow, we have to add extras.
            // But there's no strictly defined behavior for what to do with the extras.
            // Technically speaking, Penumbra doesn't even allow users to add IMC entries, but TexTools does.
            while (Variant >= metadata.ImcEntries.Count)
            {
                if (metadata.ImcEntries.Count > 0)
                {
                    // Clone Group 0 if we have one.
                    metadata.ImcEntries.Add((XivImc)metadata.ImcEntries[0].Clone());
                }
                else
                {
                    // Add a blank otherwise.
                    metadata.ImcEntries.Add(new XivImc());
                }
            }

            // Outside of variant shenanigans, TT and Penumbra store these identically.
            var imc = metadata.ImcEntries[(int)Variant];
            imc.Decal = Entry.DecalId;
            imc.Animation = Entry.MaterialAnimationId;
            imc.MaterialSet = Entry.MaterialId;
            imc.Mask = Entry.AttributeAndSound;
        }
        public static PMPImcManipulationJson FromImcEntry(XivImc entry, int variant, XivDependencyRootInfo root)
        {
            // TODO: Determine if Penumbra can handle extra IMC sets.
            var pEntry = new PMPImcManipulationJson();

            pEntry.ObjectType = PMPExtensions.XivItemTypeToPenumbraObject[root.PrimaryType];
            pEntry.BodySlot = root.SecondaryType == null ? PMPObjectType.Unknown : PMPExtensions.XivItemTypeToPenumbraObject[root.SecondaryType.Value];
            pEntry.PrimaryId = (uint)root.PrimaryId;
            pEntry.SecondaryId = (uint)(root.SecondaryId == null ? 0 : root.SecondaryId);
            pEntry.Variant = (uint)variant;

            pEntry.EquipSlot = PMPEquipSlot.Unknown;
            if (root.Slot != null)
            {
                pEntry.EquipSlot = PMPExtensions.PenumbraSlotToGameSlot.First(x => x.Value == root.Slot).Key;
            }

            pEntry.Entry.AttributeAndSound = entry.Mask;
            pEntry.Entry.MaterialId = entry.MaterialSet;
            pEntry.Entry.DecalId = entry.Decal;
            pEntry.Entry.VfxId = entry.Vfx;
            pEntry.Entry.MaterialAnimationId = entry.Animation;

            pEntry.Entry.SoundId = (byte)(entry.Mask >> 10);
            pEntry.Entry.AttributeMask = (ushort)(entry.Mask & 0x3FF);

            return pEntry;
        }
    }
    public class PMPEqdpManipulationJson : IPMPItemMetadata
    {
        public ushort Entry;
        public PMPGender Gender;
        public PMPModelRace Race;
        public uint SetId;
        public PMPEquipSlot Slot;

        public XivDependencyRoot GetRoot()
        {
            var isAccessory = PMPExtensions.IsAccessory(Slot);
            var type = isAccessory ? PMPObjectType.Accessory : PMPObjectType.Equipment;
            var root = PMPExtensions.GetRootFromPenumbraValues(type, SetId, PMPObjectType.Unknown, 0, Slot);
            return new XivDependencyRoot(root);
        }
        public void ApplyToMetadata(ItemMetadata metadata)
        {
            if (Gender == PMPGender.MaleNpc || Gender == PMPGender.FemaleNpc || Gender == PMPGender.Unknown)
            {
                // TexTools doesn't have handling/setup for NPC RGSP entries.
                // Could be added, but seems not worth the effort for how niche it is.
                return;
            }

            // Get Race.
            var xivRace = PMPExtensions.GetRaceFromPenumbraValue(Race, Gender);

            // Get the slot number.
            var slot = PMPExtensions.PenumbraSlotToGameSlot[Slot];
            var isAccessory = EquipmentDeformationParameterSet.IsAccessory(slot);
            var slotNum = EquipmentDeformationParameterSet.SlotsAsList(isAccessory).IndexOf(slot);

            // Penumbra stores the data masked in-place.  We need to shift it over.
            var shift = slotNum * 2;
            var shifted = Entry >> shift;

            // Tag bits.
            metadata.EqdpEntries[xivRace].bit0 = (shifted & 0x01) != 0;
            metadata.EqdpEntries[xivRace].bit1 = (shifted & 0x02) != 0;
        }

        public static PMPEqdpManipulationJson FromEqdpEntry(EquipmentDeformationParameter entry, XivDependencyRootInfo root, XivRace race)
        {
            var isAccessory = EquipmentDeformationParameterSet.IsAccessory(root.Slot);
            var slotNum = EquipmentDeformationParameterSet.SlotsAsList(isAccessory).IndexOf(root.Slot);

            // Penumbra stores the data masked in-place.  We need to shift it over.
            var shift = slotNum * 2;
            var bits = (ushort)0;
            if (entry.bit0)
            {
                bits |= 1;
            }
            if (entry.bit1)
            {
                bits |= 2;
            }

            bits = (ushort)(bits << shift);
            var rg = PMPExtensions.GetPMPRaceGenderFromXivRace(race);
            var slot = PMPExtensions.PenumbraSlotToGameSlot.First(x => x.Value == root.Slot).Key;
            var setId = (uint)root.PrimaryId;

            var pEntry = new PMPEqdpManipulationJson()
            {
                Entry = bits,
                Gender = rg.Gender,
                Race = rg.Race,
                SetId = setId,
                Slot = slot,
            };
            return pEntry;
        }
    }
    public class PMPEqpManipulationJson : IPMPItemMetadata
    {
        public ulong Entry;
        public uint SetId;
        public PMPEquipSlot Slot;

        public XivDependencyRoot GetRoot()
        {
            var root = PMPExtensions.GetRootFromPenumbraValues(PMPObjectType.Equipment, SetId, PMPObjectType.Unknown, 0, Slot);
            return new XivDependencyRoot(root);
        }
        public void ApplyToMetadata(ItemMetadata metadata)
        {
            // Like with EQDP data, Penumbra stores EQP data in-place, masked.
            // TT stores the data sliced up and bit shifted down to LSB.
            // So we need to determine data size, shift it down, and slice out the bytes we want.

            var slot = PMPExtensions.PenumbraSlotToGameSlot[Slot];
            var offset = EquipmentParameterSet.EntryOffsets[slot];
            var size = EquipmentParameterSet.EntrySizes[slot];

            var shifted = Entry >> (offset * 8);
            var shiftedBytes = BitConverter.GetBytes(shifted);

            var data = new byte[size];
            Array.Copy(shiftedBytes, 0, data, 0, size);

            var eqp = metadata.EqpEntry;
            eqp.SetBytes(data);
        }

        public static PMPEqpManipulationJson FromEqpEntry(EquipmentParameter entry, XivDependencyRootInfo root)
        {

            var slot = PMPExtensions.PenumbraSlotToGameSlot.First(x => x.Value == entry.Slot).Key;
            var offset = EquipmentParameterSet.EntryOffsets[entry.Slot];
            var size = EquipmentParameterSet.EntrySizes[entry.Slot];

            // Re-shift the value for Penumbra.
            var arr = new byte[8];
            var ebytes = entry.GetBytes();
            Array.Copy(ebytes, 0, arr, 0, ebytes.Length);

            ulong value = BitConverter.ToUInt64(arr, 0);
            value = (ulong)(value << offset * 8);

            var pEntry = new PMPEqpManipulationJson()
            {
                Entry = value,
                SetId = (uint)root.PrimaryId,
                Slot = slot
            };

            return pEntry;
        }
    }
    public class PMPGmpManipulationJson : IPMPItemMetadata
    {
        public struct PMPGmpEntry
        {
            public bool Enabled;
            public bool Animated;
            public ushort RotationA;
            public ushort RotationB;
            public ushort RotationC;

            public byte UnknownA;
            public byte UnknownB;
            public ushort UnknownTotal;

            // Full Value
            public ulong Value;
        }

        public PMPGmpEntry Entry;
        public uint SetId;

        public XivDependencyRoot GetRoot()
        {
            var root = PMPExtensions.GetRootFromPenumbraValues(PMPObjectType.Equipment, SetId, PMPObjectType.Unknown, 0, PMPEquipSlot.Head);
            return new XivDependencyRoot(root);
        }

        public void ApplyToMetadata(ItemMetadata metadata)
        {
            // We could bind the data individually...
            // Or we could just copy in the full uint Value since it's stored here.

            var gmp = new GimmickParameter(BitConverter.GetBytes(Entry.Value));
            metadata.GmpEntry = gmp;
        }
        public static PMPGmpManipulationJson FromGmpEntry(GimmickParameter entry, XivDependencyRootInfo root)
        {
            var pEntry = new PMPGmpManipulationJson();

            pEntry.SetId = (uint)root.PrimaryId;
            pEntry.Entry.Value = BitConverter.ToUInt32(entry.GetBytes(), 0);
            pEntry.Entry.Animated = entry.Animated;
            pEntry.Entry.Enabled = entry.Enabled;
            pEntry.Entry.RotationA = entry.RotationA;
            pEntry.Entry.RotationB = entry.RotationB;
            pEntry.Entry.RotationC = entry.RotationC;
            pEntry.Entry.UnknownA = entry.UnknownLow;
            pEntry.Entry.UnknownB = entry.UnknownHigh;

            return pEntry;
        }
    }
    public class PMPRspManipulationJson
    {
        public float Entry;
        public PMPSubRace SubRace;
        public RspAttribute Attribute;

        /// <summary>
        /// Simple function to return a consistent race/gender hash for grouping.
        /// </summary>
        /// <returns></returns>
        public uint GetRaceGenderHash()
        {
            var rg = GetRaceGender();

            return GetRaceGenderHash(rg.Race, rg.Gender);
        }
        public static uint GetRaceGenderHash(XivSubRace race, XivGender gender)
        {
            uint hash = 0;
            hash |= ((uint)race) << 8;
            hash |= ((uint)gender);

            return hash;
        }

        public (XivSubRace Race, XivGender Gender) GetRaceGender()
        {
            var pmpGen = Attribute.ToPMPGender();

            if (pmpGen == PMPGender.MaleNpc || pmpGen == PMPGender.FemaleNpc || pmpGen == PMPGender.Unknown)
            {
                // TexTools doesn't have handling/setup for NPC RGSP entries.
                // Could be added, but seems not worth the effort for how niche it is.
                return (XivSubRace.Invalid, XivGender.Male);
            }

            var xivRace = PMPExtensions.PMPSubraceToXivSubrace[SubRace];
            var xivGen = PMPExtensions.PMPGenderToXivGender[pmpGen];
            return (xivRace, xivGen);
        }

        /// <summary>
        /// Apply this scaling modifier to the given RacialScalingParameter.
        /// </summary>
        /// <param name="cmp"></param>
        internal void ApplyScaling(RacialGenderScalingParameter cmp)
        {
            switch (Attribute)
            {
                case RspAttribute.BustMinX:
                    cmp.BustMinX = Entry;
                    break;
                case RspAttribute.BustMaxX:
                    cmp.BustMaxX = Entry;
                    break;
                case RspAttribute.BustMinY:
                    cmp.BustMinY = Entry;
                    break;
                case RspAttribute.BustMaxY:
                    cmp.BustMaxY = Entry;
                    break;
                case RspAttribute.BustMinZ:
                    cmp.BustMinZ = Entry;
                    break;
                case RspAttribute.BustMaxZ:
                    cmp.BustMaxZ = Entry;
                    break;
                case RspAttribute.MaleMinTail:
                case RspAttribute.FemaleMinTail:
                    cmp.MinTail = Entry;
                    break;
                case RspAttribute.MaleMaxTail:
                case RspAttribute.FemaleMaxTail:
                    cmp.MaxTail = Entry;
                    break;
                case RspAttribute.MaleMinSize:
                case RspAttribute.FemaleMinSize:
                    cmp.MinSize = Entry;
                    break;
                case RspAttribute.MaleMaxSize:
                case RspAttribute.FemaleMaxSize:
                    cmp.MaxSize = Entry;
                    break;
            }
            return;
        }


        private static PMPRspManipulationJson GetManip(PMPSubRace sr, RspAttribute attribute, float value)
        {
            return new PMPRspManipulationJson()
            {
                Attribute = attribute,
                Entry = value,
                SubRace = sr,
            };
        }

        public static List<PMPRspManipulationJson> FromRgspEntry(RacialGenderScalingParameter fullEntry)
        {
            var sr = PMPExtensions.PMPSubraceToXivSubrace.First(x => x.Value == fullEntry.Race).Key;
            var male = fullEntry.Gender == XivGender.Male;

            var manips = new List<PMPRspManipulationJson>();

            if (male)
            {
                manips.Add(GetManip(sr, RspAttribute.MaleMinTail, fullEntry.MinTail));
                manips.Add(GetManip(sr, RspAttribute.MaleMaxTail, fullEntry.MaxTail));
                manips.Add(GetManip(sr, RspAttribute.MaleMinSize, fullEntry.MinSize));
                manips.Add(GetManip(sr, RspAttribute.MaleMaxSize, fullEntry.MaxSize));
            }
            else
            {
                manips.Add(GetManip(sr, RspAttribute.BustMinX, fullEntry.BustMinX));
                manips.Add(GetManip(sr, RspAttribute.BustMaxX, fullEntry.BustMaxX));
                manips.Add(GetManip(sr, RspAttribute.BustMinY, fullEntry.BustMinY));
                manips.Add(GetManip(sr, RspAttribute.BustMaxY, fullEntry.BustMaxY));
                manips.Add(GetManip(sr, RspAttribute.BustMinZ, fullEntry.BustMinZ));
                manips.Add(GetManip(sr, RspAttribute.BustMaxY, fullEntry.BustMaxZ));
                manips.Add(GetManip(sr, RspAttribute.FemaleMinTail, fullEntry.MinTail));
                manips.Add(GetManip(sr, RspAttribute.FemaleMaxTail, fullEntry.MaxTail));
                manips.Add(GetManip(sr, RspAttribute.FemaleMinSize, fullEntry.MinSize));
                manips.Add(GetManip(sr, RspAttribute.FemaleMaxSize, fullEntry.MaxSize));
            }
            return manips;
        }

    }
    public class PMPGlobalEqpManipulationJson
    {
        public GlobalEqpType Type;
        public int Condition;
    }

    #endregion

}
