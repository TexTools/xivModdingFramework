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
using System.Text.RegularExpressions;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Resources;

namespace xivModdingFramework.Items
{
    /// <summary>
    /// This class contains the getter for the item type
    /// </summary>
    public static class ItemType
    {
        /// <summary>
        /// Gets the type of the item
        /// </summary>
        /// <remarks>
        /// The item type is determined by its ItemCategory
        /// </remarks>
        /// <see cref="XivItemType"/>
        /// <param name="item">The item to get the type for</param>
        /// <returns>The XivItemType containing the type of the item</returns>
        public static XivItemType GetPrimaryItemType(this IItem item)
        {
            XivItemType itemType;

            if (item.PrimaryCategory == null || item.SecondaryCategory == null) return XivItemType.unknown;

            if (item.SecondaryCategory.Equals(XivStrings.Main_Hand) || item.SecondaryCategory.Equals(XivStrings.Off_Hand) || 
                item.SecondaryCategory.Equals(XivStrings.Main_Off) || item.SecondaryCategory.Equals(XivStrings.Two_Handed) || item.SecondaryCategory.Equals(XivStrings.Dual_Wield) || item.SecondaryCategory.Equals(XivStrings.Food))
            {
                itemType = XivItemType.weapon;

                try
                {
                    // Check to see if we're an equipment item masquerading as a weapon.
                    var mi = (XivGearModelInfo)((IItemModel)item).ModelInfo;
                    if (mi != null)
                    {
                        if (!mi.IsWeapon)
                        {
                            itemType = XivItemType.equipment;
                        }
                    }
                }
                catch
                {
                    //No-op.
                }
            }
            else if (item.PrimaryCategory.Equals(XivStrings.Gear) && (item.SecondaryCategory.Equals(XivStrings.Earring) || item.SecondaryCategory.Equals(XivStrings.Neck) || 
                     item.SecondaryCategory.Equals(XivStrings.Wrists) || item.SecondaryCategory.Equals(XivStrings.Rings) || item.SecondaryCategory.Equals(XivStrings.LeftRing)))
            {
                itemType = XivItemType.accessory;
            }
            else if (item.SecondaryCategory.Equals(XivStrings.Mounts) || item.SecondaryCategory.Equals(XivStrings.Minions) || item.SecondaryCategory.Equals(XivStrings.Pets)
                     || item.SecondaryCategory.Equals(XivStrings.Monster) || item.SecondaryCategory.Equals(XivStrings.Ornaments))
            {
                // This is a little squiggly.  Monster/Demihuman support needs work across the board though
                // So not going to worry about making this better just yet.
                try
                {
                    var modelInfo = (XivMonsterModelInfo)(((IItemModel)item).ModelInfo);
                    itemType = modelInfo.ModelType;
                } catch(Exception ex)
                {
                    itemType = XivItemType.monster;
                }
            }
            else if (item.SecondaryCategory.Equals(XivStrings.DemiHuman))
            {
                itemType = XivItemType.demihuman;
            }
            else if (item.PrimaryCategory.Equals(XivStrings.Character))
            {
                itemType = XivItemType.human;
            }
            else if (item.SecondaryCategory.Equals("UI"))
            {
                itemType = XivItemType.ui;
            }
            else if (item.PrimaryCategory.Equals(XivStrings.Housing))
            {
                if(item.SecondaryCategory.Equals(XivStrings.Paintings))
                {
                    itemType = XivItemType.ui;
                } else
                {
                    itemType = XivItemType.furniture;
                }
            } else if(item.SecondaryCategory.Equals(XivStrings.Equipment_Decals) || item.SecondaryCategory.Equals(XivStrings.Face_Paint))
            {
                itemType = XivItemType.decal;
            }
            else
            {
                itemType = XivItemType.equipment;
            }

            return itemType;
        }

