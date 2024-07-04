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

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Memory;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Mods;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;
using System.Diagnostics;
using System.Threading;
using xivModdingFramework.Cache;

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
            if(item == null)
            {
                return saveDirectory.FullName;
            }
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
            else
            {
                var res = ExtractRaceRegex.Match(path);
                if (res.Success)
                {
                    xivRace = XivRaces.GetXivRace(res.Groups[1].Value);
                }
            }

            return xivRace;
        }


        private static Regex ExtractRaceRegex = new Regex("c([0-9]{4})");
        private static Regex SimpleRootExtractRegex = new Regex("([a-z][0-9]{4}[a-z][0-9]{4})");
        private static Regex PrimaryExtractionRegex = new Regex("([a-z][0-9]{4})[a-z][0-9]{4}");

        /// <summary>
        /// Extracts just the secondary ID portion of a file name,
        /// Ex. [c0101b1234_sho.mdl] => b1234
        /// </summary>
        /// <param name="filenameOrPath"></param>
        /// <returns></returns>
        public static string GetPrimaryIdFromFileName(string filenameOrPath)
        {
            if (string.IsNullOrWhiteSpace(filenameOrPath))
            {
                return "";
            }
            filenameOrPath = Path.GetFileNameWithoutExtension(filenameOrPath);

            var match = PrimaryExtractionRegex.Match(filenameOrPath);
            if (!match.Success)
            {
                return "";
            }

            return match.Groups[1].Value;
        }

        /// <summary>
        /// Extracts the primary and secondary ID portion of a filename
        /// Ex. [c0101b1234_sho.mdl] => c0101b1234
        /// </summary>
        /// <param name="filenameOrPath"></param>
        /// <returns></returns>
        public static string GetPrimarySecondaryFromFilename(string filenameOrPath)
        {

            if (string.IsNullOrWhiteSpace(filenameOrPath))
            {
                return "";
            }
            filenameOrPath = Path.GetFileNameWithoutExtension(filenameOrPath);

            var match = SimpleRootExtractRegex.Match(filenameOrPath);
            if (!match.Success)
            {
                return "";
            }

            return match.Groups[1].Value;
        }

        /// <summary>
        /// Creates a row-by-row Matrix from a column order float set.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static float[] TransposeMatrix(float[] data)
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

            // Scan them in reverse as the most specific ones are listed last.
            Array.Reverse(files);
            foreach (var f in files)
            {
                var file = (XivDataFile)f;
                var prefix = file.GetFolderKey();

                if (path.StartsWith(prefix))
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

        /// <summary>
        /// Safely checks if the given file lives in the user's temp directory, and deletes the file IF and only IF it does.
        /// </summary>
        /// <param name="dir"></param>
        public static void DeleteTempFile(string file)
        {
            if (String.IsNullOrWhiteSpace(file))
            {
                return;
            }
            if (file.StartsWith(Path.GetTempPath()))
            {
                File.Delete(file);
            } else if (file.StartsWith(XivCache.FrameworkSettings.TempDirectory))
            {
                File.Delete(file);
            }
        }


        private static Regex MtrlSuffixExtractionRegex = new Regex("\\/?mt_[a-z][0-9]{4}[a-z][0-9]{4}(?:_[a-z]{3})?_([a-z]+)\\.mtrl");
        public static string GetMaterialSuffix(string mtrlNameOrPath)
        {
            var match = MtrlSuffixExtractionRegex.Match(mtrlNameOrPath);
            if (!match.Success)
            {
                return "";
            }
            return match.Groups[1].Value;
        }

        private static Regex MtrlSlotExtractionRegex = new Regex("_([a-z]{3})(?:_.*)?\\.mtrl");
        public static string GetMaterialSlot(string mtrlNameOrPath)
        {
            var match = MtrlSlotExtractionRegex.Match(mtrlNameOrPath);
            if (!match.Success)
            {
                return "";
            }
            return match.Groups[1].Value;
        }

        /// <summary>
        /// Safely checks if the given directory is a temporary directory, and deletes it IF and only IF it is a temporary file directory.
        /// </summary>
        /// <param name="dir"></param>
        public static void DeleteTempDirectory(string dir)
        {
            if(String.IsNullOrWhiteSpace(dir))
            {
                return;
            }
            if (dir.StartsWith(Path.GetTempPath()))
            {
                RecursiveDeleteDirectory(dir);
            } else if (dir.StartsWith(XivCache.FrameworkSettings.TempDirectory))
            {
                RecursiveDeleteDirectory(dir);
            }
        }
        public static void RecursiveDeleteDirectory(string dir)
        {
            RecursiveDeleteDirectory(new DirectoryInfo(dir));
        }
        public static void RecursiveDeleteDirectory(DirectoryInfo baseDir)
        {
            if (!baseDir.Exists)
                return;

            foreach (var dir in baseDir.EnumerateDirectories())
            {
                RecursiveDeleteDirectory(dir);
            }
            baseDir.Delete(true);
        }



        /// <summary>
        /// Simple no-op import progress handler
        /// </summary>
        public static IProgress<(int current, int total, string message)> NoOpImportProgress = new Progress<(int current, int total, string message)>((update) =>
        {
            //No-Op
        });

        public static string ReadOffsetString(BinaryReader br, long offsetBase, bool utf8 = true)
        {
            var offset = br.ReadInt32();
            var pos = br.BaseStream.Position;
            br.BaseStream.Seek(offsetBase + offset, SeekOrigin.Begin);
            var st = ReadNullTerminatedString(br, utf8);
            br.BaseStream.Seek(pos, SeekOrigin.Begin);
            return st;
        }
        public static string ReadNullTerminatedString(BinaryReader br, bool utf8 = true)
        {
            var data = new List<byte>();
            var b = br.ReadByte();
            while (b != 0)
            {
                data.Add(b);
                b = br.ReadByte();
            }
            if (utf8)
            {
                return System.Text.Encoding.UTF8.GetString(data.ToArray());
            }else
            {
                return System.Text.Encoding.ASCII.GetString(data.ToArray());
            }
        }

        /// <summary>
        /// Wipes the bottom 7 bits the given offset, matching it to SE style expected file increments.
        /// </summary>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static long RemoveDatNumberEmbed(long offset)
        {
            offset = offset & ~(0b1111111);
            return offset;
        }

        /// <summary>
        /// Takes an 8x Dat Embeded offset, returning the constituent parts.
        /// </summary>
        /// <param name="offset8xWithDatNumEmbed"></param>
        /// <returns></returns>
        public static (long Offset, int DatNum) Offset8xToParts(long offset8xWithDatNumEmbed)
        {
            var datNum = (int)(((ulong)offset8xWithDatNumEmbed >> 4) & 0b111);
            var offset = RemoveDatNumberEmbed(offset8xWithDatNumEmbed);
            return (offset, datNum);
        }

        public static long PartsTo8xDataOffset(long explicitFilePointer, int datNumber)
        {
            explicitFilePointer = RemoveDatNumberEmbed(explicitFilePointer);

            var unum = (uint)datNumber;

            return explicitFilePointer | (unum << 4);
        }
        public static uint PartsToRawDataOffset(uint dataPointer, int datNumber)
        {

            var unum = (uint)datNumber;

            unchecked
            {
                dataPointer = dataPointer & ((uint)~0x0F);
            }

            return dataPointer | (unum << 1);
        }

        /// <summary>
        /// Takes a uint 32 Embeded offset, returning the constituent parts.
        /// </summary>
        /// <param name="offset8xWithDatNumEmbed"></param>
        /// <returns></returns>
        public static (long Offset, int DatNum) RawOffsetToParts(uint sqpackOffset)
        {
            return Offset8xToParts(sqpackOffset * 8L);
        }

        public static string[] GetFilesInFolder(string path, string format = "*.*")
        {
            return Directory.GetFiles(path, format, SearchOption.AllDirectories);
        }


        private static Regex _InvalidRegex = new Regex("[^a-z0-9\\.\\/\\-_{}]");
        public static bool IsFFXIVInternalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;


            if (_InvalidRegex.IsMatch(path))
            {
                return false;
            }

            foreach (XivDataFile df in Enum.GetValues(typeof(XivDataFile)))
            {
                if (path.StartsWith(df.GetFolderKey()))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns whether or not this file is one of the constituent filetypes TT-controlled meta files.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static bool IsMetaInternalFile(string path)
        {
            var ext = Path.GetExtension(path).ToLower();
            switch (ext)
            {
                case ".cmp":
                case ".eqp":
                case ".eqdp":
                case ".gmp":
                case ".est":
                case ".imc":
                    return true;
                default:
                    return false;
            }
        }


        public static FileStorageInformation MakeGameStorageInfo(XivDataFile df, long offset8x)
        {
            if(offset8x <= 0)
            {
                throw new InvalidDataException("Cannot make game storage handle for invalid offset.");
            }

            // Create standard Game DAT file request info.
            var info = new FileStorageInformation();
            var parts = IOUtil.Offset8xToParts(offset8x);
            info.RealOffset = parts.Offset;
            info.RealPath = Dat.GetDatPath(df, parts.DatNum);
            info.StorageType = EFileStorageType.CompressedBlob;

            // We could check the file size here, but since this is a temporary file handle, and we don't know if we actually need the pointer...
            // Just set it to 0, then code down the line can identify that it needs to check the file size manually if needed.
            info.FileSize = 0;

            return info;
        }

        public static async Task UnzipFile(string zipLocation, string destination, string file)
        {
            await UnzipFiles(zipLocation, destination, new List<string>() { file });
        }

        public static async Task UnzipFiles(string zipLocation, string destination, Func<string, bool> selector)
        {
            var filesToUnzip = new HashSet<string>();
            // Select all files in zip if null.
            using (var zip = new Ionic.Zip.ZipFile(zipLocation))
            {
                var files = (zip.Entries.Select(x => ReplaceSlashes(x.FileName).ToLower()));

                files = files.Where(x =>
                {
                    return selector(x);
                });

                filesToUnzip.UnionWith(files);
            }

            await UnzipFiles(zipLocation, destination, filesToUnzip);
        }
        public  static async Task UnzipFiles(string zipLocation, string destination, IEnumerable<string> files = null)
        {
            HashSet<string> filesToUnzip = null;
            if (files != null)
            {
                filesToUnzip = new HashSet<string>();
                foreach (var f in files)
                {
                    filesToUnzip.Add(ReplaceSlashes(f).ToLower());
                }
            }
            else
            {
                filesToUnzip = new HashSet<string>();
                // Select all files in zip if null.
                using (var zip = new Ionic.Zip.ZipFile(zipLocation))
                {
                    filesToUnzip.UnionWith(zip.Entries.Select(x => ReplaceSlashes(x.FileName).ToLower()));
                }
            }

            Directory.CreateDirectory(destination);

            // Extract each zip file independently in parallel.
            var tasks = new List<Task>();
            foreach (var file in filesToUnzip)
            {
                var taskFile = file;
                tasks.Add(Task.Run(async () =>
                {
                    using (var zip = new Ionic.Zip.ZipFile(zipLocation))
                    {
                        var toUnzip = zip.Entries.Where(x => ReplaceSlashes(x.FileName).ToLower() == taskFile);
                        foreach (var e in toUnzip)
                        {
                            e.Extract(destination, Ionic.Zip.ExtractExistingFileAction.OverwriteSilently);
                        }
                    }
                }));
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Makes all slashes into backslashes for consistency.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static string ReplaceSlashes(string path)
        {
            return path.Replace("/", "\\");
        }

        public static bool IsDirectory(string path)
        {
            if(path == null) return false;
            if(path.EndsWith("/") || path.EndsWith("\\"))
            {
                path = path.Substring(0, path.Length - 1);
            }

            try
            {
                FileAttributes attr = File.GetAttributes(path);

                //detect whether its a directory or file
                if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                    return true;
                else
                    return false;
            }
            catch
            {
                return false;
            }
        }

        public static string MakePathSafe(string fileName, bool makeLowercase = true)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '-');
            }
            if (makeLowercase)
            {
                fileName = fileName.ToLower();
            }
            return fileName.Trim();
        }
        public static void CopyFolder(string sourcePath, string targetPath)
        {
            Directory.CreateDirectory(targetPath);

            //Now Create all of the directories
            foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
            }

            //Copy all the files & Replaces any files with the same name
            foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
            }
        }

        public static byte[] GetImageSharpPixels(Image<Bgra32> img)
        {
            var mg = img.GetPixelMemoryGroup();
            using (var ms = new MemoryStream())
            {
                foreach (var g in mg)
                {
                    var data = MemoryMarshal.AsBytes(g.Span).ToArray();
                    ms.Write(data, 0, data.Length);
                }
                return ms.ToArray();
            }
        }
        public static byte[] GetImageSharpPixels(Image<Rgba32> img)
        {
            var mg = img.GetPixelMemoryGroup();
            using (var ms = new MemoryStream())
            {
                foreach (var g in mg)
                {
                    var data = MemoryMarshal.AsBytes(g.Span).ToArray();
                    ms.Write(data, 0, data.Length);
                }
                return ms.ToArray();
            }
        }

        public static string GetUniqueSubfolder(string basePath, string prefix = "")
        {
            var id = 0;
            var path = Path.GetFullPath(Path.Combine(basePath, prefix + id.ToString()));
            while (Directory.Exists(path))
            {
                id++;
                path = Path.GetFullPath(Path.Combine(basePath, prefix + id.ToString()));
            }
            Directory.CreateDirectory(path);

            return path;
        }
        public static string GetFrameworkTempSubfolder(string prefix = "")
        {
            var basePath = GetFrameworkTempFolder();
            return GetUniqueSubfolder(basePath, prefix);
        }
        public static string GetFrameworkTempFolder()
        {
            var path = Path.Combine(XivCache.FrameworkSettings.TempDirectory, "xivmf");
            Directory.CreateDirectory(path);
            return path;
        }
        public static string GetFrameworkTempFile()
        {

            return Path.Combine(GetFrameworkTempFolder(), Guid.NewGuid().ToString());
        }

        public static void ClearTempFolder()
        {
            var path = GetFrameworkTempFolder();
            DeleteTempDirectory(path);
        }

        public static string GetParentIfExists(string path, string target, bool caseSensitive = true)
        {
            var compare = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (path.EndsWith(target, compare))
            {
                return path;
            }

            var par = Directory.GetParent(path);

            if(par == null)
            {
                return null;
            }

            return GetParentIfExists(par.FullName, target, caseSensitive);
        }

        public static bool IsPowerOfTwo(long x)
        {
            return IsPowerOfTwo((ulong)x);
        }
        public static bool IsPowerOfTwo(ulong x)
        {
            return (x != 0) && ((x & (x - 1)) == 0);
        }

        public static int RoundToPowerOfTwo(int x)
        {
            var min = FloorPower2(x);
            var max = CeilPower2(x);


            return max - x < x - min ? max : min;
        }

        private static int CeilPower2(int x)
        {
            if (x < 2)
            {
                return 1;
            }
            return (int)Math.Pow(2, (int)Math.Log(x - 1, 2) + 1);
        }

        private static int FloorPower2(int x)
        {
            if (x < 1)
            {
                return 1;
            }
            return (int)Math.Pow(2, (int)Math.Log(x, 2));
        }

    }
}