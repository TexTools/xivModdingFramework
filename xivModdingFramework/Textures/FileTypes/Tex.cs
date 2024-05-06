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
using Lumina.Extensions;
using Lumina.Models.Models;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.SharpDX.Core.Helper;

namespace xivModdingFramework.Textures.FileTypes
{
    /// <summary>
    /// This class contains the methods that deal with the .tex file type 
    /// </summary>
    public class Tex
    {
        private const string TexExtension = ".tex";
        private readonly DirectoryInfo _gameDirectory;
        private readonly Index _index;
        private readonly Dat _dat;
        private readonly XivDataFile _dataFile;

        // The 'magic number' for PNG file headers, indicating that the following data is a PNG file.
        private readonly byte[] _PNG_MAGIC = { 137, 80, 78, 71, 13, 10, 26, 10 };

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

        public Tex(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
            _index = new Index(_gameDirectory);
            _dat = new Dat(_gameDirectory);
        }

        public Tex(DirectoryInfo gameDirectory, XivDataFile dataFile)
        {
            _gameDirectory = gameDirectory;
            _index = new Index(_gameDirectory);
            _dat = new Dat(_gameDirectory);
            _dataFile = dataFile;
        }

        public async Task<XivTex> GetTexData(MtrlTexture tex, ModTransaction tx = null)
        {
            return await GetTexData(tex.TexturePath, tex.Usage, tx);
        }

        public async Task<XivTex> GetTexData(TexTypePath ttp, ModTransaction tx = null)
        {
            return await GetTexData(ttp.Path, ttp.Type, tx);
        }
        public async Task<XivTex> GetTexData(string path, XivTexType usage, ModTransaction tx = null)
        {
            var dataFile = IOUtil.GetDataFileFromPath(path);
            var ttp = new TexTypePath()
            {
                DataFile = dataFile,
                Path = path,
                Type = usage
            };
            var xivTex = await GetTexData(ttp.Path, tx);
            xivTex.TextureTypeAndPath = ttp;
            return xivTex;
        }
        public async Task<XivTex> GetTexData(string path, ModTransaction tx = null)
        {
            var folder = Path.GetDirectoryName(path);
            folder = folder.Replace("\\", "/");
            var file = Path.GetFileName(path);

            long offset = 0;

            var hashedfolder = 0;
            var hashedfile = 0;

            hashedfolder = HashGenerator.GetHash(folder);
            hashedfile = HashGenerator.GetHash(file);
            var df = IOUtil.GetDataFileFromPath(path);

            if (tx != null)
            {
                offset = (await tx.GetIndexFile(df)).Get8xDataOffset(path);
            }
            else
            {
                offset = await _index.GetDataOffset(path);
            }

            if (offset == 0)
            {
                throw new Exception($"Could not find offset for {path}");
            }

            XivTex xivTex;

            try
            {
                if (path.Contains(".atex"))
                {
                    var atex = new ATex(_gameDirectory, df);
                    xivTex = await atex.GetATexData(offset);
                }
                else
                {
                    xivTex = await _dat.GetType4Data(offset, df);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"There was an error reading texture data at offset {offset}");
            }

            var ttp = new TexTypePath();
            ttp.DataFile = df;
            ttp.Name = Path.GetFileName(path);
            ttp.Type = XivTexType.Other;
            ttp.Path = path;
            xivTex.TextureTypeAndPath = ttp;

            return xivTex;
        }

        /// <summary>
        /// Gets the Icon info for a specific gear item
        /// </summary>
        /// <param name="gearItem">The gear item</param>
        /// <returns>A list of TexTypePath containing Icon Info</returns>
        public async Task<List<TexTypePath>> GetItemIcons(IItemModel iconItem)
        {
            var type = iconItem.GetType();
            uint iconNumber = 0;
            if (type == typeof(XivGear))
            {
                iconNumber = ((XivGear)iconItem).IconNumber;
            }
            else if (type == typeof(XivFurniture))
            {
                iconNumber = ((XivFurniture)iconItem).IconNumber;
            }

            if (iconNumber <= 0)
            {
                return new List<TexTypePath>();
            }

            var iconString = iconNumber.ToString();

            var ttpList = new List<TexTypePath>();


            var iconBaseNum = iconString.Substring(0, 2).PadRight(iconString.Length, '0');
            var iconFolder = $"ui/icon/{iconBaseNum.PadLeft(6, '0')}";
            var iconHQFolder = $"{iconFolder}/hq";
            var iconFile = $"{iconString.PadLeft(6, '0')}.tex";

            var path = iconFolder + "/" + iconFile;
            if (await _index.FileExists(path, XivDataFile._06_Ui))
            {
                ttpList.Add(new TexTypePath
                {
                    Name = "Icon",
                    Path = $"{iconFolder}/{iconFile}",
                    Type = XivTexType.Icon,
                    DataFile = XivDataFile._06_Ui
                });
            }


            path = iconHQFolder + "/" + iconFile;
            if (await _index.FileExists(path, XivDataFile._06_Ui))
            {
                ttpList.Add(new TexTypePath
                {
                    Name = "HQ Icon",
                    Path = $"{iconHQFolder}/{iconFile}",
                    Type = XivTexType.Icon,
                    DataFile = XivDataFile._06_Ui
                });
            }

            return ttpList;
        }


        public async Task<XivTex> GetTexDataByOffset(TexTypePath ttp, long offset)
        {
            if (offset == 0)
            {
                throw new Exception($"Could not find offset for {ttp.Path}");
            }

            XivTex xivTex;
            try
            {
                if (ttp.Path.Contains(".atex"))
                {
                    var atex = new ATex(_gameDirectory, ttp.DataFile);
                    xivTex = await atex.GetATexData(offset);
                }
                else
                {
                    xivTex = await _dat.GetType4Data(offset, ttp.DataFile);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"There was an error reading texture data at offset {offset}");
            }

            xivTex.TextureTypeAndPath = ttp;

            return xivTex;
        }

