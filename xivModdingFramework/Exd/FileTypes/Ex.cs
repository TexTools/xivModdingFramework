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

using HelixToolkit.SharpDX.Core.Helper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.Exd.Enums;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Mods;
using xivModdingFramework.SqPack.FileTypes;

using Index = xivModdingFramework.SqPack.FileTypes.Index;

namespace xivModdingFramework.Exd.FileTypes
{
    // Lifted directly from Lumina
    public enum ExcelColumnDataType : ushort
    {
        String = 0x0,
        Bool = 0x1,
        Int8 = 0x2,
        UInt8 = 0x3,
        Int16 = 0x4,
        UInt16 = 0x5,
        Int32 = 0x6,
        UInt32 = 0x7,
        // unused?
        Unk = 0x8,
        Float32 = 0x9,
        Int64 = 0xA,
        UInt64 = 0xB,
        // unused?
        Unk2 = 0xC,

        // 0 is read like data & 1, 1 is like data & 2, 2 = data & 4, etc...
        PackedBool0 = 0x19,
        PackedBool1 = 0x1A,
        PackedBool2 = 0x1B,
        PackedBool3 = 0x1C,
        PackedBool4 = 0x1D,
        PackedBool5 = 0x1E,
        PackedBool6 = 0x1F,
        PackedBool7 = 0x20,
    }

    /// <summary>
    /// This class contains the methods that deal with the .exh and .exd file types
    /// </summary>
    public class Ex
    {
        private const string ExhExtension = ".exh";
        private const string ExdExtension = ".exd";

        private readonly string _langCode;
        private readonly XivLanguage _language;


        /// <summary>
        /// Columns, keyed by data offset.
        /// </summary>
        public List<(int Offset, ExcelColumnDataType Type)> Columns { get; private set; }

        public List<int> Pages { get; private set; }

        public List<int> LanguageList { get; private set; }

        private int DataOffset;



        /// <summary>
        /// Reads and parses Ex Header files.
        /// </summary>
        /// <param name="gameDirectory">The install directory for the game.</param>
        /// <param name="lang">The language in which to read the data.</param>
        public Ex()
        {
            var lang = XivCache.GameInfo.GameLanguage;
            _langCode = lang.GetLanguageCode();
            _language = lang;
        }

