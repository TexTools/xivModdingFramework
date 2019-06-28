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
    /// This class contains information for Items in the Pet Category
    /// </summary>
    public class XivPet : IItemModel
    {
        /// <summary>
        /// The name of the Pet
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Main Category
        /// </summary>
        /// <remarks>
        /// For Pets the Main Category is "Companions"
        /// </remarks>
        public string Category { get; set; }

        /// <summary>
        /// The item category
        /// </summary>
        /// <remarks>
        /// For Pets the item Category is "Pets"
        /// </remarks>
        public string ItemCategory { get; set; }

        /// <summary>
        /// The item SubCategory
        /// </summary>
        /// <remarks>
        /// This is currently not used for the Pet Category, but may be used in the future
        /// </remarks>
        public string ItemSubCategory { get; set; }

        /// <summary>
        /// The data file the item belongs to
        /// </summary>
        /// <remarks>
        /// Mount items are always in 040000
        /// </remarks>
        public XivDataFile DataFile { get; set; } = XivDataFile._04_Chara;

        /// <summary>
        /// The Primary Model Information of the Pet Item 
        /// </summary>
        public XivModelInfo ModelInfo { get; set; }

        public int CompareTo(object obj)
        {
            return string.Compare(Name, ((XivPet)obj).Name, StringComparison.Ordinal);
        }

        public object Clone()
        {
            var copy = (XivPet)this.MemberwiseClone();
            copy.ModelInfo = (XivModelInfo)ModelInfo.Clone();
            return copy;
        }
    }
}