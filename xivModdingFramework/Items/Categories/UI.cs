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
using System.Text;
using xivModdingFramework.Exd.Enums;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.HUD.FileTypes;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Resources;

namespace xivModdingFramework.Items.Categories
{
    /// <summary>
    /// This class contains getters for different types of UI elements
    /// </summary>
    public class UI
    {
        private readonly DirectoryInfo _gameDirectory;
        private readonly XivLanguage _xivLanguage;

        public UI(DirectoryInfo gameDirectory, XivLanguage xivLanguage)
        {
            _gameDirectory = gameDirectory;
            _xivLanguage = xivLanguage;
        }

        /// <summary>
        /// Gets a list of UI elements from the Uld Files
        /// </summary>
        /// <remarks>
        /// The uld files are in the 06 files
        /// They contain refrences to textures among other unknown things (likely placement data)
        /// </remarks>
        /// <returns>A list containing XivUi data</returns>
        public List<XivUi> GetUldList()
        {
            var uldList = new List<XivUi>();

            var uld = new Uld(_gameDirectory);
            var uldPaths = uld.GetTexFromUld();

            // Loops through the list of uld paths
            foreach (var uldPath in uldPaths)
            {
                var xivUi = new XivUi()
                {
                    Name = Path.GetFileNameWithoutExtension(uldPath),
                    Category = "UI",
                    ItemCategory = "ULD"
                };

                uldList.Add(xivUi);
            }

            return uldList;
        }

        /// <summary>
        /// Gets the list of available map data
        /// </summary>
        /// <remarks>
        /// The map data is obtained from the map exd files
        /// There may be unlisted maps which this does not check for
        /// </remarks>
        /// <returns>A list containing XivUi data</returns>
        public List<XivUi> GetMapList()
        {
            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int regionMapDataOffset = 12;
            const int dataLength = 32;

            var mapList = new List<XivUi>();

            var ex = new Ex(_gameDirectory, _xivLanguage);
            var placeNameData = ex.ReadExData(XivEx.placename);
            var mapData = ex.ReadExData(XivEx.map);

            var mapNameList = new List<string>();

            // Loops through all available maps in the map exd files
            // At present only one file exists (map_0)
            foreach (var map in mapData.Values)
            {
                int regionIndex;
                int mapIndex;

                var xivUi = new XivUi()
                {
                    Category = "UI",
                    ItemCategory = XivStrings.Maps
                };

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(map)))
                {
                    br.BaseStream.Seek(regionMapDataOffset, SeekOrigin.Begin);

                    regionIndex = br.ReadInt16();
                    mapIndex = br.ReadInt16();

                    if(mapIndex == 0) continue;

                    br.BaseStream.Seek(dataLength, SeekOrigin.Begin);

                    // The size of the map path string 
                    // Size of the entire data chunk - size of the data portion
                    var mapPathLength = map.Length - dataLength;

                    if(mapPathLength < 4) continue;

                    xivUi.UiPath = Encoding.UTF8.GetString(br.ReadBytes(mapPathLength)).Replace("\0", "");
                }

                // Gets the name from the placename exd file
                var regionName = GetPlaceName(placeNameData[regionIndex]);
                var mapName = GetPlaceName(placeNameData[mapIndex]);

                if (mapName.Equals(string.Empty)) continue;

                xivUi.Name = mapName;
                xivUi.ItemSubCategory = regionName;

                if (mapNameList.Contains(mapName))
                {
                    xivUi.Name = mapName + " " + xivUi.UiPath.Substring(xivUi.UiPath.Length - 2);
                }

                mapNameList.Add(mapName);

                mapList.Add(xivUi);
            }

            mapList.Sort();

