﻿// xivModdingFramework
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

using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TeximpNet.Compression;
using TeximpNet.DDS;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Materials.DataContainers;
using xivModdingFramework.Materials.FileTypes;
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.Enums;
using xivModdingFramework.Variants.FileTypes;

using Surface = TeximpNet.Surface;
using Index = xivModdingFramework.SqPack.FileTypes.Index;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using System.Text;
using System.Diagnostics;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.SharpDX.Core.Helper;
using xivModdingFramework.Exd.Enums;
using SharpDX.Toolkit.Graphics;
using xivModdingFramework.Models.DataContainers;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.Formats.Tga;

namespace xivModdingFramework.Textures.FileTypes
{
    /// <summary>
    /// This class contains the methods that deal with the .tex file type 
    /// </summary>
    public static class Tex
    {
        #region Consts, Structs, & Constructor

        public const uint _DDSHeaderSize = 128;
        public const uint _TexHeaderSize = 80;
        public struct TexHeader
        {
            // Bitflags
            public uint Attributes;

            // Texture Format
            public uint TextureFormat;

            public ushort Width;
            public ushort Height;

            public ushort Depth;

            public byte MipCount;
            public byte MipFlag;

            public byte ArraySize;

            // 3 Ints, representing which MipMaps to use at each LoD level.
            public uint[] LoDMips;

            public uint[] MipMapOffsets;

            /// <summary>
            /// Reads a .tex file header (80 bytes) from the given stream.
            /// </summary>
            /// <param name="br"></param>
            internal static TexHeader ReadTexHeader(BinaryReader br, long offset = -1)
            {
                var header = new TexHeader();
                if (offset >= 0)
                {
                    br.BaseStream.Seek(offset, SeekOrigin.Begin);
                }

                header.Attributes = br.ReadUInt32();
                header.TextureFormat = br.ReadUInt32();

                header.Width = br.ReadUInt16();
                header.Height = br.ReadUInt16();

                header.Depth = br.ReadUInt16();
                header.MipCount = br.ReadByte();
                header.MipFlag = (byte)(header.MipCount >> 4);
                header.MipCount = (byte)(header.MipCount & 0xF);
                header.ArraySize = br.ReadByte();

                header.LoDMips = new uint[3];
                for (int i = 0; i < header.LoDMips.Length; i++)
                {
                    header.LoDMips[i] = br.ReadUInt32();
                }

                header.MipMapOffsets = new uint[13];
                for (int i = 0; i < header.MipMapOffsets.Length; i++)
                {
                    header.MipMapOffsets[i] = br.ReadUInt32();
                }
                return header;
            }

            /// <summary>
            /// Writes a .tex file header from this.
            /// </summary>
            /// <returns>Byte array containing the header data.</returns>
            internal byte[] ToBytes()
            {
                if (this.LoDMips[1] < this.LoDMips[0] || this.LoDMips[2] < this.LoDMips[1])
                    throw new InvalidOperationException("LoDMips is not in non-descending order.");
                if (this.LoDMips[2] >= this.MipCount)
                    throw new InvalidOperationException("All LoDMips must be strictly lesser than MipCount.");
                if (this.MipFlag > 15)
                    throw new InvalidOperationException("MipFlag must be strictly lesser than 16.");
                if (this.MipCount > 13)
                    throw new InvalidOperationException("MipCount must be strictly lesser than 14.");

                var res = new byte[_TexHeaderSize];
                var bw = new BinaryWriter(new MemoryStream(res, true));
                bw.Write(this.Attributes);
                bw.Write(this.TextureFormat);
                bw.Write(this.Width);
                bw.Write(this.Height);
                bw.Write(this.Depth);
                bw.Write((byte)((this.MipFlag << 4) | this.MipCount));
                bw.Write(this.ArraySize);
                foreach (var x in this.LoDMips)
                    bw.Write(x);
                foreach (var x in this.MipMapOffsets)
                    bw.Write(x);

                Debug.Assert(bw.BaseStream.Position == _TexHeaderSize, "Data was not fully written.");
                return res;
            }
        }


        /// <summary>
        /// Gets the path to the default blank texture for a given texture format.
        /// For use when making new texture files.
        /// </summary>
        /// <param name="format"></param>
        /// <returns></returns>
        public static DirectoryInfo GetDefaultTexturePath(XivTexType usageType)
        {
            //new DirectoryInfo(Directory.GetFiles("AddNewTexturePartTexTmps", $"{Path.GetFileNameWithoutExtension(oldTexPath)}.dds", SearchOption.AllDirectories)[0]);
            var strings = Directory.GetFiles("Resources\\DefaultTextures", usageType.ToString() + ".dds", SearchOption.AllDirectories);
            if(strings.Length == 0)
            {
                strings = Directory.GetFiles("Resources\\DefaultTextures", XivTexType.Other.ToString() + ".dds", SearchOption.AllDirectories);
            }
            return new DirectoryInfo(strings[0]);
        }

        #endregion

        #region High-Level XixTex Accessors

        public static async Task<XivTex> GetXivTex(MtrlTexture tex, bool forceOriginal = false, ModTransaction tx = null)
        {
            return await GetXivTex(tex.TexturePath, forceOriginal, tx);
        }
        public static async Task<XivTex> GetXivTex(TexTypePath ttp, bool forceOriginal = false, ModTransaction tx = null)
        {
            return await GetXivTex(ttp.Path, forceOriginal, tx);
        }
        public static async Task<XivTex> GetXivTex(string path, bool forceOriginal = false, ModTransaction tx = null)
        {
            if(tx == null)
            {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginReadonlyTransaction();
            }

            var exists = (await tx.FileExists(path, forceOriginal));

            if (!exists)
            {
                throw new FileNotFoundException($"Could not find offset for {path}");
            }

            XivTex xivTex;

            try
            {
                var data = await tx.ReadFile(path, forceOriginal);
                xivTex = XivTex.FromUncompressedTex(data);
            }
            catch (Exception ex)
            {
                throw new Exception($"There was an error reading the file: " + path);
            }

            xivTex.FilePath = path;

            return xivTex;
        }

        #endregion

        #region Weird One-Off Resolution Functions
        // Weird brute-force texture path resolvers for UI stuff.

