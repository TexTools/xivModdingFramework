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
    public class XivFurniture : IItemModel
    {
        /// <summary>
        /// The name of the furniture item
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Main Category
        /// </summary>
        /// <remarks>
        /// For Furniture, the main category is "Furniture"
        /// </remarks>
        public string Category { get; set; }

        /// <summary>
        /// The furniture item Category
        /// </summary>
        public string ItemCategory { get; set; }

        /// <summary>
        /// The furniture item SubCategory
        /// </summary>
        /// <remarks>
        /// This is currently not used for Furniture, but may be used in the future
        /// </remarks>
        public string ItemSubCategory { get; set; }

        /// <summary>
        /// The data file the item belongs to
        /// </summary>
        /// <remarks>
        /// Housing items are either in 010000
        /// </remarks>
        public XivDataFile DataFile { get; set; } = XivDataFile._01_Bgcommon;

        /// <summary>
        /// The Primary Model Information for the furniture item
        /// </summary>
        public XivModelInfo ModelInfo { get; set; }

        /// <summary>
        /// The Icon Number associated with the furniture item
        /// </summary>
        public uint IconNumber { get; set; }

        public int CompareTo(object obj)
        {
            return string.Compare(Name, ((XivFurniture)obj).Name, StringComparison.Ordinal);
        }

        public object Clone()
        {
            var copy = (XivFurniture)this.MemberwiseClone();
            copy.ModelInfo = (XivModelInfo)ModelInfo.Clone();
            return copy;
        }
    }
}