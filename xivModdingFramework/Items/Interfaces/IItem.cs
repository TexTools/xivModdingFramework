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

namespace xivModdingFramework.Items.Interfaces
{
    /// <summary>
    /// Interface for Item details
    /// </summary>
    public interface IItem : IComparable
    {
        /// <summary>
        /// The item Name
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The Main Category
        /// </summary>
        /// <remarks>
        /// This would be a category such as Gear, Character, Companion, and UI
        /// </remarks>
        string Category { get; }

        /// <summary>
        /// The items Category
        /// </summary>
        /// <remarks>
        /// This would be a category such as Body, Legs, Ears, Hair, Minions, Maps
        /// </remarks>
        string ItemCategory { get; }

        /// <summary>
        /// The items Sub-Category
        /// </summary>
        /// <remarks>
        /// This would be a category such as La Noscea within Maps, Marker within Actions, Detrimental within Status
        /// This is mostly used in the UI main category
        /// </remarks>
        string ItemSubCategory { get; }
    }
}