        /// <summary>
        /// Gets the Icon info for a specific gear item
        /// </summary>
        /// <param name="gearItem">The gear item</param>
        /// <returns>A list of TexTypePath containing Icon Info</returns>
        public static async Task<List<string>> GetItemIcons(IItemModel iconItem, ModTransaction tx = null)
        {
            if (tx == null)
            {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginReadonlyTransaction();
            }
            if (iconItem.IconId <= 0)
            {
                return new List<string>();
            }

            var iconString = iconItem.IconId.ToString();

            var ttpList = new List<string>();


            var iconBaseNum = iconString.Substring(0, 2).PadRight(iconString.Length, '0');
            var iconFolder = $"ui/icon/{iconBaseNum.PadLeft(6, '0')}";
            var iconHQFolder = $"{iconFolder}/hq";
            var iconFile = $"{iconString.PadLeft(6, '0')}.tex";

            var path = iconFolder + "/" + iconFile;
            if (await tx.FileExists(path))
            {
                ttpList.Add($"{iconFolder}/{iconFile}");
            }


            path = iconHQFolder + "/" + iconFile;
            if (await tx.FileExists(path))
            {
                ttpList.Add($"{iconHQFolder}/{iconFile}");
            }

            return ttpList;
        }
        public static async Task<List<string>> GetItemIcons(uint iconId, ModTransaction tx = null)
        {
            if (tx == null)
            {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginReadonlyTransaction();
            }
            if (iconId <= 0)
            {
                return new List<string>();
            }

            var baseNum = (iconId / 1000) * 1000;
            var iconString = baseNum.ToString();

            var ttpList = new List<string>();

            var iconFolder = $"ui/icon/{baseNum.ToString("D6")}";
            var iconHQFolder = $"{iconFolder}/hq";
            var iconFile = $"{iconId.ToString("D6")}.tex";

            var path = iconFolder + "/" + iconFile;
            if (await tx.FileExists(path))
            {
                ttpList.Add($"{iconFolder}/{iconFile}");
            }


            path = iconHQFolder + "/" + iconFile;
            if (await tx.FileExists(path))
            {
                ttpList.Add($"{iconHQFolder}/{iconFile}");
            }

            return ttpList;
        }


        /// <summary>
        /// Retrieves the avaiable textures for a given UI Map, via brute force check of estimated file names.
        /// 
        /// Not Transaction Safe
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static async Task<List<string>> GetMapAvailableTex(string path, ModTransaction tx)
        {
            var mapNamePathDictonary = new List<string>();

            var folderPath = $"ui/map/{path}";

            var files = await Index.GetAllHashedFilesInFolder(HashGenerator.GetHash(folderPath), XivDataFile._06_Ui, tx);

            foreach (var mapType in MapTypeDictionary)
            {
                var file = $"{path.Replace("/", "")}{mapType.Key}.tex";

                if (files.Contains(HashGenerator.GetHash(file)))
                {
                    mapNamePathDictonary.Add(folderPath + "/" + file);
                }
            }

            return mapNamePathDictonary;
        }


        public static async Task ResizeXivTx(XivTex tex, int width, int height, bool nearestNeighbor = false)
        {
            var data = await TextureHelpers.ResizeImage(tex, width, height, nearestNeighbor);

            tex.Height = height;
            tex.Width = width;
            await MergePixelData(tex, data);
        }

        #endregion

        #region High-level File Exporting
        /// <summary>
        /// Saves a texture file located at the given internal path as a DDS at the given external path.
        /// </summary>
        /// <param name="internalPath"></param>
        /// <param name="externalPath"></param>
        /// <param name="forceoriginal"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        public static async Task SaveTexAsDDS(string internalPath, string externalPath, bool forceoriginal = false, ModTransaction tx = null)
        {
            var tex = await GetXivTex(internalPath, forceoriginal, tx);
            SaveTexAsDDS(externalPath, tex);
        }

        public static void SaveTexAsDDS(IItem item, XivTex xivTex, DirectoryInfo saveDirectory, XivRace race = XivRace.All_Races)
        {
            var path = IOUtil.MakeItemSavePath(item, saveDirectory, race);
            Directory.CreateDirectory(path);
            var savePath = Path.Combine(path, Path.GetFileNameWithoutExtension(xivTex.FilePath) + ".dds");
            SaveTexAsDDS(savePath, xivTex);
        }

        public static void SaveTexAsDDS(string path, XivTex xivTex)
        {
            DDS.MakeDDS(xivTex, path);
        }
        #endregion

        #region Image Import Pipeline

        /// <summary>
        /// Covers the entire pipeline of importing an external image file into the game files.
        /// This converts the file (if needed) to DDS (stored in a temp folder).
        /// Then converts that DDS file to an uncompreesed .TEX file.
        /// Then SQPacks compresses that .TEX file into a type 2 or 4 SQPack file depending on internal file path.
        /// Then injects that SQPacked file into the given mod transaction or base game files.
        /// </summary>
        /// <param name="internalPath"></param>
        /// <param name="externalPath"></param>
        /// <param name="item"></param>
        /// <param name="source"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        public static async Task<long> ImportTex(string internalPath, string externalPath, IItem item, string source, ModTransaction tx = null)
        {
            var path = internalPath;

            var data = await MakeCompressedTex(path, externalPath, XivTexFormat.INVALID, tx);
            var offset = await Dat.WriteModFile(data, path, source, item, tx);
            return offset;
        }

        /// <summary>
        /// Creates texture data ready to be imported into the DATs from an external file.
        /// If format is not specified, either the incoming file's DDS format is used (DDS files), 
        /// or the existing internal file's DDS format is used.
        /// </summary>
        /// <param name="internalPath"></param>
        /// <param name="externalPath"></param>
        /// <param name="texFormat"></param>
        /// <returns></returns>
        public static async Task<byte[]> MakeCompressedTex(string internalPath, string externalPath, XivTexFormat texFormat = XivTexFormat.INVALID, ModTransaction tx = null)
        {
            // Ensure file exists.
            if (!File.Exists(externalPath))
            {
                throw new IOException($"Could not find file: {externalPath}");
            }

            string ddsFilePath = null;
            await Task.Run(async () =>
            {
                ddsFilePath = await ConvertToDDS(externalPath, internalPath, texFormat, tx);
            });

            try
            {
                var data = DDSToUncompressedTex(ddsFilePath);
                data = await CompressTexFile(data, internalPath);
                return data;
            }
            finally
            {
                // Cleanup the dds file if it was a temp file we created.
                if (ddsFilePath != externalPath)
                {
                    IOUtil.DeleteTempFile(ddsFilePath);
                }
            }
        }

