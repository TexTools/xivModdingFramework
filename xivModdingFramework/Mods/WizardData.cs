using SixLabors.ImageSharp.Formats.Png;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using static xivModdingFramework.Mods.FileTypes.TTMP;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.DataContainers;
using xivModdingFramework.General;
using xivModdingFramework.Helpers;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.FileTypes.PMP;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Variants.DataContainers;
using xivModdingFramework.Variants.FileTypes;
using Image = SixLabors.ImageSharp.Image;

namespace xivModdingFramework.Mods
{
    public enum EOptionType
    {
        Single,
        Multi
    };

    public enum EGroupType
    {
        Standard,
        Imc
    };

    internal static class WizardHelpers
    {
        public static string WriteImage(string currentPath, string tempFolder, string newName)
        {

            if (string.IsNullOrWhiteSpace(currentPath))
            {
                return "";
            }

            if (!File.Exists(currentPath))
            {
                return "";
            }

            var path = Path.Combine("images", newName + ".png");

            var img = SixLabors.ImageSharp.Image.Load(currentPath);
            var fName = Path.Combine(tempFolder, path);
            var dir = Path.GetDirectoryName(fName);
            Directory.CreateDirectory(dir);

            using var fs = File.OpenWrite(fName);
            var enc = new PngEncoder();
            enc.BitDepth = PngBitDepth.Bit16;
            img.Save(fs, enc);

            return path;
        }

    }

    public class WizardStandardOptionData : WizardOptionData
    {
        public Dictionary<string, FileStorageInformation> Files = new Dictionary<string, FileStorageInformation>();

        public List<PMPManipulationWrapperJson> Manipulations = new List<PMPManipulationWrapperJson>();

        public int Priority;

        protected override bool CheckHasData()
        {
            return Files.Count > 0 || Manipulations.Count > 0;
        }

        private static List<string> ManipulationTypeSortOrder = new List<string>()
        {
            "Imc",
            "Eqp",
            "Eqdp",
            "Est",
            "Gmp",
            "Rsp",
            "Atch",
            "GlobalEqp"
        };

        public void SortManipulations()
        {
            if(Manipulations == null)
            {
                Manipulations = new List<PMPManipulationWrapperJson>();
            }

            Manipulations.Sort((a, b) =>
            {
                if (a == null || b == null)
                {
                    return -1;
                }
                var aType = ManipulationTypeSortOrder.IndexOf(a.Type);
                var bType = ManipulationTypeSortOrder.IndexOf(b.Type);

                aType = aType < 0 ? int.MaxValue : aType;
                bType = bType < 0 ? int.MaxValue : bType;

                if (aType != bType)
                {
                    return aType - bType;
                }

                if (a.Type == "Rsp")
                {
                    var aRsp = a.GetManipulation() as PMPRspManipulationJson;
                    var bRsp = b.GetManipulation() as PMPRspManipulationJson;

                    var aVal = (int)aRsp.GetRaceGenderHash();
                    var bVal = (int)bRsp.GetRaceGenderHash();

                    aVal = aVal * 100 + (int)aRsp.Attribute;
                    bVal = bVal * 100 + (int)aRsp.Attribute;
                    return aVal - bVal;
                }

                var aIm = a.GetManipulation() as IPMPItemMetadata;
                var bIm = b.GetManipulation() as IPMPItemMetadata;

                if (aIm == null)
                {
                    // No defined order for non Item, Non-Rsp types.
                    return 0;
                }

                // Sort by root information.
                var diff = GetSortOrder(aIm) - GetSortOrder(bIm);
                if (diff != 0 || a.Type != "Imc")
                {
                    return diff;
                }

                var aImc = a.GetManipulation() as PMPImcManipulationJson;
                var bImc = b.GetManipulation() as PMPImcManipulationJson;

                if (aImc == null)
                {
                    return 0;
                }

                return (int)aImc.Variant - (int)bImc.Variant;
            });
        }

        private static int GetSortOrder(IPMPItemMetadata manipulation)
        {
            //Generic sort-order resolver for root using manipulations.
            var root = manipulation.GetRoot();

            // 6x shift
            var val = (int)root.Info.PrimaryType * 1000000;

            // 2x shift
            val += root.Info.PrimaryId * 100;

            // 0x shift
            if (root.Info.Slot == null || !Imc.SlotOffsetDictionary.ContainsKey(root.Info.Slot))
            {
                val += 0;
            }
            else
            {
                val += (Imc.SlotOffsetDictionary.Keys.ToList().IndexOf(root.Info.Slot) + 1);
            }

            return val;
        }
    }

    public class WizardImcOptionData : WizardOptionData
    {
        public bool IsDisableOption;
        public ushort AttributeMask;

        protected override bool CheckHasData()
        {
            if (AttributeMask > 0 || IsDisableOption)
            {
                return true;
            }
            return false;
        }
    }

    public class WizardOptionData
    {

        public bool HasData
        {
            get
            {
                return CheckHasData();
            }
        }

        protected virtual bool CheckHasData()
        {
            return false;
        }

    }

    /// <summary>
    /// Class representing a single, clickable [Option],
    /// Aka a Radio Button or Checkbox the user can select, that internally resolves
    /// to a single list of files to be imported.
    /// </summary>
    public class WizardOptionEntry : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Image { get; set; } = "";
        internal string FolderPath { get; set; }

        public string NoDataIndicator
        {
            get
            {
                if (HasData)
                {
                    return "";
                }

                if (!_Group.Options.Any(x => x.HasData))
                {
                    // Group needs valid data.
                    return " (Empty)";
                }

                if (OptionType == EOptionType.Single)
                {
                    if (_Group.Options.FirstOrDefault(x => !x.HasData) == this)
                    {
                        // First empty is preserved in single select.
                        return "";
                    }
                }

                return " (Empty)";
            }
        }

