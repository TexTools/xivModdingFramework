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

using SixLabors.ImageSharp.Processing;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.Exd.Enums;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Mods;
using static xivModdingFramework.Exd.FileTypes.Ex;

namespace xivModdingFramework.General
{
    /// <summary>
    /// This class contains the methods to get data from modelchara exd
    /// </summary>
    public static class XivModelChara
    {
        /// <summary>
        /// Shortcut accessor for reading the modelchara Ex Data
        /// </summary>
        /// <param name="gameDirectory"></param>
        /// <returns>Dictionary with modelchara data</returns>
        public static async Task<Dictionary<int, Ex.ExdRow>> GetModelCharaData(ModTransaction tx = null)
        {
            var ex = new Ex();
            var exData = await ex.ReadExData(XivEx.modelchara, tx);
            return exData;
        }

        /// <summary>
        /// Gets the model info from the modelchara exd file
        /// </summary>
        /// <param name="gameDirectory">The game directory</param>
        /// <param name="index">The index of the data</param>
        /// <returns>The XivModelInfo data</returns>
        public static async Task<XivModelInfo> GetModelInfo(int index, ModTransaction tx = null)
        {
            var ex = new Ex();
            var modelCharaEx = await ex.ReadExData(XivEx.modelchara, tx);
            return GetModelInfo(modelCharaEx[index]);
        }

        /// <summary>
        /// Gets the model info from the modelchara exd data
        /// </summary>
        /// <param name="row">The modelchara ex data</param>
        /// <param name="index">The index of the data</param>
        /// <returns>The XivModelInfo data</returns>
        public static XivModelInfo GetModelInfo(ExdRow row)
        {
            var xivModelInfo = new XivMonsterModelInfo();

            var type = (byte)row.GetColumnByName("Type");
            xivModelInfo.PrimaryID = (ushort) row.GetColumnByName("PrimaryId");
            xivModelInfo.SecondaryID = (byte)row.GetColumnByName("SecondaryId");
            xivModelInfo.ImcSubsetID = (byte)row.GetColumnByName("Variant");

            if (type == 2)
            {
                xivModelInfo.ModelType = XivItemType.demihuman;
            }
            else if (type == 3)
            {
                xivModelInfo.ModelType = XivItemType.monster;
            }
            else
            {
                xivModelInfo.ModelType = XivItemType.unknown;
            }

            return xivModelInfo;
        }
    }
}