        /// <summary>
        /// Converts a given external image file to a DDS file, returning the resulting DDS File's filepath.
        /// Requires an internal path in order to determine if mipmaps should be generated, and resolve default texture format.
        /// </summary>
        /// <param name="externalPath"></param>
        /// <param name="internalPath"></param>
        /// <returns></returns>
        public static async Task<string> ConvertToDDS(string externalPath, string internalPath, XivTexFormat texFormat = XivTexFormat.INVALID, ModTransaction tx = null)
        {
            var df= IOUtil.GetDataFileFromPath(internalPath);
            bool useMips = df != XivDataFile._06_Ui;


            // First of all, check if the file is a DDS file.
            using(var f = File.OpenRead(externalPath))
            {
                using(var br =  new BinaryReader(f))
                {
                    const uint _DDSMagic = 0x20534444;
                    if(br.ReadUInt32() == _DDSMagic)
                    {
                        // If it is, we don't need to do anything.
                        return externalPath;
                    }
                }
            }

            // If no format was specified...
            if (texFormat == XivTexFormat.INVALID)
            {
                // Use the current internal format.
                var xivt = await Tex.GetXivTex(internalPath, false, tx);
                texFormat = xivt.TextureFormat;
            }

            // Ensure we're converting to a format we can actually process.
            CompressionFormat compressionFormat = GetCompressionFormat(texFormat);

            if (compressionFormat == CompressionFormat.BC7)
            {
                return await DDS.TexConv(externalPath, "BC7_UNORM", useMips);
            }

            // We have to check the image size here to be sure it won't nuke TexImpNet.
            // Extremely small (<64x64) image sizes will cause it to memory error and nuke the entire application.
            using (var img = Image.Load(externalPath))
            {
                if(img.Width < 64 || img.Height < 64)
                {
                    var w = 0;
                    var h = 0;
                    if(img.Width < img.Height)
                    {
                        w = 64;
                        var mul = 64.0f / img.Width;
                        h = (int) Math.Floor(img.Height * mul);
                    }
                    else
                    {
                        h = 64;
                        var mul = 64.0f / img.Height;
                        w = (int)Math.Floor(img.Width * mul);
                    }
                    var rOptions = new ResizeOptions()
                    {
                        Size = new Size(w, h),
                        PremultiplyAlpha = false,
                        Mode = SixLabors.ImageSharp.Processing.ResizeMode.Stretch,
                        Sampler = KnownResamplers.NearestNeighbor,
                    };

                    img.Mutate(x => x.Resize(rOptions));

                    var encoder = new TgaEncoder() { BitsPerPixel = TgaBitsPerPixel.Pixel32, Compression = TgaCompression.None };

                    var path = IOUtil.GetFrameworkTempFile() + ".tga";
                    img.Save(path, encoder);
                    externalPath = path;
                }
            }

            var ddsContainer = new DDSContainer();
            try
            {
                using (var surface = Surface.LoadFromFile(externalPath))
                {
                    if (surface == null)
                        throw new FormatException($"Unsupported texture format or unable to load file: "  + externalPath);

                    surface.FlipVertically();

                    var maxMipCount = 1;
                    if (useMips)
                    {
                        // For things that have real roots (things that have actual models/aren't UI textures), we always want mipMaps, even if the existing texture only has one.
                        // (Ex. The Default Mat-Add textures)
                        maxMipCount = -1;
                    }

                    using (var compressor = new Compressor())
                    {
                        // UI/Paintings only have a single mipmap and will crash if more are generated, for everything else generate max levels
                        compressor.Input.SetMipmapGeneration(true, maxMipCount);
                        compressor.Input.SetData(surface);
                        compressor.Compression.Format = compressionFormat;
                        compressor.Compression.SetBGRAPixelFormat();
                        compressor.Process(out ddsContainer);
                    }
                }

                // Write the new DDS file to disk.
                var tempFile = Path.Combine(IOUtil.GetFrameworkTempFolder(), Guid.NewGuid().ToString() + ".dds");
                ddsContainer.Write(tempFile, DDSFlags.None);
                return tempFile;
            }
            finally
            {
                // Has to be a try/finally instead of using block due to useage as an out/ref value.
                ddsContainer.Dispose();
            }
        }
        public static async Task MergePixelData(XivTex tex, byte[] data)
        {
            // Always retain mip settings.
            bool useMips = tex.MipMapCount > 1 ? true : false;

            CompressionFormat compressionFormat = GetCompressionFormat(tex.TextureFormat);

            if(compressionFormat == CompressionFormat.BGRA)
            {
                tex.TextureFormat = XivTexFormat.A8R8G8B8;
            }

            byte[] ddsData = null;
            if (compressionFormat == CompressionFormat.BC7)
            {
                ddsData = await DDS.TexConvRawPixels(data, tex.Width, tex.Height, "BC7_UNORM", useMips);
            }
            else
            {
                if(tex.Width < 64 || tex.Height < 64)
                {
                    // The TexImpNet compressor will hard crash the entire application with a memory error with small sizes.
                    throw new InvalidDataException("Image is too small for DDS Compressor. (64x64 Minimum Size)");
                }

                // TexImpNet Route
                unsafe
                {
                    fixed (byte* p = data)
                    {
                        var ptr = (IntPtr)p;
                        using (var surface = Surface.LoadFromRawData(ptr, tex.Width, tex.Height, tex.Width * 4, false, false))
                        {
                            if (surface == null)
                                throw new FormatException($"Unsupported texture format");

                            var maxMipCount = 1;
                            if (useMips)
                            {
                                // For things that have real roots (things that have actual models/aren't UI textures), we always want mipMaps, even if the existing texture only has one.
                                // (Ex. The Default Mat-Add textures)
                                maxMipCount = -1;
                            }

                            using (var compressor = new Compressor())
                            {
                                using (var ms = new MemoryStream())
                                {
                                    // UI/Paintings only have a single mipmap and will crash if more are generated, for everything else generate max levels
                                    compressor.Input.SetMipmapGeneration(true, maxMipCount);
                                    compressor.Input.SetData(surface);
                                    compressor.Compression.Format = compressionFormat;
                                    compressor.Compression.SetBGRAPixelFormat();
                                    compressor.Output.OutputHeader = false;
                                    compressor.Process(ms);
                                    ddsData = ms.ToArray();
                                }
                            }
                        }
                    }
                }
            }
            tex.TexData = ddsData;

            if (useMips)
            {
                var calc = GetMipCount(tex.Width, tex.Height);
                tex.MipMapCount = calc;
            }
        }

