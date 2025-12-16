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
using System.Linq;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.Exd.Enums;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.HUD.FileTypes;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Resources;
using xivModdingFramework.Mods;
using static xivModdingFramework.Exd.FileTypes.Ex;

namespace xivModdingFramework.Items.Categories
{
    /// <summary>
    /// This class contains getters for different types of UI elements
    /// </summary>
    public class UI
    {
        public UI()
        {
        }


        public async Task<List<XivUi>> GetUIList()
        {
            return await XivCache.GetCachedUiList();
        }

        /// <summary>
        /// Gets a list of UI elements from the Uld Files
        /// </summary>
        /// <remarks>
        /// The uld files are in the 06 files
        /// They contain refrences to textures among other unknown things (likely placement data)
        /// </remarks>
        /// <returns>A list containing XivUi data</returns>
        public async Task<List<XivUi>> GetUldList(ModTransaction tx = null)
        {
            var uldLock = new object();
            var uldList = new List<XivUi>();

            var _ex = new Ex();
            var uldPaths = await Uld.GetTexFromUld(tx);

            await Task.Run(() => Parallel.ForEach(uldPaths, (uldPath) =>
            {
                var xivUi = new XivUi
                {
                    Name = Path.GetFileNameWithoutExtension(uldPath),
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.HUD,
                    UiPath = "ui/uld"
                };

                if (xivUi.Name.Equals(string.Empty)) return;

                lock (uldLock)
                {
                    uldList.Add(xivUi);
                }
            }));

            uldList.Sort();

            return uldList;
        }

        private string GetPlaceName(Dictionary<int, ExdRow> placeData, object placeId)
        {
            return GetPlaceName(placeData, (ushort)placeId);
        }
        private string GetPlaceName(Dictionary<int, ExdRow> placeData, int placeId)
        {
            return (string) placeData[placeId].GetColumnByName("Name");
        }
        private string GetActionCategory(Dictionary<int, ExdRow> data, int index)
        {
            return (string) data[index].GetColumnByName("Name");
        }

        /// <summary>
        /// Gets the list of available map data
        /// </summary>
        /// <remarks>
        /// The map data is obtained from the map exd files
        /// There may be unlisted maps which this does not check for
        /// </remarks>
        /// <returns>A list containing XivUi data</returns>
        public async Task<List<XivUi>> GetMapList(ModTransaction tx = null)
        {
            var mapLock = new object();
            var mapList = new List<XivUi>();

            var _ex = new Ex();
            var placeNameData = await _ex.ReadExData(XivEx.placename, tx);
            var mapData = await _ex.ReadExData(XivEx.map, tx);


            // Loops through all available maps in the map exd files
            // At present only one file exists (map_0)
            await Task.Run(() => Parallel.ForEach(mapData.Values, (map) =>
            {


                var regionName = GetPlaceName(placeNameData, map.GetColumnByName("RegionPlaceNameId"));
                var primaryName = GetPlaceName(placeNameData, map.GetColumnByName("PrimaryPlaceNameId"));
                var subMapName = GetPlaceName(placeNameData, map.GetColumnByName("SubPlaceNameId"));
                var mapId = (string) map.GetColumnByName("MapId");

                if (string.IsNullOrWhiteSpace(mapId))
                    return;

                var name = string.IsNullOrEmpty(subMapName) ? primaryName : subMapName;

                if (string.IsNullOrWhiteSpace(regionName))
                {
                    name = "Unknown Map - " + mapId;
                }
                else if (string.IsNullOrWhiteSpace(primaryName))
                {
                    name = "Unknown " + regionName + " Map " + mapId;
                }

                var xivUi = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.Maps,
                    UiPath = mapId,
                    TertiaryCategory = regionName,
                    MapZoneCategory = string.IsNullOrEmpty(subMapName) ? "" : primaryName,
                    Name = name,
                };

                lock (mapLock)
                {
                    mapList.Add(xivUi);
                }
            }));

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
        public async Task<List<XivUi>> GetActionList(ModTransaction tx)
        {
            var actionLock = new object();

            // Data from the action_0 exd
            var _ex = new Ex();
            var actionExData = await _ex.ReadExData(XivEx.action, tx);
            var actionCategoryExData = await _ex.ReadExData(XivEx.actioncategory, tx);

            var actionList = new List<XivUi>();
            var actionNames = new HashSet<string>();

            await Task.Run(() => Parallel.ForEach(actionExData.Values, (action) =>
            {

                var name = (string) action.GetColumnByName("Name");
                var iconId = (ushort)action.GetColumnByName("Icon");
                var actionCatId = (byte)action.GetColumnByName("ActionCategoryId");
                var actionCat = GetActionCategory(actionCategoryExData, actionCatId);

                var xivUi = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.Actions,
                    Name = name,
                    IconNumber = iconId,
                    TertiaryCategory = string.IsNullOrWhiteSpace(actionCat) ? XivStrings.None : actionCat
                };

                // The Cure icon is used as a placeholder so filter out all actions that aren't Cure but are using its icon as a placeholder
                if (string.IsNullOrWhiteSpace(xivUi.Name) || (!xivUi.Name.Equals("Cure") && xivUi.IconNumber == 405)) return;

                var originalName = xivUi.Name;
                var count = 2;
                while(actionNames.Contains(xivUi.Name))
                {
                    xivUi.Name = originalName + " #" + count;
                    count++;
                }

                lock (actionLock)
                {
                    actionNames.Add(xivUi.Name);
                    actionList.Add(xivUi);
                }
             }));

