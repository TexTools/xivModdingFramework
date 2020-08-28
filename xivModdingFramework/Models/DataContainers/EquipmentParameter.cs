using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Models.FileTypes;

namespace xivModdingFramework.Models.DataContainers
{

    public class GimmickParameter
    {
        public bool Enabled;
        public bool Animated;

        public ushort RotationA;
        public ushort RotationB;
        public ushort RotationC;

        public byte UnknownHigh;
        public byte UnknownLow;

        public GimmickParameter()
        {
            Enabled = false;
            Animated = false;

            RotationA = 0;
            RotationB = 0;
            RotationC = 0;

            UnknownHigh = 0;
            UnknownLow = 0;
        }
        public GimmickParameter(byte[] bytes)
        {

            var l = BitConverter.ToUInt32(bytes, 0);

            Enabled = (l & 1) > 0;
            Animated = (l & 2) > 0;

            var d1 = l >> 2;
            RotationA = (ushort)(d1 & 0x3FF);

            var d2 = l >> 12;
            RotationB = (ushort)(d2 & 0x3FF);

            var d3 = l >> 22;
            RotationC = (ushort)(d3 & 0x3FF);

            UnknownHigh = (byte)((bytes[4] >> 4) & 0x0F);
            UnknownLow = (byte)((bytes[4] & 0x0F));
        }

        /// <summary>
        /// Retrieves the raw bytewise representation of the parameter.
        /// </summary>
        /// <param name="enabled"></param>
        /// <param name="animated"></param>
        /// <param name="rotation1"></param>
        /// <param name="rotation2"></param>
        /// <param name="rotation3"></param>
        /// <returns></returns>
        public byte[] GetBytes()
        {
            int ret = 0;
            if (Enabled)
            {
                ret = ret | 1;
            }
            if (Animated)
            {
                ret = ret | 2;
            }

            int rot1 = (RotationA & 0x3FF) << 2;
            int rot2 = (RotationB & 0x3FF) << 12;
            int rot3 = (RotationC & 0x3FF) << 22;

            ret = ret | rot1;
            ret = ret | rot2;
            ret = ret | rot3;

            byte last = (byte)((UnknownHigh << 4) | (UnknownLow & 0x0F));

            var bytes = new byte[5];

            IOUtil.ReplaceBytesAt(bytes, BitConverter.GetBytes(ret), 0);
            bytes[4] = last;

            return bytes;
        }
    }

    public class EquipmentParameterSet
    {
        /// <summary>
        /// The actual parameters contained in this set, by Slot Abbreviation.
        /// Strings should match up to Mdl.SlotAbbreviationDictionary Keys
        /// This element should always contain 5 entries: [met, top, glv, dwn, sho]
        /// </summary>
        public Dictionary<string, EquipmentParameter> Parameters;

        // Entry order within the set.
        public static readonly List<string> EntryOrder = new List<string>()
        {
            "top", "dwn", "glv", "sho", "met"
        };

        // Byte sizes within the set.
        public static readonly Dictionary<string, int> EntrySizes = new Dictionary<string, int>()
        {
            { "top", 2 },
            { "dwn", 1 },
            { "glv", 1 },
            { "sho", 1 },
            { "met", 3 }
        };

        // Byte offsets within the set.
        public static readonly Dictionary<string, int> EntryOffsets = new Dictionary<string, int>()
        {
            { "top", 0 },
            { "dwn", 2 },
            { "glv", 3 },
            { "sho", 4 },
            { "met", 5 }
        };

