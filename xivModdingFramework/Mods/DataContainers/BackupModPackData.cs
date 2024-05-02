using System;
using System.Collections.Generic;
using xivModdingFramework.Mods.Interfaces;

namespace xivModdingFramework.Mods.DataContainers
{
    public class BackupModPackData : IModPackData
    {
        /// <summary>
        /// The name of the mod pack
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The mod pack author
        /// </summary>
        public string Author { get; set; } = "TexTools";

        /// <summary>
        /// The mod pack version
        /// </summary>
        public Version Version { get; set; } = new Version("1.0.0");

        /// <summary>
        /// The modpack Url
        /// </summary>
        public string Url { get; set; } = "";

        /// <summary>
        /// The description for the mod pack
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// The list of mods to back up
        /// </summary>
        public List<BackupModData> ModsToBackup { get; set; }
    }

    public class BackupModData
    {
        /// <summary>
        /// Simple mod data
        /// </summary>
        public SimpleModData SimpleModData { get; set; }

        /// <summary>
        /// Mod pack that the mod is a part of
        /// </summary>
        public  ModPack ModPack { get; set; }
    }
}
