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

using HelixToolkit.SharpDX.Core.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Resources;
using xivModdingFramework.Resources;

namespace xivModdingFramework.General.Enums
{

    public enum XivBaseRace : byte
    {
        Hyur = 0,
        Elezen = 1,
        Lalafell = 2,
        Miqote = 3,
        Roegadyn = 4,
        AuRa = 5,
        Hrothgar = 6,
        Viera = 7
    };

    public enum XivSubRace : byte
    {
        Hyur_Midlander,
        Hyur_Highlander,
        Elezen_Wildwood,
        Elezen_Duskwight,
        Lalafell_Plainsfolk,
        Lalafell_Dunesfolk,
        Miqote_Seeker,
        Miqote_Keeper,
        Roegadyn_SeaWolf,
        Roegadyn_Hellsguard,
        AuRa_Raen,
        AuRa_Xaela,
        Hrothgar_Helion,
        Hrothgar_Lost,
        Viera_Rava,
        Viera_Veena,
    };
    public enum XivGender : byte
    {
        Male = 0,
        Female = 1
    };

    /// <summary>
    /// Enum containing all known races
    /// </summary>
    /// <remarks>
    /// Some of the NPC races don't seem to be present anywhere
    /// They were added to the list in case they are ever used in the future
    /// </remarks>
    public enum XivRace
    {
        [Description("0101")] Hyur_Midlander_Male = 101,
        [Description("0104")] Hyur_Midlander_Male_NPC = 104,
        [Description("0201")] Hyur_Midlander_Female = 201,
        [Description("0204")] Hyur_Midlander_Female_NPC = 204,
        [Description("0301")] Hyur_Highlander_Male = 301,
        [Description("0304")] Hyur_Highlander_Male_NPC = 304,
        [Description("0401")] Hyur_Highlander_Female = 401,
        [Description("0404")] Hyur_Highlander_Female_NPC = 404,
        [Description("0501")] Elezen_Male = 501,
        [Description("0504")] Elezen_Male_NPC = 504,
        [Description("0601")] Elezen_Female = 601,
        [Description("0604")] Elezen_Female_NPC = 604,
        [Description("0701")] Miqote_Male = 0701,
        [Description("0704")] Miqote_Male_NPC = 0704,
        [Description("0801")] Miqote_Female = 801,
        [Description("0804")] Miqote_Female_NPC = 804,
        [Description("0901")] Roegadyn_Male = 901,
        [Description("0904")] Roegadyn_Male_NPC = 904,
        [Description("1001")] Roegadyn_Female = 1001,
        [Description("1004")] Roegadyn_Female_NPC = 1004,
        [Description("1101")] Lalafell_Male = 1101,
        [Description("1104")] Lalafell_Male_NPC = 1104,
        [Description("1201")] Lalafell_Female = 1201,
        [Description("1204")] Lalafell_Female_NPC = 1204,
        [Description("1301")] AuRa_Male = 1301,
        [Description("1304")] AuRa_Male_NPC = 1304,
        [Description("1401")] AuRa_Female = 1401,
        [Description("1404")] AuRa_Female_NPC = 1404,
        [Description("1501")] Hrothgar_Male = 1501,
        [Description("1504")] Hrothgar_Male_NPC = 1504,

        [Description("1601")] Hrothgar_Female = 1601,
        [Description("1604")] Hrothgar_Female_NPC = 1604,

        [Description("1701")] Viera_Male = 1701,
        [Description("1704")] Viera_Male_NPC = 1704,
        [Description("1801")] Viera_Female = 1801,
        [Description("1804")] Viera_Female_NPC = 1804,
        [Description("9104")] NPC_Male = 9104,
        [Description("9204")] NPC_Female = 9204,
        [Description("0000")] All_Races = 0000,
        [Description("0000")] Monster = 0001,
        [Description("0000")] DemiHuman = 0002,
    }

    public class XivRaceNode {
        public XivRaceNode Parent;
        public List<XivRaceNode> Children;
        public XivRace Race;
        public bool HasSkin = false;
    }

    public static class XivRaceTree
    {
        private static XivRaceNode tree;
        private static Dictionary<XivRace, XivRaceNode> dict;


