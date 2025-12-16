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

            // Really could've done this with reflection...
            // But I am a madlad.  Also I like PascalCase.
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
                case XivEx.generalaction:
                    return GetGeneralActionExpectations(language);
                case XivEx.buddyaction:
                    return GetBuddyActionExpectations(language);
                case XivEx.companyaction:
                    return GetCompanyActionExpectations(language);
                case XivEx.craftaction:
                    return GetCraftActionExpectations(language);
                case XivEx.eventaction:
                    return GetEventActionExpectations(language);
                case XivEx.emote:
                    return GetEmoteExpectations(language);
                case XivEx.marker:
                    return GetMarkerExpectations(language);
                case XivEx.fieldmarker:
                    return GetFieldMarkerExpectations(language);
                case XivEx.vfx:
                    return GetVfxExpectations(language);
                case XivEx.aquariumfish:
                    return GetAquariumFishExpectations(language);
                default:
                    return columnExpectations;
            }


        }

        /*  ===== COLUMN DEFINITIONS =====
         * 
         *  These represent the expected column # and data type, for columns we actually care about.
         *  These columns are automatically validated on EXD load, and will error with clear error messages if they are wrong.
         *  
         *  Thus, typically only include columns we actually care about in these, so we don't have to constantly update these 
         *  on every minor table alteration.
         *  
         *  That said, if there's a column you need to access, please add it here so the system can track it for changes.
         *  
         *  Each function also has an If block for CN/KR, to allow for compatibility with their client versions, which
         *  sometimes have altered EXD tables.
         *  
         *  You can use tools like Godbert or XivExplorer to view the raw EXD tables if necessary when checking the column IDs
         *  and data types, or hook a debugger onto the EXD load and inspect the column results manually.
         *  
        */ 

        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetItemExpectations(XivLanguage language = XivLanguage.English)
        {
            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Name", ( 9, ExcelColumnDataType.String ) },
                //{ "EquipType", ( 46, ExcelColumnDataType.UInt8 ) },
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
                { "SecondaryId", ( 1, ExcelColumnDataType.Int32 ) },
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
                { "Icon", ( 28, ExcelColumnDataType.UInt16 ) },
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
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetGeneralActionExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Name", ( 0, ExcelColumnDataType.String ) },
                { "Icon", ( 7, ExcelColumnDataType.Int32 ) },
                { "ActionId", ( 3, ExcelColumnDataType.UInt16 ) },
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
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetBuddyActionExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Name", ( 0, ExcelColumnDataType.String ) },
                { "Icon", ( 2, ExcelColumnDataType.Int32 ) },
                { "IconStatus", ( 3, ExcelColumnDataType.Int32 ) },
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
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetCompanyActionExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Name", ( 0, ExcelColumnDataType.String ) },
                { "Icon", ( 2, ExcelColumnDataType.Int32 ) },
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
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetCraftActionExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Name", ( 0, ExcelColumnDataType.String ) },
                { "Icon", ( 4, ExcelColumnDataType.UInt16 ) },
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
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetEventActionExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Name", ( 0, ExcelColumnDataType.String ) },
                { "Icon", ( 1, ExcelColumnDataType.UInt16 ) },
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
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetEmoteExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Name", ( 0, ExcelColumnDataType.String ) },
                { "Timeline0", ( 1, ExcelColumnDataType.UInt16 ) }, // Default Animation
                { "Timeline1", ( 2, ExcelColumnDataType.UInt16 ) }, // Chair Animation
                { "Timeline2", ( 3, ExcelColumnDataType.UInt16 ) }, // Groundsit?
                { "Timeline3", ( 4, ExcelColumnDataType.UInt16 ) },
                { "Timeline4", ( 5, ExcelColumnDataType.UInt16 ) },
                { "Timeline5", ( 6, ExcelColumnDataType.UInt16 ) },
                { "Timeline6", ( 7, ExcelColumnDataType.UInt16 ) },
                { "Icon", ( 20, ExcelColumnDataType.UInt32 ) },
            };

            if (language == XivLanguage.Korean)
            {
                // Set up overrides here if necessary for KR.
                columnExpectations["Icon"] = (20, ExcelColumnDataType.UInt16);
            }
            else if (language == XivLanguage.Chinese)
            {
                // Set up overrides here if necessary for CN.
                columnExpectations["Icon"] = (20, ExcelColumnDataType.UInt16);
            }
            else if (language == XivLanguage.TraditionalChinese)
            {
                // Set up overrides here if necessary for CN.
                columnExpectations["Icon"] = (20, ExcelColumnDataType.UInt16);
            }

            return columnExpectations;
        }
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetMarkerExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Icon", ( 0, ExcelColumnDataType.Int32 ) },
                { "Name", ( 2, ExcelColumnDataType.String ) },
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
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetFieldMarkerExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "VfxId", ( 0, ExcelColumnDataType.Int32) },
                { "Icon", ( 1, ExcelColumnDataType.UInt16 ) },
                { "MiniMapIcon", ( 2, ExcelColumnDataType.UInt16 ) },
                { "Name", ( 3, ExcelColumnDataType.String ) },
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
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetVfxExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "Path", ( 0, ExcelColumnDataType.String ) },
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
        private static Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> GetAquariumFishExpectations(XivLanguage language = XivLanguage.English)
        {

            var columnExpectations = new Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)>()
            {
                { "ItemId", ( 2, ExcelColumnDataType.UInt32) },
                { "Size", ( 1, ExcelColumnDataType.UInt8) },
                { "FishId", ( 3, ExcelColumnDataType.UInt16) },
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
