using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using static xivModdingFramework.Mods.TTMPWriter;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.DataContainers;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Mods.FileTypes.PMP;

namespace xivModdingFramework.Mods.FileTypes
{
    #region Enums
    // https://github.com/Ottermandias/Penumbra.GameData/blob/main/Enums/Race.cs
    // Penumbra Enums are all serialized as strings.

    [JsonConverter(typeof(StringEnumConverter))]
    public enum PMPObjectType : byte
    {
        Unknown,
        Vfx,
        DemiHuman,
        Accessory,
        World,
        Housing,
        Monster,
        Icon,
        LoadingScreen,
        Map,
        Interface,
        Equipment,
        Character,
        Weapon,
        Font,
    }
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PMPModelRace : byte
    {
        Unknown,
        Midlander,
        Highlander,
        Elezen,
        Lalafell,
        Miqote,
        Roegadyn,
        AuRa,
        Hrothgar,
        Viera,
    }
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PMPSubRace : byte
    {
        Unknown,
        Midlander,
        Highlander,
        Wildwood,
        Duskwight,
        Plainsfolk,
        Dunesfolk,
        SeekerOfTheSun,
        KeeperOfTheMoon,
        Seawolf,
        Hellsguard,
        Raen,
        Xaela,
        Helion,
        Lost,
        Rava,
        Veena,
    }
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PMPGender : byte
    {
        Unknown,
        Male,
        Female,
        MaleNpc,
        FemaleNpc,
    }
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PMPGenderRace : ushort
    {
        Unknown = 0,
        MidlanderMale = 0101,
        MidlanderMaleNpc = 0104,
        MidlanderFemale = 0201,
        MidlanderFemaleNpc = 0204,
        HighlanderMale = 0301,
        HighlanderMaleNpc = 0304,
        HighlanderFemale = 0401,
        HighlanderFemaleNpc = 0404,
        ElezenMale = 0501,
        ElezenMaleNpc = 0504,
        ElezenFemale = 0601,
        ElezenFemaleNpc = 0604,
        MiqoteMale = 0701,
        MiqoteMaleNpc = 0704,
        MiqoteFemale = 0801,
        MiqoteFemaleNpc = 0804,
        RoegadynMale = 0901,
        RoegadynMaleNpc = 0904,
        RoegadynFemale = 1001,
        RoegadynFemaleNpc = 1004,
        LalafellMale = 1101,
        LalafellMaleNpc = 1104,
        LalafellFemale = 1201,
        LalafellFemaleNpc = 1204,
        AuRaMale = 1301,
        AuRaMaleNpc = 1304,
        AuRaFemale = 1401,
        AuRaFemaleNpc = 1404,
        HrothgarMale = 1501,
        HrothgarMaleNpc = 1504,
        HrothgarFemale = 1601,
        HrothgarFemaleNpc = 1604,
        VieraMale = 1701,
        VieraMaleNpc = 1704,
        VieraFemale = 1801,
        VieraFemaleNpc = 1804,
        UnknownMaleNpc = 9104,
        UnknownFemaleNpc = 9204,
    }
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PMPEquipSlot : byte
    {
        Unknown = 0,
        MainHand = 1,
        OffHand = 2,
        Head = 3,
        Body = 4,
        Hands = 5,
        Belt = 6,
        Legs = 7,
        Feet = 8,
        Ears = 9,
        Neck = 10,
        Wrists = 11,
        RFinger = 12,
        BothHand = 13,
        LFinger = 14, // Not officially existing, means "weapon could be equipped in either hand" for the game.
        HeadBody = 15,
        BodyHandsLegsFeet = 16,
        SoulCrystal = 17,
        LegsFeet = 18,
        FullBody = 19,
        BodyHands = 20,
        BodyLegsFeet = 21,
        ChestHands = 22,
        Nothing = 23,
        All = 24, // Not officially existing
    }
    [JsonConverter(typeof(StringEnumConverter))]
    public enum RspAttribute : byte
    {
        MaleMinSize,
        MaleMaxSize,
        MaleMinTail,
        MaleMaxTail,
        FemaleMinSize,
        FemaleMaxSize,
        FemaleMinTail,
        FemaleMaxTail,
        BustMinX,
        BustMinY,
        BustMinZ,
        BustMaxX,
        BustMaxY,
        BustMaxZ,
        NumAttributes,
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum GlobalEqpType
    {
        DoNotHideEarrings,
        DoNotHideNecklace,
        DoNotHideBracelets,
        DoNotHideRingR,
        DoNotHideRingL,
        DoNotHideHrothgarHats,
        DoNotHideVieraHats
    }


