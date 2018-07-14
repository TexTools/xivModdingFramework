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
using xivModdingFramework.Helpers;
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
    }
}