        public EquipmentParameterSet(List<byte> rawBytes)
        {
            var slotBytes = new Dictionary<string, List<byte>>();
            slotBytes.Add("top", new List<byte>());
            slotBytes.Add("dwn", new List<byte>());
            slotBytes.Add("glv", new List<byte>());
            slotBytes.Add("sho", new List<byte>());
            slotBytes.Add("met", new List<byte>());

            slotBytes["top"].Add(rawBytes[0]);
            slotBytes["top"].Add(rawBytes[1]);
            slotBytes["dwn"].Add(rawBytes[2]);
            slotBytes["glv"].Add(rawBytes[3]);
            slotBytes["sho"].Add(rawBytes[4]);
            slotBytes["met"].Add(rawBytes[5]);
            slotBytes["met"].Add(rawBytes[6]);
            slotBytes["met"].Add(rawBytes[7]);

            Parameters = new Dictionary<string, EquipmentParameter>() {
                { "top", new EquipmentParameter("top", slotBytes["top"].ToArray()) },
                { "dwn", new EquipmentParameter("dwn", slotBytes["dwn"].ToArray()) },
                { "glv", new EquipmentParameter("glv", slotBytes["glv"].ToArray()) },
                { "sho", new EquipmentParameter("sho", slotBytes["sho"].ToArray()) },
                { "met", new EquipmentParameter("met", slotBytes["met"].ToArray()) }
            };
        }
        public static List<string> SlotsAsList()
        {
            return new List<string>() { "met", "top", "glv", "dwn", "sho" };
        }
    }

    /// <summary>
    /// Bitwise flags for the main equipment parameter 64 bit array.
    /// Flag names are set based on what they do when they are set to [1]
    /// </summary>
    public enum EquipmentParameterFlag
    {
        // For FULL GEAR PIECES, they're always marked as TRUE = Show
        // For PARTIAL GEAR PIECES, they're marked as TRUE = HIDE

        //Byte 0 - Body
        EnableBodyFlags = 0,
        BodyHideWaist = 1,
        Bit2 = 2,
        BodyHideShortGloves = 3, // Bit 3 OR Bit 4 is often set on Legacy gear.
        Bit4 = 4,                // Bit 3 OR Bit 4 is often set on Legacy gear.
        BodyHideMidGloves = 5,
        BodyHideLongGloves = 6,
        BodyHideGorget = 7,

        // Byte 1 - Body
        BodyShowLeg = 8,                // When turned off, Leg hiding data is resolved from this same set, rather than the set of the equipped piece.
        BodyShowHand = 9,               // When turned off, Hand hiding data is resolved from this same set, rather than the set of the equipped piece.
        BodyShowHead = 10,              // When turned off, Head hiding data is resolved from this same set, rather than the set of the equipped piece.
        BodyShowNecklace = 11,
        BodyShowBracelet = 12,          // "Wrist[slot]" is not used in this context b/c it can be confusing with other settings.
        BodyShowTail = 13,
        BodyTriggersomeShapeData = 14,
        Bit15 = 15,

        // Byte 2 - Leg
        EnableLegFlags = 16,
        Bit17 = 17,
        Bit18 = 18,
        LegHideHalfboot = 19,
        Bit20 = 20,
        LegShowFoot = 21,
        Bit22 = 22,
        Bit23 = 23,

        // Byte 3 - Hand
        EnableHandFlags = 24,
        HandHideElbow = 25,            // Requires bit 26 on as well to work.
        HandHideForearm = 26,          // "Wrist[anatomy]" is not used in this context b/c it can be confusing with other settings.
        Bit27 = 27,
        HandShowBracelet = 28,         // "Wrist[slot]" is not used in this context b/c it can be confusing with other settings.
        HandShowRingL = 29,
        HandShowRingR = 30,
        Bit31 = 31,

        // Byte 4 - Foot
        EnableFootFlags = 32,
        FootHideKnee = 33,              // Requires bit 34 on as well to work.
        FootHideCalf = 34,
        FootUsuallyOn = 35,             // Usually set to [1], the remaining bits of this byte are always [0].
        Bit36 = 36,
        Bit37 = 37,
        Bit38 = 38,
        Bit39 = 39,

