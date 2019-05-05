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
using System.Threading.Tasks;
using xivModdingFramework.Exd.Enums;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;

namespace xivModdingFramework.General
{
    /// <summary>
    /// This class contains the methods to get data from modelchara exd
    /// </summary>
    public static class XivModelChara
    {
        /// <summary>
        /// Gets the data dictionary for the modelchara ex
        /// </summary>
        /// <param name="gameDirectory"></param>
        /// <returns>Dictionary with modelchara data</returns>
        public static async Task<Dictionary<int, byte[]>> GetModelCharaData(DirectoryInfo gameDirectory)
        {
            var ex = new Ex(gameDirectory);
            var exData = await ex.ReadExData(XivEx.modelchara);
            return exData;
        }

        /// <summary>
        /// Gets the model info from the modelchara exd file
        /// </summary>
        /// <param name="gameDirectory">The game directory</param>
        /// <param name="index">The index of the data</param>
        /// <returns>The XivModelInfo data</returns>
        public static async Task<XivModelInfo> GetModelInfo(DirectoryInfo gameDirectory, int index)
        {
            var xivModelInfo = new XivModelInfo();

            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int modelDataOffset = 4;

            var ex = new Ex(gameDirectory);
            var modelCharaEx = await ex.ReadExData(XivEx.modelchara);

            // Big Endian Byte Order 
            using (var br = new BinaryReaderBE(new MemoryStream(modelCharaEx[index])))
            {
                xivModelInfo.ModelID = br.ReadInt16();

                br.BaseStream.Seek(modelDataOffset, SeekOrigin.Begin);
                var modelType = br.ReadByte();
                xivModelInfo.Body = br.ReadByte();
                xivModelInfo.Variant = br.ReadByte();

                if (modelType == 2)
                {
                    xivModelInfo.ModelType = XivItemType.demihuman;
                }
                else if (modelType == 3)
                {
                    xivModelInfo.ModelType = XivItemType.monster;
                }
                else
                {
                    xivModelInfo.ModelType = XivItemType.unknown;
                }
            }

            return xivModelInfo;
        }

        /// <summary>
        /// Gets the model info from the modelchara exd data
        /// </summary>
        /// <param name="modelCharaEx">The modelchara ex data</param>
        /// <param name="index">The index of the data</param>
        /// <returns>The XivModelInfo data</returns>
        public static XivModelInfo GetModelInfo(Dictionary<int, byte[]> modelCharaEx, int index)
        {
            var xivModelInfo = new XivModelInfo();

            // These are the offsets to relevant data
            // These will need to be changed if data gets added or removed with a patch
            const int modelDataOffset = 4;

            // Big Endian Byte Order 
            using (var br = new BinaryReaderBE(new MemoryStream(modelCharaEx[index])))
            {
                xivModelInfo.ModelID = br.ReadInt16();

                br.BaseStream.Seek(modelDataOffset, SeekOrigin.Begin);
                var modelType = br.ReadByte();
                xivModelInfo.Body = br.ReadByte();
                xivModelInfo.Variant = br.ReadByte();

                if (modelType == 2)
                {
                    xivModelInfo.ModelType = XivItemType.demihuman;
                }
                else if (modelType == 3)
                {
                    xivModelInfo.ModelType = XivItemType.monster;
                }
                else
                {
                    xivModelInfo.ModelType = XivItemType.unknown;
                }
            }

            return xivModelInfo;
        }
    }
}