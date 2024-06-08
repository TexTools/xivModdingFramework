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
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Mods;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Variants.FileTypes;
using xivModdingFramework.VFX.FileTypes;

using Index = xivModdingFramework.SqPack.FileTypes.Index;

namespace xivModdingFramework.Textures.FileTypes
{
    public static class ATex
    {
        /// <summary>
        /// Gets the atex paths for a given item
        /// </summary>
        /// <param name="itemModel">The item to get the atex paths for</param>
        /// <returns>A list of TexTypePath containing the atex info</returns>
        public static async Task<List<string>> GetAtexPaths(IItemModel itemModel, bool forceOriginal = false, ModTransaction tx = null)
        {
            // Gear is the only type we know how to retrieve atex information for.
            if (itemModel.GetType() != typeof(XivGear)) return new List<string>();



            var vfxPath = await GetVfxPath(itemModel, false, tx);
            return await GetAtexPaths(vfxPath.Folder + '/' + vfxPath.File, forceOriginal, tx);
        }
        public static async Task<List<string>> GetAtexPaths(string vfxPath, bool forceOriginal = false, ModTransaction tx = null)
        {
            if (!IOUtil.IsFFXIVInternalPath(vfxPath))
            {
                return new List<string>();
            }
            if(tx == null)
            {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginTransaction();
            }

            if (!await tx.FileExists(vfxPath))
            {
                return new List<string>();
            }

            return await Avfx.GetATexPaths(vfxPath, forceOriginal, tx);
        }

        public static char GetVfxPrefix(XivDependencyRootInfo root)
        {
            var itemType = root.PrimaryType;
            if(root.PrimaryType == XivItemType.demihuman)
            {
                itemType = (XivItemType)root.SecondaryType;
            }

            if(itemType == XivItemType.equipment)
            {
                return 'e';
            } else if (itemType == XivItemType.weapon)
            {
                return 'w';
            }
            else if (itemType == XivItemType.monster)
            {
                return 'm';
            }
            else
            {
                return '\0';
            }
        }

        /// <summary>
        /// Gets the avfx path
        /// </summary>
        /// <param name="itemModel">The item to get the avfx path for</param>
        /// <param name="itemType">The type of the item</param>
        /// <returns>A tuple containing the path folder and file</returns>
        public static async Task<(string Folder, string File)> GetVfxPath(IItemModel itemModel, bool forceDefault = false, ModTransaction tx = null)
        {
            // get the vfx version from the imc file
            var imcInfo = await Imc.GetImcInfo(itemModel, forceDefault, tx);
            if(imcInfo == null)
            {
                return ("", "");
            }

            int vfx = imcInfo.Vfx;

            var root = itemModel.GetRootInfo();
            return await GetVfxPath(root, vfx);

        }
        public static async Task<(string Folder, string File)> GetVfxPath(XivDependencyRootInfo root, int vfx) { 

            var type = root.PrimaryType;
            var id = root.PrimaryId.ToString().PadLeft(4, '0');
            var bodyVer = root.SecondaryId.ToString().PadLeft(4, '0');
            var prefix = XivItemTypes.GetSystemPrefix(type);
            var vfxPrefix = GetVfxPrefix(root);

            string vfxFolder, vfxFile;

            switch (type)
            {

                case XivItemType.equipment:
                    vfxFolder = $"chara/{type}/{prefix}{id}/vfx/eff";
                    vfxFile = $"v{vfxPrefix}{vfx.ToString().PadLeft(4, '0')}.avfx";
                    break;
                case XivItemType.weapon:
                case XivItemType.monster:
                    vfxFolder = $"chara/{type}/{prefix}{id}/obj/body/b{bodyVer}/vfx/eff";
                    vfxFile = $"v{vfxPrefix}{vfx.ToString().PadLeft(4, '0')}.avfx";
                    break;
                case XivItemType.demihuman:
                    vfxFolder = $"chara/{type}/{prefix}{id}/obj/equipment/e{bodyVer}/vfx/eff";
                    vfxFile = $"v{vfxPrefix}{vfx.ToString().PadLeft(4, '0')}.avfx";
                    break;
                default:
                    vfxFolder = "";
                    vfxFile = "";
                    break;
            }

            return (vfxFolder, vfxFile);
        }
    }
}