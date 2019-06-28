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
    /// </summary>
    public class XivModelInfo : ICloneable
    {
        /// <summary>
        /// Currently not used.
        /// </summary>
        /// <remarks>
        /// There are always 2 empty bytes at the start of the model info
        /// So far there has been no instance of there being any data in those bytes
        /// Keeping this here in case it starts being used
        /// </remarks>
        public int Unused { get; set; }

        /// <summary>
        /// The items Model ID
        /// </summary>
        public int ModelID { get; set; }

        /// <summary>
        /// The items Variant
        /// </summary>
        public int Variant { get; set; }

        /// <summary>
        /// The items Body
        /// </summary>
        public int Body { get; set; }

        /// <summary>
        /// The model type, this is only used when an item has a reference to ModelChara
        /// </summary>
        public XivItemType ModelType { get; set; }

        /// <summary>
        /// The items full model key value.
        /// </summary>
        public Quad ModelKey { get; set; }

        public object Clone()
        {
            return MemberwiseClone();
        }
    }
}