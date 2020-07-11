using HelixToolkit.SharpDX.Core;
using HelixToolkit.SharpDX.Core.Model.Scene2D;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using xivModdingFramework.Cache;
using xivModdingFramework.Helpers;
using xivModdingFramework.Textures.Enums;
using static xivModdingFramework.Cache.XivCache;

namespace xivModdingFramework.Models.DataContainers
{

    /// <summary>
    /// Class representing a fully qualified, Square-Enix style Vertex.
    /// In SE's system, these values are all keyed to the same index value, 
    /// so none of them can be separated from the others without creating
    /// an entirely new vertex.
    /// </summary>
    public class TTVertex {
        public Vector3 Position = new Vector3(0,0,0);

        public Vector3 Normal = new Vector3(0, 0, 0);
        public Vector3 Tangent = new Vector3(0, 0, 0);
        public Vector3 Binormal = new Vector3(0, 0, 0);
        public bool Handedness = true;

        public Vector2 UV1 = new Vector2(0, 0);
        public Vector2 UV2 = new Vector2(0, 0);

        // RGBA
        public byte[] VertexColor = new byte[4];

        // BoneIds and Weights.  FFXIV Vertices can only be affected by a maximum of 4 bones.
        public byte[] BoneIds = new byte[4];
        public byte[] Weights = new byte[4];
    }


    /// <summary>
    /// Class representing the base infromation for a Mesh Part, unrelated
    /// to the Item or anything else above the level of the base 3D model.
    /// </summary>
    public class TTMeshPart
    {
        // Purely semantic/not guaranteed to be unique.
        public string Name = null;

        // List of fully qualified TT/SE style vertices.
        public List<TTVertex> Vertices = new List<TTVertex>();

        // List of Vertex IDs that make up the triangles of the mesh.
        public List<int> TriangleIndices = new List<int>();

        // List of Attributes attached to this part.
        public HashSet<string> Attributes = new HashSet<string>();

    }

    /// <summary>
    /// Class representing a mesh group in TexTools
    /// At the FFXIV level, all the parts are crushed down together into one
    /// Singular 'Mesh'.
    /// </summary>
    public class TTMeshGroup
    {
        public List<TTMeshPart> Parts = new List<TTMeshPart>();

        /// <summary>
        /// Material used by this Mesh Group.
        /// </summary>
        public string Material;


        /// <summary>
        /// List of bones used by this mesh group's vertices.
        /// </summary>
        public List<string> Bones = new List<string>();

        /// <summary>
        /// Accessor for the full unified MeshGroup level Vertex list.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public TTVertex GetVertexAt(int id)
        {
            if (Parts.Count == 0)
                return null;

            var startingOffset = 0;
            TTMeshPart part = Parts[0];
            foreach(var p in Parts)
            {
                if(startingOffset + p.Vertices.Count < id)
                {
                    startingOffset += p.Vertices.Count;
                } else
                {
                    part = p;
                    break;
                }
            }

            var realId = id - startingOffset;
            return part.Vertices[realId];
        }

        /// <summary>
        /// Accessor for the full unified MeshGroup level Index list
        /// Also corrects the resultant Index to point to the MeshGroup level Vertex list.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public int GetIndexAt(int id)
        {
            if (Parts.Count == 0)
                return -1;

            var startingOffset = 0;
            var partId = 0;
            for(var i = 0; i < Parts.Count; i++)
            {
                var p = Parts[i];
                if (startingOffset + p.TriangleIndices.Count <= id)
                {
                    startingOffset += p.TriangleIndices.Count;
                }
                else
                {
                    partId = i;
                    break;
                }
            }
            var part = Parts[partId];
            var realId = id - startingOffset;
            var realVertexId = part.TriangleIndices[realId];

            var offsets = PartVertexOffsets;
            var modifiedVertexId = realVertexId + offsets[partId];
            return modifiedVertexId;

        }

