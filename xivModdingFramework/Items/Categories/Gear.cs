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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.Exd.Enums;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.General;
using xivModdingFramework.General.DataContainers;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Mods;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.Enums;
using xivModdingFramework.Variants.FileTypes;

using Index = xivModdingFramework.SqPack.FileTypes.Index;

namespace xivModdingFramework.Items.Categories
{
    /// <summary>
    /// This class is used to obtain a list of available gear
    /// This includes equipment, accessories, and weapons
    /// Food is a special case as it is within the chara/weapons directory
    /// </summary>
    public class Gear
    {
        private static object _gearLock = new object();

        public Gear()
        {
        }
        public async Task<List<XivGear>> GetGearList(string substring = null)
        {
            return await XivCache.GetCachedGearList(substring);
        }

        public async Task GetGlasses(ModTransaction tx = null)
        {

            var ex = new Ex();
            var glasses = await ex.ReadExData(XivEx.glasses, tx);
            var ex2 = new Ex();
            var gstyles = await ex2.ReadExData(XivEx.glassesstyle, tx);
            var ex3 = new Ex();
            var itemDictionary = await ex3.ReadExData(XivEx.item, tx);

            await Task.Run(() => Parallel.ForEach(glasses, (item) =>
            {
                var cols = new List<object>();

                for(int i = 0; i < item.Value.Columns.Count; i++)
                {
                    cols.Add(item.Value.GetColumn(i));
                }


                Trace.WriteLine(cols);

            }));
            await Task.Run(() => Parallel.ForEach(gstyles, (item) =>
            {
                var cols = new List<object>();

                for (int i = 0; i < item.Value.Columns.Count; i++)
                {
                    cols.Add(item.Value.GetColumn(i));
                }

                Trace.WriteLine(cols);

            }));
        }

