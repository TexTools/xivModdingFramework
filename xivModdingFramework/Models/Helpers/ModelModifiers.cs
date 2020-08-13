using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Helpers;
using xivModdingFramework.Mods;
using xivModdingFramework.Models.FileTypes;
using System.Threading.Tasks;
using SharpDX;
using xivModdingFramework.General.Enums;
using System.Threading;
using System.IO;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using HelixToolkit.SharpDX.Core;
using System.Runtime.CompilerServices;

namespace xivModdingFramework.Models.Helpers
{

    /// <summary>
    /// Simple booleans to determine behavior of the gobal level model modifiers.
    /// </summary>
    public class ModelModifierOptions
    {
        public bool CopyAttributes { get; set; }
        public bool CopyMaterials { get; set; }
        public bool EnableShapeData { get; set; }
        public bool ForceUVQuadrant { get; set; }
        public bool ClearUV2 { get; set; }
        public bool CloneUV2 { get; set; }
        public bool ClearVColor { get; set; }
        public bool ClearVAlpha { get; set; }
        public bool AutoScale { get; set; }
        public XivRace SourceRace { get; set; }


        /// <summary>
        /// Default constructor explicitly establishes option defaults.
        /// </summary>
        public ModelModifierOptions()
        {
            CopyAttributes = true;
            CopyMaterials = true;
            EnableShapeData = false;
            ForceUVQuadrant = false;
            ClearUV2 = false;
            CloneUV2 = false;
            ClearVColor = false;
            ClearVAlpha = false;
            AutoScale = true;
            SourceRace = XivRace.All_Races;
        }

        /// <summary>
        /// Function to apply these options to a given model.
        /// originalMdl is optional as it's only used when copying shape data.
        /// </summary>
        /// <param name="ttModel"></param>
        public void Apply(TTModel ttModel, XivMdl currentMdl = null, XivMdl originalMdl = null, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = ModelModifiers.NoOp;
            }

            if(originalMdl == null)
            {
                originalMdl = currentMdl;
            }


            if (CopyAttributes)
            {
                if(currentMdl == null)
                {
                    throw new Exception("Cannot copy settings from null MDL.");
                }
                ModelModifiers.MergeAttributeData(ttModel, currentMdl, loggingFunction);
            }

            if (CopyMaterials)
            {
                if (currentMdl == null)
                {
                    throw new Exception("Cannot copy settings from null MDL.");
                }
                ModelModifiers.MergeMaterialData(ttModel, currentMdl, loggingFunction);
            }

            if (ForceUVQuadrant)
            {
                ModelModifiers.ForceUVQuadrant(ttModel, loggingFunction);
            }

            if (ClearUV2)
            {
                ModelModifiers.ClearUV2(ttModel, loggingFunction);
            }

            if (CloneUV2)
            {
                ModelModifiers.CloneUV2(ttModel, loggingFunction);
            }

            if (ClearVColor)
            {
                ModelModifiers.ClearVColor(ttModel, loggingFunction);
            }

            if (ClearVAlpha)
            {
                ModelModifiers.ClearVAlpha(ttModel, loggingFunction);
            }

            if(SourceRace != XivRace.All_Races)
            {
                if (currentMdl == null)
                {
                    throw new Exception("Cannot racially convert from null MDL.");
                }
                ModelModifiers.RaceConvert(ttModel, SourceRace, currentMdl.MdlPath, loggingFunction);
            }

            // We need to load the original unmodified model to get the shape data.
            if (EnableShapeData)
            {
                if (ttModel.HasShapeData)
                {
                    // We already have shape data, nothing to do here.
                }
                else
                {
                    if (originalMdl == null)
                    {
                        throw new Exception("Cannot copy settings from null MDL.");
                    }
                    ModelModifiers.MergeShapeData(ttModel, originalMdl, loggingFunction);
                }
            } else
            {
                ModelModifiers.ClearShapeData(ttModel, loggingFunction);
            }

