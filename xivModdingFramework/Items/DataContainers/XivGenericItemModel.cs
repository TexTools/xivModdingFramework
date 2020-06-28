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
    /// This class holds information for Generic Items with models
    /// </summary>
    public class XivGenericItemModel : IItemModel
    {
        /// <summary>
        /// The name of the item
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Main Category
        /// </summary>
        public string PrimaryCategory { get; set; }

        /// <summary>
        /// The item Category
        /// </summary>
        public string SecondaryCategory { get; set; }

        /// <summary>
        /// The item SubCategory
        /// </summary>
        public string TertiaryCategory { get; set; }

        /// <summary>
        /// The data file the item belongs to
        /// </summary>
        public XivDataFile DataFile { get; set; }

        /// <summary>
        /// The Model Information for the gear item
        /// </summary>
        public XivModelInfo ModelInfo { get; set; }

        public int CompareTo(object obj)
        {
            return string.Compare(Name, ((XivGenericItemModel)obj).Name, StringComparison.Ordinal);
        }

        public object Clone()
        {
            var copy = (XivGenericItemModel)this.MemberwiseClone();
            copy.ModelInfo = (XivModelInfo)ModelInfo.Clone();
            return copy;
        }
    }
}