        /// <summary>
        /// Retrieves an item's secondary item type based on it's category information.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public static XivItemType GetSecondaryItemType(this IItem item)
        {
            var itemType = XivItemType.none;

            // Weapons, Monsters of all kinds, and the character Body use the body type secondary identifier.
            if (item.SecondaryCategory.Equals(XivStrings.Main_Hand) || item.SecondaryCategory.Equals(XivStrings.Off_Hand) || item.SecondaryCategory.Equals(XivStrings.Dual_Wield)
                || item.SecondaryCategory.Equals(XivStrings.Main_Off) || item.SecondaryCategory.Equals(XivStrings.Two_Handed) || item.SecondaryCategory.Equals(XivStrings.Food)
                || item.PrimaryCategory.Equals(XivStrings.Companions) || item.PrimaryCategory.Equals(XivStrings.Monster) || item.SecondaryCategory.Equals(XivStrings.Body))
            {
                itemType = XivItemType.body;
            } else if(item.SecondaryCategory.Equals( XivStrings.Face ))
            {
                itemType = XivItemType.face;
            }
            else if (item.SecondaryCategory.Equals( XivStrings.Tail ))
            {
                itemType = XivItemType.tail;
            }
            else if (item.SecondaryCategory.Equals(XivStrings.Hair))
            {
                itemType = XivItemType.hair;
            }
            else if (item.PrimaryCategory.Equals(XivStrings.Character) && item.SecondaryCategory.Equals( XivStrings.Ear ))
            {
                itemType = XivItemType.ear;
            }
            else if (item.SecondaryCategory.Equals( XivStrings.DemiHuman))
            {
                itemType = XivItemType.equipment;
            }

            return itemType;
        }

        /// <summary>
        /// Gets the item's slot abbreviation, such at 'dwn', or 'top'.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public static string GetItemSlotAbbreviation(this IItem item)
        {
            // Test to make sure we're in the right category for these.
            if (item.SecondaryCategory == XivStrings.Dual_Wield)
            {
                try
                {
                    var im = ((XivGearModelInfo)((XivGear)item).ModelInfo);
                    if(!im.IsWeapon)
                    {
                        return Mdl.SlotAbbreviationDictionary[XivStrings.Hands];
                    }
                }
                catch
                {
                    // Use the default logic if we failed the cast.
                }
            }

            if(item.SecondaryCategory == XivStrings.Rings)
            {
                // Rings contain both left and right rings.
                if (item.Name.EndsWith(XivStrings.Left))
                {
                    return "ril";
                }
                else if (item.Name.EndsWith(XivStrings.Right))
                {
                    return "rir";
                }
            }

            if(item.GetType() == typeof(XivMount))
            {
                var m = (XivMount)item;
                if(m.ModelInfo != null && m.ModelInfo.GetType() == typeof(XivMonsterModelInfo))
                {
                    var mi = (XivMonsterModelInfo)m.ModelInfo;
                    if(mi.ModelType == XivItemType.demihuman)
                    {
                        // Slot has to be extracted from name here.
                        var rex = new Regex("d[0-9]{4}e[0-9]{4}_([a-z]{3})");
                        var match = rex.Match(item.Name);
                        if(match.Success)
                        {
                            return match.Groups[1].Value;
                        }
                    }
                }
            }

            // This is not actually correct currently for Demihuman models.
            // Demihuman models have their own Equipment slots, but that information is currently
            // Not pulled into textools, so they'll just end up with missing slot abbreviations.
            if (Mdl.SlotAbbreviationDictionary.ContainsKey(item.SecondaryCategory))
            {
                return Mdl.SlotAbbreviationDictionary[item.SecondaryCategory];
            }
            return "";
        }

        public static readonly Dictionary<XivItemType, char> XivItemTypePrefixes = new Dictionary<XivItemType, char>()
        {
            { XivItemType.unknown, '-' },
            { XivItemType.weapon, 'w' },
            { XivItemType.equipment, 'e' },
            { XivItemType.accessory, 'a' },
            { XivItemType.monster, 'm' },
            { XivItemType.demihuman, 'd' },
            { XivItemType.body, 'b' },
            { XivItemType.face, 'f' },
            { XivItemType.tail, 't' },
            { XivItemType.hair, 'h' },
            { XivItemType.ear, 'z' },
            { XivItemType.human, '-' },
            { XivItemType.ui, '-' },
            { XivItemType.furniture, '-' },
        };