        public static int GetMipCount(int width, int height)
        {
            return GetMipCount(width > height ? width : height);
        }
        public static int GetMipCount(int largestSize)
        {
            return (int) Math.Floor(Math.Log(largestSize, 2) + 1);
        }


        public static CompressionFormat GetCompressionFormat(XivTexFormat format)
        {
            // Ensure we're converting to a format we can actually process.
            CompressionFormat compressionFormat;
            switch (format)
            {
                case XivTexFormat.DXT1:
                    compressionFormat = CompressionFormat.BC1a;
                    break;
                case XivTexFormat.DXT5:
                    compressionFormat = CompressionFormat.BC3;
                    break;
                case XivTexFormat.BC4:
                    compressionFormat = CompressionFormat.BC4;
                    break;
                case XivTexFormat.BC5:
                    compressionFormat = CompressionFormat.BC5;
                    break;
                case XivTexFormat.BC7:
                    compressionFormat = CompressionFormat.BC7;
                    break;
                case XivTexFormat.A8R8G8B8:
                    compressionFormat = CompressionFormat.BGRA;
                    break;
                default:
                    throw new InvalidDataException("Format is currently unsupported: " + format.ToString());
            }

            return compressionFormat;
        }

        /// <summary>
        /// Returns the raw bytes of a DDS file.
        /// </summary>
        /// <param name="rgbaData">8.8.8.8 Pixel format data.</param>
        /// <returns></returns>
        public static async Task<byte[]> ConvertToDDS(byte[] rgbaData, XivTexFormat texFormat, bool useMipMaps, int width, int height,  bool allowFast8888 = true)
        {

            // Ensure we're converting to a format we can actually process.
            CompressionFormat compressionFormat = GetCompressionFormat(texFormat);

            var maxMipCount = 1;
            if (useMipMaps)
            {
                maxMipCount = 13;
            }

            if (compressionFormat == CompressionFormat.BC7)
            {
                return await DDS.TexConvRawPixels(rgbaData, width, height, "BC7_UNORM", useMipMaps, false);
            }
            else
            {
                await TextureHelpers.SwizzleRB(rgbaData, width, height);
                if (allowFast8888 && texFormat == XivTexFormat.A8R8G8B8)
                {
                    return CreateFast8888DDS(rgbaData, width, height);
                }

                var sizePerPixel = 4;
                var mipData = new MipData(width, height, width * sizePerPixel);
                Marshal.Copy(rgbaData, 0, mipData.Data, rgbaData.Length);

                using (var compressor = new Compressor())
                {
                    // UI/Paintings only have a single mipmap and will crash if more are generated, for everything else generate max levels
                    compressor.Input.SetMipmapGeneration(true, maxMipCount);
                    compressor.Input.SetData(mipData, true);
                    compressor.Compression.Format = compressionFormat;
                    //compressor.Compression.Quality = CompressionQuality.Fastest;

                    compressor.Output.OutputHeader = true;
                    byte[] ddsData = null;



                    // Normal, well-behaved DDS conversion.
                    await Task.Run(() =>
                    {
                        using (var ms = new MemoryStream())
                        {
                            if (!compressor.Process(ms))
                            {
                                throw new ImageProcessingException("Compressor was unable to convert image to DDS format.");
                            }
                            ddsData = ms.ToArray();
                        }
                    });
                    return ddsData;
                }

            }
        }

