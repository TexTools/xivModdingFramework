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

            if (item.SecondaryCategory.Equals(XivStrings.Main_Hand) || item.SecondaryCategory.Equals(XivStrings.Off_Hand) || 
                item.SecondaryCategory.Equals(XivStrings.Main_Off) || item.SecondaryCategory.Equals(XivStrings.Two_Handed) || item.SecondaryCategory.Equals(XivStrings.Food))
            {
                itemType = XivItemType.weapon;
            }
            else if (item.PrimaryCategory.Equals(XivStrings.Gear) && (item.SecondaryCategory.Equals(XivStrings.Ears) || item.SecondaryCategory.Equals(XivStrings.Neck) || 
                     item.SecondaryCategory.Equals(XivStrings.Wrists) || item.SecondaryCategory.Equals(XivStrings.Rings)))
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
            if (item.SecondaryCategory.Equals(XivStrings.Main_Hand) || item.SecondaryCategory.Equals(XivStrings.Off_Hand) ||
                item.SecondaryCategory.Equals(XivStrings.Main_Off) || item.SecondaryCategory.Equals(XivStrings.Two_Handed) || item.SecondaryCategory.Equals(XivStrings.Food)
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
            else if (item.PrimaryCategory.Equals(XivStrings.Character) && item.SecondaryCategory.Equals( XivStrings.Ears ))
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
                modelInfo = ((IItemModel)item).ModelInfo;
                primaryId = modelInfo.PrimaryID.ToString().PadLeft(4, '0');
                secondaryId = modelInfo.SecondaryID.ToString().PadLeft(4, '0');
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
                var uiItem = (XivUi)item;
                return uiItem.UiPath + uiItem.IconNumber.ToString().PadLeft(6, '0');
            }
            else if (primaryType == XivItemType.furniture)
            {
                if (item.SecondaryCategory == XivStrings.Paintings)
                {
                    return "ui/icon/" + modelInfo.PrimaryID.ToString().PadLeft(6, '0');
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
                    ret += "ears/z";
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