            return mapList;
        }

        /// <summary>
        /// Gets the list of action UI elements
        /// </summary>
        /// <remarks>
        /// The actions are obtained from different sources, but is not all inclusive
        /// There may be some actions that are missing
        /// </remarks>
        /// <returns>A list containing XivUi data</returns>
        public List<XivUi> GetActionList()
        {
            var ex = new Ex(_gameDirectory, _xivLanguage);

            // Data from the action_0 exd
            var actionExData = ex.ReadExData(XivEx.action);
            var actionCategoryExData = ex.ReadExData(XivEx.actioncategory);

            var actionList = new List<XivUi>();

            var actionNames = new List<string>();

            foreach (var action in actionExData.Values)
            {
                var xivUi = new XivUi()
                {
                    Category = "UI",
                    ItemCategory = XivStrings.Actions
                };

                int actionCategory;

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(action)))
                {
                    br.BaseStream.Seek(8, SeekOrigin.Begin);
                    var iconNumber = br.ReadUInt16();

                    if (iconNumber == 0) continue;

                    br.BaseStream.Seek(26, SeekOrigin.Begin);
                    actionCategory = br.ReadByte();

                    br.BaseStream.Seek(56, SeekOrigin.Begin);
                    var nameLength = action.Length - 56;
                    var name = Encoding.UTF8.GetString(br.ReadBytes(nameLength)).Replace("\0", "");

                    xivUi.Name = name;
                    xivUi.IconNumber = iconNumber;
                }

                if (actionNames.Contains(xivUi.Name)) continue;
                actionNames.Add(xivUi.Name);

                var actionCategoryData = actionCategoryExData[actionCategory];

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(actionCategoryData)))
                {
                    br.BaseStream.Seek(4, SeekOrigin.Begin);

                    var nameLength = actionCategoryData.Length - 4;
                    var name = Encoding.UTF8.GetString(br.ReadBytes(nameLength)).Replace("\0", "");

                    if (name.Equals(string.Empty))
                    {
                        xivUi.ItemSubCategory = XivStrings.None;
                    }
                    else
                    {
                        xivUi.ItemSubCategory = name;
                    }
                }

                actionList.Add(xivUi);
            }

            // Data from generalaction_0
            var generalActionExData = ex.ReadExData(XivEx.generalaction);

            foreach (var action in generalActionExData.Values)
            {
                var xivUi = new XivUi()
                {
                    Category = "UI",
                    ItemCategory = XivStrings.Actions,
                    ItemSubCategory = XivStrings.General
                };

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(action)))
                {
                    br.BaseStream.Seek(8, SeekOrigin.Begin);

                    var nameLength = br.ReadInt16();

                    br.BaseStream.Seek(10, SeekOrigin.Begin);

                    var iconNumber = br.ReadUInt16();

                    if (iconNumber == 0) continue;

                    br.BaseStream.Seek(20, SeekOrigin.Begin);

                    var name = Encoding.UTF8.GetString(br.ReadBytes(nameLength)).Replace("\0", "");

                    xivUi.Name = name;
                    xivUi.IconNumber = iconNumber;
                }

                if (actionNames.Contains(xivUi.Name)) continue;
                actionNames.Add(xivUi.Name);

                actionList.Add(xivUi);
            }

            // Data from buddyaction_0
            var buddyActionExData = ex.ReadExData(XivEx.buddyaction);
             
            foreach (var action in buddyActionExData.Values)
            {
                var xivUi = new XivUi()
                {
                    Category = "UI",
                    ItemCategory = XivStrings.Actions,
                    ItemSubCategory = XivStrings.Buddy
                };

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(action)))
                {
                    br.BaseStream.Seek(6, SeekOrigin.Begin);

                    var nameLength = br.ReadInt16();

                    br.BaseStream.Seek(10, SeekOrigin.Begin);

                    var iconNumber = br.ReadUInt16();

                    if (iconNumber == 0) continue;

                    br.BaseStream.Seek(20, SeekOrigin.Begin);

                    var name = Encoding.UTF8.GetString(br.ReadBytes(nameLength)).Replace("\0", "");

                    xivUi.Name = name;
                    xivUi.IconNumber = iconNumber;
                }

                if (actionNames.Contains(xivUi.Name)) continue;
                actionNames.Add(xivUi.Name);

                actionList.Add(xivUi);
            }

            // Data from companyaction_0
            var companyActionExData = ex.ReadExData(XivEx.companyaction);

            foreach (var action in companyActionExData.Values)
            {
                var xivUi = new XivUi()
                {
                    Category = "UI",
                    ItemCategory = XivStrings.Actions,
                    ItemSubCategory = XivStrings.Company
                };

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(action)))
                {
                    br.BaseStream.Seek(6, SeekOrigin.Begin);

                    var nameLength = br.ReadInt16();

                    br.BaseStream.Seek(10, SeekOrigin.Begin);

                    var iconNumber = br.ReadUInt16();

                    if (iconNumber == 0) continue;

                    br.BaseStream.Seek(20, SeekOrigin.Begin);

                    var name = Encoding.UTF8.GetString(br.ReadBytes(nameLength)).Replace("\0", "");

                    xivUi.Name = name;
                    xivUi.IconNumber = iconNumber;
                }

                if (actionNames.Contains(xivUi.Name)) continue;
                actionNames.Add(xivUi.Name);

                actionList.Add(xivUi);
            }

            // Data from craftaction_100000
            var craftActionExData = ex.ReadExData(XivEx.craftaction);

            foreach (var action in craftActionExData.Values)
            {
                var xivUi = new XivUi()
                {
                    Category = "UI",
                    ItemCategory = XivStrings.Actions,
                    ItemSubCategory = XivStrings.Craft
                };

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(action)))
                {
                    br.BaseStream.Seek(6, SeekOrigin.Begin);

                    var nameLength = br.ReadInt16();

                    br.BaseStream.Seek(48, SeekOrigin.Begin);

                    var iconNumber = br.ReadUInt16();

                    if (iconNumber == 0) continue;

                    br.BaseStream.Seek(60, SeekOrigin.Begin);

                    var name = Encoding.UTF8.GetString(br.ReadBytes(nameLength)).Replace("\0", "");

                    xivUi.Name = name;
                    xivUi.IconNumber = iconNumber;
                }

                if (actionNames.Contains(xivUi.Name)) continue;
                actionNames.Add(xivUi.Name);

                actionList.Add(xivUi);
            }

            // Data from eventaction_0
            var eventActionExData = ex.ReadExData(XivEx.eventaction);

            foreach (var action in eventActionExData.Values)
            {
                var xivUi = new XivUi()
                {
                    Category = "UI",
                    ItemCategory = XivStrings.Actions,
                    ItemSubCategory = XivStrings.Event
                };

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(action)))
                {
                    br.BaseStream.Seek(4, SeekOrigin.Begin);

                    var iconNumber = br.ReadUInt16();

                    if (iconNumber == 0) continue;

                    br.BaseStream.Seek(16, SeekOrigin.Begin);

                    var nameLength = action.Length - 16;

                    var name = Encoding.UTF8.GetString(br.ReadBytes(nameLength)).Replace("\0", "");

                    xivUi.Name = name;
                    xivUi.IconNumber = iconNumber;
                }

                if (actionNames.Contains(xivUi.Name)) continue;
                actionNames.Add(xivUi.Name);

                actionList.Add(xivUi);
            }

            // Data from emote_0
            var emoteExData = ex.ReadExData(XivEx.emote);

            foreach (var action in emoteExData.Values)
            {
                var xivUi = new XivUi()
                {
                    Category = "UI",
                    ItemCategory = XivStrings.Actions,
                    ItemSubCategory = XivStrings.Emote
                };

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(action)))
                {
                    br.BaseStream.Seek(26, SeekOrigin.Begin);

                    var iconNumber = br.ReadUInt16();

                    if (iconNumber == 0) continue;

                    br.BaseStream.Seek(36, SeekOrigin.Begin);

                    var nameLength = action.Length - 36;

                    var name = Encoding.UTF8.GetString(br.ReadBytes(nameLength)).Replace("\0", "");

                    xivUi.Name = name;
                    xivUi.IconNumber = iconNumber;
                }

                if (actionNames.Contains(xivUi.Name)) continue;
                actionNames.Add(xivUi.Name);

                actionList.Add(xivUi);
            }

            // Data from marker_0
            var markerExData = ex.ReadExData(XivEx.marker);

            foreach (var action in markerExData.Values)
            {
                var xivUi = new XivUi()
                {
                    Category = "UI",
                    ItemCategory = XivStrings.Actions,
                    ItemSubCategory = XivStrings.Marker
                };

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(action)))
                {
                    br.BaseStream.Seek(6, SeekOrigin.Begin);

                    var iconNumber = br.ReadUInt16();

                    if (iconNumber == 0) continue;

                    var nameLength = action.Length - 6;

                    var name = Encoding.UTF8.GetString(br.ReadBytes(nameLength)).Replace("\0", "");

                    xivUi.Name = name;
                    xivUi.IconNumber = iconNumber;
                }

                if (actionNames.Contains(xivUi.Name)) continue;
                actionNames.Add(xivUi.Name);

                actionList.Add(xivUi);
            }

            // Data from fieldmarker_0
            var fieldMarkerExData = ex.ReadExData(XivEx.fieldmarker);

            foreach (var action in fieldMarkerExData.Values)
            {
                var xivUi = new XivUi()
                {
                    Category = "UI",
                    ItemCategory = XivStrings.Actions,
                    ItemSubCategory = XivStrings.FieldMarker
                };

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(action)))
                {
                    br.BaseStream.Seek(8, SeekOrigin.Begin);

                    var iconNumber = br.ReadUInt16();

                    if (iconNumber == 0) continue;

                    br.BaseStream.Seek(12, SeekOrigin.Begin);

                    var nameLength = action.Length - 12;

                    var name = Encoding.UTF8.GetString(br.ReadBytes(nameLength)).Replace("\0", "");

                    xivUi.Name = name;
                    xivUi.IconNumber = iconNumber;
                }

                if (actionNames.Contains(xivUi.Name)) continue;
                actionNames.Add(xivUi.Name);

                actionList.Add(xivUi);
            }

            actionList.Sort();

            return actionList;
        }

        /// <summary>
        /// Gets the list of status effect UI elements
        /// </summary>
        /// <returns>A list containing XivUi data</returns>
        public List<XivUi> GetStatusList()
        {
            var statusList = new List<XivUi>();

            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int nameLengthDataOffset = 6;
            const int typeDataOffset = 13;
            const int dataLength = 24;

            var ex = new Ex(_gameDirectory, _xivLanguage);
            var statusExData = ex.ReadExData(XivEx.status);

            foreach (var status in statusExData.Values)
            {
                var xivUi = new XivUi()
                {
                    Category = "UI",
                    ItemCategory = XivStrings.Status
                };

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(status)))
                {
                    br.BaseStream.Seek(nameLengthDataOffset, SeekOrigin.Begin);

                    var nameLength = br.ReadInt16();

                    var iconNumber = br.ReadUInt16();

                    if (iconNumber == 0) continue;

                    br.BaseStream.Seek(typeDataOffset, SeekOrigin.Begin);

                    var type = br.ReadByte();

                    br.BaseStream.Seek(dataLength, SeekOrigin.Begin);

                    var name = Encoding.UTF8.GetString(br.ReadBytes(nameLength)).Replace("\0", "");

                    xivUi.IconNumber = iconNumber;
                    xivUi.Name = name;

                    if(name.Equals(string.Empty)) continue;

                    //Status effects have a byte that determines whether the effect is detrimental or beneficial
                    if (type == 1)
                    {
                        xivUi.ItemSubCategory = XivStrings.Beneficial;
                    }
                    else if (type == 2)
                    {
                        xivUi.ItemSubCategory = XivStrings.Detrimental;
                    }
                    else
                    {
                        xivUi.ItemSubCategory = XivStrings.None;
                        xivUi.Name = xivUi.Name + " " + type;
                    }
                }

                statusList.Add(xivUi);
            }

            statusList.Sort();

            return statusList;
        }

        /// <summary>
        /// Gets the list of map symbol UI elements
        /// </summary>
        /// <remarks>
        /// The map symbol exd only contains refrences to the placenamedata exd
        /// The names of the symbols are contained withing the placenamedata exd
        /// </remarks>
        /// <returns>A list containing XivUi data</returns>
        public List<XivUi> GetMapSymbolList()
        {
            var mapSymbolList = new List<XivUi>();

            var ex = new Ex(_gameDirectory, _xivLanguage);
            var mapSymbolExData = ex.ReadExData(XivEx.mapsymbol);
            var placeNameData = ex.ReadExData(XivEx.placename);

            foreach (var mapSymbol in mapSymbolExData.Values)
            {
                var xivUi = new XivUi()
                {
                    Category = "UI",
                    ItemCategory = XivStrings.MapSymbol
                };

                int placeNameIndex;
                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(mapSymbol)))
                {
                    var iconNumber = br.ReadInt32();

                    placeNameIndex = br.ReadInt32();

                    if (iconNumber == 0) continue;

                    xivUi.IconNumber = iconNumber;
                }

                // Gets the name of the map symbol from the placename exd
                xivUi.Name = GetPlaceName(placeNameData[placeNameIndex]);

                mapSymbolList.Add(xivUi);
            }

            mapSymbolList.Sort();

            return mapSymbolList;
        }

        /// <summary>
        /// Gets the list of online status UI elements
        /// </summary>
        /// <returns>A list containing XivUi data</returns>
        public List<XivUi> GetOnlineStatusList()
        {
            var onlineStatusList = new List<XivUi>();

            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int iconNumberOffset = 6;
            const int dataSize = 12;

            var ex = new Ex(_gameDirectory, _xivLanguage);
            var onlineStatusExData = ex.ReadExData(XivEx.onlinestatus);

            foreach (var onlineStatus in onlineStatusExData.Values)
            {
                var xivUi = new XivUi()
                {
                    Category = "UI",
                    ItemCategory = XivStrings.OnlineStatus
                };

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(onlineStatus)))
                {
                    br.BaseStream.Seek(iconNumberOffset, SeekOrigin.Begin);

                    var iconNumber = br.ReadUInt16();

                    if (iconNumber == 0) continue;

                    br.BaseStream.Seek(dataSize, SeekOrigin.Begin);

                    // The size of the online status name string 
                    // Size of the entire data chunk - size of the data portion
                    var nameLength = onlineStatus.Length - dataSize;

                    var name = Encoding.UTF8.GetString(br.ReadBytes(nameLength)).Replace("\0", "");

                    xivUi.IconNumber = iconNumber;
                    xivUi.Name = name;
                }

                onlineStatusList.Add(xivUi);
            }

            onlineStatusList.Sort();

            return onlineStatusList;
        }

        /// <summary>
        /// Gets the list of Weather UI elements
        /// </summary>
        /// <returns>A list containing XivUi data</returns>
        public List<XivUi> GetWeatherList()
        {
            var weatherList = new List<XivUi>();

            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int nameLengthOffset = 6;
            const int iconNumberOffest = 26;

            var ex = new Ex(_gameDirectory, _xivLanguage);
            var weatherExData = ex.ReadExData(XivEx.weather);

            var weatherNames = new List<string>();

            foreach (var weather in weatherExData.Values)
            {
                var xivUi = new XivUi()
                {
                    Category = "UI",
                    ItemCategory = XivStrings.Weather
                };

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(weather)))
                {
                    br.BaseStream.Seek(nameLengthOffset, SeekOrigin.Begin);

                    var nameLength = br.ReadInt16();

                    br.BaseStream.Seek(iconNumberOffest, SeekOrigin.Begin);

                    var iconNumber = br.ReadUInt16();

                    if (iconNumber == 0) continue;

                    var name = Encoding.UTF8.GetString(br.ReadBytes(nameLength)).Replace("\0", "");

                    xivUi.IconNumber = iconNumber;
                    xivUi.Name = name;
                }

                if (weatherNames.Contains(xivUi.Name)) continue;
                weatherNames.Add(xivUi.Name);

                weatherList.Add(xivUi);
            }

            weatherList.Sort();

            return weatherList;
        }

        /// <summary>
        /// Gets the list of available loading screen images
        /// </summary>
        /// <returns>A list containing XivUi data</returns>
        public List<XivUi> GetLoadingImageList()
        {
            var loadingImageList = new List<XivUi>();

            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int dataLength = 4;

            var ex = new Ex(_gameDirectory, _xivLanguage);
            var loadingImageExData = ex.ReadExData(XivEx.loadingimage);

            foreach (var loadingImage in loadingImageExData.Values)
            {
                var xivUi = new XivUi()
                {
                    Category = "UI",
                    ItemCategory = XivStrings.LoadingScreen,
                    UiPath = "ui/loadingimage/"
                };

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(loadingImage)))
                {
                    br.BaseStream.Seek(dataLength, SeekOrigin.Begin);

                    // The length of the loading image name string
                    // Size of the entire data chunk - size of the data portion
                    var nameLength = loadingImage.Length - dataLength;

                    var name = Encoding.UTF8.GetString(br.ReadBytes(nameLength)).Replace("\0", "");

                    if(name.Equals("")) continue;

                    xivUi.Name = name;
                }

                loadingImageList.Add(xivUi);
            }

            loadingImageList.Sort();

            return loadingImageList;
        }

        /// <summary>
        /// Helper function to obtain the string value from placename_0
        /// </summary>
        /// <param name="data">The uncompressed placename byte data</param>
        /// <returns>A string containing the place name</returns>
        private static string GetPlaceName(byte[] data)
        {

            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int nameLengthOffset = 6;
            const int dataLength = 24;

            // Big Endian Byte Order 
            using (var br = new BinaryReaderBE(new MemoryStream(data)))
            {
                br.BaseStream.Seek(nameLengthOffset, SeekOrigin.Begin);

                var nameLength = br.ReadInt16();

                br.BaseStream.Seek(dataLength, SeekOrigin.Begin);

                return Encoding.UTF8.GetString(br.ReadBytes(nameLength)).Replace("\0", "");
            }
        }
    }
}