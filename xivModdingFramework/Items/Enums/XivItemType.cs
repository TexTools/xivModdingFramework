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

using System.ComponentModel;

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
        [Description("w")] weapon,
        [Description("e")] equipment,
        [Description("a")] accessory,
        [Description("m")] monster,
        [Description("d")] demihuman,
        [Description("b")] body,
        [Description("h")] hair,
        [Description("t")] tail,
        [Description("z")] ear,
        [Description("f")] face,
        [Description("c")] human,
        [Description("")] decal,
        [Description("")] ui,
        [Description("")] furniture, // This one's a little vague and encompasses really all of /bgcommon/
        [Description("")] indoor,     // These are the clearer versions, but only used by the dependency graph.
        [Description("")] outdoor
    }

    public static class XivItemTypes {

        /// <summary>
        /// Gets the file type prefix for the enum value from its description.
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The race code</returns>
        public static string GetFilePrefix(this XivItemType value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = (DescriptionAttribute[])field.GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attribute.Length > 0 ? attribute[0].Description : value.ToString();
        }
    }
}