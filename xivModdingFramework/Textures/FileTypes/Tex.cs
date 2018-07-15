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
using Newtonsoft.Json;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.Enums;

namespace xivModdingFramework.Textures.FileTypes
{
    /// <summary>
    /// This class contains the methods that deal with the .tex file type 
    /// </summary>
    public class Tex
    {
        private const string TexExtension = ".tex";
        private readonly DirectoryInfo _gameDirectory;

        public Tex(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
        }

        public XivTex getTexData(TexTypePath ttp)
        {
            var xivTex = new XivTex {TextureTypeAndPath = ttp};

            var folder = Path.GetDirectoryName(ttp.Path);
            var file = Path.GetFileName(ttp.Path);

            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);

            var offset = index.GetDataOffset(HashGenerator.GetHash(folder), HashGenerator.GetHash(file), ttp.DataFile);
            dat.GetType4Data(offset, ttp.DataFile, xivTex);

            return xivTex;
        }

        /// <summary>
        /// Gets the raw pixel data for the texture
        /// </summary>
        public byte[] GetImageData(XivTex xivTex)
        {
            byte[] imageData = null;

            switch (xivTex.TextureFormat)
            {
                case XivTexFormat.DXT1:
                    imageData = DxtUtil.DecompressDxt1(xivTex.TexData, xivTex.Width, xivTex.Heigth);
                    break;
                case XivTexFormat.DXT3:
                    imageData = DxtUtil.DecompressDxt3(xivTex.TexData, xivTex.Width, xivTex.Heigth);
                    break;
                case XivTexFormat.DXT5:
                    imageData = DxtUtil.DecompressDxt5(xivTex.TexData, xivTex.Width, xivTex.Heigth);
                    break;
                case XivTexFormat.L8:
                case XivTexFormat.A8:
                case XivTexFormat.A4R4G4B4:
                case XivTexFormat.A1R5G5B5:
                case XivTexFormat.A8R8G8B8:
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

            return imageData;
        }

        /// <summary>
        /// Converts a DDS file into a TEX file then imports it 
        /// </summary>
        /// <param name="xivTex">The texture data</param>
        /// <param name="item">The item who's texture we are importing</param>
        /// <param name="ddsFileDirectory">The directory of the dds file being imported</param>
        /// <param name="modlistDirectory">The directory of the modlist to write to</param>
        /// <returns>The offset to the new imported data</returns>
        public long TexDDSImporter(XivTex xivTex, IItem item, DirectoryInfo ddsFileDirectory, DirectoryInfo modlistDirectory)
        {
            int lineNum = 0, offset = 0;
            var inModList = false;
            ModInfo modInfo = null;

            var dat = new Dat(_gameDirectory);

            if (File.Exists(ddsFileDirectory.FullName))
            {
                // Check if the texture being imported has been imported before
                using (var sr = new StreamReader(modlistDirectory.FullName))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        modInfo = JsonConvert.DeserializeObject<ModInfo>(line);
                        if (modInfo.fullPath.Equals(xivTex.TextureTypeAndPath.Path))
                        {
                            inModList = true;
                            break;
                        }
                        lineNum++;
                    }
                }

                using (var br = new BinaryReader(File.OpenRead(ddsFileDirectory.FullName)))
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
                            var bpp = br.ReadInt32();
                            if (bpp == 32)
                            {
                                textureType = XivTexFormat.A8R8G8B8;
                            }
                            else
                            {
                                var red = br.ReadInt32();

                                switch (red)
                                {
                                    case 31744:
                                        textureType = XivTexFormat.A1R5G5B5;
                                        break;
                                    case 3840:
                                        textureType =  XivTexFormat.A4R4G4B4;
                                        break;
                                }
                            }

                            break;
                    }

                    if (textureType == xivTex.TextureFormat)
                    {
                        var newTex = new List<byte>();

                        var uncompressedLength = (int)new FileInfo(ddsFileDirectory.FullName).Length - 128;

                        var DDSInfo = DDS.ReadDDS(br, xivTex, newWidth, newHeight, newMipCount);

                        newTex.AddRange(dat.MakeType4DatHeader(xivTex, DDSInfo.mipPartOffsets, DDSInfo.mipPartCounts, uncompressedLength, newMipCount, newWidth, newHeight));
                        newTex.AddRange(MakeTextureInfoHeader(xivTex, newWidth, newHeight, newMipCount));
                        newTex.AddRange(DDSInfo.compressedDDS);

                        offset = dat.WriteToDat(newTex, modInfo, inModList, xivTex.TextureTypeAndPath.Path,
                            item.ItemCategory, item.Name, lineNum, xivTex.TextureTypeAndPath.DataFile, modlistDirectory);
                    }
                    else
                    {
                        throw new Exception($"Incorrect file type. Expected: {xivTex.TextureFormat}  Given: {textureType}");
                    }
                }
            }
            else
            {
                throw new IOException($"Could not find file: {ddsFileDirectory.FullName}");
            }

            return offset;
        }

        /// <summary>
        /// Creates the header for the texture info from the data to be imported.
        /// </summary>
        /// <param name="xivTex">Data for the currently displayed texture.</param>
        /// <param name="newWidth">The width of the DDS texture to be imported.</param>
        /// <param name="newHeight">The height of the DDS texture to be imported.</param>
        /// <param name="newMipCount">The number of mipmaps the DDS texture to be imported contains.</param>
        /// <returns>The created header data.</returns>
        private static List<byte> MakeTextureInfoHeader(XivTex xivTex, int newWidth, int newHeight, int newMipCount)
        {
            var headerData = new List<byte>();
            
            headerData.AddRange(BitConverter.GetBytes((short)0));
            headerData.AddRange(BitConverter.GetBytes((short)128));
            headerData.AddRange(BitConverter.GetBytes(short.Parse(xivTex.TextureFormat.GetTexFormatCode())));
            headerData.AddRange(BitConverter.GetBytes((short)0));
            headerData.AddRange(BitConverter.GetBytes((short)newWidth));
            headerData.AddRange(BitConverter.GetBytes((short)newHeight));
            headerData.AddRange(BitConverter.GetBytes((short)1));
            headerData.AddRange(BitConverter.GetBytes((short)newMipCount));


            headerData.AddRange(BitConverter.GetBytes(0));
            headerData.AddRange(BitConverter.GetBytes(1));
            headerData.AddRange(BitConverter.GetBytes(2));

            int mipLength;

            switch (xivTex.TextureFormat)
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
        private readonly Dictionary<int, XivTexFormat> DDSType = new Dictionary<int, XivTexFormat>
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
    }
}