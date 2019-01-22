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

namespace xivModdingFramework.Mods.DataContainers
{
    public class SimpleModPackEntries : IComparable<SimpleModPackEntries>
    {
        /// <summary>
        /// The name of the item
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The category of the item
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// The race associated with the mod
        /// </summary>
        public string Race { get; set; }

        /// <summary>
        /// The item part
        /// </summary>
        public string Part { get; set; }

        /// <summary>
        /// The item number
        /// </summary>
        public string Num { get; set; }

        /// <summary>
        /// The item texture map
        /// </summary>
        public string Map { get; set; }

        /// <summary>
        /// The status of the mod
        /// </summary>
        public bool Active { get; set; }

        /// <summary>
        /// The mod entry
        /// </summary>
        public Mod ModEntry { get; set; }

        /// <summary>
        /// The json mod entry
        /// </summary>
        public ModsJson JsonEntry { get; set; }

        public int CompareTo(SimpleModPackEntries obj)
        {
            return Name.CompareTo(obj.Name);
        }
    }
}