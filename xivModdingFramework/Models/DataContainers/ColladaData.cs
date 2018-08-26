using System.Collections.Generic;

namespace xivModdingFramework.Models.DataContainers
{
    public class ColladaData
    {
        /// <summary>
        /// The bone strings in the collada file
        /// </summary>
        public string[] Bones { get; set; }

        /// <summary>
        /// The positions in the collada file
        /// </summary>
        public List<float> Positions { get; set; }

        /// <summary>
        /// The normals in the collada file
        /// </summary>
        public List<float> Normals { get; set; }

        /// <summary>
        /// The primary texture coordinates in the collada file
        /// </summary>
        public List<float> TextureCoordinates0 { get; set; }

        /// <summary>
        /// The secondary texture coordinates in the collada file
        /// </summary>
        public List<float> TextureCoordinates1 { get; set; }

        /// <summary>
        /// The bone weights in the collada file
        /// </summary>
        public List<float> BoneWeights { get; set; }

        /// <summary>
        /// The BiNormals in the collada file
        /// </summary>
        public List<float> BiNormals { get; set; }

        /// <summary>
        /// The tangents in the collada file
        /// </summary>
        public List<float> Tangents { get; set; }

        /// <summary>
        /// The indices in teh collada file
        /// </summary>
        public List<int> Indices { get; set; }


        public List<int> BoneIndices { get; set; }

        public List<int> Vcounts { get; set; }

        public List<int> PositionIndices { get; set; }

        public List<int> NormalIndices { get; set; }

        public List<int> BiNormalIndices { get; set; }

        public List<int> TextureCoordinate0Indices { get; set; }

        public List<int> TextureCoordinate1Indices { get; set; }

        public Dictionary<int, int> PartsDictionary = new Dictionary<int, int>();

        public int IndexStride { get; set; }

        public int TextureCoordinateStride { get; set; }

        public bool IsBlender { get; set; }
    }
}