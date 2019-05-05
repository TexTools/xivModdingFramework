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
using xivModdingFramework.General.Enums;
using xivModdingFramework.Items.Interfaces;

namespace xivModdingFramework.Items.DataContainers
{
    /// <summary>
    /// This class holds information for Gear Items
    /// </summary>
    public class XivGear : IItemModel
    {
        /// <summary>
        /// The name of the gear item
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Main Category
        /// </summary>
        /// <remarks>
        /// For Gear, the main category is "Gear"
        /// </remarks>
        public string Category { get; set; }

        /// <summary>
        /// The gear item Category
        /// </summary>
        /// <remarks>
        /// This would be a category such as Body, Legs, Hands, Feet, Rings, Main Hand
        /// </remarks>
        public string ItemCategory { get; set; }

        /// <summary>
        /// The gear item SubCategory
        /// </summary>
        /// <remarks>
        /// This is currently not used for Gear, but may be used in the future
        /// </remarks>
        public string ItemSubCategory { get; set; }

        /// <summary>
        /// The data file the item belongs to
        /// </summary>
        /// <remarks>
        /// Gear items are always in 040000
        /// </remarks>
        public XivDataFile DataFile { get; set; } = XivDataFile._04_Chara;

        /// <summary>
        /// The Primary Model Information for the gear item
        /// </summary>
        public XivModelInfo ModelInfo { get; set; }

        /// <summary>
        /// The Secondary Model Information for the gear item
        /// </summary>
        public XivModelInfo SecondaryModelInfo { get; set; }

        /// <summary>
        /// The Icon Number associated with the gear item
        /// </summary>
        public uint IconNumber { get; set; }

        /// <summary>
        /// The gear EquipSlotCategory key
        /// </summary>
        public int EquipSlotCategory { get; set; }

        public object Clone()
        {
            var copy = (XivGear)this.MemberwiseClone();
            copy.ModelInfo = (XivModelInfo)ModelInfo.Clone();

            if (SecondaryModelInfo != null)
            {
                copy.SecondaryModelInfo = (XivModelInfo)SecondaryModelInfo.Clone();
            }

            return copy;
        }

        public int CompareTo(object obj)
        {
            return string.Compare(Name, ((XivGear) obj).Name, StringComparison.Ordinal);
        }

        public override string ToString() => Name;
    }
}