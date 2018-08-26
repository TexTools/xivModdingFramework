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
using xivModdingFramework.Exd.Enums;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.Exd.FileTypes
{
    /// <summary>
    /// This class contains the methods that deal with the .exh and .exd file types
    /// </summary>
    public class Ex
    {
        private const string ExhExtension = ".exh";
        private const string ExdExtension = ".exd";

        private readonly string _langCode;

        private readonly DirectoryInfo _gameDirectory;

        public Dictionary<int, int> OffsetTypeDict { get; private set; }

        public List<int> PageList { get; private set; }

        public List<int> LanguageList { get; private set; }


        /// <summary>
        /// Reads and parses Ex Header files.
        /// </summary>
        /// <param name="gameDirectory">The install directory for the game.</param>
        /// <param name="lang">The language in which to read the data.</param>
        public Ex(DirectoryInfo gameDirectory, XivLanguage lang)
        {
            _gameDirectory = gameDirectory;
            _langCode = lang.GetLanguageCode();
        }

        /// <summary>
        /// Reads and parses Ex Header files, uses english as default language.
        /// </summary>
        /// <remarks>
        /// Used for ex files that do not have language data
        /// </remarks>
        /// <param name="gameDirectory">The install directory for the game.</param>
        public Ex(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
            _langCode = XivLanguage.English.GetLanguageCode();
        }

        /// <summary>
        /// Reads and parses the ExHeader file
        /// </summary>
        /// <param name="exFile">The Ex file to use.</param>
        private void ReadExHeader(XivEx exFile)
        {
            OffsetTypeDict = new Dictionary<int, int>();
            PageList = new List<int>();
            LanguageList = new List<int>();

            var exdFolderHash = HashGenerator.GetHash("exd");
            var exdFileHash = HashGenerator.GetHash(exFile + ExhExtension);

            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);

            var offset = index.GetDataOffset(exdFolderHash, exdFileHash, XivDataFile._0A_Exd);

            if (offset == 0)
            {
                throw new Exception("Offset cannot be 0.");
            }

            var exhData = dat.GetType2Data(offset, XivDataFile._0A_Exd);

            // Big Endian Byte Order 
            using (var br = new BinaryReaderBE(new MemoryStream(exhData)))
            {
                var signature      = br.ReadInt32();
                var version        = br.ReadInt16();
                var dataSetChunk   = br.ReadInt16();
                var dataSetCount   = br.ReadInt16();
                var pageTableCount = br.ReadInt16();
                var langTableCount = br.ReadInt16();
                var unknown        = br.ReadInt16();
                var unknown1       = br.ReadInt32();
                var entryCount     = br.ReadInt32();
                br.ReadBytes(8);

                for (var i = 0; i < dataSetCount; i++)
                {
                    var dataType = br.ReadInt16();
                    var dataOffset = br.ReadInt16();

                    if (!OffsetTypeDict.ContainsKey(dataOffset))
                    {
                        OffsetTypeDict.Add(dataOffset, dataType);
                    }
                }

                for (var i = 0; i < pageTableCount; i++)
                {
                    var pageNumber = br.ReadInt32();
                    var pageSize = br.ReadInt32();
                    PageList.Add(pageNumber);
                }

                for (var i = 0; i < langTableCount; i++)
                {
                    var langCode = br.ReadInt16();
                    LanguageList.Add(langCode);
                }
            }
        }

        /// <summary>
        /// Reads and parses the ExData file
        /// </summary>
        /// <remarks>
        /// This reads the data at each index of the exd file
        /// It then places the data in a dictionary with format [index, raw data]
        /// </remarks>
        /// <param name="exFile"></param>
        /// <returns>A dictionary containing the Index and Raw Data of the ex file</returns>
        public Dictionary<int, byte[]> ReadExData(XivEx exFile)
        {
            var exdOffsetList = new List<int>();
            var exdDataDictionary = new Dictionary<int, byte[]>();

            ReadExHeader(exFile);

            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);

            var language = "_" + _langCode;

            // Some Ex files are universal and do not have a language code
            if (LanguageList.Count <= 1)
            {
                language = "";
            }

            // Each page is a new exd file
            // A good example is item_[page]_[language].exd 
            // item_0_en.exd, item_500_en.exd, item_1000_en.exd, etc.
            foreach (var page in PageList)
            {
                var exdFile = exFile + "_" + page + language + ExdExtension;

                var exdFolderHash = HashGenerator.GetHash("exd");
                var exdFileHash = HashGenerator.GetHash(exdFile);

                exdOffsetList.Add(index.GetDataOffset(exdFolderHash, exdFileHash, XivDataFile._0A_Exd));
            }

            foreach (var offset in exdOffsetList)
            {
                var exData = dat.GetType2Data(offset, XivDataFile._0A_Exd);

                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(exData)))
                {
                    br.ReadBytes(8);
                    var offsetTableSize = br.ReadInt32();

                    for (var i = 0; i < offsetTableSize; i += 8)
                    {
                        br.BaseStream.Seek(i + 32, SeekOrigin.Begin);

                        var entryNum = br.ReadInt32();
                        var entryOffset = br.ReadInt32();

                        br.BaseStream.Seek(entryOffset, SeekOrigin.Begin);

                        var entrySize = br.ReadInt32();
                        br.ReadBytes(2);


                        exdDataDictionary.Add(entryNum, br.ReadBytes(entrySize));
                    }
                }
            }

            return exdDataDictionary;
        }
    }
}