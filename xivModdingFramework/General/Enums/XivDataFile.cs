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
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using xivModdingFramework.Cache;

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
        // A Realm Reborn - Base Game
        [XivDataFileDescription("ffxiv/000000", "common/")] _00_Common,
        [XivDataFileDescription("ffxiv/010000", "bgcommon/")] _01_Bgcommon,
        [XivDataFileDescription("ffxiv/020000", "bg/")] _02_Bg,
        [XivDataFileDescription("ffxiv/030000", "cut/")] _03_Cut,
        [XivDataFileDescription("ffxiv/040000", "chara/")] _04_Chara,
        [XivDataFileDescription("ffxiv/050000", "shader/")] _05_Shader,
        [XivDataFileDescription("ffxiv/060000", "ui/")] _06_Ui,
        [XivDataFileDescription("ffxiv/070000", "sound/")] _07_Sound,
        [XivDataFileDescription("ffxiv/080000", "vfx/")] _08_Vfx,
        [XivDataFileDescription("ffxiv/0a0000", "exd/")] _0A_Exd,
        [XivDataFileDescription("ffxiv/0c0000", "music/")] _0C_Music,

#if ENDWALKER

        // Heavensward
        [XivDataFileDescription("ex1/020101", "bg/ex1/01_")] _EX1_BG_01,
        [XivDataFileDescription("ex1/020102", "bg/ex1/02_")] _EX1_BG_02,
        [XivDataFileDescription("ex1/020103", "bg/ex1/03_")] _EX1_BG_03,
        [XivDataFileDescription("ex1/020104", "bg/ex1/04_")] _EX1_BG_04,
        [XivDataFileDescription("ex1/020105", "bg/ex1/05_")] _EX1_BG_05,
        [XivDataFileDescription("ex1/030100", "cut/ex3/")] _EX1_Cut,

        // Stormblood
        [XivDataFileDescription("ex2/020201", "bg/ex2/01_")] _EX2_BG_01,
        [XivDataFileDescription("ex2/020202", "bg/ex2/02_")] _EX2_BG_02,
        [XivDataFileDescription("ex2/020203", "bg/ex2/03_")] _EX2_BG_03,
        //[XivDataFileDescription("ex2/020204", "bg/ex2/04_")] _EX2_BG_04,  // Doesn't actually exist? They skipped a number...
        [XivDataFileDescription("ex2/020205", "bg/ex2/05_")] _EX2_BG_05,
        [XivDataFileDescription("ex2/030200", "cut/ex2/")] _EX2_Cut,
        [XivDataFileDescription("ex2/0c0200", "music/ex2/")] _EX2_Music,

        // Shadowbringers
        [XivDataFileDescription("ex3/020301", "bg/ex3/01_")] _EX3_BG_01,
        [XivDataFileDescription("ex3/020302", "bg/ex3/02_")] _EX3_BG_02,
        //[XivDataFileDescription("ex3/020303", "bg/ex3/03_")] _EX3_BG_03, // Doesn't exist
        [XivDataFileDescription("ex3/020304", "bg/ex3/04_")] _EX3_BG_04,
        [XivDataFileDescription("ex3/030300", "cut/ex3/")] _EX3_Cut,
        [XivDataFileDescription("ex3/0c0300", "music/ex3/")] _EX3_Music,

        // Endwalker
        [XivDataFileDescription("ex4/020401", "bg/ex4/01_")] _EX4_BG_01,
        [XivDataFileDescription("ex4/020402", "bg/ex4/02_")] _EX4_BG_02,
        [XivDataFileDescription("ex4/020403", "bg/ex4/03_")] _EX4_BG_03,
        [XivDataFileDescription("ex4/020404", "bg/ex4/04_")] _EX4_BG_04,
        [XivDataFileDescription("ex4/020405", "bg/ex4/05_")] _EX4_BG_05,
        [XivDataFileDescription("ex4/020406", "bg/ex4/06_")] _EX4_BG_06,
        [XivDataFileDescription("ex4/020407", "bg/ex4/07_")] _EX4_BG_07,
        [XivDataFileDescription("ex4/020408", "bg/ex4/08_")] _EX4_BG_08,
        [XivDataFileDescription("ex4/020409", "bg/ex4/09_")] _EX4_BG_09,
        [XivDataFileDescription("ex4/030400", "cut/ex4/")] _EX4_Cut,
        [XivDataFileDescription("ex4/0c0400", "music/ex4/")] _EX4_Music,

#else
        // Dawntrail (Benchmark)
        [XivDataFileDescription("ex5/020502", "bg/ex5/02")] _EX5_BG_02,
#endif

        // Empty or virtually empty indexes.
        // These have very inconsistent behavior on whether their empty offsets are written or not,
        // Or how large their blank data segments are, making it hard for us to replicate them properly.
        //[XivDataFileDescription("ffxiv/090000", "ui_script")] _09_UiScript,
        //[XivDataFileDescription("ffxiv/120000", "???")] _09_UiScript,
        //[XivDataFileDescription("ffxiv/130000", "???")] _09_UiScript,
        //[XivDataFileDescription("ex1/120300", "???")] _EX1_12,
        //[XivDataFileDescription("ex1/020100", "bg/ex1/00_")] _EX1_BG_00,
        //[XivDataFileDescription("ex2/020200", "bg/ex2/00_")] _EX2_BG_00,
        //[XivDataFileDescription("ex3/020300", "bg/ex3/00_")] _EX3_BG_00,
        //[XivDataFileDescription("ex4/020400", "bg/ex4/00_")] _EX4_BG_00,

        // This oddball has a folder entry with Hash [0] in it, that points to the end of the File Entries table, but claims it has 1 file.
        // Since we naturally drop the folder on write, it causes a hashing difference if this index is ever re-saved.
        // The index only has a few files in it anyways, so it's safest/simplest to just disable it.
        //[XivDataFileDescription("ex3/020305", "bg/ex3/05_")] _EX3_BG_05,


        ///Currently Disabled due to a CVE 
        ///[XivDataFileDescription("0b0000", "game_script")] _0B_GameScript, 

    }

    /// <summary>
    /// Class used to get the description from the enum value
    /// </summary>
    public static class XivDataFiles
    {
        internal static string GetFullPath(this XivDataFile df, string extension = "")
        {
            var parent = XivCache.GameInfo.GameDirectory.Parent.FullName;
            var path = Path.Combine(parent, GetFilePath(df)) + extension;
            return path;
        }

        public static string GetFileName(this XivDataFile value)
        {
            return Path.GetFileNameWithoutExtension(GetFilePath(value));
        }
        /// <summary>
        /// Gets the description from the enum value, in this case the File Name
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The File Name</returns>
        internal static string GetFilePath(this XivDataFile value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = (XivDataFileDescriptionAttribute[])field.GetCustomAttributes(typeof(XivDataFileDescriptionAttribute), false);
            return attribute.Length > 0 ? attribute[0].CatNumber : value.ToString();
        }

        internal static string GetContainingFolder(this XivDataFile value)
        {
            var path = GetFullPath(value);
            var df = new DirectoryInfo(path);
            return df.Parent.FullName;
        }

        /// <summary>
        /// Gets the description from the enum value, in this case the folder of game data it contains
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The folder key</returns>
        internal static string GetFolderKey(this XivDataFile value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = (XivDataFileDescriptionAttribute[])field.GetCustomAttributes(typeof(XivDataFileDescriptionAttribute), false);
            return attribute.Length > 0 ? attribute[0].FolderKey : value.ToString();
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