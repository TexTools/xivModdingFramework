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
        public async Task<XivImc> GetImcInfo(IItemModel item, XivModelInfo modelInfo)
        {
            var xivImc = new XivImc();

            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int headerLength = 4;
            const int variantLength = 6;
            const int variantSetLength = 30;

            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);

            var itemType = ItemType.GetItemType(item);
            var imcPath = GetImcPath(modelInfo, itemType);

            var itemCategory = item.ItemCategory;

            var imcOffset = await index.GetDataOffset(HashGenerator.GetHash(imcPath.Folder),
                HashGenerator.GetHash(imcPath.File), _dataFile);

            if (imcOffset == 0)
            {
                if (item.ItemCategory == XivStrings.Two_Handed)
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
                        variantOffset = (modelInfo.Variant * variantLength) + headerLength;

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
                        variantOffset = (modelInfo.Variant * variantSetLength) +
                                        (_slotOffsetDictionary[itemCategory] * variantLength) + headerLength;

                        // use defalut if offset is out of range
                        if (variantOffset >= imcData.Length)
                        {
                            variantOffset = (_slotOffsetDictionary[itemCategory] * variantLength) + headerLength;
                        }
                    }

                    br.BaseStream.Seek(variantOffset, SeekOrigin.Begin);

                    // if(variantOffset)

                    xivImc.Version = br.ReadByte();
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
        public async Task<ImcData> GetFullImcInfo(IItemModel item, XivModelInfo modelInfo)
        {
            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);

            var itemType = ItemType.GetItemType(item);
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
                    var imcData = new ImcData()
                    {
                        VariantCount = br.ReadInt16(),
                        Unknown = br.ReadInt16(),
                        GearVariantList = new List<VariantSet>()
                    };

                    //weapons and monsters do not have variant sets
                    if (itemType == XivItemType.weapon || itemType == XivItemType.monster)
                    {
                        imcData.OtherVariantList = new List<XivImc>();

                        imcData.DefaultVariant = new XivImc
                        {
                            Version = br.ReadUInt16(),
                            Mask = br.ReadUInt16(),
                            Vfx = br.ReadUInt16()
                        };

                        for (var i = 0; i < imcData.VariantCount; i++)
                        {
                            imcData.OtherVariantList.Add(new XivImc
                                {Version = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()});
                        }
                    }
                    else
                    {
                        imcData.GearVariantList = new List<VariantSet>();

                        imcData.DefaultVariantSet = new VariantSet
                        {
                            Slot1 = new XivImc
                                {Version = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                            Slot2 = new XivImc
                                {Version = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                            Slot3 = new XivImc
                                {Version = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                            Slot4 = new XivImc
                                {Version = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                            Slot5 = new XivImc
                                {Version = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                        };

                        for (var i = 0; i < imcData.VariantCount; i++)
                        {
                            // gets the data for each slot in the current variant set
                            var imcGear = new VariantSet
                            {
                                Slot1 = new XivImc
                                    {Version = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                                Slot2 = new XivImc
                                    {Version = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                                Slot3 = new XivImc
                                    {Version = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                                Slot4 = new XivImc
                                    {Version = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                                Slot5 = new XivImc
                                    {Version = br.ReadUInt16(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                            };

                            imcData.GearVariantList.Add(imcGear);
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

            var modelID = modelInfo.ModelID.ToString().PadLeft(4, '0');
            var body = modelInfo.Body.ToString().PadLeft(4, '0');

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
        private readonly Dictionary<string, int> _slotOffsetDictionary = new Dictionary<string, int>
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
        public class ImcData
        {
            /// <summary>
            /// The amount of Variants contained in the IMC file
            /// </summary>
            public int VariantCount { get; set; }

            /// <summary>
            /// Unknown Value
            /// </summary>
            public int Unknown { get; set; }

            /// <summary>
            /// Variant List for Gear which contains variant sets
            /// </summary>
            public List<VariantSet> GearVariantList { get; set; }

            /// <summary>
            /// Variant List for other items that do not contain varian sets
            /// </summary>
            public List<XivImc> OtherVariantList { get; set; }

            /// <summary>
            /// The default variant for the item, always the variant immediatly following the header
            /// </summary>
            public XivImc DefaultVariant { get; set; }

            /// <summary>
            /// The default variant set for the item, always the variant immediatly following the header
            /// </summary>
            public VariantSet DefaultVariantSet { get; set; }
        }

        /// <summary>
        /// A class that contains the information for a variant set
        /// </summary>
        public class VariantSet
        {
            /// <summary>
            /// Slot 1 of the variant set
            /// </summary>
            /// <remarks>
            /// Head for Gear, Ears for Accessories
            /// </remarks>
            public XivImc Slot1 { get; set; }

            /// <summary>
            /// Slot 2 of the variant set
            /// </summary>
            /// <remarks>
            /// Body for Gear, Neck for Accessories
            /// </remarks>
            public XivImc Slot2 { get; set; }

            /// <summary>
            /// Slot 3 of the variant set
            /// </summary>
            /// <remarks>
            /// Hands for Gear, Wrists for Accessories
            /// </remarks>
            public XivImc Slot3 { get; set; }

            /// <summary>
            /// Slot 4 of the variant set
            /// </summary>
            /// <remarks>
            /// Legs for Gear, Left Ring for Accessories
            /// </remarks>
            public XivImc Slot4 { get; set; }

            /// <summary>
            /// Slot 5 of the variant set
            /// </summary>
            /// <remarks>
            /// Feet for Gear, Right Ring for Accessories
            /// </remarks>
            public XivImc Slot5 { get; set; }
        }
    }
}