        /// <summary>
        /// Gets the list of available mtrl parts for a given item
        /// </summary>
        /// <param name="itemModel">An item that contains model data</param>
        /// <param name="xivRace">The race for the requested data</param>
        /// <returns>A list of part characters</returns>
        public async Task<List<string>> GetTexturePartList(IItemModel itemModel, XivRace xivRace, XivDataFile dataFile)
        {
            var itemType = ItemType.GetPrimaryItemType(itemModel);

            var version = "0001";

            var id = itemModel.ModelInfo.PrimaryID.ToString().PadLeft(4, '0');
            var bodyVer = itemModel.ModelInfo.SecondaryID.ToString().PadLeft(4, '0');
            var itemCategory = itemModel.SecondaryCategory;

            if (itemType != XivItemType.human && itemType != XivItemType.furniture)
            {
                // Get the mtrl version for the given item from the imc file
                var imc = new Imc(_gameDirectory);
                version = (await imc.GetImcInfo(itemModel)).MaterialSet.ToString().PadLeft(4, '0');
            }

            var parts = Helpers.Constants.Alphabet;
            var race = xivRace.GetRaceCode();

            string mtrlFolder = "", mtrlFile = "";

            switch (itemType)
            {
                case XivItemType.equipment:
                    mtrlFolder = $"chara/{itemType}/e{id}/material/v{version}";
                    mtrlFile = $"mt_c{race}e{id}_{itemModel.GetItemSlotAbbreviation()}_";
                    break;
                case XivItemType.accessory:
                    mtrlFolder = $"chara/{itemType}/a{id}/material/v{version}";
                    mtrlFile = $"mt_c{race}a{id}_{SlotAbbreviationDictionary[itemCategory]}_";
                    break;
                case XivItemType.weapon:
                    mtrlFolder = $"chara/{itemType}/w{id}/obj/body/b{bodyVer}/material/v{version}";
                    mtrlFile = $"mt_w{id}b{bodyVer}_";
                    break;
                case XivItemType.monster:
                    mtrlFolder = $"chara/{itemType}/m{id}/obj/body/b{bodyVer}/material/v{version}";
                    mtrlFile = $"mt_m{id}b{bodyVer}_";
                    break;
                case XivItemType.demihuman:
                    mtrlFolder = $"chara/{itemType}/d{id}/obj/body/e{bodyVer}/material/v{version}";
                    mtrlFile = $"mt_d{id}e{bodyVer}_";
                    break;
                case XivItemType.human:
                    if (itemCategory.Equals(XivStrings.Body))
                    {
                        mtrlFolder = $"chara/{itemType}/c{id}/obj/body/b{bodyVer}/material/v{version}";
                        mtrlFile = $"mt_c{id}b{bodyVer}_";
                    }
                    else if (itemCategory.Equals(XivStrings.Hair))
                    {
                        mtrlFolder = $"chara/{itemType}/c{id}/obj/body/h{bodyVer}/material/v{version}";
                        mtrlFile = $"mt_c{id}h{bodyVer}_{SlotAbbreviationDictionary[itemCategory]}_";
                    }
                    else if (itemCategory.Equals(XivStrings.Face))
                    {
                        mtrlFolder = $"chara/{itemType}/c{id}/obj/body/f{bodyVer}/material/v{version}";
                        mtrlFile = $"mt_c{id}f{bodyVer}_{SlotAbbreviationDictionary[itemCategory]}_";
                    }
                    else if (itemCategory.Equals(XivStrings.Tail))
                    {
                        mtrlFolder = $"chara/{itemType}/c{id}/obj/body/t{bodyVer}/material/v{version}";
                        mtrlFile = $"mt_c{id}t{bodyVer}_";
                    }
                    break;
                case XivItemType.furniture:
                    if (itemCategory.Equals(XivStrings.Furniture_Indoor))
                    {
                        mtrlFolder = $"bgcommon/hou/indoor/general/{id}/material";
                        mtrlFile = $"fun_b0_m{id}_0";
                    }
                    else if (itemCategory.Equals(XivStrings.Furniture_Outdoor))
                    {
                        mtrlFolder = $"bgcommon/hou/outdoor/general/{id}/material";
                        mtrlFile = $"gar_b0_m{id}_0";
                    }

                    break;
                default:
                    mtrlFolder = "";
                    break;
            }

            // Get a list of hashed mtrl files that are in the given folder
            var files = await _index.GetAllHashedFilesInFolder(HashGenerator.GetHash(mtrlFolder), dataFile);

            // append the part char to the mtrl file and see if its hashed value is within the files list
            var partList =
                (from part in parts
                 let mtrlCheck = mtrlFile + part + ".mtrl"
                 where files.Contains(HashGenerator.GetHash(mtrlCheck))
                 select part.ToString()).ToList();

            if (partList.Count < 1 && itemType == XivItemType.furniture)
            {
                if (itemCategory.Equals(XivStrings.Furniture_Indoor))
                {
                    mtrlFile = $"fun_b0_m{id}_1";
                }
                else if (itemCategory.Equals(XivStrings.Furniture_Outdoor))
                {
                    mtrlFile = $"gar_b0_m{id}_1";
                }

                // Get a list of hashed mtrl files that are in the given folder
                files = await _index.GetAllHashedFilesInFolder(HashGenerator.GetHash(mtrlFolder), dataFile);

                // append the part char to the mtrl file and see if its hashed value is within the files list
                partList =
                    (from part in parts
                     let mtrlCheck = mtrlFile + part + ".mtrl"
                     where files.Contains(HashGenerator.GetHash(mtrlCheck))
                     select part.ToString()).ToList();
            }

            // returns the list of parts that exist within the mtrl folder
            return partList;
        }

        public async Task<Dictionary<string, string>> GetMapAvailableTex(string path)
        {
            var mapNamePathDictonary = new Dictionary<string, string>();

            var folderPath = $"ui/map/{path}";

            var files = await _index.GetAllHashedFilesInFolder(HashGenerator.GetHash(folderPath), XivDataFile._06_Ui);

            foreach (var mapType in MapTypeDictionary)
            {
                var file = $"{path.Replace("/", "")}{mapType.Key}.tex";

                if (files.Contains(HashGenerator.GetHash(file)))
                {
                    mapNamePathDictonary.Add(mapType.Value, folderPath + "/" + file);
                }
            }

            return mapNamePathDictonary;
        }

        public void SaveTexAsDDS(IItem item, XivTex xivTex, DirectoryInfo saveDirectory, XivRace race = XivRace.All_Races)
        {
            var path = IOUtil.MakeItemSavePath(item, saveDirectory, race);
            Directory.CreateDirectory(path);
            var savePath = Path.Combine(path, Path.GetFileNameWithoutExtension(xivTex.TextureTypeAndPath.Path) + ".dds");
            SaveTexAsDDS(savePath, xivTex);
        }

