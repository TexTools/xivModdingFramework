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
using xivModdingFramework.General;
using xivModdingFramework.Items.Enums;

namespace xivModdingFramework.Items.DataContainers
{
    /// <summary>
    /// This class contains Model Information for an Item
    /// Note - This is a partial class effectively, and 
    /// cannot actually be resolved into a full model without
    /// it's parent IIitem, as it lacks a slot identifier (top, dwn, glv, ...)
    /// </summary>
    public class XivModelInfo : ICloneable
    {

        /// <summary>
        /// The Item's primary ID.  This is one of:
        /// - (e)quipment number
        /// - (a)ccessory number
        /// - (w)eapon number
        /// - (d)emihuman number
        /// - (m)onster number
        /// - bg/furniture number
        /// - ui/icon number
        /// </summary>
        public int PrimaryID { get; set; }

        /// <summary>
        /// The Item's secondary ID.  This is one of
        /// - (b)ody number (Monster/Companion/Weapon)
        /// - (e)quipment number (Demihuman)
        /// - (z)ear number (Human)
        /// - (f)ace number (Human)
        /// - (t)ail number (Human)
        /// - (h)air number (Human)
        /// - None
        /// </summary>
        public int SecondaryID { get; set; }

        /// <summary>
        /// The item's Model SubSet ID - This can be resolved to
        /// Material Set ID/Variant via use of IMC->GetImcInfo()
        /// Only available for equipment items.
        /// </summary>
        public int ImcSubsetID { get; set; }

        /// <summary>
        /// The items full model key value.
        /// This is not actually used anywhere, and may be invalid/misunderstood data.
        /// </summary>
        public Quad ModelKey { get; set; }

        public object Clone()
        {
            return MemberwiseClone();
        }
    }
}