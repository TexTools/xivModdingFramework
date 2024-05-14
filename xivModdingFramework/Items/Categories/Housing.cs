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
using System.Resources;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.Exd.Enums;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.General.DataContainers;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Mods;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;

using Index = xivModdingFramework.SqPack.FileTypes.Index;

namespace xivModdingFramework.Items.Categories
{
    public class Housing
    {
        private readonly DirectoryInfo _gameDirectory;
        private readonly XivLanguage _xivLanguage;
        public Housing(DirectoryInfo gameDirectory, XivLanguage xivLanguage)
        {
            _gameDirectory = gameDirectory;
            _xivLanguage = xivLanguage;
        }

        /// <summary>
        /// Gets the list of all Housing Items
        /// </summary>
        /// <returns>A list of XivFurniture objects containing housing items</returns>
        public async Task<List<XivFurniture>> GetFurnitureList(string substring = null)
        {
            return await XivCache.GetCachedFurnitureList(substring);
        }

        /// <summary>
        /// Gets the list of all Housing Items
        /// </summary>
        /// <returns>A list of XivFurniture objects containing housing items</returns>
        public async Task<List<XivFurniture>> GetUncachedFurnitureList()
        {
            var furnitureList = new List<XivFurniture>();

            furnitureList.AddRange(await GetIndoorFurniture());
            furnitureList.AddRange(await GetPaintings());
            furnitureList.AddRange(await GetOutdoorFurniture());

            return furnitureList;
        }


        /// <summary>
        /// Gets the list of indoor furniture
        /// </summary>
        /// <remarks>
        /// Housing items can be obtained one of two ways
        /// One: checking the housingfurniture exd for the item index, and going to that item to grab the data
        /// Two: iterating through the entire item list seeing if the item contains an index to a housing item (offset 112, 4 bytes)
        /// This method does option one
        /// </remarks>
        /// <returns>A list of XivFurniture objects containing indoor furniture item info</returns>
        private async Task<List<XivFurniture>> GetIndoorFurniture()
        {
            var indoorLock = new object();
            var ex = new Ex(_gameDirectory, _xivLanguage);
            var housingDictionary = await ex.ReadExData(XivEx.housingfurniture);
            var itemDictionary = await ex.ReadExData(XivEx.item);

            var furnitureList = new List<XivFurniture>();


            await Task.Run(() => Parallel.ForEach(housingDictionary.Values, (housingRow) =>
            {
                try
                {
                    var item = new XivFurniture
                    {
                        PrimaryCategory = XivStrings.Housing,
                        SecondaryCategory = XivStrings.Furniture_Indoor,
                        ModelInfo = new XivModelInfo()
                    };

                    var itemIndex = (uint)housingRow.GetColumnByName("ItemId");
                    item.ModelInfo.PrimaryID = (ushort) housingRow.GetColumnByName("PrimaryId");
                    var housingCategory = (byte) housingRow.GetColumnByName("Category");

                    // Get the associated item row.
                    var itemRow = itemDictionary[(int)itemIndex];
                    AttachItemInfo(item, itemRow);

                    if (!item.Name.Equals(string.Empty))
                    {
                        lock (indoorLock)
                        {
                            furnitureList.Add(item);
                        }
                    }
                } catch (Exception ex)
                {
                    throw;
                }
            }));

            furnitureList.Sort();

            return furnitureList;
        }

        private void AttachItemInfo(XivFurniture furnishing, Ex.ExdRow itemRow)
        {
            furnishing.IconNumber = (ushort)itemRow.GetColumnByName("Icon");
            furnishing.Name = (string)itemRow.GetColumnByName("Name");
        }

