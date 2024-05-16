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
using xivModdingFramework.Mods.DataContainers;

namespace xivModdingFramework.Models.DataContainers
{

    [Flags]
    public enum EMeshFlags1 : byte
    {
        ShadowDisabled = 0x01,
        LightShadowDisabled = 0x02,
        WavingAnimationDisabled = 0x04,
        LightingReflectionEnabled = 0x08,
        Unknown10 = 0x10,
        RainOcclusionEnabled = 0x20,
        SnowOcclusionEnabled = 0x40,
        DustOcclusionEnabled = 0x80,
    };

    [Flags]
    public enum EMeshFlags2 : byte
    {
        HasBonelessParts = 0x01,
        EdgeGeometryEnabled = 0x02,
        ForceLodRangeEnabled = 0x04,
        ShadowMaskEnabled = 0x08,
        HasExtraMeshes = 0x10,
        EnableForceNonResident = 0x20,
        BgUvScrollEnabled = 0x40,
        Unknown80 = 0x80,
    };

    [Flags]
    public enum EMeshFlags3 : byte
    {
        Unknown01 = 0x01,
        UseMaterialChange = 0x02,
        UseCrestChange = 0x04,
        Unknown08 = 0x08,
        Unknown10 = 0x10,
        Unknown20 = 0x20,
        Unknown40 = 0x40,
        Unknown80 = 0x80,
    };


    /// <summary>
    /// This class cotains the properties for the MDL model data
    /// </summary>
    /// <remarks>
    /// This section of the MDL file still has a lot of unknowns
    /// </remarks>
    public class MdlModelData
    {
        /// <summary>
        /// Unknown Usage
        /// </summary>
        public float Radius { get; set; }

        /// <summary>
        /// The total number of meshes that the model contains
        /// </summary>
        /// <remarks>
        /// This includes all LoD meshes
        /// </remarks>
        public short MeshCount { get; set; }

        /// <summary>
        /// The number of attributes used by the model
        /// </summary>
        public short AttributeCount { get; set; }

        /// <summary>
        /// The total number of mesh parts the model contains
        /// </summary>
        public short MeshPartCount { get; set; }

        /// <summary>
        /// The number of materials used by the model
        /// </summary>
        public short MaterialCount { get; set; }

        /// <summary>
        /// The number of bones used by the model
        /// </summary>
        public short BoneCount { get; set; }

        /// <summary>
        /// The total number of Bone Lists the model uses
        /// </summary>
        /// <remarks>
        /// There is usually one per LoD
        /// </remarks>
        public short BoneListCount { get; set; }

        /// <summary>
        /// The number of Mesh Shapes
        /// </summary>
        public short ShapeCount { get; set; }

        /// <summary>
        /// The number of data blocks in the mesh shapes
        /// </summary>
        public short ShapePartCount { get; set; }

        /// <summary>
        /// The total number of indices that the mesh shapes uses
        /// </summary>
        public ushort ShapeDataCount { get; set; }

        /// <summary>
        /// The total number of LoD
        /// </summary>
        public byte LoDCount { get; set; }

        public EMeshFlags1 Flags1 { get; set; }

        public ushort ElementIdCount { get; set; }

        public byte TerrainShadowMeshCount { get; set; }


        public EMeshFlags2 Flags2 { get; set; }


        /// <summary>
        /// Unknown Usage
        /// </summary>
        public float ModelClipOutDistance { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public float ShadowClipOutDistance { get; set; }

        /// <summary>
        /// Bounding boxes for LoD0 Mesh0 parts for furniture/Non-Boned items with multiple parts.
        /// </summary>
        public ushort FurniturePartBoundingBoxCount { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short TerrainShadowPartCount { get; set; }

        public EMeshFlags3 Flags3 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public byte BgChangeMaterialIndex { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public byte BgCrestChangeMaterialIndex { get; set; }

        public byte Unknown12 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short BoneSetSize { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown13 { get; set; }

        /// <summary>
        /// Unknown Usage
        /// </summary>
        public short Unknown14 { get; set; }

        /// <summary>
        /// Padding?
        /// </summary>
        public short Unknown15 { get; set; }

        /// <summary>
        /// Padding?
        /// </summary>
        public short Unknown16 { get; set; }

        /// <summary>
        /// Padding?
        /// </summary>
        public short Unknown17 { get; set; }
    }
}