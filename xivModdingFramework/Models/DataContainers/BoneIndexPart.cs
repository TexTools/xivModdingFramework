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
    /// <summary>
    /// This class contains the properties for the Bone Index Data
    /// </summary>
    /// <remarks>
    /// This references the specific bones that are used by the mesh part by their index in the bone string list 
    /// </remarks>
    public class BoneIndexPart
    {
        /// <summary>
        /// The number of Bone Indices in the list
        /// </summary>
        public int BoneIndexCount { get; set; }

        /// <summary>
        /// The list of bone indices in the Bone Index Data
        /// </summary>
        /// <remarks>
        /// This list contains the indices to the bones in MdlPathData.BoneList
        /// </remarks>
        public List<short> BoneIndexList { get; set; }
    }
}