        // Byte 5 - Head & Hair
        EnableHeadFlags = 40,
        HeadHideScalp = 41,          // When set alone, hides top(hat part) of hair.  When set with 42, hides everything.
        HeadHideHair = 42,             // When set with 41, hides everything neck up.  When set without, hides all hair.
        HeadShowHairOverride = 43,     // Overrides Bit 41 & 42 When set.
        HeadHideNeck = 44,
        HeadShowNecklace = 45,
        Bit46 = 46,
        HeadShowEarrings = 47,        // This cannot be toggled off without enabling bit 42.

        // Byte 6 - Ears/Horns/Etc.    
        HeadShowEarringsHuman = 48,
        HeadShowEarringsAura = 49,
        HeadShowEarHuman = 50,
        HeadShowEarMiqo = 51,
        HeadShowEarAura = 52,
        HeadShowEarViera = 53,
        HeadUnknownHelmet1 = 54,      // Usually set on for helmets, in place of 48/49
        HeadUnknownHelmet2 = 55,      // Usually set on for helmets, in place of 48/49

        // Byte 7 - Shadowbringers Race Settings
        EnableShbFlags = 56,
        ShbShowHead = 57,
        Bit58 = 58,
        Bit59 = 59,
        Bit60 = 60,
        Bit61 = 61,
        Bit62 = 62,
        Bit63 = 63,
    }


    /// <summary>
    /// Class representing an EquipmentParameter entry,
    /// mostly contains data relating to whether or not
    /// certain elements should be shown or hidden for 
    /// this piece of gear.
    /// </summary>
    public class EquipmentParameter
    {
        /// <summary>
        /// Slot abbreviation for ths parameter set.
        /// </summary>
        public readonly string Slot;

        /// <summary>
        /// The raw bits which make up this parameter.
        /// </summary>
        private BitArray _bits;


        /// <summary>
        /// The available flags for this EquipmentParameter.
        /// </summary>
        public List<EquipmentParameterFlag> AvailableFlags
        {
            get
            {
                return FlagOffsetDictionaries[Slot].Keys.ToList();
            }
        }

        /// <summary>
        /// Retrieves the list of all available flags, with their values.
        /// Changing the values will not affect the actual underlying data.
        /// </summary>
        /// <returns></returns>
        public Dictionary<EquipmentParameterFlag, bool> GetFlags()
        {
            var ret = new Dictionary<EquipmentParameterFlag, bool>();
            var flags = AvailableFlags;
            foreach (var flag in flags)
            {
                ret.Add(flag, GetFlag(flag));
            }
            return ret;
        }

        /// <summary>
        /// Set all (or a subset) of flags in this Parameter set at once.
        /// </summary>
        /// <param name="flags"></param>
        public void SetFlags(Dictionary<EquipmentParameterFlag, bool> flags)
        {
            foreach(var kv in flags)
            {
                SetFlag(kv.Key, kv.Value);
            }
        }

        /// <summary>
        /// Constructor.  Slot is required.
        /// </summary>
        /// <param name="slot"></param>
        public EquipmentParameter(string slot, byte[] rawBytes)
        {
            Slot = slot;
            _bits = new BitArray(rawBytes);
        }

        public bool GetFlag(EquipmentParameterFlag flag)
        {
            if(!FlagOffsetDictionaries[Slot].ContainsKey(flag))
                return false;

            var index = FlagOffsetDictionaries[Slot][flag];
            return _bits[index];
        }

        public void SetFlag(EquipmentParameterFlag flag, bool value)
        {
            if (!FlagOffsetDictionaries[Slot].ContainsKey(flag))
                return;

            var index = FlagOffsetDictionaries[Slot][flag];
            _bits[index] = value;
        }

        /// <summary>
        /// Gets the raw bytes of this EquipmentParameter.
        /// </summary>
        /// <returns></returns>
        public byte[]GetBytes()
        {
            byte[] bytes = new byte[_bits.Count / 8];
            _bits.CopyTo(bytes, 0);
            return bytes;
        }