        /// <summary>
        /// Reads and parses the ExHeader file
        /// </summary>
        /// <param name="exFile">The Ex file to use.</param>
        private async Task ReadExHeader(XivEx exFile)
        {
            Columns = new List<(int Offset, ExcelColumnDataType Type)>();
            Pages = new List<int>();
            LanguageList = new List<int>();

            // Readonly TX.  We don't allow live modification of exd/exh files.
            var tx = ModTransaction.BeginTransaction();

            var file = "exd/" + exFile + ExhExtension;

            if (!await tx.FileExists(file))
            {
                throw new FileNotFoundException($"Could not find offset for exd/{exFile}{ExhExtension}");
            }

            var exhData = await tx.ReadFile(file);

            await Task.Run(() =>
            {
                // Big Endian Byte Order 
                using (var br = new BinaryReaderBE(new MemoryStream(exhData)))
                {
                    var signature      = br.ReadInt32();
                    var version        = br.ReadInt16();
                    DataOffset = br.ReadInt16();
                    var columnCount    = br.ReadInt16();
                    var pageCount      = br.ReadInt16();
                    var languageCount  = br.ReadInt16();
                    var unknown1       = br.ReadUInt16();
                    var variant        = br.ReadByte();
                    var unknown2       = br.ReadByte();
                    var unknown3       = br.ReadUInt16();
                    var rowCount       = br.ReadInt32();

                    var u4 = br.ReadBytes(8);

                    for (var i = 0; i < columnCount; i++)
                    {
                        var dataType = br.ReadUInt16();
                        var columOffset = br.ReadInt16();

                        Columns.Add((columOffset, (ExcelColumnDataType) dataType));
                    }

                    for (var i = 0; i < pageCount; i++)
                    {
                        var pageNumber = br.ReadInt32();
                        var pageSize = br.ReadInt32();
                        Pages.Add(pageNumber);
                    }

                    for (var i = 0; i < languageCount; i++)
                    {
                        var langCode = br.ReadInt16();
                        if (langCode != 0)
                        {
                            LanguageList.Add(langCode);
                        }
                    }
                }
            });
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
        public async Task<Dictionary<int, ExdRow>> ReadExData(XivEx exFile, ModTransaction tx = null)
        {
            var expectations = ExColumnExpectations.GetColumnExpectations(exFile, _language);
            var exdNameOffsetDictionary = new Dictionary<long, string>();
            var parsedRows = new Dictionary<int, ExdRow>();
            var errorString = "";

            // Load the header data.
            await ReadExHeader(exFile);

            var language = "_" + _langCode;

            // Some Ex files are universal and do not have a language code
            if (LanguageList.Count == 0)
            {
                language = "";
            }

            if (tx == null)
            {
                tx = ModTransaction.BeginTransaction();
            }

            await Task.Run(async () =>
            {
                foreach (var page in Pages)
                {
                    // Each page is a new exd file
                    // A good example is item_[page]_[language].exd 
                    // item_0_en.exd, item_500_en.exd, item_1000_en.exd, etc.

                    // There are other EXD folders, but the only things we actually care about live in exd/
                    var exdFile = "exd/" + exFile + "_" + page + language + ExdExtension;

                    try
                    {
                        // File may not exist in unusual situations (Ex. Benchmarks)
                        if (!await tx.FileExists(exdFile))
                            continue;

                        // Always read the original base game file for now.
                        var exData = await tx.ReadFile(exdFile);

                        // Big Endian Byte Order 
                        using (var br = new BinaryReaderBE(new MemoryStream(exData)))
                        {

                            var magic = br.ReadUInt32();
                            var version = br.ReadUInt16();
                            var unknown1 = br.ReadUInt16();
                            var rowOffsets = br.ReadInt32();

                            var unknownArray = new ushort[10];
                            for(int i = 0; i < unknownArray.Length; i++)
                            {
                                unknownArray[i] = br.ReadUInt16();
                            }

                            var endOfHeader = br.BaseStream.Position;

                            for (var i = 0; i < rowOffsets; i += 8)
                            {
                                br.BaseStream.Seek(endOfHeader + i, SeekOrigin.Begin);

                                var rowId = br.ReadInt32();
                                var rowOffset = br.ReadInt32();

                                br.BaseStream.Seek(rowOffset, SeekOrigin.Begin);

                                var entrySize = br.ReadInt32();
                                var subRowCount =  br.ReadUInt16();

                                if (!parsedRows.ContainsKey(rowId))
                                {
                                    var row = ReadRow(br, exFile, rowId, entrySize, subRowCount, expectations);
                                    parsedRows.Add(rowId, row);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errorString += $"File: {exdFile}\nError: {ex.Message}\n\n";
                    }
                }
            });


            if (!string.IsNullOrEmpty(errorString))
            {
                throw new Exception($"There was an error reading EX data for the following\n\n{errorString}");
            }

            // Test the Ex File.
            if(parsedRows.Count > 0)
            {
                parsedRows.First().Value.CheckColumns(expectations);
            }

            return parsedRows;
        }

        private ExdRow ReadRow(BinaryReaderBE br, XivEx exFile, int rowId, int entrySize, ushort subRowCount, Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> expectations)
        {
            var data = br.ReadBytes(entrySize);
            return new ExdRow(exFile, rowId, data, Columns, DataOffset, expectations);
        }

        public struct ExdRow
        {
            public int RowId;
            public List<(int Offset, ExcelColumnDataType Type)> Columns { get; private set; }

            public readonly byte[] RawData;

            public readonly int DataOffset;

            public readonly XivEx ExFile;

            public readonly Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> ColumnsByName;


            public ExdRow(XivEx exFile, int rowId, byte[] data, List<(int Offset, ExcelColumnDataType Type)> columns, int dataOffset, Dictionary<string, (int ColumnIndex, ExcelColumnDataType Type)> expectations)
            {
                ExFile = exFile;
                RowId = rowId;
                RawData = data;
                Columns = columns;
                DataOffset = dataOffset;
                ColumnsByName = expectations;
            }

            /// <summary>
            /// Largely copied from Lumina.
            /// Read the data for the given column.
            /// </summary>
            /// <param name="br"></param>
            /// <param name="type"></param>
            /// <returns></returns>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public object GetColumn(int column)
            {
                object? data = null;

                using (var ms = new MemoryStream(RawData))
                {
                    using (var br = new BinaryReaderBE(ms))
                    {
                        var definition = Columns[column];

                        br.BaseStream.Seek(definition.Offset, SeekOrigin.Begin);

                        switch (definition.Type)
                        {
                            case ExcelColumnDataType.String:
                                {
                                    var stringOffset = br.ReadInt32();
                                    br.BaseStream.Seek(stringOffset + DataOffset, SeekOrigin.Begin);
                                    data = IOUtil.ReadNullTerminatedString(br);
                                    break;
                                }
                            case ExcelColumnDataType.Bool:
                                {
                                    data = br.ReadBoolean();
                                    break;
                                }
                            case ExcelColumnDataType.Int8:
                                {
                                    data = br.ReadSByte();
                                    break;
                                }
                            case ExcelColumnDataType.UInt8:
                                {
                                    data = br.ReadByte();
                                    break;
                                }
                            case ExcelColumnDataType.Int16:
                                {
                                    data = br.ReadInt16();
                                    break;
                                }
                            case ExcelColumnDataType.UInt16:
                                {
                                    data = br.ReadUInt16();
                                    break;
                                }
                            case ExcelColumnDataType.Int32:
                                {
                                    data = br.ReadInt32();
                                    break;
                                }
                            case ExcelColumnDataType.UInt32:
                                {
                                    data = br.ReadUInt32();
                                    break;
                                }
                            // case ExcelColumnDataType.Unk:
                            // break;
                            case ExcelColumnDataType.Float32:
                                {
                                    data = br.ReadSingle();
                                    break;
                                }
                            case ExcelColumnDataType.Int64:
                                {
                                    data = br.ReadUInt64();
                                    break;
                                }
                            case ExcelColumnDataType.UInt64:
                                {
                                    data = br.ReadUInt64();
                                    break;
                                }
                            // case ExcelColumnDataType.Unk2:
                            // break;
                            case ExcelColumnDataType.PackedBool0:
                            case ExcelColumnDataType.PackedBool1:
                            case ExcelColumnDataType.PackedBool2:
                            case ExcelColumnDataType.PackedBool3:
                            case ExcelColumnDataType.PackedBool4:
                            case ExcelColumnDataType.PackedBool5:
                            case ExcelColumnDataType.PackedBool6:
                            case ExcelColumnDataType.PackedBool7:
                                {
                                    var shift = (int)definition.Type - (int)ExcelColumnDataType.PackedBool0;
                                    var bit = 1 << shift;

                                    var rawData = br.ReadByte();

                                    data = (rawData & bit) == bit;

                                    break;
                                }
                            default:
                                throw new ArgumentOutOfRangeException("type", $"invalid excel column type: {definition.Type}");
                        }

                        return data;
                    }
                }
            }


            /// <summary>
            /// Retrieve a column by its name, as defined by the ExColumnExpectations.cs
            /// </summary>
            /// <param name="name"></param>
            /// <returns></returns>
            /// <exception cref="ArgumentException"></exception>
            public object GetColumnByName(string name)
            {
                if (!ColumnsByName.ContainsKey(name))
                {
                    throw new ArgumentException(ExFile.ToString() + " : Invalid Column Name: " + name);
                }
                var column = ColumnsByName[name].ColumnIndex;
                return GetColumn(column);
            }

            /// <summary>
            /// Checks that the given column has the expected data type.
            /// </summary>
            /// <param name="column"></param>
            /// <param name="expectedType"></param>
            /// <exception cref="InvalidDataException"></exception>
            public void CheckColumn(int column, ExcelColumnDataType expectedType, string name = "Unknown")
            {
                if(column >= Columns.Count)
                {
                    throw new InvalidDataException(ExFile.ToString() + " Column " + column + " (" + name + ") is larger than the available column count.");
                }

                if (Columns[column].Type != expectedType)
                {
                    throw new InvalidDataException(ExFile.ToString() + " Column " + column + " (" + name + ") had unexpected data type: " + Columns[column].Type.ToString() + ".  Expected: " + expectedType.ToString());
                }
            }

            /// <summary>
            /// Checks a collection of column expecations to ensure EXD table reading will be successful.
            /// </summary>
            /// <param name="expectations"></param>
            public void CheckColumns(Dictionary<string, (int column, ExcelColumnDataType type)> expectations)
            {
                foreach(var kv in  expectations)
                {
                    CheckColumn(kv.Value.column, kv.Value.type, kv.Key);
                }
            }
        }
    }
}