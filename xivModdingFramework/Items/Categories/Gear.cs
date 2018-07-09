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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using xivModdingFramework.Exd.Enums;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Resources;

namespace xivModdingFramework.Items.Categories
{
    /// <summary>
    /// This class is used to obtain a list of available gear
    /// This includes equipment, accessories, and weapons
    /// Food is a special case as it is within the chara/weapons directory
    /// </summary>
    public class Gear
    {
        private readonly DirectoryInfo _gameDirectory;
        private readonly XivLanguage _xivLanguage;
        public Gear(DirectoryInfo gameDirectory, XivLanguage xivLanguage)
        {
            _gameDirectory = gameDirectory;
            _xivLanguage = xivLanguage;
        }

        /// <summary>
        /// A getter for available gear in the Item exd files
        /// </summary>
        /// <returns>A list containing XivGear data</returns>
        public List<XivGear> GetGearList()
        {
            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int modelDataCheckOffset = 31;
            const int dataLength = 152;
            const int nameDataOffset = 14;
            const int modelDataOffset = 24;
            const int iconDataOffset = 128;
            const int slotDataOffset = 146;

            var xivGearList = new List<XivGear>();

            var ex = new Ex(_gameDirectory, _xivLanguage);
            var itemDictionary = ex.ReadExData(XivEx.item);

            // Loops through all the items in the item exd files
            // Item files start at 0 and increment by 500 for each new file
            // Item_0, Item_500, Item_1000, etc.
            foreach (var item in itemDictionary.Values)
            {
                // This checks whether there is any model data present in the current item
                if (item[modelDataCheckOffset] <= 0) continue;

                // Gear can have 2 separate models (MNK weapons for example)
                var primaryMi = new XivModelInfo();
                var secondaryMi = new XivModelInfo();

                var xivGear = new XivGear
                {
                    Category = XivStrings.Gear,
                    PrimaryModelInfo = primaryMi,
                    SecondaryModelInfo = secondaryMi
                };

                /* Used to determine if the given model is a weapon
                 * This is important because the data is formated differently
                 * The model data is a 16 byte section separated into two 8 byte parts (primary model, secondary model)
                 * Format is 8 bytes in length with 2 bytes per data point [short, short, short, short]
                 * Gear: primary model [blank, blank, variant, ID] nothing in secondary model
                 * Weapon: primary model [blank, variant, body, ID] secondary model [blank, variant, body, ID]
                */
                var isWeapon = false;

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(item)))
                {
                    br.BaseStream.Seek(nameDataOffset, SeekOrigin.Begin);
                    var nameOffset = br.ReadInt16();

                    // Model Data
                    br.BaseStream.Seek(modelDataOffset, SeekOrigin.Begin);

                    // Primary Blank
                    primaryMi.Unused = br.ReadInt16();

                    // Primary Variant for weapon, blank otherwise
                    var weaponVariant = br.ReadInt16();

                    if (weaponVariant != 0)
                    {
                        primaryMi.Variant = weaponVariant;
                        isWeapon = true;
                    }

                    // Primary Body if weapon, Variant otherwise
                    if (isWeapon)
                    {
                        primaryMi.Body = br.ReadInt16();
                    }
                    else
                    {
                        primaryMi.Variant = br.ReadInt16();
                    }

                    // Primary Model ID
                    primaryMi.ModelID = br.ReadInt16();

                    // Secondary Blank
                    secondaryMi.Unused = br.ReadInt16();

                    // Secondary Variant for weapon, blank otherwise
                    weaponVariant = br.ReadInt16();

                    if (weaponVariant != 0)
                    {
                        secondaryMi.Variant = weaponVariant;
                        isWeapon = true;
                    }

                    // Secondary Body if weapon, Variant otherwise
                    if (isWeapon)
                    {
                        secondaryMi.Body = br.ReadInt16();
                    }
                    else
                    {
                        secondaryMi.Variant = br.ReadInt16();
                    }

                    // Secondary Model ID
                    secondaryMi.ModelID = br.ReadInt16();

                    // Icon
                    br.BaseStream.Seek(iconDataOffset, SeekOrigin.Begin);
                    xivGear.IconNumber = br.ReadUInt16();

                    // Gear Slot/Category
                    br.BaseStream.Seek(slotDataOffset, SeekOrigin.Begin);
                    int slotNum = br.ReadByte();
                    xivGear.ItemCategory = _slotNameDictionary.ContainsKey(slotNum) ? _slotNameDictionary[slotNum] : "Unknown";

                    // Gear Name
                    var gearNameOffset = dataLength + nameOffset;
                    var gearNameLength = item.Length - gearNameOffset;
                    br.BaseStream.Seek(gearNameOffset, SeekOrigin.Begin);
                    var nameString = Encoding.UTF8.GetString(br.ReadBytes(gearNameLength)).Replace("\0", "");
                    xivGear.Name = new string(nameString.Where(c => !char.IsControl(c)).ToArray());

                    xivGearList.Add(xivGear);
                }
            }

            return xivGearList;
        }

        // A dictionary containg <Slot ID, Gear Category>
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
            {9, XivStrings.Ears },
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
            {21, XivStrings.Body_Legs_Feet }
        };
    }
}