        public bool HasData
        {
            get
            {

                if (_Group.ModOption != null)
                {
                    // Read mode.
                    return true;
                }

                if (StandardData != null)
                {
                    return StandardData.HasData;
                }
                else if (ImcData != null)
                {
                    return ImcData.HasData;
                }
                return false;
            }
        }


        private bool _Selected;
        public bool Selected
        {
            get
            {
                return _Selected;
            }
            set
            {
                if (_Selected == value) return;
                _Selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));

                var index = _Group.Options.IndexOf(this);
                if (index < 0) return;

                if (GroupType == EGroupType.Imc && Selected == true)
                {
                    if (ImcData.IsDisableOption)
                    {
                        foreach (var opt in _Group.Options)
                        {
                            if (!opt.ImcData.IsDisableOption)
                            {
                                opt.Selected = false;
                            }
                        }
                    }
                    else
                    {
                        foreach (var opt in _Group.Options)
                        {
                            if (opt.ImcData.IsDisableOption)
                            {
                                opt.Selected = false;
                            }
                        }
                    }
                }
            }
        }

        private WizardGroupEntry _Group;

        // Group name is used by the UI template binding for establishing radio button groupings.
        public string GroupName
        {
            get
            {
                return _Group.Name;
            }
        }

        // Option type is used by the UI template binding to determine template type.
        public EOptionType OptionType
        {
            get
            {
                return _Group.OptionType;
            }
        }
        public EGroupType GroupType
        {
            get
            {
                return _Group.GroupType;
            }
        }

        private WizardOptionData _Data = new WizardStandardOptionData();

        public WizardImcOptionData ImcData
        {
            get
            {
                if (GroupType == EGroupType.Imc)
                {
                    return _Data as WizardImcOptionData;
                }
                else
                {
                    return null;
                }
            }
            set
            {
                if (GroupType == EGroupType.Imc)
                {
                    _Data = value;
                }
            }
        }

        public WizardStandardOptionData StandardData
        {
            get
            {
                if (GroupType == EGroupType.Standard)
                {
                    return _Data as WizardStandardOptionData;
                }
                else
                {
                    return null;
                }
            }
            set
            {
                if (GroupType == EGroupType.Standard)
                {
                    _Data = value;
                }
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;

        public WizardOptionEntry(WizardGroupEntry owningGroup)
        {
            _Group = owningGroup;
        }

        public async Task<ModOption> ToModOption()
        {
            Image img = null;
            if (!string.IsNullOrWhiteSpace(Image))
            {
                img = SixLabors.ImageSharp.Image.Load(Image);
            }

            var mo = new ModOption()
            {
                Description = Description,
                Name = Name,
                GroupName = _Group.Name,
                ImageFileName = Image,
                IsChecked = Selected,
                SelectionType = OptionType.ToString(),
                Image = img,
            };

            if (StandardData == null)
            {
                throw new NotImplementedException("TTMP Export does not support one or more of the selected Option types.");
            }

            foreach (var fkv in StandardData.Files)
            {
                var path = fkv.Key;
                var forceType2 = path.EndsWith(".atex");
                if (!File.Exists(fkv.Value.RealPath))
                {
                    // Sometimes poorly behaved PMPs or Penumbra folders may have been used as a source,
                    // where they are missing files that they claim to have.
                    continue;
                }
                var data = await TransactionDataHandler.GetCompressedFile(fkv.Value, forceType2);

                var root = await XivCache.GetFirstRoot(path);
                var itemCategory = "Unknown";
                var itemName = "Unknown";
                if (root != null)
                {
                    var item = root.GetFirstItem();
                    if (item != null)
                    {
                        itemCategory = item.SecondaryCategory;
                        itemName = item.Name;
                    }
                }

                var mData = new ModData()
                {
                    Name = itemName,
                    Category = itemCategory,
                    FullPath = path,
                    ModDataBytes = data,
                };
                mo.Mods.Add(path, mData);
            }

            if (StandardData.Manipulations != null && StandardData.Manipulations.Count > 0)
            {
                // Readonly TX for retrieving base values.
                var tx = ModTransaction.BeginReadonlyTransaction();
                var manips = await PMP.ManipulationsToMetadata(this.StandardData.Manipulations, tx);

                foreach (var meta in manips.Metadatas)
                {
                    // Need to convert these and add them to the file array.
                    var item = meta.Root.GetFirstItem();
                    var path = meta.Root.Info.GetRootFile();
                    var mData = new ModData()
                    {
                        Name = item.Name,
                        Category = item.SecondaryCategory,
                        FullPath = path,
                        ModDataBytes = await SmartImport.CreateCompressedFile(await ItemMetadata.Serialize(meta), true),
                    };
                    mo.Mods.Add(path, mData);
                }

                foreach (var rgsp in manips.Rgsps)
                {
                    // Need to convert these and add them to the file array.
                    var data = await SmartImport.CreateCompressedFile(rgsp.GetBytes(), true);
                    var path = CMP.GetRgspPath(rgsp);
                    var item = CMP.GetDummyItem(rgsp);

                    var mData = new ModData()
                    {
                        Name = item.Name,
                        Category = item.SecondaryCategory,
                        FullPath = path,
                        ModDataBytes = data,
                    };
                    mo.Mods.Add(path, mData);
                }

                if (manips.OtherManipulations.Count > 0)
                {
                    var manip0 = manips.OtherManipulations[0];
                    throw new InvalidDataException("TTMP Does not support " + manip0.Type + " manipulations.");
                }
            }


            return mo;
        }

        public async Task<PMPOptionJson> ToPmpOption(string tempFolder, IEnumerable<FileIdentifier> identifiers, string imageName)
        {
            PMPOptionJson op;
            if (GroupType == EGroupType.Imc)
            {
                var io = new PmpImcOptionJson();
                op = io;
                if (!ImcData.IsDisableOption)
                    io.AttributeMask = ImcData.AttributeMask;
                else
                    io.IsDisableSubMod = true;
            }
            else
            {
                PmpStandardOptionJson so;

                if (OptionType == EOptionType.Multi)
                {
                    var mo = new PmpMultiOptionJson();
                    so = mo;
                    mo.Priority = StandardData.Priority;
                }
                else
                {
                    so = new PmpSingleOptionJson();
                }
                // This unpacks our deduplicated files as needed.
                await PMP.PopulatePmpStandardOption(so, tempFolder, identifiers, StandardData.Manipulations);
                op = so;
            }

            op.Name = Name ?? "";
            op.Description = Description ?? "";
            op.Image = WizardHelpers.WriteImage(Image, tempFolder, imageName);

            return op;
        }
    }

    public class WizardImcGroupData
    {
        public XivDependencyRoot Root = new XivDependencyRoot(new XivDependencyRootInfo()
        {
            PrimaryType = xivModdingFramework.Items.Enums.XivItemType.equipment,
            PrimaryId = 1,
            Slot = "top"
        });
        public ushort Variant;
        public XivImc BaseEntry = new XivImc();
        public bool AllVariants;
        public bool OnlyAttributes;
    }

    /// <summary>
    /// Class represnting a Group of options.
    /// Aka a collection of radio buttons or checkboxes.
    /// </summary>
    public class WizardGroupEntry
    {
        public string Name = "";
        public string Description = "";
        public string Image = "";

        internal string FolderPath;

        // Int or Bitflag depending on OptionType.
        public ulong Selection
        {
            get
            {
                if (this.OptionType == EOptionType.Single)
                {
                    var op = Options.FirstOrDefault(x => x.Selected);
                    if (op == null)
                    {
                        return 0;
                    }
                    return (ulong)Options.IndexOf(op);
                }
                else
                {
                    ulong total = 0;
                    for (int i = 0; i < Options.Count; i++)
                    {
                        if (Options[i].Selected)
                        {
                            var bit = 1UL << i;
                            total |= bit;
                        }
                    }
                    return total;
                }
            }
        }

        public EOptionType OptionType;

        public EGroupType GroupType
        {
            get
            {
                if (ImcData != null)
                {
                    return EGroupType.Imc;
                }
                return EGroupType.Standard;
            }
        }

        public bool HasData
        {
            get
            {
                return Options.Any(x => x.HasData);
            }
        }


        public List<WizardOptionEntry> Options = new List<WizardOptionEntry>();

        /// <summary>
        /// Option Data for Penumbra style Imc-Mask Option Groups.
        /// </summary>
        public WizardImcGroupData ImcData = null;

        public int Priority;

        /// <summary>
        /// Handler to the base modpack option.
        /// Typically either a ModGroupJson or PMPGroupJson
        /// </summary>
        public object ModOption;

        public static async Task<WizardGroupEntry> FromWizardGroup(ModGroupJson tGroup, string unzipPath, bool needsTexFix)
        {
            var group = new WizardGroupEntry();
            group.Options = new List<WizardOptionEntry>();
            group.ModOption = tGroup;

            group.Name = tGroup.GroupName;
            group.OptionType = tGroup.SelectionType == "Single" ? EOptionType.Single : EOptionType.Multi;

            var mpdPath = Path.Combine(unzipPath, "TTMPD.mpd");

            foreach (var o in tGroup.OptionList)
            {
                var wizOp = new WizardOptionEntry(group);
                wizOp.Name = o.Name;
                wizOp.Description = o.Description;
                if (!String.IsNullOrWhiteSpace(o.ImagePath))
                {
                    wizOp.Image = Path.Combine(unzipPath, o.ImagePath);
                }
                wizOp.Selected = o.IsChecked;

                var data = new WizardStandardOptionData();

                foreach (var mj in o.ModsJsons)
                {
                    var finfo = new FileStorageInformation()
                    {
                        StorageType = EFileStorageType.CompressedBlob,
                        FileSize = mj.ModSize,
                        RealOffset = mj.ModOffset,
                        RealPath = mpdPath
                    };

                    // Data may not be unzipped here if we're in import mode.
                    if (File.Exists(finfo.RealPath))
                    {
                        if (mj.FullPath.EndsWith(".meta") || mj.FullPath.EndsWith(".rgsp"))
                        {
                            var raw = await TransactionDataHandler.GetUncompressedFile(finfo);
                            if (mj.FullPath.EndsWith(".meta"))
                            {
                                var meta = await ItemMetadata.Deserialize(raw);
                                data.Manipulations.AddRange(PMPExtensions.MetadataToManipulations(meta));
                            }
                            else
                            {
                                var rgsp = new RacialGenderScalingParameter(raw);
                                data.Manipulations.AddRange(PMPExtensions.RgspToManipulations(rgsp));
                            }
                        }
                        else
                        {
                            if (needsTexFix && mj.FullPath.EndsWith(".tex"))
                            {
                                try
                                {
                                    finfo = await TTMP.FixOldTexData(finfo);
                                }
                                catch (Exception ex)
                                {
                                    Trace.WriteLine(ex);
                                    // File majorly broken, skip it.
                                    continue;
                                }
                            }
                            else if (needsTexFix && mj.FullPath.EndsWith(".mdl"))
                            {
                                try
                                {
                                    // Have to fix old busted models.
                                    finfo = await EndwalkerUpgrade.FixOldModel(finfo);
                                }
                                catch (Exception ex)
                                {
                                    Trace.WriteLine(ex);
                                    // Hmm... What should we do about this?
                                    // Skip the file?
                                    continue;
                                }
                            }
                            if (data.Files.ContainsKey(mj.FullPath))
                            {

                                data.Files[mj.FullPath] = finfo;
                            }
                            else
                            {
                                data.Files.Add(mj.FullPath, finfo);
                            }
                        }
                    }
                }

                wizOp.StandardData = data;



                group.Options.Add(wizOp);
            }

            if (group.Options.Count == 0)
            {
                // Empty group.
                return null;
            }

            if (group.OptionType == EOptionType.Single && !group.Options.Any(x => x.Selected))
            {
                group.Options[0].Selected = true;
            }

            return group;
        }

        public static async Task<WizardGroupEntry> FromPMPGroup(PMPGroupJson pGroup, string unzipPath)
        {
            var group = new WizardGroupEntry();
            group.Options = new List<WizardOptionEntry>();
            group.ModOption = pGroup;

            group.OptionType = pGroup.Type == "Single" ? EOptionType.Single : EOptionType.Multi;
            group.Name = pGroup.Name;
            group.Priority = pGroup.Priority;

            if (!string.IsNullOrWhiteSpace(pGroup.Image))
            {
                group.Image = Path.Combine(unzipPath, pGroup.Image);
            }
            else
            {
                group.Image = "";
            }

            group.Description = pGroup.Description;

            var imcGroup = pGroup as PMPImcGroupJson;
            if (imcGroup != null)
            {
                group.ImcData = new WizardImcGroupData()
                {
                    Variant = imcGroup.Identifier.Variant,
                    Root = imcGroup.Identifier.GetRoot(),
                    BaseEntry = imcGroup.DefaultEntry.ToXivImc(),
                    AllVariants = imcGroup.AllVariants,
                    OnlyAttributes = imcGroup.OnlyAttributes,
                };
            }

            var idx = 0;
            foreach (var o in pGroup.Options)
            {
                var wizOp = new WizardOptionEntry(group);
                wizOp.Name = o.Name;
                wizOp.Description = o.Description;
                wizOp.Image = null;

                if (group.OptionType == EOptionType.Single)
                {
                    wizOp.Selected = pGroup.DefaultSettings == (ulong)idx;
                }
                else
                {
                    var bit = 1UL << idx;
                    wizOp.Selected = (pGroup.DefaultSettings & bit) != 0;
                }
                group.Options.Add(wizOp);

                if (group.GroupType == EGroupType.Standard)
                {
                    var data = await PMP.UnpackPmpOption(o, null, unzipPath, false);
                    wizOp.StandardData.Files = data.Files;
                    wizOp.StandardData.Manipulations = data.OtherManipulations;
                    var mop = o as PmpMultiOptionJson;
                    if (mop != null)
                    {
                        wizOp.StandardData.Priority = mop.Priority;
                    }

                }
                else if (group.GroupType == EGroupType.Imc)
                {
                    var imcData = new WizardImcOptionData();
                    var imcOp = o as PmpImcOptionJson;
                    if (!imcOp.IsDisableSubMod)
                        imcData.AttributeMask = imcOp.AttributeMask;
                    else
                        imcData.IsDisableOption = imcOp.IsDisableSubMod;
                    wizOp.ImcData = imcData;
                }

                if (!string.IsNullOrWhiteSpace(o.Image))
                {
                    wizOp.Image = Path.Combine(unzipPath, o.Image);
                }
                else
                {
                    wizOp.Image = "";
                }

                idx++;
            }

            if (group.Options.Count == 0)
            {
                // Empty group.
                return null;
            }

            if (group.OptionType == EOptionType.Single && !group.Options.Any(x => x.Selected))
            {
                group.Options[0].Selected = true;
            }


            return group;
        }

        public async Task<ModGroup> ToModGroup()
        {
            if (this.ImcData != null)
            {
                throw new InvalidDataException("TTMP Does not support IMC Groups.");
            }

            var mg = new ModGroup()
            {
                GroupName = Name,
                OptionList = new List<ModOption>(),
                SelectionType = OptionType.ToString(),
            };

            foreach (var option in Options)
            {
                var tOpt = await option.ToModOption();
                mg.OptionList.Add(tOpt);
            }

            return mg;
        }

        public async Task<PMPGroupJson> ToPmpGroup(string tempFolder, Dictionary<string, List<FileIdentifier>> identifiers, int page, bool oneOption = false)
        {
            PMPGroupJson pg;
            // We want to insert a type-erased PMPOptionJson, as returned by ToPmpOption(), in to a type-erased PMPGroupJson
            // This is the alternative to creating a single-purpose virtual function on PMPGroupJson
            Action<PMPOptionJson> addOptionFn;

            if (this.ImcData != null)
            {
                var imcG = new PMPImcGroupJson();
                pg = imcG;
                pg.Type = "Imc";

                imcG.Identifier = PmpIdentifierJson.FromRoot(ImcData.Root.Info, ImcData.Variant);
                imcG.DefaultEntry = PMPImcManipulationJson.PMPImcEntry.FromXivImc(ImcData.BaseEntry);
                imcG.AllVariants = ImcData.AllVariants;
                imcG.OnlyAttributes = ImcData.OnlyAttributes;
                addOptionFn = (PMPOptionJson o) => imcG.OptionData.Add((PmpImcOptionJson)o);
            }
            else
            {
                if (this.OptionType == EOptionType.Multi)
                {
                    var mg = new PMPMultiGroupJson();
                    pg = mg;
                    pg.Type = "Multi";
                    addOptionFn = (PMPOptionJson o) => mg.OptionData.Add((PmpMultiOptionJson)o);
                }
                else
                {
                    var sg = new PMPSingleGroupJson();
                    pg = sg;
                    pg.Type = "Single";
                    addOptionFn = (PMPOptionJson o) => sg.OptionData.Add((PmpSingleOptionJson)o);
                }
            }

            foreach (var option in Options)
            {
                option.Name = option.Name.Trim();
                var optionPrefix = WizardData.MakeOptionPrefix(this, option);
                var imgName = optionPrefix;
                if (imgName.Length > 0)
                {
                    // Remove trailing slash
                    imgName = imgName.Substring(0, imgName.Length - 1);
                }

                if (oneOption)
                {
                    imgName = "default_image";
                }
                identifiers.TryGetValue(optionPrefix, out var files);
                var opt = await option.ToPmpOption(tempFolder, files, imgName);
                addOptionFn(opt);
            }

            pg.Name = (Name ?? "").Trim();
            pg.Description = Description ?? "";
            pg.Priority = Priority;
            pg.DefaultSettings = Selection;
            pg.SelectedSettings = Selection;
            pg.Page = page;

            pg.Image = WizardHelpers.WriteImage(Image, tempFolder, IOUtil.MakePathSafe(Name));

            return pg;
        }
    }

    /// <summary>
    /// Class representing a Page of Groups.
    /// </summary>
    public class WizardPageEntry
    {
        public string Name;
        public List<WizardGroupEntry> Groups = new List<WizardGroupEntry>();

        public string FolderPath;

        public bool HasData
        {
            get
            {
                return Groups.Any(x => x.HasData);
            }
        }

        public static async Task<WizardPageEntry> FromWizardModpackPage(ModPackPageJson jp, string unzipPath, bool needsTexFix)
        {
            var page = new WizardPageEntry();
            page.Name = "Page " + (jp.PageIndex + 1);

            page.Groups = new List<WizardGroupEntry>();
            foreach (var p in jp.ModGroups)
            {
                var g = await WizardGroupEntry.FromWizardGroup(p, unzipPath, needsTexFix);
                if (g == null) continue;
                page.Groups.Add(g);
            }
            return page;
        }

        public async Task<ModPackData.ModPackPage> ToModPackPage(int index)
        {
            var mpp = new ModPackData.ModPackPage()
            {
                PageIndex = index,
                ModGroups = new List<ModGroup>(),
            };

            foreach (var group in Groups)
            {
                var mpg = await group.ToModGroup();
                mpp.ModGroups.Add(mpg);
            }

            return mpp;
        }
    }

    /// <summary>
    /// Class representing the description/cover page of a Modpack.
    /// </summary>
    public class WizardMetaEntry
    {
        public string Name = "";
        public string Author = "";
        public string Description = "";
        public string Url = "";
        public string Version = "1.0";
        public string Image = "";

        public List<string> Tags = new List<string>();

        public static WizardMetaEntry FromPMP(PMPJson pmp, string unzipPath)
        {
            var meta = pmp.Meta;
            var page = new WizardMetaEntry();
            page.Url = meta.Website;
            page.Version = meta.Version;
            page.Author = meta.Author;
            page.Description = meta.Description;
            page.Name = meta.Name;
            page.Tags = meta.ModTags;


            if (!string.IsNullOrWhiteSpace(meta.Image))
            {
                page.Image = Path.Combine(unzipPath, meta.Image);
            }
            else
            {
                page.Image = "";
                var hImage = pmp.GetHeaderImage();
                if (!string.IsNullOrWhiteSpace(hImage))
                {
                    meta.Image = Path.Combine(unzipPath, hImage);
                }
            }
            return page;
        }

        public static WizardMetaEntry FromTtmp(ModPackJson wiz, string unzipPath)
        {
            var page = new WizardMetaEntry();
            page.Url = wiz.Url;
            page.Name = wiz.Name;
            page.Version = wiz.Version;
            page.Author = wiz.Author;
            page.Description = wiz.Description;

            var img = wiz.GetHeaderImagePath();
            if (!string.IsNullOrWhiteSpace(img))
            {
                page.Image = Path.Combine(unzipPath, img);
            }


            return page;
        }
    }


    /// <summary>
    /// The full set of data necessary to render and display a wizard modpack install.
    /// </summary>
    public class WizardData
    {
        public WizardMetaEntry MetaPage = new WizardMetaEntry();
        public List<WizardPageEntry> DataPages = new List<WizardPageEntry>();
        public EModpackType ModpackType;
        public ModPack ModPack;

        public Dictionary<string, string> ExtraFiles = new Dictionary<string, string>();
        public bool HasData
        {
            get
            {
                return DataPages.Any(x => x.HasData);
            }
        }

        /// <summary>
        /// Original source this Wizard Data was generated from.
        /// Null if the user created a fresh modpack in the UI.
        /// </summary>
        public object RawSource;

        public static async Task<WizardData> FromPmp(PMPJson pmp, string unzipPath)
        {
            var data = new WizardData();
            data.MetaPage = WizardMetaEntry.FromPMP(pmp, unzipPath);
            data.DataPages = new List<WizardPageEntry>();
            data.ModpackType = EModpackType.Pmp;

            foreach(var f in pmp.ExtraFiles)
            {
                data.ExtraFiles.Add(f, Path.GetFullPath(Path.Combine(unzipPath, f)));
            }

            var mp = new ModPack(null);
            mp.Author = data.MetaPage.Author;
            mp.Version = data.MetaPage.Version;
            mp.Name = data.MetaPage.Name;
            mp.Url = data.MetaPage.Url;
            data.ModPack = mp;
            data.RawSource = pmp;

            if (pmp.DefaultMod != null && !pmp.DefaultMod.IsEmptyOption)
            {
                // Just drum up a basic group containing the default option.
                var fakeOption = new PmpSingleOptionJson();
                fakeOption.Name = "Default";
                fakeOption.Files = pmp.DefaultMod.Files;
                fakeOption.FileSwaps = pmp.DefaultMod.FileSwaps;
                fakeOption.Manipulations = pmp.DefaultMod.Manipulations;

                var fakeGroup = new PMPSingleGroupJson();
                fakeGroup.Name = "Default";
                fakeGroup.OptionData = new List<PmpSingleOptionJson>() { fakeOption };
                fakeGroup.SelectedSettings = 1;
                fakeGroup.Type = "Single";

                var page = new WizardPageEntry();
                page.Name = "Page 1";
                page.Groups = new List<WizardGroupEntry>();
                page.Groups.Add(await WizardGroupEntry.FromPMPGroup(fakeGroup, unzipPath));
                data.DataPages.Add(page);
            }

            if (pmp.Groups.Count > 0)
            {
                // Create sufficient pages.
                var pageMax = pmp.Groups.Max(x => x.Page);
                for (int i = 0; i <= pageMax; i++)
                {
                    var page = new WizardPageEntry();
                    page.Name = "Page " + (i + 1).ToString();
                    page.Groups = new List<WizardGroupEntry>();
                    data.DataPages.Add(page);
                }

                // Assign groups to pages.
                foreach (var g in pmp.Groups)
                {
                    var page = data.DataPages[g.Page];
                    page.Groups.Add(await WizardGroupEntry.FromPMPGroup(g, unzipPath));
                }
            }
            data.ClearNulls();
            return data;
        }

        public static async Task<WizardData> FromWizardTtmp(ModPackJson mpl, string unzipPath)
        {
            var data = new WizardData();
            data.ModpackType = EModpackType.TtmpWizard;
            data.MetaPage = WizardMetaEntry.FromTtmp(mpl, unzipPath);

            var mp = new ModPack(null);
            mp.Author = data.MetaPage.Author;
            mp.Version = data.MetaPage.Version;
            mp.Name = data.MetaPage.Name;
            mp.Url = data.MetaPage.Url;
            data.ModPack = mp;
            data.RawSource = mpl;

            var needsTexFix = TTMP.DoesModpackNeedTexFix(mpl);


            data.DataPages = new List<WizardPageEntry>();
            foreach (var p in mpl.ModPackPages)
            {
                data.DataPages.Add(await WizardPageEntry.FromWizardModpackPage(p, unzipPath, needsTexFix));
            }
            return data;
        }
        public static async Task<WizardData> FromSimpleTtmp(ModPackJson mpl, string unzipPath)
        {
            var data = new WizardData();
            data.ModpackType = EModpackType.TtmpWizard;
            data.MetaPage = WizardMetaEntry.FromTtmp(mpl, unzipPath);

            var mp = new ModPack(null);
            mp.Author = data.MetaPage.Author;
            mp.Version = data.MetaPage.Version;
            mp.Name = data.MetaPage.Name;
            mp.Url = data.MetaPage.Url;
            data.ModPack = mp;
            data.RawSource = mpl;

            var needsTexFix = TTMP.DoesModpackNeedTexFix(mpl);

            // Create a fake page/group.
            data.DataPages = new List<WizardPageEntry>();
            var page = new WizardPageEntry()
            {
                Groups = new List<WizardGroupEntry>(),
                Name = "Page 1"
            };
            data.DataPages.Add(page);

            var mgj = new ModGroupJson()
            {
                GroupName = "Default Group",
                SelectionType = "Single",
                OptionList = new List<ModOptionJson>()
            {
                new ModOptionJson()
                {
                    Name = "Default Option",
                    IsChecked = true,
                    SelectionType = "Single",
                    GroupName = "Default Group",
                    ModsJsons = mpl.SimpleModsList,
                },
            },
            };

            var g = await WizardGroupEntry.FromWizardGroup(mgj, unzipPath, needsTexFix);
            page.Groups.Add(g);
            return data;
        }

        public void ClearNulls()
        {
            var pages = DataPages.ToList();
            foreach (var p in pages)
            {
                p.FolderPath = null;
                if (!p.HasData)
                {
                    DataPages.Remove(p);
                    continue;
                }

                var groups = p.Groups.ToList();
                foreach (var g in groups)
                {
                    if (g == null || !g.HasData)
                    {
                        p.Groups.Remove(g);
                        continue;
                    }
                    g.FolderPath = null;

                    var options = g.Options.ToList();
                    foreach (var o in options)
                    {
                        if (o == null)
                        {
                            g.Options.Remove(o);
                        }
                    }
                }
            }
        }

        public void ClearEmpties()
        {
            var pages = DataPages.ToList();
            foreach (var p in pages)
            {
                p.FolderPath = null;
                if (!p.HasData)
                {
                    DataPages.Remove(p);
                    continue;
                }

                var groups = p.Groups.ToList();
                foreach (var g in groups)
                {
                    if (g == null || !g.HasData)
                    {
                        p.Groups.Remove(g);
                        continue;
                    }
                    g.FolderPath = null;

                    var options = g.Options.ToList();
                    var firstEmpty = false;
                    foreach (var o in options)
                    {
                        o.FolderPath = null;
                        if (!o.HasData)
                        {
                            if (!firstEmpty && g.OptionType == EOptionType.Single)
                            {
                                // Allow one empty option for single selects.
                                firstEmpty = true;
                                continue;
                            }
                            g.Options.Remove(o);
                            continue;
                        }
                    }
                }
            }

        }

        public async Task WriteModpack(string targetPath, bool saveExtraFiles = false)
        {
            if (targetPath.ToLower().EndsWith(".pmp"))
            {
                await WritePmp(targetPath, true, saveExtraFiles);
            }
            else if (targetPath.ToLower().EndsWith(".ttmp2"))
            {
                await WriteWizardPack(targetPath);
            }
            else if (Directory.Exists(targetPath) || !Path.GetFileName(targetPath).Contains("."))
            {
                Directory.CreateDirectory(targetPath);
                await WritePmp(targetPath, false, saveExtraFiles);
            }
            else
            {
                throw new ArgumentException("Invalid Modpack Path: " + targetPath);
            }
        }
        public async Task WriteWizardPack(string targetPath)
        {
            ClearNulls();
            Version.TryParse(MetaPage.Version, out var ver);

            ver ??= new Version("1.0");
            var modPackData = new ModPackData()
            {
                Name = MetaPage.Name,
                Author = MetaPage.Author,
                Url = MetaPage.Url,
                Version = ver,
                Description = MetaPage.Description,
                ModPackPages = new List<ModPackData.ModPackPage>(),
            };

            int i = 0;
            foreach (var page in DataPages)
            {
                if (!page.HasData)
                {
                    continue;
                }
                modPackData.ModPackPages.Add(await page.ToModPackPage(i));
                i++;
            }

            await TTMP.CreateWizardModPack(modPackData, targetPath, null, true);
        }

        private string MakePagePrefix(WizardPageEntry page)
        {
            if(page.FolderPath != null)
            {
                return page.FolderPath;
            }

            var pagePrefix = "";
            if (DataPages.Count > 1)
            {
                var pIdx = DataPages.IndexOf(page)+1;
                pagePrefix = "p" + pIdx + "/";
            }
            else if (page.Groups.Count == 1)
            {
                pagePrefix = "";
            }

            page.FolderPath = pagePrefix;
            return page.FolderPath;
        }
        private string MakeGroupPrefix(WizardPageEntry page, WizardGroupEntry group)
        {
            if(group.FolderPath != null)
            {
                return group.FolderPath;
            }

            var gName = IOUtil.MakePathSafe(group.Name);
            if (string.IsNullOrWhiteSpace(gName))
            {
                gName = "Blank Group";
            }

            var pagePrefix = MakePagePrefix(page);
            var prefix = pagePrefix;
            if (page.Groups.Count > 0 )
            {
                prefix = pagePrefix + gName + "/";
            }

            var groupPrefix = prefix;
            var i = 1;

            while (page.Groups.Any(x => x.FolderPath == groupPrefix))
            {
                groupPrefix = pagePrefix + gName + " (" + i +")/";
            }

            group.FolderPath = groupPrefix;
            return groupPrefix;
        }
        private string MakeOptionPrefix(WizardPageEntry page, WizardGroupEntry group, WizardOptionEntry option)
        {
            MakeGroupPrefix(page, group);
            return MakeOptionPrefix(group, option);
        }
        internal static string MakeOptionPrefix(WizardGroupEntry group, WizardOptionEntry option)
        {
            if(option.FolderPath != null)
            {
                return option.FolderPath;
            }

            if(group.FolderPath == null)
            {
                // Fallback catch for single option write.
                group.FolderPath = "";
            }

            var oName = IOUtil.MakePathSafe(option.Name);
            if (string.IsNullOrWhiteSpace(oName))
            {
                oName = "Blank Option";
            }

            var path = "";
            if (group.Options.Count > 1)
            {
                path = group.FolderPath + oName + "/";
            }
            else
            {
                path = group.FolderPath;
            }

            var i = 1;
            while(group.Options.Any(x => x.FolderPath == path))
            {
                path = group.FolderPath + oName + " ("+i+")/";
                i++;
            }

            option.FolderPath = path;

            return option.FolderPath;
        }

        public async Task WritePmp(string targetPath, bool zip = true, bool saveExtraFiles = false)
        {
            ClearNulls();
            var pmp = new PMPJson()
            {
                DefaultMod = new PmpDefaultMod(),
                Groups = new List<PMPGroupJson>(),
                Meta = new PMPMetaJson(),
            };

            var tempFolder = IOUtil.GetFrameworkTempSubfolder("PMP");
            Directory.CreateDirectory(tempFolder);
            try
            {
                Version.TryParse(MetaPage.Version, out var ver);
                ver ??= new Version("1.0");

                if (saveExtraFiles && ExtraFiles.Count > 0)
                {
                    foreach (var file in ExtraFiles)
                    {
                        if (File.Exists(file.Value))
                        {
                            var path = Path.GetFullPath(Path.Combine(tempFolder, file.Key));
                            Directory.CreateDirectory(Path.GetDirectoryName(path));
                            File.Copy(IOUtil.MakeLongPath(file.Value), IOUtil.MakeLongPath(path), true);
                        }
                    }
                }

                pmp.Meta.Name = MetaPage.Name;
                pmp.Meta.Author = MetaPage.Author;
                pmp.Meta.Website = MetaPage.Url;
                pmp.Meta.Description = MetaPage.Description;
                pmp.Meta.Version = ver.ToString();
                pmp.Meta.ModTags = new List<string>();
                pmp.Meta.FileVersion = PMP._WriteFileVersion;
                pmp.Meta.Image = WizardHelpers.WriteImage(MetaPage.Image, tempFolder, "_MetaImage");
                pmp.Meta.ModTags = MetaPage.Tags;

                var optionCount = DataPages.Sum(p => p.Groups.Sum(x => x.Options.Count));

                // We need to compose a list of all the file storage information we're going to use.
                // Grouped by option folder.
                var allFiles = new Dictionary<string, Dictionary<string, FileStorageInformation>>();
                var pIdx = 1;
                foreach (var p in DataPages)
                {
                    foreach (var g in p.Groups)
                    {
                        g.Name = g.Name.Trim();
                        foreach (var o in g.Options)
                        {
                            if (o.GroupType != EGroupType.Standard)
                            {
                                continue;
                            }

                            var files = o.StandardData.Files;

                            if (string.IsNullOrWhiteSpace(o.Name) || string.IsNullOrWhiteSpace(g.Name))
                            {
                                throw new InvalidDataException("PMP Files must have valid group and option names.");
                            }


                            var optionPrefix = MakeOptionPrefix(p, g, o);

                            if (allFiles.ContainsKey(optionPrefix))
                            {
                                foreach (var f in files)
                                {
                                    allFiles[optionPrefix].Add(f.Key, f.Value);
                                }
                            }
                            else
                            {
                                allFiles.Add(optionPrefix, files);
                            }
                        }
                    }
                    pIdx++;
                }

                // These are de-duplicated internal write paths for the final PMP folder structure, coupled with
                // their file identifier and internal path information
                var identifiers = await FileIdentifier.IdentifierListFromDictionaries(allFiles);

                WizardGroupEntry defaultModGroup = null;

                if (optionCount >= 1)
                {
                    // Synthesize a PMP default mod from wizard data if an appropriate looking single-option mod group is present.
                    foreach (var p in DataPages)
                    {
                        foreach (var g in p.Groups)
                        {
                            if (g.GroupType == EGroupType.Standard
                                && (g.Name == "Default" || g.Name == "Default Group")
                                && g.Options.Count == 1
                                && (g.Options[0].Name == "Default" || g.Options[0].Name == "Default Option"))
                            {
                                var sg = await g.ToPmpGroup(tempFolder, identifiers, 0, true);
                                var so = sg.Options[0] as PmpStandardOptionJson;

                                if (so != null)
                                {
                                    pmp.DefaultMod.Files = so.Files;
                                    pmp.DefaultMod.FileSwaps = so.FileSwaps;
                                    pmp.DefaultMod.Manipulations = so.Manipulations;
                                    defaultModGroup = g;
                                    break;
                                }
                            }
                        }

                        if (defaultModGroup != null)
                            break;
                    }
                }
                
                // This both constructs the JSON structure and writes our files to their
                // real location in the folder tree in the temp folder.
                var page = 0;
                foreach (var p in DataPages)
                {
                    var numGroupsThisPage = 0;
                    foreach (var g in p.Groups)
                    {
                        // Skip the group that was used to generate DefaultMod, if any
                        if (g == defaultModGroup)
                            continue;

                        var gPrefix = MakeGroupPrefix(p, g);
                        var pg = await g.ToPmpGroup(tempFolder, identifiers, page);
                        pmp.Groups.Add(pg);
                        ++numGroupsThisPage;
                    }
                    if (numGroupsThisPage > 0)
                        page++;
                }


                // This performs the final json serialization/writing and zipping.
                if (zip)
                {
                    await PMP.WritePmp(pmp, tempFolder, targetPath);
                }
                else
                {
                    await PMP.WritePmp(pmp, tempFolder);
                    Directory.CreateDirectory(targetPath);
                    IOUtil.CopyFolder(tempFolder, targetPath);
                }
            }
            finally
            {
                IOUtil.DeleteTempDirectory(tempFolder);
            }
        }

        /// <summary>
        /// Updates the base Penumbra groups with the new user-selected values.
        /// </summary>
        public void FinalizePmpSelections()
        {
            // Need to go through and assign the Selected values back to the PMP.
            foreach (var p in DataPages)
            {
                foreach (var g in p.Groups)
                {
                    var pg = (g.ModOption as PMPGroupJson);
                    pg.SelectedSettings = g.Selection;
                }
            }
        }


        /// <summary>
        /// Returns the list of selected mod files that the TTMP importers expect, based on user selection(s).
        /// </summary>
        /// <returns></returns>
        public List<ModsJson> FinalizeTttmpSelections()
        {
            List<ModsJson> modFiles = new List<ModsJson>();
            // Need to go through and compile the final ModJson list.
            foreach (var p in DataPages)
            {
                foreach (var g in p.Groups)
                {
                    var ttGroup = g.ModOption as ModGroupJson;
                    if (ttGroup == null)
                    {
                        continue;
                    }

                    var selected = 0;
                    for (int i = 0; i < g.Options.Count; i++)
                    {
                        var opt = g.Options[i];
                        if (opt.Selected)
                        {
                            if (opt.GroupType == EGroupType.Standard)
                            {
                                if (opt.StandardData.Manipulations != null && opt.StandardData.Manipulations.Count > 0)
                                {
                                    // We shouldn't actually be able to get to this path, but safety is good.
                                    throw new NotImplementedException("Importing TTMPs with Meta Manipulations is not supported.  How did you get here though?");
                                }

                                var ttOpt = ttGroup.OptionList[i];
                                modFiles.AddRange(ttOpt.ModsJsons);
                            }
                            else
                            {
                                // We shouldn't actually be able to get to this path, but safety is good.
                                throw new NotImplementedException("Importing TTMPs with IMC Groups is not supported.  How did you get here though?");
                            }
                        }
                    }
                }
            }

            // Assign mod pack linkage that the framework expects.
            foreach (var mj in modFiles)
            {
                mj.ModPackEntry = ModPack;
            }

            return modFiles;
        }


        /// <summary>
        /// The simplest method for generating fully constructed wizard data.
        /// Unzips the entire modpack in the process.
        /// </summary>
        /// <param name="modpack"></param>
        /// <returns></returns>
        public static async Task<WizardData> FromModpack(string modpack)
        {
            return await Task.Run(async () =>
            {
                var modpackType = TTMP.GetModpackType(modpack);

                if (modpackType == TTMP.EModpackType.Pmp)
                {
                    var pmp = await PMP.LoadPMP(modpack, false, true);
                    return await WizardData.FromPmp(pmp.pmp, pmp.path);
                }
                else if (modpackType == TTMP.EModpackType.TtmpWizard)
                {
                    var ttmp = await TTMP.UnzipTtmp(modpack);
                    return await WizardData.FromWizardTtmp(ttmp.Mpl, ttmp.UnzipFolder);
                }
                else if (modpackType == TTMP.EModpackType.TtmpSimple || modpackType == TTMP.EModpackType.TtmpOriginal || modpackType == TTMP.EModpackType.TtmpBackup)
                {
                    var ttmp = await TTMP.UnzipTtmp(modpack);
                    return await WizardData.FromSimpleTtmp(ttmp.Mpl, ttmp.UnzipFolder);
                }
                return null;
            });
        }
    }
}
