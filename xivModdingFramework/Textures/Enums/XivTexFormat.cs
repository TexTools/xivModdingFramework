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

namespace xivModdingFramework.Textures.Enums
{
    /// <summary>
    /// Enum containing the known Texture Formats and their associated code
    /// </summary>
    public enum XivTexFormat
    {
        [Description("4400")] L8,
        [Description("4401")] A8,
        [Description("5184")] A4R4G4B4,
        [Description("5185")] A1R5G5B5,
        [Description("5200")] A8R8G8B8,
        [Description("5201")] X8R8G8B8,
        [Description("8528")] R32F,
        [Description("8784")] G16R16F,
        [Description("8800")] G32R32F,
        [Description("9312")] A16B16G16R16F,
        [Description("9328")] A32B32G32R32F,
        [Description("13344")] DXT1,
        [Description("13360")] DXT3,
        [Description("13361")] DXT5,
        [Description("16704")] D16
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
            var attribute = (DescriptionAttribute[])field.GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attribute.Length > 0 ? attribute[0].Description : value.ToString();
        }
    }
}