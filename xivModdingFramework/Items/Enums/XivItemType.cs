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
        [Description("")] furniture, // This one's a little vague and encompasses really all of /bgcommon/
        [Description("")] indoor,     // These are the clearer versions, but only used by the dependency graph.
        [Description("")] outdoor
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
            { XivItemType.furniture, XivStrings.Housing },
            { XivItemType.indoor, XivStrings.Furniture_Indoor },
            { XivItemType.outdoor, XivStrings.Furniture_Outdoor }
        };

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
            foreach (XivItemType type in (XivItemType[])Enum.GetValues(typeof(XivItemType)))
            {
                if (type.GetSystemPrefix()[0] == value)
                {
                    return type;
                }
            }
            return XivItemType.unknown;
        }

        /// <summary>
        /// Gets the file type prefix for the enum value from its description.
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The race code</returns>
        public static string GetSystemName(this XivItemType value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = (DescriptionAttribute[])field.GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attribute.Length > 0 ? attribute[0].Description : value.ToString();
        }

        /// <summary>
        /// Retrieves an XivItemType from a system name.
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The race code</returns>
        public static XivItemType FromSystemName(string value)
        {
            foreach (XivItemType type in (XivItemType[])Enum.GetValues(typeof(XivItemType)))
            {
                if(type.GetSystemName() == value)
                {
                    return type;
                }
            }
            return XivItemType.unknown;
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
    }
}