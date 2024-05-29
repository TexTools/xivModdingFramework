using System;

namespace xivModdingFramework.Mods.Interfaces
{
    public interface IModPackData
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

    /// <summary>
    /// Baseline modpack data, just used for handling metadata.
    /// Only used in PMP creation currently.
    /// </summary>
    public class BaseModpackData : IModPackData
    {
        public string Name { get; set; }
        public string Author { get; set; }
        public Version Version { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }
    }
}