        /// <summary>
        /// Gets the list of indoor furniture
        /// </summary>
        /// <remarks>
        /// Housing items can be obtained one of two ways
        /// One: checking the housingfurniture exd for the item index, and going to that item to grab the data
        /// Two: iterating through the entire item list seeing if the item contains an index to a housing item (offset 112, 4 bytes)
        /// This method does option two as the item index was removed from the picture exd file in patch 5.2
        /// </remarks>
        /// <returns>A list of XivFurniture objects containing indoor furniture item info</returns>
        private async Task<List<XivFurniture>> GetPaintings()
        {
            var paintingsLock = new object();

            var ex = new Ex(_gameDirectory, _xivLanguage);
            var pictureDictionary = await ex.ReadExData(XivEx.picture);
            var itemDictionary = await ex.ReadExData(XivEx.item);

            var furnitureList = new List<XivFurniture>();

            await Task.Run(() => Parallel.ForEach(itemDictionary.Values, (itemRow) =>
            {
                var painting = new XivFurniture
                {
                    PrimaryCategory = XivStrings.Housing,
                    SecondaryCategory = XivStrings.Paintings,
                    ModelInfo = new XivModelInfo()
                };

                var pictureId = (uint) itemRow.GetColumnByName("PictureId");
                if (pictureId == 0 || pictureId > pictureDictionary.Count)
                    return;


                var name = (string)itemRow.GetColumnByName("Name");
                if(string.IsNullOrEmpty(name))
                {
                    return;
                }

                var filterGroup = (byte)itemRow.GetColumnByName("FilterGroup");
                if (filterGroup != 34)
                {
                    return;
                }



                AttachItemInfo(painting, itemRow);

                var pictureRow = pictureDictionary[(int)pictureId];
                painting.ModelInfo.PrimaryID = (int) pictureRow.GetColumnByName("PrimaryId");

                lock (paintingsLock)
                {
                    furnitureList.Add(painting);
                }
            }));

            furnitureList.Sort();

            return furnitureList;
        }

        /// <summary>
        /// Gets the list of outdoor furniture
        /// </summary>
        /// <returns>A list of XivFurniture objects containing outdoor furniture item info</returns>
        private async Task<List<XivFurniture>> GetOutdoorFurniture()
        {
            var outdoorLock = new object();
            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch


            var ex = new Ex(_gameDirectory, _xivLanguage);
            var housingEx = await ex.ReadExData(XivEx.housingyardobject);
            var itemsEx = await ex.ReadExData(XivEx.item);

            var furnitureList = new List<XivFurniture>();

            await Task.Run(() => Parallel.ForEach(housingEx.Values, (row) =>
            {
                var item = new XivFurniture
                {
                    PrimaryCategory = XivStrings.Housing,
                    SecondaryCategory = XivStrings.Furniture_Outdoor,
                    ModelInfo = new XivModelInfo()
                };

                var itemIndex = (int) ((uint) row.GetColumnByName("ItemId"));
                item.ModelInfo.PrimaryID = (ushort) row.GetColumnByName("PrimaryId");


                // Benchmark
                if (!itemsEx.ContainsKey(itemIndex))
                    return;

                AttachItemInfo(item, itemsEx[itemIndex]);

                if (!item.Name.Equals(string.Empty))
                {
                    lock (outdoorLock)
                    {
                        furnitureList.Add(item);
                    }
                }
            }));

            furnitureList.Sort();

            return furnitureList;
        }

        public async Task<Dictionary<string, string>> GetFurnitureModelParts(IItemModel itemModel, ModTransaction tx = null)
        {

            return await GetFurnitureModelParts(itemModel.ModelInfo.PrimaryID, itemModel.SecondaryCategory, tx);
        }


        public async Task<Dictionary<string, string>> GetFurnitureModelParts(int modelID, XivItemType type, ModTransaction tx = null)
        {
            var cat = type == XivItemType.indoor ? XivStrings.Furniture_Indoor : XivStrings.Furniture_Outdoor;
            return await GetFurnitureModelParts(modelID, cat, tx);
        }

