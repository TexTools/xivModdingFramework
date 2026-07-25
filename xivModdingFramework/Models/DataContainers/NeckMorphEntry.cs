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

using SharpDX;
using System.Collections.Generic;

namespace xivModdingFramework.Models.DataContainers
{
    /// <summary>
    /// Class representing a neck morph table entry in a model, which modifies a vertex on the neck seam of a character's body when active.
    /// Typically 10 of these entries exist on a head mesh.
    /// It is unclear how each entry relates to individual vertex on the body mesh.
    /// </summary>
    public class NeckMorphEntry
    {
        /// <summary>
        /// Relative adjustment of the Position of the vertex.
        /// Its unclear how this is applied, but it seems affected by the referenced bones.
        /// </summary>
        public Vector3 PositionAdjust { get; set; }

        /// <summary>
        /// This value is unclear but preserving it is important.
        /// If it is incorrect, the vertex adjustments disappear when viewed from certain camera angles.
        /// For all known working examples, it just has the value 0x00006699.
        /// </summary>
        public uint Unknown { get; set; }

        /// <summary>
        /// Relative adjustment of the Normal of the vertex.
        /// Its unclear how this is applied.
        /// </summary>
        public Vector3 NormalAdjust { get; set; }

        /// <summary>
        /// A list of bones, stored as indexes in to MdlPathData.BoneList.
        /// For all known working examples, the referenced bones are ["j_kubi", "j_sebo_c"].
        /// If the incorrect bones are referenced, the neck behavior becomes erratic.
        /// NOTE: In the model file these are actually stored as indexes in to BoneSet 0.
        /// </summary>
        public List<short> Bones { get; set; }
    }
}