        /// <summary>
        /// Creates a valid DDS file from a 8.8.8.8 byte array...
        /// By manually creating a DDS header and stapling it onto the end.
        /// This is significantly faster than using the TexImpNet implementation for 8.8.8.8 writing.
        /// The quality of the MipMaps it generates is quite bad though.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="height"></param>
        /// <param name="width"></param>
        /// <returns></returns>
        private static byte[] CreateFast8888DDS(byte[] data, int width, int height)
        {
            var header = new byte[128];
            var pixelSizeBits = 32;

            var minDim = Math.Min(height, width);
            
            // Minimum size mipmap we care about is 32x32, to simplify things.
            var mipCount = (int) Math.Log(minDim, 2);
            mipCount = Math.Max(mipCount, 1);

            Encoding.ASCII.GetBytes("DDS ").CopyTo(header, 0);
            BitConverter.GetBytes(124).CopyTo(header, 4);

            // Flags?
            BitConverter.GetBytes(0x21007).CopyTo(header, 8);

            // Size
            BitConverter.GetBytes(height).CopyTo(header, 12);
            BitConverter.GetBytes(width).CopyTo(header, 16);

            // Pitch
            BitConverter.GetBytes(width * 4).CopyTo(header, 20);

            // Depth
            BitConverter.GetBytes(0).CopyTo(header, 24);

            // MipMap Count
            BitConverter.GetBytes(mipCount).CopyTo(header, 28);

            // dwCaps. DDSCAPS_MIPMAP(0x40000) + DDSCAPS_TEXTURE(0x1000)
            BitConverter.GetBytes(mipCount > 1 ? 0x401000 : 0x1000).CopyTo(header, 104);

            var startOfPixStruct = 76;
            // Pixel struct size
            BitConverter.GetBytes(32).CopyTo(header, startOfPixStruct);

            // Pixel Flags.  In this case, uncompressed(0x40) + contains alpha(0x01).
            BitConverter.GetBytes(0x41).CopyTo(header, startOfPixStruct + 4);

            // DWFourCC, unused
            BitConverter.GetBytes(0).CopyTo(header, startOfPixStruct + 8);

            // Pixel size
            BitConverter.GetBytes(pixelSizeBits).CopyTo(header, startOfPixStruct + 12);

            // Red Mask
            BitConverter.GetBytes(0x00ff0000).CopyTo(header, startOfPixStruct + 16);

            // Green Mask
            BitConverter.GetBytes(0x0000ff00).CopyTo(header, startOfPixStruct + 20);

            // Blue Mask
            BitConverter.GetBytes(0x000000ff).CopyTo(header, startOfPixStruct + 24);

            // Alpha Mask
            BitConverter.GetBytes(0xff000000).CopyTo(header, startOfPixStruct + 28);

            var pixelSize = pixelSizeBits / 8;

            try
            {
                var lastMipData = data;
                var currentMipSize = data.Length;
                var totalMipSize = data.Length;
                var curw = width;
                var curh = height;

                var mipData = new List<byte[]>(mipCount);
                mipData.Add(data);
                for (int i = 1; i < mipCount; i++)
                {
                    // Each MipMap is 1/4 the net size of the last.
                    currentMipSize /= 4;
                    curw /= 2;
                    curh /= 2;

                    var mipArray = new byte[currentMipSize];

                    // We are about to compute the world's singularly worst MipMaps in existence.
                    // But it's going to be fast.
                    for (int y = 0; y < curh; y++) { 
                        for (int x = 0; x < curw; x++)
                        {
                            var destOffset = ((y * curw) + x) * pixelSize;
                            var sourceOffset = (((y*2) * (curw*2)) + (x*2)) * pixelSize;

                            // Copy one of the pixels into the mip data.
                            Array.Copy(lastMipData, sourceOffset, mipArray, destOffset, pixelSize);
                        }
                    }
                    mipData.Add(mipArray);
                    lastMipData = mipArray;
                    totalMipSize += mipArray.Length;
                }

                // Allocate final array and copy the data in.
                var ret = new byte[header.Length + totalMipSize];
                header.CopyTo(ret, 0);
                var offset = header.Length;
                for (int i = 0; i < mipCount; i++)
                {
                    mipData[i].CopyTo(ret, offset);
                    offset += mipData[i].Length;
                }

                // And just like that, we have the world's worst Mip-Enabled DDS file.
                return ret;
            } catch(Exception ex)
            {
                throw;
            }
        }


        /// <summary>
        /// Converts the given DDS format image file/data to an uncompressed .TEX file.
        /// </summary>
        /// <param name="externalDdsPath"></param>
        /// <returns></returns>
        public static byte[] DDSToUncompressedTex(string externalDdsPath)
        {
            var ddsSize = (uint)(new FileInfo(externalDdsPath).Length);
            byte[] uncompTex;

            // Stream the file in to replace the header...
            using (var fs = File.OpenRead(externalDdsPath))
            {
                using (var fileBr = new BinaryReader(fs))
                {
                    // Could use a file stream here instead..?
                    // Less RAM, but slower..?
                    using (var uncompTexMs = new MemoryStream())
                    {
                        using (var uncompTexWriter = new BinaryWriter(uncompTexMs))
                        {
                            DDSToUncompressedTex(fileBr, uncompTexWriter, ddsSize);
                        }

                        //uncompTexMs.Position = 0;
                        uncompTex = uncompTexMs.ToArray();
                    }
                }
            }
            return uncompTex;
        }

        public static byte[] DDSToUncompressedTex(byte[] data)
        {
            var uncompressedLength = data.Length;
            using (var ms = new MemoryStream(data))
            {
                using (var br = new BinaryReader(ms))
                {
                    using (var msOut = new MemoryStream())
                    {
                        using (var bw = new BinaryWriter(msOut))
                        {
                            DDSToUncompressedTex(br, bw, (uint)uncompressedLength);
                            return msOut.ToArray();
                        }
                    }
                }
            }
        }

        public static void DDSToUncompressedTex(BinaryReader br, BinaryWriter bw, uint ddsSize)
        {
            var uncompressedLength = ddsSize;
            var header = DDSHeaderToTexHeader(br);
            uncompressedLength -= header.DDSHeaderSize;
            bw.Write(header.TexHeader);
            bw.Write(br.ReadBytes((int)uncompressedLength));
        }

        /// <summary>
        /// Retrieves the texture format information from a DDS file stream/DDS header.
        /// Expects the stream position or offset to point to the start of the DDS Header.
        /// Advances the stream position somewhat arbitrarily.
        /// </summary>
        /// <param name="ddsStream"></param>
        /// <returns></returns>
        public static XivTexFormat GetDDSTexFormat(BinaryReader ddsStream, long offset = -1)
        {

            if(offset >= 0)
            {
                ddsStream.BaseStream.Seek(offset, SeekOrigin.Begin);
            }
            else
            {
                offset = ddsStream.BaseStream.Position;
            }


            ddsStream.BaseStream.Seek(offset + 12, SeekOrigin.Begin);

            var newHeight = ddsStream.ReadInt32();
            var newWidth = ddsStream.ReadInt32();
            ddsStream.ReadBytes(8);
            var newMipCount = ddsStream.ReadInt32();

            ddsStream.BaseStream.Seek(offset + DDS._DDS_PixelFormatOffset, SeekOrigin.Begin);

            var pixelFormatSize = ddsStream.ReadInt32();
            var flags = ddsStream.ReadUInt32();
            var fourCC = ddsStream.ReadUInt32();
            XivTexFormat textureType;

            if ((flags & DDS._DWFourCCFlag) != 0)
            {
                if (fourCC == DDS._DX10)
                {
                    ddsStream.BaseStream.Seek(offset + 128, SeekOrigin.Begin);
                    var dxgiTexType = ddsStream.ReadUInt32();

                    if (DDS.DxgiTypeToXivTex.ContainsKey(dxgiTexType))
                    {
                        textureType = DDS.DxgiTypeToXivTex[dxgiTexType];
                    }
                    else
                    {
                        throw new Exception($"DXGI format ({dxgiTexType}) not recognized.");
                    }
                }
                else if (DDS.DdsTypeToXivTex.ContainsKey(fourCC))
                {
                    textureType = DDS.DdsTypeToXivTex[fourCC];
                }
                else
                {
                    throw new Exception($"DDS Type ({fourCC}) not recognized.");
                }
            } else
            {
                // Uncompressed.
                textureType = XivTexFormat.A8R8G8B8;
            }


            switch (flags)
            {
                case 2 when textureType == XivTexFormat.A8R8G8B8:
                    textureType = XivTexFormat.A8;
                    break;
                case 65 when textureType == XivTexFormat.A8R8G8B8:
                    var bpp = ddsStream.ReadInt32();
                    if (bpp == 32)
                    {
                        textureType = XivTexFormat.A8R8G8B8;
                    }
                    else
                    {
                        var red = ddsStream.ReadInt32();

                        switch (red)
                        {
                            case 31744:
                                textureType = XivTexFormat.A1R5G5B5;
                                break;
                            case 3840:
                                textureType = XivTexFormat.A4R4G4B4;
                                break;
                        }
                    }

                    break;
            }
            return textureType;
        }

