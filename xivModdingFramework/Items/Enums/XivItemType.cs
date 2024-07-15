// xivModdingFramework
// Copyright © 2018 Rafael Gonzalez - All Rights Reserved
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Resources;

namespace xivModdingFramework.Items.Enums
{
    /// <summary>
    /// Enum containing the types of items
    /// These are the FFXIV file system divisions of item types.
    /// </summary>
    public enum XivItemType
    {
        [Description("")] unknown,
        [Description("")] none,
        [Description("weapon")] weapon,
        [Description("equipment")] equipment,
        [Description("accessory")] accessory,
        [Description("monster")] monster,
        [Description("demihuman")] demihuman,
        [Description("body")] body,
        [Description("hair")] hair,
        [Description("tail")] tail,
        [Description("zear")] ear,
        [Description("face")] face,
        [Description("human")] human,
        [Description("")] decal,
        [Description("")] ui,
        [Description("indoor")] indoor,
        [Description("outdoor")] outdoor,
        [Description("pic")] painting,
        [Description("gyo")] fish
    }

    public enum XivWeaponType
    {
        Shield,
        Sword,
        Fists,
        FistsOff,
        FistsGloves,
        Axe,
        Lance,
        Bow,
        BowOff,
        Wand,
        Staff,
        Broadsword,
        Book,
        Daggers,
        DaggersOff,
        Gun,
        GunOff,
        Orrery,
        OrreryOff,
        Katana,
        KatanaOff,
        Rapier,
        RapierOff,
        Cane,
        Gunblade,
        Glaives,
        GlaivesOff,
        Nouliths,
        Scythe,
        Brush,
        Palette,
        Twinfangs,
        TwinfangsOff,
        Whip,
        Saw,
        ClawHammer,
        CrossPeinHammer,
        File,
        RaisingHammer,
        Pliers,
        LapidaryHammer,
        GrindingWheel,
        RoundKnife,
        Awl,
        Needle,
        SpinningWheel,
        Alembic,
        Mortar,
        Frypan,
        CulinaryKnife,
        Pickaxe,
        Sledgehammer,
        Hatchet,
        GardenScythe,
        FishingRod,
        Gig,
        Food,
        Unknown,
    }

