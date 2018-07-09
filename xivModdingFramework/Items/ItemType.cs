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

using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Resources;

namespace xivModdingFramework.Items
{
    /// <summary>
    /// This class contains the getter for the item type
    /// </summary>
    public static class ItemType
    {
        /// <summary>
        /// Gets the type of the item
        /// </summary>
        /// <remarks>
        /// The item type is determined by its category
        /// </remarks>
        /// <see cref="XivItemType"/>
        /// <param name="item">The item to get the type for</param>
        /// <returns>The XivItemType containing the type of the item</returns>
        public static XivItemType GetItemType(IItemModel item)
        {
            XivItemType itemType;

            if (item.Category.Equals(XivStrings.Main_Hand) || item.Category.Equals(XivStrings.Off_Hand) || 
                item.Category.Equals(XivStrings.Main_Off) || item.Category.Equals(XivStrings.Two_Handed))
            {
                itemType = XivItemType.weapon;
            }
            else if (item.Category.Equals(XivStrings.Ears) || item.Category.Equals(XivStrings.Neck) || 
                     item.Category.Equals(XivStrings.Wrists) || item.Category.Equals(XivStrings.Rings))
            {
                itemType = XivItemType.accessory;
            }
            else if (item.Category.Equals(XivStrings.Mounts) || item.Category.Equals(XivStrings.Minions) || item.Category.Equals(XivStrings.Pets)
                     || item.Category.Equals(XivStrings.Monster) || item.Category.Equals(XivStrings.Food))
            {
                itemType = XivItemType.monster;
            }
            else if (item.Category.Equals(XivStrings.DemiHuman))
            {
                itemType = XivItemType.demihuman;
            }
            else if (item.Category.Equals(XivStrings.Character))
            {
                itemType = XivItemType.human;
            }
            else if (item.Category.Equals("UI"))
            {
                itemType = XivItemType.ui;
            }
            else
            {
                itemType = XivItemType.equipment;
            }

            return itemType;
        }
    }
}