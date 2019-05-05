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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Exd.Enums;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.General.DataContainers;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;

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
        public async Task<List<XivFurniture>> GetFurnitureList()
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
            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int itemIndexOffset = 10;
            const int modelNumberOffset = 12;
            const int itemCategoryOffset = 14;

            const int itemNameDataOffset = 14;
            const int itemDataLength = 160;
            const int itemIconDataOffset = 136;

            var ex = new Ex(_gameDirectory, _xivLanguage);
            var housingDictionary = await ex.ReadExData(XivEx.housingfurniture);
            var itemDictionary = await ex.ReadExData(XivEx.item);

            var furnitureList = new List<XivFurniture>();

            await Task.Run(() => Parallel.ForEach(housingDictionary.Values, (housingItem) =>
            {
                var item = new XivFurniture
                {
                    Category = XivStrings.Housing,
                    ItemCategory = XivStrings.Furniture_Indoor,
                    ModelInfo = new XivModelInfo()
                };

                using (var br = new BinaryReaderBE(new MemoryStream(housingItem)))
                {
                    br.BaseStream.Seek(itemIndexOffset, SeekOrigin.Begin);
                    var itemIndex = br.ReadInt16();

                    br.BaseStream.Seek(modelNumberOffset, SeekOrigin.Begin);
                    item.ModelInfo.ModelID = br.ReadInt16();

                    br.BaseStream.Seek(itemCategoryOffset, SeekOrigin.Begin);
                    var housingCategory = br.ReadByte();

                    using (var br1 = new BinaryReaderBE(new MemoryStream(itemDictionary[itemIndex])))
                    {
                        br1.BaseStream.Seek(itemNameDataOffset, SeekOrigin.Begin);
                        var nameOffset = br1.ReadInt16();

                        br1.BaseStream.Seek(itemIconDataOffset, SeekOrigin.Begin);
                        item.IconNumber = br1.ReadUInt16();

                        var gearNameOffset = itemDataLength + nameOffset;
                        var gearNameLength = itemDictionary[itemIndex].Length - gearNameOffset;
                        br1.BaseStream.Seek(gearNameOffset, SeekOrigin.Begin);
                        var nameString = Encoding.UTF8.GetString(br1.ReadBytes(gearNameLength)).Replace("\0", "");
                        item.Name = new string(nameString.Where(c => !char.IsControl(c)).ToArray());
                    }
                }

                if (!item.Name.Equals(string.Empty))
                {
                    lock (indoorLock)
                    {
                        furnitureList.Add(item);
                    }
                }
            }));

            furnitureList.Sort();

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
        private async Task<List<XivFurniture>> GetPaintings()
        {
            var paintingsLock = new object();
            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int itemIndexOffset = 0;
            const int iconNumberOffset = 4;

            const int itemNameDataOffset = 14;
            const int itemDataLength = 160;
            const int itemIconDataOffset = 136;

            var ex = new Ex(_gameDirectory, _xivLanguage);
            var pictureDictionary = await ex.ReadExData(XivEx.picture);
            var itemDictionary = await ex.ReadExData(XivEx.item);

            var furnitureList = new List<XivFurniture>();

            await Task.Run(() => Parallel.ForEach(pictureDictionary.Values, (housingItem) =>
            {
                var item = new XivFurniture
                {
                    Category = XivStrings.Housing,
                    ItemCategory = XivStrings.Paintings,
                    ModelInfo = new XivModelInfo()
                };

                using (var br = new BinaryReaderBE(new MemoryStream(housingItem)))
                {
                    br.BaseStream.Seek(itemIndexOffset, SeekOrigin.Begin);
                    var itemIndex = br.ReadInt32();

                    br.BaseStream.Seek(iconNumberOffset, SeekOrigin.Begin);
                    item.ModelInfo.ModelID = br.ReadInt32();

                    using (var br1 = new BinaryReaderBE(new MemoryStream(itemDictionary[itemIndex])))
                    {
                        br1.BaseStream.Seek(itemNameDataOffset, SeekOrigin.Begin);
                        var nameOffset = br1.ReadInt16();

                        br1.BaseStream.Seek(itemIconDataOffset, SeekOrigin.Begin);
                        item.IconNumber = br1.ReadUInt16();

                        var gearNameOffset = itemDataLength + nameOffset;
                        var gearNameLength = itemDictionary[itemIndex].Length - gearNameOffset;
                        br1.BaseStream.Seek(gearNameOffset, SeekOrigin.Begin);
                        var nameString = Encoding.UTF8.GetString(br1.ReadBytes(gearNameLength)).Replace("\0", "");
                        item.Name = new string(nameString.Where(c => !char.IsControl(c)).ToArray());
                    }
                }

                if (!item.Name.Equals(string.Empty))
                {
                    lock (paintingsLock)
                    {
                        furnitureList.Add(item);
                    }
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
            const int itemIndexOffset = 10;
            const int modelNumberOffset = 12;
            const int itemCategoryOffset = 13;

            const int itemNameDataOffset = 14;
            const int itemDataLength = 160;
            const int itemIconDataOffset = 136;

            var ex = new Ex(_gameDirectory, _xivLanguage);
            var housingDictionary = await ex.ReadExData(XivEx.housingyardobject);
            var itemDictionary = await ex.ReadExData(XivEx.item);

            var furnitureList = new List<XivFurniture>();

            await Task.Run(() => Parallel.ForEach(housingDictionary.Values, (housingItem) =>
            {
                var item = new XivFurniture
                {
                    Category = XivStrings.Housing,
                    ItemCategory = XivStrings.Furniture_Outdoor,
                    ModelInfo = new XivModelInfo()
                };

                using (var br = new BinaryReaderBE(new MemoryStream(housingItem)))
                {
                    br.BaseStream.Seek(itemIndexOffset, SeekOrigin.Begin);
                    var itemIndex = br.ReadInt16();

                    br.BaseStream.Seek(modelNumberOffset, SeekOrigin.Begin);
                    item.ModelInfo.ModelID = br.ReadByte();

                    br.BaseStream.Seek(itemCategoryOffset, SeekOrigin.Begin);
                    var housingCategory = br.ReadByte();

                    using (var br1 = new BinaryReaderBE(new MemoryStream(itemDictionary[itemIndex])))
                    {
                        br1.BaseStream.Seek(itemNameDataOffset, SeekOrigin.Begin);
                        var nameOffset = br1.ReadInt16();

                        br1.BaseStream.Seek(itemIconDataOffset, SeekOrigin.Begin);
                        item.IconNumber = br1.ReadUInt16();

                        var gearNameOffset = itemDataLength + nameOffset;
                        var gearNameLength = itemDictionary[itemIndex].Length - gearNameOffset;
                        br1.BaseStream.Seek(gearNameOffset, SeekOrigin.Begin);
                        var nameString = Encoding.UTF8.GetString(br1.ReadBytes(gearNameLength)).Replace("\0", "");
                        item.Name = new string(nameString.Where(c => !char.IsControl(c)).ToArray());
                    }
                }

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

        /// <summary>
        /// Gets the parts list for furniture
        /// </summary>
        /// <param name="itemModel">The item to get the parts for</param>
        /// <returns>A dictionary containing the part string and mdl path string</returns>
        public async Task<Dictionary<string, string>> GetFurnitureModelParts(IItemModel itemModel)
        {
            var furniturePartDict = new Dictionary<string, string>();

            var assets = await GetFurnitureAssets(itemModel.ModelInfo.ModelID, itemModel.ItemCategory);

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
        private async Task<HousingAssets> GetFurnitureAssets(int modelID, string category)
        {
            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);

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

            var assetOffset = await index.GetDataOffset(HashGenerator.GetHash(assetFolder), HashGenerator.GetHash(assetFile),
                XivDataFile._01_Bgcommon);

            var assetData = await dat.GetType2Data(assetOffset, XivDataFile._01_Bgcommon);

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
                await GetAdditionalAssets(housingAssets);
            }


            return housingAssets;
        }

        /// <summary>
        /// Gets additional assets when the original asset file contains asset file paths within it
        /// </summary>
        /// <param name="assets">The current asset object</param>
        private async Task GetAdditionalAssets(HousingAssets assets)
        {
            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);

            foreach (var additionalAsset in assets.AdditionalAssetList)
            {
                var assetFolder = Path.GetDirectoryName(additionalAsset).Replace("\\", "/");
                var assetFile = Path.GetFileName(additionalAsset);

                var assetOffset = await index.GetDataOffset(HashGenerator.GetHash(assetFolder), HashGenerator.GetHash(assetFile), XivDataFile._01_Bgcommon);

                var assetData = await dat.GetType2Data(assetOffset, XivDataFile._01_Bgcommon);

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

        /// <summary>
        /// Searches for housing items given a model ID
        /// </summary>
        /// <param name="modelID">The Model ID of the housing item</param>
        /// <param name="type">The type of housing item to search for</param>
        /// <returns>A list of Search Results</returns>
        public async Task<List<SearchResults>> SearchHousingByModelID(int modelID, XivItemType type)
        {
            var searchResultsList = new List<SearchResults>();
            var index = new Index(_gameDirectory);
            var id = modelID.ToString().PadLeft(4, '0');

            var folder = "";

            if (type == XivItemType.furniture)
            {
                folder = $"bgcommon/hou/indoor/general/{id}/material";
            }

            if (await index.FolderExists(HashGenerator.GetHash(folder), XivDataFile._01_Bgcommon))
            {
                var searchResults = new SearchResults
                {
                    Body = "-",
                    Slot = XivStrings.Furniture_Indoor,
                    Variant = int.Parse(id)
                };

                searchResultsList.Add(searchResults);
            }

            folder = $"bgcommon/hou/outdoor/general/{id}/material";

            if (await index.FolderExists(HashGenerator.GetHash(folder), XivDataFile._01_Bgcommon))
            {
                var searchResults = new SearchResults
                {
                    Body = "-",
                    Slot = XivStrings.Furniture_Outdoor,
                    Variant = int.Parse(id)
                };

                searchResultsList.Add(searchResults);
            }

            searchResultsList.Sort();

            return searchResultsList;
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