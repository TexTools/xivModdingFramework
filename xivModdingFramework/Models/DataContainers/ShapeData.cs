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
    public class ShapeData
    {
        public List<ShapeInfo> ShapeInfoList { get; set; }

        public List<ShapeIndexInfo> ShapeDataInfoList { get; set; }

        public List<ShapeEntryData> ShapeDataList { get; set; }

        public class ShapeInfo
        {
            /// <summary>
            /// The offset to the shape path in the path data block
            /// </summary>
            public int ShapePathOffset { get; set; }

            /// <summary>
            /// The path of the shape
            /// </summary>
            public string ShapePath { get; set; }

            /// <summary>
            /// The list of shape index parts
            /// </summary>
            public List<ShapeIndexPart> ShapeIndexParts { get; set; }
        }

        public class ShapeIndexInfo
        {
            /// <summary>
            /// The offset to the index data
            /// </summary>
            /// <remarks>
            /// This should match a Index Data Offset in MeshDataInfo.IndexDataOffset
            /// </remarks>
            public int IndexDataOffset { get; set; }

            /// <summary>
            /// The number of indices to read from the Shape Data
            /// </summary>
            public int IndexCount { get; set; }

            /// <summary>
            /// The offset to the index in the Shape Data
            /// </summary>
            public int DataIndexOffset { get; set; }
        }


        public class ShapeEntryData
        {
            /// <summary>
            /// The offset to the existing index that will be moved
            /// </summary>
            public ushort ReferenceIndexOffset { get; set; }

            /// <summary>
            /// This is the Index to which the Reference index will move to
            /// </summary>
            public ushort ShapeIndex { get; set; }
        }

        public class ShapeIndexPart
        {
            /// <summary>
            /// Index to the Shape Data Information
            /// </summary>
            public ushort DataInfoIndex { get; set; }

            /// <summary>
            /// The number of parts in the Shape Data Information
            /// </summary>
            public short PartCount { get; set; }
        }

    }
}