            if(AutoScale)
            {
                if (originalMdl == null)
                {
                    throw new Exception("Cannot auto-scale without base model loaded.");
                }

                var oldModel = TTModel.FromRaw(originalMdl);
                ModelModifiers.AutoScaleModel(ttModel, oldModel, 0.3, loggingFunction);
            }
        }
    }


    /// <summary>
    /// List of modifier functions to TTModel Objects.
    /// </summary>
    public static class ModelModifiers
    {

        /// <summary>
        /// Automatically rescales the model to correct for unit scaling errors based on comparison of size to the original model.
        /// </summary>
        /// <param name="ttModel"></param>
        /// <param name="originalModel"></param>
        /// <param name="tolerance"></param>
        /// <param name="loggingFunction"></param>
        public static void AutoScaleModel(TTModel ttModel, TTModel originalModel, double tolerance = 0.3, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            loggingFunction(false, "Checking for model scale errors...");


            // Calculate the model bounding box sizes.
            float minX = 9999.0f, minY = 9999.0f, minZ = 9999.0f;
            float maxX = -9999.0f, maxY = -9999.0f, maxZ = -9999.0f;
            foreach (var m in ttModel.MeshGroups)
            {
                foreach (var p in m.Parts)
                {
                    foreach (var v in p.Vertices)
                    {
                        minX = minX < v.Position.X ? minX : v.Position.X;
                        minY = minY < v.Position.Y ? minY : v.Position.Y;
                        minZ = minZ < v.Position.Z ? minZ : v.Position.Z;

                        maxX = maxX > v.Position.X ? maxX : v.Position.X;
                        maxY = maxY > v.Position.Y ? maxY : v.Position.Y;
                        maxZ = maxZ > v.Position.Z ? maxZ : v.Position.Z;
                    }
                }
            }

            Vector3 min = new Vector3(minX, minY, minZ);
            Vector3 max = new Vector3(maxX, maxY, maxZ);
            double NewModelSize = Vector3.Distance(min, max);



            minX = 9999.0f; minY = 9999.0f; minZ = 9999.0f;
            maxX = -9999.0f; maxY = -9999.0f; maxZ = -9999.0f;
            foreach (var m in originalModel.MeshGroups)
            {
                foreach (var p in m.Parts)
                {
                    foreach (var v in p.Vertices)
                    {
                        minX = minX < v.Position.X ? minX : v.Position.X;
                        minY = minY < v.Position.Y ? minY : v.Position.Y;
                        minZ = minZ < v.Position.Z ? minZ : v.Position.Z;

                        maxX = maxX > v.Position.X ? maxX : v.Position.X;
                        maxY = maxY > v.Position.Y ? maxY : v.Position.Y;
                        maxZ = maxZ > v.Position.Z ? maxZ : v.Position.Z;
                    }
                }
            }

            min = new Vector3(minX, minY, minZ);
            max = new Vector3(maxX, maxY, maxZ);
            double OldModelSize = Vector3.Distance(min, max);


            // Calculate the percentage difference between these two.
            List<double> possibleConversions = new List<double>()
            {
                // Standard metric conversions get first priority.
                1.0D,
                10.0D,
                100.0D,
                1000.0D,
                0.1D,
                0.01D,
                0.001D,
                0.0001D,

                // Metric Imperial legacy fuckup conversions get second priority.
                0.003937007874D,
                0.03937007874D,
                0.3937007874D,
                3.937007874D,
                39.37007874D,

                // "Correct" Inch conversions come last.
                254.0D,
                25.40D,
                2.540D,
                0.254D,
                0.0254D,
                0.00254D,
            };

            foreach(var conversion in possibleConversions)
            {
                var nSize = NewModelSize * conversion;
                var diff = (OldModelSize - nSize) / OldModelSize;

                if(Math.Abs(diff) < tolerance)
                {
                    if(conversion != 1.0D)
                    {
                        loggingFunction(true, "Correcting Scaling Error: Rescaling model by " + conversion);
                        ScaleModel(ttModel, conversion, loggingFunction);
                        return;
                    } else
                    {
                        // Done here.
                        loggingFunction(false, "Model is correctly scaled, no adjustment needed.");
                        return;
                    }
                }
            }

            loggingFunction(true, "Unable to find appropriate scale for model, scale unchanged.");

        }

        public static void ScaleModel(TTModel ttModel, double scale, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }
            loggingFunction(false, "Scaling model by: " + scale.ToString("0.00"));

            foreach (var m in ttModel.MeshGroups)
            {
                foreach(var p in m.Parts)
                {
                    foreach (var v in p.Vertices)
                    {
                        v.Position.X = (float)(v.Position.X * scale);
                        v.Position.Y = (float)(v.Position.Y * scale);
                        v.Position.Z = (float)(v.Position.Z * scale);
                    }
                }
            }
        }

        // Merges the full geometry data from a raw xivMdl
        // This will destroy any existing mesh groups in the TTModel.
        public static void MergeGeometryData(TTModel ttModel, XivMdl rawMdl, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            ttModel.MeshGroups.Clear();

            var meshIdx = 0;
            var totalPartIdx = 0;
            foreach(var baseMesh in rawMdl.LoDList[0].MeshDataList)
            {
                var ttMesh = new TTMeshGroup();
                ttModel.MeshGroups.Add(ttMesh);
                ttMesh.Name = "Group " + meshIdx;

                // Build the bone set for our mesh.
                if (rawMdl.MeshBoneSets != null && rawMdl.MeshBoneSets.Count > 0)
                {
                    var meshBoneSet = rawMdl.MeshBoneSets[baseMesh.MeshInfo.BoneSetIndex];
                    for (var bi = 0; bi < meshBoneSet.BoneIndexCount; bi++)
                    {
                        // This is an index into the main bone paths list.
                        var boneIndex = meshBoneSet.BoneIndices[bi];
                        var boneName = rawMdl.PathData.BoneList[boneIndex];
                        ttMesh.Bones.Add(boneName);
                    }
                }

                var partIdx = 0;
                bool fakePart = false;
                var totalParts = baseMesh.MeshPartList.Count;

                // This is a furniture or other mesh that doesn't use the part system.
                if (rawMdl.Partless)
                {
                    fakePart = true;
                    totalParts = 1;
                }

                for(var pi = 0; pi < totalParts; pi++)
                {

                    var ttPart = new TTMeshPart();
                    ttMesh.Parts.Add(ttPart);
                    ttPart.Name = "Part " + partIdx;

                    // Get the Indicies uniuqe to this part.
                    var basePart = fakePart == false ? baseMesh.MeshPartList[pi] : null;
                    var indexStart = fakePart == false ? basePart.IndexOffset - baseMesh.MeshInfo.IndexDataOffset : 0;
                    var indexCount = fakePart == false ? basePart.IndexCount : baseMesh.MeshInfo.IndexCount;

                    var indices = baseMesh.VertexData.Indices.GetRange(indexStart, indexCount);

                    // Get the Vertices unique to this part.
                    var uniqueVertexIdSet = new SortedSet<int>(indices); // Maximum possible amount is # of indices, though likely it is less.

                    foreach(var ind in indices)
                    {
                        uniqueVertexIdSet.Add(ind);
                    }

                    // Need it as a list to have index access to it.
                    var uniqueVertexIds = uniqueVertexIdSet.ToList();

                    // Maps old vertex ID to new vertex ID.
                    var vertDict = new Dictionary<int, int>(uniqueVertexIds.Count);

                    // Now we need to loop through, copy over the vertex data, keeping track of the new vertex IDs.
                    for(var i = 0; i < uniqueVertexIds.Count; i++)
                    {
                        var oldVertexId = uniqueVertexIds[i];
                        var ttVert = new TTVertex();

                        // Copy in the datapoints if they exist.
                        if (baseMesh.VertexData.Positions.Count > oldVertexId)
                        {
                            ttVert.Position = baseMesh.VertexData.Positions[oldVertexId];
                        }
                        if (baseMesh.VertexData.Normals.Count > oldVertexId)
                        {
                            ttVert.Normal = baseMesh.VertexData.Normals[oldVertexId];
                        }
                        if (baseMesh.VertexData.BiNormals.Count > oldVertexId)
                        {
                            ttVert.Binormal = baseMesh.VertexData.BiNormals[oldVertexId];
                        }
                        if (baseMesh.VertexData.Colors.Count > oldVertexId)
                        {
                            ttVert.VertexColor[0] = baseMesh.VertexData.Colors[oldVertexId].R;
                            ttVert.VertexColor[1] = baseMesh.VertexData.Colors[oldVertexId].G;
                            ttVert.VertexColor[2] = baseMesh.VertexData.Colors[oldVertexId].B;
                            ttVert.VertexColor[3] = baseMesh.VertexData.Colors[oldVertexId].A;
                        }
                        if (baseMesh.VertexData.BiNormalHandedness.Count > oldVertexId)
                        {
                            ttVert.Handedness = baseMesh.VertexData.BiNormalHandedness[oldVertexId] == 0 ? false : true;
                        }
                        if (baseMesh.VertexData.Tangents.Count > oldVertexId)
                        {
                            ttVert.Tangent = baseMesh.VertexData.Tangents[oldVertexId];
                        }
                        if (baseMesh.VertexData.TextureCoordinates0.Count > oldVertexId)
                        {
                            ttVert.UV1 = baseMesh.VertexData.TextureCoordinates0[oldVertexId];
                        }
                        if (baseMesh.VertexData.TextureCoordinates1.Count > oldVertexId)
                        {
                            ttVert.UV2 = baseMesh.VertexData.TextureCoordinates1[oldVertexId];

                            if (float.IsNaN(ttVert.UV2.X))
                            {
                                ttVert.UV2.X = 0;
                            }

                            if(float.IsNaN(ttVert.UV2.Y))
                            {
                                ttVert.UV2.Y = 0;
                            }
                        }



                        // Now for the fun part, establishing bones.
                        for(var bIdx = 0; bIdx < 4; bIdx++)
                        {
                            // Vertex doesn't have weights.
                            if (baseMesh.VertexData.BoneWeights.Count <= oldVertexId) break;
                            
                            // No more weights for this vertex.
                            if (baseMesh.VertexData.BoneIndices[oldVertexId].Length <= bIdx) break;

                            // Null weight for this bone.
                            if (baseMesh.VertexData.BoneWeights[oldVertexId][bIdx] == 0) continue;

                            var boneId = baseMesh.VertexData.BoneIndices[oldVertexId][bIdx];
                            var weight = baseMesh.VertexData.BoneWeights[oldVertexId][bIdx];
                            //var boneName = 

                            // These seem to actually be irrelevant, and the bone ID is just routed directly to the mesh level identifier.
                            // var partBoneSet = rawMdl.PartBoneSets.BoneIndices.GetRange(basePart.BoneStartOffset, basePart.BoneCount);

                            ttVert.BoneIds[bIdx] = (byte) boneId;
                            ttVert.Weights[bIdx] = (byte) Math.Round(weight * 255);
                        }

                        ttPart.Vertices.Add(ttVert);
                        vertDict.Add(oldVertexId, ttPart.Vertices.Count - 1);
                    }

                    // Now we need to copy in the triangle indices, pointing to the new, part-level vertex IDs.
                    foreach(var oldVertexId in indices)
                    {
                        ttPart.TriangleIndices.Add(vertDict[oldVertexId]);
                    }

                    // Ok, gucci now.

                    partIdx++;
                    totalPartIdx++;
                }

                meshIdx++;
            }

        }
        // Merges attribute data from the given raw XivMdl.
        public static void MergeAttributeData(TTModel ttModel, XivMdl rawMdl, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            // Only need to loop the data we're merging in, other elements are naturally
            // left blank/default.

            var attributes = rawMdl.PathData.AttributeList;
            for (var mIdx = 0; mIdx < rawMdl.LoDList[0].MeshDataList.Count; mIdx++)
            {
                // Can only carry in data to meshes that exist
                if (mIdx >= ttModel.MeshGroups.Count) continue;

                var md = rawMdl.LoDList[0].MeshDataList[mIdx];
                var localMesh = ttModel.MeshGroups[mIdx];

                for (var pIdx = 0; pIdx < md.MeshPartList.Count; pIdx++)
                {
                    // Can only carry in data to parts that exist
                    if (pIdx >= localMesh.Parts.Count) continue;


                    var p = md.MeshPartList[pIdx];
                    var localPart = localMesh.Parts[pIdx];

                    // Copy over attributes. (Convert from bitmask to full string values)
                    var mask = p.AttributeBitmask;
                    uint bit = 1;
                    for (int i = 0; i < 32; i++)
                    {
                        bit = (uint)1 << i;

                        if ((mask & bit) > 0)
                        {
                            // Can't add attributes that don't exist (should never be hit, but sanity).
                            if (i >= attributes.Count) continue;

                            localPart.Attributes.Add(attributes[i]);
                        }
                    }
                }
            }
        }

        // Merges material data from the given raw XivMdl.
        public static void MergeMaterialData(TTModel ttModel, XivMdl rawMdl, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            // Only need to loop the data we're merging in, other elements are naturally
            // left blank/default.

            for (var mIdx = 0; mIdx < rawMdl.LoDList[0].MeshDataList.Count; mIdx++)
            {
                // Can only carry in data to meshes that exist
                if (mIdx >= ttModel.MeshGroups.Count) continue;

                var md = rawMdl.LoDList[0].MeshDataList[mIdx];
                var localMesh = ttModel.MeshGroups[mIdx];

                // Copy over Material
                var matIdx = md.MeshInfo.MaterialIndex;
                if (matIdx < rawMdl.PathData.MaterialList.Count)
                {
                    var oldMtrl = rawMdl.PathData.MaterialList[matIdx];
                    localMesh.Material = oldMtrl;
                } else
                {
                    localMesh.Material = rawMdl.PathData.MaterialList[0];
                }
            }
        }

        // Merges shape data from the given raw XivMdl.
        public static void MergeShapeData(TTModel ttModel, XivMdl ogMdl, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            loggingFunction(false, "Merging Shape Data from Original Model...");

            try
            {
                // Sanity checks.
                if (!ogMdl.HasShapeData)
                {
                    return;
                }

                // Just use LoD 0 only.
                var lIdx = 0;

                var baseShapeData = ogMdl.MeshShapeData;
                // For every Shape
                foreach (var shape in baseShapeData.ShapeInfoList)
                {
                    var name = shape.Name;
                    // For every Mesh Group
                    for (var mIdx = 0; mIdx < ttModel.MeshGroups.Count; mIdx++)
                    {
                        if (mIdx >= ogMdl.LoDList[lIdx].MeshDataList.Count)
                        {
                            break;
                        }
                        var ogGroup = ogMdl.LoDList[lIdx].MeshDataList[mIdx];
                        var newGroup = ttModel.MeshGroups[mIdx];
                        var newBoneSet = newGroup.Bones;

                        // Have to convert the raw bone set to a useable format...
                        var oldBoneSetRaw = ogMdl.MeshBoneSets[ogGroup.MeshInfo.BoneSetIndex];
                        var oldBoneSet = new List<string>();
                        for (int bi = 0; bi < oldBoneSetRaw.BoneIndexCount; bi++)
                        {
                            oldBoneSet.Add(ogMdl.PathData.BoneList[bi]);
                        }

                        // No shape data for groups that don't exist in the old model.
                        if (mIdx >= ogMdl.LoDList[lIdx].MeshDataList.Count) return;

                        // Get all the parts for this mesh.
                        var parts = baseShapeData.ShapeParts.Where(x => x.ShapeName == name && x.MeshNumber == mIdx && x.LodLevel == lIdx).OrderBy(x => x.ShapeName).ToList();

                        // If we have any, we need to create entries for them.
                        if (parts.Count > 0)
                        {
                            foreach (var p in parts)
                            {
                                var ttPart = new TTShapePart();
                                ttPart.Name = name;

                                var data = baseShapeData.GetShapeData(p);


                                // That's the easy part...

                                // Now we have to build the Vertex List for this part.
                                // First we need a set of all the unique vertex IDs in the the data.

                                // This matches old vertex ID(old MeshGroup level) to new vertex ID(new shape part level).
                                var vertexDictionary = new Dictionary<int, int>();

                                var uniqueVertexIds = new SortedSet<int>();
                                foreach (var d in data)
                                {
                                    uniqueVertexIds.Add(d.ShapeVertex);
                                }

                                // Now we need to use these to reference the original vertex list for the mesh
                                // to create our new TTVertexes.
                                var oldVertexIds = uniqueVertexIds.ToList();
                                foreach (var vId in oldVertexIds)
                                {
                                    var vert = new TTVertex();
                                    vert.Position = ogGroup.VertexData.Positions.Count > vId ? ogGroup.VertexData.Positions[vId] : new Vector3();
                                    vert.Normal = ogGroup.VertexData.Normals.Count > vId ? ogGroup.VertexData.Normals[vId] : new Vector3();
                                    vert.Tangent = ogGroup.VertexData.Tangents.Count > vId ? ogGroup.VertexData.Tangents[vId] : new Vector3();
                                    vert.Binormal = ogGroup.VertexData.BiNormals.Count > vId ? ogGroup.VertexData.BiNormals[vId] : new Vector3();
                                    vert.Handedness = ogGroup.VertexData.BiNormalHandedness.Count > vId ? ogGroup.VertexData.BiNormalHandedness[vId] == 0 ? false : true : false;
                                    vert.UV1 = ogGroup.VertexData.TextureCoordinates0.Count > vId ? ogGroup.VertexData.TextureCoordinates0[vId] : new Vector2();
                                    vert.UV2 = ogGroup.VertexData.TextureCoordinates1.Count > vId ? ogGroup.VertexData.TextureCoordinates1[vId] : new Vector2();
                                    var color = ogGroup.VertexData.Colors.Count > vId ? ogGroup.VertexData.Colors[vId] : new Color();

                                    vert.VertexColor[0] = color.R;
                                    vert.VertexColor[1] = color.G;
                                    vert.VertexColor[2] = color.B;
                                    vert.VertexColor[3] = color.A;

                                    for (int i = 0; i < 4 && i < ogGroup.VertexData.BoneWeights[vId].Length; i++)
                                    {
                                        // Copy Weights over.
                                        vert.Weights[i] = (byte)(Math.Round(ogGroup.VertexData.BoneWeights[vId][i] * 255));

                                        // We have to convert the bone ID to match the new bone IDs used in this TTModel.
                                        var oldBoneId = ogGroup.VertexData.BoneIndices[vId][i];
                                        var boneName = oldBoneSet[oldBoneId];
                                        int newBoneId = newBoneSet.IndexOf(boneName);
                                        if (newBoneId < 0)
                                        {
                                            // Add the missing bone in at the end of the list.
                                            newGroup.Bones.Add(boneName);
                                            newBoneId = newGroup.Bones.Count - 1;
                                        }

                                        vert.BoneIds[i] = (byte)newBoneId;
                                    }

                                    // We can now add our new fully qualified vertex.
                                    ttPart.Vertices.Add(vert);
                                    vertexDictionary.Add(vId, ttPart.Vertices.Count - 1);
                                }

                                // Okay, we now have fully qualified vertex data, but we need to carry over the index
                                // edits, and modify the vertex #'s to point to the new vertex list.
                                //   ( That way this part's vertex offsets aren't dependent on the rest of the model maintaining the same structure )
                                foreach (var d in data)
                                {
                                    var vertexId = vertexDictionary[d.ShapeVertex];

                                    // This line is where shape data gets F*cked by changes to the base model.
                                    // Because we're using the index offset that's relative to the original model's mesh group index list.
                                    var indexId = d.BaseIndex;

                                    ttPart.Replacements.Add(indexId, vertexId);
                                }
                                newGroup.ShapeParts.Add(ttPart);
                            }
                        }
                    }
                }

                foreach (var m in ttModel.MeshGroups)
                {
                    m.ShapeParts = m.ShapeParts.OrderBy(x => x.Name).ToList();
                }

            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        // Clears all shape data in the model.
        public static void ClearShapeData(TTModel ttModel, Action<bool, string> loggingFunction = null)
        {

            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            loggingFunction(false, "Clearing Shape Data...");

            ttModel.MeshGroups.ForEach(x => x.ShapeParts.Clear());
        }

        // Forces all UV Coordinates in UV1 Layer to [1,1] (pre-flip) Quadrant.
        public static void ForceUVQuadrant(TTModel model, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            MakeExportReady(model, loggingFunction);

            loggingFunction(false, "Forcing UV1 to [1,-1]...");
            foreach(var m in model.MeshGroups)
            {
                foreach(var p in m.Parts)
                {
                    foreach(var v in p.Vertices)
                    {

                        v.UV1.X = (v.UV1.X % 1);
                        v.UV1.Y = (v.UV1.Y % 1);

                        if (v.UV1.X < 0)
                        {
                            v.UV1.X += 1;
                        }

                        if (v.UV1.Y > 0)
                        {
                            v.UV1.Y -= 1;
                        }
                    }
                }
            }

            // Tangents have to be recalculated because we moved the UVs.
            foreach (var m in model.MeshGroups)
            {
                foreach (var p in m.Parts)
                {
                    foreach (var v in p.Vertices)
                    {
                        v.Tangent = Vector3.Zero;
                        v.Binormal = Vector3.Zero;
                        v.Handedness = false;
                    }
                }
            }
            MakeImportReady(model, loggingFunction);

        }

        // Resets UV2 to [0,0]
        public static void ClearUV2(TTModel model, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            loggingFunction(false, "Clearing UV2...");
            foreach (var m in model.MeshGroups)
            {
                foreach (var p in m.Parts)
                {
                    foreach (var v in p.Vertices)
                    {

                        v.UV2 = Vector2.Zero;
                    }
                }
            }
        }

        // Resets Vertex Color to White.
        public static void ClearVColor(TTModel model, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            loggingFunction(false, "Clearing Vertex Color...");
            foreach (var m in model.MeshGroups)
            {
                foreach (var p in m.Parts)
                {
                    foreach (var v in p.Vertices)
                    {

                        v.VertexColor[0] = 255;
                        v.VertexColor[1] = 255;
                        v.VertexColor[2] = 255;
                    }
                }
            }
        }

        // Resets Vertex Alpha to 255
        public static void ClearVAlpha(TTModel model, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            loggingFunction(false, "Clearing Vertex Alpha...");
            foreach (var m in model.MeshGroups)
            {
                foreach (var p in m.Parts)
                {
                    foreach (var v in p.Vertices)
                    {

                        v.VertexColor[3] = 255;
                    }
                }
            }
        }

        // Clones UV1 to UV2
        public static void CloneUV2(TTModel model, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            loggingFunction(false, "Cloning UV1 to UV2...");
            foreach (var m in model.MeshGroups)
            {
                foreach (var p in m.Parts)
                {
                    foreach (var v in p.Vertices)
                    {

                        v.UV2 = v.UV1;
                    }
                }
            }
        }

        /// <summary>
        /// Converts a model being imported to match the race of an already existing system file.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="originalRace"></param>
        /// <param name="loggingFunction"></param>
        public static void RaceConvert(TTModel incomingModel, XivRace modelRace, string originalModelPath, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            // Extract the original race from the ttModel if we weren't provided with one.
            var raceRegex = new Regex("c([0-9]{4})");
            var match = raceRegex.Match(originalModelPath);
            XivRace race = XivRace.All_Races;
            if (match.Success)
            {
                loggingFunction(false, "Converting model from " + modelRace.GetDisplayName() + " to " + race.GetDisplayName() + "...");
                race = XivRaces.GetXivRace(match.Groups[1].Value);
                RaceConvert(incomingModel, race, modelRace, loggingFunction);
            }
            else
            {
                loggingFunction(true, "Racial Conversion cancelled - Model is not a racial model.");
            }
        }

        public static void RaceConvert(TTModel model, XivRace targetRace, XivRace originalRace = XivRace.All_Races, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            // Extract the original race from the ttModel if we weren't provided with one.
            if (originalRace == XivRace.All_Races)
            {
                var raceRegex = new Regex("c([0-9]{4})");
                if (!model.IsInternal)
                {
                    var match = raceRegex.Match(model.Source);
                    if (match.Success)
                    {
                        originalRace = XivRaces.GetXivRace(match.Groups[1].Value);
                    }
                    else
                    {
                        loggingFunction(true, "Racial Conversion cancelled - Model is not a racial model.");
                    }

                }
                else
                {
                    throw new InvalidDataException("Cannot racially convert external model without provided Original Race value.");
                }
            }
            RaceConvertRecursive(model, targetRace, originalRace, loggingFunction);
            //ModelModifiers.CalculateTangents(model, loggingFunction);
        }


        /// <summary>
        /// Recursive function for converting races.  Split out and set private so that
        /// We don't constantly recalculate tangents and do re-validation on every pass.
        /// Raceconvert() is the correct entry point for this function.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="targetRace"></param>
        /// <param name="originalRace"></param>
        /// <param name="loggingFunction"></param>
        private static void RaceConvertRecursive(TTModel model, XivRace targetRace, XivRace originalRace, Action<bool, string> loggingFunction)
        {
            try
            {
                // Current race is already parent node
                // Direct conversion
                // [ Current > (apply deform) > Target ]
                if (originalRace.IsDirectParentOf(targetRace))
                {
                    ModelModifiers.ApplyRacialDeform(model, targetRace, false, loggingFunction);
                }
                // Target race is parent node of Current race
                // Convert to parent (invert deform)
                // [ Current > (apply inverse deform) > Target ]
                else if (targetRace.IsDirectParentOf(originalRace))
                {
                    ModelModifiers.ApplyRacialDeform(model, originalRace, true, loggingFunction);
                }
                // Current race is not parent of Target Race and Current race has parent
                // Make a recursive call with the current races parent race
                // [ Current > (apply inverse deform) > Current.Parent > Recursive Call ]
                else if (originalRace.GetNode().Parent != null)
                {
                    ModelModifiers.ApplyRacialDeform(model, originalRace, true, loggingFunction);
                    RaceConvert(model, targetRace, originalRace.GetNode().Parent.Race, loggingFunction);
                }
                // Current race has no parent
                // Make a recursive call with the target races parent race
                // [ Target > (apply deform on Target.Parent) > Target.Parent > Recursive Call ]
                else
                {
                    ModelModifiers.ApplyRacialDeform(model, targetRace.GetNode().Parent.Race, false, loggingFunction);
                    RaceConvert(model, targetRace.GetNode().Parent.Race, targetRace, loggingFunction);
                }
            }
            catch (Exception ex)
            {
                // Show a warning that deforms are missing for the target race
                // This mostly happens with Face, Hair, Tails, Ears, and Female > Male deforms
                // The model is still added but no deforms are applied
                loggingFunction(true, "Unable to convert racial model.");
            }
        }

        /// <summary>
        /// Attempts to deform a model from its original race to the given target race.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="targetRace"></param>
        /// <param name="loggingFunction"></param>
        public static void ApplyRacialDeform(TTModel model, XivRace targetRace, bool invert = false, Action<bool, string> loggingFunction = null)
        {
            try
            {
                if (loggingFunction == null)
                {
                    loggingFunction = NoOp;
                }

                loggingFunction(false, "Attempting to deform model...");

                Dictionary<string, Matrix> deformations, inverted, normalmatrixes, invertednormalmatrixes;
                Mdl.GetDeformationMatrices(targetRace, out deformations, out inverted, out normalmatrixes, out invertednormalmatrixes);


                // Check if deformation is possible
                var missingDeforms = new HashSet<string>();

                foreach (var m in model.MeshGroups)
                {
                    foreach (var mBone in m.Bones)
                    {
                        if (!deformations.ContainsKey(mBone))
                        {
                            missingDeforms.Add(mBone);
                        }
                    }
                }

                // Throw an exception if there is any missing deform bones
                if (missingDeforms.Any())
                {
                    // For a bone to be missing in the deformation data completely, it has to have come from a different skeleton, which
                    // had the bone, while our new one has no entry for it at all.  In these cases, just use identity.
                    var skel = model.ResolveBoneHeirarchy(loggingFunction);
                    foreach(var bone in missingDeforms)
                    {
                        deformations[bone] = Matrix.Identity;
                        inverted[bone] = Matrix.Identity;
                        normalmatrixes[bone] = Matrix.Identity;
                        invertednormalmatrixes[bone] = Matrix.Identity;
                    }
                }

                // Now we're ready to animate...

                // For each mesh
                foreach (var m in model.MeshGroups)
                {
                    //And each part in that mesh...
                    foreach (var p in m.Parts)
                    {
                        // And each vertex in that part...
                        foreach (var v in p.Vertices)
                        {
                            Vector3 position = Vector3.Zero;
                            Vector3 normal = Vector3.Zero;
                            Vector3 binormal = Vector3.Zero;
                            Vector3 tangent = Vector3.Zero;

                            // And each bone in that vertex.
                            for (var b = 0; b < 4; b++)
                            {
                                if (v.Weights[b] == 0) continue;
                                var boneName = m.Bones[v.BoneIds[b]];
                                var boneWeight = (v.Weights[b]) / 255f;

                                var matrix = Matrix.Identity;
                                var normalMatrix = Matrix.Identity;
                                matrix = deformations[boneName];
                                normalMatrix = normalmatrixes[boneName];

                                if (invert)
                                {
                                    matrix = inverted[boneName];
                                    normalMatrix = invertednormalmatrixes[boneName];
                                }


                                position += MatrixTransform(v.Position, matrix) * boneWeight;
                                normal += MatrixTransform(v.Normal, normalMatrix) * boneWeight;
                                binormal += MatrixTransform(v.Binormal, normalMatrix) * boneWeight;
                                tangent += MatrixTransform(v.Tangent, normalMatrix) * boneWeight;
                            }

                            v.Position = position;
                            v.Normal = normal;
                            v.Binormal = binormal;
                            v.Tangent = tangent;
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                throw (ex);
            }
        }


        /// <summary>
        /// This takes a standard Affine Transformation matrix [0,0,0,1 on bottom], and a vector, and applies the transformation to it.
        /// Treating the vector as a [1x4] Column, with [1] in the last entry.  This function is necessary because SharpDX implementation
        /// of transforms assumes your affine matrices are set up with translation on the bottom(and vectors as rows), not the right(and vectors as columns).
        /// </summary>
        /// <param name="vector"></param>
        /// <param name="transform"></param>
        /// <param name="result"></param>
        private static Vector3 MatrixTransform(Vector3 vector, Matrix transform)
        {
            var result = new Vector3(
                (vector.X * transform[0]) +  (vector.Y * transform[1])  + (vector.Z * transform[2])  + (1.0f * transform[3]),
                (vector.X * transform[4]) +  (vector.Y * transform[5])  + (vector.Z * transform[6])  + (1.0f * transform[7]),
                (vector.X * transform[8]) +  (vector.Y * transform[9])  + (vector.Z * transform[10]) + (1.0f * transform[11]));

            return result;
        }

        /// <summary>
        /// This function does all the minor adjustments to a Model that makes it
        /// ready for injection into the SE filesystem.  Such as flipping the 
        /// UVs, calculating tangents, and applying the global level size multiplier.
        /// Likewise, MakeExportReady() undoes this process.
        /// 
        /// Weight check skip lets us avoid some calculation in cases where we already know they're fine.
        /// </summary>
        public static void MakeImportReady(TTModel model, Action<bool, string> loggingFunction = null, bool reconvert = false)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            // Calculate Tangents if needed - BEFORE flipping UVs.
            // Skip this is we're just reconverting back, to avoid any potential issues and save time.
            if (!reconvert)
            {
                CalculateTangents(model, loggingFunction);
            }

            var totalMajorCorrections = 0;
            var warnings = new List<string>();
            var mIdx = 0;
            foreach (var m in model.MeshGroups)
            {
                var pIdx = 0;
                foreach (var p in m.Parts)
                {
                    var vIdx = 0;
                    foreach (var v in p.Vertices)
                    {
                        // UV Flipping
                        v.UV1[1] *= -1;
                        v.UV2[1] *= -1;

                        if (!reconvert)
                        {
                            // Weight Validation
                            if (model.HasWeights)
                            {
                                int boneSum = 0;
                                // Weight corrections.
                                while (boneSum != 255)
                                {
                                    boneSum = 0;
                                    var mostMajor = 0;
                                    var most = 0;
                                    // Loop them to sum them up.
                                    // and snag the least/most major influences while we're at it.
                                    for (var i = 0; i < v.Weights.Length; i++)
                                    {
                                        var value = v.Weights[i];

                                        // Don't care about 0 weight entries.
                                        if (value == 0) continue;

                                        boneSum += value;
                                        if (value > most)
                                        {
                                            mostMajor = i;
                                            most = value;
                                        }
                                    }

                                    if(most == 0)
                                    {
                                        loggingFunction(true, "Group: " + mIdx + " Part:" + pIdx + " Vertex:" + vIdx + " Has no valid bone weights.  This will cause animation issues.");
                                        totalMajorCorrections++;
                                        v.Weights[0] = 255;
                                        break;
                                    }

                                    var alteration = 255 - boneSum;
                                    if (Math.Abs(alteration) > 1)
                                    {
                                        totalMajorCorrections++;
                                    }

                                    if (Math.Abs(alteration) > 255)
                                    {
                                        // Just No.
                                        v.Weights[0] = 255;
                                        v.Weights[1] = 0;
                                        v.Weights[1] = 0;
                                        v.Weights[1] = 0;
                                        break;

                                    }

                                    // Take or Add to the most major bone.
                                    v.Weights[mostMajor] += (byte)alteration;
                                    boneSum += alteration;
                                }
                            }
                        }
                        vIdx++;
                    }
                    pIdx++;
                }
                mIdx++;
            }

            if (totalMajorCorrections > 0)
            {
                loggingFunction(true, totalMajorCorrections.ToString() + " Vertices had major corrections made to their weight data.");
            }

        }

        /// <summary>
        /// This process undoes all the strange minor adjustments to a model
        /// that FFXIV expects in the SE filesystem, such as flipping the UVs,
        /// and having tiny ass models.
        /// </summary>
        public static void MakeExportReady(TTModel model, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            foreach (var m in model.MeshGroups)
            {
                foreach (var p in m.Parts)
                {
                    foreach (var v in p.Vertices)
                    {
                        // UV Flipping
                        v.UV1[1] *= -1;
                        v.UV2[1] *= -1;
                    }
                }
            }
        }

        /// <summary>
        /// Convenience function for calculating tangent data for a TTModel.
        /// </summary>
        /// <param name="model"></param>
        public static void CalculateTangents(TTModel model, Action<bool, string> loggingFunction = null)
        {
            if(loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            loggingFunction(false, "Calculating Tangents...");
            var hasTangents = model.MeshGroups.Any(x => x.Parts.Any(x => x.Vertices.Any(x => x.Tangent != Vector3.Zero)));
            var hasBinormals = model.MeshGroups.Any(x => x.Parts.Any(x => x.Vertices.Any(x => x.Binormal != Vector3.Zero)));

            if(hasTangents && hasBinormals)
            {
                // Why are we here?  Go away.
                return;
            }

            // If we already have binormal data, we can just use the cheaper function.
            if (hasBinormals)
            {
                CalculateTangentsFromBinormals(model, loggingFunction);
                return;
            }

            // Technically we could have another shortcut case here if we have tangent but not binormal data.
            // But that never happens in ffxiv.



            foreach (var m in model.MeshGroups)
            {
                foreach (var p in m.Parts)
                {
                    // Make sure there's actually data to use...
                    if (p.Vertices.Count == 0 || p.TriangleIndices.Count == 0)
                    {
                        continue;
                    }

                    // Interim arrays for calculations
                    var tangents = new List<Vector3>(p.Vertices.Count);
                    tangents.AddRange(Enumerable.Repeat(Vector3.Zero, p.Vertices.Count));
                    var bitangents = new List<Vector3>(p.Vertices.Count);
                    bitangents.AddRange(Enumerable.Repeat(Vector3.Zero, p.Vertices.Count));

                    // Calculate Tangent, Bitangent/Binormal and Handedness.

                    // This loops for each TRI, building up the sum
                    // tangent/bitangent angles at each VERTEX.
                    for (var a = 0; a < p.TriangleIndices.Count; a += 3)
                    {
                        var vertexId1 = p.TriangleIndices[a];
                        var vertexId2 = p.TriangleIndices[a + 1];
                        var vertexId3 = p.TriangleIndices[a + 2];

                        var vertex1 = p.Vertices[vertexId1];
                        var vertex2 = p.Vertices[vertexId2];
                        var vertex3 = p.Vertices[vertexId3];

                        var deltaX1 = vertex2.Position.X - vertex1.Position.X;
                        var deltaX2 = vertex3.Position.X - vertex1.Position.X;

                        var deltaY1 = vertex2.Position.Y - vertex1.Position.Y;
                        var deltaY2 = vertex3.Position.Y - vertex1.Position.Y;

                        var deltaZ1 = vertex2.Position.Z - vertex1.Position.Z;
                        var deltaZ2 = vertex3.Position.Z - vertex1.Position.Z;

                        var deltaU1 = vertex2.UV1.X - vertex1.UV1.X;
                        var deltaU2 = vertex3.UV1.X - vertex1.UV1.X;

                        var deltaV1 = vertex2.UV1.Y - vertex1.UV1.Y;
                        var deltaV2 = vertex3.UV1.Y - vertex1.UV1.Y;

                        var r = 1.0f / (deltaU1 * deltaV2 - deltaU2 * deltaV1);
                        var sdir = new Vector3((deltaV2 * deltaX1 - deltaV1 * deltaX2) * r, (deltaV2 * deltaY1 - deltaV1 * deltaY2) * r, (deltaV2 * deltaZ1 - deltaV1 * deltaZ2) * r);
                        var tdir = new Vector3((deltaU1 * deltaX2 - deltaU2 * deltaX1) * r, (deltaU1 * deltaY2 - deltaU2 * deltaY1) * r, (deltaU1 * deltaZ2 - deltaU2 * deltaZ1) * r);

                        tangents[vertexId1] += sdir;
                        tangents[vertexId2] += sdir;
                        tangents[vertexId3] += sdir;

                        bitangents[vertexId1] += tdir;
                        bitangents[vertexId2] += tdir;
                        bitangents[vertexId3] += tdir;
                    }

                    // Loop the VERTEXES now to calculate the end tangent/bitangents based on the summed data for each VERTEX
                    for (var vertexId = 0; vertexId < p.Vertices.Count; ++vertexId)
                    {
                        // Reference: https://marti.works/posts/post-calculating-tangents-for-your-mesh/post/
                        // We were already doing these calculations to establish handedness, but we weren't actually
                        // using the other results before.  Better to kill the previous computations and use these numbers
                        // for everything to avoid minor differences causing errors.

                        //var posIdx = vDict[a];
                        var vertex = p.Vertices[vertexId];

                        var n = vertex.Normal;

                        var t = tangents[vertexId];
                        var b = bitangents[vertexId];

                        // Calculate tangent vector
                        var tangent = t - (n * Vector3.Dot(n, t));
                        tangent = Vector3.Normalize(tangent);

                        // Compute binormal
                        var binormal = Vector3.Cross(n, tangent);
                        binormal.Normalize();

                        // Compute handedness
                        int handedness = Vector3.Dot(Vector3.Cross(t, b), n) > 0 ? 1 : -1;

                        // Apply handedness
                        binormal *= handedness;

                        vertex.Tangent = tangent;
                        vertex.Binormal = binormal;
                        vertex.Handedness = handedness < 0 ? true : false;

                        // FFXIV actually tracks BINORMAL handedness, not TANGENT handeness, so we have to reverse this.
                        vertex.Handedness = !vertex.Handedness;
                    }
                }
            }
        }

        /// <summary>
        /// Calculates the tangent data, assuming we already have the binormal data available.
        /// </summary>
        /// <param name="normals"></param>
        /// <param name="binormals"></param>
        /// <param name="handedness"></param>
        /// <returns></returns>
        public static void CalculateTangentsFromBinormals(TTModel model, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }
            loggingFunction(false, "Calculating Tangents from Binormal Data...");

            foreach ( var m in model.MeshGroups)
            {
                foreach(var p in m.Parts)
                {
                    foreach(var v in p.Vertices)
                    {

                        var tangent = Vector3.Cross(v.Normal, v.Binormal);
                        tangent *= (v.Handedness == true ? -1 : 1);
                        v.Tangent = tangent;
                    }
                }
            }
        }


        private static readonly Regex _skinMaterialRegex = new Regex("^/mt_c([0-9]{4})b([0-9]{4})_.+\\.mtrl$");



        /// <summary>
        /// Fixes up the racial skin references in the model's materials.
        /// this isn't actually necessary as the game will auto-resolve these regardless, but it's nice to do.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="newInternalPath"></param>
        public static void FixUpSkinReferences(TTModel model, string newInternalPath, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            // Here we should to go in and correct any Skin Material references to point to the skin material for this race.
            // It's not actually -NEEDED-, as the game will dynamically resolve them anyways to the player's skin material, but it's good for user expectation and sanity.

            var raceRegex = new Regex("(c[0-9]{4})");

            // So we have to do this step first.
            var newRaceMatch = raceRegex.Match(newInternalPath);

            // Now model doesn't exist in a racial folder.  Nothing to fix up/impossible to.
            if (!newRaceMatch.Success)
            {
                return;
            }

            loggingFunction(false, "Fixing up racial skin references...");

            // Need to find the racial skin for this race.
            var baseRace = XivRaces.GetXivRace(newRaceMatch.Groups[1].Value.Substring(1));

            FixUpSkinReferences(model, baseRace, loggingFunction);
        }




        /// <summary>
        /// Fixes up the racial skin references in the model's materials.
        /// this isn't actually necessary as the game will auto-resolve these regardless, but it's nice to do.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="newInternalPath"></param>
        public static void FixUpSkinReferences(TTModel model, XivRace baseRace, Action<bool, string> loggingFunction = null, string bodyReplacement = "")
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            var skinRace = XivRaceTree.GetSkinRace(baseRace);
            var skinRaceString = "c" + XivRaces.GetRaceCode(skinRace);

            var raceRegex = new Regex("(c[0-9]{4})");
            var bodyRegex = new Regex("(b[0-9]{4})");

            foreach (var m in model.MeshGroups)
            {
                if (m.Material == null) continue;

                // Only fix up -skin- materials.
                if (_skinMaterialRegex.IsMatch(m.Material))
                {
                    var mtrlMatch = raceRegex.Match(m.Material);
                    if (mtrlMatch.Success && mtrlMatch.Groups[1].Value != skinRaceString)
                    {
                        m.Material = m.Material.Replace(mtrlMatch.Groups[1].Value, skinRaceString);

                        // Reset the body ID if we actually changed races.
                        bodyReplacement = string.IsNullOrEmpty(bodyReplacement) ? "b0001" : bodyReplacement;
                        m.Material = bodyRegex.Replace(m.Material, bodyReplacement);
                    }
                    else if (bodyReplacement != "")
                    {
                        m.Material = bodyRegex.Replace(m.Material, bodyReplacement);
                    }
                }
            }

        }

        /// <summary>
        /// /dev/null function used when no logging parameter is supplied, 
        /// just to make sure we don't have to constantly null-check the logging functions.
        /// </summary>
        /// <param name="warning"></param>
        /// <param name="message"></param>
        public static void NoOp(bool warning, string message)
        {
            // No-Op
        }

    }
}
