using System;
using System.Collections.Generic;
using System.Text;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Materials.DataContainers;
using xivModdingFramework.Models.DataContainers;

namespace xivModdingFramework.Mods.FileTypes
{
    /// <summary>
    /// The high-level representation of a model kit.
    /// </summary>
    public class ModelKit
    {
        public string Name;
        public string Author;
        public string Version;
        public string Url;
        public string Description;

        public string Image;


        public TTModel Model;
        public XivMtrl Material;
        public XivRace OriginalRace;

    }



    /// <summary>
    /// The base Low-Level JSON representation for a model kit.
    /// </summary>
    public class ModelKitJson
    {
        public string Name;
        public string Author;
        public string Version;
        public string Url;
        public string Description;


        /// <summary>
        /// Path to the preview image within the Zip file.
        /// </summary>
        public string Image;

        /// <summary>
        /// The original Race identifier if there was one for the model.
        /// Should be NULL or Empty String if the model should not be racially scaled.
        /// </summary>
        public string OriginalRace;

        /// <summary>
        /// Model file path within the zip file.
        /// </summary>
        public string Model;

        /// <summary>
        /// Model-listed Material Name => Zip Path for Materials.
        /// </summary>
        public Dictionary<string, string> Materials;

        /// <summary>
        /// FFXIV Internal Path => Zip Path for textures.
        /// Textures should only be imported by default if they do not exist.
        /// </summary>
        public Dictionary<string, string> Textures;
    }
}
