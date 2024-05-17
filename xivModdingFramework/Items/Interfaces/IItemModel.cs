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
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Resources;

namespace xivModdingFramework.Items.Interfaces
{
    /// <summary>
    /// Interface for Items that have model data
    /// </summary>
    public interface IItemModel : IItem, ICloneable
    {
        /// <summary>
        /// The Primary Model Information of the Item
        /// </summary>
        public XivModelInfo ModelInfo { get; set; }

        public uint IconId { get; set; }
    }

    public static class IItemModelExtensions
    {
        public static bool HasRealModel(this IItemModel item)
        {
            // Catch for paintings being dumb.
            if(item.ModelInfo != null && item.SecondaryCategory != XivStrings.Paintings)
            {
                return true;
            }
            return false;
        }
    }
}