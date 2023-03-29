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
        [XivDataFileDescription("000000", "common")] _00_Common,
        /// <summary>
        /// Contains common world data such terrain, housing, etc.
        /// </summary>
        [XivDataFileDescription("010000", "bgcommon")] _01_Bgcommon,
        /// <summary>
        /// Contains world data such as dungeons, trials, pvp, etc.
        /// </summary>
        [XivDataFileDescription("020000", "bg")] _02_Bg,
        /// <summary>
        /// Contains cutscene data
        /// </summary>
        [XivDataFileDescription("030000", "cut")] _03_Cut,
        /// <summary>
        /// Contains Character data such as equipment, accessories, weapons, monsters, etc.
        /// </summary>
        [XivDataFileDescription("040000", "chara")] _04_Chara,
        /// <summary>
        /// Contains Shader data
        /// </summary>
        [XivDataFileDescription("050000", "shader")] _05_Shader,
        /// <summary>
        /// Contains UI data such as Icons, Maps, HUD, etc.
        /// </summary>
        [XivDataFileDescription("060000", "ui")] _06_Ui,
        /// <summary>
        /// Contains Sound data such as battle, voices, and effects
        /// </summary>
        [XivDataFileDescription("070000", "sound")] _07_Sound,
        /// <summary>
        /// Contains Visual Effects data
        /// </summary>
        [XivDataFileDescription("080000", "vfx")] _08_Vfx,
        /// <summary>
        /// Contains UI Script data(CURRENTLY NOT PRESENT)
        /// </summary>
        [XivDataFileDescription("090000", "ui_script")] _09_UiScript,
        /// <summary>
        /// Contains EXD data such as information files, cut scene text, quest text, etc.
        /// </summary>
        [XivDataFileDescription("0a0000", "exd")] _0A_Exd,
        /// <summary>
        /// Contains Game Scripts in LUA format
        /// </summary>
        ///[XivDataFileDescription("0b0000", "game_script")] _0B_GameScript, ///Currently Disabled due to a CVE 
        /// <summary>
        /// Contains Music data
        /// </summary>
        [XivDataFileDescription("0c0000", "music")] _0C_Music
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
            var attribute = (XivDataFileDescriptionAttribute[])field.GetCustomAttributes(typeof(XivDataFileDescriptionAttribute), false);
            return attribute.Length > 0 ? attribute[0].CatNumber : value.ToString();
        }

        /// <summary>
        /// Gets the description from the enum value, in this case the folder of game data it contains
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The folder key</returns>
        public static string GetFolderKey(this XivDataFile value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = (XivDataFileDescriptionAttribute[])field.GetCustomAttributes(typeof(XivDataFileDescriptionAttribute), false);
            return attribute.Length > 0 ? attribute[0].FolderKey : value.ToString();
        }


        /// <summary>
        /// Gets the enum value from the description
        /// </summary>
        /// <param name="value">The data file string</param>
        /// <returns>The XivDataFile enum</returns>
        public static XivDataFile GetXivDataFile(string value)
        {
            var dataFiles = Enum.GetValues(typeof(XivDataFile)).Cast<XivDataFile>();

            return dataFiles.FirstOrDefault(dataFile => dataFile.GetDataFileName() == value);
        }
    }

    [AttributeUsage(AttributeTargets.All)]
    public class XivDataFileDescriptionAttribute : DescriptionAttribute
    {
        public XivDataFileDescriptionAttribute(string catNum, string folderKey)
        {
            this.CatNumber = catNum;
            this.FolderKey = folderKey;
        }

        public string CatNumber { get; set; }
        public string FolderKey { get; set; }
    }
}