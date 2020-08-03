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
using System.Collections.Generic;
using xivModdingFramework.General.Enums;

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
        /// The top level category
        /// -- This has no relevance in actual game file structure.  This is purely a 
        /// -- custom generated Human Readable convention for the sake of TexTools sorting.
        /// </summary>
        /// <remarks>
        /// This would be a category such as Gear, Character, Companion, and UI
        /// </remarks>
        string PrimaryCategory { get; }

        /// <summary>
        /// The second level category.
        /// </summary>
        /// <remarks>
        /// This would be a category such as Body, Legs, Ears, Hair, Minions, Maps
        /// </remarks>
        string SecondaryCategory { get; }

        /// <summary>
        /// The third level category.
        /// </summary>
        /// <remarks>
        /// This would be a category such as La Noscea within Maps, Marker within Actions, Detrimental within Status
        /// This is mostly used in the UI main category
        /// </remarks>
        string TertiaryCategory { get; }

        /// <summary>
        /// The data file the item belongs to
        /// </summary>
        /// <remarks>
        /// This would change depending on the data file the data is to be pulled from
        /// </remarks>
        XivDataFile DataFile { get; }
    }


    /// <summary>
    /// Custom item name comparator.
    /// [Human Readable Names] => [NPC Names] =>  [Nulls/Invalids]
    /// </summary>
    public class ItemNameComparer : IComparer<string>
    {
        public ItemNameComparer()
        {
        }

        public int Compare(string x, string y)
        {
            // Nulls
            var xNull = String.IsNullOrEmpty(x);
            var yNull = String.IsNullOrEmpty(y);
            if ( xNull && yNull )
            {
                return 0;
            } else if(xNull)
            {
                return 1;
            } else if(yNull)
            {
                return -1;
            }

            // Both NPC Items.
            var xNpc = x.Contains("_v");
            var yNpc = y.Contains("_v");
            if (xNpc && yNpc)
            {
                return  String.Compare(x, y, StringComparison.InvariantCultureIgnoreCase);
            } else if(xNpc)
            {
                return 1;
            } else if (yNpc)
            {
                return -1;
            }


            return String.Compare(x, y, StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
