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
        /// The item type is determined by its ItemCategory
        /// </remarks>
        /// <see cref="XivItemType"/>
        /// <param name="item">The item to get the type for</param>
        /// <returns>The XivItemType containing the type of the item</returns>
        public static XivItemType GetItemType(IItemModel item)
        {
            XivItemType itemType;

            if (item.ItemCategory.Equals(XivStrings.Main_Hand) || item.ItemCategory.Equals(XivStrings.Off_Hand) || 
                item.ItemCategory.Equals(XivStrings.Main_Off) || item.ItemCategory.Equals(XivStrings.Two_Handed) || item.ItemCategory.Equals(XivStrings.Food))
            {
                itemType = XivItemType.weapon;
            }
            else if (item.Category.Equals(XivStrings.Gear) && (item.ItemCategory.Equals(XivStrings.Ears) || item.ItemCategory.Equals(XivStrings.Neck) || 
                     item.ItemCategory.Equals(XivStrings.Wrists) || item.ItemCategory.Equals(XivStrings.Rings)))
            {
                itemType = XivItemType.accessory;
            }
            else if (item.ItemCategory.Equals(XivStrings.Mounts) || item.ItemCategory.Equals(XivStrings.Minions) || item.ItemCategory.Equals(XivStrings.Pets)
                     || item.ItemCategory.Equals(XivStrings.Monster))
            {
                itemType = item.ModelInfo.ModelType;
            }
            else if (item.ItemCategory.Equals(XivStrings.DemiHuman))
            {
                itemType = XivItemType.demihuman;
            }
            else if (item.Category.Equals(XivStrings.Character))
            {
                itemType = XivItemType.human;
            }
            else if (item.ItemCategory.Equals("UI"))
            {
                itemType = XivItemType.ui;
            }
            else if (item.Category.Equals(XivStrings.Housing))
            {
                itemType = XivItemType.furniture;
            }
            else
            {
                itemType = XivItemType.equipment;
            }

            return itemType;
        }
    }
}