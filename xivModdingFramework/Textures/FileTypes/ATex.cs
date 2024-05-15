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
    public class ATex
    {
        private readonly DirectoryInfo _gameDirectory;

        public ATex(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
        }

        /// <summary>
        /// Gets the atex paths for a given item
        /// </summary>
        /// <param name="itemModel">The item to get the atex paths for</param>
        /// <returns>A list of TexTypePath containing the atex info</returns>
        public async Task<List<TexTypePath>> GetAtexPaths(IItemModel itemModel, bool forceOriginal = false, ModTransaction tx = null)
        {
            // Gear is the only type we know how to retrieve atex information for.
            if (itemModel.GetType() != typeof(XivGear)) return new List<TexTypePath>();



            var itemType = ItemType.GetPrimaryItemType(itemModel);

            var vfxPath = await GetVfxPath(itemModel);
            return await GetAtexPaths(vfxPath.Folder + '/' + vfxPath.File, forceOriginal, tx);
        }
        public async Task<List<TexTypePath>> GetAtexPaths(string vfxPath, bool forceOriginal = false, ModTransaction tx = null)
        {
            var df = IOUtil.GetDataFileFromPath(vfxPath);
            var index = new Index(_gameDirectory);
            var avfx = new Avfx(_gameDirectory, df);
            if(tx == null)
            {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginTransaction();
            }


            if (!await tx.FileExists(vfxPath))
            {
                return new List<TexTypePath>();
            }

            var atexTexTypePathList = new List<TexTypePath>();
            var aTexPaths = await avfx.GetATexPaths(vfxPath, forceOriginal, tx);          

            foreach (var atexPath in aTexPaths)
            {
                var ttp = new TexTypePath
                {
                    DataFile = df,
                    Name = "VFX: " + Path.GetFileNameWithoutExtension(atexPath),
                    Path = atexPath
                };

                atexTexTypePathList.Add(ttp);
            }

            return atexTexTypePathList;
        }

        /// <summary>
        /// Gets the ATex data
        /// </summary>
        /// <param name="offset">The offset to the ATex file</param>
        /// <returns>An XivTex with all the texture data</returns>
        public async Task<XivTex> GetATexData(long offset, XivDataFile df, ModTransaction tx = null)
        {
            var dat = new Dat(_gameDirectory);
            var atexData = await dat.ReadSqPackType2(offset, df, tx);

            var xivTex = new XivTex();
            xivTex.Layers = 1;

            using (var br = new BinaryReader(new MemoryStream(atexData)))
            {
                var signature = br.ReadInt32();
                xivTex.TextureFormat = Tex.TextureTypeDictionary[br.ReadInt32()];
                xivTex.Width = br.ReadInt16();
                xivTex.Height = br.ReadInt16();

                br.ReadBytes(2);

                xivTex.MipMapCount = br.ReadInt16();

                br.ReadBytes(64);

                xivTex.TexData = br.ReadBytes(atexData.Length - 80);
            }

            return xivTex;
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
            var imc = new Imc(XivCache.GameInfo.GameDirectory);
            var imcInfo = await imc.GetImcInfo(itemModel, forceDefault, tx);
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
            {//

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