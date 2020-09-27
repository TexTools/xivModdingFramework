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

        public async Task<XivTex> GetTexData(MapInfo map)
        {

            var dataFile = IOUtil.GetDataFileFromPath(map.Path);
            var ttp = new TexTypePath()
            {
                DataFile = dataFile,
                Path = map.Path,
                Type = map.Usage
            };
            return await GetTexData(ttp);
        }

        public async Task<XivTex> GetTexData(TexTypePath ttp)
        {
            var xivTex = await GetTexData(ttp.Path);
            xivTex.TextureTypeAndPath = ttp;
            return xivTex;
        }
        public async Task<XivTex> GetTexData(string path)
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

            offset = await _index.GetDataOffset(hashedfolder, hashedfile, df);

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


        public async Task<XivTex> GetTexDataPreFetchedIndex(TexTypePath ttp)
        {
            var offset = await _index.GetDataOffset(ttp.Path);

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

            var parts = Constants.Alphabet;
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

            DDS.MakeDDS(xivTex, savePath);
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
                                var red = br.ReadByte();
                                var green = br.ReadByte();
                                var blue = br.ReadByte();
                                var alpha = br.ReadByte();

                                convertedBytes.Add(blue);
                                convertedBytes.Add(green);
                                convertedBytes.Add(red);
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

            if (DDSType.ContainsKey(texType))
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
        /// Creates texture data ready to be imported into the DATs from an external file.
        /// If format is not specified, either the incoming file's DDS format is used (DDS files), 
        /// or the existing internal file's DDS format is used.
        /// </summary>
        /// <param name="internalPath"></param>
        /// <param name="externalPath"></param>
        /// <param name="texFormat"></param>
        /// <returns></returns>
        public async Task<byte[]> MakeTexData(string internalPath, string externalPath, XivTexFormat texFormat = XivTexFormat.INVALID )
        {
            // Ensure file exists.
            if (!File.Exists(externalPath))
            {
                throw new IOException($"Could not find file: {externalPath}");
            }

            var root = await XivCache.GetFirstRoot(internalPath);
            bool isDds = Path.GetExtension(externalPath).ToLower() == ".dds";

            var ddsContainer = new DDSContainer();
            try
            {
                // If no format was specified...
                if (texFormat == XivTexFormat.INVALID)
                {
                    if(isDds)
                    {
                        // If we're importing a DDS file, get the format from the incoming DDS file
                        using (var fs = new FileStream(externalPath, FileMode.Open))
                        {
                            using (var sr = new BinaryReader(fs))
                            {
                                texFormat = GetDDSTexFormat(sr);
                            }
                        }
                    }
                    else
                    {
                        // Otherwise use the current internal format.
                        var xivt = await _dat.GetType4Data(internalPath, false);
                        texFormat = xivt.TextureFormat;
                    }
                }

                // Check if the texture being imported has been imported before
                CompressionFormat compressionFormat = CompressionFormat.BGRA;

                switch (texFormat)
                {
                    case XivTexFormat.DXT1:
                        compressionFormat = CompressionFormat.BC1a;
                        break;
                    case XivTexFormat.DXT5:
                        compressionFormat = CompressionFormat.BC3;
                        break;
                    case XivTexFormat.A8R8G8B8:
                        compressionFormat = CompressionFormat.BGRA;
                        break;
                    default:
                        if (!isDds)
                        {
                            throw new Exception($"Format {texFormat} is not currently supported for BMP import\n\nPlease use the DDS import option instead.");
                        }
                        break;
                }

                if (!isDds)
                {
                    using (var surface = Surface.LoadFromFile(externalPath))
                    {
                        if (surface == null)
                            throw new FormatException($"Unsupported texture format");

                        surface.FlipVertically();

                        var maxMipCount = 1;
                        if (root != null)
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
                }

                // If we're not a DDS, write the DDS to file temporarily.
                var ddsFilePath = externalPath;
                if(!isDds)
                {
                    var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".dds");
                    ddsContainer.Write(tempFile, DDSFlags.None);
                    ddsFilePath = tempFile;
                }

                using (var br = new BinaryReader(File.OpenRead(ddsFilePath)))
                {
                    br.BaseStream.Seek(12, SeekOrigin.Begin);

                    var newHeight = br.ReadInt32();
                    var newWidth = br.ReadInt32();
                    br.ReadBytes(8);
                    var newMipCount = br.ReadInt32();

                    if (newHeight % 2 != 0 || newWidth % 2 != 0)
                    {
                        throw new Exception("Resolution must be a multiple of 2");
                    }

                    br.BaseStream.Seek(80, SeekOrigin.Begin);

                    var textureFlags = br.ReadInt32();
                    var texType = br.ReadInt32();

                    var uncompressedLength = (int)new FileInfo(ddsFilePath).Length - 128;
                    var newTex = new List<byte>();

                    if (!internalPath.Contains(".atex"))
                    {
                        var DDSInfo = await DDS.ReadDDS(br, texFormat, newWidth, newHeight, newMipCount);

                        newTex.AddRange(_dat.MakeType4DatHeader(texFormat, DDSInfo.mipPartOffsets, DDSInfo.mipPartCounts, (int)uncompressedLength, newMipCount, newWidth, newHeight));
                        newTex.AddRange(MakeTextureInfoHeader(texFormat, newWidth, newHeight, newMipCount));
                        newTex.AddRange(DDSInfo.compressedDDS);

                        return newTex.ToArray();
                    }
                    else
                    {
                        br.BaseStream.Seek(128, SeekOrigin.Begin);
                        newTex.AddRange(MakeTextureInfoHeader(texFormat, newWidth, newHeight, newMipCount));
                        newTex.AddRange(br.ReadBytes((int)uncompressedLength));
                        var data = await _dat.CreateType2Data(newTex.ToArray());
                        return data;
                    }
                }
            }
            finally
            {
                ddsContainer.Dispose();
            }
        }

        public async Task<long> ImportTex(string internalPath, string externalPath, IItem item, string source, IndexFile cachedIndexFile = null, ModList cachedModList = null)
        {
            long offset = 0;
            var path = internalPath;
            var df = IOUtil.GetDataFileFromPath(path);

            var data = await MakeTexData(path, externalPath);
            var modding = new Modding(_gameDirectory);
            Mod entry = null;
            if(cachedModList != null) 
            {
                entry = cachedModList.Mods.FirstOrDefault(x => x.fullPath == path);
            } else
            {
                entry = await modding.TryGetModEntry(path);
            }

            var type = Path.GetExtension(path) == ".atex" ? 2 : 4;

            offset = await _dat.WriteModFile(data, path, source, item, cachedIndexFile, cachedModList);
            return offset;
        }


        /// <summary>
        /// Imports a ColorSet file
        /// </summary>
        /// <param name="xivMtrl">The XivMtrl data of the original</param>
        /// <param name="ddsFileDirectory">The dds directory of the new ColorSet</param>
        /// <param name="item">The item</param>
        /// <param name="source">The source importing the file</param>
        /// <returns>The new offset</returns>
        public async Task<long> TexColorImporter(XivMtrl xivMtrl, DirectoryInfo ddsFileDirectory, IItem item, string source, XivLanguage lang)
        {
            var colorSetData = new List<Half>();
            byte[] colorSetExtraData = null;


            colorSetData = GetColorsetDataFromDDS(ddsFileDirectory);
            colorSetExtraData = GetColorsetExtraDataFromDDS(ddsFileDirectory);

            // Replace the color set data with the imported data
            xivMtrl.ColorSetData = colorSetData;
            xivMtrl.ColorSetDyeData = colorSetExtraData;
            if (xivMtrl.Unknown2.Length > 0)
            {
                // This byte enables the dye set if it's not already enabled.
                xivMtrl.Unknown2[0] = 12;
            }

            var mtrl = new Mtrl(_gameDirectory, xivMtrl.TextureTypePathList[0].DataFile, lang);
            return await mtrl.ImportMtrl(xivMtrl, item, source);
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
                var colorSetData = new List<Half>(256);

                // skip DDS header
                br.BaseStream.Seek(128, SeekOrigin.Begin);

                // color data is always 512 (4w x 16h = 64 x 8bpp = 512)
                // this reads 256 ushort values which is 256 x 2 = 512
                for (var i = 0; i < 256; i++)
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
        public byte[] DDStoMtrlData(XivMtrl xivMtrl, DirectoryInfo ddsFileDirectory, IItem item, XivLanguage lang)
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

            if (xivMtrl.Unknown2.Length > 0)
            {
                // This byte enables the dye set if it's not already enabled.
                xivMtrl.Unknown2[0] = 12;
            }

            var mtrl = new Mtrl(_gameDirectory, xivMtrl.TextureTypePathList[0].DataFile, lang);
            return mtrl.CreateMtrlFile(xivMtrl, item);
        }

        /// <summary>
        /// Creates the header for the texture info from the data to be imported.
        /// </summary>
        /// <param name="xivTex">Data for the currently displayed texture.</param>
        /// <param name="newWidth">The width of the DDS texture to be imported.</param>
        /// <param name="newHeight">The height of the DDS texture to be imported.</param>
        /// <param name="newMipCount">The number of mipmaps the DDS texture to be imported contains.</param>
        /// <returns>The created header data.</returns>
        private static List<byte> MakeTextureInfoHeader(XivTexFormat format, int newWidth, int newHeight, int newMipCount)
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


            headerData.AddRange(BitConverter.GetBytes(0));
            headerData.AddRange(BitConverter.GetBytes(1));
            headerData.AddRange(BitConverter.GetBytes(2));

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

            var combinedLength = 80;

            for (var i = 0; i < newMipCount; i++)
            {
                headerData.AddRange(BitConverter.GetBytes(combinedLength));
                combinedLength = combinedLength + mipLength;

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

            //ARGB 16F
            {113, XivTexFormat.A16B16G16R16F },

            //Uncompressed RGBA
            {0, XivTexFormat.A8R8G8B8 }

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