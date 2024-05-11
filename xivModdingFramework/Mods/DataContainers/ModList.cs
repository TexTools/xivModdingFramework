// xivModdingFramework
// Copyright © 2018 Rafael Gonzalez - All Rights Reserved
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;

namespace xivModdingFramework.Mods.DataContainers
{
    public class ModList : ICloneable 
    {
        /// <summary>
        /// The ModList Version
        /// </summary>
        public string version { get; set; }

        /// <summary>
        /// The list of ModPacks currently installed
        /// </summary>
        public IEnumerable<ModPack> ModPacks { get
            {
                if (Mods == null) { 
                    return new List<ModPack>();
                }

                var modPacks = new Dictionary<string, ModPack>();
                foreach (var m in Mods)
                {
                    if(m.modPack == null)
                    {
                        continue;
                    }
                    if(String.IsNullOrWhiteSpace(m.fullPath))
                    {
                        continue;
                    }

                    if (modPacks.ContainsKey(m.modPack.name))
                    {
                        m.modPack = modPacks[m.modPack.name];
                    }
                    else
                    {
                        modPacks.Add(m.modPack.name, m.modPack);
                    }
                }
                return modPacks.Select(x => x.Value);
            }
        }

        [JsonIgnore]
        private Dictionary<string, Mod> _ModDictionary;

        [JsonIgnore]
        public Dictionary<string, Mod> ModDictionary
        {
            get
            {
                if(_ModDictionary == null)
                {
                    _ModDictionary = new Dictionary<string, Mod>();
                    foreach(var mod in Mods)
                    {
                        _ModDictionary.Add(mod.fullPath, mod);
                    }
                }
                return _ModDictionary;
            }

        }

        [JsonIgnore]
        private List<Mod> _ModList;

        /// <summary>
        /// The list of Mods
        /// Returns a SHALLOW CLONE of the current modlist.
        /// Directly adding to or removing mods from this list will have no lasting effect.
        /// </summary>
        [JsonProperty]
        public List<Mod> Mods
        {
            get
            {
                if(_ModList == null)
                {
                    return null;
                }
                return _ModList.ToList();
            }
            private set
            {
                _ModList = value;
            }
        }

        /// <summary>
        /// Removes a given mod based on its modified path, if it exists.
        /// </summary>
        /// <param name="path"></param>
        public void RemoveMod(string path)
        {
            if (ModDictionary.ContainsKey(path))
            {
                RemoveMod(ModDictionary[path]);
            }
        }

        /// <summary>
        /// Adds or replaces a mod based on it's file path.
        /// </summary>
        /// <param name="m"></param>
        public void AddOrUpdateMod(Mod m)
        {
            if (ModDictionary.ContainsKey(m.fullPath))
            {
                ModDictionary[m.fullPath] = m;
            }
            else
            {
                ModDictionary.Add(m.fullPath, m);
            }

            // If list structure already contains the mod, we don't need to re-add it.
            if (!_ModList.Contains(m))
            {
                _ModList.Add(m);
            }
        }

        /// <summary>
        /// Removes a mod from the modlist.
        /// </summary>
        /// <param name="m"></param>
        public void RemoveMod(Mod m)
        {
            if (ModDictionary.ContainsKey(m.fullPath))
            {
                ModDictionary.Remove(m.fullPath);
            }
            _ModList.Remove(m);
        }

        /// <summary>
        /// Removes a subset of mods from the modlist.
        /// </summary>
        /// <param name="mods"></param>
        public void RemoveMods(IEnumerable<Mod> mods)
        {
            foreach (var mod in mods)
            {
                RemoveMod(mod);
            }
        }

        /// <summary>
        /// Safely attempts to get a given mod based on path, returning NULL if the mod does not exist.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public Mod GetMod(string path)
        {
            ModDictionary.TryGetValue(path, out var mod);
            return mod;
        }

        public ModList(bool newList = false)
        {
            // This weird pattern is necessary to not bash JSON Deserialization.
            if (newList)
            {
                _ModList = new List<Mod>();
            }
        }
        public object Clone()
        {
            // Since reflection methods have proven slightly unstable for this purpose, the safest
            // method is to simply serialize and deserialize us into a new object.

