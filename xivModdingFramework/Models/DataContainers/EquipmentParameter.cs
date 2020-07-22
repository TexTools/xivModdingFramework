using System;
using System.Collections.Generic;
using System.Text;
using xivModdingFramework.Models.FileTypes;

namespace xivModdingFramework.Models.DataContainers
{


    public class EquipmentParameterSet
    {
        /// <summary>
        /// The actual parameters contained in this set, by Slot Abbreviation.
        /// Strings should match up to Mdl.SlotAbbreviationDictionary Keys
        /// This element should always contain 5 entries: [met, top, glv, dwn, sho]
        /// </summary>
        public Dictionary<string, EquipmentParameter> Parameters;

        public EquipmentParameterSet()
        {
            Parameters = new Dictionary<string, EquipmentParameter>() {
                { "met", new EquipmentParameter("met") },
                { "top", new EquipmentParameter("top") },
                { "glv", new EquipmentParameter("glv") },
                { "dwn", new EquipmentParameter("dwn") },
                { "sho", new EquipmentParameter("sho") }
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
        //Byte 0 - Body
        EnableBodyFlags = 0,
        BodyHideWaist = 1,
        BodyPreventArmHiding = 6,
        BodyPreventNeckHiding = 7,

        // Byte 1 - Body
        BodyShowLeg = 8,                // When turned off, Leg hiding data is resolved from this same set, rather than the set of the equipped piece.
        BodyShowHand = 9,               // When turned off, Hand hiding data is resolved from this same set, rather than the set of the equipped piece.
        BodyShowHead = 10,              // When turned off, Head hiding data is resolved from this same set, rather than the set of the equipped piece.
        BodyShowNecklace = 11,
        BodyShowBracelet = 12,          // "Wrist[slot]" is not used in this context b/c it can be confusing with other settings.
        BodyHideTail = 14,

        // Byte 2 - Leg
        EnableLegFlags = 16,           
        LegShowFoot = 21,              

        // Byte 3 - Hand
        EnableHandFlags = 24,
        HandHideElbow = 25,            // Requires bit 26 on as well to work.
        HandHideForearm = 26,          // "Wrist[anatomy]" is not used in this context b/c it can be confusing with other settings.
        HandShowBracelet = 28,         // "Wrist[slot]" is not used in this context b/c it can be confusing with other settings.
        HandShowRingL = 29,
        HandShowRingR = 30,

        // Byte 4 - Foot
        EnableFootFlags = 32,
        FootHideKnee = 33,              // Requires bit 34 on as well to work.
        FootHideCalf = 34,
        FootUsuallyOn = 35,             // Usually set to [1], the remaining bits of this byte are always [0].

        // Byte 5 - Head & Hair
        EnableHeadFlags = 40,
        HeadHideHairTop = 41,          // When set alone, hides top(hat part) of hair.  When set with 42, hides everything.
        HeadHideHair = 42,             // When set with 41, hides everything neck up.  When set without, hides all hair.
        HeadShowHair = 43,             // Seems to override bit 42 if set?
        HeadHideNeck = 44,
        HeadShowNecklace = 45,
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
        ShbShowHead = 57
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
        /// The binary flag data for this equipment parameter.
        /// Only the subset of values for this slot will be available.
        /// </summary>
        public Dictionary<EquipmentParameterFlag, bool?> Flags;

        /// <summary>
        /// Constructor.  Slot is required.
        /// </summary>
        /// <param name="slot"></param>
        public EquipmentParameter(string slot)
        {
            Slot = slot;
            var keys = GetFlagList(slot);
            foreach(var key in keys)
            {
                Flags.Add(key, null);
            }
        }

        /// <summary>
        /// Get the list of dictionary keys available for this parameter slot.
        /// </summary>
        /// <returns></returns>
        public List<EquipmentParameterFlag> GetFlagList()
        {
            var ret = new List<EquipmentParameterFlag>(); ;
            foreach (var kv in Flags)
            {
                ret.Add(kv.Key);
            }
            return ret;

        }

        /// <summary>
        /// Gets the list of dictionary keys available for this slot type.
        /// </summary>
        /// <returns></returns>
        public static List<EquipmentParameterFlag> GetFlagList(string slot)
        {
            var ret = new List<EquipmentParameterFlag>();
            if (slot == "met")
            {
                // Head flags setup.

            }
            else if (slot == "top")
            {
                // Body flags setup.

            }
            else if (slot == "glv")
            {
                // Glove flags setup.

            }
            else if (slot == "dwn")
            {
                // Leg flags setup.

            }
            else if (slot == "sho")
            {
                // Foot flags setup.

            }
            return ret;
        }
    }
    

    /// <summary>
    /// Class representing an Equipment Deformation parameter for a given race/slot.
    /// </summary>
    public class EquipmentDeformationParameter
    {
        public bool bit0;
        public bool bit1;
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
