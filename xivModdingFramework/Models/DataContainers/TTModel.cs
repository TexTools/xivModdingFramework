using HelixToolkit.SharpDX.Core;
using HelixToolkit.SharpDX.Core.Animations;
using HelixToolkit.SharpDX.Core.Core;
using HelixToolkit.SharpDX.Core.Model.Scene2D;
using Newtonsoft.Json;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Models.Enums;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Models.Helpers;
using xivModdingFramework.Models.ModelTextures;
using xivModdingFramework.Mods;
using xivModdingFramework.Textures.Enums;
using static xivModdingFramework.Cache.XivCache;

namespace xivModdingFramework.Models.DataContainers
{

    /// <summary>
    /// Represents the 'type' of a Mesh Group, when stored in MDL format.
    /// </summary>
    public enum EMeshType
    {
        // Standard Mesh
        Standard,
        Water,
        Fog,

        // Extra Meshes
        LightShaft,
        Glass,
        MaterialChange,
        CrestChange,
        ExtraUnknown4,
        ExtraUnknown5,
        ExtraUnknown6,
        ExtraUnknown7,
        ExtraUnknown8,
        ExtraUnknown9,

        Shadow,
        TerrainShadow,
    }
    public static class EMeshTypeExtensions {
        public static bool IsExtraMesh(this EMeshType mesh)
        {
            if((int) mesh >= (int) EMeshType.LightShaft && (int) mesh < (int)EMeshType.Shadow)
            {
                return true;
            }
            return false;
        }

        public static bool UseZeroDefaultOffset(this EMeshType mesh)
        {
            if(mesh.IsExtraMesh() || mesh == EMeshType.TerrainShadow)
            {
                return true;
            }
            return false;
        }
    }



    /// <summary>
    /// Class representing a fully qualified, Square-Enix style Vertex.
    /// In SE's system, these values are all keyed to the same index value, 
    /// so none of them can be separated from the others without creating
    /// an entirely new vertex.
    /// </summary>
    public class TTVertex : ICloneable {
        public Vector3 Position = new Vector3(0,0,0);

        public Vector3 Normal = new Vector3(0, 0, 0);
        public Vector3 Binormal = new Vector3(0, 0, 0);
        public Vector3 Tangent = new Vector3(0, 0, 0);

        // This is Technically BINORMAL handedness in FFXIV.
        // A values of TRUE indicates we need to flip the Tangent when generated. (-1)
        public bool Handedness = false;

        public Vector2 UV1 = new Vector2(0, 0);
        public Vector2 UV2 = new Vector2(0, 0);

        // RGBA
        public byte[] VertexColor = new byte[] { 255, 255, 255, 255 };
        public byte[] VertexColor2 = new byte[] { 0, 0, 0, 255 };

        private const int _BONE_ARRAY_LENGTH = 8;

        // BoneIds and Weights.
        public byte[] BoneIds = new byte[_BONE_ARRAY_LENGTH];
        public byte[] Weights = new byte[_BONE_ARRAY_LENGTH];

        public static bool operator ==(TTVertex a, TTVertex b)
        {
            // Memberwise equality.
            if (a.Position != b.Position) return false;
            if (a.Normal != b.Normal) return false;
            if (a.Binormal != b.Binormal) return false;
            if (a.Handedness != b.Handedness) return false;
            if (a.UV1 != b.UV1) return false;
            if (a.UV2 != b.UV2) return false;

            for(var ci = 0; ci < _BONE_ARRAY_LENGTH; ci++)
            {
                if (ci < 4)
                {
                    if (a.VertexColor[ci] != b.VertexColor[ci]) return false;
                    if (a.VertexColor2[ci] != b.VertexColor2[ci]) return false;
                }

                if (a.BoneIds[ci] != b.BoneIds[ci]) return false;
                if (a.Weights[ci] != b.Weights[ci]) return false;
            }

                return true;
        }

        public static bool operator !=(TTVertex a, TTVertex b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            
            return base.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj.GetType() != typeof(TTVertex)) return false;
            var b = (TTVertex)obj;
            return b == this;
        }

        public object Clone()
        {
            var clone = (TTVertex) this.MemberwiseClone();

            clone.VertexColor = new byte[4];
            clone.VertexColor2 = new byte[4];
            clone.BoneIds = new byte[_BONE_ARRAY_LENGTH];
            clone.Weights = new byte[_BONE_ARRAY_LENGTH];

            Array.Copy(this.BoneIds, 0, clone.BoneIds, 0, _BONE_ARRAY_LENGTH);
            Array.Copy(this.Weights, 0, clone.Weights, 0, _BONE_ARRAY_LENGTH);
            Array.Copy(this.VertexColor, 0, clone.VertexColor, 0, 4);
            Array.Copy(this.VertexColor2, 0, clone.VertexColor2, 0, 4);

            return clone;
        }
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

        public Dictionary<string, TTShapePart> ShapeParts = new Dictionary<string, TTShapePart>();

        public List<Vector3> GetBoundingBox()
        {
            var bb = new List<Vector3>();

            // Calculate Radius here for convenience.
            // These values also used in writing bounding boxes later.
            float minX = 9999.0f, minY = 9999.0f, minZ = 9999.0f;
            float maxX = -9999.0f, maxY = -9999.0f, maxZ = -9999.0f;
            foreach (var v in Vertices)
            {
                minX = minX < v.Position.X ? minX : v.Position.X;
                minY = minY < v.Position.Y ? minY : v.Position.Y;
                minZ = minZ < v.Position.Z ? minZ : v.Position.Z;

                maxX = maxX > v.Position.X ? maxX : v.Position.X;
                maxY = maxY > v.Position.Y ? maxY : v.Position.Y;
                maxZ = maxZ > v.Position.Z ? maxZ : v.Position.Z;
            }
            var minVect = new Vector3(minX, minY, minZ);
            var maxVect = new Vector3(maxX, maxY, maxZ);
            bb.Add(minVect);
            bb.Add(maxVect);

            return bb;
        }

