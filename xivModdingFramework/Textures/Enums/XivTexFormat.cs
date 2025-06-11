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
        // WHY ARE THESE STRINGS?
        [XivTexFormatDescription("4400", "8L")] L8 = 4400,
        [XivTexFormatDescription("4401", "8A")] A8 = 4401,
        [XivTexFormatDescription("5184", "4.4.4.4 RGB")] A4R4G4B4 = 5184,
        [XivTexFormatDescription("5185", "1.5.5.5 ARGB")] A1R5G5B5 = 5185,
        [XivTexFormatDescription("5200", "8.8.8.8 ARGB")] A8R8G8B8 = 5200,
        [XivTexFormatDescription("5201", "X.8.8.8 XRGB")] X8R8G8B8 = 5201,
        [XivTexFormatDescription("8528", "32f R")] R32F = 8528,
        [XivTexFormatDescription("8784", "16.16f GR")] G16R16F = 8784,
        [XivTexFormatDescription("8800", "32.32f GR")] G32R32F = 8800,
        [XivTexFormatDescription("9312", "16.16.16.16f ABGR")] A16B16G16R16F = 9312,
        [XivTexFormatDescription("9328", "32.32.32.32f ABGR")] A32B32G32R32F = 9328,
        [XivTexFormatDescription("13344", "DXT1 RGB")] DXT1 = 13344,
        [XivTexFormatDescription("13360", "DXT3 ARGB")] DXT3 = 13360,
        [XivTexFormatDescription("13361", "DXT5 ARGB")] DXT5 = 13361,
        [XivTexFormatDescription("16704", "D16")] D16 = 16704,
        [XivTexFormatDescription("24864", "BC4")] BC4 = 24864,
        [XivTexFormatDescription("25136", "BC5")] BC5 = 25136,
        [XivTexFormatDescription("25650", "BC7")] BC7 = 25650,
        [XivTexFormatDescription("0", "INVALID")] INVALID = 0,
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

        public static bool IsCompressedFormat(this XivTexFormat value)
        {
            switch (value)
            {
                case XivTexFormat.DXT1:
                case XivTexFormat.DXT3:
                case XivTexFormat.DXT5:
                case XivTexFormat.BC4:
                case XivTexFormat.BC5:
                case XivTexFormat.BC7:
                    return true;
                default:
                    return false;
            }
        }

        public static int GetMipMinDimension(this XivTexFormat value)
        {
            return value.IsCompressedFormat() ? 4 : 1;
        }

        public static int GetBitsPerPixel(this XivTexFormat value)
        {
            switch (value)
            {
                case XivTexFormat.DXT1:
                case XivTexFormat.BC4:
                    return 4;
                case XivTexFormat.DXT5:
                case XivTexFormat.BC5:
                case XivTexFormat.A8:
                case XivTexFormat.BC7:
                    return 8;
                case XivTexFormat.A1R5G5B5:
                case XivTexFormat.A4R4G4B4:
                    return 16;
                case XivTexFormat.L8:
                case XivTexFormat.A8R8G8B8:
                case XivTexFormat.X8R8G8B8:
                case XivTexFormat.R32F:
                case XivTexFormat.G16R16F:
                case XivTexFormat.G32R32F:
                case XivTexFormat.A16B16G16R16F:
                case XivTexFormat.A32B32G32R32F:
                case XivTexFormat.DXT3:
                case XivTexFormat.D16:
                    return 32;
            }

            throw new ArgumentException("No BitsPerPixel defined for texture format");
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