        /// <summary>
        /// Get the prefix letter for a given item type, or '-' if none exists.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static char GetItemTypePrefix(XivItemType type)
        {
            if(XivItemTypePrefixes.ContainsKey(type))
            {
                return XivItemTypePrefixes[type];
            }
            return '-';
        }

        // Zero(th) group is the full file path.
        // First group is the item root path.
        // Second group is the imc file name.
        private static readonly Regex _equipmentRegex = new Regex("^(chara/equipment/(e[0-9]{4})/).*");
        private static readonly Regex _accessoryRegex = new Regex("^(chara/accessory/(a[0-9]{4})/).*");
        private static readonly Regex _weaponRegex = new Regex("^(chara/weapon/w[0-9]{4}/obj/body/(b[0-9]{4})/).*");
        private static readonly Regex _monsterRegex = new Regex("^(chara/monster/m[0-9]{4}/obj/body/(b[0-9]{4})/).*");
        private static readonly Regex _demihumanRegex = new Regex("^(chara/demihuman/d[0-9]{4}/obj/equipment/(e[0-9]{4})/).*");
        public static XivItemType GetItemTypeFromPath(string path)
        {

            if (_equipmentRegex.IsMatch(path)) return XivItemType.equipment;
            if (_accessoryRegex.IsMatch(path)) return XivItemType.accessory;
            if (_weaponRegex.IsMatch(path)) return XivItemType.weapon;
            if (_monsterRegex.IsMatch(path)) return XivItemType.monster;
            if (_demihumanRegex.IsMatch(path)) return XivItemType.demihuman;

            return XivItemType.unknown;
        }

        /// <summary>
        /// Finds the root folder for an item, from any child path.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string GetRootFolderFromPath(string path)
        {
            var match = _equipmentRegex.Match(path);
            if (match.Success) return match.Groups[1].Value;
            match = _accessoryRegex.Match(path);
            if (match.Success) return match.Groups[1].Value;
            match = _weaponRegex.Match(path);
            if (match.Success) return match.Groups[1].Value;
            match = _monsterRegex.Match(path);
            if (match.Success) return match.Groups[1].Value;
            match = _demihumanRegex.Match(path);
            if (match.Success) return match.Groups[1].Value;

            return null;
        }

        /// <summary>
        /// Finds suffix-less -name- of the IMC File for a given child path..
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static string GetIMCNameFromChildPath(string path)
        {
            var match = _equipmentRegex.Match(path);
            if (match.Success) return match.Groups[2].Value;
            match = _accessoryRegex.Match(path);
            if (match.Success) return match.Groups[2].Value;
            match = _weaponRegex.Match(path);
            if (match.Success) return match.Groups[2].Value;
            match = _monsterRegex.Match(path);
            if (match.Success) return match.Groups[2].Value;
            match = _demihumanRegex.Match(path);
            if (match.Success) return match.Groups[2].Value;

            return null;
        }

        /// <summary>
        /// Gets the IMC path for a given child path.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string GetIMCPathFromChildPath(string path)
        {

            var imcName = GetIMCNameFromChildPath(path);
            var rootFolder = GetRootFolderFromPath(path);
            if (imcName == null || rootFolder == null)
            {
                return null;
            }

            return rootFolder + imcName + ".imc";
        }