        /// <summary>
        /// Updates all shapes in this part to any updated UV/Normal/etc. data from the base model.
        /// </summary>
        public void UpdateShapeData()
        {
            foreach(var shpKv in ShapeParts)
            {
                var shp = shpKv.Value;

                foreach(var rKv in shp.VertexReplacements)
                {
                    var baseVert = Vertices[rKv.Key];
                    var shapeVert = shp.Vertices[rKv.Value];

                    shp.Vertices[rKv.Value] = (TTVertex)baseVert.Clone();
                    shp.Vertices[rKv.Value].Position = shapeVert.Position;
                }
            }
        }

    }

    /// <summary>
    /// Class representing a shape data part.
    /// A MeshGroup may have any amount of these, including
    /// multiple that have the same shape name.
    /// </summary>
    public class TTShapePart
    {
        /// <summary>
        /// The raw shp_ identifier.
        /// </summary>
        public string Name;

        /// <summary>
        /// The list of vertices this Shape introduces.
        /// </summary>
        public List<TTVertex> Vertices = new List<TTVertex>();

        /// <summary>
        /// Dictionary of [Part Level Vertex #] => [Shape Part Level Vertex #] to replace it with.
        /// </summary>
        public Dictionary<int, int> VertexReplacements = new Dictionary<int, int>(); 
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
        /// Purely semantic.
        /// </summary>
        public string Name;

        /// <summary>
        /// The type of this mesh when stored in MDL format.
        /// </summary>
        public EMeshType MeshType = EMeshType.Standard;


        /// <summary>
        /// List of bones used by this mesh group's vertices.
        /// </summary>
        public List<string> Bones = new List<string>();

        public int GetVertexCount()
        {
            int count = 0;
            foreach(var p in Parts)
            {
                count += p.Vertices.Count;
            }
            return count;
        }

        public int GetIndexCount()
        {
            int count = 0;
            foreach (var p in Parts)
            {
                count += p.TriangleIndices.Count;
            }
            return count;
        }

        /// <summary>
        /// Set an index by its MESH RELEVANT index ID and vertex ID.
        /// </summary>
        /// <param name="indexId"></param>
        /// <param name="vertexIdToSet"></param>
        public void SetIndexAt(int indexId, int vertexIdToSet)
        {
            int verticesSoFar = 0;
            int indicesSoFar = 0;

            foreach(var p in Parts)
            {
                if(indexId >= indicesSoFar + p.TriangleIndices.Count)
                {
                    // Need to keep looping.
                    verticesSoFar += p.Vertices.Count;
                    indicesSoFar += p.TriangleIndices.Count;
                    continue;
                }
                // Okay, we've found the part containing our index.
                var relevantIndex = indexId - indicesSoFar;
                var relevantVertex = vertexIdToSet - verticesSoFar;
                if(relevantVertex < 0 || relevantVertex >= p.Vertices.Count)
                {
                    throw new InvalidDataException("Cannot set triangle index to vertex which is not contained by the same mesh part.");
                }

                p.TriangleIndices[relevantIndex] = relevantVertex;
            }
        }

        /// <summary>
        /// Set a vertex by its MESH RELEVANT vertex id.
        /// </summary>
        /// <param name="vertex"></param>
        public void SetVertexAt(int vertexId, TTVertex vertex)
        {
            int verticesSoFar = 0;

            foreach (var p in Parts)
            {
                if (vertexId >= verticesSoFar + p.Vertices.Count)
                {
                    // Need to keep looping.
                    verticesSoFar += p.Vertices.Count;
                    continue;
                }

                var relevantVertex = vertexId - verticesSoFar;
                p.Vertices[relevantVertex] = vertex;
            }
        }


        /// <summary>
        /// Retrieves all the part information for a given Mesh-Relevant vertex Id.
        /// </summary>
        /// <param name="vertexId"></param>
        /// <returns></returns>
        public (int PartId, int PartReleventOffset) GetPartRelevantVertexInformation(int vertexId)
        {
            int verticesSoFar = 0;

            var pIdx = 0;
            foreach (var p in Parts)
            {
                if (vertexId >= verticesSoFar + p.Vertices.Count)
                {
                    // Need to keep looping.
                    verticesSoFar += p.Vertices.Count;
                    pIdx++;
                    continue;
                }

                var relevantVertex = vertexId - verticesSoFar;
                return (pIdx, relevantVertex);
            }

            return (-1, -1);
        }

        /// <summary>
        /// Gets the part id of the part which owns a given triangle index.
        /// </summary>
        /// <param name="meshRelevantTriangleIndex"></param>
        /// <returns></returns>
        public int GetOwningPartIdByIndex(int meshRelevantTriangleIndex)
        {
            int indicesSoFar = 0;

            var idx = 0;
            foreach (var p in Parts)
            {
                if (meshRelevantTriangleIndex >= indicesSoFar + p.TriangleIndices.Count)
                {
                    // Need to keep looping.
                    indicesSoFar += p.TriangleIndices.Count;
                    idx++;
                    continue;
                }
                return idx;
            }
            return -1;
        }

        /// <summary>
        /// Gets the part id of the part which owns a given vertex.
        /// </summary>
        /// <param name="meshRelevantTriangleIndex"></param>
        /// <returns></returns>
        public int GetOwningPartIdByVertex(int meshRelevantVertexId)
        {
            int verticesSoFar = 0;

            var idx = 0;
            foreach (var p in Parts)
            {
                if (meshRelevantVertexId >= verticesSoFar + p.Vertices.Count)
                {
                    // Need to keep looping.
                    verticesSoFar += p.Vertices.Count;
                    idx++;
                    continue;
                }
                return idx;
            }
            return -1;
        }

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
                if(startingOffset + p.Vertices.Count <= id)
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
        /// Updates all shapes in this mesh group to any updated UV/Normal/etc. data from the base model.
        /// </summary>
        public void UpdateShapeData()
        {
            foreach (var p in Parts)
            {
                p.UpdateShapeData();
            }
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

        /// <summary>
        /// Creates the list of Mesh Level Triangle indices, using Mesh Level index pointers.
        /// This data should be cached and not repeatedly accessed via this property.
        /// </summary>
        public List<int> TriangleIndices
        {
            get
            {
                var indices = new List<int>((int)IndexCount);
                var vertCount = 0;
                foreach(var p in Parts)
                {
                    foreach (var index in p.TriangleIndices)
                    {
                        indices.Add(index + vertCount);
                    }
                    vertCount += p.Vertices.Count;
                }
                return indices;
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
        public static string _SETTINGS_KEY_EXPORT_ALL_BONES = "setting_export_all_bones";

        /// <summary>
        /// The internal or external file path where this TTModel originated from.
        /// </summary>
        public string Source = "";

        /// <summary>
        /// The internal FFXIV Mdl Version of this file.
        /// </summary>
        public ushort MdlVersion;

        /// <summary>
        /// The Mesh groups and parts of this mesh.
        /// </summary>
        public List<TTMeshGroup> MeshGroups = new List<TTMeshGroup>();

        public HashSet<string> ActiveShapes = new HashSet<string>();


        /// <summary>
        /// Re-orders the mesh group list for import.
        /// In specific, mesh groups are ordered by their mesh types, but otherwise kept in-order.
        /// </summary>
        public void OrderMeshGroupsForImport()
        {
            MeshGroups = MeshGroups.OrderBy(x => (int)x.MeshType).ToList();

            if (MeshGroups.Any(x => x.MeshType != EMeshType.Standard &&
            x.Parts.Any(x => x.ShapeParts.Count(x => x.Key.StartsWith("shp_")) > 0)))
            {
                throw new InvalidDataException("Non-Standard Meshes cannot have shape data.");
            }
            
        }


        #region Calculated Properties
        /// <summary>
        /// Does this TTModel have any of the mesh types which require the LoD header extension?
        /// </summary>
        public bool HasExtraMeshes
        {
            get
            {
                return MeshGroups.Any(x => x.MeshType.IsExtraMesh());
            }
        }

        /// <summary>
        /// Is this TTModel populated from an internal file, or external?
        /// </summary>
        public bool IsInternal
        {
            get
            {
                var regex = new Regex("\\.mdl$");
                var match = regex.Match(Source);
                return match.Success;
            }
        }

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
                    if (m.Material != null)
                    {
                        ret.Add(m.Material);
                    }
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


        public ushort GetMeshTypeCount(EMeshType type)
        {
            var ct = MeshGroups.Count(x => x.MeshType == type);
            return (ushort) ct;
        }

        public ushort GetMeshTypeOffset(EMeshType type)
        {
            var anyExist = GetMeshTypeCount(type) > 0;

            // There are meshes, just check where offset starts.
            if (anyExist)
            {
                for (int i = 0; i < MeshGroups.Count; i++)
                {
                    if (MeshGroups[i].MeshType == type)
                    {
                        return (ushort)i;
                    }
                }
            } else if (type.UseZeroDefaultOffset())
            {
                return 0;
            }

            // None exist.  We need to find the last type that did exist.
            var t = type;
            while (!anyExist && t != EMeshType.Standard)
            {
                t = (EMeshType)((int)t - 1);
                anyExist = GetMeshTypeCount(t) > 0;
            }

            if(t == EMeshType.Standard && !anyExist)
            {
                // No meshes before us, so we get 0 offset.
                return 0;
            }

            // Return end of last real mesh.
            var lastRealType = GetMeshTypeOffset(t);
            var offset = lastRealType + MeshGroups.Count(x => x.MeshType == t);

            return (ushort) offset;
        }


        /// <summary>
        /// Whether or not to write Shape data to the resulting MDL.
        /// </summary>
        public bool HasShapeData
        {
            get
            {
                return MeshGroups.Any(x => x.Parts.Any( x => x.ShapeParts.Count(x => x.Key.StartsWith("shp_")) > 0 ));
            }
        }
        
        /// <summary>
        /// List of all shape names used in the model.
        /// </summary>
        public List<string> ShapeNames
        {
            get
            {
                var shapes = new SortedSet<string>();
                foreach(var m in MeshGroups)
                {
                    foreach(var p in m.Parts)
                    {
                        foreach (var shp in p.ShapeParts)
                        {
                            if (!shp.Key.StartsWith("shp_")) continue;
                            shapes.Add(shp.Key);
                        }
                    }
                }
                return shapes.ToList();
            }
        }
        
        /// <summary>
        /// Total # of Shape Parts
        /// </summary>
        public short ShapePartCount
        {
            get
            {
                short sum = 0;
                foreach(var m in MeshGroups)
                {
                    HashSet<string> shapeNames = new HashSet<string>();
                    foreach (var p in m.Parts)
                    {
                        foreach(var shp in p.ShapeParts)
                        {
                            if (!shp.Key.StartsWith("shp_")) continue;
                            shapeNames.Add(shp.Key);
                        }
                    }
                    sum += (short)shapeNames.Count;
                }
                return sum;
            }
        }

        /// <summary>
        /// Total Shape Data (Index) Entries
        /// </summary>
        public ushort ShapeDataCount
        {
            get
            {
                uint sum = 0;
                // This one is a little more complex.
                foreach (var m in MeshGroups)
                {
                    foreach(var p in m.Parts)
                    {
                        foreach(var index in p.TriangleIndices)
                        {
                            // For every index.
                            foreach(var shp in p.ShapeParts)
                            {
                                if (!shp.Key.StartsWith("shp_")) continue;
                                // There is an entry for every shape it shows up in.
                                if (shp.Value.VertexReplacements.ContainsKey(index))
                                {
                                    sum++;
                                }
                            }
                        }
                    }
                }
                
                if(sum > ushort.MaxValue)
                {
                    throw new Exception($"Model exceeds the maximum possible shape data indices.\n\nCurrent: {sum.ToString()}\nMaximum: {ushort.MaxValue.ToString()}");
                }

                return (ushort) sum;
            }
        }

        /// <summary>
        /// Per-Shape sum of parts; matches up by index to ShapeNames.
        /// </summary>
        /// <returns></returns>
        public List<short> ShapePartCounts
        {
            get
            {
                var counts = new List<short>(new short[ShapeNames.Count]);
                var shapeNames = ShapeNames;

                var shapes = new SortedSet<string>();
                var shpIdx = 0;
                foreach (var shpNm in shapeNames)
                {
                    if (!shpNm.StartsWith("shp_")) continue;
                    foreach (var m in MeshGroups)
                    {
                        if (m.Parts.Any(x => x.ShapeParts.ContainsKey(shpNm)))
                        {
                            counts[shpIdx]++;
                        }
                    }
                    shpIdx++;
                }
                return counts;
            }
        }

        private class VertexReplacementInfo
        {
            public int MeshVertexId;
            public TTVertex VertexData;
            public TTShapePart Shape;
            public int ShapeVertexId;
        }

        /// <summary>
        /// Gets all the raw shape data of the mesh for use with importing the data back into FFXIV's file system.
        /// Calling/building this data is somewhat expensive, and should only be done
        /// if actually needed in this specified format.
        /// </summary>
        internal (List<(string ShapeName, int MeshId, Dictionary<int, int> IndexReplacements)> ShapeList, List<List<TTVertex>> Vertices) GetRawShapeParts()
        {
            // This is the final list of ShapeParts that will be written to file.
            var ret = new List<(string ShapeName, int MeshId, Dictionary<int, int> IndexReplacements)>();

            // This is a Per-Mesh list of vertices ordered in vertex ID of the vertex they replace.
            var finalVertices = new List<List<TTVertex>>();

            var meshVertexOffset = 0;
            var meshId = 0;
            var shapeOrder = new List<string>();
            foreach (var m in MeshGroups)
            {

                // Converts Mesh relevant vertex ID to Mesh relevant triangle index.
                var vertexToIndexDictionary = new Dictionary<int, List<int>>();
                var meshIndices = m.TriangleIndices;
                for (int i = 0; i < m.IndexCount; i++)
                {
                    var vertId = meshIndices[i];
                    if(!vertexToIndexDictionary.ContainsKey(vertId))
                    {
                        vertexToIndexDictionary.Add(vertId, new List<int>());
                    }
                    vertexToIndexDictionary[vertId].Add(i);
                }


                // We need to build the dictionaries of Vertex-To-Vertex replacement.

                // ShapeVertexId will be populated only after we compile the final shape vertex list.
                var perShapeDictionary = new Dictionary<string, (List<VertexReplacementInfo> ShapeData, int MinTargetVertex)>();
                var partVertexOffset = 0;
                foreach(var p in m.Parts)
                {
                    foreach(var shapeKv in p.ShapeParts)
                    {
                        var shapeName = shapeKv.Key;
                        if(!shapeName.StartsWith("shp_"))
                        {
                            continue;
                        }

                        var shape = shapeKv.Value;
                        var dataList = new List<VertexReplacementInfo>();
                        var minVert = int.MaxValue;
                        foreach (var replacement in shape.VertexReplacements)
                        {
                            VertexReplacementInfo data = new VertexReplacementInfo();

                            data.MeshVertexId = partVertexOffset + replacement.Key;
                            data.VertexData = shape.Vertices[replacement.Value];
                            data.Shape = shape;
                            dataList.Add(data);

                            if (data.MeshVertexId < minVert)
                            {
                                minVert = data.MeshVertexId;
                            }
                        }
                        
                        // We have to squish all the parts back together here.
                        if(!perShapeDictionary.ContainsKey(shapeName))
                        {
                            perShapeDictionary.Add(shapeName, (dataList, minVert));
                        }
                        else
                        {
                            var val = perShapeDictionary[shapeName];
                            val.ShapeData.AddRange(dataList);
                            if(val.MinTargetVertex > minVert)
                            {
                                val.MinTargetVertex = minVert;
                            }
                            perShapeDictionary[shapeName] = val;
                        }
                    }
                    partVertexOffset += p.Vertices.Count;
                }


                // The dictionaries now need to be sorted to determine vertex write order.
                // (SE Writes shape groups in the order they're encountered in the mesh vertex array... With some kind of unknown tiebreaker.)
                List<(List<VertexReplacementInfo> ShapeData, int MinTargetVertex)> sorted;
                sorted = perShapeDictionary.Values.ToList();
                sorted.Sort((a, b) =>
                {
                    if (a.MinTargetVertex != b.MinTargetVertex)
                    {
                        return a.MinTargetVertex - b.MinTargetVertex;
                    }
                    return String.Compare(a.ShapeData[0].Shape.Name, b.ShapeData[0].Shape.Name);
                });



                // This is now the order which we write the vertex blocks.
                // But -- NOT -- the order the shapes are listed in.
                var vertexList = new List<TTVertex>();
                foreach(var val in sorted)
                {
                    var rawShapeInfo = val.ShapeData;
                    foreach(var vertexReplacement in rawShapeInfo)
                    {
                        vertexReplacement.ShapeVertexId = vertexList.Count;
                        vertexList.Add(vertexReplacement.VertexData);
                    }
                } 

                
                finalVertices.Add(vertexList);


                var finalShapeParts = new List<(string ShapeName, int MeshId, Dictionary<int, int> IndexReplacements)>();

                // These are now reconstituted per-shape dictionaries of vertex replacements.
                // We now need to convert them into Triangle Index => Shape Vertex lists.
                // This list must be ordered in Shape Part order.
                foreach (var kv in perShapeDictionary)
                {
                    var shapeName = kv.Key;
                    var list = kv.Value.ShapeData;

                    // Index replacements of the format [Mesh Level Index => Mesh Level Vertex]
                    var replacements = new Dictionary<int, int>();
                    foreach (var data in list)
                    {
                        var meshReleventShapeVertexId = data.ShapeVertexId + (int)m.VertexCount;
                        var indexesUsedByVertex = vertexToIndexDictionary[data.MeshVertexId];
                        foreach(var meshRelevantIndex in indexesUsedByVertex)
                        {
                            //var modelRelevantIndex = 
                            replacements.Add(meshRelevantIndex, meshReleventShapeVertexId);
                        }
                    }
                    ret.Add((shapeName, meshId, replacements));
                }

                // Vertices are written BY GROUP as
                // [ Main Vertices ] [ Shape Vertices ] ... [[ Next Group ]]
                meshVertexOffset += (int) m.VertexCount + vertexList.Count;
                meshId++;
            }

            // Shape listing is sorted by Shape Name => MeshId
            ret.Sort((a, b) =>
            {
                var cmp = String.Compare(a.ShapeName, b.ShapeName);
                if (cmp != 0)
                {
                    return cmp;
                }
                return a.MeshId - b.MeshId;
            });

            return (ret, finalVertices);
        }


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
        /// Get the # of mesh groups by mesh type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public int GetMeshGroupCount(EMeshType type)
        {
            return MeshGroups.Count(x => x.MeshType == type);
        }

        /// <summary>
        /// Creates a bone set from the model and group information.
        /// </summary>
        /// <param name="PartNumber"></param>
        public List<byte> Getv6BoneSet(int groupNumber)
        {
            var fullList = Bones;
            var partial = MeshGroups[groupNumber].Bones;
            var used = new List<short>();

            var result = new List<byte>(new byte[(partial.Count * 2)]);

            // This is essential a translation table of [mesh group bone index] => [full model bone index]
            for (int i = 0; i < partial.Count; i++)
            {
                var idx = (short)fullList.IndexOf(partial[i]);
                used.Add(idx);
                var b = BitConverter.GetBytes(idx);
                IOUtil.ReplaceBytesAt(result, b, i * 2);
            }

            return result;
        }

        /// <summary>
        /// Creates a bone set from the model and group information.
        /// </summary>
        /// <param name="PartNumber"></param>
        public List<byte> GetBoneSet(int groupNumber)
        {
            var fullList = Bones;
            var partial = MeshGroups[groupNumber].Bones;

            var result = new List<byte>(new byte[partial.Count * 2]);

            // This is essential a translation table of [mesh group bone index] => [full model bone index]
            for (int i = 0; i < partial.Count; i++)
            {
                var b = BitConverter.GetBytes(((short) fullList.IndexOf(partial[i])));
                IOUtil.ReplaceBytesAt(result, b, i * 2);
            }

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

            
            short index = (short)Materials.IndexOf(m.Material);

            return index > 0 ? index : (short)0; 
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
        /// Re-Loads the XivMDL at the source path for this model, if it exists.
        /// </summary>
        /// <returns></returns>
        public async Task<XivMdl> GetRawMdl(Mdl _mdl, ModTransaction tx = null)
        {
            if (!IsInternal) return null;
            return await _mdl.GetXivMdl(Source, false, tx);
        }
        #endregion

        #region Major Public Functions

        /// <summary>
        /// Loads a TTModel file from a given SQLite3 DB filepath.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static TTModel LoadFromFile(string filePath, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = ModelModifiers.NoOp;
            }

            var connectionString = "Data Source=" + filePath + ";Pooling=False;";
            TTModel model = new TTModel();
            model.Source = filePath;

            // Spawn a DB connection to do the raw queries.
            using (var db = new SQLiteConnection(connectionString))
            {
                db.Open();
                // Using statements help ensure we don't accidentally leave any connections open and lock the file handle.

                // Load Mesh Groups
                var query = "select * from meshes order by mesh asc;";
                using (var cmd = new SQLiteCommand(query, db))
                {
                    using (var reader = new CacheReader(cmd.ExecuteReader()))
                    {
                        while (reader.NextRow())
                        {
                            var meshNum = reader.GetInt32("mesh");

                            // Spawn mesh groups as needed.
                            while (model.MeshGroups.Count <= meshNum)
                            {
                                model.MeshGroups.Add(new TTMeshGroup());
                            }
                            var t = reader.GetString("type");

                            if (string.IsNullOrWhiteSpace(t))
                            {
                                model.MeshGroups[meshNum].MeshType = EMeshType.Standard;
                            }
                            else
                            {
                                model.MeshGroups[meshNum].MeshType = (EMeshType) Enum.Parse(typeof(EMeshType), t);
                            }

                            model.MeshGroups[meshNum].Name = reader.GetString("name");
                        }
                    }
                }


                // Load Mesh Parts
                query = "select * from parts order by mesh asc, part asc;";
                using (var cmd = new SQLiteCommand(query, db))
                {
                    using (var reader = new CacheReader(cmd.ExecuteReader()))
                    {
                        while (reader.NextRow())
                        {
                            var meshNum = reader.GetInt32("mesh");
                            var partNum = reader.GetInt32("part");

                            // Spawn mesh groups if needed.
                            while (model.MeshGroups.Count <= meshNum)
                            {
                                model.MeshGroups.Add(new TTMeshGroup());
                            }

                            // Spawn parts as needed.
                            while(model.MeshGroups[meshNum].Parts.Count <= partNum)
                            {
                                model.MeshGroups[meshNum].Parts.Add(new TTMeshPart());

                            }
                            
                            var attribs = reader.GetString("attributes");
                            var attributes = new string[0];
                            if (!String.IsNullOrWhiteSpace(attribs))
                            {
                                attributes = attribs.Split(',');
                            }

                            // Load attributes
                            model.MeshGroups[meshNum].Parts[partNum].Attributes = new HashSet<string>(attributes);
                            model.MeshGroups[meshNum].Parts[partNum].Name = reader.GetString("name");
                        }
                    }
                }


                // Load Bones
                query = "select * from bones where mesh >= 0 order by mesh asc, bone_id asc;";
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

                        // Vertex Colors - Vertex color is RGBA
                        vertex.VertexColor2[0] = (byte)(Math.Round(reader.GetFloat("color2_r") * 255));
                        vertex.VertexColor2[1] = (byte)(Math.Round(reader.GetFloat("color2_g") * 255));
                        vertex.VertexColor2[2] = (byte)(Math.Round(reader.GetFloat("color2_b") * 255));
                        vertex.VertexColor2[3] = (byte)(Math.Round(reader.GetFloat("color2_a") * 255));

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
                        vertex.BoneIds[4] = (byte)(reader.GetByte("bone_5_id"));
                        vertex.BoneIds[5] = (byte)(reader.GetByte("bone_6_id"));
                        vertex.BoneIds[6] = (byte)(reader.GetByte("bone_7_id"));
                        vertex.BoneIds[7] = (byte)(reader.GetByte("bone_8_id"));

                        // Weights
                        vertex.Weights[0] = (byte)(Math.Round(reader.GetFloat("bone_1_weight") * 255));
                        vertex.Weights[1] = (byte)(Math.Round(reader.GetFloat("bone_2_weight") * 255));
                        vertex.Weights[2] = (byte)(Math.Round(reader.GetFloat("bone_3_weight") * 255));
                        vertex.Weights[3] = (byte)(Math.Round(reader.GetFloat("bone_4_weight") * 255));
                        vertex.Weights[4] = (byte)(Math.Round(reader.GetFloat("bone_5_weight") * 255));
                        vertex.Weights[5] = (byte)(Math.Round(reader.GetFloat("bone_6_weight") * 255));
                        vertex.Weights[6] = (byte)(Math.Round(reader.GetFloat("bone_7_weight") * 255));
                        vertex.Weights[7] = (byte)(Math.Round(reader.GetFloat("bone_8_weight") * 255));

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

            // Spawn a DB connection to do the raw queries.
            using (var db = new SQLiteConnection(connectionString))
            {
                db.Open();
                // Load Shape Verts
                var query = "select * from shape_vertices order by shape asc, mesh asc, part asc, vertex_id asc;";
                using (var cmd = new SQLiteCommand(query, db))
                {
                    using (var reader = new CacheReader(cmd.ExecuteReader()))
                    {
                        while (reader.NextRow())
                        {
                            var shapeName = reader.GetString("shape");
                            var meshNum = reader.GetInt32("mesh");
                            var partNum = reader.GetInt32("part");
                            var vertexId = reader.GetInt32("vertex_id");

                            var part = model.MeshGroups[meshNum].Parts[partNum];
                            // Copy the original vertex and update position.
                            TTVertex vertex = (TTVertex)part.Vertices[vertexId].Clone();
                            vertex.Position.X = reader.GetFloat("position_x");
                            vertex.Position.Y = reader.GetFloat("position_y");
                            vertex.Position.Z = reader.GetFloat("position_z");

                            var repVert = part.Vertices[vertexId];
                            if (repVert.Position.Equals(vertex.Position))
                            {
                                // Skip morphology which doesn't actually change anything.
                                continue;
                            }

                            if (!part.ShapeParts.ContainsKey(shapeName))
                            {
                                var shpPt = new TTShapePart();
                                shpPt.Name = shapeName;
                                part.ShapeParts.Add(shapeName, shpPt);
                            }


                            part.ShapeParts[shapeName].VertexReplacements.Add(vertexId, part.ShapeParts[shapeName].Vertices.Count);
                            part.ShapeParts[shapeName].Vertices.Add(vertex);

                        }
                    }
                }
            }



            // Convert the model to FFXIV's internal weirdness.
            ModelModifiers.MakeImportReady(model, loggingFunction);
            return model;
        }


        /// <summary>
        /// Saves the TTModel to a .DB file for use with external importers/exporters.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="loggingFunction"></param>
        public void SaveToFile(string filePath, string texturePath = null, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = ModelModifiers.NoOp;
            }
            File.Delete(filePath);
            var directory = Path.GetDirectoryName(filePath);
            var textureDirectory = directory;

            if (texturePath != null)
            {
                textureDirectory = Path.GetDirectoryName(texturePath);
            }

            ModelModifiers.MakeExportReady(this, loggingFunction);

            var connectionString = "Data Source=" + filePath + ";Pooling=False;";
            try
            {
                var useAllBones = XivCache.GetMetaValueBoolean(_SETTINGS_KEY_EXPORT_ALL_BONES);
                var bones = useAllBones ? null : Bones;

                var boneDict = ResolveBoneHeirarchy(null, XivRace.All_Races, bones, loggingFunction);

                const string creationScript = "CreateImportDB.sql";
                // Spawn a DB connection to do the raw queries.
                // Using statements help ensure we don't accidentally leave any connections open and lock the file handle.
                using (var db = new SQLiteConnection(connectionString))
                {
                    db.Open();

                    // Create the DB
                    var lines = File.ReadAllLines("Resources\\SQL\\" + creationScript);
                    var sqlCmd = String.Join("\n", lines);

                    using (var cmd = new SQLiteCommand(sqlCmd, db))
                    {
                        cmd.ExecuteScalar();
                    }

                    // Write the Data.
                    using (var transaction = db.BeginTransaction())
                    {

                        // Metadata.
                        var query = @"insert into meta (key, value) values ($key, $value)";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            // FFXIV stores stuff in Meters.
                            cmd.Parameters.AddWithValue("key", "unit");
                            cmd.Parameters.AddWithValue("value", "meter");
                            cmd.ExecuteScalar();

                            // Application that created the db.
                            cmd.Parameters.AddWithValue("key", "application");
                            cmd.Parameters.AddWithValue("value", "ffxiv_tt");
                            cmd.ExecuteScalar();


                            cmd.Parameters.AddWithValue("key", "version");
                            cmd.Parameters.AddWithValue("value", typeof(XivCache).Assembly.GetName().Version);
                            cmd.ExecuteScalar();

                            // Axis information
                            cmd.Parameters.AddWithValue("key", "up");
                            cmd.Parameters.AddWithValue("value", "y");
                            cmd.ExecuteScalar();

                            cmd.Parameters.AddWithValue("key", "front");
                            cmd.Parameters.AddWithValue("value", "z");
                            cmd.ExecuteScalar();

                            cmd.Parameters.AddWithValue("key", "handedness");
                            cmd.Parameters.AddWithValue("value", "r");
                            cmd.ExecuteScalar();

                            var for3ds = ModelTexture.GetCustomColors().InvertNormalGreen;
                            cmd.Parameters.AddWithValue("key", "for_3ds_max");
                            cmd.Parameters.AddWithValue("value", for3ds ? "1" : "0");
                            cmd.ExecuteScalar();


                            // FFXIV stores stuff in Meters.
                            cmd.Parameters.AddWithValue("key", "root_name");
                            cmd.Parameters.AddWithValue("value", Path.GetFileNameWithoutExtension(Source));
                            cmd.ExecuteScalar();

                        }

                        // Skeleton
                        query = @"insert into skeleton (name, parent, matrix_0, matrix_1, matrix_2, matrix_3, matrix_4, matrix_5, matrix_6, matrix_7, matrix_8, matrix_9, matrix_10, matrix_11, matrix_12, matrix_13, matrix_14, matrix_15) 
                                             values ($name, $parent, $matrix_0, $matrix_1, $matrix_2, $matrix_3, $matrix_4, $matrix_5, $matrix_6, $matrix_7, $matrix_8, $matrix_9, $matrix_10, $matrix_11, $matrix_12, $matrix_13, $matrix_14, $matrix_15);";

                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            foreach (var b in boneDict)
                            {
                                var parent = boneDict.FirstOrDefault(x => x.Value.BoneNumber == b.Value.BoneParent);
                                var parentName = parent.Key == null ? null : parent.Key;
                                cmd.Parameters.AddWithValue("name", b.Value.BoneName);
                                cmd.Parameters.AddWithValue("parent", parentName);

                                for (int i = 0; i < 16; i++)
                                {
                                    cmd.Parameters.AddWithValue("matrix_" + i.ToString(), b.Value.PoseMatrix[i]);
                                }

                                cmd.ExecuteScalar();
                            }
                        }

                        var modelIdx = 0;
                        var models = new List<string>() { Path.GetFileNameWithoutExtension(Source) };
                        foreach (var model in models)
                        {
                            query = @"insert into models (model, name) values ($model, $name);";
                            using (var cmd = new SQLiteCommand(query, db))
                            {
                                cmd.Parameters.AddWithValue("model", modelIdx);
                                cmd.Parameters.AddWithValue("name", model);
                                cmd.ExecuteScalar();

                            }
                            modelIdx++;
                        }

                        var matIdx = 0;
                        foreach(var material in Materials)
                        {
                            // Materials
                            query = @"insert into materials (material_id, name, diffuse, normal, specular, opacity, emissive) values ($material_id, $name, $diffuse, $normal, $specular, $opacity, $emissive);";
                            using (var cmd = new SQLiteCommand(query, db))
                            {
                                var mtrl_prefix = textureDirectory + "\\" + Path.GetFileNameWithoutExtension(material.Substring(1)) + "_";
                                var mtrl_suffix = ".png";
                                var name = material;
                                try
                                {
                                    name = Path.GetFileNameWithoutExtension(material);
                                } catch
                                {

                                }
                                cmd.Parameters.AddWithValue("material_id", matIdx);
                                cmd.Parameters.AddWithValue("name", name);
                                cmd.Parameters.AddWithValue("diffuse", mtrl_prefix + "d" + mtrl_suffix);
                                cmd.Parameters.AddWithValue("normal", mtrl_prefix + "n" + mtrl_suffix);
                                cmd.Parameters.AddWithValue("specular", mtrl_prefix + "s" + mtrl_suffix);
                                cmd.Parameters.AddWithValue("emissive", mtrl_prefix + "e" + mtrl_suffix);
                                cmd.Parameters.AddWithValue("opacity", mtrl_prefix + "o" + mtrl_suffix);
                                cmd.ExecuteScalar();
                            }
                            matIdx++;
                        }

                        var meshIdx = 0;
                        foreach (var m in MeshGroups)
                        {
                            // Bones
                            query = @"insert into bones (mesh, bone_id, name) values ($mesh, $bone_id, $name);";
                            var bIdx = 0;
                            foreach (var b in m.Bones)
                            {
                                using (var cmd = new SQLiteCommand(query, db))
                                {
                                    cmd.Parameters.AddWithValue("name", b);
                                    cmd.Parameters.AddWithValue("bone_id", bIdx);
                                    cmd.Parameters.AddWithValue("parent_id", null);
                                    cmd.Parameters.AddWithValue("mesh", meshIdx);
                                    cmd.ExecuteScalar();
                                }
                                bIdx++;
                            }


                            // Groups
                            query = @"insert into meshes (mesh, name, material_id, model, type) values ($mesh, $name, $material_id, $model, $type);";
                            using (var cmd = new SQLiteCommand(query, db))
                            {
                                cmd.Parameters.AddWithValue("name", m.Name);
                                cmd.Parameters.AddWithValue("mesh", meshIdx);

                                // This is always 0 for now.  Support element for Liinko's work on multi-model export.
                                cmd.Parameters.AddWithValue("model", 0);
                                cmd.Parameters.AddWithValue("material_id", GetMaterialIndex(meshIdx));
                                cmd.Parameters.AddWithValue("type", (int) m.MeshType);
                                cmd.ExecuteScalar();
                            }


                            // Parts
                            var partIdx = 0;
                            foreach (var p in m.Parts)
                            {
                                // Parts
                                query = @"insert into parts (mesh, part, name, attributes) values ($mesh, $part, $name, $attributes);";
                                using (var cmd = new SQLiteCommand(query, db))
                                {
                                    cmd.Parameters.AddWithValue("name", p.Name);
                                    cmd.Parameters.AddWithValue("part", partIdx);
                                    cmd.Parameters.AddWithValue("mesh", meshIdx);
                                    cmd.Parameters.AddWithValue("attributes", String.Join(",", p.Attributes));
                                    cmd.ExecuteScalar();
                                }

                                // Vertices
                                var vIdx = 0;
                                foreach (var v in p.Vertices)
                                {
                                    query = @"insert into vertices ( mesh,  part,  vertex_id,  position_x,  position_y,  position_z,  normal_x,  normal_y,  normal_z,  color_r,  color_g,  color_b,  color_a,  color2_r,  color2_g,  color2_b,  color2_a,  uv_1_u,  uv_1_v,  uv_2_u,  uv_2_v,  bone_1_id,  bone_1_weight,  bone_2_id,  bone_2_weight,  bone_3_id,  bone_3_weight,  bone_4_id,  bone_4_weight,  bone_5_id,  bone_5_weight,  bone_6_id,  bone_6_weight,  bone_7_id,  bone_7_weight,  bone_8_id,  bone_8_weight) 
                                                        values    ( $mesh, $part, $vertex_id, $position_x, $position_y, $position_z, $normal_x, $normal_y, $normal_z, $color_r, $color_g, $color_b, $color_a, $color2_r, $color2_g, $color2_b, $color2_a, $uv_1_u, $uv_1_v, $uv_2_u, $uv_2_v, $bone_1_id, $bone_1_weight, $bone_2_id, $bone_2_weight, $bone_3_id, $bone_3_weight, $bone_4_id, $bone_4_weight, $bone_5_id, $bone_5_weight, $bone_6_id, $bone_6_weight, $bone_7_id, $bone_7_weight, $bone_8_id, $bone_8_weight);";
                                    using (var cmd = new SQLiteCommand(query, db))
                                    {
                                        cmd.Parameters.AddWithValue("part", partIdx);
                                        cmd.Parameters.AddWithValue("mesh", meshIdx);
                                        cmd.Parameters.AddWithValue("vertex_id", vIdx);

                                        cmd.Parameters.AddWithValue("position_x", v.Position.X);
                                        cmd.Parameters.AddWithValue("position_y", v.Position.Y);
                                        cmd.Parameters.AddWithValue("position_z", v.Position.Z);

                                        cmd.Parameters.AddWithValue("normal_x", v.Normal.X);
                                        cmd.Parameters.AddWithValue("normal_y", v.Normal.Y);
                                        cmd.Parameters.AddWithValue("normal_z", v.Normal.Z);

                                        cmd.Parameters.AddWithValue("color_r", v.VertexColor[0] / 255f);
                                        cmd.Parameters.AddWithValue("color_g", v.VertexColor[1] / 255f);
                                        cmd.Parameters.AddWithValue("color_b", v.VertexColor[2] / 255f);
                                        cmd.Parameters.AddWithValue("color_a", v.VertexColor[3] / 255f);

                                        cmd.Parameters.AddWithValue("color2_r", v.VertexColor2[0] / 255f);
                                        cmd.Parameters.AddWithValue("color2_g", v.VertexColor2[1] / 255f);
                                        cmd.Parameters.AddWithValue("color2_b", v.VertexColor2[2] / 255f);
                                        cmd.Parameters.AddWithValue("color2_a", v.VertexColor2[3] / 255f);

                                        cmd.Parameters.AddWithValue("uv_1_u", v.UV1.X);
                                        cmd.Parameters.AddWithValue("uv_1_v", v.UV1.Y);
                                        cmd.Parameters.AddWithValue("uv_2_u", v.UV2.X);
                                        cmd.Parameters.AddWithValue("uv_2_v", v.UV2.Y);


                                        cmd.Parameters.AddWithValue("bone_1_id", v.BoneIds[0]);
                                        cmd.Parameters.AddWithValue("bone_1_weight", v.Weights[0] / 255f);

                                        cmd.Parameters.AddWithValue("bone_2_id", v.BoneIds[1]);
                                        cmd.Parameters.AddWithValue("bone_2_weight", v.Weights[1] / 255f);

                                        cmd.Parameters.AddWithValue("bone_3_id", v.BoneIds[2]);
                                        cmd.Parameters.AddWithValue("bone_3_weight", v.Weights[2] / 255f);

                                        cmd.Parameters.AddWithValue("bone_4_id", v.BoneIds[3]);
                                        cmd.Parameters.AddWithValue("bone_4_weight", v.Weights[3] / 255f);

                                        cmd.Parameters.AddWithValue("bone_5_id", v.BoneIds[4]);
                                        cmd.Parameters.AddWithValue("bone_5_weight", v.Weights[4] / 255f);

                                        cmd.Parameters.AddWithValue("bone_6_id", v.BoneIds[5]);
                                        cmd.Parameters.AddWithValue("bone_6_weight", v.Weights[5] / 255f);

                                        cmd.Parameters.AddWithValue("bone_7_id", v.BoneIds[6]);
                                        cmd.Parameters.AddWithValue("bone_7_weight", v.Weights[6] / 255f);

                                        cmd.Parameters.AddWithValue("bone_8_id", v.BoneIds[7]);
                                        cmd.Parameters.AddWithValue("bone_8_weight", v.Weights[7] / 255f);



                                        cmd.ExecuteScalar();
                                        vIdx++;
                                    }
                                }

                                // Indices
                                for (var i = 0; i < p.TriangleIndices.Count; i++)
                                {
                                    query = @"insert into indices (mesh, part, index_id, vertex_id) values ($mesh, $part, $index_id, $vertex_id);";
                                    using (var cmd = new SQLiteCommand(query, db))
                                    {
                                        cmd.Parameters.AddWithValue("part", partIdx);
                                        cmd.Parameters.AddWithValue("mesh", meshIdx);
                                        cmd.Parameters.AddWithValue("index_id", i);
                                        cmd.Parameters.AddWithValue("vertex_id", p.TriangleIndices[i]);
                                        cmd.ExecuteScalar();
                                    }
                                }



                                // Shape Parts
                                foreach(var shpKv in p.ShapeParts)
                                {
                                    if (!shpKv.Key.StartsWith("shp_")) continue;
                                    var shp = shpKv.Value;

                                    query = @"insert into shape_vertices ( mesh,  part,  shape,  vertex_id,  position_x,  position_y,  position_z) 
                                                                   values($mesh, $part, $shape, $vertex_id, $position_x, $position_y, $position_z);";
                                    using (var cmd = new SQLiteCommand(query, db))
                                    {
                                        foreach (var vKv in shp.VertexReplacements)
                                        {
                                        var v = shp.Vertices[vKv.Value];
                                            cmd.Parameters.AddWithValue("part", partIdx);
                                            cmd.Parameters.AddWithValue("mesh", meshIdx);
                                            cmd.Parameters.AddWithValue("shape", shpKv.Key);
                                            cmd.Parameters.AddWithValue("vertex_id", vKv.Key);

                                            cmd.Parameters.AddWithValue("position_x", v.Position.X);
                                            cmd.Parameters.AddWithValue("position_y", v.Position.Y);
                                            cmd.Parameters.AddWithValue("position_z", v.Position.Z);


                                            cmd.ExecuteScalar();
                                            vIdx++;
                                        }
                                    }
                                }

                                partIdx++;
                            }



                            meshIdx++;
                        }
                        transaction.Commit();
                    }
                }
            } catch(Exception Ex)
            {
                ModelModifiers.MakeImportReady(this, loggingFunction);
                throw;
            }

            // Undo the export ready at the start.
            ModelModifiers.MakeImportReady(this, loggingFunction);
        }

        public static Dictionary<string, SkeletonData> ResolveFullBoneHeirarchy(XivRace race, List<string> models, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = ModelModifiers.NoOp;
            }


            // First thing we need to do here is scrap through the groups to
            // pull back out the extra skeletons of the constituent models.
            var _metRegex = new Regex("e([0-9]{4})_met");
            var _topRegex = new Regex("e([0-9]{4})_top");
            var _faceRegex = new Regex("f([0-9]{4})");
            var _hairRegex = new Regex("h([0-9]{4})");

            int topNum = -1;
            int metNum = -1;
            int faceNum = -1;
            int hairNum = -1;

            foreach (var m in models)
            {
                var metMatch = _metRegex.Match(m);
                var topMatch = _topRegex.Match(m);
                var faceMatch = _faceRegex.Match(m);
                var hairMatch = _hairRegex.Match(m);

                if (metMatch.Success)
                {
                    metNum = Int32.Parse(metMatch.Groups[1].Value);
                }
                else if (topMatch.Success)
                {
                    topNum = Int32.Parse(topMatch.Groups[1].Value);

                }
                else if (faceMatch.Success)
                {
                    faceNum = Int32.Parse(faceMatch.Groups[1].Value);
                }
                else if (hairMatch.Success)
                {
                    hairNum = Int32.Parse(hairMatch.Groups[1].Value);
                }
            }

            // This is a list of the roots we'll need to pull extra skeleton data for.
            List<XivDependencyRootInfo> rootsToResolve = new List<XivDependencyRootInfo>();

            if (metNum >= 0)
            {
                var root = new XivDependencyRootInfo();
                root.PrimaryType = XivItemType.equipment;
                root.PrimaryId = metNum;
                root.Slot = "met";

                rootsToResolve.Add(root);
            }
            if (topNum >= 0)
            {
                var root = new XivDependencyRootInfo();
                root.PrimaryType = XivItemType.equipment;
                root.PrimaryId = topNum;
                root.Slot = "top";
                rootsToResolve.Add(root);
            }
            if (faceNum >= 0)
            {
                var root = new XivDependencyRootInfo();
                root.PrimaryType = XivItemType.human;
                root.PrimaryId = XivRaces.GetRaceCodeInt(race);
                root.SecondaryType = XivItemType.face;
                root.SecondaryId = faceNum;
                root.Slot = "fac";
                rootsToResolve.Add(root);
            }
            if (hairNum >= 0)
            {
                var root = new XivDependencyRootInfo();
                root.PrimaryType = XivItemType.human;
                root.PrimaryId = XivRaces.GetRaceCodeInt(race);
                root.SecondaryType = XivItemType.hair;
                root.SecondaryId = hairNum;
                root.Slot = "hir";
                rootsToResolve.Add(root);
            }

            // No extra skeletons using slots were used, just add the base root so we get the race's standard skeleton at least.
            if (rootsToResolve.Count == 0)
            {
                var root = new XivDependencyRootInfo();
                root.PrimaryType = XivItemType.equipment;
                root.PrimaryId = 0;
                root.Slot = "top";
                rootsToResolve.Add(root);
            }

            var boneDict = TTModel.ResolveBoneHeirarchyRaw(rootsToResolve, race, null, loggingFunction);
            return boneDict;
        }

        /// <summary>
        /// Saves a TTModel to a .DB file for use with external importers/exporters.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="loggingFunction"></param>
        public static void SaveFullToFile(string filePath, XivRace race, List<TTModel> models, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = ModelModifiers.NoOp;
            }

            var directory = Path.GetDirectoryName(filePath);

            var paths = new List<string>();
            foreach(var m in models)
            {
                paths.Add(m.Source);
            }

            var boneDict = ResolveFullBoneHeirarchy(race, paths, loggingFunction);



            var connectionString = "Data Source=" + filePath + ";Pooling=False;";
            foreach (var model in models)
            {
                try
                {
                    ModelModifiers.MakeExportReady(model, loggingFunction);


                    // Spawn a DB connection to do the raw queries.
                    // Using statements help ensure we don't accidentally leave any connections open and lock the file handle.
                    using (var db = new SQLiteConnection(connectionString))
                    {
                        db.Open();

                        // Write the Data.
                        using (var transaction = db.BeginTransaction())
                        {
                            // Get the model Ids already in the DB
                            var modelList = new List<int>();
                            var getModelQuery = "SELECT model FROM models";
                            using (var cmd = new SQLiteCommand(getModelQuery, db))
                            {
                                var sqReader = cmd.ExecuteReader();

                                while (sqReader.Read())
                                {
                                    modelList.Add(sqReader.GetInt32(0));
                                }
                            }

                            var modelIdx = modelList.Any() ? modelList.Max() + 1 : 0;
                            var modelName = Path.GetFileNameWithoutExtension(model.Source);

                            //Models
                            var query = @"insert into models (model, name) values ($model, $name);";
                            using (var cmd = new SQLiteCommand(query, db))
                            {
                                cmd.Parameters.AddWithValue("model", modelIdx);
                                cmd.Parameters.AddWithValue("name", modelName);

                                cmd.ExecuteScalar();
                            }

                            // Get the skeleton names already in the DB
                            var skelList = new List<string>();
                            var getSkelQuery = "SELECT name FROM skeleton";
                            using (var cmd = new SQLiteCommand(getSkelQuery, db))
                            {
                                var sqReader = cmd.ExecuteReader();

                                while (sqReader.Read())
                                {
                                    skelList.Add(sqReader.GetString(0));
                                }
                            }

                            // Skeleton
                            query = @"insert into skeleton (name, parent, matrix_0, matrix_1, matrix_2, matrix_3, matrix_4, matrix_5, matrix_6, matrix_7, matrix_8, matrix_9, matrix_10, matrix_11, matrix_12, matrix_13, matrix_14, matrix_15) 
                                             values ($name, $parent, $matrix_0, $matrix_1, $matrix_2, $matrix_3, $matrix_4, $matrix_5, $matrix_6, $matrix_7, $matrix_8, $matrix_9, $matrix_10, $matrix_11, $matrix_12, $matrix_13, $matrix_14, $matrix_15);";

                            using (var cmd = new SQLiteCommand(query, db))
                            {
                                foreach (var b in boneDict)
                                {
                                    // Skip the bone if it's already in the DB
                                    if (skelList.Contains(b.Value.BoneName)) continue;

                                    var parent = boneDict.FirstOrDefault(x => x.Value.BoneNumber == b.Value.BoneParent);
                                    var parentName = parent.Key == null ? null : parent.Key;
                                    cmd.Parameters.AddWithValue("name", b.Value.BoneName);
                                    cmd.Parameters.AddWithValue("parent", parentName);

                                    for (int i = 0; i < 16; i++)
                                    {
                                        cmd.Parameters.AddWithValue("matrix_" + i.ToString(), b.Value.PoseMatrix[i]);
                                    }

                                    cmd.ExecuteScalar();
                                }
                            }

                            // Get the material ids already in the DB
                            var matIdList = new List<int>();
                            var getMatIdQuery = "SELECT material_id FROM materials";
                            using (var cmd = new SQLiteCommand(getMatIdQuery, db))
                            {
                                var sqReader = cmd.ExecuteReader();

                                while (sqReader.Read())
                                {
                                    matIdList.Add(sqReader.GetInt32(0));
                                }
                            }

                            // Start from the last material ID in the DB
                            var matIdx = matIdList.Any() ? matIdList.Max() + 1 : 0;
                            var tempMatDict = new Dictionary<string, int>();
                            foreach (var material in model.Materials)
                            {
                                // Materials
                                query = @"insert into materials (material_id, name, diffuse, normal, specular, opacity, emissive) values ($material_id, $name, $diffuse, $normal, $specular, $opacity, $emissive);";
                                using (var cmd = new SQLiteCommand(query, db))
                                {
                                    var mtrl_prefix = directory + "\\" + Path.GetFileNameWithoutExtension(material.Substring(1)) + "_";
                                    var mtrl_suffix = ".png";
                                    var name = material;
                                    try
                                    {
                                        name = Path.GetFileNameWithoutExtension(material);
                                    }
                                    catch
                                    {

                                    }
                                    cmd.Parameters.AddWithValue("material_id", matIdx);
                                    cmd.Parameters.AddWithValue("name", name);
                                    cmd.Parameters.AddWithValue("diffuse", mtrl_prefix + "d" + mtrl_suffix);
                                    cmd.Parameters.AddWithValue("normal", mtrl_prefix + "n" + mtrl_suffix);
                                    cmd.Parameters.AddWithValue("specular", mtrl_prefix + "s" + mtrl_suffix);
                                    cmd.Parameters.AddWithValue("emissive", mtrl_prefix + "e" + mtrl_suffix);
                                    cmd.Parameters.AddWithValue("opacity", mtrl_prefix + "o" + mtrl_suffix);
                                    cmd.ExecuteScalar();
                                }
                                tempMatDict.Add(Path.GetFileNameWithoutExtension(material), matIdx);
                                matIdx++;
                            }

                            // Get the mesh ids already in the DB for Bones
                            var meshIdList = new List<int>();
                            var getMeshIdQuery = "SELECT mesh FROM bones";
                            using (var cmd = new SQLiteCommand(getMeshIdQuery, db))
                            {
                                var sqReader = cmd.ExecuteReader();

                                while (sqReader.Read())
                                {
                                    meshIdList.Add(sqReader.GetInt32(0));
                                }
                            }

                            // Start from the last mesh ID in the DB
                            var meshIdx = meshIdList.Any() ? meshIdList.Max() + 1 : 0;
                            foreach (var m in model.MeshGroups)
                            {
                                // Bones
                                query = @"insert into bones (mesh, bone_id, name) values ($mesh, $bone_id, $name);";
                                var bIdx = 0;
                                foreach (var b in m.Bones)
                                {
                                    using (var cmd = new SQLiteCommand(query, db))
                                    {
                                        cmd.Parameters.AddWithValue("name", b);
                                        cmd.Parameters.AddWithValue("bone_id", bIdx);
                                        cmd.Parameters.AddWithValue("parent_id", null);
                                        cmd.Parameters.AddWithValue("mesh", meshIdx);
                                        cmd.ExecuteScalar();
                                    }
                                    bIdx++;
                                }

                                // Meshes
                                query = @"insert into meshes (mesh, model, name, material_id) values ($mesh, $model, $name, $material_id);";
                                using (var cmd = new SQLiteCommand(query, db))
                                {
                                    cmd.Parameters.AddWithValue("name", m.Name);
                                    cmd.Parameters.AddWithValue("model", modelIdx);
                                    cmd.Parameters.AddWithValue("mesh", meshIdx);
                                    cmd.Parameters.AddWithValue("material_id", tempMatDict[Path.GetFileNameWithoutExtension(m.Material)]);
                                    cmd.Parameters.AddWithValue("type", (int)m.MeshType);
                                    cmd.ExecuteScalar();
                                }


                                // Parts
                                var partIdx = 0;
                                foreach (var p in m.Parts)
                                {
                                    // Parts
                                    query = @"insert into parts (mesh, part, name) values ($mesh, $part, $name);";
                                    using (var cmd = new SQLiteCommand(query, db))
                                    {
                                        cmd.Parameters.AddWithValue("name", p.Name);
                                        cmd.Parameters.AddWithValue("part", partIdx);
                                        cmd.Parameters.AddWithValue("mesh", meshIdx);
                                        cmd.Parameters.AddWithValue("attributes", String.Join(",", p.Attributes));
                                        cmd.ExecuteScalar();
                                    }

                                    // Vertices
                                    var vIdx = 0;
                                    foreach (var v in p.Vertices)
                                    {
                                        query = @"insert into vertices ( mesh,  part,  vertex_id,  position_x,  position_y,  position_z,  normal_x,  normal_y,  normal_z,  color_r,  color_g,  color_b,   color_a,  color2_r,  color2_g,  color2_b,  color2_a,  uv_1_u,  uv_1_v,  uv_2_u,  uv_2_v,  bone_1_id,  bone_1_weight,  bone_2_id,  bone_2_weight,  bone_3_id,  bone_3_weight,  bone_4_id,  bone_4_weight,  bone_5_id,  bone_5_weight,  bone_6_id,  bone_6_weight,  bone_7_id,  bone_7_weight,  bone_8_id,  bone_8_weight) 
                                                        values         ($mesh, $part, $vertex_id, $position_x, $position_y, $position_z, $normal_x, $normal_y, $normal_z, $color_r, $color_g, $color_b, $color2_a, $color2_r, $color2_g, $color2_b, $color2_a, $uv_1_u, $uv_1_v, $uv_2_u, $uv_2_v, $bone_1_id, $bone_1_weight, $bone_2_id, $bone_2_weight, $bone_3_id, $bone_3_weight, $bone_4_id, $bone_4_weight, $bone_5_id, $bone_5_weight, $bone_6_id, $bone_6_weight, $bone_7_id, $bone_7_weight, $bone_8_id, $bone_8_weight);";
                                        using (var cmd = new SQLiteCommand(query, db))
                                        {
                                            cmd.Parameters.AddWithValue("part", partIdx);
                                            cmd.Parameters.AddWithValue("mesh", meshIdx);
                                            cmd.Parameters.AddWithValue("vertex_id", vIdx);

                                            cmd.Parameters.AddWithValue("position_x", v.Position.X);
                                            cmd.Parameters.AddWithValue("position_y", v.Position.Y);
                                            cmd.Parameters.AddWithValue("position_z", v.Position.Z);

                                            cmd.Parameters.AddWithValue("normal_x", v.Normal.X);
                                            cmd.Parameters.AddWithValue("normal_y", v.Normal.Y);
                                            cmd.Parameters.AddWithValue("normal_z", v.Normal.Z);

                                            cmd.Parameters.AddWithValue("color_r", v.VertexColor[0] / 255f);
                                            cmd.Parameters.AddWithValue("color_g", v.VertexColor[1] / 255f);
                                            cmd.Parameters.AddWithValue("color_b", v.VertexColor[2] / 255f);
                                            cmd.Parameters.AddWithValue("color_a", v.VertexColor[3] / 255f);

                                            cmd.Parameters.AddWithValue("color2_r", v.VertexColor2[0] / 255f);
                                            cmd.Parameters.AddWithValue("color2_g", v.VertexColor2[1] / 255f);
                                            cmd.Parameters.AddWithValue("color2_b", v.VertexColor2[2] / 255f);
                                            cmd.Parameters.AddWithValue("color2_a", v.VertexColor2[3] / 255f);

                                            cmd.Parameters.AddWithValue("uv_1_u", v.UV1.X);
                                            cmd.Parameters.AddWithValue("uv_1_v", v.UV1.Y);
                                            cmd.Parameters.AddWithValue("uv_2_u", v.UV2.X);
                                            cmd.Parameters.AddWithValue("uv_2_v", v.UV2.Y);


                                            cmd.Parameters.AddWithValue("bone_1_id", v.BoneIds[0]);
                                            cmd.Parameters.AddWithValue("bone_1_weight", v.Weights[0] / 255f);

                                            cmd.Parameters.AddWithValue("bone_2_id", v.BoneIds[1]);
                                            cmd.Parameters.AddWithValue("bone_2_weight", v.Weights[1] / 255f);

                                            cmd.Parameters.AddWithValue("bone_3_id", v.BoneIds[2]);
                                            cmd.Parameters.AddWithValue("bone_3_weight", v.Weights[2] / 255f);

                                            cmd.Parameters.AddWithValue("bone_4_id", v.BoneIds[3]);
                                            cmd.Parameters.AddWithValue("bone_4_weight", v.Weights[3] / 255f);

                                            cmd.Parameters.AddWithValue("bone_5_id", v.BoneIds[4]);
                                            cmd.Parameters.AddWithValue("bone_5_weight", v.Weights[4] / 255f);

                                            cmd.Parameters.AddWithValue("bone_6_id", v.BoneIds[5]);
                                            cmd.Parameters.AddWithValue("bone_6_weight", v.Weights[5] / 255f);

                                            cmd.Parameters.AddWithValue("bone_7_id", v.BoneIds[6]);
                                            cmd.Parameters.AddWithValue("bone_7_weight", v.Weights[6] / 255f);

                                            cmd.Parameters.AddWithValue("bone_8_id", v.BoneIds[7]);
                                            cmd.Parameters.AddWithValue("bone_8_weight", v.Weights[7] / 255f);

                                            cmd.ExecuteScalar();
                                            vIdx++;
                                        }
                                    }

                                    // Indices
                                    for (var i = 0; i < p.TriangleIndices.Count; i++)
                                    {
                                        query = @"insert into indices (mesh, part, index_id, vertex_id) values ($mesh, $part, $index_id, $vertex_id);";
                                        using (var cmd = new SQLiteCommand(query, db))
                                        {
                                            cmd.Parameters.AddWithValue("part", partIdx);
                                            cmd.Parameters.AddWithValue("mesh", meshIdx);
                                            cmd.Parameters.AddWithValue("index_id", i);
                                            cmd.Parameters.AddWithValue("vertex_id", p.TriangleIndices[i]);
                                            cmd.ExecuteScalar();
                                        }
                                    }

                                    // Shape Parts
                                    foreach (var shpKv in p.ShapeParts)
                                    {
                                        if (!shpKv.Key.StartsWith("shp_")) continue;
                                        var shp = shpKv.Value;

                                        query = @"insert into shape_vertices ( mesh,  part,  shape,  vertex_id,  position_x,  position_y,  position_z) 
                                                                   values($mesh, $part, $shape, $vertex_id, $position_x, $position_y, $position_z);";
                                        using (var cmd = new SQLiteCommand(query, db))
                                        {
                                            foreach (var vKv in shp.VertexReplacements)
                                            {
                                                var v = shp.Vertices[vKv.Value];
                                                cmd.Parameters.AddWithValue("part", partIdx);
                                                cmd.Parameters.AddWithValue("mesh", meshIdx);
                                                cmd.Parameters.AddWithValue("shape", shpKv.Key);
                                                cmd.Parameters.AddWithValue("vertex_id", vKv.Key);

                                                cmd.Parameters.AddWithValue("position_x", v.Position.X);
                                                cmd.Parameters.AddWithValue("position_y", v.Position.Y);
                                                cmd.Parameters.AddWithValue("position_z", v.Position.Z);


                                                cmd.ExecuteScalar();
                                                vIdx++;
                                            }
                                        }
                                    }

                                    partIdx++;
                                }

                                meshIdx++;


                            }
                            transaction.Commit();
                        }
                    }
                }
                catch (Exception Ex)
                {
                    ModelModifiers.MakeImportReady(model, loggingFunction);
                    throw Ex;
                }
                ModelModifiers.MakeImportReady(model, loggingFunction);
            }
        }


        /// <summary>
        /// Create the DB and set the Meta Data for the full model
        /// </summary>
        /// <param name="filePath">The DB file path</param>
        /// <param name="fullModelName">The name of the full model</param>
        public void SetFullModelDBMetaData(string filePath, string fullModelName)
        {
            var connectionString = "Data Source=" + filePath + ";Pooling=False;";

            try
            {
                const string creationScript = "CreateImportDB.sql";

                using (var db = new SQLiteConnection(connectionString))
                {
                    db.Open();

                    // Create the DB
                    var lines = File.ReadAllLines("Resources\\SQL\\" + creationScript);
                    var sqlCmd = String.Join("\n", lines);

                    using (var cmd = new SQLiteCommand(sqlCmd, db))
                    {
                        cmd.ExecuteScalar();
                    }

                    using (var transaction = db.BeginTransaction())
                    {
                        // Metadata.
                        var query = @"insert into meta (key, value) values ($key, $value)";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            // FFXIV stores stuff in Meters.
                            cmd.Parameters.AddWithValue("key", "unit");
                            cmd.Parameters.AddWithValue("value", "meter");
                            cmd.ExecuteScalar();

                            // Application that created the db.
                            cmd.Parameters.AddWithValue("key", "application");
                            cmd.Parameters.AddWithValue("value", "ffxiv_tt");
                            cmd.ExecuteScalar();

                            // Does the framework NOT have a version identifier?  I couldn't find one, so the Cache version works.
                            cmd.Parameters.AddWithValue("key", "version");
                            cmd.Parameters.AddWithValue("value", XivCache.CacheVersion);
                            cmd.ExecuteScalar();

                            // Axis information
                            cmd.Parameters.AddWithValue("key", "up");
                            cmd.Parameters.AddWithValue("value", "y");
                            cmd.ExecuteScalar();

                            cmd.Parameters.AddWithValue("key", "front");
                            cmd.Parameters.AddWithValue("value", "z");
                            cmd.ExecuteScalar();

                            cmd.Parameters.AddWithValue("key", "handedness");
                            cmd.Parameters.AddWithValue("value", "r");
                            cmd.ExecuteScalar();

                            // FFXIV stores stuff in Meters.
                            cmd.Parameters.AddWithValue("key", "name");
                            cmd.Parameters.AddWithValue("value", fullModelName);
                            cmd.ExecuteScalar();
                        }
                        transaction.Commit();
                    }
                }
            }
            catch (Exception ex)
            {

            }
        }

        /// <summary>
        /// Creates and populates a TTModel object from a raw XivMdl
        /// </summary>
        /// <param name="rawMdl"></param>
        /// <returns></returns>
        public static TTModel FromRaw(XivMdl rawMdl, Action<bool, string> loggingFunction = null)
        {
            if(rawMdl == null)
            {
                return null;
            }

            if (loggingFunction == null)
            {
                loggingFunction = ModelModifiers.NoOp;
            }

            var ttModel = new TTModel();
            ModelModifiers.MergeGeometryData(ttModel, rawMdl, loggingFunction);
            ModelModifiers.MergeAttributeData(ttModel, rawMdl, loggingFunction);
            ModelModifiers.MergeMaterialData(ttModel, rawMdl, loggingFunction);
            try
            {
                ModelModifiers.MergeShapeData(ttModel, rawMdl, loggingFunction);
            } catch(Exception ex)
            {
                loggingFunction(true, "Unable to load shape data: " + ex.Message);
                ModelModifiers.ClearShapeData(ttModel, loggingFunction);
            }
            ttModel.Source = rawMdl.MdlPath;
            ttModel.MdlVersion = rawMdl.MdlVersion;

            return ttModel;
        }


        /// <summary>
        /// Updates all shapes in this model to any updated UV/Normal/etc. data from the base model.
        /// </summary>
        public void UpdateShapeData()
        {
            foreach(var m in MeshGroups)
            {
                m.UpdateShapeData();
            }
        }

        #endregion

        #region  Internal Helper Functions

        private static float[] NewIdentityMatrix()
        {
            var arr = new float [16];
            arr[0] = 1f;
            arr[1] = 0f;
            arr[2] = 0f;
            arr[3] = 0f;

            arr[4] = 0f;
            arr[5] = 1f;
            arr[6] = 0f;
            arr[7] = 0f;

            arr[8] = 0f;
            arr[9] = 0f;
            arr[10] = 1f;
            arr[11] = 0f;

            arr[12] = 0f;
            arr[13] = 0f;
            arr[14] = 0f;
            arr[15] = 1f;
            return arr;
        }

        public Dictionary<string, SkeletonData> ResolveBoneHeirarchy(List<XivDependencyRootInfo> roots = null, XivRace race = XivRace.All_Races, List<string> bones = null, Action<bool, string> loggingFunction = null)
        {
            if (roots == null || roots.Count == 0)
            {
                if (!IsInternal)
                {
                    throw new Exception("Cannot dynamically resolve bone heirarchy for external model.");
                }


                // We can use the raw function here since we know this is a valid internal model file.
                XivDependencyRootInfo root = XivDependencyGraph.ExtractRootInfo(Source);

                if (race == XivRace.All_Races)
                {
                    race = IOUtil.GetRaceFromPath(Source);
                }


                // Just our one dynamically found root.
                roots = new List<XivDependencyRootInfo>() { root };
            }

            return TTModel.ResolveBoneHeirarchyRaw(roots, race, bones, loggingFunction);
        }
        /// <summary>
        /// Resolves the full bone heirarchy necessary to animate this TTModel.
        /// Used when saving the file to DB.  (Or potentially animating it)
        /// 
        /// NOTE: NOT Transaction safe... If the base skeletons were modified during transaction?
        /// This is niche enough to leave for the moment and come back to if it proves an issue.
        /// </summary>
        /// <returns></returns>
        public static Dictionary<string, SkeletonData> ResolveBoneHeirarchyRaw(List<XivDependencyRootInfo> roots, XivRace race, List<string> bones = null, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = ModelModifiers.NoOp;
            }

            var fullSkel = new Dictionary<string, SkeletonData>();
            var skelDict = new Dictionary<string, SkeletonData>();



            Task.Run(async () =>
            {
                bool parsedBase = false;
                var baseSkeletonPath = "";
                var extraSkeletonPath = "";

                foreach (var root in roots)
                {
                    // Do we need to get the base skel still?
                    string[] skeletonData;
                    if (!parsedBase)
                    {
                        try
                        {
                            baseSkeletonPath = await Sklb.GetBaseSkeletonFile(root, race);
                            skeletonData = File.ReadAllLines(baseSkeletonPath);

                            // Parse both skeleton files, starting with the base file.
                            foreach (var b in skeletonData)
                            {
                                if (b == "") continue;
                                var j = JsonConvert.DeserializeObject<SkeletonData>(b);
                                j.PoseMatrix = IOUtil.RowsFromColumns(j.PoseMatrix);
                                fullSkel.Add(j.BoneName, j);
                            }

                        } catch(Exception ex)
                        {
                            // If we failed to resolve the bones for some reason, log the error message and use a blank skel.
                            loggingFunction(true, "Error Parsing Skeleton ("+ baseSkeletonPath.ToString() +"):" + ex.Message);
                        }
                        parsedBase = true;
                    }


                    extraSkeletonPath = await Sklb.GetExtraSkeletonFile(root, race);
                    // Did this root have an extra skeleton in use?
                    if (!String.IsNullOrEmpty(extraSkeletonPath))
                    {
                        try
                        {
                            // If it did, add its bones to the resulting skeleton.
                            Dictionary<int, int> exTranslationTable = new Dictionary<int, int>();
                            skeletonData = File.ReadAllLines(extraSkeletonPath);
                            foreach (var b in skeletonData)
                            {
                                if (b == "") continue;
                                var j = JsonConvert.DeserializeObject<SkeletonData>(b);
                                j.PoseMatrix = IOUtil.RowsFromColumns(j.PoseMatrix);

                                if (fullSkel.ContainsKey(j.BoneName))
                                {
                                    // This is a parent level reference to a base bone.
                                    exTranslationTable.Add(j.BoneNumber, fullSkel[j.BoneName].BoneNumber);
                                } 
                                else if (exTranslationTable.ContainsKey(j.BoneParent))
                                {
                                    // Run it through the translation to match up with the base skeleton.
                                    j.BoneParent = exTranslationTable[j.BoneParent];

                                    // And generate its own new bone number
                                    var originalNumber = j.BoneNumber;
                                    j.BoneNumber = fullSkel.Select(x => x.Value.BoneNumber).Max() + 1;

                                    fullSkel.Add(j.BoneName, j);
                                    exTranslationTable.Add(originalNumber, j.BoneNumber);
                                } else
                                {
                                    // This is a root bone in the EX skeleton that has no parent element in the base skeleton.
                                    // Just stick it onto the root bone.
                                    j.BoneParent = fullSkel["n_root"].BoneNumber;

                                    // And generate its own new bone number
                                    var originalNumber = j.BoneNumber;
                                    j.BoneNumber = fullSkel.Select(x => x.Value.BoneNumber).Max() + 1;

                                    fullSkel.Add(j.BoneName, j);
                                    exTranslationTable.Add(originalNumber, j.BoneNumber);

                                }
                            }
                        } catch(Exception ex)
                        {
                            // If we failed to resolve the bones for some reason, log the error message and use a blank skel.
                            loggingFunction(true, "Error Parsing Extra Skeleton (" + extraSkeletonPath.ToString() + "):" + ex.Message);
                        }
                    }
                }
            }).Wait();

            // If no bones were specified, include all of them.
            if(bones == null)
            {
                bones = new List<string>();
                foreach(var e in fullSkel)
                {
                    bones.Add(e.Value.BoneName);
                }

                bones = bones.Distinct().ToList();
            }


            var badBoneId = 900;
            foreach (var s in bones)
            {
                // Merge additional bone copies in tools like 3ds/etc.  But not things like the tongue bones that end in _##.
                var fixedBone = Regex.Replace(s, "(?<=[^_0-9])[0-9]+$", string.Empty);

                if (fullSkel.ContainsKey(fixedBone))
                {
                    var skel = fullSkel[fixedBone];

                    if (skel.BoneParent == -1 && !skelDict.ContainsKey(skel.BoneName))
                    {
                        skelDict.Add(skel.BoneName, skel);
                    }

                    while (skel.BoneParent != -1)
                    {
                        if (!skelDict.ContainsKey(skel.BoneName))
                        {
                            skelDict.Add(skel.BoneName, skel);
                        }
                        skel = fullSkel.First(x => x.Value.BoneNumber == skel.BoneParent).Value;

                        if (skel.BoneParent == -1 && !skelDict.ContainsKey(skel.BoneName))
                        {
                            skelDict.Add(skel.BoneName, skel);
                        }
                    }
                }
                else
                {
                    // Create a fake bone for this, rather than strictly crashing out.
                    var skel = new SkeletonData();
                    skel.BoneName = s;
                    skel.BoneNumber = badBoneId;
                    badBoneId++;
                    skel.BoneParent = 0;
                    skel.InversePoseMatrix = NewIdentityMatrix();
                    skel.PoseMatrix = NewIdentityMatrix();

                    skelDict.Add(s, skel);
                    loggingFunction(true, $"The base game skeleton did not contain bone {s}. It has been parented to the root bone.");
                }
            }

            return skelDict;
        }


        /// <summary>
        /// Performs a basic sanity check on an incoming TTModel
        /// Returns true if there were no errors or errors that were resolvable.
        /// Returns false if the model was deemed insane.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="loggingFunction"></param>
        /// <returns></returns>
        public static bool SanityCheck(TTModel model, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = ModelModifiers.NoOp;
            }
            loggingFunction(false, "Validating model sanity...");

            bool hasWeights = model.HasWeights;

            if (model.MeshGroups.Count == 0)
            {
                loggingFunction(true, "Model has no data. - Model must have at least one valid Mesh Group.");
                return false;
            }

            var mIdx = 0;
            foreach(var m in model.MeshGroups)
            {
                if(m.Parts.Count == 0)
                {
                    var part = new TTMeshPart();
                    part.Name = "Part 0";
                    m.Parts.Add(part);
                }

                // Meshes in animated models must have at least one bone in their bone set in order to not generate a crash.
                if(hasWeights && m.Bones.Count == 0)
                {
                    m.Bones.Add("n_root");
                }
                mIdx++;
            }

            return true;
        }

        /// <summary>
        /// Checks the model for common valid-but-unusual states that users often end up in by accident, providing 
        /// a warning message for each one, if the conditions are met.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="loggingFunction"></param>
        public static void CheckCommonUserErrors(TTModel model, Action<bool, string> loggingFunction = null)
        {
            if (loggingFunction == null)
            {
                loggingFunction = ModelModifiers.NoOp;
            }
            loggingFunction(false, "Checking for unusual data...");

            if (model.Materials.Count > 4 && model.MdlVersion == 5)
            {
                loggingFunction(true, "Model has more than four active materials.  The following materials will be ignored in game: ");
                var idx = 0;
                foreach (var m in model.Materials)
                {
                    if (idx >= 4)
                    {
                        loggingFunction(true, "Material: " + m);
                    }
                    idx++;
                }
            }

            int mIdx = 0;
            foreach (var m in model.MeshGroups)
            {
                int pIdx = 0;
                foreach (var p in m.Parts)
                {

                    if (p.Vertices.Count == 0)
                    {
                        pIdx++;
                        continue;
                    }

                    bool anyAlpha = false;
                    bool anyColor = false;
                    bool anyColor2 = false;
                    bool anyWeirdUV1s = false;
                    bool anyWeirdUV2s = false;

                    foreach (var v in p.Vertices)
                    {
                        anyAlpha = anyAlpha || (v.VertexColor[3] > 0);
                        anyColor = anyColor || (v.VertexColor[0] > 0 || v.VertexColor[1] > 0 || v.VertexColor[2] > 0);
                        anyColor2 = anyColor2 || (v.VertexColor2[0] > 0 || v.VertexColor2[1] > 0 || v.VertexColor2[2] > 0 || v.VertexColor2[3] > 0);
                        anyWeirdUV1s = anyWeirdUV1s || (v.UV1.X > 2 || v.UV1.X < -2 || v.UV1.Y > 2 || v.UV1.Y < -2);
                        anyWeirdUV2s = anyWeirdUV2s || (v.UV2.X > 2 || v.UV2.X < -2 || v.UV2.Y > 2 || v.UV2.Y < -2);
                    }

                    if (!anyAlpha)
                    {
                        loggingFunction(true, "Mesh: " + mIdx + " Part: " + pIdx + " has a fully black Vertex Alpha channel.  This will render the part invisible in-game.  Was this intended?");
                    }

                    if (!anyColor)
                    {
                        loggingFunction(true, "Mesh: " + mIdx + " Part: " + pIdx + " has a fully black Vertex Color channel.  This can have unexpected results on in-game rendering.  Was this intended?");
                    }
                    if (!anyColor)
                    {
                        // TODO: Do we care about this? Who knows.
                    }

                    if (anyWeirdUV1s)
                    {
                        loggingFunction(true, "Mesh: " + mIdx + " Part: " + pIdx + " has unusual UV1 data.  This can have unexpected results on texture placement.  Was this inteneded?");
                    }

                    if (anyWeirdUV2s)
                    {
                        loggingFunction(true, "Mesh: " + mIdx + " Part: " + pIdx + " has unusual UV2 data.  This can have unexpected results on decal placement or opacity.  Was this inteneded?");
                    }

                    pIdx++;
                }
                mIdx++;
            }

        }

        #endregion
    }
}
