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
        /// Saves a given set of modified entries to the EST file.
        /// Entries with a SkeletonID of 0 will be removed.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="modifiedEntries"></param>
        /// <returns></returns>
        public static async Task SaveExtraSkeletonEntries(EstType type, List<ExtraSkeletonEntry> modifiedEntries)
        {
            var entries = await GetEstFile(type, false);

            // Add/Remove entries.
            foreach (var entry in modifiedEntries)
            {
                if (entry.SetId == 0)
                {
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
            await SaveEstFile(type, entries);
        }

        public static async Task<Dictionary<XivRace, ExtraSkeletonEntry>> GetExtraSkeletonEntries(IItem item, bool forceDefault = false)
        {
            if (item == null)
            {
                return new Dictionary<XivRace, ExtraSkeletonEntry>();
            }

            return await GetExtraSkeletonEntries(item.GetRoot(), forceDefault);
        }
        public static async Task<Dictionary<XivRace, ExtraSkeletonEntry>> GetExtraSkeletonEntries(XivDependencyRoot root, bool forceDefault = false)
        {
            if(root == null)
            {
                return new Dictionary<XivRace, ExtraSkeletonEntry>();
            }

            return await GetExtraSkeletonEntries(root.Info, forceDefault);
        }
        public static async Task<Dictionary<XivRace, ExtraSkeletonEntry>> GetExtraSkeletonEntries(XivDependencyRootInfo root, bool forceDefault = false)
        {
            var type = GetEstType(root);
            if(type == EstType.Invalid)
            {
                return new Dictionary<XivRace, ExtraSkeletonEntry>();
            }

            var id = (ushort)root.PrimaryId;

            return await GetExtraSkeletonEntries(type, id, forceDefault);
        }

        /// <summary>
        /// Retrieves the skeleton information for a given set for all races.
        /// </summary>
        /// <param name=""></param>
        /// <param name="setId"></param>
        /// <param name="forceDefault"></param>
        /// <returns></returns>
        public static async Task<Dictionary<XivRace, ExtraSkeletonEntry>> GetExtraSkeletonEntries(EstType type, ushort setId, bool forceDefault = false, bool includeNpcs = false)
        {
            if (type == EstType.Invalid)
            {
                return new Dictionary<XivRace, ExtraSkeletonEntry>();
            }

            var entries = await GetEstFile(type, forceDefault);
            
            var races = Eqp.DeformationAvailableRaces;
            if(includeNpcs)
            {
                races = Eqp.DeformationAvailableRacesWithNPCs;
            }

            var ret = new Dictionary<XivRace, ExtraSkeletonEntry>();

            foreach (var race in races)
            {
                var dict = entries[race];
                if (dict.ContainsKey(setId))
                {
                    ret.Add(race, dict[setId]);
                } else
                {
                    ret.Add(race, new ExtraSkeletonEntry());
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
        public static async Task<ExtraSkeletonEntry> GetExtraSkeletonEntry(EstType type, XivRace race, ushort setId, bool forceDefault = false)
        {
            
            var entries = await GetEstFile(type, forceDefault);

            if(!entries.ContainsKey(race)) {
                return new ExtraSkeletonEntry();
            }

            if(!entries[race].ContainsKey(setId))
            {
                return new ExtraSkeletonEntry();
            }

            return entries[race][setId];
        }

        private static async Task<Dictionary<XivRace, Dictionary<ushort, ExtraSkeletonEntry>>> GetEstFile(EstType type, bool forceDefault = false)
        {
            var _dat = new Dat(_gameDirectory);
            var data = await _dat.GetType2Data(EstFiles[type], forceDefault);

            var count = BitConverter.ToUInt32(data, 0);

            Dictionary<XivRace, Dictionary<ushort, ExtraSkeletonEntry>> entries = new Dictionary<XivRace, Dictionary<ushort, ExtraSkeletonEntry>>();
            for (int i = 0; i < count; i++)
            {
                var entry = ExtraSkeletonEntry.Read(data, count, (uint)i);
                if (!entries.ContainsKey(entry.Race))
                {
                    entries.Add(entry.Race, new Dictionary<ushort, ExtraSkeletonEntry>());
                }
                entries[entry.Race].Add(entry.SetId, entry);
            }

            return entries;
        }

        private static async Task SaveEstFile(EstType type, Dictionary<XivRace, Dictionary<ushort, ExtraSkeletonEntry>> entries)
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
            await _dat.ImportType2Data(data, "_EST_INTERNAL_", EstFiles[type], Constants.InternalMetaFileSourceName, Constants.InternalMetaFileSourceName);
        }

    }
}