        // Validates the underlying tree structure, generating it if needed.
        private static void CheckTree()
        {
            if (tree != null) return;
            dict = new Dictionary<XivRace, XivRaceNode>();
            // Common Race Males
            dict.Add(XivRace.Hyur_Midlander_Male, new XivRaceNode()
            {
                Race = XivRace.Hyur_Midlander_Male,
                Children = new List<XivRaceNode>(),
                HasSkin = true
            });
            dict.Add(XivRace.Elezen_Male, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Male],
                Race = XivRace.Elezen_Male,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.Miqote_Male, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Male],
                Race = XivRace.Miqote_Male,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.AuRa_Male, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Male],
                Race = XivRace.AuRa_Male,
                Children = new List<XivRaceNode>(),
                HasSkin = true
            });
            dict.Add(XivRace.Viera_Male, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Male],
                Race = XivRace.Viera_Male,
                Children = new List<XivRaceNode>(),
                HasSkin = true
            });


            // Muscular Race Males
            dict.Add(XivRace.Hyur_Highlander_Male, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Male],
                Race = XivRace.Hyur_Highlander_Male,
                Children = new List<XivRaceNode>(),
                HasSkin = true
            });
            dict.Add(XivRace.Roegadyn_Male, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Male],
                Race = XivRace.Roegadyn_Male,
                Children = new List<XivRaceNode>(),
                HasSkin = true
            });
            dict.Add(XivRace.Hrothgar_Male, new XivRaceNode()
            {
                Parent = dict[XivRace.Roegadyn_Male],
                Race = XivRace.Hrothgar_Male,
                Children = new List<XivRaceNode>(),
                HasSkin = true
            });


            // Common Race Females
            dict.Add(XivRace.Hyur_Midlander_Female, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Male],
                Race = XivRace.Hyur_Midlander_Female,
                Children = new List<XivRaceNode>(),
                HasSkin = true
            });
            dict.Add(XivRace.Elezen_Female, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Female],
                Race = XivRace.Elezen_Female,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.Viera_Female, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Female],
                Race = XivRace.Viera_Female,
                Children = new List<XivRaceNode>(),
                HasSkin = true
            });
            dict.Add(XivRace.Miqote_Female, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Female],
                Race = XivRace.Miqote_Female,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.AuRa_Female, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Female],
                Race = XivRace.AuRa_Female,
                Children = new List<XivRaceNode>(),
                HasSkin = true
            });
#if DAWNTRAIL
            dict.Add(XivRace.Hrothgar_Female, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Female],
                Race = XivRace.Hrothgar_Female,
                Children = new List<XivRaceNode>(),
                HasSkin = true
            });