    #endregion

    #region Extensions

    public static class PMPExtensions
    {
        public static Dictionary<string, string> PenumbraTypeToGameType = new Dictionary<string, string>();


        /// <summary>
        /// Incomplete listing, only used for IMC-using types.
        /// </summary>
        public static Dictionary<XivItemType, PMPObjectType> XivItemTypeToPenumbraObject = new Dictionary<XivItemType, PMPObjectType>()
        {
            { XivItemType.weapon, PMPObjectType.Weapon },
            { XivItemType.equipment, PMPObjectType.Equipment },
            { XivItemType.accessory, PMPObjectType.Accessory },
            { XivItemType.demihuman, PMPObjectType.DemiHuman },
            { XivItemType.monster, PMPObjectType.Monster },
        };

        public static PmpIdentifierJson GetPenumbraIdentifierFromRoot(XivDependencyRoot root, int variant = 1)
        {
            return GetPenumbraIdentifierFromRoot(root.Info, variant);
        }
        public static PmpIdentifierJson GetPenumbraIdentifierFromRoot(XivDependencyRootInfo root, int variant = 1)
        {
            return PmpIdentifierJson.FromRoot(root, variant);
        }
        public static XivDependencyRootInfo GetRootFromPenumbraValues(PmpIdentifierJson identifier)
        {
            return identifier.GetRoot().Info;
        }
        public static XivDependencyRootInfo GetRootFromPenumbraValues(PMPObjectType objectType, uint primaryId, PMPObjectType bodySlot, uint secondaryId, PMPEquipSlot slot)
        {
            var info = new XivDependencyRootInfo();
            info.PrimaryId = (int)primaryId;
            info.SecondaryId = (int)secondaryId;

            if (objectType != PMPObjectType.Unknown)
            {
                info.PrimaryType = XivItemTypes.FromSystemName(objectType.ToString().ToLower());
            }

            if (bodySlot != PMPObjectType.Unknown)
            {
                info.SecondaryType = XivItemTypes.FromSystemName(bodySlot.ToString().ToLower());
            }

            if (PenumbraSlotToGameSlot.ContainsKey(slot))
            {
                info.Slot = PenumbraSlotToGameSlot[slot];
            } else
            {
                info.Slot = null;
            }


            return info;
        }

        // We only really care about the ones used in IMC entries here.
        public static Dictionary<PMPEquipSlot, string> PenumbraSlotToGameSlot = new Dictionary<PMPEquipSlot, string>()
        {
            // Main
            { PMPEquipSlot.Head, "met" },
            { PMPEquipSlot.Body, "top" },
            { PMPEquipSlot.Hands, "glv" },
            { PMPEquipSlot.Legs, "dwn" },
            { PMPEquipSlot.Feet, "sho" },

            // Accessory
            { PMPEquipSlot.Ears, "ear" },
            { PMPEquipSlot.Neck, "nek" },
            { PMPEquipSlot.Wrists, "wrs" },
            { PMPEquipSlot.RFinger, "rir" },
            { PMPEquipSlot.LFinger, "ril" },
        };
        // We only really care about the ones used in IMC entries here.
        public static bool IsAccessory(PMPEquipSlot slot)
        {
            if(slot == PMPEquipSlot.Ears
                || slot == PMPEquipSlot.Neck
                || slot == PMPEquipSlot.Wrists
                || slot == PMPEquipSlot.RFinger
                || slot == PMPEquipSlot.LFinger
                )
            {
                return true;
            }
            return false;
        }