        /// <summary>
        /// Gets the parts list for furniture
        /// </summary>
        /// <param name="itemModel">The item to get the parts for</param>
        /// <returns>A dictionary containing the part string and mdl path string</returns>
        public async Task<Dictionary<string, string>> GetFurnitureModelParts(int modelID, string category, ModTransaction tx = null)
        {
            var furniturePartDict = new Dictionary<string, string>();

            var assets = await GetFurnitureAssets(modelID, category, tx);

            foreach (var mdl in assets.MdlList)
            {
                if (mdl.Contains("base"))
                {
                    var part = mdl.Substring(mdl.LastIndexOf("_") + 1, 1);

                    furniturePartDict.Add($"base ( {part} )", mdl);
                }
                else if (mdl.Contains("al_"))
                {
                    var startIndex = mdl.IndexOf("_") + 1;
                    var length = mdl.LastIndexOf(".") - startIndex;

                    var part = mdl.Substring(startIndex, length);

                    furniturePartDict.Add($"{part}", mdl);
                }
                else
                {
                    var startIndex = mdl.IndexOf("_") + 1;
                    var length = mdl.LastIndexOf("_") - startIndex;

                    var part = mdl.Substring(startIndex, length);

                    if (!furniturePartDict.ContainsKey($"{part}"))
                    {
                        furniturePartDict.Add($"{part}", mdl);
                    }
                    else
                    {
                        part = mdl.Substring(startIndex, length);
                        var descriptor = mdl.Substring(mdl.LastIndexOf(".") - 1, 1);

                        if (furniturePartDict.ContainsKey($"{part} ( {descriptor} )"))
                        {
                            Debug.WriteLine($"Possible Duplicate: {mdl}");
                            continue;
                        }

                        furniturePartDict.Add($"{part} ( {descriptor} )", mdl);
                    }
                }
            }

            return furniturePartDict;
        }

            /// <summary>
            /// Gets the assets for furniture
            /// </summary>
            /// <param name="modelID">The model id to get the assets for</param>
            /// <returns>A HousingAssets object containing the asset info</returns>
        private async Task<HousingAssets> GetFurnitureAssets(int modelID, string category, ModTransaction tx = null)
        {
            var dat = new Dat(_gameDirectory);
            if(tx == null)
            {
                // Readonly TX if we don't have one;
                tx = ModTransaction.BeginTransaction();
            }

            var id = modelID.ToString().PadLeft(4, '0');

            var assetFolder = "";
            var assetFile = "";

            if (category.Equals(XivStrings.Furniture_Indoor))
            {
                assetFolder = $"bgcommon/hou/indoor/general/{id}/asset";
                assetFile = $"fun_b0_m{id}.sgb";
            }
            else if (category.Equals(XivStrings.Furniture_Outdoor))
            {
                assetFolder = $"bgcommon/hou/outdoor/general/{id}/asset";
                assetFile = $"gar_b0_m{id}.sgb";
            }

            var assetOffset = await tx.Get8xDataOffset(assetFolder + "/" + assetFile);

            var assetData = await dat.ReadSqPackType2(assetOffset, XivDataFile._01_Bgcommon, tx);

            var housingAssets = new HousingAssets();

            await Task.Run(() =>
            {
                using (var br = new BinaryReader(new MemoryStream(assetData)))
                {
                    br.BaseStream.Seek(20, SeekOrigin.Begin);

                    var skip = br.ReadInt32() + 20;

                    br.BaseStream.Seek(skip + 4, SeekOrigin.Begin);

                    var stringsOffset = br.ReadInt32();

                    br.BaseStream.Seek(skip + stringsOffset, SeekOrigin.Begin);

                    var pathCounts = 0;

                    while (true)
                    {
                        // Because we don't know the length of the string, we read the data until we reach a 0 value
                        // That 0 value is the space between strings
                        byte a;
                        var pathName = new List<byte>();
                        while ((a = br.ReadByte()) != 0)
                        {
                            if (a == 0xFF) break;

                            pathName.Add(a);
                        }

                        if (a == 0xFF) break;

                        // Read the string from the byte array and remove null terminators
                        var path = Encoding.ASCII.GetString(pathName.ToArray()).Replace("\0", "");

                        if (path.Equals(string.Empty)) continue;

                        // Add the attribute to the list
                        if (pathCounts == 0)
                        {
                            housingAssets.Shared = path;
                        }
                        else if (pathCounts == 1)
                        {
                            housingAssets.BaseFileName = path;
                        }
                        else
                        {
                            if (path.Contains(".mdl"))
                            {
                                housingAssets.MdlList.Add(path);
                            }
                            else if (path.Contains(".sgb"))
                            {
                                housingAssets.AdditionalAssetList.Add(path);
                            }
                            else if (!path.Contains("."))
                            {
                                housingAssets.BaseFolder = path;
                            }
                            else
                            {
                                housingAssets.OthersList.Add(path);
                            }
                        }

                        pathCounts++;
                    }
                }
            });

            if (housingAssets.AdditionalAssetList.Count > 0)
            {
                await GetAdditionalAssets(housingAssets, tx);
            }


            return housingAssets;
        }