        public void SaveTexAsDDS(string path, XivTex xivTex)
        {
            DDS.MakeDDS(xivTex, path);
        }


        /// <summary>
        /// Gets the raw pixel data for the texture
        /// </summary>
        /// <param name="xivTex">The texture data</param>
        /// <returns>A byte array with the image data</returns>
        public Task<byte[]> GetImageData(XivTex xivTex, int layer = -1)
        {
            return Task.Run(async () =>
            {
                byte[] imageData = null;

                var layers = xivTex.Layers;
                if(layers == 0)
                {
                    layers = 1;
                }

                switch (xivTex.TextureFormat)
                {
                    case XivTexFormat.DXT1:
                        imageData = DxtUtil.DecompressDxt1(xivTex.TexData, xivTex.Width, xivTex.Height * layers);
                        break;
                    case XivTexFormat.DXT3:
                        imageData = DxtUtil.DecompressDxt3(xivTex.TexData, xivTex.Width, xivTex.Height * layers);
                        break;
                    case XivTexFormat.DXT5:
                        imageData = DxtUtil.DecompressDxt5(xivTex.TexData, xivTex.Width, xivTex.Height * layers);
                        break;
                    case XivTexFormat.BC5:
                        imageData = DxtUtil.DecompressBc5(xivTex.TexData, xivTex.Width, xivTex.Height * layers);
                        break;
                    case XivTexFormat.BC7:
                        imageData = DxtUtil.DecompressBc7(xivTex.TexData, xivTex.Width, xivTex.Height * layers);
                        break;
                    case XivTexFormat.A4R4G4B4:
                        imageData = await Read4444Image(xivTex.TexData, xivTex.Width, xivTex.Height * layers);
                        break;
                    case XivTexFormat.A1R5G5B5:
                        imageData = await Read5551Image(xivTex.TexData, xivTex.Width, xivTex.Height * layers);
                        break;
                    case XivTexFormat.A8R8G8B8:
                        imageData = await SwapRBColors(xivTex.TexData, xivTex.Width, xivTex.Height * layers);
                        break;
                    case XivTexFormat.L8:
                    case XivTexFormat.A8:
                        imageData = await Read8bitImage(xivTex.TexData, xivTex.Width, xivTex.Height * layers);
                        break;
                    case XivTexFormat.X8R8G8B8:
                    case XivTexFormat.R32F:
                    case XivTexFormat.G16R16F:
                    case XivTexFormat.G32R32F:
                    case XivTexFormat.A16B16G16R16F:
                    case XivTexFormat.A32B32G32R32F:
                    case XivTexFormat.D16:
                    default:
                        imageData = xivTex.TexData;
                        break;
                }

                if(layer >= 0)
                {
                    var bytesPerLayer = imageData.Length / xivTex.Layers;
                    var offset = bytesPerLayer * layer;

                    byte[] nData = new byte[bytesPerLayer];
                    Array.Copy(imageData, offset, nData, 0, bytesPerLayer);

                    imageData = nData;
                }

                return imageData;
            });
        }

        /// <summary>
        /// Creates bitmap from decompressed A1R5G5B5 texture data.
        /// </summary>
        /// <param name="textureData">The decompressed texture data.</param>
        /// <param name="width">The textures width.</param>
        /// <param name="height">The textures height.</param>
        /// <returns>The raw byte data in 32bit</returns>
        private static async Task<byte[]> Read5551Image(byte[] textureData, int width, int height)
        {
            var convertedBytes = new List<byte>();

            await Task.Run(() =>
            {
                using (var ms = new MemoryStream(textureData))
                {
                    using (var br = new BinaryReader(ms))
                    {
                        for (var y = 0; y < height; y++)
                        {
                            for (var x = 0; x < width; x++)
                            {
                                var pixel = br.ReadUInt16() & 0xFFFF;

                                var red = ((pixel & 0x7E00) >> 10) * 8;
                                var green = ((pixel & 0x3E0) >> 5) * 8;
                                var blue = ((pixel & 0x1F)) * 8;
                                var alpha = ((pixel & 0x8000) >> 15) * 255;

                                convertedBytes.Add((byte) red);
                                convertedBytes.Add((byte) green);
                                convertedBytes.Add((byte) blue);
                                convertedBytes.Add((byte) alpha);
                            }
                        }
                    }
                }
            });

            return convertedBytes.ToArray();
        }


        /// <summary>
        /// Creates bitmap from decompressed A4R4G4B4 texture data.
        /// </summary>
        /// <param name="textureData">The decompressed texture data.</param>
        /// <param name="width">The textures width.</param>
        /// <param name="height">The textures height.</param>
        /// <returns>The raw byte data in 32bit</returns>
        private static async Task<byte[]> Read4444Image(byte[] textureData, int width, int height)
        {
            var convertedBytes = new List<byte>();

            await Task.Run(() =>
            {
                using (var ms = new MemoryStream(textureData))
                {
                    using (var br = new BinaryReader(ms))
                    {
                        for (var y = 0; y < height; y++)
                        {
                            for (var x = 0; x < width; x++)
                            {
                                var pixel = br.ReadUInt16() & 0xFFFF;
                                var red = ((pixel & 0xF)) * 16;
                                var green = ((pixel & 0xF0) >> 4) * 16;
                                var blue = ((pixel & 0xF00) >> 8) * 16;
                                var alpha = ((pixel & 0xF000) >> 12) * 16;

                                convertedBytes.Add((byte) blue);
                                convertedBytes.Add((byte) green);
                                convertedBytes.Add((byte) red);
                                convertedBytes.Add((byte) alpha);
                            }
                        }
                    }
                }
            });

            return convertedBytes.ToArray();
        }

        /// <summary>
        /// Creates bitmap from decompressed A8/L8 texture data.
        /// </summary>
        /// <param name="textureData">The decompressed texture data.</param>
        /// <param name="width">The textures width.</param>
        /// <param name="height">The textures height.</param>
        /// <returns>The created bitmap.</returns>
        private static async Task<byte[]> Read8bitImage(byte[] textureData, int width, int height)
        {
            var convertedBytes = new List<byte>();

            await Task.Run(() =>
            {
                using (var ms = new MemoryStream(textureData))
                {
                    using (var br = new BinaryReader(ms))
                    {
                        for (var y = 0; y < height; y++)
                        {
                            for (var x = 0; x < width; x++)
                            {
                                var pixel = br.ReadByte() & 0xFF;

                                convertedBytes.Add((byte) pixel);
                                convertedBytes.Add((byte) pixel);
                                convertedBytes.Add((byte) pixel);
                                convertedBytes.Add(255);
                            }
                        }
                    }
                }
            });

            return convertedBytes.ToArray();
        }

