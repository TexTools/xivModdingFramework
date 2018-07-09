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

namespace xivModdingFramework.Models.DataContainers
{
    public class MaterialDataBlock
    {
        /// <summary>
        /// This data block is associated with the models materials but their function is unknown
        /// </summary>
        /// <remarks>
        /// The size of this material data block is the [ MdlModelData.MaterialCount * 4 ]
        /// </remarks>
        public byte[] Unknown { get; set; }
    }
}