        /// <summary>
        /// Get this item's root folder in the FFXIV internal directory structure.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public static string GetItemRootFolder(this IItem item)
        {
            var primaryType = item.GetPrimaryItemType();
            var secondaryType = item.GetSecondaryItemType();
            var primaryId = "";
            var secondaryId = "";
            XivModelInfo modelInfo = null;
            try {
                // Hitting this catch 60,000 time for all the UI elements is really slow.
                if (item != null && primaryType != XivItemType.ui)
                {
                    modelInfo = ((IItemModel)item).ModelInfo;
                    if (modelInfo != null)
                    {
                        primaryId = modelInfo.PrimaryID.ToString().PadLeft(4, '0');
                        secondaryId = modelInfo.SecondaryID.ToString().PadLeft(4, '0');
                    }
                }
            } catch(Exception ex)
            {
                // No-op.  If it failed it's one of the types we're not going to use it modelInfo on anyways.
            }


            if (primaryType == XivItemType.monster)
            {
                return "chara/monster/m" + primaryId + "/obj/body/b" + secondaryId;
            }
            else if (primaryType == XivItemType.demihuman)
            {
                return "chara/demihuman/d" + primaryId + "/obj/equipment/e" + secondaryId;
            }
            else if(primaryType == XivItemType.equipment)
            {
                return "chara/equipment/e" + primaryId;
            }
            else if (primaryType == XivItemType.accessory)
            {
                return "chara/accessory/a" + primaryId;
            }
            else if (primaryType == XivItemType.ui)
            {
                if (item.SecondaryCategory == XivStrings.Paintings)
                {
                    try
                    {
                        var furnitureItem = (IItemModel)item;
                        modelInfo = furnitureItem.ModelInfo;
                        return "ui/icon/" + modelInfo.PrimaryID.ToString().PadLeft(6, '0');
                    } catch
                    {
                        var uiItem = (XivUi)item;
                        return "ui/icon/" + uiItem.IconNumber.ToString().PadLeft(6, '0');
                    }
                }
                else
                {
                    var uiItem = (XivUi)item;
                    return uiItem.UiPath + uiItem.IconNumber.ToString().PadLeft(6, '0');
                }
            }
            else if (primaryType == XivItemType.furniture)
            {
                if (item.SecondaryCategory == XivStrings.Paintings)
                {
                    try
                    {
                        var furnitureItem = (IItemModel)item;
                        modelInfo = furnitureItem.ModelInfo;
                        return "ui/icon/" + modelInfo.PrimaryID.ToString().PadLeft(6, '0');
                    }
                    catch
                    {
                        var uiItem = (XivUi)item;
                        return "ui/icon/" + uiItem.IconNumber.ToString().PadLeft(6, '0');
                    }
                }
                else
                {
                    var ret = "bgcommon/hou/";
                    if (item.SecondaryCategory == XivStrings.Furniture_Indoor)
                    {
                        ret += "indoor/";
                    }
                    else if (item.SecondaryCategory == XivStrings.Furniture_Outdoor)
                    {
                        ret += "outdoor/";
                    }

                    ret += "general/" + primaryId;
                    return ret;
                }
            }
            else if (primaryType == XivItemType.weapon)
            {
                return "chara/weapon/w" + primaryId + "/obj/body/b" + secondaryId;
            } 
            else if(primaryType == XivItemType.human)
            {
                var ret = "chara/human/c" + primaryId + "/obj/";
                if (secondaryType == XivItemType.body)
                {
                    ret += "body/b";
                }
                else if (secondaryType == XivItemType.face)
                {
                    ret += "face/f";
                }
                else if (secondaryType == XivItemType.tail)
                {
                    ret += "tail/t";
                }
                else if (secondaryType == XivItemType.hair)
                {
                    ret += "hair/h";
                }
                else if (secondaryType == XivItemType.ear)
                {
                    ret += "zear/z";
                }

                ret += secondaryId;
                return ret;
            } 
            else if(primaryType == XivItemType.decal)
            {
                if (item.SecondaryCategory == XivStrings.Face_Paint)
                {
                    return "chara/common/texture/decal_face";
                }
                if (item.SecondaryCategory == XivStrings.Equipment_Decals)
                {
                    return "chara/common/texture/decal_equip";
                }
            }
            return "";
        }
    }
}