        /// <summary>
        /// Creates bitmap from decompressed Linear texture data.
        /// </summary>
        /// <param name="textureData">The decompressed texture data.</param>
        /// <param name="width">The textures width.</param>
        /// <param name="height">The textures height.</param>
        /// <returns>The raw byte data in 32bit</returns>
        private static async Task<byte[]> SwapRBColors(byte[] textureData, int width, int height)
        {
            var convertedBytes = new List<byte>();

            await Task.Run(() =>
            {
                using (var ms = new MemoryStream(textureData))
                {
                    using (var br = new BinaryReader(ms))
                    {
                        for (var y = 0; y < height; y++)
                        {
                            for (var x = 0; x < width; x++)
                            {
                                var blue = br.ReadByte();
                                var green = br.ReadByte();
                                var red = br.ReadByte();
                                var alpha = br.ReadByte();

                                convertedBytes.Add(red);
                                convertedBytes.Add(green);
                                convertedBytes.Add(blue);
                                convertedBytes.Add(alpha);
                            }
                        }
                    }
                }
            });

            return convertedBytes.ToArray();
        }


        /// <summary>
        /// Retrieves the texture format information from a DDS file stream.
        /// </summary>
        /// <param name="ddsStream"></param>
        /// <returns></returns>
        public XivTexFormat GetDDSTexFormat(BinaryReader ddsStream)
        {
            // DX10 format magic number
            const uint fourccDX10 = 0x30315844;

            ddsStream.BaseStream.Seek(12, SeekOrigin.Begin);

            var newHeight = ddsStream.ReadInt32();
            var newWidth = ddsStream.ReadInt32();
            ddsStream.ReadBytes(8);
            var newMipCount = ddsStream.ReadInt32();

            if (newHeight % 2 != 0 || newWidth % 2 != 0)
            {
                throw new Exception("Resolution must be a multiple of 2");
            }

            ddsStream.BaseStream.Seek(80, SeekOrigin.Begin);

            var textureFlags = ddsStream.ReadInt32();
            var texType = ddsStream.ReadInt32();

            XivTexFormat textureType;

            if (texType == fourccDX10)
            {
                ddsStream.BaseStream.Seek(128, SeekOrigin.Begin);
                var dxgiTexType = ddsStream.ReadInt32();

                if (DXGIType.ContainsKey(dxgiTexType))
                {
                    textureType = DXGIType[dxgiTexType];
                }
                else
                {
                    throw new Exception($"DXGI format ({dxgiTexType}) not recognized.");
                }
            }
            else if (DDSType.ContainsKey(texType))
            {
                textureType = DDSType[texType];
            }
            else
            {
                throw new Exception($"DDS Type ({texType}) not recognized.");
            }

            switch (textureFlags)
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
        /// Applies an overlay texture onto a base texture.
        /// Overlays must be either DDS or PNG file streams.
        /// Stores the output as a PNG file in the temporary files directory and returns the path.
        /// 
        /// This does NOT modify the game dats or indexes.
        /// </summary>
        /// <param name="baseData"></param>
        /// <param name="overlayData"></param>
        /// <returns></returns>
        public async Task<string> CreateMergedOverlayFile(XivTex baseTex, Stream overlayStream)
        {

            // Get both images in pixel format.
            var basePixelData = await GetImageData(baseTex);
            var baseImage = Image.LoadPixelData<SixLabors.ImageSharp.PixelFormats.Rgba32>(basePixelData, baseTex.Width, baseTex.Height);

            Image overlayImage;


            // Check if it's a PNG or DDS stream.
            var isPng = true;
            var position = overlayStream.Position;
            var buff = new byte[8];
            overlayStream.Read(buff, 0, 8);

            for(int i = 0; i < _PNG_MAGIC.Length; i++)
            {
                if(_PNG_MAGIC[i] != buff[i])
                {
                    isPng = false;
                }
            }

            // Rewind stream.
            overlayStream.Seek(position, SeekOrigin.Begin);

            if(isPng)
            {
                // If it's a PNG stream, imagesharp should be able to just directly load it.
                overlayImage = Image.Load(overlayStream);

            } else
            {
                // Read DDS data, then pump the raw pixels into ImageSharp.
                var overlayPixelData = await DDStoPixel(overlayStream);
                overlayImage = Image.LoadPixelData<SixLabors.ImageSharp.PixelFormats.Rgba32>(overlayPixelData.Item1, overlayPixelData.Item2, overlayPixelData.Item3);
            }

            if(baseImage.Width != overlayImage.Width || baseImage.Height != overlayImage.Height)
            {
                // Resize the overlay to match.
                overlayImage.Mutate(x => x.Resize(baseImage.Width, baseImage.Height));
            }

            // Merge Images
            baseImage.Mutate(x =>
            {
                x.DrawImage(overlayImage, 1.0f);
            });

            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".png");
            baseImage.SaveAsPng(tempFile);


            return tempFile;
        }

        /// <summary>
        /// Applies an overlay texture onto a base texture.
        /// Overlays must be either DDS or PNG file streams.
        /// 
        /// Writes the resulting file to the Dats/Indexes.
        /// </summary>
        /// <param name="baseTex"></param>
        /// <param name="overlayStream"></param>
        /// <returns></returns>
        public async Task ApplyOverlay(XivTex baseTex, Stream overlayStream, string source, ModTransaction tx = null)
        {
            var pngPath = await CreateMergedOverlayFile(baseTex, overlayStream);

            var root = await XivCache.GetFirstRoot(baseTex.TextureTypeAndPath.Path);
            var item = root.GetFirstItem();

            await ImportTex(baseTex.TextureTypeAndPath.Path, pngPath, item, source, tx);

            try
            {
                // Clear out temp files.
                File.Delete(pngPath);
            }
            catch
            {

            }
        }

        /// <summary>
        /// Takes a raw DDS Data block and decodes it into pixel data.
        /// Only works with single layer DDS files.
        /// 
        /// Return Pixel Data, Width, Height, Original DDS format.
        /// </summary>
        /// <param name="ddsData"></param>
        /// <returns></returns>
        public async Task<Tuple<byte[], int, int, XivTexFormat>> DDStoPixel(byte [] rawDdsData)
        {
            using (var mStream = new MemoryStream(rawDdsData))
            {
                return await DDStoPixel(mStream);
            }
        }

