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

namespace xivModdingFramework.Variants.DataContainers
{
    /// <summary>
    /// This class holds IMC information
    /// </summary>
    public class XivImc
    {
        /// <summary>
        /// The IMC Version
        /// </summary>
        public ushort Version { get; set; }

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
    }
}