        /// <summary>
        /// Creates an uncompressed .TEX file header from the given data.
        /// TODO: This really should probably be a member function off TexHeader, and thus use the full
        /// set of available header data, instead of nuking the Attributes and MipFlag.
        /// </summary>
        /// <param name="newWidth">The width of the DDS texture to be imported.</param>
        /// <param name="newHeight">The height of the DDS texture to be imported.</param>
        /// <param name="newMipCount">The number of mipmaps the DDS texture to be imported contains.</param>
        /// <returns>The created header data.</returns>
        internal static List<byte> CreateTexFileHeader(XivTexFormat format, int newWidth, int newHeight, int newMipCount)
        {
            if (newMipCount > 13)
            {
                throw new InvalidDataException("Image has too many MipMaps. (Max 13)");
            }
            var headerData = new List<byte>();

            headerData.AddRange(BitConverter.GetBytes((short)0));
            headerData.AddRange(BitConverter.GetBytes((short)128));
            headerData.AddRange(BitConverter.GetBytes(short.Parse(format.GetTexFormatCode())));
            headerData.AddRange(BitConverter.GetBytes((short)0));
            headerData.AddRange(BitConverter.GetBytes((short)newWidth));
            headerData.AddRange(BitConverter.GetBytes((short)newHeight));
            headerData.AddRange(BitConverter.GetBytes((short)1));
            headerData.AddRange(BitConverter.GetBytes((short)newMipCount));

            var mipSizes = DDS.CalculateMipMapSizes(format, newWidth, newHeight);

            if (mipSizes.Count < newMipCount)
                throw new InvalidDataException($"CreateTexFileHeader: newMipCount ({newMipCount}) is too high for texture ({newWidth}x{newHeight}, format={format})");

            headerData.AddRange(BitConverter.GetBytes(0)); // LoD 0 Mip
            headerData.AddRange(BitConverter.GetBytes(newMipCount > 1 ? 1 : 0)); // LoD 1 Mip
            headerData.AddRange(BitConverter.GetBytes(newMipCount > 2 ? 2 : 0)); // LoD 2 Mip

            var mipMapUncompressedOffset = 80;

            for (var i = 0; i < newMipCount; i++)
            {
                headerData.AddRange(BitConverter.GetBytes(mipMapUncompressedOffset));
                mipMapUncompressedOffset = mipMapUncompressedOffset + mipSizes[i];
            }

            var padding = 80 - headerData.Count;

            headerData.AddRange(new byte[padding]);

            return headerData;
        }

        /// <summary>
        /// Replaces the DDS File header with a TexFile header.
        /// Reads the incoming Binary Reader stream to the end of the DDS Header.
        /// </summary>
        /// <param name="br">Open binary reader positioned to the start of the binary DDS data (including header).</param>
        /// <param name="br">Total size of the DDS Data (including header)</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static (byte[] TexHeader, uint DDSHeaderSize) DDSHeaderToTexHeader(BinaryReader br, long offset = -1)
        {
            if(offset >= 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            } else
            {
                offset = br.BaseStream.Position;
            }

            // DDS Header Reference: https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-header
            var texFormat = GetDDSTexFormat(br, offset);

            br.BaseStream.Seek(offset + 4, SeekOrigin.Begin);
            var flags = br.ReadInt32();

            br.BaseStream.Seek(offset + 12, SeekOrigin.Begin);

            var newHeight = br.ReadInt32();
            var newWidth = br.ReadInt32();
            br.ReadBytes(8);
            var newMipCount = br.ReadInt32();

            if ((!IOUtil.IsPowerOfTwo(newHeight) || !IOUtil.IsPowerOfTwo(newWidth)) && newMipCount > 1)
            {
                throw new Exception("Resolution must be a multiple of 2.  (Ex. 256, 512, 1024, ...)");
            }

            if (offset >= 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            }

            br.BaseStream.Seek(offset + DDS._DDS_PixelFormatOffset, SeekOrigin.Begin);
            var pixfmtSize = br.ReadUInt32();
            var pixfmtFlags = br.ReadUInt32();
            var dwFourCc= br.ReadUInt32();

            var headerLength = _DDSHeaderSize;

            if((pixfmtFlags & DDS._DWFourCCFlag) != 0)
            {
                if(dwFourCc == DDS._DX10)
                {
                    headerLength += 20;
                }
            }

            br.BaseStream.Seek(headerLength, SeekOrigin.Begin);

            // Write header.
            var texHeader = CreateTexFileHeader(texFormat, newWidth, newHeight, newMipCount).ToArray();
            return (texHeader, headerLength);
        }

