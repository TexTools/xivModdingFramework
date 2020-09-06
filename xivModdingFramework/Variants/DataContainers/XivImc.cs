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

using HelixToolkit.SharpDX.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using xivModdingFramework.Helpers;
using xivModdingFramework.Variants.FileTypes;

namespace xivModdingFramework.Variants.DataContainers
{
    /// <summary>
    /// The IMC information for a Specific Variant of a Specific Slot in a Gear Set.
    /// </summary>
    public class XivImc : ICloneable
    {
        /// <summary>
        /// The Material Set / Variant #
        /// </summary>
        public byte Variant { get; set; }

        /// <summary>
        /// Unknown byte next to the Variant
        /// </summary>
        public byte Unknown { get; set; }

        /// <summary>
        /// The IMC mask data
        /// </summary>
        /// <remarks>
        /// This is used with a models mesh part mask data
        /// It is used to determine what parts of the mesh are hidden
        /// </remarks>
        public ushort Mask { get; set; }

        /// <summary>
        /// The IMC VFX data
        /// </summary>
        /// <remarks>
        /// Only a few items have VFX data associated with them
        /// Some examples would be any of the Lux weapons
        /// </remarks>
        public ushort Vfx { get; set; }

        /// <summary>
        /// Returns the raw bytes that make up this IMC entry.
        /// </summary>
        /// <returns></returns>
        public byte[] GetBytes(ImcType type)
        {
            var bytes = new List<byte>();
            bytes.Add(Variant);
            bytes.Add(Unknown);
            bytes.AddRange(BitConverter.GetBytes(Mask));
            bytes.AddRange(BitConverter.GetBytes(Vfx));
            return bytes.ToArray();
        }

        /// <summary>
        /// Shows a given attribute part
        /// </summary>
        /// <param name="part"></param>
        public void ShowPart(char part)
        {
            var index = Helpers.Constants.Alphabet.ToList().IndexOf(part);
            if(index < 0 || index > 9)
            {
                throw new NotSupportedException("Invalid IMC Part Letter.");
            }

            var bit = 1;
            for(var i = 0; i < index; i++)
            {
                bit = bit << 1;
            }

            Mask = (ushort)(Mask | bit);
        }

        /// <summary>
        /// Hides a given attribute part
        /// </summary>
        /// <param name="part"></param>
        public void HidePart(char part)
        {
            var index = Helpers.Constants.Alphabet.ToList().IndexOf(part);
            if (index < 0 || index > 9)
            {
                throw new NotSupportedException("Invalid IMC Part Letter.");
            }

            var bit = 1;
            for (var i = 0; i < index; i++)
            {
                bit = bit << 1;
            }

            Mask = (ushort)(Mask & (~bit));

        }

        public object Clone()
        {
            return this.MemberwiseClone();
        }

    }
}
