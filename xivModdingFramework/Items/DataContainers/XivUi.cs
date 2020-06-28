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
    /// This class holds information for Items in the UI Category
    /// </summary>
    public class XivUi : IItem
    {
        /// <summary>
        /// The name of the UI item
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Main Category
        /// </summary>
        /// <remarks>
        /// For UI the Main Category is "UI"
        /// </remarks>
        public string PrimaryCategory { get; set; }

        /// <summary>
        /// The items category
        /// </summary>
        /// <remarks>
        /// This would be a category such as Maps, Actions, Status, Weather
        /// </remarks>
        public string SecondaryCategory { get; set; }

        /// <summary>
        /// The items SubCategory
        /// </summary>
        /// <remarks>
        /// This would be a category such as a maps region names and action types
        /// </remarks>
        public string TertiaryCategory { get; set; }

        /// <summary>
        /// The data file the item belongs to
        /// </summary>
        /// <remarks>
        /// Minion items are always in 060000
        /// </remarks>
        public XivDataFile DataFile { get; set; } = XivDataFile._06_Ui;

        /// <summary>
        /// The internal UI path
        /// </summary>
        public string UiPath { get; set; }

        /// <summary>
        /// The Icon Number
        /// </summary>
        public int IconNumber { get; set; }


        public int CompareTo(object obj)
        {
            return string.Compare(Name, ((XivUi)obj).Name, StringComparison.Ordinal);
        }
    }
}