        /// <summary>
        /// Takes a raw DDS Data block and decodes it into pixel data.
        /// Only works with single layer DDS files.
        /// 
        /// Return Pixel Data, Width, Height, Original DDS format.
        /// </summary>
        /// <param name="ddsData"></param>
        /// <returns></returns>
        public async Task<Tuple<byte[], int, int, XivTexFormat>> DDStoPixel(Stream ddsStream)
        {
            using (var reader = new BinaryReader(ddsStream))
            {
                var format = GetDDSTexFormat(reader);
                var ddsContainer = new DDSContainer();

                reader.BaseStream.Seek(12, SeekOrigin.Begin);

                var height = reader.ReadInt32();
                var width = reader.ReadInt32();

                byte[] imageData = null;
                var layers = 1;


                reader.BaseStream.Seek(0, SeekOrigin.Begin);
                var rawData = IOUtil.ReadAllBytes(reader);

                switch (format)
                {
                    case XivTexFormat.DXT1:
                        imageData = DxtUtil.DecompressDxt1(rawData, width, height * layers);
                        break;
                    case XivTexFormat.DXT3:
                        imageData = DxtUtil.DecompressDxt3(rawData, width, height * layers);
                        break;
                    case XivTexFormat.DXT5:
                        imageData = DxtUtil.DecompressDxt5(rawData, width, height * layers);
                        break;
                    case XivTexFormat.BC5:
                        imageData = DxtUtil.DecompressBc5(rawData, width, height * layers);
                        break;
                    case XivTexFormat.BC7:
                        imageData = DxtUtil.DecompressBc7(rawData, width, height * layers);
                        break;
                    case XivTexFormat.A4R4G4B4:
                        imageData = await Read4444Image(rawData, width, height * layers);
                        break;
                    case XivTexFormat.A1R5G5B5:
                        imageData = await Read5551Image(rawData, width, height * layers);
                        break;
                    case XivTexFormat.A8R8G8B8:
                        imageData = await SwapRBColors(rawData, width, height * layers);
                        break;
                    case XivTexFormat.L8:
                    case XivTexFormat.A8:
                        imageData = await Read8bitImage(rawData, width, height * layers);
                        break;
                    case XivTexFormat.X8R8G8B8:
                    case XivTexFormat.R32F:
                    case XivTexFormat.G16R16F:
                    case XivTexFormat.G32R32F:
                    case XivTexFormat.A16B16G16R16F:
                    case XivTexFormat.A32B32G32R32F:
                    case XivTexFormat.D16:
                    default:
                        imageData = rawData;
                        break;
                }

                return new Tuple<byte[], int, int, XivTexFormat>(imageData, width, height, format);
            }
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
        public async Task<byte[]> MakeTexData(string internalPath, string externalPath, XivTexFormat texFormat = XivTexFormat.INVALID, ModTransaction tx = null)
        {
            // Ensure file exists.
            if (!File.Exists(externalPath))
            {
                throw new IOException($"Could not find file: {externalPath}");
            }
            var ddsFilePath = await ConvertToDDS(externalPath, internalPath, texFormat, tx);
            var data = await DDSToDatReady(ddsFilePath, internalPath);
            return data;
        }

        /// <summary>
        /// Converts a given external image file to a DDS file, returning the resulting DDS File's filepath.
        /// Requires an internal path in order to determine if mipmaps should be generated, and resolve default texture format.
        /// </summary>
        /// <param name="externalPath"></param>
        /// <param name="internalPath"></param>
        /// <returns></returns>
        public async Task<string> ConvertToDDS(string externalPath, string internalPath, XivTexFormat texFormat = XivTexFormat.INVALID, ModTransaction tx = null)
        {
            var root = await XivCache.GetFirstRoot(internalPath);
            bool useMips = root != null;


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

            var ddsContainer = new DDSContainer();
            try
            {
                // If no format was specified...
                if (texFormat == XivTexFormat.INVALID)
                {
                    // Use the current internal format.
                    var xivt = await _dat.GetType4Data(internalPath, false, tx);
                    texFormat = xivt.TextureFormat;
                }

                // Ensure we're converting to a format we can actually process.
                CompressionFormat compressionFormat = CompressionFormat.BGRA;
                switch (texFormat)
                {
                    case XivTexFormat.DXT1:
                        compressionFormat = CompressionFormat.BC1a;
                        break;
                    case XivTexFormat.DXT5:
                        compressionFormat = CompressionFormat.BC3;
                        break;
                    case XivTexFormat.BC5:
                        compressionFormat = CompressionFormat.BC5;
                        break;
                    case XivTexFormat.A8R8G8B8:
                        compressionFormat = CompressionFormat.BGRA;
                        break;
                    default:
                        throw new Exception($"Format {texFormat} is not currently supported for Non-DDS import\n\nPlease use the DDS import option instead.");
                }
                using (var surface = Surface.LoadFromFile(externalPath))
                {
                    if (surface == null)
                        throw new FormatException($"Unsupported texture format");

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
                var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".dds");
                ddsContainer.Write(tempFile, DDSFlags.None);
                return tempFile;
            }
            finally
            {
                // Has to be a try/finally instead of using block due to useage as an out/ref value.
                ddsContainer.Dispose();
            }
        }


        /// <summary>
        /// Convert a raw pixel byte array into a DDS data block.
        /// </summary>
        /// <param name="data">8.8.8.8 Pixel format data.</param>
        /// <param name="allowNonsense">Cursed argument that allows generating fake DDS files for speed.</param>
        /// <returns></returns>
        public async Task<byte[]> ConvertToDDS(byte[] data, XivTexFormat texFormat, bool useMipMaps, int height, int width, bool allowFast8888 = true)
        {
            if(allowFast8888 && texFormat == XivTexFormat.A8R8G8B8)
            {
                return CreateFast8888DDS(data, height, width);
            }

            // Ensure we're converting to a format we can actually process.
            CompressionFormat compressionFormat = CompressionFormat.BGRA;
            switch (texFormat)
            {
                case XivTexFormat.DXT1:
                    compressionFormat = CompressionFormat.BC1a;
                    break;
                case XivTexFormat.DXT5:
                    compressionFormat = CompressionFormat.BC3;
                    break;
                case XivTexFormat.BC5:
                    compressionFormat = CompressionFormat.BC5;
                    break;
                case XivTexFormat.A8R8G8B8:
                    compressionFormat = CompressionFormat.BGRA;
                    break;
                default:
                    throw new Exception($"Format {texFormat} is not currently supported for Non-DDS import\n\nPlease use the DDS import option instead.");
            }

            var maxMipCount = 1;
            if (useMipMaps)
            {
                maxMipCount = 13;
            }

            var sizePerPixel = 4;
            var mipData = new MipData(width, height, width * sizePerPixel);
            Marshal.Copy(data, 0, mipData.Data, data.Length);

            using (var compressor = new Compressor())
            {
                // UI/Paintings only have a single mipmap and will crash if more are generated, for everything else generate max levels
                compressor.Input.SetMipmapGeneration(true, maxMipCount);
                compressor.Input.SetData(mipData, true);
                compressor.Compression.Format = compressionFormat;
                compressor.Compression.SetBGRAPixelFormat();

                //compressor.Compression.SetRGBAPixelFormat
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

        const uint _DDSHeaderSize = 128;
        const uint _TexHeaderSize = 80;


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
        private byte[] CreateFast8888DDS(byte[] data, int height, int width)
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
            BitConverter.GetBytes(0).CopyTo(header, 8);

            // Size
            BitConverter.GetBytes(height).CopyTo(header, 12);
            BitConverter.GetBytes(width).CopyTo(header, 16);

            // Pitch
            BitConverter.GetBytes(width * 4).CopyTo(header, 20);

            // Depth
            BitConverter.GetBytes(0).CopyTo(header, 24);

            // MipMap Count
            BitConverter.GetBytes(mipCount).CopyTo(header, 28);

            var startOfPixStruct = 76;
            // Pixel struct size
            BitConverter.GetBytes(32).CopyTo(header, startOfPixStruct);

            // Pixel Flags.  In this case, uncompressed(64) + contains alpha(1).
            BitConverter.GetBytes(65).CopyTo(header, startOfPixStruct + 4);

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

        public async Task<byte[]> DDSToDatReady(string externalDdsPath, string internalPath)
        {
            var ddsSize = (uint) (new FileInfo(externalDdsPath).Length);
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

            using (var ms = new MemoryStream(uncompTex))
            {
                using (var br = new BinaryReader(ms))
                {
                    return await CompressTexFile(br, (uint) uncompTex.Length, internalPath);
                }
            }
        }
        public byte[] DDSToUncompressedTex(string externalDdsPath)
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


        public async Task<byte[]> DDSToUncompressedTex(byte[] data)
        {
            // Set up streams to pass down to the lower level functions.
            return await Task.Run(() =>
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
            });
        }

        public void DDSToUncompressedTex(BinaryReader br, BinaryWriter bw, uint ddsSize)
        {
            var uncompressedLength = ddsSize;
            var header = DDSHeaderToTexHeader(br);
            uncompressedLength -= header.DDSHeaderSize;
            bw.Write(header.TexHeader);
            bw.Write(br.ReadBytes((int)uncompressedLength));
        }

        /// <summary>
        /// Replaces the DDS File header with a TexFile header.
        /// Reads the incoming Binary Reader stream to the end of the DDS Header.
        /// </summary>
        /// <param name="br">Open binary reader positioned to the start of the binary DDS data (including header).</param>
        /// <param name="br">Total size of the DDS Data (including header)</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public (byte[] TexHeader, uint DDSHeaderSize) DDSHeaderToTexHeader(BinaryReader br, long offset = -1)
        {
            // DDS Header Reference: https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-header
            var texFormat = GetDDSTexFormat(br);
            br.BaseStream.Seek(12, SeekOrigin.Begin);

            var newHeight = br.ReadInt32();
            var newWidth = br.ReadInt32();
            br.ReadBytes(8);
            var newMipCount = br.ReadInt32();

            if (newHeight % 2 != 0 || newWidth % 2 != 0)
            {
                throw new Exception("Resolution must be a multiple of 2");
            }
            
            if(offset >= 0)
            {
                br.BaseStream.Seek(offset, SeekOrigin.Begin);
            }

            br.BaseStream.Seek(80, SeekOrigin.Begin);
            var texType = br.ReadInt32();

            var headerLength = _DDSHeaderSize;

            // DX10 DDS Files have a 20 byte header extension.
            const uint fourccDX10 = 0x30315844;
            uint extraHeaderBytes = (uint)(texType == fourccDX10 ? 20 : 0); // sizeof DDS_HEADER_DXT10
            headerLength += extraHeaderBytes;

            br.Seek(headerLength);

            // Write header.
            var texHeader = CreateTexFileHeader(texFormat, newWidth, newHeight, newMipCount).ToArray();
            return (texHeader, headerLength);
        }


        /// <summary>
        /// Compress a given uncompressed TEX file into a type 2 or type 4 file ready to be added to the DATs.
        /// Takes a file extension (or internal path with file extension) in order to determine
        /// if the file should be compressed into Type4(.tex) or Type2(.atex)
        /// </summary>
        /// <param name="br"></param>
        /// <param name="extentionOrInternalPath"></param>
        /// <returns></returns>
        public async Task<byte[]> CompressTexFile(byte[] data, string extentionOrInternalPath = ".tex")
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
        public async Task<byte[]> CompressTexFile(BinaryReader br, uint lengthIncludingHeader, string extentionOrInternalPath = ".tex", long offset = -1)
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


                br.BaseStream.Seek(offset, SeekOrigin.Begin);

                // Type 4 Header
                newTex.AddRange(_dat.MakeType4DatHeader((XivTexFormat)header.TextureFormat, ddsParts, (int)uncompLength, header.Width, header.Height));

                // Texture file header.
                newTex.AddRange(br.ReadBytes((int)_TexHeaderSize));

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
                var data = await _dat.CompressType2Data(br.ReadAllBytes());
                return data;
            }
        }