        /// <summary>
        /// When stacked together, this is the list of points which the Triangle Index pointer would start for each part.
        /// </summary>
        public List<int> PartIndexOffsets
        {
            get
            {
                var list = new List<int>();
                var offset = 0;
                foreach (var p in Parts)
                {
                    list.Add(offset);
                    offset += p.TriangleIndices.Count;
                }
                return list;
            }
        }

        /// <summary>
        /// When stacked together, this is the list of points which the Vertex pointer would start for each part.
        /// </summary>
        public List<int> PartVertexOffsets
        {
            get
            {
                var list = new List<int>();
                var offset = 0;
                foreach (var p in Parts)
                {
                    list.Add(offset);
                    offset += p.Vertices.Count;
                }
                return list;
            }
        }

        public uint VertexCount
        {
            get
            {
                uint count = 0;
                foreach (var p in Parts)
                {
                    count += (uint)p.Vertices.Count;
                }
                return count;
            }
        }
        public uint IndexCount
        {
            get
            {
                uint count = 0;
                foreach (var p in Parts)
                {
                    count += (uint)p.TriangleIndices.Count;
                }
                return count;
            }
        }
    }


    /// <summary>
    /// Class representing the base information for a 3D Model, unrelated to the 
    /// item or anything else that it's associated with.  This should be writeable
    /// into the FFXIV file system with some calculation, but is primarly a class
    /// for I/O with importers/exporters, and should not contain information like
    /// padding bytes or unknown bytes unless this is data the end user can 
    /// manipulate to some effect.
    /// </summary>
    public class TTModel
    {
        /// <summary>
        /// The Mesh groups and parts of this mesh.
        /// </summary>
        public List<TTMeshGroup> MeshGroups = new List<TTMeshGroup>();

        /// <summary>
        /// Readonly list of bones that are used in this model.
        /// </summary>
        public List<string> Bones
        {
            get
            {
                var ret = new SortedSet<string>();
                foreach (var m in MeshGroups)
                {
                    foreach(var b in m.Bones)
                    {
                        ret.Add(b);
                    }
                }
                return ret.ToList();
            }
        }

        /// <summary>
        /// Readonly list of Materials used in this model.
        /// </summary>
        public List<string> Materials
        {
            get
            {
                var ret = new SortedSet<string>();
                foreach(var m in MeshGroups)
                {
                    ret.Add(m.Material);
                }
                return ret.ToList();
            }
        }

        /// <summary>
        /// Readonly list of attributes used by this model.
        /// </summary>
        public List<string> Attributes
        {
            get
            {
                var ret = new SortedSet<string>();
                foreach( var m in MeshGroups)
                {
                    foreach(var p in m.Parts)
                    {
                        foreach(var a in p.Attributes)
                        {
                            ret.Add(a);
                        }
                    }
                }
                return ret.ToList();
            }
        }


        /// <summary>
        /// Whether or not to write Shape data to the resulting MDL.
        /// </summary>
        public bool EnableShapeData = false;