            // If perf is too bad, we can also introduce a full clone down the chain, but that's
            // slightly less safe in the event any of the classes ever get extended.
            return JsonConvert.DeserializeObject<ModList>(JsonConvert.SerializeObject(this));
        }
    }

    public class Mod
    {
        /// <summary>
        /// The source of the mod
        /// </summary>
        /// <remarks>
        /// This is normally the name of the application used to import the mod
        /// </remarks>
        public string source { get; set; }

        /// <summary>
        /// The modified items name
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// The modified items category
        /// </summary>
        public string category { get; set; }

        /// <summary>
        /// The internal path of the modified item
        /// </summary>
        public string fullPath { get; set; }

        /// <summary>
        /// The dat file where the modified item is located
        /// </summary>
        public string datFile { get; set; }

        /// <summary>
        /// The mod status
        /// </summary>
        /// <remarks>
        /// true if enabled, false if disabled
        /// </remarks>
        public bool enabled { get; set; }

        /// <summary>
        /// The minimum framework version necessary to operate on this mod safely.
        /// </summary>
        public string minimumFrameworkVersion = "1.0.0.0";

        /// <summary>
        /// The modPack associated with this mod
        /// </summary>
        public ModPack modPack { get; set; }

        /// <summary>
        /// The mod data including offsets
        /// </summary>
        public Data data { get; set; }

        public bool IsInternal()
        {
            return source == Constants.InternalModSourceName;
        }

        public bool IsCustomFile()
        {
            return data.modOffset == data.originalOffset;
        }

        /// <summary>
        /// Creates a Mod instance using the provided JSON
        /// </summary>
        /// <param name="modsJson"></param>
        /// <param name="sourceApplication"></param>
        /// <returns></returns>
        public static Mod MakeModFromJson(ModsJson modsJson, string sourceApplication)
        {
            return new Mod
            {
                source = sourceApplication,
                name = modsJson.Name,
                category = modsJson.Category,
                fullPath = modsJson.FullPath,
                datFile = IOUtil.GetDataFileFromPath(modsJson.FullPath).GetDataFileName(),
                enabled = true,
                modPack = modsJson.ModPackEntry,
                data = new Data()
            };
        }
    }

    public class Data
    {
        /// <summary>
        /// The datatype associated with this mod
        /// </summary>
        /// <remarks>
        /// 2: Binary Data, 3: Models, 4: Textures
        /// </remarks>
        public int dataType { get; set; }

        /// <summary>
        /// The oringial offset of the modified item
        /// </summary>
        /// <remarks>
        /// Used to revert to the items original texture
        /// </remarks>
        public long originalOffset { get; set; }

        /// <summary>
        /// The modified offset of the modified item
        /// </summary>
        public long modOffset { get; set; }

        /// <summary>
        /// The size of the modified items data
        /// </summary>
        /// <remarks>
        /// When importing a previously modified texture, this value is used to determine whether the modified data will be overwritten
        /// </remarks>
        public int modSize { get; set; }

    }

    public class ModPack
    {

        /// <summary>
        /// Generates a hash identifier from this mod's name and author information,
        /// in lowercase.  For identifying new versions of same mods, potentially.
        /// </summary>
        /// <returns></returns>
        public byte[] GetHash()
        {
            using (SHA256 sha = SHA256.Create())
            {
                var n = name.ToLower();
                var a = author.ToLower();
                var key = n + a;
                var keyBytes= Encoding.Unicode.GetBytes(key);
                var hash = sha.ComputeHash(keyBytes);
                return hash;
            }
        }

        /// <summary>
        /// The name of the modpack
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// The modpack author
        /// </summary>
        public string author { get; set; }

        /// <summary>
        /// The modpack version
        /// </summary>
        public string version { get; set; }

        /// <summary>
        /// The URL the author associated with this modpack.
        /// </summary>
        public string url { get; set; }
    }
}