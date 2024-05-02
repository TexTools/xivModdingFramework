using HelixToolkit.SharpDX.Core.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Authentication.ExtendedProtection;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.Models.FileTypes
{
    public static class Est
    {
        public enum EstType
        {
            Invalid,
            Head,
            Body, 
            Hair,
            Face
        };

        private static DirectoryInfo _gameDirectory {
            get {
                return XivCache.GameInfo.GameDirectory;
            }
        }

        private static readonly Dictionary<EstType, string> EstFiles = new Dictionary<EstType, string>()
        {
            { EstType.Head, "chara/xls/charadb/extra_met.est" },
            { EstType.Body, "chara/xls/charadb/extra_top.est" },
            { EstType.Hair, "chara/xls/charadb/hairskeletontemplate.est" },
            { EstType.Face, "chara/xls/charadb/faceskeletontemplate.est" },
        };

        public static EstType GetEstType(IItem item)
        {
            if(item == null)
            {
                return EstType.Invalid;
            }

            var root = item.GetRoot();
            return GetEstType(root);
        }
        public static EstType GetEstType(XivDependencyRoot root)
        {
            if (root == null) return EstType.Invalid;

            return GetEstType(root.Info);
        }
        public static EstType GetEstType(XivDependencyRootInfo root)
        {
            if (root.PrimaryType == XivItemType.human)
            {
                if(root.SecondaryType == XivItemType.face)
                {
                    return EstType.Face;
                } else if (root.SecondaryType == XivItemType.hair)
                {
                    return EstType.Hair;
                } else
                {
                    return EstType.Invalid;
                }

            } else if( root.PrimaryType == XivItemType.equipment)
            {
                if(root.Slot == "met")
                {
                    return EstType.Head;
                } else if (root.Slot == "top")
                {
                    return EstType.Body;
                } else
                {
                    return EstType.Invalid;
                }

            } else
            {
                return EstType.Invalid;
            }
        }

        public static string GetSystemSlot(EstType type)
        {
            switch (type)
            {
                case EstType.Head:
                    return "met";
                case EstType.Body:
                    return "top";
                case EstType.Face:
                    return "face";
                case EstType.Hair:
                    return "hair";
                default:
                    throw new Exception("Cannot get slot for Invalid EST Type.");
            }

        }
        public static char GetSystemPrefix(EstType type)
        {
            switch(type)
            {
                case EstType.Head:
                    return 'm';
                case EstType.Body:
                    return 't';
                case EstType.Face:
                    return 'f';
                case EstType.Hair:
                    return 'h';
                default:
                    throw new Exception("Cannot get prefix for Invalid EST Type.");
            }
        }

        /// <summary>
        /// Retrieve the list of all possible skeletal selections for a given type.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static async Task<Dictionary<XivRace, HashSet<int>>> GetAllExtraSkeletons(EstType type, XivRace raceFilter = XivRace.All_Races, bool includeNpcs = false, ModTransaction tx = null)
        {
            var ret = new Dictionary<XivRace, HashSet<int>>();
            var races = includeNpcs ? Eqp.PlayableRacesWithNPCs : Eqp.PlayableRaces;

            if(raceFilter != XivRace.All_Races)
            {
                races = new List<XivRace>();
                races.Add(raceFilter);
            }

            var entries = await GetEstFile(type, false, tx);

            foreach (var race in races)
            {
                ret.Add(race, new HashSet<int>());
                if (!entries.ContainsKey(race)) continue;

                var dict = entries[race];
                foreach (var kv in dict)
                {
                    ret[race].Add(kv.Value.SkelId);
                }
            }

            return ret;
        }

        /// <summary>
        /// Saves a given set of modified entries to the EST file.
        /// Entries with a SkeletonID of 0 will be removed.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="modifiedEntries"></param>
        /// <returns></returns>
        public static async Task SaveExtraSkeletonEntries(EstType type, List<ExtraSkeletonEntry> modifiedEntries, IItem referenceItem = null, ModTransaction tx = null)
        {
            var entries = await GetEstFile(type, false, tx);

            // Add/Remove entries.
            foreach (var entry in modifiedEntries)
            {
                if (entry.SkelId == 0)
                {
                    if (!entries.ContainsKey(entry.Race)) {
                        continue;
                    }
                    // Remove this entry.
                    if (entries[entry.Race].ContainsKey(entry.SetId))
                    {
                        entries[entry.Race].Remove(entry.SetId);
                    }
                }
                else
                {
                    // Add or update this entry.
                    if (entries[entry.Race].ContainsKey(entry.SetId))
                    {
                        entries[entry.Race][entry.SetId] = entry;
                    }
                    else
                    {
                        entries[entry.Race].Add(entry.SetId, entry);
                    }
                }
            }

            // Save file.
            await SaveEstFile(type, entries, referenceItem, tx);
        }

        public static async Task<Dictionary<XivRace, ExtraSkeletonEntry>> GetExtraSkeletonEntries(IItem item, bool forceDefault = false, ModTransaction tx = null)
        {
            if (item == null)
            {
                return new Dictionary<XivRace, ExtraSkeletonEntry>();
            }

            return await GetExtraSkeletonEntries(item.GetRoot(), forceDefault, tx);
        }
        public static async Task<Dictionary<XivRace, ExtraSkeletonEntry>> GetExtraSkeletonEntries(XivDependencyRoot root, bool forceDefault = false, ModTransaction tx = null)
        {
            if(root == null)
            {
                return new Dictionary<XivRace, ExtraSkeletonEntry>();
            }

            return await GetExtraSkeletonEntries(root.Info, forceDefault, tx);
        }
        public static async Task<Dictionary<XivRace, ExtraSkeletonEntry>> GetExtraSkeletonEntries(XivDependencyRootInfo root, bool forceDefault = false, ModTransaction tx = null)
        {
            var type = GetEstType(root);
            if(type == EstType.Invalid)
            {
                return new Dictionary<XivRace, ExtraSkeletonEntry>();
            }

            var id = (ushort)root.PrimaryId;
            if (type == EstType.Face || type == EstType.Hair) {
                id = (ushort)root.SecondaryId;
            }
            var entries = await GetExtraSkeletonEntries(type, id, forceDefault, false, tx);


            // Hair and Faces have to be further trimmed down to only the entries associated with their root.
            if (type == EstType.Face || type == EstType.Hair)
            {
                var ret = new Dictionary<XivRace, ExtraSkeletonEntry>();
                var race = XivRaces.GetXivRace(root.PrimaryId);

                if (entries.ContainsKey(race))
                {
                    ret.Add(race, entries[race]);
                } else
                {
                    ret.Add(race, new ExtraSkeletonEntry(race, id));
                }
                entries = ret;
            }

            return entries;
        }

        /// <summary>
        /// Retrieves the skeleton information for a given set for all races.
        /// </summary>
        /// <param name=""></param>
        /// <param name="setId"></param>
        /// <param name="forceDefault"></param>
        /// <returns></returns>
        public static async Task<Dictionary<XivRace, ExtraSkeletonEntry>> GetExtraSkeletonEntries(EstType type, ushort setId, bool forceDefault = false, bool includeNpcs = false, ModTransaction tx = null)
        {
            if (type == EstType.Invalid)
            {
                return new Dictionary<XivRace, ExtraSkeletonEntry>();
            }

            var entries = await GetEstFile(type, forceDefault, tx);
            
            var races = Eqp.PlayableRaces;
            if(includeNpcs)
            {
                races = Eqp.PlayableRacesWithNPCs;
            }

            var ret = new Dictionary<XivRace, ExtraSkeletonEntry>();

            foreach (var race in races)
            {
                if(!entries.ContainsKey(race))
                {
                    continue;
                }

                var dict = entries[race];
                if (dict.ContainsKey(setId))
                {
                    ret.Add(race, dict[setId]);
                } else
                {
                    ret.Add(race, new ExtraSkeletonEntry(race, setId));
                }
            }
            return ret;
        }


        /// <summary>
        /// Retrieves the extra skeleton entry for a given Type/Race/Equipment Set.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="race"></param>
        /// <param name="setId"></param>
        /// <param name="forceDefault"></param>
        /// <returns></returns>
        public static async Task<ExtraSkeletonEntry> GetExtraSkeletonEntry(EstType type, XivRace race, ushort setId, bool forceDefault = false, ModTransaction tx = null)
        {
            
            var entries = await GetEstFile(type, forceDefault, tx);

            if(!entries.ContainsKey(race)) {
                return new ExtraSkeletonEntry(race, setId);
            }

            if(!entries[race].ContainsKey(setId))
            {
                return new ExtraSkeletonEntry(race, setId);
            }

            return entries[race][setId];
        }

        private static async Task<Dictionary<XivRace, Dictionary<ushort, ExtraSkeletonEntry>>> GetEstFile(EstType type, bool forceDefault = false, ModTransaction tx = null)
        {
            var _dat = new Dat(_gameDirectory);
            var data = await _dat.GetType2Data(EstFiles[type], forceDefault, tx);

            var count = BitConverter.ToUInt32(data, 0);

            Dictionary<XivRace, Dictionary<ushort, ExtraSkeletonEntry>> entries = new Dictionary<XivRace, Dictionary<ushort, ExtraSkeletonEntry>>();
            for (int i = 0; i < count; i++)
            {
                var entry = ExtraSkeletonEntry.Read(data, count, (uint)i);
                if (!entries.ContainsKey(entry.Race))
                {
                    entries.Add(entry.Race, new Dictionary<ushort, ExtraSkeletonEntry>());
                }
                
                if(!entries[entry.Race].ContainsKey(entry.SetId))
                {
                    // For whatever reason there is exactly one dupe in the game files, where a Lalafell M face has two identical entries.
                    // Doesn't seem to matter to SE, so shouldn't matter to us.
                    entries[entry.Race].Add(entry.SetId, entry);
                }
            }

            return entries;
        }

        private static async Task SaveEstFile(EstType type, Dictionary<XivRace, Dictionary<ushort, ExtraSkeletonEntry>> entries, IItem referenceItem = null, ModTransaction tx = null)
        {
            var count = entries.Select(x => x.Value.Count).Aggregate((x, y) => x + y);

            var data = new byte[4 + (count * 6)];
            IOUtil.ReplaceBytesAt(data, BitConverter.GetBytes(count), 0);

            var races = entries.Keys.ToList();
            races.Sort();


            var index = 0;
            foreach (var race in races)
            {
                var sets = entries[race].Keys.ToList();
                sets.Sort();
                foreach (var set in sets)
                {
                    var entry = entries[race][set];
                    ExtraSkeletonEntry.Write(data, entry, count, index);
                    index++;
                }
            }


            var _dat = new Dat(_gameDirectory);
            await _dat.ImportType2Data(data, EstFiles[type], Constants.InternalModSourceName, referenceItem, tx);
        }

    }
}