        public static XivRace GetRaceFromPenumbraValue(PMPModelRace race, PMPGender gender)
        {
            // We can get a little cheesy here.
            var rGenderSt = race.ToString() + gender.ToString();
            var rGender = Enum.Parse(typeof(PMPGenderRace), rGenderSt);
            var intVal = (int)((ushort)rGender);

            // Can just direct cast this now.
            return (XivRace)intVal;
        }

        public static (PMPModelRace Race, PMPGender Gender) GetPMPRaceGenderFromXivRace(XivRace xivRace)
            => xivRace switch
            {
                XivRace.Hyur_Midlander_Male => (PMPModelRace.Midlander, PMPGender.Male),
                XivRace.Hyur_Midlander_Female => (PMPModelRace.Midlander, PMPGender.Female),

                XivRace.Hyur_Highlander_Male => (PMPModelRace.Highlander, PMPGender.Male),
                XivRace.Hyur_Highlander_Female => (PMPModelRace.Highlander, PMPGender.Female),

                XivRace.Elezen_Male => (PMPModelRace.Elezen, PMPGender.Male),
                XivRace.Elezen_Female => (PMPModelRace.Elezen, PMPGender.Female),

                XivRace.Roegadyn_Male => (PMPModelRace.Roegadyn, PMPGender.Male),
                XivRace.Roegadyn_Female => (PMPModelRace.Roegadyn, PMPGender.Female),

                XivRace.Miqote_Male => (PMPModelRace.Miqote, PMPGender.Male),
                XivRace.Miqote_Female => (PMPModelRace.Miqote, PMPGender.Female),

                XivRace.Lalafell_Male => (PMPModelRace.Lalafell, PMPGender.Male),
                XivRace.Lalafell_Female => (PMPModelRace.Lalafell, PMPGender.Female),

                XivRace.AuRa_Male => (PMPModelRace.AuRa, PMPGender.Male),
                XivRace.AuRa_Female => (PMPModelRace.AuRa, PMPGender.Female),

                XivRace.Viera_Male => (PMPModelRace.Viera, PMPGender.Male),
                XivRace.Viera_Female => (PMPModelRace.Viera, PMPGender.Female),

                XivRace.Hrothgar_Male => (PMPModelRace.Hrothgar, PMPGender.Male),
#if DAWNTRAIL
                XivRace.Hrothgar_Female => (PMPModelRace.Hrothgar, PMPGender.Female),
#endif

                _ => (PMPModelRace.Unknown, PMPGender.Unknown)
            };


        public static Dictionary<PMPGender, XivGender> PMPGenderToXivGender = new Dictionary<PMPGender, XivGender>()
        {
            { PMPGender.Male, XivGender.Male },
            { PMPGender.Female, XivGender.Female }
        };

        public static Dictionary<PMPSubRace, XivSubRace> PMPSubraceToXivSubrace = new Dictionary<PMPSubRace, XivSubRace>()
        {
            { PMPSubRace.Midlander, XivSubRace.Hyur_Midlander },
            { PMPSubRace.Highlander, XivSubRace.Hyur_Highlander },

            { PMPSubRace.SeekerOfTheSun, XivSubRace.Miqote_Seeker },
            { PMPSubRace.KeeperOfTheMoon, XivSubRace.Miqote_Keeper },

            { PMPSubRace.Wildwood, XivSubRace.Elezen_Wildwood },
            { PMPSubRace.Duskwight, XivSubRace.Elezen_Duskwight },

            { PMPSubRace.Seawolf, XivSubRace.Roegadyn_SeaWolf},
            { PMPSubRace.Hellsguard, XivSubRace.Roegadyn_Hellsguard },

            { PMPSubRace.Dunesfolk, XivSubRace.Lalafell_Dunesfolk },
            { PMPSubRace.Plainsfolk, XivSubRace.Lalafell_Plainsfolk },

            { PMPSubRace.Raen, XivSubRace.AuRa_Raen },
            { PMPSubRace.Xaela, XivSubRace.AuRa_Xaela },

            { PMPSubRace.Veena, XivSubRace.Viera_Veena },
            { PMPSubRace.Rava, XivSubRace.Viera_Rava },

            { PMPSubRace.Lost, XivSubRace.Hrothgar_Lost },
            { PMPSubRace.Helion, XivSubRace.Hrothgar_Helion },
        };