        public async Task<long> ImportTex(string internalPath, string externalPath, IItem item, string source, ModTransaction tx = null)
        {
            var path = internalPath;

            var data = await MakeTexData(path, externalPath, XivTexFormat.INVALID, tx);

            var offset = await _dat.WriteModFile(data, path, source, item, tx);
            return offset;
        }

        private struct TexHeader
        {
            // Bitflags
            public uint Attributes = 0;

            // Texture Format
            public uint TextureFormat = 0;

            public ushort Width = 0;
            public ushort Height = 0;

            public ushort Depth = 1;

            // This is technically 2 fields smooshed together,
            // Mip Count and ArraySize for image arrays.
            // Can deal with that later though, TexTools already chokes on those files for multiple reasons.
            public ushort MipCount = 1;

            uint[] LoDMips = new uint[3];

            uint[] MipMapOffsets = new uint[13];

            public TexHeader()
            {

            }

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
                header.MipCount = br.ReadUInt16();

                for(int i = 0; i < header.LoDMips.Length; i++)
                {
                    header.LoDMips[i] = br.ReadUInt32();
                }

                for (int i = 0; i < header.MipMapOffsets.Length; i++)
                {
                    header.MipMapOffsets[i] = br.ReadUInt32();
                }
                return header;
            }
        }



