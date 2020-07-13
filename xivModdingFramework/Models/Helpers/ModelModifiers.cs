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


        }
    }


    /// <summary>
    /// List of modifier functions to TTModel Objects.
    /// </summary>
    public static class ModelModifiers
    {
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

                // Copy over Material
                var matIdx = md.MeshInfo.MaterialIndex;
                if (matIdx < rawMdl.PathData.MaterialList.Count)
                {
                    var oldMtrl = rawMdl.PathData.MaterialList[matIdx];
                    localMesh.Material = oldMtrl;
                }


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
                        var ogGroup = ogMdl.LoDList[lIdx].MeshDataList[mIdx];
                        var newGroup = ttModel.MeshGroups[mIdx];
                        var newBoneSet = newGroup.Bones;

                        // Have to convert the raw bone set to a useable format...
                        var oldBoneSetRaw = ogMdl.MeshBoneSets[ogGroup.MeshInfo.BoneListIndex];
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
                                            throw new Exception("New model is missing bone used in Shape Data: " + boneName);
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

        // Forces all UV Coordinates in UV1 Layer to [1,-1] Quadrant.
        public static void ForceUVQuadrant(TTModel model, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            loggingFunction(false, "Forcing UV1 to [1,-1]...");
            foreach(var m in model.MeshGroups)
            {
                foreach(var p in m.Parts)
                {
                    foreach(var v in p.Vertices)
                    {

                        v.Position.X = Math.Abs((v.Position.X % 1));
                        v.Position.Y = Math.Abs((v.Position.X % 1)) * -1;
                    }
                }
            }
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

            loggingFunction(false, "Clearing UV2...");
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
        /// This function does all the minor adjustments to a Model that makes it
        /// ready for injection into the SE filesystem.  Such as flipping the 
        /// UVs, and applying the global level size multiplier.
        /// Likewise, MakeExportReady() undoes this process.
        /// 
        /// Returns a list of warnings we might want to inform the user about,
        /// if there were any oddities in the data.
        /// </summary>
        public static void MakeImportReady(TTModel model, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = NoOp;
            }
            
            // Calculate Tangents if needed - BEFORE flipping UVs.
            var hasTangents = model.MeshGroups.Any(x => x.Parts.Any(x => x.Vertices.Any(x => x.Tangent != Vector3.Zero)));
            if (!hasTangents)
            {
                loggingFunction(false, "Calculating Tangent Data...");
                CalculateTangents(model);
            }

            var totalMajorCorrections = 0;
            var warnings = new List<string>();
            foreach (var m in model.MeshGroups)
            {
                foreach (var p in m.Parts)
                {
                    foreach (var v in p.Vertices)
                    {
                        // Model Size Multiplier.
                        v.Position /= xivModdingFramework.Helpers.Constants.ModelMultiplier;

                        // UV Flipping
                        v.UV1[1] *= -1;
                        v.UV2[1] *= -1;

                        if (model.HasWeights)
                        {
                            int boneSum = 0;
                            // Weight corrections.
                            while (boneSum != 255)
                            {
                                boneSum = 0;
                                var mostMajor = -1;
                                var most = -1;
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
                }
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
        /// 
        /// This process is expected to be warning-free, so it has no return value.
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
                        // Model Size Multitplier.
                        v.Position *= xivModdingFramework.Helpers.Constants.ModelMultiplier;

                        // UV Flipping
                        v.UV1[1] *= -1;
                        v.UV2[1] *= -1;
                    }
                }
            }
        }

        /// <summary>
        /// Convenience function for calculating tangent data for a TTModel.
        /// This is significantly more performant than creating Position/Normal lists
        /// and passing them to the Mdl.cs version, but the calculations are the same.
        /// </summary>
        /// <param name="model"></param>
        public static void CalculateTangents(TTModel model, Action<bool, string> loggingFunction = null)
        {
            if(loggingFunction == null)
            {
                loggingFunction = NoOp;
            }

            // Set up arrays.
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
