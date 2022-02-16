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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Resources;

namespace xivModdingFramework.Helpers
{
    public static class IOUtil
    {
        /// <summary>
        /// Compresses raw byte data.
        /// </summary>
        /// <param name="uncompressedBytes">The data to be compressed.</param>
        /// <returns>The compressed byte data.</returns>
        public static async Task<byte[]> Compressor(byte[] uncompressedBytes)
        {
            using (var uMemoryStream = new MemoryStream(uncompressedBytes))
            {
                byte[] compbytes = null;
                using (var cMemoryStream = new MemoryStream())
                {
                    using (var deflateStream = new DeflateStream(cMemoryStream, CompressionMode.Compress))
                    {
                        await uMemoryStream.CopyToAsync(deflateStream);
                        deflateStream.Close();
                        compbytes = cMemoryStream.ToArray();
                    }
                }
                return compbytes;
            }
        }

        /// <summary>
        /// Decompresses raw byte data.
        /// </summary>
        /// <param name="compressedBytes">The byte data to decompress.</param>
        /// <param name="uncompressedSize">The final size of the compressed data after decompression.</param>
        /// <returns>The decompressed byte data.</returns>
        public static async Task<byte[]> Decompressor(byte[] compressedBytes, int uncompressedSize)
        {
            var decompressedBytes = new byte[uncompressedSize];

            using (var ms = new MemoryStream(compressedBytes))
            {
                using (var ds = new DeflateStream(ms, CompressionMode.Decompress, true))
                {
                    int offset = 0; // offset for writing into buffer
                    int bytesRead; // number of bytes read from Read operation
                    while ((bytesRead = await ds.ReadAsync(decompressedBytes, offset, uncompressedSize - offset)) > 0)
                    {
                        offset += bytesRead;  // offset in buffer for results of next reading
                        if (bytesRead == uncompressedSize) break;
                    }
                }
            }

            return decompressedBytes;
        }

        /// <summary>
        /// Makes the save path the item will be saved to
        /// </summary>
        /// <param name="item">The item to be saved</param>
        /// <param name="saveDirectory">The base directory to save to</param>
        /// <returns>A string containing the full save path for the given item</returns>
        public static string MakeItemSavePath(IItem item, DirectoryInfo saveDirectory, XivRace race = XivRace.All_Races, int primaryNumber = -1)
        {
            string path, validItemName;

            // Check for invalid characters and replace with dash 
            if (item.Name.Equals("???"))
            {
                validItemName = "Unk";
            }
            else
            {
                validItemName = string.Join("：", item.Name.Split(Path.GetInvalidFileNameChars()));
            }

            if (item.PrimaryCategory.Equals("UI"))
            {
                if (item.TertiaryCategory != null && !item.SecondaryCategory.Equals(string.Empty))
                {
                    path = $"{saveDirectory.FullName}/{item.PrimaryCategory}/{item.SecondaryCategory}/{item.TertiaryCategory}/{validItemName}";
                }
                else
                {
                    path = $"{saveDirectory.FullName}/{item.PrimaryCategory}/{item.SecondaryCategory}/{validItemName}";
                }

                if (path.Contains("???"))
                {
                    path = path.Replace("???", "Unk");
                }
            }
            else if (item.PrimaryCategory.Equals(XivStrings.Character))
            {
                if (item.Name.Equals(XivStrings.Equipment_Decals) || item.Name.Equals(XivStrings.Face_Paint))
                {
                    path = $"{saveDirectory.FullName}/{item.PrimaryCategory}/{validItemName}";
                }
                else if(primaryNumber >= 0)
                {
                    path = $"{saveDirectory.FullName}/{item.PrimaryCategory}/{item.SecondaryCategory}/{race}/{primaryNumber}";
                } else
                {
                    path = $"{saveDirectory.FullName}/{item.PrimaryCategory}/{item.SecondaryCategory}/{race}/{((IItemModel)item).ModelInfo.SecondaryID}";
                }
            }
            else
            {
                path = $"{saveDirectory.FullName}/{item.SecondaryCategory}/{validItemName}";
            }
            
            return path;
        }