            // Data from generalaction_0
            var generalActionExData = await _ex.ReadExData(XivEx.generalaction, tx);

            await Task.Run(() => Parallel.ForEach(generalActionExData.Values, (action) =>
            {
                var name = (string)action.GetColumnByName("Name");
                var iconId = (int)action.GetColumnByName("Icon");

                var xivUi = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.Actions,
                    Name = name,
                    IconNumber = iconId,
                    TertiaryCategory = XivStrings.GeneralAction,
                };

                if (string.IsNullOrWhiteSpace(xivUi.Name)) return;

                lock (actionLock)
                {
                    actionList.Add(xivUi);
                }
            }));


            // Data from buddyaction_0
            var buddyActionExData = await _ex.ReadExData(XivEx.buddyaction, tx);

            await Task.Run(() => Parallel.ForEach(buddyActionExData.Values, (action) =>
            {
                var name = (string)action.GetColumnByName("Name");
                var iconId = (int)action.GetColumnByName("Icon");
                var iconStatus = (int)action.GetColumnByName("IconStatus");

                if (string.IsNullOrWhiteSpace(name)) return;

                var xivUi = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.Actions,
                    Name = name,
                    IconNumber = iconId,
                    TertiaryCategory = XivStrings.BuddyAction,
                };

                // Add the status icon too.
                var xivUi2 = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.Actions,
                    Name = name + " " + XivStrings.Status,
                    IconNumber = iconId,
                    TertiaryCategory = XivStrings.BuddyAction,
                };

                lock (actionLock)
                {
                    actionList.Add(xivUi);
                    actionList.Add(xivUi2);
                }
            }));

            var companyActionExData = await _ex.ReadExData(XivEx.companyaction, tx);
            await Task.Run(() => Parallel.ForEach(companyActionExData.Values, (action) =>
            {
                var name = (string)action.GetColumnByName("Name");
                var iconId = (int)action.GetColumnByName("Icon");

                if (string.IsNullOrWhiteSpace(name)) return;

                var xivUi = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.Actions,
                    Name = name,
                    IconNumber = iconId,
                    TertiaryCategory = XivStrings.CompanyAction,
                };

                lock (actionLock)
                {
                    actionList.Add(xivUi);
                }
            }));

            var craftActionData = await _ex.ReadExData(XivEx.craftaction, tx);
            await Task.Run(() => Parallel.ForEach(craftActionData.Values, (action) =>
            {
                var name = (string)action.GetColumnByName("Name");
                var iconId = (ushort)action.GetColumnByName("Icon");

                if (string.IsNullOrWhiteSpace(name)) return;

                var xivUi = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.Actions,
                    Name = name,
                    IconNumber = iconId,
                    TertiaryCategory = XivStrings.CraftAction,
                };

                lock (actionLock)
                {
                    actionList.Add(xivUi);
                }
            }));

            var eventActionData = await _ex.ReadExData(XivEx.eventaction, tx);
            await Task.Run(() => Parallel.ForEach(eventActionData.Values, (action) =>
            {
                var name = (string)action.GetColumnByName("Name");
                var iconId = (ushort)action.GetColumnByName("Icon");

                if (string.IsNullOrWhiteSpace(name)) return;

                var xivUi = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.Actions,
                    Name = name,
                    IconNumber = iconId,
                    TertiaryCategory = XivStrings.EventAction,
                };

                lock (actionLock)
                {
                    actionList.Add(xivUi);
                }
            }));


            var emoteData = await _ex.ReadExData(XivEx.emote, tx);
            await Task.Run(() => Parallel.ForEach(emoteData.Values, (action) =>
            {
                var name = (string)action.GetColumnByName("Name");

                // HACKHACK - Because SE is unlikely to ever actually use past 2^31 icons here, the simpler choice is used of casting this to int for now.
                int iconId = (int)((uint)action.GetColumnByName("Icon"));

                if (string.IsNullOrWhiteSpace(name)) return;

                var xivUi = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.Actions,
                    Name = name,
                    IconNumber = iconId,
                    TertiaryCategory = XivStrings.Emote,
                };

                lock (actionLock)
                {
                    actionList.Add(xivUi);
                }
            }));


            var markerData = await _ex.ReadExData(XivEx.marker, tx);
            await Task.Run(() => Parallel.ForEach(markerData.Values, (action) =>
            {
                var name = (string)action.GetColumnByName("Name");
                var iconId = (int)action.GetColumnByName("Icon");

                if (string.IsNullOrWhiteSpace(name)) return;

                var xivUi = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.Actions,
                    Name = name,
                    IconNumber = iconId,
                    TertiaryCategory = XivStrings.Marker,
                };

                lock (actionLock)
                {
                    actionList.Add(xivUi);
                }
            }));

            var fieldMarkerData = await _ex.ReadExData(XivEx.fieldmarker, tx);
            var vfxData = await _ex.ReadExData(XivEx.vfx, tx);
            await Task.Run(() => Parallel.ForEach(fieldMarkerData.Values, (action) =>
            {
                var vfxId = (int)action.GetColumnByName("VfxId");
                var name = (string)action.GetColumnByName("Name");
                var iconId = (ushort)action.GetColumnByName("Icon");
                var minimapIcon = (ushort)action.GetColumnByName("MiniMapIcon");

                var vfx = (string)vfxData[vfxId].GetColumnByName("Path");

                if (string.IsNullOrWhiteSpace(name)) return;

                var xivUi = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.Actions,
                    Name = name,
                    IconNumber = iconId,
                    TertiaryCategory = XivStrings.FieldMarker,
                    UiPath = vfx,
                };

                var xivUi2 = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.Actions,
                    Name = name + " - " + XivStrings.Map,
                    IconNumber = minimapIcon,
                    TertiaryCategory = XivStrings.FieldMarker,
                };

                lock (actionLock)
                {
                    actionList.Add(xivUi);
                    actionList.Add(xivUi2);
                }
            }));


            actionList.Sort();

            return actionList;
        }

        /// <summary>
        /// Gets the list of status effect UI elements
        /// </summary>
        /// <returns>A list containing XivUi data</returns>
        public async Task<List<XivUi>> GetStatusList(ModTransaction tx = null)
        {
            var statusLock = new object();
            var statusList = new List<XivUi>();

            var _ex = new Ex();
            var statusExData = await _ex.ReadExData(XivEx.status, tx);

            await Task.Run(() => Parallel.ForEach(statusExData.Values, (status) =>
            {
                var xivUi = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.Status,
                    Name = (string) status.GetColumnByName("Name"),
                    IconNumber = (int)((uint) status.GetColumnByName("Icon"))
                };
                if (string.IsNullOrWhiteSpace(xivUi.Name)) return;

                //Status effects have a byte that determines whether the effect is detrimental or beneficial
                var type = (byte)status.GetColumnByName("Type");
                if (type == 1)
                {
                    xivUi.TertiaryCategory = XivStrings.Beneficial;
                }
                else if (type == 2)
                {
                    xivUi.TertiaryCategory = XivStrings.Detrimental;
                }
                else
                {
                    xivUi.TertiaryCategory = XivStrings.None;
                    xivUi.Name = xivUi.Name + " " + type;
                }

            lock (statusLock)
            {
                statusList.Add(xivUi);
            }
            }));

            // Remove any duplicates and return the sorted the list
            statusList = statusList.Distinct().ToList();
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
        public async Task<List<XivUi>> GetMapSymbolList(ModTransaction tx = null)
        {
            var mapSymbolLock = new object();
            var mapSymbolList = new List<XivUi>();

            var _ex = new Ex();
            var mapSymbolExData = await _ex.ReadExData(XivEx.mapsymbol, tx);
            var placeNameData = await _ex.ReadExData(XivEx.placename, tx);

            await Task.Run(() => Parallel.ForEach(mapSymbolExData.Values, (mapSymbol) =>
            {
                var xivUi = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.MapSymbol,
                    IconNumber = (int)mapSymbol.GetColumnByName("Icon"),
                    Name = GetPlaceName(placeNameData, (int)mapSymbol.GetColumnByName("PlaceNameId")),
                };

                if (string.IsNullOrWhiteSpace(xivUi.Name))
                {
                    xivUi.Name = "Unknown Map Symbol #" + xivUi.IconNumber.ToString();
                }

                lock (mapSymbolLock)
                {
                    mapSymbolList.Add(xivUi);
                }
            }));

            mapSymbolList.Sort();

            return mapSymbolList;
        }

        /// <summary>
        /// Gets the list of online status UI elements
        /// </summary>
        /// <returns>A list containing XivUi data</returns>
        public async Task<List<XivUi>> GetOnlineStatusList(ModTransaction tx = null)
        {
            var onlineStatusLock = new object();
            var onlineStatusList = new List<XivUi>();


            var _ex = new Ex();
            var onlineStatusExData = await _ex.ReadExData(XivEx.onlinestatus, tx);

            await Task.Run(() => Parallel.ForEach(onlineStatusExData.Values, (onlineStatus) =>
            {
                var xivUi = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.OnlineStatus,
                    IconNumber = (int)(uint)onlineStatus.GetColumnByName("Icon"),
                    Name = (string)onlineStatus.GetColumnByName("Name"),
                };

                if (string.IsNullOrWhiteSpace(xivUi.Name))
                {
                    xivUi.Name = "Unknown Online Status #" + xivUi.IconNumber.ToString();
                }

                lock (onlineStatusLock)
                {
                    onlineStatusList.Add(xivUi);
                }
            }));

            onlineStatusList.Sort();

            return onlineStatusList;
        }

        /// <summary>
        /// Gets the list of Weather UI elements
        /// </summary>
        /// <returns>A list containing XivUi data</returns>
        public async Task<List<XivUi>> GetWeatherList(ModTransaction tx = null)
        {
            var weatherLock = new object();
            var weatherList = new List<XivUi>();

            var _ex = new Ex();
            var weatherExData = await _ex.ReadExData(XivEx.weather, tx);

            var weatherNames = new List<string>();

            await Task.Run(() => Parallel.ForEach(weatherExData.Values, (weather) =>
            {
                var xivUi = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.Weather,
                    IconNumber = (int)weather.GetColumnByName("Icon"),
                    Name = (string)weather.GetColumnByName("Name"),
                };

                if (string.IsNullOrWhiteSpace(xivUi.Name))
                {
                    xivUi.Name = "Unknown Weather #" + xivUi.IconNumber.ToString();
                }


                lock (weatherLock)
                {
                    weatherList.Add(xivUi);
                }
            }));

            weatherList.Sort();

            return weatherList;
        }

        /// <summary>
        /// Gets the list of available loading screen images
        /// </summary>
        /// <returns>A list containing XivUi data</returns>
        public async Task<List<XivUi>> GetLoadingImageList(ModTransaction tx = null)
        {
            var loadingImageLock = new object();
            var loadingImageList = new List<XivUi>();

            var _ex = new Ex();
            var loadingImageExData = await _ex.ReadExData(XivEx.loadingimage, tx);

            await Task.Run(() => Parallel.ForEach(loadingImageExData.Values, (loadingImage) =>
            {
                var xivUi = new XivUi()
                {
                    PrimaryCategory = "UI",
                    SecondaryCategory = XivStrings.LoadingScreen,
                    UiPath = "ui/loadingimage",
                    Name = (string)loadingImage.GetColumnByName("Name"),
                };

                lock (loadingImageLock)
                {
                    loadingImageList.Add(xivUi);
                }
            }));

            loadingImageList.Sort();

            return loadingImageList;
        }

        public async Task<List<XivUi>> GetPaintingUiImages(ModTransaction tx = null)
        {
            var paintingsLock = new object();

            var ex = new Ex();
            var pictureDictionary = await ex.ReadExData(XivEx.picture, tx);
            var itemDictionary = await ex.ReadExData(XivEx.item, tx);

            var paintingList = new List<XivUi>();

            await Task.Run(() => Parallel.ForEach(itemDictionary.Values, (itemRow) =>
            {

                var pictureId = (uint)itemRow.GetColumnByName("PictureId");
                if (pictureId == 0 || pictureId > pictureDictionary.Count)
                    return;

                var name = (string)itemRow.GetColumnByName("Name");
                if (string.IsNullOrEmpty(name))
                {
                    return;
                }

                var filterGroup = (byte)itemRow.GetColumnByName("FilterGroup");
                if (filterGroup != 34)
                {
                    return;
                }

                var pictureRow = pictureDictionary[(int)pictureId];

                var id = (int)pictureRow.GetColumnByName("PrimaryId");
                var painting = new XivUi
                {
                    PrimaryCategory = XivStrings.UI,
                    SecondaryCategory = XivStrings.Painting_Icons,
                    UiPath = "ui/icon/" + (id).ToString("D6"),
                    IconNumber = id,
                };
                //painting.IconId = (ushort)itemRow.GetColumnByName("Icon");
                painting.Name = (string)itemRow.GetColumnByName("Name") + " Icon";


                lock (paintingsLock)
                {
                    paintingList.Add(painting);
                }
            }));

            paintingList.Sort();

            return paintingList;

        }

    }
}
