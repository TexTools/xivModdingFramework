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
using System.ComponentModel;
using System.Linq;

namespace xivModdingFramework.General.Enums
{
    /// <summary>
    /// Enum containing the languages supported by the game
    /// </summary>
    /// <remarks>
    /// May need to be updated if the game ever supports an additional language
    /// </remarks>
    public enum XivLanguage
    {
        [Description("en")] English,
        [Description("ja")] Japanese,
        [Description("de")] German,
        [Description("fr")] French,
        [Description("ko")] Korean,
        [Description("chs")] Chinese
    }

    /// <summary>
    /// Class used to get the description from the enum value
    /// </summary>
    public static class XivLanguages
    {
        /// <summary>
        /// Gets the description from the enum value, in this case the Language
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The Language Code</returns>
        public static string GetLanguageCode(this XivLanguage value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = (DescriptionAttribute[])field.GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attribute.Length > 0 ? attribute[0].Description : value.ToString();
        }

        /// <summary>
        /// Gets the enum value from the description
        /// </summary>
        /// <param name="value">The language string</param>
        /// <returns>The XivLanguage enum</returns>
        public static XivLanguage GetXivLanguage(string value)
        {
            //esrinzou for china ffxiv
            if (value == "zh")
                return XivLanguage.Chinese;
            //esrinzou end
            var languages = Enum.GetValues(typeof(XivLanguage)).Cast<XivLanguage>();

            return languages.FirstOrDefault(language => language.GetLanguageCode() == value);
        }
    }
}