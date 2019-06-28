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

using System.IO;
using System.IO.Compression;
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

            if (item.Category.Equals("UI"))
            {
                if (item.ItemSubCategory != null && !item.ItemCategory.Equals(string.Empty))
                {
                    path = $"{saveDirectory.FullName}/{item.Category}/{item.ItemCategory}/{item.ItemSubCategory}/{validItemName}";
                }
                else
                {
                    path = $"{saveDirectory.FullName}/{item.Category}/{item.ItemCategory}/{validItemName}";
                }

                if (path.Contains("???"))
                {
                    path = path.Replace("???", "Unk");
                }
            }
            else if (item.Category.Equals(XivStrings.Character))
            {
                if (item.Name.Equals(XivStrings.Equipment_Decals) || item.Name.Equals(XivStrings.Face_Paint))
                {
                    path = $"{saveDirectory.FullName}/{item.Category}/{validItemName}";
                }
                else
                {
                    path = $"{saveDirectory.FullName}/{item.Category}/{validItemName}/{race}/{((IItemModel)item).ModelInfo.Body}";
                }
            }
            else
            {
                path = $"{saveDirectory.FullName}/{item.ItemCategory}/{validItemName}";
            }
            
            return path;
        }

        /// <summary>
        /// Determines whether a DDS file exists for the given item
        /// </summary>
        /// <param name="item">The item to check</param>
        /// <param name="saveDirectory">The save directory where the DDS should be located</param>
        /// <param name="fileName">The name of the file</param>
        /// <returns>True if the DDS file exists, false otherwise</returns>
        public static bool DDSFileExists(IItem item, DirectoryInfo saveDirectory, string fileName, XivRace race = XivRace.All_Races)
        {
            var path = MakeItemSavePath(item, saveDirectory, race);

            var fullPath = new DirectoryInfo($"{path}\\{fileName}.dds");

            return File.Exists(fullPath.FullName);
        }

        /// <summary>
        /// Determines whether a BMP file exists for the given item
        /// </summary>
        /// <param name="item">The item to check</param>
        /// <param name="saveDirectory">The save directory where the BMP should be located</param>
        /// <param name="fileName">The name of the file</param>
        /// <returns>True if the BMP file exists, false otherwise</returns>
        public static bool BMPFileExists(IItem item, DirectoryInfo saveDirectory, string fileName, XivRace race = XivRace.All_Races)
        {
            var path = MakeItemSavePath(item, saveDirectory, race);

            var fullPath = new DirectoryInfo($"{path}\\{fileName}.bmp");

            return File.Exists(fullPath.FullName);
        }
    }
}