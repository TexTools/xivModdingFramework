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
using System.Linq;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Resources;

namespace xivModdingFramework.Items.DataContainers
{
    /// <summary>
    /// This class holds information for Gear Items
    /// </summary>
    public class XivGear : IItemModel
    {

        /// <summary>
        /// The ID of this item in items.exd
        /// </summary>
        public int ExdID { get; set; }

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
        public string PrimaryCategory { get; set; }

        /// <summary>
        /// The gear item Category
        /// </summary>
        /// <remarks>
        /// This would be a category such as Body, Legs, Hands, Feet, Rings, Main Hand
        /// </remarks>
        public string SecondaryCategory { get; set; }

        /// <summary>
        /// The gear item SubCategory
        /// </summary>
        /// <remarks>
        /// This is currently not used for Gear, but may be used in the future
        /// </remarks>
        public string TertiaryCategory { get; set; }

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
        /// The Icon Number associated with the gear item
        /// </summary>
        public uint IconNumber { get; set; }

        /// <summary>
        /// The gear EquipSlotCategory key
        /// </summary>
        public int EquipSlotCategory { get; set; }

        /// <summary>
        /// The other item in this pair (main or offhand)
        /// </summary>
        public XivGear PairedItem { get; set; }

        public static IItemModel FromDependencyRoot(XivDependencyRoot root)
        {
            var item = new XivGear();
            item.ModelInfo = new XivGearModelInfo();
            item.ModelInfo.PrimaryID = root.Info.PrimaryId;
            if (root.Info.SecondaryId != null)
            {
                item.ModelInfo.SecondaryID = (int)root.Info.SecondaryId;
            }

            item.Name = root.Info.GetBaseFileName();
            item.PrimaryCategory = XivStrings.Gear;

            if (root.Info.PrimaryType == Enums.XivItemType.weapon)
            {
                ((XivGearModelInfo)item.ModelInfo).IsWeapon = true;
                item.SecondaryCategory = XivStrings.Main_Hand;
            }
            else
            {
                item.SecondaryCategory = Mdl.SlotAbbreviationDictionary.First(x => x.Value == root.Info.Slot).Key;
            }


            return item;
        }

        public object Clone()
        {
            var copy = (XivGear)this.MemberwiseClone();
            copy.ModelInfo = (XivGearModelInfo)ModelInfo.Clone();

            return copy;
        }

        public int CompareTo(object obj)
        {
            return string.Compare(Name, ((XivGear) obj).Name, StringComparison.Ordinal);
        }

        public override string ToString() => Name;
    }

    public class XivGearModelInfo : XivModelInfo
    {
        public bool IsWeapon = false;
    }
}