        /// <summary>
        /// Imports a ColorSet file
        /// </summary>
        /// <param name="xivMtrl">The XivMtrl data of the original</param>
        /// <param name="ddsFileDirectory">The dds directory of the new ColorSet</param>
        /// <param name="item">The item</param>
        /// <param name="source">The source importing the file</param>
        /// <returns>The new offset</returns>
        public async Task<long> ImportColorsetTexture(XivMtrl xivMtrl, DirectoryInfo ddsFileDirectory, IItem item, string source, XivLanguage lang, ModTransaction tx = null)
        {
            var colorSetData = GetColorsetDataFromDDS(ddsFileDirectory);
            var colorSetExtraData = GetColorsetExtraDataFromDDS(ddsFileDirectory);

            // Replace the color set data with the imported data
            xivMtrl.ColorSetData = colorSetData;
            xivMtrl.ColorSetDyeData = colorSetExtraData;
            if (xivMtrl.AdditionalData.Length > 0)
            {
                // This byte enables the dye set if it's not already enabled.
                xivMtrl.AdditionalData[0] = 12;
            }

            var doSave = false;
            if(tx == null)
            {
                doSave = true;
                // Open a transaction if needed since we're performing multiple operations.
                tx = ModTransaction.BeginTransaction();
            }
            try
            {
                var _mtrl = new Mtrl(XivCache.GameInfo.GameDirectory);
                if (xivMtrl.ColorSetData.Count < 1024)
                {
                    await _mtrl.FixPreDawntrailMaterial(xivMtrl, source, tx);
                }

                var offset = await _mtrl.ImportMtrl(xivMtrl, item, source, false, tx);
                if (doSave)
                {
                    await ModTransaction.CommitTransaction(tx);
                }
                return offset;
            }
            catch
            {
                if (doSave) {
                    ModTransaction.CancelTransaction(tx);
                }
                throw;
            }
        }



