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
    /// This class holds information for items in the Mount Category
    /// </summary>
    public class XivMount : IItemModel
    {
        /// <summary>
        /// The mount name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Main Category
        /// </summary>
        /// <remarks>
        /// For Mounts the Main Category is "Companions"
        /// </remarks>
        public string PrimaryCategory { get; set; }

        /// <summary>
        /// The item Category
        /// </summary>
        /// <remarks>
        /// For Mounts the item Category is "Mounts"
        /// </remarks>
        public string SecondaryCategory { get; set; }

        /// <summary>
        /// The item SubCategory
        /// </summary>
        /// <remarks>
        /// This is currently not used for the Mount Category, but may be used in the future
        /// </remarks>
        public string TertiaryCategory { get; set; }

        public ushort IconId { get; set; }

        /// <summary>
        /// The data file the item belongs to
        /// </summary>
        /// <remarks>
        /// Mount items are always in 040000
        /// </remarks>
        public XivDataFile DataFile { get; set; } = XivDataFile._04_Chara;

        /// <summary>
        /// The Primary Model Information of the Mount Item
        /// </summary>
        public XivModelInfo ModelInfo { get; set; }

        /// <summary>
        /// Gets the item's name as it should be written to the modlist/modpack files.
        /// </summary>
        /// <returns></returns>
        public string GetModlistItemName()
        {
            return Name != null ? Name : "Unknown Mount";
        }

        /// <summary>
        /// Gets the item's category as it should be written to the modlist/modpack files.
        /// </summary>
        /// <returns></returns>
        public string GetModlistItemCategory()
        {
            return SecondaryCategory != null ? SecondaryCategory : XivStrings.Mounts;
        }
        internal static IItemModel FromDependencyRoot(XivDependencyRoot root, int imcSubset)
        {
            var item = new XivMount();
            var mi = new XivMonsterModelInfo();
            mi.ModelType = root.Info.PrimaryType;
            mi.PrimaryID = root.Info.PrimaryId;
            mi.SecondaryID = (int)root.Info.SecondaryId;
            mi.ImcSubsetID = imcSubset;

            item.ModelInfo = mi;
            item.Name = root.Info.GetBaseFileName() + "_v" + imcSubset.ToString();
            item.PrimaryCategory = XivStrings.Companions;
            item.SecondaryCategory = XivStrings.Mounts;

            if(root.Info.Slot != null)
            {
                item.TertiaryCategory = Mdl.SlotAbbreviationDictionary.First(x => x.Value == root.Info.Slot).Key;
            }

            return item;
        }

        public int CompareTo(object obj)
        {
            return string.Compare(Name, ((XivMount)obj).Name, StringComparison.Ordinal);
        }

        public object Clone()
        {
            var copy = (XivMount)this.MemberwiseClone();
            copy.ModelInfo = (XivModelInfo)ModelInfo.Clone();
            return copy;
        }
    }
}