        public static XivRace GetRaceFromPath(string path)
        {
            if(path == null)
            {
                return XivRace.All_Races;
            }

            var xivRace = XivRace.All_Races;

            if (path.Contains("ui/") || path.Contains(".avfx"))
            {
                xivRace = XivRace.All_Races;
            }
            else if (path.Contains("monster"))
            {
                xivRace = XivRace.Monster;
            }
            else if (path.Contains(".tex") || path.Contains(".mdl") || path.Contains(".atex"))
            {
                if (path.Contains("weapon") || path.Contains("/common/"))
                {
                    xivRace = XivRace.All_Races;
                }
                else
                {
                    if (path.Contains("demihuman"))
                    {
                        xivRace = XivRace.DemiHuman;
                    }
                    else if (path.Contains("/v"))
                    {
                        var raceCode = path.Substring(path.IndexOf("_c") + 2, 4);
                        xivRace = XivRaces.GetXivRace(raceCode);
                    }
                    else
                    {
                        var raceCode = path.Substring(path.IndexOf("/c") + 2, 4);
                        xivRace = XivRaces.GetXivRace(raceCode);
                    }
                }

            }

            return xivRace;
        }

        /// <summary>
        /// Creates a row-by-row Matrix from a column order float set.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static float[] RowsFromColumns(float[] data)
        {
            var formatted = new float[16];

            formatted[0] = data[0];
            formatted[1] = data[4];
            formatted[2] = data[8];
            formatted[3] = data[12];


            formatted[4] = data[1];
            formatted[5] = data[5];
            formatted[6] = data[9];
            formatted[7] = data[13];

            formatted[8] = data[2];
            formatted[9] = data[6];
            formatted[10] = data[10];
            formatted[11] = data[14];

            formatted[12] = data[3];
            formatted[13] = data[7];
            formatted[14] = data[11];
            formatted[15] = data[15];

            return formatted;
        }

        /// <summary>
        /// Replaces the bytes in a given byte array with the bytes from another array, starting at the given index of the original array.
        /// </summary>
        /// <param name="original"></param>
        /// <param name="toInject"></param>
        /// <param name="index"></param>
        public static void ReplaceBytesAt(List<byte> original, byte[] toInject, int index)
        {
            for(var i = 0; i < toInject.Length; i++)
            {
                original[index + i] = toInject[i];
            };
        }
        /// <summary>
        /// Replaces the bytes in a given byte array with the bytes from another array, starting at the given index of the original array.
        /// </summary>
        /// <param name="original"></param>
        /// <param name="toInject"></param>
        /// <param name="index"></param>
        public static void ReplaceBytesAt(byte[] original, byte[] toInject, int index)
        {
            for (var i = 0; i < toInject.Length; i++)
            {
                original[index + i] = toInject[i];
            };
        }



        /// <summary>
        /// Resolves what XivDataFile a file lives in based upon its internal FFXIV directory path.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static XivDataFile GetDataFileFromPath(string path)
        {
            var files = Enum.GetValues(typeof(XivDataFile));
            foreach (var f in files)
            {
                var file = (XivDataFile)f;
                var prefix = file.GetFolderKey();

                var match = Regex.Match(path, "^" + prefix);
                if (match.Success)
                {
                    return file;
                }
            }

            throw new Exception("Could not resolve data file - Invalid internal FFXIV path.");
        }

        /// <summary>
        /// Validates a string to ensure it's a valid URL.
        /// If the given URL is totally invalid, NULL is returned.
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public static string ValidateUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri uriResult)
                   && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps) ? url : null;
        }
        public static byte[] ReadAllBytes(this BinaryReader reader)
        {
            const int bufferSize = 4096;
            using (var ms = new MemoryStream())
            {
                byte[] buffer = new byte[bufferSize];
                int count;
                while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
                    ms.Write(buffer, 0, count);
                return ms.ToArray();
            }

        }
    }
}