        /// <summary>
        /// Gets the raw Colorset data list from a dds file.
        /// </summary>
        /// <param name="ddsFileDirectory"></param>
        /// <returns></returns>
        public static List<Half> GetColorsetDataFromDDS(DirectoryInfo ddsFileDirectory)
        {
            using (var br = new BinaryReader(File.OpenRead(ddsFileDirectory.FullName)))
            {
                // Check DDS type
                br.BaseStream.Seek(84, SeekOrigin.Begin);

                var texType = br.ReadInt32();
                XivTexFormat textureType;

                if (DDSType.ContainsKey(texType))
                {
                    textureType = DDSType[texType];
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

                List<Half> colorSetData;
                int size;
                if (width == 4) {
                    size = 256;
                    colorSetData = new List<Half>(256);
                } else
                {
                    size = 1024;
                    colorSetData = new List<Half>(1024);
                }


                // skip DDS header
                br.BaseStream.Seek(128, SeekOrigin.Begin);

                // color data is always 512 (4w x 16h = 64 x 8bpp = 512)
                // this reads 256 ushort values which is 256 x 2 = 512
                for (var i = 0; i < size; i++)
                {
                    colorSetData.Add((new Half(br.ReadUInt16())));
                }

                return colorSetData;
            }
        }

        /// <summary>
        /// Retreives the associated .dat file for colorset dye data, if it exists.
        /// This takes in the .DDS FILE path, not the .DAT file path.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public static byte[] GetColorsetExtraDataFromDDS(DirectoryInfo file)
        {
            var flagsPath = Path.Combine(Path.GetDirectoryName(file.FullName), (Path.GetFileNameWithoutExtension(file.FullName) + ".dat"));

            byte[] colorSetExtraData;
            if (File.Exists(flagsPath))
            {
                // The extra data after the colorset is always 32 bytes 
                // This reads 16 ushort values which is 16 x 2 = 32
                colorSetExtraData = File.ReadAllBytes(flagsPath);

                // If for whatever reason there is a .dat file but it's missing data
                if (colorSetExtraData.Length != 32)
                {
                    // Set all dye modifiers to 0 (undyeable)
                    colorSetExtraData = new byte[32];
                }
            }
            else
            {
                // If .dat file is missing set all values to 0 (undyeable)
                colorSetExtraData = new byte[32];
            }
            return colorSetExtraData;
        }

        /// <summary>
        /// Converts a DDS file into a mtrl file and returns the raw data
        /// </summary>
        /// <param name="xivMtrl">The XivMtrl data of the original</param>
        /// <param name="ddsFileDirectory">The dds directory of the new ColorSet</param>
        /// <param name="item">The item</param>
        /// <returns>The raw mtrl data</returns>
        public byte[] DDStoMtrlData(XivMtrl xivMtrl, DirectoryInfo ddsFileDirectory)
        {
            var colorSetData = GetColorsetDataFromDDS(ddsFileDirectory);

            var colorSetExtraData = new byte[32];
            // If the colorset size is 544, it contains extra data that must be imported
            try
            {
                colorSetExtraData = GetColorsetExtraDataFromDDS(ddsFileDirectory);
            } catch
            {
                colorSetExtraData = new byte[32];
            }

            // Replace the color set data with the imported data
            xivMtrl.ColorSetData = colorSetData;
            xivMtrl.ColorSetDyeData = colorSetExtraData;

            if (xivMtrl.AdditionalData.Length > 0)
            {
                // This byte enables the dye set if it's not already enabled.
                xivMtrl.AdditionalData[0] = 12;
            }

            var _mtrl = new Mtrl(XivCache.GameInfo.GameDirectory);
            return _mtrl.CreateMtrlFile(xivMtrl);
        }

        /// <summary>
        /// Creates the header for the texture info from the data to be imported.
        /// </summary>
        /// <param name="xivTex">Data for the currently displayed texture.</param>
        /// <param name="newWidth">The width of the DDS texture to be imported.</param>
        /// <param name="newHeight">The height of the DDS texture to be imported.</param>
        /// <param name="newMipCount">The number of mipmaps the DDS texture to be imported contains.</param>
        /// <returns>The created header data.</returns>
        private static List<byte> CreateTexFileHeader(XivTexFormat format, int newWidth, int newHeight, int newMipCount)
        {
            var headerData = new List<byte>();
            
            headerData.AddRange(BitConverter.GetBytes((short)0));
            headerData.AddRange(BitConverter.GetBytes((short)128));
            headerData.AddRange(BitConverter.GetBytes(short.Parse(format.GetTexFormatCode())));
            headerData.AddRange(BitConverter.GetBytes((short)0));
            headerData.AddRange(BitConverter.GetBytes((short)newWidth));
            headerData.AddRange(BitConverter.GetBytes((short)newHeight));
            headerData.AddRange(BitConverter.GetBytes((short)1));
            headerData.AddRange(BitConverter.GetBytes((short)newMipCount));


            headerData.AddRange(BitConverter.GetBytes(0)); // LoD 0 Mip
            headerData.AddRange(BitConverter.GetBytes(newMipCount > 1 ? 1 : 0)); // LoD 1 Mip
            headerData.AddRange(BitConverter.GetBytes(newMipCount > 2 ? 2 : 0)); // LoD 2 Mip

            int mipLength;

            switch (format)
            {
                case XivTexFormat.DXT1:
                    mipLength = (newWidth * newHeight) / 2;
                    break;
                case XivTexFormat.DXT5:
                case XivTexFormat.A8:
                    mipLength = newWidth * newHeight;
                    break;
                case XivTexFormat.A1R5G5B5:
                case XivTexFormat.A4R4G4B4:
                    mipLength = (newWidth * newHeight) * 2;
                    break;
                case XivTexFormat.L8:
                case XivTexFormat.A8R8G8B8:
                case XivTexFormat.X8R8G8B8:
                case XivTexFormat.R32F:
                case XivTexFormat.G16R16F:
                case XivTexFormat.G32R32F:
                case XivTexFormat.A16B16G16R16F:
                case XivTexFormat.A32B32G32R32F:
                case XivTexFormat.DXT3:
                case XivTexFormat.D16:
                default:
                    mipLength = (newWidth * newHeight) * 4;
                    break;
            }

            var mipMapUncompressedOffset = 80;

            for (var i = 0; i < newMipCount; i++)
            {
                headerData.AddRange(BitConverter.GetBytes(mipMapUncompressedOffset));
                mipMapUncompressedOffset = mipMapUncompressedOffset + mipLength;

                if (mipLength > 16)
                {
                    mipLength = mipLength / 4;
                }
                else
                {
                    mipLength = 16;
                }
            }

            var padding = 80 - headerData.Count;

            headerData.AddRange(new byte[padding]);

            return headerData;
        }


        /// <summary>
        /// A dictionary containing the int represntations of known file types for DDS
        /// </summary>
        private static readonly Dictionary<int, XivTexFormat> DDSType = new Dictionary<int, XivTexFormat>
        {
            //DXT1
            {827611204, XivTexFormat.DXT1 },

            //DXT3
            {861165636, XivTexFormat.DXT3 },

            //DXT5
            {894720068, XivTexFormat.DXT5 },

            //ATI2 (BC5)
            {843666497, XivTexFormat.BC5 },

            //ARGB 16F
            {113, XivTexFormat.A16B16G16R16F },

            //Uncompressed RGBA
            {0, XivTexFormat.A8R8G8B8 }

        };

        /// <summary>
        /// A dictionary containing the int represntations of known DXGI formats for DDS
        /// </summary>
        private static readonly Dictionary<int, XivTexFormat> DXGIType = new Dictionary<int, XivTexFormat>
        {
            {(int)DDS.DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM, XivTexFormat.DXT1 },
            {(int)DDS.DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM, XivTexFormat.DXT3 },
            {(int)DDS.DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM, XivTexFormat.DXT5 },
            {(int)DDS.DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM, XivTexFormat.BC5 },
            {(int)DDS.DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM, XivTexFormat.BC7 },
            {(int)DDS.DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT, XivTexFormat.A16B16G16R16F },
            {(int)DDS.DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, XivTexFormat.A8R8G8B8 }
        };

        /// <summary>
        /// A dictionary containing slot data in the format [Slot Name, Slot abbreviation]
        /// </summary>
        private static readonly Dictionary<string, string> SlotAbbreviationDictionary = new Dictionary<string, string>
        {
            {XivStrings.Head, "met"},
            {XivStrings.Hands, "glv"},
            {XivStrings.Legs, "dwn"},
            {XivStrings.Feet, "sho"},
            {XivStrings.Body, "top"},
            {XivStrings.Earring, "ear"},
            {XivStrings.Neck, "nek"},
            {XivStrings.Rings, "rir"},
            {XivStrings.LeftRing, "ril"},
            {XivStrings.Wrists, "wrs"},
            {XivStrings.Head_Body, "top"},
            {XivStrings.Body_Hands, "top"},
            {XivStrings.Body_Hands_Legs, "top"},
            {XivStrings.Body_Legs_Feet, "top"},
            {XivStrings.Body_Hands_Legs_Feet, "top"},
            {XivStrings.Legs_Feet, "top"},
            {XivStrings.All, "top"},
            {XivStrings.Face, "fac"},
            {XivStrings.Iris, "iri"},
            {XivStrings.Etc, "etc"},
            {XivStrings.Accessory, "acc"},
            {XivStrings.Hair, "hir"}

        };

        /// <summary>
        /// A dictionary containing slot data in the format [Slot Name, Slot abbreviation]
        /// </summary>
        private static readonly Dictionary<string, string> MapTypeDictionary = new Dictionary<string, string>
        {
            {"_m", "HighRes Diffuse"},
            {"_s", "LowRes Diffuse"},
            {"d", "PoI"},
            {"m_m", "HighRes Mask"},
            {"m_s", "LowRes Mask"}

        };

        
    }
}