#endif


            // Muscular Race Females
            dict.Add(XivRace.Hyur_Highlander_Female, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Female],
                Race = XivRace.Hyur_Highlander_Female,
                Children = new List<XivRaceNode>(),
                HasSkin = true
            });
            dict.Add(XivRace.Roegadyn_Female, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Female],
                Race = XivRace.Roegadyn_Female,
                Children = new List<XivRaceNode>()
            });


            // Lalas
            dict.Add(XivRace.Lalafell_Male, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Male],
                Race = XivRace.Lalafell_Male,
                Children = new List<XivRaceNode>(),
                HasSkin = true
            });
            dict.Add(XivRace.Lalafell_Female, new XivRaceNode()
            {
                Parent = dict[XivRace.Lalafell_Male],
                Race = XivRace.Lalafell_Female,
                Children = new List<XivRaceNode>()
            });

            // NPCs
            dict.Add(XivRace.Hyur_Midlander_Male_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Male],
                Race = XivRace.Hyur_Midlander_Male_NPC,
                Children = new List<XivRaceNode>()
            });

            dict.Add(XivRace.Hyur_Midlander_Female_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Female],
                Race = XivRace.Hyur_Midlander_Female_NPC,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.Hyur_Highlander_Male_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Highlander_Male],
                Race = XivRace.Hyur_Highlander_Male_NPC,
                Children = new List<XivRaceNode>()
            });


            dict.Add(XivRace.Hyur_Highlander_Female_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Highlander_Female],
                Race = XivRace.Hyur_Highlander_Female_NPC,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.AuRa_Male_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.AuRa_Male],
                Race = XivRace.AuRa_Male_NPC,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.AuRa_Female_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.AuRa_Female],
                Race = XivRace.AuRa_Female_NPC,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.Roegadyn_Male_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Roegadyn_Male],
                Race = XivRace.Roegadyn_Male_NPC,
                Children = new List<XivRaceNode>()
            });

            dict.Add(XivRace.Roegadyn_Female_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Roegadyn_Female],
                Race = XivRace.Roegadyn_Female_NPC,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.Elezen_Male_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Elezen_Male],
                Race = XivRace.Elezen_Male_NPC,
                Children = new List<XivRaceNode>()
            });

            dict.Add(XivRace.Elezen_Female_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Elezen_Female],
                Race = XivRace.Elezen_Female_NPC,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.Miqote_Male_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Miqote_Male],
                Race = XivRace.Miqote_Male_NPC,
                Children = new List<XivRaceNode>()
            });

            dict.Add(XivRace.Miqote_Female_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Miqote_Female],
                Race = XivRace.Miqote_Female_NPC,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.Lalafell_Male_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Lalafell_Male],
                Race = XivRace.Lalafell_Male_NPC,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.Lalafell_Female_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Lalafell_Female],
                Race = XivRace.Lalafell_Female_NPC,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.Viera_Male_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Viera_Male],
                Race = XivRace.Viera_Male_NPC,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.Viera_Female_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Viera_Female],
                Race = XivRace.Viera_Female_NPC,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.Hrothgar_Male_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Hrothgar_Male],
                Race = XivRace.Hrothgar_Male_NPC,
                Children = new List<XivRaceNode>()
            });

            dict.Add(XivRace.Hrothgar_Female_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Hrothgar_Female],
                Race = XivRace.Hrothgar_Female_NPC,
                Children = new List<XivRaceNode>()
            });

            dict.Add(XivRace.NPC_Male, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Male],
                Race = XivRace.NPC_Male,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.NPC_Female, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Female],
                Race = XivRace.NPC_Female,
                Children = new List<XivRaceNode>()
            });


            tree = dict[XivRace.Hyur_Midlander_Male];
        }

        /// <summary>
        /// Gets the full race tree.
        /// </summary>
        /// <returns></returns>
        public static XivRaceNode GetFullRaceTree()
        {
            CheckTree();
            return dict[XivRace.Hyur_Midlander_Male];
        }

        /// <summary>
        /// Determines if this race is a child of another given race or not.
        /// If the values are the same, it is considered TRUE by default.
        /// </summary>
        /// <param name="possibleChild"></param>
        /// <param name="possibleParent"></param>
        /// <returns></returns>
        public static bool IsChildOf(this XivRace possibleChild, XivRace possibleParent, bool allowSame = true)
        {
            CheckTree();
            if (possibleChild == possibleParent && allowSame)
            {
                return true;
            }
            var isParent = IsParentOf(possibleParent, possibleChild, allowSame);
            return isParent;
        }

        /// <summary>
        /// Determines if this race is a parent of another given race or not.
        /// If the values are the same, it is considered TRUE by default.
        /// </summary>
        /// <param name="possibleParent"></param>
        /// <param name="possibleChild"></param>
        /// <returns></returns>
        public static bool IsParentOf(this XivRace possibleParent, XivRace possibleChild, bool allowSame = true)
        {
            CheckTree();
            if (possibleChild == possibleParent && allowSame)
            {
                return true;
            }

            var node = GetNode(possibleChild);
            if (node == null) return false;

            while (node.Parent != null)
            {
                node = node.Parent;
                if (node.Race == possibleParent)
                {
                    return true;
                }
            }
            return false;
        }

        public static XivRace GetNextChildToward(this XivRace parentRace, XivRace childRace)
        {
            if(!parentRace.IsParentOf(childRace))
            {
                return XivRace.All_Races;
            }

            var node = GetNode(childRace);
            if(node == null)
            {
                return XivRace.All_Races;
            }

            var race = childRace;
            while (node.Parent != null && node.Parent.Race != parentRace)
            {
                node = node.Parent;
                race = node.Race;
            }
            return race;
        }

        /// <summary>
        /// Determines if this race is a direct parent of another given race or not.
        /// If the values are the same, it is considered TRUE by default.
        /// </summary>
        /// <param name="possibleParent"></param>
        /// <param name="possibleChild"></param>
        /// <returns></returns>
        public static bool IsDirectParentOf(this XivRace possibleParent, XivRace possibleChild, bool allowSame = true)
        {
            CheckTree();
            if (possibleChild == possibleParent && allowSame)
            {
                return true;
            }

            var child = GetNode(possibleChild);

            if (child?.Parent != null)
            {
                if (child.Parent.Race == possibleParent)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Retrieves the tree node for the given race.
        /// </summary>
        /// <param name="race"></param>
        /// <returns></returns>
        public static XivRaceNode GetNode(this XivRace race)
        {
            CheckTree();
            return dict[race];
        }

        /// <summary>
        /// Gets the full list of children for this node.
        /// </summary>
        /// <param name="race"></param>
        /// <param name="includeNPCs"></param>
        /// <returns></returns>
        public static List<XivRace> GetChildren(this XivRace race, bool includeNPCs = false)
        {
            CheckTree();
            var node = GetNode(race);
            var name = node.Race.ToString();

            // Skip NPCs
            if (name.Contains("NPC") && !includeNPCs) return new List<XivRace>();

            // Return ourselves if no children.
            if (node.Children == null || node.Children.Count == 0)
                return new List<XivRace>() { race };

            // Recursion for children.
            var children = new List<XivRace>();
            foreach (var c in node.Children)
            {
                children.AddRange(GetChildren(c.Race, includeNPCs));
            }

            // Final return.
            return children;
        }

        /// <summary>
        /// Gets the first ancestor with a skin.
        /// </summary>
        /// <param name="race"></param>
        /// <returns></returns>
        public static XivRace GetSkinRace(this XivRace race)
        {
            var node = GetNode(race);
            if (node == null)
            {
                return XivRace.Hyur_Midlander_Male;
            }

            // Roe F is very weird and uses Highlander F's skin materials,
            // but Midlander F's models.  Blame SE hard-coding shit.
            if(node.Race == XivRace.Roegadyn_Female)
            {
                return XivRace.Hyur_Highlander_Female;
            }

            if (node.HasSkin)
            {
                return node.Race;
            }

            while (node.Parent != null)
            {
                node = node.Parent;
                if (node.HasSkin)
                    return node.Race;
            }

            return XivRace.Hyur_Midlander_Male;
        }


        /// <summary>
        /// Retrieves the subrace offset for this race.
        /// </summary>
        /// <param name="race"></param>
        /// <returns></returns>
        public static int GetSubRaceId(this XivSubRace subrace)
        {
            return (((int)subrace)) % 2;
        }
        public static XivBaseRace GetBaseRace(this XivSubRace subrace)
        {
            byte rId = (byte) (((int)subrace) / 2);
            return (XivBaseRace) rId;
        }

        public static string GetDisplayName(this XivSubRace subrace)
        {
            var rm = new ResourceManager(typeof(XivStrings));
            var displayName = rm.GetString(subrace.ToString());
            return displayName;
        }

        /// <summary>
        /// Retrieves the base race enum value for this race/clan/gender race.
        /// Used for CMP files and a few other things.
        /// </summary>
        /// <param name="race"></param>
        /// <returns></returns>
        public static XivBaseRace GetBaseRace(this XivRace race)
        {
            switch(race)
            {
                case XivRace.Hyur_Midlander_Male:
                case XivRace.Hyur_Midlander_Female:
                case XivRace.Hyur_Midlander_Male_NPC:
                case XivRace.Hyur_Midlander_Female_NPC:
                case XivRace.Hyur_Highlander_Male:
                case XivRace.Hyur_Highlander_Female:
                case XivRace.Hyur_Highlander_Male_NPC:
                case XivRace.Hyur_Highlander_Female_NPC:
                    return XivBaseRace.Hyur;
                case XivRace.Elezen_Male:
                case XivRace.Elezen_Female:
                case XivRace.Elezen_Male_NPC:
                case XivRace.Elezen_Female_NPC:
                    return XivBaseRace.Elezen;
                case XivRace.Lalafell_Male:
                case XivRace.Lalafell_Female:
                case XivRace.Lalafell_Male_NPC:
                case XivRace.Lalafell_Female_NPC:
                    return XivBaseRace.Lalafell;
                case XivRace.Miqote_Male:
                case XivRace.Miqote_Female:
                case XivRace.Miqote_Male_NPC:
                case XivRace.Miqote_Female_NPC:
                    return XivBaseRace.Miqote;
                case XivRace.Roegadyn_Male:
                case XivRace.Roegadyn_Female:
                case XivRace.Roegadyn_Male_NPC:
                case XivRace.Roegadyn_Female_NPC:
                    return XivBaseRace.Roegadyn;
                case XivRace.AuRa_Male:
                case XivRace.AuRa_Female:
                case XivRace.AuRa_Male_NPC:
                case XivRace.AuRa_Female_NPC:
                    return XivBaseRace.AuRa;
                case XivRace.Viera_Male:
                case XivRace.Viera_Female:
                case XivRace.Viera_Male_NPC:
                case XivRace.Viera_Female_NPC:
                    return XivBaseRace.Viera;
                case XivRace.Hrothgar_Male:
                case XivRace.Hrothgar_Male_NPC:
                case XivRace.Hrothgar_Female:
                case XivRace.Hrothgar_Female_NPC:
                    return XivBaseRace.Hrothgar;
                default:
                    return XivBaseRace.Hyur;
            }
        }

        /// <summary>
        /// Gets the internal FFXIV MTRL path for a given race's skin, using the tree as needed to find the appropriate ancestor skin.
        /// </summary>
        /// <param name="race"></param>
        /// <returns></returns>
        public static string GetSkinPath(this XivRace startingRace, int body = 1, string materialId = "a")
        {
            var race = GetSkinRace(startingRace);
            if(race != startingRace)
            {
                body = 1;
            }

            var bodyCode = body.ToString().PadLeft(4, '0');
            var path = "/chara/human/c" + race.GetRaceCode() + "/obj/body/b" + bodyCode + "/material/v0001/mt_c" + race.GetRaceCode() + "b" + bodyCode + "_" + materialId + ".mtrl";
            return path;
        }
    }

    /// <summary>
    /// Class used to get certain values from the enum
    /// </summary>
    public static class XivRaces
    {
        public static readonly List<XivRace> PlayableRaces = new List<XivRace>() {
            XivRace.Hyur_Midlander_Male,
            XivRace.Hyur_Midlander_Female,
            XivRace.Hyur_Highlander_Male,
            XivRace.Hyur_Highlander_Female,
            XivRace.Elezen_Male,
            XivRace.Elezen_Female,
            XivRace.Miqote_Male,
            XivRace.Miqote_Female,
            XivRace.Roegadyn_Male,
            XivRace.Roegadyn_Female,
            XivRace.Lalafell_Male,
            XivRace.Lalafell_Female,
            XivRace.AuRa_Male,
            XivRace.AuRa_Female,
            XivRace.Hrothgar_Male,
            XivRace.Hrothgar_Female,
            XivRace.Viera_Female,
            XivRace.Viera_Male,
        };

        /// <summary>
        /// Gets the description from the enum value, in this case the Race Code
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The race code</returns>
        public static string GetRaceCode(this XivRace value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = (DescriptionAttribute[])field.GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attribute.Length > 0 ? attribute[0].Description : value.ToString();
        }
        public static int GetRaceCodeInt(this XivRace value)
        {
            return Int32.Parse(GetRaceCode(value));
        }


        /// <summary>
        /// This function gets a semi-arbitrarily defined list of model priority for model conversions.
        /// In specific, this is used for deciding which model to base new racial models off of.
        /// 
        /// These are grouped and ordered in order to try and reduce the chances of pulling a model which 
        /// won't transform correctly as the new base model.
        /// </summary>
        /// <param name="race"></param>
        /// <returns></returns>
        public static List<XivRace> GetModelPriorityList(this XivRace race)
        {
            var ret = new List<XivRace>();
            if (race == XivRace.All_Races) throw new InvalidDataException("Cannot get model priority list for race-less model.");

            switch(race)
            {
                // Standard Female Races
                case XivRace.Hyur_Midlander_Female:
                case XivRace.Hyur_Midlander_Female_NPC:
                case XivRace.Miqote_Female:
                case XivRace.Miqote_Female_NPC:
                case XivRace.AuRa_Female:
                case XivRace.AuRa_Female_NPC:
                case XivRace.Viera_Female:
                case XivRace.Viera_Female_NPC:
                case XivRace.Hyur_Highlander_Female:
                case XivRace.Hyur_Highlander_Female_NPC:
                case XivRace.Elezen_Female:
                case XivRace.Elezen_Female_NPC:
                case XivRace.Roegadyn_Female:
                case XivRace.Roegadyn_Female_NPC:
                case XivRace.Hrothgar_Female:
                case XivRace.Hrothgar_Female_NPC:
                    return new List<XivRace>()
                    {
                        // Standard Female Races First
                        XivRace.Hyur_Midlander_Female,
                        XivRace.Hyur_Midlander_Female_NPC,
                        XivRace.Miqote_Female,
                        XivRace.Miqote_Female_NPC,
                        XivRace.AuRa_Female,
                        XivRace.AuRa_Female_NPC,
                        XivRace.Viera_Female,
                        XivRace.Viera_Female_NPC,
                        XivRace.Hyur_Highlander_Female,
                        XivRace.Hyur_Highlander_Female_NPC,
                        XivRace.Elezen_Female,
                        XivRace.Elezen_Female_NPC,
                        XivRace.Roegadyn_Female,
                        XivRace.Roegadyn_Female_NPC,
                        XivRace.Hrothgar_Female,
                        XivRace.Hrothgar_Female_NPC,

                        // Male Base Races Next
                        XivRace.Hyur_Midlander_Male,
                        XivRace.Hyur_Midlander_Male_NPC,
                        XivRace.Miqote_Male,
                        XivRace.Miqote_Male_NPC,
                        XivRace.Elezen_Male,
                        XivRace.Elezen_Male_NPC,
                        XivRace.AuRa_Male,
                        XivRace.AuRa_Male_NPC,
                        XivRace.Viera_Male,
                        XivRace.Viera_Male_NPC,

                        // Highlander Next
                        XivRace.Hyur_Highlander_Male,
                        XivRace.Hyur_Highlander_Male_NPC,

                        // Roe M?  These are pretty fucked at this point.
                        XivRace.Roegadyn_Male,
                        XivRace.Roegadyn_Male_NPC,
                        XivRace.Hrothgar_Male,
                        XivRace.Hrothgar_Male_NPC,

                        // Lala ? 
                        XivRace.Lalafell_Male,
                        XivRace.Lalafell_Male_NPC,
                        XivRace.Lalafell_Female,
                        XivRace.Lalafell_Female_NPC
                    };

                // Male base races.
                case XivRace.Hyur_Midlander_Male:
                case XivRace.Hyur_Midlander_Male_NPC:
                case XivRace.Miqote_Male:
                case XivRace.Miqote_Male_NPC:
                case XivRace.Elezen_Male:
                case XivRace.Elezen_Male_NPC:
                case XivRace.AuRa_Male:
                case XivRace.AuRa_Male_NPC:
                case XivRace.Viera_Male:
                case XivRace.Viera_Male_NPC:
                    return new List<XivRace>()
                    {
                        // Male Base Races First
                        XivRace.Hyur_Midlander_Male,
                        XivRace.Hyur_Midlander_Male_NPC,
                        XivRace.Miqote_Male,
                        XivRace.Miqote_Male_NPC,
                        XivRace.Elezen_Male,
                        XivRace.Elezen_Male_NPC,
                        XivRace.AuRa_Male,
                        XivRace.AuRa_Male_NPC,
                        XivRace.Viera_Male,
                        XivRace.Viera_Male_NPC,

                        // Highlander Next
                        XivRace.Hyur_Highlander_Male,
                        XivRace.Hyur_Highlander_Male_NPC,
                        
                        // Standard Female Races Next ?  We're getting into trouble territory here.
                        XivRace.Hyur_Midlander_Female,
                        XivRace.Hyur_Midlander_Female_NPC,
                        XivRace.Miqote_Female,
                        XivRace.Miqote_Female_NPC,
                        XivRace.AuRa_Female,
                        XivRace.AuRa_Female_NPC,
                        XivRace.Viera_Female,
                        XivRace.Viera_Female_NPC,
                        XivRace.Hyur_Highlander_Female,
                        XivRace.Hyur_Highlander_Female_NPC,
                        XivRace.Elezen_Female,
                        XivRace.Elezen_Female_NPC,
                        XivRace.Roegadyn_Female,
                        XivRace.Roegadyn_Female_NPC,
                        XivRace.Hrothgar_Female,
                        XivRace.Hrothgar_Female_NPC,

                        // Roe M? These are pretty fucked at this point.
                        XivRace.Roegadyn_Male,
                        XivRace.Roegadyn_Male_NPC,
                        XivRace.Hrothgar_Male,
                        XivRace.Hrothgar_Male_NPC,

                        // Lala ? 
                        XivRace.Lalafell_Male,
                        XivRace.Lalafell_Male_NPC,
                        XivRace.Lalafell_Female,
                        XivRace.Lalafell_Female_NPC
                    };

                // Highlander gets slightly different resolution order.
                case XivRace.Hyur_Highlander_Male:
                case XivRace.Hyur_Highlander_Male_NPC:
                    return new List<XivRace>()
                    {
                        // Highlander First
                        XivRace.Hyur_Highlander_Male,
                        XivRace.Hyur_Highlander_Male_NPC,
                        
                        // Male Base Races Next
                        XivRace.Hyur_Midlander_Male,
                        XivRace.Hyur_Midlander_Male_NPC,
                        XivRace.Miqote_Male,
                        XivRace.Miqote_Male_NPC,
                        XivRace.Elezen_Male,
                        XivRace.Elezen_Male_NPC,
                        XivRace.AuRa_Male,
                        XivRace.AuRa_Male_NPC,
                        XivRace.Viera_Male,
                        XivRace.Viera_Male_NPC,

                        // Standard Female Races Next ?  We're getting into trouble territory here.
                        XivRace.Hyur_Midlander_Female,
                        XivRace.Hyur_Midlander_Female_NPC,
                        XivRace.Miqote_Female,
                        XivRace.Miqote_Female_NPC,
                        XivRace.AuRa_Female,
                        XivRace.AuRa_Female_NPC,
                        XivRace.Viera_Female,
                        XivRace.Viera_Female_NPC,
                        XivRace.Hyur_Highlander_Female,
                        XivRace.Hyur_Highlander_Female_NPC,
                        XivRace.Elezen_Female,
                        XivRace.Elezen_Female_NPC,
                        XivRace.Roegadyn_Female,
                        XivRace.Roegadyn_Female_NPC,
                        XivRace.Hrothgar_Female,
                        XivRace.Hrothgar_Female_NPC,

                        // Roe M? These are pretty fucked at this point.
                        XivRace.Roegadyn_Male,
                        XivRace.Roegadyn_Male_NPC,
                        XivRace.Hrothgar_Male,
                        XivRace.Hrothgar_Male_NPC,

                        // Lala ? 
                        XivRace.Lalafell_Male,
                        XivRace.Lalafell_Male_NPC,
                        XivRace.Lalafell_Female,
                        XivRace.Lalafell_Female_NPC
                    };

                // Big Boys
                case XivRace.Roegadyn_Male:
                case XivRace.Roegadyn_Male_NPC:
                case XivRace.Hrothgar_Male:
                case XivRace.Hrothgar_Male_NPC:
                    return new List<XivRace>()
                    {
                        // Roe M
                        XivRace.Roegadyn_Male,
                        XivRace.Roegadyn_Male_NPC,
                        XivRace.Hrothgar_Male,
                        XivRace.Hrothgar_Male_NPC,

                        // Highlander Next
                        XivRace.Hyur_Highlander_Male,
                        XivRace.Hyur_Highlander_Male_NPC,
                        
                        // Male Base Races Next
                        XivRace.Hyur_Midlander_Male,
                        XivRace.Hyur_Midlander_Male_NPC,
                        XivRace.Miqote_Male,
                        XivRace.Miqote_Male_NPC,
                        XivRace.Elezen_Male,
                        XivRace.Elezen_Male_NPC,
                        XivRace.AuRa_Male,
                        XivRace.AuRa_Male_NPC,
                        XivRace.Viera_Male,
                        XivRace.Viera_Male_NPC,

                        // Standard Female Races Next ?  We're getting into trouble territory here.
                        XivRace.Hyur_Midlander_Female,
                        XivRace.Hyur_Midlander_Female_NPC,
                        XivRace.Miqote_Female,
                        XivRace.Miqote_Female_NPC,
                        XivRace.AuRa_Female,
                        XivRace.AuRa_Female_NPC,
                        XivRace.Viera_Female,
                        XivRace.Viera_Female_NPC,
                        XivRace.Hyur_Highlander_Female,
                        XivRace.Hyur_Highlander_Female_NPC,
                        XivRace.Elezen_Female,
                        XivRace.Elezen_Female_NPC,
                        XivRace.Roegadyn_Female,
                        XivRace.Roegadyn_Female_NPC,
                        XivRace.Hrothgar_Female,
                        XivRace.Hrothgar_Female_NPC,

                        // Lala ? 
                        XivRace.Lalafell_Male,
                        XivRace.Lalafell_Male_NPC,
                        XivRace.Lalafell_Female,
                        XivRace.Lalafell_Female_NPC
                    };

                // 'Taters
                case XivRace.Lalafell_Male:
                case XivRace.Lalafell_Male_NPC:
                case XivRace.Lalafell_Female:
                case XivRace.Lalafell_Female_NPC:
                    return new List<XivRace>()
                    {
                        // Lala
                        XivRace.Lalafell_Male,
                        XivRace.Lalafell_Male_NPC,
                        XivRace.Lalafell_Female,
                        XivRace.Lalafell_Female_NPC,
                        
                        // Male Base Races Next
                        XivRace.Hyur_Midlander_Male,
                        XivRace.Hyur_Midlander_Male_NPC,
                        XivRace.Miqote_Male,
                        XivRace.Miqote_Male_NPC,
                        XivRace.Elezen_Male,
                        XivRace.Elezen_Male_NPC,
                        XivRace.AuRa_Male,
                        XivRace.AuRa_Male_NPC,
                        XivRace.Viera_Male,
                        XivRace.Viera_Male_NPC,
                        
                        // Standard Female Races Next ?  We're getting into trouble territory here.
                        XivRace.Hyur_Midlander_Female,
                        XivRace.Hyur_Midlander_Female_NPC,
                        XivRace.Miqote_Female,
                        XivRace.Miqote_Female_NPC,
                        XivRace.AuRa_Female,
                        XivRace.AuRa_Female_NPC,
                        XivRace.Viera_Female,
                        XivRace.Viera_Female_NPC,
                        XivRace.Hyur_Highlander_Female,
                        XivRace.Hyur_Highlander_Female_NPC,
                        XivRace.Elezen_Female,
                        XivRace.Elezen_Female_NPC,
                        XivRace.Roegadyn_Female,
                        XivRace.Roegadyn_Female_NPC,
                        XivRace.Hrothgar_Female,
                        XivRace.Hrothgar_Female_NPC,

                        // Highlander Next
                        XivRace.Hyur_Highlander_Male,
                        XivRace.Hyur_Highlander_Male_NPC,

                        // Roe M
                        XivRace.Roegadyn_Male,
                        XivRace.Roegadyn_Male_NPC,
                        XivRace.Hrothgar_Male,
                        XivRace.Hrothgar_Male_NPC
                    };

            }
            throw new Exception("Unable to resolve racial model test order.");

        }

        /// <summary>
        /// Gets the Display Name of the Race from the Resource file in order to support localization
        /// </summary>
        /// <param name="value">The enum value</param>
        /// <returns>The localized display name of the race</returns>
        public static string GetDisplayName(this XivRace value)
        {
            var rm = new ResourceManager(typeof(XivStrings));
            var displayName = rm.GetString(value.ToString());

            return displayName;
        }


        /// <summary>
        /// Gets the enum value from the description
        /// </summary>
        /// <param name="value">The race string</param>
        /// <returns>The XivRace enum</returns>
        public static XivRace GetXivRaceFromDisplayName(string value)
        {
            var races = Enum.GetValues(typeof(XivRace)).Cast<XivRace>();

            return races.FirstOrDefault(race => race.GetDisplayName() == value);
        }


        /// <summary>
        /// Gets the enum value from the description
        /// </summary>
        /// <param name="value">The race string</param>
        /// <returns>The XivRace enum</returns>
        public static XivRace GetXivRace(string value)
        {
            var races = Enum.GetValues(typeof(XivRace)).Cast<XivRace>();

            return races.FirstOrDefault(race => race.GetRaceCode() == value);
        }

        public static XivRace GetXivRace(int value)
        {
            var code = value.ToString().PadLeft(4, '0');
            return GetXivRace(code);
        }

    }
}