        /// <summary>
        /// A getter for available gear in the Item exd files
        /// </summary>
        /// <returns>A list containing XivGear data</returns>
        public async Task<List<XivGear>> GetUnCachedGearList(ModTransaction tx = null)
        {
            var ex = new Ex();
            var itemDictionary = await ex.ReadExData(XivEx.item, tx);



            var xivGearList = new List<XivGear>();

            xivGearList.AddRange(GetMissingGear());

            if (itemDictionary.Count == 0)
                return xivGearList;

            // Loops through all the items in the item exd files
            // Item files start at 0 and increment by 500 for each new file
            // Item_0, Item_500, Item_1000, etc.
            await Task.Run(() => Parallel.ForEach(itemDictionary, (item) =>
            {
                var row = item.Value;
                try
                {
                    var primaryInfo = (ulong)row.GetColumnByName("PrimaryInfo");
                    var secondaryInfo = (ulong) row.GetColumnByName("SecondaryInfo");

                    // Check if item can be equipped.
                    if (primaryInfo == 0 && secondaryInfo == 0)
                        return;

                    // Belts. No longer exist in game + have no model despite having a setId.
                    var slotNum = (byte)row.GetColumnByName("SlotNum");
                    if (slotNum == 6) return;

                    // Has to have a valid name.
                    var name = (string)row.GetColumnByName("Name");
                    if (String.IsNullOrEmpty(name))
                        return;

                    var icon = (ushort)row.GetColumnByName("Icon");

                    var primaryMi = new XivModelInfo();
                    var secondaryMi = new XivModelInfo();
                    var xivGear = new XivGear
                    {
                        Name = name,
                        ExdID = item.Key,
                        PrimaryCategory = XivStrings.Gear,
                        ModelInfo = primaryMi,
                        IconId = icon,
                    };


                    xivGear.SecondaryCategory = _slotNameDictionary.ContainsKey(slotNum) ? _slotNameDictionary[slotNum] : "Unknown";

                    // Model information is stored in a short-array format.
                    var primaryQuad = Quad.Read(BitConverter.GetBytes(primaryInfo), 0);
                    var secondaryQuad = Quad.Read(BitConverter.GetBytes(secondaryInfo), 0);

                    // If the model has a 3rd value, 2nd is body ID and variant ID is pushed to 3rd slot.
                    bool hasBodyId = primaryQuad.Values[2] > 0 ? true : false;
                    bool hasOffhand = secondaryQuad.Values[0] > 0 ? true : false;

                    primaryMi.PrimaryID = primaryQuad.Values[0];
                    secondaryMi.PrimaryID = secondaryQuad.Values[0];

                    if (hasBodyId)
                    {
                        xivGear.PrimaryCategory = XivStrings.Weapons;
                        primaryMi.SecondaryID = primaryQuad.Values[1];
                        primaryMi.ImcSubsetID = primaryQuad.Values[2];
                        secondaryMi.SecondaryID = secondaryQuad.Values[1];
                        secondaryMi.ImcSubsetID = secondaryQuad.Values[2];
                    }
                    else
                    {
                        primaryMi.ImcSubsetID = primaryQuad.Values[1];
                        secondaryMi.ImcSubsetID = secondaryQuad.Values[1];
                        if (xivGear.SecondaryCategory == XivStrings.Earring
                            || xivGear.SecondaryCategory == XivStrings.Neck
                            || xivGear.SecondaryCategory == XivStrings.Wrists
                            || xivGear.SecondaryCategory == XivStrings.Rings)
                        {
                            xivGear.PrimaryCategory = XivStrings.Accessories;
                        }
                    }

                    XivGear secondaryItem = null;
                    if (secondaryMi.PrimaryID != 0)
                    {
                        // Make an entry for the offhand model.
                        secondaryItem = (XivGear)xivGear.Clone();
                        secondaryItem.ModelInfo = secondaryMi;
                        xivGear.Name += " - " + XivStrings.Main_Hand;
                        secondaryItem.Name += " - " + XivStrings.Off_Hand;
                        xivGear.PairedItem = secondaryItem;
                        secondaryItem.PairedItem = xivGear;
                        xivGear.SecondaryCategory = XivStrings.Dual_Wield;
                        secondaryItem.SecondaryCategory = XivStrings.Dual_Wield;

                    } else if(slotNum == 12)
                    {
                        // Make this the Right ring, and create the Left Ring entry.
                        secondaryItem = (XivGear)xivGear.Clone();

                        xivGear.Name += " - " + XivStrings.Right;
                        secondaryItem.Name += " - " + XivStrings.Left;

                        xivGear.PairedItem = secondaryItem;
                        secondaryItem.PairedItem = xivGear;
                    }

                    lock (_gearLock)
                    {
                        xivGearList.Add(xivGear);
                        if (secondaryItem != null)
                        {
                            xivGearList.Add(secondaryItem);
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw;
                }
            }));

            xivGearList.Sort();

            return xivGearList;
        }

        /// <summary>
        /// Gets any missing gear that must be added manualy as it does not exist in the items exd
        /// </summary>
        /// <returns>The list of missing gear</returns>
        private List<XivGear> GetMissingGear()
        {
            var xivGearList = new List<XivGear>();

            var xivGear = new XivGear
            {
                Name = "SmallClothes Body",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[4],
                ModelInfo = new XivModelInfo { PrimaryID = 0, ImcSubsetID = 1, SecondaryID = 0}
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Hands",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[5],
                ModelInfo = new XivModelInfo { PrimaryID = 0, ImcSubsetID = 1, SecondaryID = 0 }
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Legs",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[7],
                ModelInfo = new XivModelInfo { PrimaryID = 0, ImcSubsetID = 1, SecondaryID = 0 }
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Feet",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[8],
                ModelInfo = new XivModelInfo { PrimaryID = 0, ImcSubsetID = 1, SecondaryID = 0 }
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Body (NPC)",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[4],
                ModelInfo = new XivModelInfo { PrimaryID = 9903, ImcSubsetID = 1, SecondaryID = 0 }
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Hands (NPC)",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[5],
                ModelInfo = new XivModelInfo { PrimaryID = 9903, ImcSubsetID = 1, SecondaryID = 0 }
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Legs (NPC)",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[7],
                ModelInfo = new XivModelInfo { PrimaryID = 9903, ImcSubsetID = 1, SecondaryID = 0 }
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Feet (NPC)",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[8],
                ModelInfo = new XivModelInfo { PrimaryID = 9903, ImcSubsetID = 1, SecondaryID = 0 }
            };

            xivGearList.Add(xivGear);

            xivGear = new XivGear
            {
                Name = "SmallClothes Feet 2 (NPC)",
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = _slotNameDictionary[8],
                ModelInfo = new XivModelInfo { PrimaryID = 9901, ImcSubsetID = 1, SecondaryID = 0 }
            };

            xivGearList.Add(xivGear);

            return xivGearList;
        }



        // A dictionary containing <Slot ID, Gear Category>
        private readonly Dictionary<int, string> _slotNameDictionary = new Dictionary<int, string>
        {
            {0, XivStrings.Food },
            {1, XivStrings.Main_Hand },
            {2, XivStrings.Off_Hand },
            {3, XivStrings.Head },
            {4, XivStrings.Body },
            {5, XivStrings.Hands },
            {6, XivStrings.Waist },
            {7, XivStrings.Legs },
            {8, XivStrings.Feet },
            {9, XivStrings.Earring },
            {10, XivStrings.Neck },
            {11, XivStrings.Wrists },
            {12, XivStrings.Rings },
            {13, XivStrings.Two_Handed },
            {14, XivStrings.Main_Off },
            {15, XivStrings.Head_Body },
            {16, XivStrings.Body_Hands_Legs_Feet },
            {17, XivStrings.Soul_Crystal },
            {18, XivStrings.Legs_Feet },
            {19, XivStrings.All },
            {20, XivStrings.Body_Hands_Legs },
            {21, XivStrings.Body_Legs_Feet },
            {22, XivStrings.Body_Hands },
            {23, XivStrings.Body_Legs }
        };

    }
}