    public static class XivWeaponTypes
    {
        public static string GetNiceName(this XivWeaponType type)
        {
            return type switch
            {
                XivWeaponType.Shield => "Shields",
                XivWeaponType.Sword => "Paladin Arms",
                XivWeaponType.Fists => "Monk Arms",
                XivWeaponType.FistsOff => "Monk Arms",
                XivWeaponType.FistsGloves => "Monk Arms",
                XivWeaponType.Axe => "Warrior Arms",

                XivWeaponType.Lance => "Dragoon Arms",
                XivWeaponType.Bow => "Bard Arms",
                XivWeaponType.BowOff => "Bard Arms",
                XivWeaponType.Wand => "Black Mage Arms",
                XivWeaponType.Staff => "White Mage Arms",
                XivWeaponType.Broadsword => "Dark Knight Arms",
                XivWeaponType.Book => "Summoner-Scholar Arms",
                XivWeaponType.Daggers => "Ninja Arms",
                XivWeaponType.DaggersOff => "Ninja Arms",
                XivWeaponType.Gun => "Machinist Arms",
                XivWeaponType.GunOff => "Machinist Arms",
                XivWeaponType.Orrery => "Astrologian Arms",
                XivWeaponType.OrreryOff => "Astrologian Arms",
                XivWeaponType.Katana => "Samurai Arms",
                XivWeaponType.KatanaOff => "Samurai Arms",
                XivWeaponType.Rapier => "Red Mage Arms",
                XivWeaponType.RapierOff => "Red Mage Arms",
                XivWeaponType.Cane => "Blue Mage Arms",
                XivWeaponType.Gunblade => "Gunbreaker Arms",
                XivWeaponType.Glaives => "Dancer Arms",
                XivWeaponType.GlaivesOff => "Dancer Arms",
                XivWeaponType.Nouliths => "Sage Arms",
                XivWeaponType.Scythe => "Reaper Arms",
                XivWeaponType.Brush => "Pictomancer Arms",
                XivWeaponType.Palette => "Pictomancer Arms",
                XivWeaponType.Twinfangs => "Viper Arms",
                XivWeaponType.TwinfangsOff => "Viper Arms",
                XivWeaponType.Whip => "Whips",
                XivWeaponType.Saw => "Carpenter Arms",
                XivWeaponType.ClawHammer => "Carpenter Arms",
                XivWeaponType.CrossPeinHammer => "Blacksmith Arms",
                XivWeaponType.File => "Blacksmith Arms",
                XivWeaponType.RaisingHammer => "Armorer Arms",
                XivWeaponType.Pliers => "Armorer Arms",
                XivWeaponType.LapidaryHammer => "Goldsmith Arms",
                XivWeaponType.GrindingWheel => "Goldsmith Arms",
                XivWeaponType.RoundKnife => "Leatherworker Arms",
                XivWeaponType.Awl => "Leatherworker Arms",
                XivWeaponType.Needle => "Weaver Arms",
                XivWeaponType.SpinningWheel => "Weaver Arms",
                XivWeaponType.Alembic => "Alchemist Arms",
                XivWeaponType.Mortar => "Alchemist Arms",
                XivWeaponType.Frypan => "Culinarian Arms",
                XivWeaponType.CulinaryKnife => "Culinarian Arms",
                XivWeaponType.Pickaxe => "Miner Arms",
                XivWeaponType.Sledgehammer => "Miner Arms",
                XivWeaponType.Hatchet => "Botanist Arms",
                XivWeaponType.GardenScythe => "Botanist Arms",
                XivWeaponType.FishingRod => "Fisher Arms",
                XivWeaponType.Gig => "Fisher Arms",
                XivWeaponType.Food => "Food",


                _ => type.ToString(),
            };
        }
        public static XivWeaponType GetWeaponType(int value)
        {
            return value switch
            {
                > 0100 and <= 0200 => XivWeaponType.Shield,
                > 0200 and <= 0300 => XivWeaponType.Sword,
                > 0300 and <= 0350 => XivWeaponType.Fists,
                > 0350 and <= 0400 => XivWeaponType.FistsOff,
                > 0400 and <= 0500 => XivWeaponType.Axe,
                > 0500 and <= 0600 => XivWeaponType.Lance,
                > 0600 and <= 0650 => XivWeaponType.Bow,
                > 0650 and <= 0700 => XivWeaponType.BowOff,
                > 0700 and <= 0800 => XivWeaponType.Wand,
                > 0800 and <= 0900 => XivWeaponType.Staff,
                > 0900 and <= 1000 => XivWeaponType.Wand,
                > 1000 and <= 1100 => XivWeaponType.Staff,
                > 1500 and <= 1600 => XivWeaponType.Broadsword,
                > 1600 and <= 1650 => XivWeaponType.Fists,
                > 1650 and <= 1700 => XivWeaponType.FistsOff,
                > 1700 and <= 1800 => XivWeaponType.Book,
                > 1800 and <= 1850 => XivWeaponType.Daggers,
                > 1850 and <= 1900 => XivWeaponType.DaggersOff,
                > 2000 and <= 2050 => XivWeaponType.Gun,
                > 2050 and <= 2100 => XivWeaponType.GunOff,
                > 2100 and <= 2150 => XivWeaponType.Orrery,
                > 2150 and <= 2200 => XivWeaponType.OrreryOff,
                > 2200 and <= 2250 => XivWeaponType.Katana,
                > 2250 and <= 2300 => XivWeaponType.KatanaOff,
                > 2300 and <= 2350 => XivWeaponType.Rapier,
                > 2350 and <= 2400 => XivWeaponType.RapierOff,
                > 2400 and <= 2500 => XivWeaponType.Cane,
                > 2500 and <= 2600 => XivWeaponType.Gunblade,
                > 2600 and <= 2650 => XivWeaponType.Glaives,
                > 2650 and <= 2700 => XivWeaponType.GlaivesOff,
                > 2700 and <= 2800 => XivWeaponType.Nouliths,
                > 2800 and <= 2900 => XivWeaponType.Scythe,
                > 2900 and <= 2950 => XivWeaponType.Brush,
                > 2950 and <= 3000 => XivWeaponType.Palette,
                > 3000 and <= 3050 => XivWeaponType.Twinfangs,
                > 3050 and <= 3100 => XivWeaponType.TwinfangsOff,
                > 3100 and <= 3150 => XivWeaponType.Twinfangs,
                > 3150 and <= 3200 => XivWeaponType.TwinfangsOff,
                > 3200 and <= 3300 => XivWeaponType.Whip,
                > 5000 and <= 5040 => XivWeaponType.Saw,
                > 5040 and <= 5100 => XivWeaponType.ClawHammer,
                > 5100 and <= 5140 => XivWeaponType.CrossPeinHammer,
                > 5140 and <= 5200 => XivWeaponType.File,
                > 5200 and <= 5240 => XivWeaponType.RaisingHammer,
                > 5240 and <= 5300 => XivWeaponType.Pliers,
                > 5300 and <= 5340 => XivWeaponType.LapidaryHammer,
                > 5340 and <= 5400 => XivWeaponType.GrindingWheel,
                > 5400 and <= 5440 => XivWeaponType.RoundKnife,
                > 5440 and <= 5500 => XivWeaponType.Awl,
                > 5500 and <= 5540 => XivWeaponType.Needle,
                > 5540 and <= 5600 => XivWeaponType.SpinningWheel,
                > 5600 and <= 5640 => XivWeaponType.Alembic,
                > 5640 and <= 5700 => XivWeaponType.Mortar,
                > 5700 and <= 5740 => XivWeaponType.Frypan,
                > 5740 and <= 5800 => XivWeaponType.CulinaryKnife,
                > 7000 and <= 7050 => XivWeaponType.Pickaxe,
                > 7050 and <= 7100 => XivWeaponType.Sledgehammer,
                > 7100 and <= 7150 => XivWeaponType.Hatchet,
                > 7150 and <= 7200 => XivWeaponType.GardenScythe,
                > 7200 and <= 7250 => XivWeaponType.FishingRod,
                > 7250 and <= 7300 => XivWeaponType.Gig,
                > 8800 and <= 8900 => XivWeaponType.FistsGloves,
                > 9000 and <= 10000 => XivWeaponType.Food,
                _ => XivWeaponType.Unknown,
            };
        }
    }