        /// <summary>
        /// Retrieves the raw pixel data from a given external file path.
        /// </summary>
        /// <param name="externalFile"></param>
        /// <returns></returns>
        public static async Task<(byte[] PixelData, int Width, int Height)> GetPixelDataFromFile(string externalFile, int resizeWidth = -1, int resizeHeight = -1)
        {
            byte[] pixelData;
            int width, height;
            var resizeOptions = new ResizeOptions
            {
                Size = new Size(resizeWidth, resizeHeight),
                PremultiplyAlpha = false,
                Mode = ResizeMode.Stretch,
            };

            if (externalFile.ToLower().EndsWith(".tex"))
            {
                var data = File.ReadAllBytes(externalFile);
                var tex = XivTex.FromUncompressedTex(data);
                var pix = await tex.GetRawPixels();

                return (pix, tex.Width, tex.Height);
            }
            else if (externalFile.ToLower().EndsWith(".dds"))
            {

                // We could have functions somewhere to just raw read the DDS tex data, but this is a 
                // relatively minor perf hit since it just flips a header around.
                var otherTex = XivTex.FromUncompressedTex(Tex.DDSToUncompressedTex(externalFile));
                width = otherTex.Width;
                height = otherTex.Height;
                pixelData = await otherTex.GetRawPixels();

                if(resizeWidth > 0 || resizeHeight > 0)
                {
                    // Resize if needed.
                    using (var image = SixLabors.ImageSharp.Image.LoadPixelData<Rgba32>(pixelData, otherTex.Width, otherTex.Height))
                    {
                        image.Mutate(x => x.Resize(resizeOptions));
                        pixelData = IOUtil.GetImageSharpPixels(image);

                        width = image.Width;
                        height = image.Height;
                    }
                }

            }
            else
            {
                using (var image = SixLabors.ImageSharp.Image.Load<Rgba32>(externalFile))
                {
                    if(resizeWidth > 0 || resizeHeight > 0)
                    {
                        image.Mutate(x => x.Resize(resizeOptions));
                    }

                    pixelData = IOUtil.GetImageSharpPixels(image);

                    width = image.Width;
                    height = image.Height;
                }
            }

            return (pixelData, width, height);
        }

        /// <summary>
        /// Compress a given uncompressed TEX file into a type 2 or type 4 file ready to be added to the DATs.
        /// Takes a file extension (or internal path with file extension) in order to determine
        /// if the file should be compressed into Type4(.tex) or Type2(.atex)
        /// </summary>
        /// <param name="br"></param>
        /// <param name="extentionOrInternalPath"></param>
        /// <returns></returns>
        public static async Task<byte[]> CompressTexFile(byte[] data, string extentionOrInternalPath = ".tex")
        {
            using (var ms = new MemoryStream(data)) {
                using (var br = new BinaryReader(ms))
                {
                    return await CompressTexFile(br, (uint) data.Length, extentionOrInternalPath);
                }
            }
        }

        /// <summary>
        /// Compress a given uncompressed TEX file into a type 2 or type 4 file ready to be added to the DATs.
        /// Takes a file extension (or internal path with file extension) in order to determine
        /// if the file should be compressed into Type4(.tex) or Type2(.atex)
        /// </summary>
        /// <param name="br"></param>
        /// <param name="extentionOrInternalPath"></param>
        /// <returns></returns>
        public static async Task<byte[]> CompressTexFile(BinaryReader br, uint lengthIncludingHeader, string extentionOrInternalPath = ".tex", long offset = -1)
        {
            if(offset >= 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            } else
            {
                offset = br.BaseStream.Position;
            }
            var header = TexHeader.ReadTexHeader(br);

            List<byte> newTex = new List<byte>();
            // Here we need to read the texture header.
            if (!extentionOrInternalPath.Contains(".atex"))
            {
                var ddsParts = await DDS.CompressDDSBody(br, (XivTexFormat) header.TextureFormat, header.Width, header.Height, header.MipCount);
                var uncompLength = lengthIncludingHeader - _TexHeaderSize;


                br.BaseStream.Seek(offset + _TexHeaderSize, SeekOrigin.Begin);

                // Type 4 Header
                newTex.AddRange(Dat.MakeType4DatHeader((XivTexFormat)header.TextureFormat, ddsParts, (int)uncompLength, header.Width, header.Height));

                // Texture file header.
                // CompressDDSBody call above alters individual mipmap sizes, ending up changing the offsets.
                // Calculate those again here.
                header.MipMapOffsets[0] = _TexHeaderSize;
                for (var i = 1; i < header.MipCount; i++)
                    header.MipMapOffsets[i] = header.MipMapOffsets[i - 1] + (uint)ddsParts[i - 1].Count;
                header.LoDMips[0] = Math.Min(header.MipCount - 1u, header.LoDMips[0]);
                header.LoDMips[1] = Math.Min(header.MipCount - 1u, Math.Max(header.LoDMips[0], header.LoDMips[1]));
                header.LoDMips[2] = Math.Min(header.MipCount - 1u, Math.Max(header.LoDMips[1], header.LoDMips[2]));
                newTex.AddRange(header.ToBytes());

                // Compressed pixel data.
                foreach (var mip in ddsParts)
                {
                    foreach (var part in mip)
                    {
                        newTex.AddRange(part);
                    }
                }

                var ret = newTex.ToArray();

                return ret;
            }
            else
            {
                // ATex are just compressed as a Type 2(Binary) file.
                var data = await Dat.CompressType2Data(br.ReadAllBytes());
                return data;
            }
        }

        #endregion

        #region Colorset Import Handling
        // Special one-off functions for importing colorsets as image files.

        public static (List<Half> ColorsetData, byte[] DyeData) GetColorsetDataFromDDS(string ddsFilePath)
        {
            var colorSetData = DDSToColorset(ddsFilePath);
            var dyeData = GetColorsetDyeInformationFromFile(ddsFilePath);

            return new (colorSetData, dyeData);
        }


        /// <summary>
        /// Imports a colorset DDS file into an xivMTRL.  Does not save the result.
        /// </summary>
        /// <param name="xivMtrl"></param>
        /// <param name="ddsFilePath"></param>
        /// <param name="item"></param>
        /// <param name="source"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        public static void ImportColorsetTexture(XivMtrl xivMtrl, string ddsFilePath, bool saveColorset = true, bool saveDye = true)
        { 
            if(!saveDye && !saveColorset)
            {
                return;
            }

            var cset = GetColorsetDataFromDDS(ddsFilePath);

            // Replace the color set data with the imported data
            if (saveColorset)
            {
                xivMtrl.ColorSetData = cset.ColorsetData;
            }
            if (saveDye)
            {
                xivMtrl.ColorSetDyeData = cset.DyeData == null ? new byte[0] : cset.DyeData;
            }

        }


