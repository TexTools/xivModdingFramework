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

using System.Collections.Generic;

namespace xivModdingFramework.Models.DataContainers
{
    public class ModelImportSettings
    {
        /// <summary>
        /// The path of the model being imported
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Determines if we will attempt to fix mesh hiding for the mesh
        /// </summary>
        public bool Fix { get; set; }

        /// <summary>
        /// Determines if we will disable mesh hiding for the mesh
        /// </summary>
        public bool Disable { get; set; }

        /// <summary>
        /// The mesh part dictionary
        /// </summary>
        public Dictionary<int, int> PartDictionary { get; set; }
    }
}