    public static class XivItemTypes {

        public static Dictionary<XivItemType, string> NiceNames = new Dictionary<XivItemType, string>
        {
            { XivItemType.unknown, XivStrings.Unknown },
            { XivItemType.none, XivStrings.None },
            { XivItemType.weapon, XivStrings.Weapon },
            { XivItemType.equipment, XivStrings.Equipment },
            { XivItemType.accessory, XivStrings.Accessory },
            { XivItemType.monster, XivStrings.Monster },
            { XivItemType.demihuman, XivStrings.DemiHuman },
            { XivItemType.body, XivStrings.Body },
            { XivItemType.hair, XivStrings.Hair },
            { XivItemType.tail, XivStrings.Tail },
            { XivItemType.ear, XivStrings.Earring },
            { XivItemType.face, XivStrings.Face },
            { XivItemType.human, XivStrings.Human },
            { XivItemType.decal, XivStrings.Decal },
            { XivItemType.ui, XivStrings.UI },
            { XivItemType.indoor, XivStrings.Furniture_Indoor },
            { XivItemType.outdoor, XivStrings.Furniture_Outdoor },
            { XivItemType.painting, XivStrings.Paintings },
            { XivItemType.fish, XivStrings.Fish },
        };

        private static Dictionary<XivItemType, string> typeToSystemNameDict = new();
        private static Dictionary<string, XivItemType> systemNameToTypeDict = new();
        private static Dictionary<char, XivItemType> systemPrefixToTypeDict = new();

        static XivItemTypes()
        {
            foreach (XivItemType type in (XivItemType[])Enum.GetValues(typeof(XivItemType)))
            {
                var field = type.GetType().GetField(type.ToString());
                var attribute = (DescriptionAttribute[])field.GetCustomAttributes(typeof(DescriptionAttribute), false);
                if (attribute.Length > 0)
                {
                    var systemName = attribute[0].Description;
                    if (systemName.Length > 0)
                    {
                        if (systemName == "human")
                            systemPrefixToTypeDict['c'] = type;
                        else
                            systemPrefixToTypeDict[systemName[0]] = type;
                    }
                    typeToSystemNameDict[type] = systemName;
                    systemNameToTypeDict[systemName] = type;
                }
                else
                {
                    typeToSystemNameDict[type] = type.ToString();
                    systemNameToTypeDict[type.ToString()] = type;
                }
            }
            int x = 1;
        }

        /// <summary>
        /// Gets the file type prefix for the enum value from its description.
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The race code</returns>
        public static string GetSystemPrefix(this XivItemType value)
        {
            if(value == XivItemType.human)
            {
                // this one's weird.
                return "c";
            }

            var name = GetSystemName(value);
            var letter = "";
            if(name.Length > 0)
            {
                letter = name[0].ToString();
            }
            return letter;
        }

        /// <summary>
        /// Retrieves an XivItemType from a system name.
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The race code</returns>
        public static XivItemType FromSystemPrefix(char value)
        {
            var result = XivItemType.unknown;
            systemPrefixToTypeDict.TryGetValue(value, out result);
            return result;
        }


        /// <summary>
        /// Gets the file type prefix for the enum value from its description.
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The race code</returns>
        public static string GetSystemName(this XivItemType value)
        {
            return typeToSystemNameDict[value];
        }

        /// <summary>
        /// Retrieves an XivItemType from a system name.
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The race code</returns>
        public static XivItemType FromSystemName(string value)
        {
            var result = XivItemType.unknown;
            systemNameToTypeDict.TryGetValue(value, out result);
            return result;
        }

        /// <summary>
        /// The available slots for a given type.
        /// Note - This doesn't accurately reflect HUMAN-BODY specifically, as that is a 
        /// wild, extremely messy exception case.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static List<string> GetAvailableSlots(this XivItemType type)
        {
            if(type == XivItemType.equipment)
            {
                return new List<string> { "met", "top", "glv", "dwn", "sho" };
            } else if(type == XivItemType.accessory)
            {
                return new List<string> { "ear", "nek", "wrs", "rir", "ril" };
            }
            else if (type == XivItemType.face)
            {
                return new List<string> { "fac" };
            }
            else if (type == XivItemType.tail)
            {
                return new List<string> { "til" };
            }
            else if (type == XivItemType.hair)
            {
                return new List<string> { "hir" };
            }
            else if (type == XivItemType.ear)
            {
                return new List<string> { "zer" };
            }
            else
            {
                return new List<string>();
            }
        }

        public static XivDataFile GetDataFile(this XivItemType type)
        {
            if(type == XivItemType.indoor || type == XivItemType.outdoor || type == XivItemType.fish || type == XivItemType.painting)
            {
                return XivDataFile._01_Bgcommon;
            }

            return XivDataFile._04_Chara;
        }
    }
}
