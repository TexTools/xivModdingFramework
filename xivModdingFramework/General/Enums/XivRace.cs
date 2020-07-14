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
using System.Linq;
using System.Resources;
using xivModdingFramework.Resources;

namespace xivModdingFramework.General.Enums
{
    /// <summary>
    /// Enum containing all known races
    /// </summary>
    /// <remarks>
    /// Some of the NPC races don't seem to be present anywhere
    /// They were added to the list in case they are ever used in the future
    /// </remarks>
    public enum XivRace
    {
        [Description("0101")] Hyur_Midlander_Male,
        [Description("0104")] Hyur_Midlander_Male_NPC,
        [Description("0201")] Hyur_Midlander_Female,
        [Description("0204")] Hyur_Midlander_Female_NPC,
        [Description("0301")] Hyur_Highlander_Male,
        [Description("0304")] Hyur_Highlander_Male_NPC,
        [Description("0401")] Hyur_Highlander_Female,
        [Description("0404")] Hyur_Highlander_Female_NPC,
        [Description("0501")] Elezen_Male,
        [Description("0504")] Elezen_Male_NPC,
        [Description("0601")] Elezen_Female,
        [Description("0604")] Elezen_Female_NPC,
        [Description("0701")] Miqote_Male,
        [Description("0704")] Miqote_Male_NPC,
        [Description("0801")] Miqote_Female,
        [Description("0804")] Miqote_Female_NPC,
        [Description("0901")] Roegadyn_Male,
        [Description("0904")] Roegadyn_Male_NPC,
        [Description("1001")] Roegadyn_Female,
        [Description("1004")] Roegadyn_Female_NPC,
        [Description("1101")] Lalafell_Male,
        [Description("1104")] Lalafell_Male_NPC,
        [Description("1201")] Lalafell_Female,
        [Description("1204")] Lalafell_Female_NPC,
        [Description("1301")] AuRa_Male,
        [Description("1304")] AuRa_Male_NPC,
        [Description("1401")] AuRa_Female,
        [Description("1404")] AuRa_Female_NPC,
        [Description("1501")] Hrothgar,
        [Description("1504")] Hrothgar_NPC,
        [Description("1801")] Viera,
        [Description("1804")] Viera_NPC,
        [Description("9104")] NPC_Male,
        [Description("9204")] NPC_Female,
        [Description("0000")] All_Races,
        [Description("0000")] Monster,
        [Description("0000")] DemiHuman,


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
                Parent = dict[XivRace.Hyur_Highlander_Male],
                Race = XivRace.Roegadyn_Male,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.Hrothgar, new XivRaceNode()
            {
                Parent = dict[XivRace.Roegadyn_Male],
                Race = XivRace.Hrothgar,
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
            dict.Add(XivRace.Viera, new XivRaceNode()
            {
                Parent = dict[XivRace.Hyur_Midlander_Female],
                Race = XivRace.Viera,
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
                Parent = dict[XivRace.Hyur_Highlander_Female],
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
            dict.Add(XivRace.Viera_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Viera],
                Race = XivRace.Viera_NPC,
                Children = new List<XivRaceNode>()
            });
            dict.Add(XivRace.Hrothgar_NPC, new XivRaceNode()
            {
                Parent = dict[XivRace.Hrothgar],
                Race = XivRace.Hrothgar_NPC,
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
        /// </summary>
        /// <param name="possibleChild"></param>
        /// <param name="possibleParent"></param>
        /// <returns></returns>
        public static bool IsChildOf(this XivRace possibleChild, XivRace possibleParent)
        {
            CheckTree();
            var node = dict[possibleChild];
            while (node.Parent != null) {
                if(node.Race == possibleParent)
                {
                    return true;
                }
                node = node.Parent;
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
            foreach(var c in node.Children)
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
            if(node == null)
            {
                return XivRace.Hyur_Midlander_Male;
            }

            if(node.HasSkin)
            {
                return node.Race;
            }

            while(node.Parent != null)
            {
                node = node.Parent;
                if (node.HasSkin)
                    return node.Race;
            }

            return XivRace.Hyur_Midlander_Male;
        }
    }

    /// <summary>
    /// Class used to get certain values from the enum
    /// </summary>
    public static class XivRaces
    {
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
        public static XivRace GetXivRace(string value)
        {
            var races = Enum.GetValues(typeof(XivRace)).Cast<XivRace>();

            return races.FirstOrDefault(race => race.GetRaceCode() == value);
        }
    }
}