        public static PMPGender ToPMPGender(this RspAttribute attribute)
            => attribute switch
            {
                RspAttribute.MaleMinSize => PMPGender.Male,
                RspAttribute.MaleMaxSize => PMPGender.Male,
                RspAttribute.MaleMinTail => PMPGender.Male,
                RspAttribute.MaleMaxTail => PMPGender.Male,
                RspAttribute.FemaleMinSize => PMPGender.Female,
                RspAttribute.FemaleMaxSize => PMPGender.Female,
                RspAttribute.FemaleMinTail => PMPGender.Female,
                RspAttribute.FemaleMaxTail => PMPGender.Female,
                RspAttribute.BustMinX => PMPGender.Female,
                RspAttribute.BustMinY => PMPGender.Female,
                RspAttribute.BustMinZ => PMPGender.Female,
                RspAttribute.BustMaxX => PMPGender.Female,
                RspAttribute.BustMaxY => PMPGender.Female,
                RspAttribute.BustMaxZ => PMPGender.Female,
                _ => PMPGender.Unknown,
            };

        public static List<PMPManipulationWrapperJson> RgspToManipulations(RacialGenderScalingParameter rgsp)
        {
            var ret = new List<PMPManipulationWrapperJson>();
            var entries = PMPRspManipulationJson.FromRgspEntry(rgsp);
            foreach (var e in entries)
            {
                var entry = new PMPRspManipulationWrapperJson() { Type = "Rsp" };
                entry.SetManipulation(e);
                ret.Add(entry);
            }
            return ret;
        }
        public static List<PMPManipulationWrapperJson> MetadataToManipulations(ItemMetadata m)
        {
            var ret = new List<PMPManipulationWrapperJson>();
            var root = m.Root.Info;

            if (m.GmpEntry != null)
            {
                var entry = new PMPGmpManipulationWrapperJson() { Type = "Gmp" };
                entry.SetManipulation(PMPGmpManipulationJson.FromGmpEntry(m.GmpEntry, root));
                ret.Add(entry);
            }

            if (m.EqpEntry != null)
            {
                var entry = new PMPEqpManipulationWrapperJson() { Type = "Eqp" };
                entry.SetManipulation(PMPEqpManipulationJson.FromEqpEntry(m.EqpEntry, root));
                ret.Add(entry);
            }

            if (m.EstEntries != null && m.EstEntries.Count > 0)
            {
                foreach (var est in m.EstEntries)
                {
                    var entry = new PMPEstManipulationWrapperJson() { Type = "Est" };
                    entry.SetManipulation(PMPEstManipulationJson.FromEstEntry(est.Value, root.Slot));
                    ret.Add(entry);
                }
            }

            if (m.EqdpEntries != null && m.EqdpEntries.Count > 0)
            {
                foreach (var eqdp in m.EqdpEntries)
                {
                    var entry = new PMPEqdpManipulationWrapperJson() { Type = "Eqdp" };
                    entry.SetManipulation(PMPEqdpManipulationJson.FromEqdpEntry(eqdp.Value, root, eqdp.Key));
                    ret.Add(entry);
                }
            }

            if (m.ImcEntries != null && m.ImcEntries.Count > 0)
            {
                for (int i = 0; i < m.ImcEntries.Count; i++)
                {
                    var entry = new PMPImcManipulationWrapperJson() { Type = "Imc" };
                    entry.SetManipulation(PMPImcManipulationJson.FromImcEntry(m.ImcEntries[i], i, root));
                    ret.Add(entry);
                }
            }

            return ret;
        }


