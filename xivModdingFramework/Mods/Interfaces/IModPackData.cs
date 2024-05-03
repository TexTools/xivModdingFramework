using System;

namespace xivModdingFramework.Mods.Interfaces
{
    internal interface IModPackData
    {
        /// <summary>
        /// The name of the mod pack
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The mod pack author
        /// </summary>
        public string Author { get; set; }

        /// <summary>
        /// The mod pack version
        /// </summary>
        public Version Version { get; set; }

        /// <summary>
        /// The description for the mod pack
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Author's supplied URL for the modpack.
        /// </summary>
        public string Url { get; set; }
    }
}
