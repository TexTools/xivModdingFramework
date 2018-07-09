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
    public class MeshHiderData
    {
        public List<HiderInfo> HiderInfoList { get; set; }

        public List<HiderIndexInfo> HiderDataInfoList { get; set; }

        public List<HiderData> HiderDataList { get; set; }

        public class HiderInfo
        {
            /// <summary>
            /// Unknown usage
            /// </summary>
            public int Unknown { get; set; }

            /// <summary>
            /// The list of hider index parts
            /// </summary>
            public List<HiderIndexPart> HiderIndexParts { get; set; }
        }

        public class HiderIndexInfo
        {
            /// <summary>
            /// The offset to the index data
            /// </summary>
            /// <remarks>
            /// This should match a Index Data Offset in MeshDataInfo.IndexDataOffset
            /// </remarks>
            public int IndexDataOffset { get; set; }

            /// <summary>
            /// The number of indices to read from the Hider Data
            /// </summary>
            public int IndexCount { get; set; }

            /// <summary>
            /// The offset to the index in the Hider Data
            /// </summary>
            public int DataIndexOffset { get; set; }
        }


        public class HiderData
        {
            /// <summary>
            /// The offset to the existing index that will be hidden/moved
            /// </summary>
            public short ReferenceIndexOffset { get; set; }

            /// <summary>
            /// This is the Index to which the Reference index will hide/move to
            /// </summary>
            public short HideIndex { get; set; }
        }

        public class HiderIndexPart
        {
            /// <summary>
            /// Index to the Hider Data Information
            /// </summary>
            public short DataInfoIndex { get; set; }

            /// <summary>
            /// The number of parts in the Hider Data Information
            /// </summary>
            public short PartCount { get; set; }
        }

    }
}