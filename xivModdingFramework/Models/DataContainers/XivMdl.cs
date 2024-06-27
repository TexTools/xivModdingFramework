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
    /// This class is an internal representation of the entire sum of an uncompressed .mdl file.
    /// It is not used in any high-level APIs, but is often used as a storage of the many bits of data that
    /// either we don't know what they do, or that users have no interaction with, but are still parts of the
    /// final .mdl file.
    /// </summary>
    public class XivMdl
    {

        /// <summary>
        /// The path to this mdl file
        /// </summary>
        public string MdlPath { get; set; }

        public ushort MdlVersion { get; set;  }

        /// <summary>
        /// The path data contained in the mdl file
        /// </summary>
        public MdlPathData PathData { get; set; }

        /// <summary>
        /// The model data contained in the mdl file
        /// </summary>
        public MdlModelData ModelData { get; set; }

        /// <summary>
        /// Currently unknown data
        /// </summary>
        public UnknownData0 UnkData0 { get; set; }

        /// <summary>
        /// The list containing the info for each Level of Detail of the model
        /// </summary>
        public List<LevelOfDetail> LoDList { get; set; }

        /// <summary>
        /// The data block containing attribute information
        /// </summary>
        public AttributeDataBlock AttrDataBlock { get; set; }

        /// <summary>
        /// Currently unknown data
        /// </summary>
        public UnknownData1 UnkData1 { get; set; }

        /// <summary>
        /// Currently unknown data
        /// </summary>
        public UnknownData2 UnkData2 { get; set; }

        /// <summary>
        /// The data block containing material information
        /// </summary>
        public MaterialDataBlock MatDataBlock { get; set; }

        /// <summary>
        /// The data block containing bone information
        /// </summary>
        public BoneDataBlock BoneDataBlock { get; set; }

        /// <summary>
        /// The list continaing each of the Bone Index Lists for each LoD
        /// </summary>
        public List<BoneSet> MeshBoneSets { get; set; }

        /// <summary>
        /// The data containing the information for mesh shapes
        /// </summary>
        public ShapeData MeshShapeData { get; set; }

        /// <summary>
        /// The data containing the information for the bone indices used by mesh parts
        /// </summary>
        public BoneSet PartBoneSets { get; set; }

        /// <summary>
        /// The size of the padded bytes immediately following 
        /// </summary>
        public byte PaddingSize { get; set; }

        /// <summary>
        /// The padded bytes
        /// </summary>
        public byte[] PaddedBytes { get; set; }

        /// <summary>
        // Bounding Boxes for the model
        /// </summary>
        public List<List<Vector4>> BoundingBoxes { get; set; }

        /// <summary>
        /// Bone Bounding Boxes
        /// </summary>
        public List<List<Vector4>> BoneBoundingBoxes { get; set; }

        /// <summary>
        /// Bone Bounding Boxes
        /// </summary>
        public List<List<Vector4>> BonelessPartBoundingBoxes { get; set; }

        /// <summary>
        /// Flag set when the model has shape data
        /// </summary>
        public bool HasShapeData { get; set; }

        /// <summary>
        /// The list containing the info for each etra Level of Detail of the model
        /// </summary>
        /// <remarks>
        /// This happens when the sum of all LoD mesh counts is less than the model data mesh count.
        /// The number of extra LoDs seems to be the value of Unknown10
        /// </remarks>
        public List<LevelOfDetail> ExtraLoDList { get; set; }

        /// <summary>
        /// The list of extra MeshData for the Model
        /// </summary>
        /// <remarks>
        /// This happens when the sum of all LoD mesh counts is less than the model data mesh count
        /// </remarks>
        public List<MeshData> ExtraMeshData { get; set; }
    }
}