        /// <summary>
        /// Gets the raw Colorset data list from a dds file.
        /// </summary>
        /// <param name="ddsFileDirectory"></param>
        /// <returns></returns>
        public static List<Half> DDSToColorset(string ddsPath)
        {
            using (var br = new BinaryReader(File.OpenRead(ddsPath)))
            {
                // Check DDS type
                br.BaseStream.Seek(DDS._DDS_PixelFormatOffset + 8, SeekOrigin.Begin);

                var texType = br.ReadUInt32();
                XivTexFormat textureType;

                if (DDS.DdsTypeToXivTex.ContainsKey(texType))
                {
                    textureType = DDS.DdsTypeToXivTex[texType];
                }
                else
                {
                    throw new Exception($"DDS Type ({texType}) not recognized. Expecting A16B16G16R16F.");
                }

                if (textureType != XivTexFormat.A16B16G16R16F)
                {
                    throw new Exception($"Incorrect file type. Expected: A16B16G16R16F  Given: {textureType}");
                }

                br.BaseStream.Seek(12, SeekOrigin.Begin);

                var height = br.ReadInt32();
                var width = br.ReadInt32();

                if(width == 4 && height == 16)
                {
                    // Endwalker Colorset, this is fine.
                } else if(width == 8 && height == 32)
                {
                    // Dawntrail Colorset, this is fine.
                } else
                {
                    throw new InvalidDataException("Colorset Images must be either 4x16 or 8x32");
                }

                var size = width * height * 4;
                var colorSetData = new List<Half>(size);


                br.BaseStream.Seek(128, SeekOrigin.Begin);

                for (var i = 0; i < size; i++)
                {
                    colorSetData.Add((new Half(br.ReadUInt16())));
                }

                return colorSetData;
            }
        }

        /// <summary>
        /// Retreives the associated .dat file for colorset dye data, if it exists.
        /// Takes either a .dat file directly or the .dds file adjacent to it.
        /// </summary>
        /// <param name="externalFilePath"></param>
        /// <returns></returns>
        public static byte[] GetColorsetDyeInformationFromFile(string externalFilePath)
        {
            var flagsPath = externalFilePath;
            if (externalFilePath.EndsWith(".dds"))
            {
                flagsPath = Path.Combine(Path.GetDirectoryName(externalFilePath), (Path.GetFileNameWithoutExtension(externalFilePath) + ".dat"));
            } else if (!externalFilePath.EndsWith(".dat"))
            {
                throw new FileNotFoundException("File for Dye data extraction must be either .dat or .dds");
            }

            byte[] colorSetExtraData;
            if (File.Exists(flagsPath))
            {
                // Dye data length varies with EW/DT.
                colorSetExtraData = File.ReadAllBytes(flagsPath);
            }
            else
            {
                // If we have no dye data, return NULL
                colorSetExtraData = null;
            }
            return colorSetExtraData;
        }


        /// <summary>
        /// Ensures the given texture meets normal size requirements, with an optional max size.
        /// Returns true if the texture was altered.
        /// </summary>
        /// <param name="tex"></param>
        /// <param name="maxSize"></param>
        /// <returns></returns>
        public static async Task<bool> EnsureValidSize(XivTex tex, int maxSize = -1)
        {
            var usesMips = false;
            if (!string.IsNullOrWhiteSpace(tex.FilePath))
            {
                var df = IOUtil.GetDataFileFromPath(tex.FilePath);
                if(df == XivDataFile._06_Ui)
                {
                    usesMips = false;
                } else
                {
                    usesMips = true;
                }
            }
            else
            {
                usesMips = tex.MipMapCount > 1;
            }

            var newWidth = tex.Width;
            var newHeight = tex.Height;
            if (usesMips)
            {
                if(!IOUtil.IsPowerOfTwo(tex.Width))
                {
                    newWidth = IOUtil.RoundToPowerOfTwo(tex.Width);
                }

                if (!IOUtil.IsPowerOfTwo(tex.Height))
                {
                    newHeight = IOUtil.RoundToPowerOfTwo(tex.Height);
                }
            }

            if (maxSize > 0)
            {
                while (newWidth > maxSize || newHeight > maxSize)
                {
                    newWidth /= 2;
                    newHeight /= 2;
                }
            }

            var regenMips = false;
            if(usesMips && tex.MipMapCount == 1)
            {
                regenMips = true;
                tex.MipMapCount = GetMipCount(tex.Width, tex.Height);
            } else if(!usesMips && tex.MipMapCount > 1)
            {
                regenMips = true;
                tex.MipMapCount = 1;
            }

            if(newWidth != tex.Width || newHeight != tex.Height || regenMips)
            {
                await ResizeXivTx(tex, newWidth, newHeight, false);
                return true;
            }
            return false;
        }
#endregion

        #region Static Dictionaries


        /// <summary>
        /// A dictionary containing slot data in the format [Slot Name, Slot abbreviation]
        /// (Used in UI Texture usage resolution)
        /// </summary>
        private static readonly Dictionary<string, string> MapTypeDictionary = new Dictionary<string, string>
        {
            {"_m", "HighRes Diffuse"},
            {"_s", "LowRes Diffuse"},
            {"d", "PoI"},
            {"m_m", "HighRes Mask"},
            {"m_s", "LowRes Mask"}

        };
        /// <summary>
        /// Dictionary that holds [Texture Code, Texture Format] data
        /// </summary>
        public static readonly Dictionary<int, XivTexFormat> TextureTypeDictionary = new Dictionary<int, XivTexFormat>
        {
            {4400, XivTexFormat.L8 },
            {4401, XivTexFormat.A8 },
            {5184, XivTexFormat.A4R4G4B4 },
            {5185, XivTexFormat.A1R5G5B5 },
            {5200, XivTexFormat.A8R8G8B8 },
            {5201, XivTexFormat.X8R8G8B8 },
            {8528, XivTexFormat.R32F},
            {8784, XivTexFormat.G16R16F },
            {8800, XivTexFormat.G32R32F },
            {9312, XivTexFormat.A16B16G16R16F },
            {9328, XivTexFormat.A32B32G32R32F },
            {13344, XivTexFormat.DXT1 },
            {13360, XivTexFormat.DXT3 },
            {13361, XivTexFormat.DXT5 },
            {16704, XivTexFormat.D16 },
            {24864, XivTexFormat.BC4 },			
            {25136, XivTexFormat.BC5 },
            {25650, XivTexFormat.BC7 }
        };

        #endregion

    }
}