        /// <summary>
        /// A dictionary of [Slot] => [Flag] => [Index within the slot's byte array] for each flag.
        /// </summary>
        public static Dictionary<string, Dictionary<EquipmentParameterFlag, int>> FlagOffsetDictionaries {
            get
            {
                var ret = new Dictionary<string, Dictionary<EquipmentParameterFlag, int>>() {
                    { "met", new Dictionary<EquipmentParameterFlag, int>() },
                    { "top", new Dictionary<EquipmentParameterFlag, int>() },
                    { "glv", new Dictionary<EquipmentParameterFlag, int>() },
                    { "dwn", new Dictionary<EquipmentParameterFlag, int>() },
                    { "sho", new Dictionary<EquipmentParameterFlag, int>() },
                };
                var flags = Enum.GetValues(typeof(EquipmentParameterFlag)).Cast<EquipmentParameterFlag>();

                foreach(var flag in flags)
                {
                    var raw = (int)flag;
                    var byteIndex = raw / 8;

                    // Find the slot that this byte belongs to.
                    var slotKv = EquipmentParameterSet.EntryOffsets.Reverse().First(x => x.Value <= byteIndex);
                    var slot = slotKv.Key;
                    var slotByteOffset = slotKv.Value;

                    // Compute the relevant bit position within the slot's grouping.
                    var relevantIndex = raw - (slotByteOffset * 8);

                    ret[slot].Add(flag, relevantIndex);
                }

                return ret;
            }
        }

    }
    

    /// <summary>
    /// Class representing an Equipment Deformation parameter for a given race/slot.
    /// </summary>
    public class EquipmentDeformationParameter
    {
        public bool bit0;
        public bool bit1;

        /// <summary>
        /// Gets a single byte representation of this entry.
        /// </summary>
        /// <returns></returns>
        public byte GetByte()
        {
            BitArray r = new BitArray(8);
            r[0] = bit0;
            r[1] = bit1;
            var arr = new byte[1];
            r.CopyTo(arr, 0);

            return arr[0];
        }

        /// <summary>
        /// Create a EquipmentDeformation Parameter from a full byte representation.
        /// </summary>
        /// <returns></returns>
        public static EquipmentDeformationParameter FromByte(byte b)
        {
            BitArray r = new BitArray(new byte[] { b });
            var def = new EquipmentDeformationParameter();
            def.bit0 = r[0];
            def.bit1 = r[1];

            return def;
        }
    }

    /// <summary>
    /// Class representing the set of equipment deformation parameters for a given race.
    /// </summary>
    public class EquipmentDeformationParameterSet
    {

        public bool IsAccessorySet = false;

        /// <summary>
        /// The actual parameters contained in this set, by Slot Abbreviation.
        /// Strings should match up to Mdl.SlotAbbreviationDictionary Keys
        /// This element should always contain 5 entries: [met, top, glv, dwn, sho]
        /// </summary>
        public Dictionary<string, EquipmentDeformationParameter> Parameters;
        public EquipmentDeformationParameterSet(bool accessory = false)
        {
            IsAccessorySet = accessory;

            if (accessory)
            {
                Parameters = new Dictionary<string, EquipmentDeformationParameter>() {
                    { "ear", new EquipmentDeformationParameter() },
                    { "nek", new EquipmentDeformationParameter() },
                    { "wrs", new EquipmentDeformationParameter() },
                    { "rir", new EquipmentDeformationParameter() },
                    { "ril", new EquipmentDeformationParameter() }
                };
            }
            else
            {
                Parameters = new Dictionary<string, EquipmentDeformationParameter>() {
                    { "met", new EquipmentDeformationParameter() },
                    { "top", new EquipmentDeformationParameter() },
                    { "glv", new EquipmentDeformationParameter() },
                    { "dwn", new EquipmentDeformationParameter() },
                    { "sho", new EquipmentDeformationParameter() }
                };
            }
        }

        public static List<string> SlotsAsList(bool accessory)
        {
            if(accessory)
            {
                return new List<string>() { "ear", "nek", "wrs", "rir", "ril" };
            } else
            {
                return new List<string>() { "met", "top", "glv", "dwn", "sho" };
            }
        }
    }
}
