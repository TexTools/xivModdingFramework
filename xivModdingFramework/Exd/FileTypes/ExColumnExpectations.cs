using System;
using System.Collections.Generic;
using System.Text;
using xivModdingFramework.Exd.Enums;
using xivModdingFramework.General.Enums;

namespace xivModdingFramework.Exd.FileTypes
{
    internal static class ExColumnExpectations
    {


        /// <summary>
        /// Get the column expectations for a given EX file.
        /// </summary>
        /// <param name="exFile"></param>
        /// <param name="language"></param>
        /// <returns></returns>
        public static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetColumnExpectations(XivEx exFile, XivLanguage language = XivLanguage.English)
        {
            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>();

            switch (exFile)
            {
                case XivEx.item:
                    return GetItemExpectations(language);
                case XivEx.housingfurniture:
                    return GetHousingFurnitureExpectations(language);
                case XivEx.housingyardobject:
                    return GetHousingYardObjectExpectations(language);
                case XivEx.picture:
                    return GetPictureExpectations(language);
                case XivEx.companion:
                    return GetCompanionExpectation(language);
                case XivEx.mount:
                    return GetMountExpectations(language);
                case XivEx.ornament:
                    return GetOrnamentExpectations(language);
                case XivEx.modelchara:
                    return GetModelCharaExpectations(language);
                case XivEx.placename:
                    return GetPlaceNameExpectations(language);
                case XivEx.map:
                    return GetMapExpectations(language);
                case XivEx.action:
                    return GetActionExpectations(language);
                case XivEx.actioncategory:
                    return GetActionCategoryExpectations(language);
                case XivEx.status:
                    return GetStatusExpectations(language);
                case XivEx.mapsymbol:
                    return GetMapSymbolExpectations(language);
                case XivEx.onlinestatus:
                    return GetOnlineStatusExpectations(language);
                case XivEx.weather:
                    return GetWeatherExpectations(language);
                case XivEx.loadingimage:
                    return GetLoadingImageExpectations(language);
                default:
                    return columnExpectations;
            }


        }

        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetItemExpectations(XivLanguage language = XivLanguage.English)
        {
            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Name", ( 9, ExcelColumnDataType.String ) },
                { "PrimaryInfo", ( 47, ExcelColumnDataType.UInt64 ) },
                { "SecondaryInfo", ( 48, ExcelColumnDataType.UInt64 ) },
                { "SlotNum", ( 17, ExcelColumnDataType.UInt8 ) },
                { "Icon", ( 10, ExcelColumnDataType.UInt16 ) },
                { "PictureId", ( 14, ExcelColumnDataType.UInt32) },
                { "FilterGroup", ( 13, ExcelColumnDataType.UInt8) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetHousingFurnitureExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "ItemId", ( 7, ExcelColumnDataType.UInt32 ) },
                { "PrimaryId", ( 0, ExcelColumnDataType.UInt16 ) },
                { "Category", ( 1, ExcelColumnDataType.UInt8 ) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetHousingYardObjectExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "ItemId", ( 6, ExcelColumnDataType.UInt32 ) },
                { "PrimaryId", ( 0, ExcelColumnDataType.UInt16 ) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetPictureExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "PrimaryId", ( 0, ExcelColumnDataType.Int32 ) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetCompanionExpectation(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Name", ( 0, ExcelColumnDataType.String ) },
                { "ModelCharaId", ( 8, ExcelColumnDataType.UInt16 ) },
                { "Icon", ( 26, ExcelColumnDataType.UInt16 ) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetMountExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Name", ( 0, ExcelColumnDataType.String ) },
                { "ModelCharaId", ( 8, ExcelColumnDataType.Int32 ) },
                { "Icon", ( 30, ExcelColumnDataType.UInt16 ) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetOrnamentExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Name", ( 8, ExcelColumnDataType.String ) },
                { "ModelCharaId", ( 0, ExcelColumnDataType.UInt16 ) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetModelCharaExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Type", ( 0, ExcelColumnDataType.UInt8 ) },
                { "PrimaryId", ( 1, ExcelColumnDataType.UInt16 ) },
                { "SecondaryId", ( 2, ExcelColumnDataType.UInt8) },
                { "Variant", ( 3, ExcelColumnDataType.UInt8) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetMapExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "MapId", ( 6, ExcelColumnDataType.String) },
                { "RegionPlaceNameId", ( 10, ExcelColumnDataType.UInt16 ) },
                { "PrimaryPlaceNameId", ( 11, ExcelColumnDataType.UInt16 ) },
                { "SubPlaceNameId", ( 12, ExcelColumnDataType.UInt16 ) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetPlaceNameExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Name", ( 0, ExcelColumnDataType.String ) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetActionExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Name", ( 0, ExcelColumnDataType.String ) },
                { "Icon", ( 2, ExcelColumnDataType.UInt16 ) },
                { "ActionCategoryId", ( 3, ExcelColumnDataType.UInt8 ) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetActionCategoryExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Name", ( 0, ExcelColumnDataType.String ) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetStatusExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Name", ( 0, ExcelColumnDataType.String ) },
                { "Icon", ( 2, ExcelColumnDataType.UInt32 ) },
                { "Type", ( 6, ExcelColumnDataType.UInt8 ) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetMapSymbolExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Icon", ( 0, ExcelColumnDataType.Int32 ) },
                { "PlaceNameId", ( 1, ExcelColumnDataType.Int32 ) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetOnlineStatusExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Icon", ( 4, ExcelColumnDataType.UInt32 ) },
                { "Name", ( 6, ExcelColumnDataType.String ) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetWeatherExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Icon", ( 0, ExcelColumnDataType.Int32 ) },
                { "Name", ( 1, ExcelColumnDataType.String ) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetLoadingImageExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Name", ( 0, ExcelColumnDataType.String ) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
            }

            return columnExpectations;
        }
    }
}
