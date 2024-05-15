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
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Mods;
using xivModdingFramework.Resources;
using xivModdingFramework.Textures.FileTypes;

namespace xivModdingFramework.Items.DataContainers
{
    /// <summary>
    /// This class holds information for Items in the UI Category
    /// </summary>
    public class XivUi : IItem
    {
        /// <summary>
        /// The name of the UI item
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Main Category
        /// </summary>
        /// <remarks>
        /// For UI the Main Category is "UI"
        /// </remarks>
        public string PrimaryCategory { get; set; }

        /// <summary>
        /// The items category
        /// </summary>
        /// <remarks>
        /// This would be a category such as Maps, Actions, Status, Weather
        /// </remarks>
        public string SecondaryCategory { get; set; }

        /// <summary>
        /// The items SubCategory
        /// </summary>
        /// <remarks>
        /// This would be a category such as a maps region names and action types
        /// </remarks>
        public string TertiaryCategory { get; set; }


        public string MapZoneCategory { get; set; }

        /// <summary>
        /// The data file the item belongs to
        /// </summary>
        /// <remarks>
        /// Minion items are always in 060000
        /// </remarks>
        public XivDataFile DataFile { get; set; } = XivDataFile._06_Ui;

        /// <summary>
        /// The internal UI path
        /// </summary>
        public string UiPath { get; set; }

        /// <summary>
        /// The Icon Number
        /// </summary>
        public int IconNumber { get; set; }

        /// <summary>
        /// Whether or not this file has a hi-res equivalent
        /// </summary>
        public bool HasHiRes { get; private set; }

        /// <summary>
        /// Gets the item's name as it should be written to the modlist/modpack files.
        /// </summary>
        /// <returns></returns>
        public string GetModlistItemName()
        {
            return Name != null ? Name : "Unknown UI Element";
        }

        /// <summary>
        /// Gets the item's category as it should be written to the modlist/modpack files.
        /// </summary>
        /// <returns></returns>
        public string GetModlistItemCategory()
        {
            return SecondaryCategory != null ? SecondaryCategory : XivStrings.UI;
        }

        public int CompareTo(object obj)
        {
            return string.Compare(Name, ((XivUi)obj).Name, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            try
            {
                XivUi other = (XivUi)obj;
                return (this.Name == other.Name && this.IconNumber == other.IconNumber);
            }
            catch
            {
                return false;
            }
        }

        public override int GetHashCode()
        {
            if(this.Name == null)
            {
                return 0;
            }
            return this.Name.GetHashCode() ^ this.IconNumber.GetHashCode();
        }

        private const string HiResUiExt = "_hr1";

        /// <summary>
        /// Retrieve texture paths.  
        /// If both low and high res are false, low res is still returned.
        /// Maps only have a single resolution.
        /// </summary>
        /// <param name="addLowRes"></param>
        /// <param name="addHiRes"></param>
        /// <returns></returns>
        public async Task<Dictionary<string, string>> GetTexPaths(bool addLowRes = true, bool addHiRes = false, ModTransaction tx = null)
        {
            var resPaths = new Dictionary<string, string>();

            if(SecondaryCategory == XivStrings.Maps)
            {
                var _tex = new Tex(XivCache.GameInfo.GameDirectory);

                var mapNamePaths = await _tex.GetMapAvailableTex(UiPath, tx);
                return mapNamePaths;
            } else if(SecondaryCategory == XivStrings.HUD)
            {
                //ui/uld/aozactionlearned.tex
                HasHiRes = true;

                if (addHiRes && !addLowRes)
                {
                    resPaths.Add(Name, "ui/uld/" + Name.ToLower() + HiResUiExt + ".tex");
                } else if(addHiRes && addLowRes)
                {
                    resPaths.Add(Name + "SD", "ui/uld/" + Name.ToLower() + ".tex");
                    resPaths.Add(Name + "HD", "ui/uld/" + Name.ToLower() + HiResUiExt + ".tex");
                } else
                {
                    resPaths.Add(Name, "ui/uld/" + Name.ToLower() + ".tex");
                }
            } else if(SecondaryCategory == XivStrings.LoadingScreen)
            {
                resPaths.Add(Name, UiPath + '/' + Name + ".tex");
            }
            else
            {
                HasHiRes = true;

                var block = ((IconNumber / 1000) * 1000).ToString().PadLeft(6,'0');
                var icon = IconNumber.ToString().PadLeft(6, '0');

                if (addHiRes && !addLowRes)
                {
                    resPaths.Add(Name, "ui/icon/" + block + '/' + icon + HiResUiExt + ".tex");
                }
                else if (addHiRes && addLowRes)
                {
                    resPaths.Add(Name + "SD", "ui/icon/" + block + '/' + icon + ".tex");
                    resPaths.Add(Name + "HD", "ui/icon/" + block + '/' + icon + HiResUiExt + ".tex");
                } else
                {
                    resPaths.Add(Name, "ui/icon/" + block + '/' + icon + ".tex");
                }
            }

            return resPaths;
        }
    }
}