        /// <summary>
        /// Gets additional assets when the original asset file contains asset file paths within it
        /// </summary>
        /// <param name="assets">The current asset object</param>
        private async Task GetAdditionalAssets(HousingAssets assets, ModTransaction tx = null)
        {
            if(tx == null)
            {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginTransaction();
            }

            var dat = new Dat(_gameDirectory);

            foreach (var additionalAsset in assets.AdditionalAssetList.ToList())
            {
                var assetFolder = Path.GetDirectoryName(additionalAsset).Replace("\\", "/");
                var assetFile = Path.GetFileName(additionalAsset);

                var assetOffset = await tx.Get8xDataOffset(additionalAsset);
                var assetData = await dat.ReadSqPackType2(assetOffset, XivDataFile._01_Bgcommon, tx);

                await Task.Run(() =>
                {
                    using (var br = new BinaryReader(new MemoryStream(assetData)))
                    {
                        br.BaseStream.Seek(20, SeekOrigin.Begin);

                        var skip = br.ReadInt32() + 20;

                        br.BaseStream.Seek(skip + 4, SeekOrigin.Begin);

                        var stringsOffset = br.ReadInt32();

                        br.BaseStream.Seek(skip + stringsOffset, SeekOrigin.Begin);

                        var pathCounts = 0;

                        while (true)
                        {
                            // Because we don't know the length of the string, we read the data until we reach a 0 value
                            // That 0 value is the space between strings
                            byte a;
                            var pathName = new List<byte>();
                            while ((a = br.ReadByte()) != 0)
                            {
                                if (a == 0xFF) break;

                                pathName.Add(a);
                            }

                            if (a == 0xFF) break;

                            // Read the string from the byte array and remove null terminators
                            var path = Encoding.ASCII.GetString(pathName.ToArray()).Replace("\0", "");

                            if (path.Equals(string.Empty)) continue;

                            // Add the attribute to the list
                            if (pathCounts == 0)
                            {
                                assets.Shared = path;
                            }
                            else if (pathCounts == 1)
                            {
                                assets.BaseFileName = path;
                            }
                            else
                            {
                                if (path.Contains(".mdl"))
                                {
                                    assets.MdlList.Add(path);
                                }
                                else if (path.Contains(".sgb"))
                                {
                                    assets.AdditionalAssetList.Add(path);
                                }
                                else if (!path.Contains("."))
                                {
                                    assets.BaseFolder = path;
                                }
                                else
                                {
                                    assets.OthersList.Add(path);
                                }
                            }

                            pathCounts++;
                        }
                    }
                });
            }
        }
    }

    /// <summary>
    /// A class that contains the data found within the housings asset file
    /// </summary>
    public class HousingAssets
    {
        public string Shared { get; set; }

        public string BaseFileName { get; set; }

        public List<string> MdlList { get; set; } = new List<string>();

        public List<string> AdditionalAssetList { get; set; } = new List<string>();

        public List<string> OthersList { get; set;} = new List<string>();

        public string BaseFolder { get; set; }
    }
}
