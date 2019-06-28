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
using System.Resources;
using xivModdingFramework.Resources;

namespace xivModdingFramework.General.Enums
{
    /// <summary>
    /// Enum containing all known races
    /// </summary>
    /// <remarks>
    /// Some of the NPC races don't seem to be present anywhere
    /// They were added to the list in case they are ever used in the future
    /// </remarks>
    public enum XivRace
    {
        [Description("0101")] Hyur_Midlander_Male,
        [Description("0104")] Hyur_Midlander_Male_NPC,
        [Description("0201")] Hyur_Midlander_Female,
        [Description("0204")] Hyur_Midlander_Female_NPC,
        [Description("0301")] Hyur_Highlander_Male,
        [Description("0304")] Hyur_Highlander_Male_NPC,
        [Description("0401")] Hyur_Highlander_Female,
        [Description("0404")] Hyur_Highlander_Female_NPC,
        [Description("0501")] Elezen_Male,
        [Description("0504")] Elezen_Male_NPC,
        [Description("0601")] Elezen_Female,
        [Description("0604")] Elezen_Female_NPC,
        [Description("0701")] Miqote_Male,
        [Description("0704")] Miqote_Male_NPC,
        [Description("0801")] Miqote_Female,
        [Description("0804")] Miqote_Female_NPC,
        [Description("0901")] Roegadyn_Male,
        [Description("0904")] Roegadyn_Male_NPC,
        [Description("1001")] Roegadyn_Female,
        [Description("1004")] Roegadyn_Female_NPC,
        [Description("1101")] Lalafell_Male,
        [Description("1104")] Lalafell_Male_NPC,
        [Description("1201")] Lalafell_Female,
        [Description("1204")] Lalafell_Female_NPC,
        [Description("1301")] AuRa_Male,
        [Description("1304")] AuRa_Male_NPC,
        [Description("1401")] AuRa_Female,
        [Description("1404")] AuRa_Female_NPC,
        [Description("1501")] Hrothgar,
        [Description("1504")] Hrothgar_NPC,
        [Description("1801")] Viera,
        [Description("1804")] Viera_NPC,
        [Description("9104")] NPC_Male,
        [Description("9204")] NPC_Female,
        [Description("0000")] All_Races,
        [Description("0000")] Monster,
        [Description("0000")] DemiHuman,


    }

    /// <summary>
    /// Class used to get certain values from the enum
    /// </summary>
    public static class XivRaces
    {
        /// <summary>
        /// Gets the description from the enum value, in this case the Race Code
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The race code</returns>
        public static string GetRaceCode(this XivRace value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = (DescriptionAttribute[])field.GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attribute.Length > 0 ? attribute[0].Description : value.ToString();
        }

        /// <summary>
        /// Gets the Display Name of the Race from the Resource file in order to support localization
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The localized display name of the race</returns>
        public static string GetDisplayName(this XivRace value)
        {
            var rm = new ResourceManager(typeof(XivStrings));
            var displayName = rm.GetString(value.ToString());

            return displayName;
        }


        /// <summary>
        /// Gets the enum value from the description
        /// </summary>
        /// <param name="value">The race string</param>
        /// <returns>The XivRace enum</returns>
        public static XivRace GetXivRace(string value)
        {
            var races = Enum.GetValues(typeof(XivRace)).Cast<XivRace>();

            return races.FirstOrDefault(race => race.GetRaceCode() == value);
        }
    }
}