        /// <summary>
        /// Resolves duplicate files and assigns PMP zip paths to all of the file Identifiers.
        /// </summary>
        /// <param name="files"></param>
        /// <param name="defaultStorageType"></param>
        /// <returns></returns>
        internal static async Task ResolveDuplicates(Dictionary<Guid, FileIdentifier> files, EFileStorageType defaultStorageType = EFileStorageType.CompressedIndividual)
        {
            using var sha1 = SHA1.Create();

            // Id => Sha Key
            var guidHashDict = new Dictionary<Guid, TTMPWriter.SHA1HashKey>();

            // Sha Key => Out File.
            var seenFiles = new Dictionary<TTMPWriter.SHA1HashKey, string>();

            var idx = 1;
            foreach (var fkv in files)
            {
                var f = fkv.Value;
                var path = f.Path;
                var info = f.Info;
                var id = f.Id;

                if (!File.Exists(f.Info.RealPath))
                {
                    // Sometimes badly behaved penumbra folders can be loaded and re-written that never actually had some of the files in question.
                    guidHashDict.Add(id, new SHA1HashKey());
                    continue;
                }

                byte[] data;
                if (defaultStorageType == EFileStorageType.CompressedIndividual || defaultStorageType == EFileStorageType.CompressedBlob)
                {
                    // Which we use here doesn't ultimately matter, but one will be faster than the other, depending on the way *most* files were stored.
                    data = await TransactionDataHandler.GetCompressedFile(info);
                }
                else
                {
                    data = await TransactionDataHandler.GetUncompressedFile(info);
                }

                var dedupeHash = new SHA1HashKey(sha1.ComputeHash(data));
                var pmpPath = f.OptionPrefix + path;

                if (seenFiles.ContainsKey(dedupeHash))
                {
                    // Shift the target path into the common folder if we're used in multiple places.
                    if (!seenFiles[dedupeHash].StartsWith("common/"))
                    {
                        seenFiles[dedupeHash] = "common/" + idx.ToString() + "/" + Path.GetFileName(seenFiles[dedupeHash]);
                        idx++;
                    }
                }
                else
                {
                    seenFiles.Add(dedupeHash, pmpPath);
                }
                pmpPath = seenFiles[dedupeHash];
                guidHashDict.Add(id, dedupeHash);
            }


            // Re-loop to assign the final paths.
            // Could do this more efficiently, but whatever.  Perf impact is de minimis.
            foreach (var fkv in files)
            {
                var hash = guidHashDict[fkv.Value.Id];
                if (!seenFiles.ContainsKey(hash))
                {
                    continue;
                }
                var pmpPath = seenFiles[hash];
                files[fkv.Key].PmpPath = pmpPath;
            }
        }
    }
    public class FileIdentifier
    {
        public FileStorageInformation Info;
        public string Path;
        public string PmpPath;
        public Guid Id = Guid.NewGuid();
        public string OptionPrefix = "";

        public static async Task<Dictionary<string, List<FileIdentifier>>> IdentifierListFromDictionaries(Dictionary<string, Dictionary<string, FileStorageInformation>> files)
        {
            var totalRefs = files.Sum(x => x.Value.Count);
            var l1Dict = new Dictionary<Guid, FileIdentifier>(totalRefs);
            foreach (var oKv in files)
            {
                var optionPrefix = oKv.Key;
                foreach (var fKv in oKv.Value)
                {
                    var fi = new FileIdentifier()
                    {
                        Path = fKv.Key,
                        Info = fKv.Value,
                        OptionPrefix = optionPrefix
                    };
                    l1Dict.Add(fi.Id, fi);
                }
            }

            await PMPExtensions.ResolveDuplicates(l1Dict);

            var l2 = new Dictionary<string, List<FileIdentifier>>();
            foreach (var id in l1Dict.Values)
            {
                if (!l2.ContainsKey(id.OptionPrefix))
                {
                    l2.Add(id.OptionPrefix, new List<FileIdentifier>());
                }
                l2[id.OptionPrefix].Add(id);
            }

            return l2;
        }
        public static async Task<List<FileIdentifier>> IdentifierListFromDictionary(Dictionary<string, FileStorageInformation> files, string optionPrefix = "")
        {
            var dict = new Dictionary<Guid, FileIdentifier>(files.Count);
            foreach (var f in files)
            {
                var fi = new FileIdentifier()
                {
                    Path = f.Key,
                    Info = f.Value,
                    OptionPrefix = optionPrefix,
                };
                dict.Add(fi.Id, fi);
            }

            await PMPExtensions.ResolveDuplicates(dict);
            return dict.Values.ToList();
        }
    }
    #endregion
}
