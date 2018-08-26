using System.Collections.Generic;

namespace xivModdingFramework.Models.DataContainers
{
    public class ModelImportSettings
    {
        /// <summary>
        /// The path of the model being imported
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Determines if we will attempt to fix mesh hiding for the mesh
        /// </summary>
        public bool Fix { get; set; }

        /// <summary>
        /// Determines if we will disable mesh hiding for the mesh
        /// </summary>
        public bool Disable { get; set; }

        /// <summary>
        /// The mesh part dictionary
        /// </summary>
        public Dictionary<int, int> PartDictionary { get; set; }
    }
}