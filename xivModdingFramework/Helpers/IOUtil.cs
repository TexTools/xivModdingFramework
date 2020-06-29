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
using System.IO.Compression;
using System.Linq;
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
                using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                {
                    await ds.ReadAsync(decompressedBytes, 0, uncompressedSize);
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
        public static string MakeItemSavePath(IItem item, DirectoryInfo saveDirectory, XivRace race = XivRace.All_Races)
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
                else
                {
                    path = $"{saveDirectory.FullName}/{item.PrimaryCategory}/{validItemName}/{race}/{((IItemModel)item).ModelInfo.SecondaryID}";
                }
            }
            else
            {
                path = $"{saveDirectory.FullName}/{item.SecondaryCategory}/{validItemName}";
            }
            
            return path;
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
    }
}