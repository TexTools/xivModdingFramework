﻿// xivModdingFramework
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
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
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
        public string Version { get; set; }


        [JsonProperty("ModPacks")]
        private Dictionary<string, ModPack> _ModPacks;

        /// <summary>
        /// The list of ModPacks currently installed.
        /// Returns a clone of the internal list.  Altering this list will have no effect on the ModList.
        /// </summary>
        [JsonIgnore]
        public Dictionary<string, ModPack> ModPacks { 
            get
            {
                return new Dictionary<string, ModPack>(_ModPacks);
            }
        }


        [JsonProperty("Mods")]
        private Dictionary<string, Mod> _Mods;

        /// <summary>
        /// The dictionary of mod files currently installed.
        /// Returns a clone of the dictionary.  Altering this list will have no effect on the ModList.
        /// </summary>
        [JsonIgnore]
        public Dictionary<string, Mod> Mods
        {
            get
            {
                return new Dictionary<string, Mod>(_Mods);
            }

        }


        /// <summary>
        /// Adds or updates a mod based on its file path.
        /// </summary>
        /// <param name="mod"></param>
        public void AddOrUpdateMod(Mod mod)
        {
            if (_Mods.ContainsKey(mod.FilePath))
            {
                var oldMod = _Mods[mod.FilePath];
                _Mods[mod.FilePath] = mod;


                if (!string.IsNullOrWhiteSpace(oldMod.ModPack) && _ModPacks.ContainsKey(oldMod.ModPack))
                {
                    // Remove our old mod from its associated modpack, and remove the modpack if it was the last one.
                    var shouldRemove = _ModPacks[oldMod.ModPack].INTERNAL_RemoveMod(oldMod.FilePath);
                    if (shouldRemove)
                    {
                        _ModPacks.Remove(oldMod.ModPack);
                    }
                }
            }
            else
            {
                _Mods.Add(mod.FilePath, mod);
            }

            // Add to modpack's list if it has a valid modpack.
            if (!string.IsNullOrWhiteSpace(mod.ModPack))
            {
                if (_ModPacks.ContainsKey(mod.ModPack))
                {
                    var newMp = new ModPack()
                    {
                        Name = mod.ModPack,
                    };
                    _ModPacks.Add(mod.ModPack, newMp);
                }
                _ModPacks[mod.ModPack].INTERNAL_AddMod(mod.FilePath);
            }
        }

        /// <summary>
        /// Add or update a subset of mods based on their file paths.
        /// </summary>
        /// <param name="mods"></param>
        public void AddOrUpdateMods(IEnumerable<Mod> mods)
        {
            foreach(var mod in mods)
            {
                AddOrUpdateMod(mod);
            }
        }

        /// <summary>
        /// Removes a mod from the modlist.
        /// </summary>
        /// <param name="mod"></param>
        public void RemoveMod(Mod mod)
        {
            RemoveMod(mod.FilePath);
        }

        /// <summary>
        /// Removes a mod from the modlist.
        /// </summary>
        /// <param name="m"></param>
        public void RemoveMod(string path)
        {
            if (!_Mods.ContainsKey(path))
            {
                return;
            }
            var oldMod = _Mods[path];
            _Mods.Remove(path);
            
        }

        /// <summary>
        /// Removes a subset of mods from the modlist.
        /// </summary>
        /// <param name="mods"></param>
        public void RemoveMods(IEnumerable<string> mods)
        {
            foreach (var mod in mods)
            {
                RemoveMod(mod);
            }
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
        public Mod? GetMod(string path)
        {
            _Mods.TryGetValue(path, out var mod);
            return mod;
        }

        public ModPack? GetModPack(string modpackName)
        {
            _ModPacks.TryGetValue(modpackName, out var modpack);
            return modpack;
        }

        public IEnumerable<Mod> GetMods()
        {
            return GetMods(x => true);
        }

        /// <summary>
        /// Retrieves a subset of mods based on a given predicate.
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public IEnumerable<Mod> GetMods(Func<Mod, bool> predicate)
        {
            Func<KeyValuePair<string, Mod>, bool> newPred = (KeyValuePair<string, Mod> kv) =>
            {
                return predicate(kv.Value);
            };

            return _Mods.Where(newPred).Select(x => x.Value);
        }

        /// <summary>
        /// Retrieves a subset of mods based on a given predicate.
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public IEnumerable<ModPack> GetModPacks(Func<ModPack, bool> predicate)
        {
            Func<KeyValuePair<string, ModPack>, bool> newPred = (KeyValuePair<string, ModPack> kv) =>
            {
                return predicate(kv.Value);
            };

            return _ModPacks.Where(newPred).Select(x => x.Value);
        }


        /// <summary>
        /// Regenerate the internal list of mods-per-modpack.
        /// Used after deserializing the json modpack file.
        /// </summary>
        internal void RebuildModPackList()
        {
            foreach(var mpkv in _ModPacks)
            {
                mpkv.Value.INTERNAL_ClearMods();
            }

            foreach(var mkv in _Mods)
            {
                if (string.IsNullOrWhiteSpace(mkv.Value.ModPack))
                {
                    continue;
                }

                if (!_ModPacks.ContainsKey(mkv.Value.ModPack))
                {
                    var newMp = new ModPack()
                    {
                        Name = mkv.Value.ModPack,
                    };
                    _ModPacks.Add(mkv.Value.ModPack, newMp);
                }

                _ModPacks[mkv.Value.ModPack].INTERNAL_AddMod(mkv.Value.FilePath);
            }
        }

        public ModList(bool newList = false)
        {
            // This weird pattern is necessary to not bash JSON Deserialization.
            if (newList)
            {
                _Mods = new Dictionary<string, Mod>();
                _ModPacks = new Dictionary<string, ModPack>();
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

    public struct Mod
    {
        /// <summary>
        /// The source of the mod
        /// </summary>
        /// <remarks>
        /// This is normally the name of the application used to import the mod
        /// </remarks>
        public string SourceApplication { get; set; }

        /// <summary>
        /// The modified items name
        /// </summary>
        public string ItemName { get; set; }

        /// <summary>
        /// The modified items category
        /// </summary>
        public string ItemCategory { get; set; }

        /// <summary>
        /// The internal path of the modified item
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// The mod status
        /// </summary>
        /// <remarks>
        /// true if enabled, false if disabled
        /// </remarks>
        public bool Enabled { get; set; }


        [JsonProperty("ModPack")]
        private string _ModPack { get; set; }

        /// <summary>
        /// The name of the containing modpack
        /// Automatically Coerced to empty string if it is ever null.
        /// </summary>
        [JsonIgnore]
        public string ModPack {
            get {

                if (string.IsNullOrWhiteSpace(_ModPack))
                {
                    return "";
                }
                else
                {
                    return _ModPack;
                }
            } 
            set {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _ModPack = "";
                } else
                {
                    _ModPack = value;
                }
            }
        }

        /// <summary>
        /// The 8x, Dat-Embedded offset of the mod file in the FFXIV internal file system.
        /// </summary>
        public long ModOffset8x { get; set; }

        /// <summary>
        /// The 8x, Dat-Embedded offset of the mod file in the FFXIV internal file system.
        /// </summary>
        public long OriginalOffset8x { get; set; }

        /// <summary>
        /// The size in bytes of the SqPack compressed mod file.
        /// </summary>
        public int FileSize { get; set; }

        public bool IsInternal()
        {
            return SourceApplication == Constants.InternalModSourceName;
        }

        public bool IsCustomFile()
        {
            return ModOffset8x == OriginalOffset8x;
        }

        /// <summary>
        /// Retrieves the full data for the associated modpack.
        /// Syntactic shortcut for ModList.GetModPack(Mod.Modpack)
        /// </summary>
        /// <param name="modList"></param>
        /// <returns></returns>
        public ModPack? GetModPack(ModList modList)
        {
            return modList.GetModPack(ModPack);
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
                SourceApplication = sourceApplication,
                ItemName = modsJson.Name,
                ItemCategory = modsJson.Category,
                FilePath = modsJson.FullPath,
                Enabled = true,
                ModPack = modsJson.ModPackEntry.Name,
                ModOffset8x = modsJson.ModOffset,
            };
        }
        public static bool operator ==(Mod m1, Mod m2)
        {
            return m1.Equals(m2);
        }

        public static bool operator !=(Mod m1, Mod m2)
        {
            return !m1.Equals(m1);
        }

        public bool Equals(Mod other)
        {
            return Equals(other, this);
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            var m2 = (Mod)obj;

            if(m2.FilePath == this.FilePath)
            {
                // Mods are considered equal if they affect the same file.
                return true;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return FilePath.GetHashCode();
        }
    }

    public struct ModPack
    {
        public bool Valid
        {
            get
            {
                return String.IsNullOrWhiteSpace(Name);
            }
        }

        /// <summary>
        /// The name of the modpack
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The modpack author
        /// </summary>
        public string Author { get; set; }

        /// <summary>
        /// The modpack version
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// The URL the author associated with this modpack.
        /// </summary>
        public string Url { get; set; }

        [JsonIgnore]
        private HashSet<string> _Mods;

        /// <summary>
        /// The Mods contained in this modpack.
        /// This is a Clone of the internal list.  Modifying the return value will not change the underlying modpack.
        /// </summary>
        [JsonIgnore]
        public HashSet<string> Mods {
            get
            {
                if (_Mods == null)
                {
                    _Mods = new HashSet<string>();
                }
                return new HashSet<string>(_Mods);
            }
        }

        /// <summary>
        /// Internal use only Clear Mods function.  Should only be called by the owning modlist.
        /// </summary>
        /// <param name="filePath"></param>
        internal void INTERNAL_ClearMods()
        {
            _Mods.Clear();
        }

        /// <summary>
        /// Internal use only Add Mod function.  Should only be called by the owning modlist.
        /// </summary>
        /// <param name="filePath"></param>
        internal void INTERNAL_AddMod(string filePath)
        {
            if(_Mods == null)
            {
                _Mods = new HashSet<string>();
            }
            if (!_Mods.Contains(filePath))
            {
                _Mods.Add(filePath);
            }
        }

        /// <summary>
        /// Internal use only Remove Mod function.  Should only be called by the owning modlist.
        /// </summary>
        /// <param name="filePath"></param>
        internal bool INTERNAL_RemoveMod(string filePath)
        {
            if (_Mods == null)
            {
                _Mods = new HashSet<string>();
            }

            _Mods.Remove(filePath);
            if(_Mods.Count == 0)
            {
                return true;
            }
            return false;
        }

        public static bool operator ==(ModPack m1, ModPack m2)
        {
            return m1.Equals(m2);
        }

        public static bool operator !=(ModPack m1, ModPack m2)
        {
            return !m1.Equals(m1);
        }

        public bool Equals(ModPack other)
        {
            return Equals(other, this);
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            var m2 = (ModPack)obj;

            if (m2.Name == this.Name)
            {
                // Modpacks are considered equal if they affect the same name.
                return true;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }
}