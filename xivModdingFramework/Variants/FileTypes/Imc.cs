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

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Variants.DataContainers;

namespace xivModdingFramework.Variants.FileTypes
{
    /// <summary>
    /// This class contains the methods that deal with the .imc file type 
    /// </summary>
    public class Imc
    {
        private const string ImcExtension = ".imc";
        private readonly DirectoryInfo _gameDirectory;
        private readonly XivDataFile _dataFile;

        public Imc(DirectoryInfo gameDirectory, XivDataFile dataFile)
        {
            _gameDirectory = gameDirectory;
            _dataFile = dataFile;
        }

        public bool ChangedType { get; set; }

        /// <summary>
        /// Gets the relevant IMC information for a given item
        /// </summary>
        /// <param name="item">The item to get the version for</param>
        /// <param name="modelInfo">The model info of the item</param>
        /// <returns>The XivImc Data</returns>
        public async Task<XivImc> GetImcInfo(IItemModel item, XivModelInfo modelInfo = null)
        {
            var xivImc = new XivImc();

            // Set based on item if we don't have one provided directly.
            if(modelInfo == null)
            {
                modelInfo = item.ModelInfo;
            }

            // If it's still null, error.
            if(modelInfo == null)
            {
               throw new NotSupportedException("Cannot get IMC info.");
            }

            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int headerLength = 4;
            const int variantLength = 6;
            const int variantSetLength = 30;

            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);

            var itemType = ItemType.GetPrimaryItemType(item);
            var imcPath = GetImcPath(modelInfo, itemType);

            var itemCategory = item.SecondaryCategory;

            var imcOffset = await index.GetDataOffset(HashGenerator.GetHash(imcPath.Folder),
                HashGenerator.GetHash(imcPath.File), _dataFile);

            if (imcOffset == 0)
            {
                if (item.SecondaryCategory == XivStrings.Two_Handed)
                {
                    itemCategory = XivStrings.Hands;
                    itemType = XivItemType.equipment;
                    imcPath = GetImcPath(modelInfo, itemType);

                    imcOffset = await index.GetDataOffset(HashGenerator.GetHash(imcPath.Folder),
                        HashGenerator.GetHash(imcPath.File), _dataFile);

                    if (imcOffset == 0)
                    {
                        throw new Exception($"Could not find offset for {imcPath.Folder}/{imcPath.File}");
                    }

                    // This is changing a public GLOBAL variable in the IMC class.
                    // This is begging to cause really awful to diagnose errors.
                    ChangedType = true;
                }
                else
                {
                    throw new Exception($"Could not find offset for {imcPath.Folder}/{imcPath.File}");
                }
            }

            var imcData = await dat.GetType2Data(imcOffset, _dataFile);

            await Task.Run(() =>
            {
                using (var br = new BinaryReader(new MemoryStream(imcData)))
                {
                    int variantOffset;

                    if (itemType == XivItemType.weapon || itemType == XivItemType.monster)
                    {
                        // weapons and monsters do not have variant sets
                        variantOffset = (modelInfo.ImcSubsetID * variantLength) + headerLength;

                        // use default if offset is out of range
                        if (variantOffset >= imcData.Length)
                        {
                            variantOffset = headerLength;
                        }
                    }
                    else
                    {
                        // Variant Sets contain 5 variants for each slot
                        // These can be Head, Body, Hands, Legs, Feet  or  Ears, Neck, Wrists, LRing, RRing
                        // This skips to the correct variant set, then to the correct slot within that set for the item
                        variantOffset = (modelInfo.ImcSubsetID * variantSetLength) +
                                        (_slotOffsetDictionary[itemCategory] * variantLength) + headerLength;

                        // use defalut if offset is out of range
                        if (variantOffset >= imcData.Length)
                        {
                            variantOffset = (_slotOffsetDictionary[itemCategory] * variantLength) + headerLength;
                        }
                    }

                    br.BaseStream.Seek(variantOffset, SeekOrigin.Begin);

                    // if(variantOffset)

                    xivImc.Variant = br.ReadByte();
                    var unknown = br.ReadByte();
                    xivImc.Mask = br.ReadUInt16();
                    xivImc.Vfx = br.ReadByte();
                    var unknown1 = br.ReadByte();
                }
            });

