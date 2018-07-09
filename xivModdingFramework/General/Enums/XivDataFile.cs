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

namespace xivModdingFramework.General.Enums
{
    /// <summary>
    /// Enum containing the known data files and brief content type
    /// </summary>
    /// <remarks>
    /// The names follow the format _[first 2 digits of file name]_[most common content type]
    /// </remarks>
    public enum XivDataFile
    {
        /// <summary>
        /// Contains common data such as fonts, mouse pointers, dictionaries, etc.
        /// </summary>
        [Description("000000")] _00_Common,
        /// <summary>
        /// Contains common world data such terrain, housing, etc.
        /// </summary>
        [Description("010000")] _01_Bgcommon,
        /// <summary>
        /// Contains world data such as dungeons, trials, pvp, etc.
        /// </summary>
        [Description("020000")] _02_Bg,
        /// <summary>
        /// Contains cutscene data
        /// </summary>
        [Description("030000")] _03_Cut,
        /// <summary>
        /// Contains Character data such as equipment, accessories, weapons, monsters, etc.
        /// </summary>
        [Description("040000")] _04_Chara,
        /// <summary>
        /// Contains Shader data
        /// </summary>
        [Description("050000")] _05_Shader,
        /// <summary>
        /// Contains UI data such as Icons, Maps, HUD, etc.
        /// </summary>
        [Description("060000")] _06_Ui,
        /// <summary>
        /// Contains Sound data such as battle, voices, and effects
        /// </summary>
        [Description("070000")] _07_Sound,
        /// <summary>
        /// Contains Visual Effects data
        /// </summary>
        [Description("080000")] _08_Vfx,
        /// <summary>
        /// Contains EXD data such as information files, cut scene text, quest text, etc.
        /// </summary>
        [Description("0a0000")] _0A_Exd,
        /// <summary>
        /// Contains Game Scripts in LUA format
        /// </summary>
        [Description("0b0000")] _0B_GameScript,
        /// <summary>
        /// Contains Music data
        /// </summary>
        [Description("0c0000")] _0C_Music
    }

    /// <summary>
    /// Class used to get the description from the enum value
    /// </summary>
    public static class XivDataFiles
    {
        /// <summary>
        /// Gets the description from the enum value, in this case the File Name
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The File Name</returns>
        public static string GetDataFileName(this XivDataFile value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = (DescriptionAttribute[])field.GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attribute.Length > 0 ? attribute[0].Description : value.ToString();
        }
    }
}