        /// <summary>
        /// Whether or not this Model actually has animation/weight data.
        /// </summary>
        public bool HasWeights
        {
            get
            {
                foreach (var m in MeshGroups)
                {
                    if (m.Bones.Count > 0)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Sum count of Vertices in this model.
        /// </summary>
        public uint VertexCount
        {
            get
            {
                uint count = 0;
                foreach (var m in MeshGroups)
                {
                    count += (uint)m.VertexCount;
                }
                return count;
            }
        }

        /// <summary>
        /// Sum count of Indices in this model.
        /// </summary>
        public uint IndexCount
        {
            get
            {
                uint count = 0;
                foreach (var m in MeshGroups)
                {
                    count += (uint)m.IndexCount;
                }
                return count;
            }
        }


        /// <summary>
        /// Creates a bone set from the model and group information.
        /// </summary>
        /// <param name="PartNumber"></param>
        public List<byte> GetBoneSet(int groupNumber)
        {
            var fullList = Bones;
            var partial = MeshGroups[groupNumber].Bones;

            var result = new List<byte>(new byte[128]);

            if(partial.Count > 64)
            {
                throw new InvalidDataException("Individual Mesh groups cannot reference more than 64 bones.");
            }

            // This is essential a translation table of [mesh group bone index] => [full model bone index]
            for (int i = 0; i < partial.Count; i++)
            {
                var b = BitConverter.GetBytes(((short) fullList.IndexOf(partial[i])));
                IOUtil.ReplaceBytesAt(result, b, i * 2);
            }

            result.AddRange(BitConverter.GetBytes(partial.Count));

            return result;
        }

        /// <summary>
        /// Gets the material index for a given group, based on model and group information.
        /// </summary>
        /// <param name="groupNumber"></param>
        /// <returns></returns>
        public short GetMaterialIndex(int groupNumber) {
            
            // Sanity check
            if (MeshGroups.Count <= groupNumber) return 0;

            var m = MeshGroups[groupNumber];

            // By definition the Materials object must contain the mesh material, so no need to check for -1 here.
            return (short) Materials.IndexOf(m.Material); 
        }

        /// <summary>
        /// Retrieves the bitmask value for a part's attributes, based on part and model settings.
        /// </summary>
        /// <param name="groupNumber"></param>
        /// <returns></returns>
        public uint GetAttributeBitmask(int groupNumber, int partNumber)
        {
            var allAttributes = Attributes;
            if(allAttributes.Count > 32)
            {
                throw new InvalidDataException("Models cannot have more than 32 total attributes.");
            }
            uint mask = 0;

            var partAttributes = MeshGroups[groupNumber].Parts[partNumber].Attributes;

            uint bit = 1;
            for(int i = 0; i < allAttributes.Count; i++)
            {
                var a = allAttributes[i];
                bit = (uint)1 << i;

                if(partAttributes.Contains(a))
                {
                    mask = (uint)(mask | bit);
                }
                
            }

            return mask;
        }



        /// <summary>
        /// Merges in the side data elements from a Mdl file that we 
        /// wouldn't normally get from one of the importers, ex. material names
        /// attribute names, etc.
        /// 
        /// Users may manipulate this data further via Advanced Import after it
        /// has been merged over.
        /// 
        /// Raw Geometry data/data that may have come from the external importers
        /// should *NOT* be overwritten/modified.
        /// </summary>
        /// <param name="rawMdl"></param>
        public void MergeData(XivMdl rawMdl)
        {
            // Only need to loop the data we're merging in, other elements are naturally
            // left blank/default.

            var attributes = rawMdl.PathData.AttributeList;
            for(var mIdx = 0; mIdx < rawMdl.LoDList[0].MeshDataList.Count; mIdx++)
            {
                // Can only carry in data to meshes that exist
                if (mIdx >= MeshGroups.Count) continue;

                var md = rawMdl.LoDList[0].MeshDataList[mIdx];
                var localMesh = MeshGroups[mIdx];

                // Copy over Material
                var matIdx = md.MeshInfo.MaterialIndex;
                if (matIdx < rawMdl.PathData.MaterialList.Count)
                {
                    var oldMtrl = rawMdl.PathData.MaterialList[matIdx];
                    localMesh.Material = oldMtrl;
                }
                

                for(var pIdx = 0; pIdx < md.MeshPartList.Count; pIdx++)
                {
                    // Can only carry in data to parts that exist
                    if (pIdx >= localMesh.Parts.Count) continue;


                    var p = md.MeshPartList[pIdx];
                    var localPart = localMesh.Parts[pIdx];

                    // Copy over attributes. (Convert from bitmask to full string values)
                    var mask = p.AttributeBitmask;
                    uint bit = 1;
                    for(int i = 0; i < 32; i++)
                        {
                        bit = (uint)1 << i;

                        if( (mask & bit) > 0)
                        {
                            // Can't add attributes that don't exist (should never be hit, but sanity).
                            if (i >= attributes.Count) continue;

                            localPart.Attributes.Add(attributes[i]);
                        }
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
        public static List<string> MakeImportReady(TTModel model)
        {
            var totalMajorCorrections = 0;
            var warnings = new List<string>();
            foreach(var m in model.MeshGroups) 
            {
                foreach(var p in m.Parts)
                {
                    foreach(var v in p.Vertices)
                    {
                        // Model Size Multiplier.
                        v.Position /= Helpers.Constants.ModelMultiplier;

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

            if(totalMajorCorrections > 0)
            {
                warnings.Add(totalMajorCorrections.ToString() + " Vertices had major corrections made to their weight data.");
            }
            return warnings;
        }

        /// <summary>
        /// This function does all the minor adjustments to a Model that makes it
        /// ready for injection into the SE filesystem.  Such as flipping the 
        /// UVs, and applying the global level size multiplier.
        /// Likewise, MakeImport() undoes this process.
        /// 
        /// This process is expected to be warning-free, so it has no return value.
        /// </summary>
        public static void MakeExportReady(TTModel model)
        {
            foreach (var m in model.MeshGroups)
            {
                foreach (var p in m.Parts)
                {
                    foreach (var v in p.Vertices)
                    {
                        // Model Size Multitplier.
                        v.Position *= Helpers.Constants.ModelMultiplier;

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
        public static void CalculateTangents(TTModel model)
        {
            // Set up arrays.
            foreach(var m in model.MeshGroups)
            {
                foreach(var p in m.Parts)
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
                        var vertex1 = p.Vertices[p.TriangleIndices[a]];
                        var vertex2 = p.Vertices[p.TriangleIndices[a + 1]];
                        var vertex3 = p.Vertices[p.TriangleIndices[a + 2]];

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

                        tangents[p.TriangleIndices[a]] += sdir;
                        tangents[p.TriangleIndices[a+1]] += sdir;
                        tangents[p.TriangleIndices[a+2]] += sdir;

                        bitangents[p.TriangleIndices[a]] += tdir;
                        bitangents[p.TriangleIndices[a+1]] += tdir;
                        bitangents[p.TriangleIndices[a+2]] += tdir;
                    }

                    // Loop the VERTEXES now to calculate the end tangent/bitangents based on the summed data for each VERTEX
                    for (var a = 0; a < p.Vertices.Count; ++a)
                    {
                        // Reference: https://marti.works/posts/post-calculating-tangents-for-your-mesh/post/
                        // We were already doing these calculations to establish handedness, but we weren't actually
                        // using the other results before.  Better to kill the previous computations and use these numbers
                        // for everything to avoid minor differences causing errors.

                        //var posIdx = vDict[a];
                        var vertex = p.Vertices[a];

                        var n = vertex.Normal;

                        var t = tangents[a];
                        var b = bitangents[a];

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
                        vertex.Handedness = handedness > 0 ? true : false;
                    }
                }
            }
        }

        /// <summary>
        /// Loads a TTModel file from a given SQLite3 DB filepath.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static TTModel LoadFromFile(string filePath, Action<bool, string> loggingFuction)
        {

            var connectionString = "Data Source=" + filePath + ";Pooling=False;";
            TTModel model = new TTModel();

            // Spawn a DB connection to do the raw queries.
            using (var db = new SQLiteConnection(connectionString))
            {
                db.Open();
                // Using statements help ensure we don't accidentally leave any connections open and lock the file handle.

                // Load Mesh Parts
                var query = "select * from parts order by mesh asc, part asc;";
                using (var cmd = new SQLiteCommand(query, db))
                {
                    using (var reader = new CacheReader(cmd.ExecuteReader()))
                    {
                        while (reader.NextRow())
                        {
                            var meshNum = reader.GetInt32("mesh");
                            var partNum = reader.GetInt32("part");

                            // Spawn mesh groups as needed.
                            while(model.MeshGroups.Count <= meshNum)
                            {
                                model.MeshGroups.Add(new TTMeshGroup());
                            }

                            // Spawn parts as needed.
                            while(model.MeshGroups[meshNum].Parts.Count <= partNum)
                            {
                                model.MeshGroups[meshNum].Parts.Add(new TTMeshPart());

                            }

                            model.MeshGroups[meshNum].Parts[partNum].Name = reader.GetString("name");
                        }
                    }
                }

                // Load Bones
                query = "select * from bones order by mesh asc, bone_id asc;";
                using (var cmd = new SQLiteCommand(query, db))
                {
                    using (var reader = new CacheReader(cmd.ExecuteReader()))
                    {
                        while (reader.NextRow())
                        {
                            var meshId = reader.GetInt32("mesh");
                            model.MeshGroups[meshId].Bones.Add(reader.GetString("name"));
                        }
                    }
                }

            }

            // Loop for each part, to populate their internal data structures.
            for (var mId = 0; mId < model.MeshGroups.Count; mId++)
            {
                var m = model.MeshGroups[mId];
                for (var pId = 0; pId < m.Parts.Count; pId++)
                {
                    var p = m.Parts[pId];
                    var where = new WhereClause();
                    var mWhere = new WhereClause();
                    mWhere.Column = "mesh";
                    mWhere.Value = mId;
                    var pWhere = new WhereClause();
                    pWhere.Column = "part";
                    pWhere.Value = pId;

                    where.Inner.Add(mWhere);
                    where.Inner.Add(pWhere);

                    // Load Vertices
                    // The reader handles coalescing the null types for us.
                    p.Vertices = BuildListFromTable(connectionString, "vertices", where, async (reader) =>
                    {
                        var vertex = new TTVertex();

                        // Positions
                        vertex.Position.X = reader.GetFloat("position_x");
                        vertex.Position.Y = reader.GetFloat("position_y");
                        vertex.Position.Z = reader.GetFloat("position_z");

                        // Normals
                        vertex.Normal.X = reader.GetFloat("normal_x");
                        vertex.Normal.Y = reader.GetFloat("normal_y");
                        vertex.Normal.Z = reader.GetFloat("normal_z");

                        // Vertex Colors - Vertex color is RGBA
                        vertex.VertexColor[0] = (byte)(Math.Round(reader.GetFloat("color_r") * 255));
                        vertex.VertexColor[1] = (byte)(Math.Round(reader.GetFloat("color_g") * 255));
                        vertex.VertexColor[2] = (byte)(Math.Round(reader.GetFloat("color_b") * 255));
                        vertex.VertexColor[3] = (byte)(Math.Round(reader.GetFloat("color_a") * 255));

                        // UV Coordinates
                        vertex.UV1.X = reader.GetFloat("uv_1_u");
                        vertex.UV1.Y = reader.GetFloat("uv_1_v");
                        vertex.UV2.X = reader.GetFloat("uv_2_u");
                        vertex.UV2.Y = reader.GetFloat("uv_2_v");

                        // Bone Ids
                        vertex.BoneIds[0] = (byte)(reader.GetByte("bone_1_id"));
                        vertex.BoneIds[1] = (byte)(reader.GetByte("bone_2_id"));
                        vertex.BoneIds[2] = (byte)(reader.GetByte("bone_3_id"));
                        vertex.BoneIds[3] = (byte)(reader.GetByte("bone_4_id"));

                        // Weights
                        vertex.Weights[0] = (byte)(Math.Round(reader.GetFloat("bone_1_weight") * 255));
                        vertex.Weights[1] = (byte)(Math.Round(reader.GetFloat("bone_2_weight") * 255));
                        vertex.Weights[2] = (byte)(Math.Round(reader.GetFloat("bone_3_weight") * 255));
                        vertex.Weights[3] = (byte)(Math.Round(reader.GetFloat("bone_4_weight") * 255));

                        return vertex;
                    }).GetAwaiter().GetResult();

                    p.TriangleIndices = BuildListFromTable(connectionString, "indices", where, async (reader) =>
                    {
                        try
                        {
                            return reader.GetInt32("vertex_id");
                        } catch(Exception ex)
                        {
                            throw ex;
                        }
                    }).GetAwaiter().GetResult();
                }
            }


            return model;
        }
    }
}
