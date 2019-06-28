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
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Variants.FileTypes;
using xivModdingFramework.VFX.FileTypes;

namespace xivModdingFramework.Textures.FileTypes
{
    public class ATex
    {
        private readonly DirectoryInfo _gameDirectory;
        private readonly XivDataFile _dataFile;

        public ATex(DirectoryInfo gameDirectory, XivDataFile dataFile)
        {
            _gameDirectory = gameDirectory;
            _dataFile = dataFile;
        }

        /// <summary>
        /// Gets the atex paths for a given item
        /// </summary>
        /// <param name="itemModel">The item to get the atex paths for</param>
        /// <returns>A list of TexTypePath containing the atex info</returns>
        public async Task<List<TexTypePath>> GetAtexPaths(IItemModel itemModel)
        {
            var atexTexTypePathList = new List<TexTypePath>();

            var index = new Index(_gameDirectory);
            var avfx = new Avfx(_gameDirectory, _dataFile);

            var itemType = ItemType.GetItemType(itemModel);

            var vfxPath = await GetVfxPath(itemModel, itemType);

            var vfxOffset = await index.GetDataOffset(HashGenerator.GetHash(vfxPath.Folder), HashGenerator.GetHash(vfxPath.File),
                _dataFile);

            if (vfxOffset == 0)
            {
                throw new Exception($"Could not find offset for vfx path {vfxPath.Folder}/{vfxPath.File}");
            }

            var aTexPaths = await avfx.GetATexPaths(vfxOffset);

            foreach (var atexPath in aTexPaths)
            {
                var ttp = new TexTypePath
                {
                    DataFile = _dataFile,
                    Name = Path.GetFileNameWithoutExtension(atexPath),
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
        public async Task<XivTex> GetATexData(int offset)
        {
            var dat = new Dat(_gameDirectory);
            var atexData = await dat.GetType2Data(offset, _dataFile);

            var xivTex = new XivTex();

            using (var br = new BinaryReader(new MemoryStream(atexData)))
            {
                var signature = br.ReadInt32();
                xivTex.TextureFormat = Dat.TextureTypeDictionary[br.ReadInt32()];
                xivTex.Width = br.ReadInt16();
                xivTex.Height = br.ReadInt16();

                br.ReadBytes(2);

                xivTex.MipMapCount = br.ReadInt16();

                br.ReadBytes(64);

                xivTex.TexData = br.ReadBytes(atexData.Length - 80);
            }

            return xivTex;
        }

        /// <summary>
        /// Gets the avfx path
        /// </summary>
        /// <param name="itemModel">The item to get the avfx path for</param>
        /// <param name="itemType">The type of the item</param>
        /// <returns>A tuple containing the path folder and file</returns>
        private async Task<(string Folder, string File)> GetVfxPath(IItemModel itemModel, XivItemType itemType)
        {
            // get the vfx version from the imc file
            var imc = new Imc(_gameDirectory, _dataFile);
            var imcInfo = await imc.GetImcInfo(itemModel, itemModel.ModelInfo);
            int vfx = imcInfo.Vfx;

            var id = itemModel.ModelInfo.ModelID.ToString().PadLeft(4, '0');
            var bodyVer = itemModel.ModelInfo.Body.ToString().PadLeft(4, '0');

            string vfxFolder, vfxFile;

            switch (itemType)
            {
                case XivItemType.equipment:
                    vfxFolder = $"chara/{itemType}/e{id}/vfx/eff";
                    vfxFile = $"ve{vfx.ToString().PadLeft(4, '0')}.avfx";
                    break;
                case XivItemType.weapon:
                    vfxFolder = $"chara/{itemType}/w{id}/obj/body/b{bodyVer}/vfx/eff";
                    vfxFile = $"vw{vfx.ToString().PadLeft(4, '0')}.avfx";
                    break;
                case XivItemType.monster:
                    vfxFolder = $"chara/{itemType}/m{id}/obj/body/b{bodyVer}/vfx/eff";
                    vfxFile = $"vm{vfx.ToString().PadLeft(4, '0')}.avfx";
                    break;
                case XivItemType.demihuman:
                    vfxFolder = $"chara/{itemType}/d{id}/obj/equipment/e{bodyVer}/vfx/eff";
                    vfxFile = $"ve{vfx.ToString().PadLeft(4, '0')}.avfx";
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