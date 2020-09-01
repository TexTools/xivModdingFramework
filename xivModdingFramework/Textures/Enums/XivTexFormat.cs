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

namespace xivModdingFramework.Textures.Enums
{
    /// <summary>
    /// Enum containing the known Texture Formats and their associated code
    /// </summary>
    public enum XivTexFormat
    {
        [XivTexFormatDescription("4400", "8\tL")] L8,
        [XivTexFormatDescription("4401", "8\tA")] A8,
        [XivTexFormatDescription("5184", "4.4.4.4\tRGB")] A4R4G4B4,
        [XivTexFormatDescription("5185", "1.5.5.5\tARGB")] A1R5G5B5,
        [XivTexFormatDescription("5200", "8.8.8.8\tARGB")] A8R8G8B8,
        [XivTexFormatDescription("5201", "X.8.8.8\tXRGB")] X8R8G8B8,
        [XivTexFormatDescription("8528", "32f\tR")] R32F,
        [XivTexFormatDescription("8784", "16.16f\tGR")] G16R16F,
        [XivTexFormatDescription("8800", "32.32f\tGR")] G32R32F,
        [XivTexFormatDescription("9312", "16.16.16.16f\tABGR")] A16B16G16R16F,
        [XivTexFormatDescription("9328", "32.32.32.32f\tABGR")] A32B32G32R32F,
        [XivTexFormatDescription("13344", "DXT1\tRGB")] DXT1,
        [XivTexFormatDescription("13360", "DXT3\tARGB")] DXT3,
        [XivTexFormatDescription("13361", "DXT5\tARGB")] DXT5,
        [XivTexFormatDescription("16704", "D16")] D16,
        [XivTexFormatDescription("0", "INVALID")] INVALID
    }

    /// <summary>
    /// Class used to get the description from the enum value
    /// </summary>
    public static class XivTexFormats
    {
        /// <summary>
        /// Gets the description from the enum value, in this case the Texture Code
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The Texture Code</returns>
        public static string GetTexFormatCode(this XivTexFormat value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = (XivTexFormatDescriptionAttribute[])field.GetCustomAttributes(typeof(XivTexFormatDescriptionAttribute), false);
            return attribute.Length > 0 ? attribute[0].TexCode : value.ToString();
        }

        /// <summary>
        /// Gets the description from the enum value, in this case the Texture Code
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The Texture Code</returns>
        public static string GetTexDisplayName(this XivTexFormat value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = (XivTexFormatDescriptionAttribute[])field.GetCustomAttributes(typeof(XivTexFormatDescriptionAttribute), false);
            return attribute.Length > 0 ? attribute[0].DisplayName : value.ToString();
        }
    }

    [AttributeUsage(AttributeTargets.All)]
    public class XivTexFormatDescriptionAttribute : DescriptionAttribute
    {
        public XivTexFormatDescriptionAttribute(string texCode, string displayName)
        {
            this.TexCode = texCode;
            this.DisplayName = displayName;
        }

        public string TexCode { get; set; }
        public string DisplayName { get; set; }
    }
}