            return xivImc;
        }

        /// <summary>
        /// Gets the full IMC information for a given item
        /// </summary>
        /// <param name="item"></param>
        /// <param name="modelInfo"></param>
        /// <returns>The ImcData data</returns>
        public async Task<FullImcInfo> GetFullImcInfo(IItemModel item, XivModelInfo modelInfo = null)
        {
            if(modelInfo == null)
            {
                modelInfo = item.ModelInfo;
            }
            if(modelInfo == null)
            {
                throw new NotSupportedException("Attempted to get IMC info for invalid item.");
            }

            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);

            var itemType = ItemType.GetPrimaryItemType(item);
            var imcPath = GetImcPath(modelInfo, itemType);

            var imcOffset = await index.GetDataOffset(HashGenerator.GetHash(imcPath.Folder), HashGenerator.GetHash(imcPath.File), _dataFile);

            if (imcOffset == 0)
            {
                throw new Exception($"Could not find offset for {imcPath.Folder}/{imcPath.File}");
            }

            var imcByteData = await dat.GetType2Data(imcOffset, _dataFile);

            return await Task.Run(() =>
            {
                using (var br = new BinaryReader(new MemoryStream(imcByteData)))
                {
                    var subsetCount = br.ReadInt16();
                    var imcData = new FullImcInfo()
                    {
                        Unknown = br.ReadInt16(),
                        DefaultSubset = new List<XivImc>(),
                        SubsetList = new List<List<XivImc>>(subsetCount)
                    };

                    //weapons and monsters do not have variant sets
                    if (itemType == XivItemType.weapon || itemType == XivItemType.monster)
                    {

                        imcData.DefaultSubset.Add(new XivImc
                        {
                            Variant = br.ReadUInt16(),
                            Mask = br.ReadUInt16(),
                            Vfx = br.ReadUInt16()
                        });

                        for (var i = 0; i < subsetCount; i++)
                        {
                            var subset = new List<XivImc>() {
                                new XivImc {Variant = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()}
                            };
                        }
                    }
                    else
                    {
                        imcData.DefaultSubset = new List<XivImc>()
                        {
                            new XivImc
                                {Variant = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                            new XivImc
                                {Variant = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                            new XivImc
                                {Variant = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                            new XivImc
                                {Variant = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                            new XivImc
                                {Variant = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                        };

                        for (var i = 0; i < subsetCount; i++)
                        {
                            // gets the data for each slot in the current variant set
                            var imcGear = new List<XivImc>()
                            {
                                new XivImc
                                    {Variant = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                                new XivImc
                                    {Variant = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                                new XivImc
                                    {Variant = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                                new XivImc
                                    {Variant = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                                new XivImc
                                    {Variant = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                            };

                            imcData.SubsetList.Add(imcGear);
                        }
                    }

                    return imcData;
                }
            });
        }

        /// <summary>
        /// Gets the IMC internal path for the given model info
        /// </summary>
        /// <param name="modelInfo">The model info of the item</param>
        /// <param name="itemType">The type of the item</param>
        /// <returns>A touple containing the Folder and File strings</returns>
        private static (string Folder, string File) GetImcPath(XivModelInfo modelInfo, XivItemType itemType)
        {
            string imcFolder;
            string imcFile;

            var modelID = modelInfo.PrimaryID.ToString().PadLeft(4, '0');
            var body = modelInfo.SecondaryID.ToString().PadLeft(4, '0');

            switch (itemType)
            {
                case XivItemType.equipment:
                    imcFolder = $"chara/{itemType}/e{modelID}";
                    imcFile = $"e{modelID}{ImcExtension}";
                    break;
                case XivItemType.accessory:
                    imcFolder = $"chara/{itemType}/a{modelID}";
                    imcFile = $"a{modelID}{ImcExtension}";
                    break;
                case XivItemType.weapon:
                    imcFolder = $"chara/{itemType}/w{modelID}/obj/body/b{body}";
                    imcFile = $"b{body}{ImcExtension}";
                    break;
                case XivItemType.monster:
                    imcFolder = $"chara/{itemType}/m{modelID}/obj/body/b{body}";
                    imcFile = $"b{body}{ImcExtension}";
                    break;
                case XivItemType.demihuman:
                    imcFolder = $"chara/{itemType}/d{modelID}/obj/equipment/e{body}";
                    imcFile = $"e{body}{ImcExtension}";
                    break;
                default:
                    imcFolder = "";
                    imcFile = "";
                    break;
            }

            return (imcFolder, imcFile);
        }

        /// <summary>
        /// A dictionary containing slot offset data in format [Slot Name, Offset within variant set]
        /// </summary>
        private static readonly Dictionary<string, int> _slotOffsetDictionary = new Dictionary<string, int>
        {
            {XivStrings.Main_Hand, 0},
            {XivStrings.Off_Hand, 0},
            {XivStrings.Two_Handed, 0},
            {XivStrings.Main_Off, 0},
            {XivStrings.Head, 0},
            {XivStrings.Body, 1},
            {XivStrings.Hands, 2},
            {XivStrings.Legs, 3},
            {XivStrings.Feet, 4},
            {XivStrings.Ears, 0},
            {XivStrings.Neck, 1},
            {XivStrings.Wrists, 2},
            {XivStrings.Rings, 3},
            {XivStrings.Head_Body, 1},
            {XivStrings.Body_Hands, 1},
            {XivStrings.Body_Hands_Legs, 1},
            {XivStrings.Body_Legs_Feet, 1},
            {XivStrings.Body_Hands_Legs_Feet, 1},
            {XivStrings.Legs_Feet, 3},
            {XivStrings.All, 1},
            {XivStrings.Food, 0},
            {XivStrings.Mounts, 0},
            {XivStrings.DemiHuman, 0},
            {XivStrings.Minions, 0},
            {XivStrings.Monster, 0},
            {XivStrings.Pets, 0}
        };

        /// <summary>
        /// Class containing the information for and IMC file
        /// </summary>
        public class FullImcInfo
        {
            /// <summary>
            /// Get the number of subsets.
            ///  -NOT- the same as number of material variants.
            /// </summary>
            public int SubsetCount { get
                {
                    return SubsetList.Count;
                }
                set {
                    throw new NotSupportedException("Attempted to directly set SubsetCount.");
                }
            }

            /// <summary>
            /// Get the size of each subset (Either 1 or 5)
            /// </summary>
            public int SubsetSize
            {
                get
                {
                    return DefaultSubset.Count;
                }
                set
                {
                    throw new NotSupportedException("Attempted to directly set SubsetSize.");
                }
            }

            /// <summary>
            /// Unknown Value
            /// </summary>
            public int Unknown { get; set; }

            /// <summary>
            /// Total # of Gear Subsets.
            /// NOT the same as number of material variants.
            /// IItemModel->ImcSubsetID can be used as an index accessory in this list.
            /// </summary>
            public List<List<XivImc>> SubsetList { get; set; }

            /// <summary>
            /// The default variant set for the item, always the variant immediatly following the header
            /// </summary>
            public List<XivImc> DefaultSubset { get; set; }


            /// <summary>
            /// Retrieve a given IMC info. Negative values retrieve the default set.
            /// </summary>
            /// <param name="index"></param>
            /// <param name="slot"></param>
            /// <returns></returns>
            public XivImc GetImcInfo(int subsetID = -1, string fullSlotName = "")
            {
                // Variant IDs are 1 based, not 0 based.
                var index = subsetID - 1;

                // Invalid Index, return default.
                if (index >= SubsetCount)
                {
                    index = -1;
                }

                // Test for getting default set.
                var subset = DefaultSubset;
                if(index > 0)
                {
                    subset = SubsetList[index];
                }

                // Get which offset the slot uses.
                var idx = 0;
                if(_slotOffsetDictionary.ContainsKey(fullSlotName))
                {
                    idx = _slotOffsetDictionary[fullSlotName];
                }

                return subset[idx];
            }
        }

    }
}