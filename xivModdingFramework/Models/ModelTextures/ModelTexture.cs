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

using ImageMagick;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using xivModdingFramework.Materials.DataContainers;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Textures.Enums;
using xivModdingFramework.Textures.FileTypes;
using Color = SharpDX.Color;

namespace xivModdingFramework.Models.ModelTextures
{
    public class ModelTexture
    {
        private readonly XivMtrl _mtrlData;
        private readonly DirectoryInfo _gameDirectory;

        public ModelTexture(DirectoryInfo gameDirectory, XivMtrl mtrlData)
        {
            _mtrlData = mtrlData;
            _gameDirectory = gameDirectory;
        }

        /// <summary>
        /// Gets the texture maps for the model
        /// </summary>
        /// <returns>The texture maps in byte arrays inside a ModelTextureData class</returns>
        public async Task<ModelTextureData> GetModelMaps(Color? customColor = null, bool colorChangeShader = false)
        {
            var texMapData = await GetTexMapData();

            var dimensions = await EqualizeTextureSizes(texMapData);

            var materialType = GetMaterialType(_mtrlData.MTRLPath);

            var diffuseMap = new List<byte>();
            var normalMap = new List<byte>();
            var specularMap = new List<byte>();
            var emissiveMap = new List<byte>();
            var alphaMap = new List<byte>();

            var diffuseColorList = new List<Color>();
            var specularColorList = new List<Color>();
            var emissiveColorList = new List<Color>();

            await Task.Run(() =>
            {
                if (texMapData.ColorSet != null)
                {
                    var colorPixels = texMapData.ColorSet.Data;

                    for (var i = 0; i < texMapData.ColorSet.Data.Length; i += 16)
                    {
                        int red = colorPixels[i];
                        int green = colorPixels[i + 1];
                        int blue = colorPixels[i + 2];
                        int alpha = colorPixels[i + 3];

                        diffuseColorList.Add(new Color(red, green, blue));

                        red = colorPixels[i + 4];
                        green = colorPixels[i + 5];
                        blue = colorPixels[i + 6];
                        alpha = colorPixels[i + 7];

                        specularColorList.Add(new Color(red, green, blue));

                        red = colorPixels[i + 8];
                        green = colorPixels[i + 9];
                        blue = colorPixels[i + 10];
                        alpha = colorPixels[i + 11];

                        emissiveColorList.Add(new Color(red, green, blue));
                    }
                }
                else
                {
                    for (var i = 0; i < 1024; i += 16)
                    {
                        if (!materialType.Equals("other") && !materialType.Equals("housing"))
                        {
                            if (customColor != null)
                            {
                                diffuseColorList.Add(customColor.GetValueOrDefault());
                            }
                            else
                            {
                                if (!materialType.Equals("body"))
                                {
                                    diffuseColorList.Add(new Color(96, 57, 19));
                                }
                                else
                                {
                                    diffuseColorList.Add(new Color(255, 255, 255));
                                }
                            }
                        }
                        else
                        {
                            diffuseColorList.Add(new Color(255, 255, 255));
                        }

                        specularColorList.Add(new Color(25, 25, 25));
                        emissiveColorList.Add(new Color(0, 0, 0));
                    }
                }
            });

            byte[] diffusePixels = null, specularPixels = null, multiPixels = null, skinPixels = null, normalPixels = null;

            if (texMapData.Normal != null)
            {
                normalPixels = texMapData.Normal.Data;
            }

            if (texMapData.Diffuse != null)
            {
                diffusePixels = texMapData.Diffuse.Data;
            }

            if (texMapData.Specular != null)
            {
                specularPixels = texMapData.Specular.Data;
            }

            if (texMapData.Multi != null)
            {
                multiPixels = texMapData.Multi.Data;
            }

            if (texMapData.Skin != null)
            {
                skinPixels = texMapData.Skin.Data;
            }

            byte diffR = 255, diffG = 255, diffB = 255;
            byte specR = 255, specG = 255, specB = 255;
            byte normR = 255, normG = 255, normB = 255;

            var dataLength = 0;

            if (normalPixels != null)
            {
                dataLength = normalPixels.Length;
            }
            else if (diffusePixels != null)
            {
                dataLength = diffusePixels.Length;
            }

            await Task.Run(() =>
            {
                for (var i = 3; i < dataLength; i += 4)
                {
                    var alpha = 255;

                    if (normalPixels != null)
                    {
                        normR = normalPixels[i - 3];
                        normG = normalPixels[i - 2];
                        alpha = normalPixels[i - 1]; // This is the normal maps blue channel, and usually acts as the alpha channel


                        if (materialType.Equals("hair") || materialType.Equals("etc") || materialType.Equals("tail") || materialType.Equals("ears"))
                        {
                            normR = normalPixels[i - 3];
                            normG = normalPixels[i - 2];
                            normB = normalPixels[i - 1];
                            alpha = normalPixels[i];
                        }
                    }
                    else if (diffusePixels != null)
                    {
                        alpha = diffusePixels[i];
                    }

                    if (materialType.Equals("housing"))
                    {
                        if (colorChangeShader && normalPixels != null)
                        {
                            alpha = normalPixels[i];
                        }
                        else
                        {
                            if (diffusePixels != null)
                            {
                                alpha = diffusePixels[i];
                            }
                            else if (normalPixels != null)
                            {
                                alpha = normalPixels[i];
                            }
                            else
                            {
                                alpha = 255;
                            }
                        }
                    }

                    if (multiPixels != null)
                    {
                        diffR = multiPixels[i - 3];
                        diffG = multiPixels[i - 3];
                        diffB = multiPixels[i - 3];

                        specR = multiPixels[i - 1];
                        specG = multiPixels[i - 1];
                        specB = multiPixels[i - 1];
                    }
                    else
                    {
                        if (diffusePixels != null)
                        {
                            diffR = diffusePixels[i - 3];
                            diffG = diffusePixels[i - 2];
                            diffB = diffusePixels[i - 1];
                        }

                        if (specularPixels != null)
                        {
                            if (specularPixels.Length > i)
                            {
                                specR = specularPixels[i - 3];
                                specG = specularPixels[i - 2];
                                specB = specularPixels[i - 1];
                            }
                        }

                        if (skinPixels != null)
                        {
                            specR = skinPixels[i - 3];
                            specG = skinPixels[i - 2];
                            specB = skinPixels[i - 1];
                        }
                    }

                    Color diffuseColor, specularColor, emissiveColor, alphaColor;

                    var pixel = 0f;
                    var blendPercent = 0f;

                    if (normalPixels != null)
                    {
                        pixel = (normalPixels[i] / 255f) * 15f;
                        blendPercent = (float) (pixel - Math.Truncate(pixel));
                    }

                    if (materialType.Equals("hair") || materialType.Equals("etc") || materialType.Equals("tail") || materialType.Equals("ears"))
                    {
                        pixel = 0;
                        blendPercent = 0;
                    }

                    if (blendPercent != 0)
                    {
                        var firstColorLocation = (int) Math.Truncate(pixel);
                        var secondColorLocation = firstColorLocation + 1;

                        var diffColor1 = diffuseColorList[secondColorLocation];
                        var diffColor2 = diffuseColorList[firstColorLocation];

                        var firstColor = new Color(diffColor1.R, diffColor1.G, diffColor1.B, alpha);
                        var secondColor = new Color(diffColor2.R, diffColor2.G, diffColor2.B, alpha);

                        var diffuseBlend = Blend(firstColor, secondColor, blendPercent);

                        var specColor1 = specularColorList[secondColorLocation];
                        var specColor2 = specularColorList[firstColorLocation];

                        firstColor = new Color(specColor1.R, specColor1.G, specColor1.B, (byte) 255);
                        secondColor = new Color(specColor2.R, specColor2.G, specColor2.B, (byte) 255);

                        var specBlend = Blend(firstColor, secondColor, blendPercent);

                        var emisColor1 = emissiveColorList[secondColorLocation];
                        var emisColor2 = emissiveColorList[firstColorLocation];

                        firstColor = new Color(emisColor1.R, emisColor1.G, emisColor1.B, (byte) 255);
                        secondColor = new Color(emisColor2.R, emisColor2.G, emisColor2.B, (byte) 255);

                        var emisBlend = Blend(firstColor, secondColor, blendPercent);

                        diffuseColor = new Color((int) ((diffuseBlend.R / 255f) * diffR),
                            (int) ((diffuseBlend.G / 255f) * diffG), (int) ((diffuseBlend.B / 255f) * diffB), alpha);
                        specularColor = new Color((int) ((specBlend.R / 255f) * specR),
                            (int) ((specBlend.G / 255f) * specG), (int) ((specBlend.B / 255f) * specB), 255);
                        emissiveColor = new Color((int) emisBlend.R, (int) emisBlend.G, (int) emisBlend.B, (int) 255);
                    }
                    else
                    {
                        var colorLoc = (int) Math.Floor(pixel + 0.5f);

                        var diffColor = diffuseColorList[colorLoc];
                        var specColor = specularColorList[colorLoc];
                        var emisColor = emissiveColorList[colorLoc];

                        if (materialType.Equals("hair") || materialType.Equals("etc") || materialType.Equals("tail") || materialType.Equals("ears"))
                        {
                            diffuseColor = new Color((int) ((diffColor.R / 255f) * specR),
                                (int) ((diffColor.G / 255f) * specR), (int) ((diffColor.B / 255f) * specR), alpha);
                            specularColor = new Color((int) ((specColor.R / 255f) * specG),
                                (int) ((specColor.G / 255f) * specG), (int) ((specColor.B / 255f) * specG), 255);
                        }
                        else
                        {
                            diffuseColor = new Color((int) ((diffColor.R / 255f) * diffR),
                                (int) ((diffColor.G / 255f) * diffG), (int) ((diffColor.B / 255f) * diffB), alpha);

                            if (materialType.Equals("body"))
                            {
                                specularColor = new Color((int) ((specColor.R / 255f) * specG),
                                    (int) ((specColor.G / 255f) * specG), (int) ((specColor.B / 255f) * specG), 255);
                            }
                            else if (materialType.Equals("housing"))
                            {
                                specularColor = new Color((int) ((specColor.R / 255f) * specG),
                                    (int) ((specColor.G / 255f) * specG), (int) ((specColor.B / 255f) * specG), 255);
                            }
                            else
                            {
                                specularColor = new Color((int) ((specColor.R / 255f) * specB),
                                    (int) ((specColor.G / 255f) * specB), (int) ((specColor.B / 255f) * specB), 255);
                            }
                        }

                        emissiveColor = new Color((int) emisColor.R, (int) emisColor.G, (int) emisColor.B, (int) 255);
                    }

                    alphaColor = new Color((int) alpha, (int) alpha, (int) alpha, (int) alpha);

                    diffuseMap.AddRange(BitConverter.GetBytes(diffuseColor.ToRgba()));
                    specularMap.AddRange(BitConverter.GetBytes(specularColor.ToRgba()));
                    emissiveMap.AddRange(BitConverter.GetBytes(emissiveColor.ToRgba()));
                    alphaMap.AddRange(BitConverter.GetBytes(alphaColor.ToRgba()));
                    normalMap.AddRange(BitConverter.GetBytes(new Color(normR, normG, normB, (byte) 255).ToRgba()));
                }
            });

            var modelTextureData = new ModelTextureData
            {
                Width = dimensions.Width,
                Height = dimensions.Height,
                Normal = normalMap.ToArray(),
                Diffuse = diffuseMap.ToArray(),
                Specular = specularMap.ToArray(),
                Emissive = emissiveMap.ToArray(),
                Alpha = alphaMap.ToArray()
            };

            return modelTextureData;
        }

        /// <summary>
        /// Gets the data for the texture map
        /// </summary>
        /// <returns>The texure map data</returns>
        private async Task<TexMapData> GetTexMapData()
        {
            var tex = new Tex(_gameDirectory);

            var texMapData = new TexMapData();

            foreach (var texTypePath in _mtrlData.TextureTypePathList)
            {
                if (texTypePath.Type != XivTexType.ColorSet)
                {
                    var texData = await tex.GetTexData(texTypePath);

                    var imageData = await tex.GetImageData(texData);

                    switch (texTypePath.Type)
                    { 
                        case XivTexType.Diffuse:
                            texMapData.Diffuse = new TexInfo { Width = texData.Width, Height = texData.Height, Data = imageData };
                            break;
                        case XivTexType.Specular:
                            texMapData.Specular = new TexInfo { Width = texData.Width, Height = texData.Height, Data = imageData }; ;
                            break;
                        case XivTexType.Normal:
                            texMapData.Normal = new TexInfo { Width = texData.Width, Height = texData.Height, Data = imageData }; ;
                            break;
                        case XivTexType.Multi:
                            texMapData.Multi = new TexInfo { Width = texData.Width, Height = texData.Height, Data = imageData }; ;
                            break;
                        case XivTexType.Skin:
                            texMapData.Skin = new TexInfo { Width = texData.Width, Height = texData.Height, Data = imageData }; ;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            foreach (var texPath in _mtrlData.TexturePathList)
            {
                if (texPath.Contains("dummy"))
                {
                    texMapData.HasDummyTextures = true;
                    break;
                }
            }

            if (_mtrlData.ColorSetDataSize > 0)
            {
                var colorSetData = new List<byte>();
                foreach (var half in _mtrlData.ColorSetData)
                {

                    var colorByte = (byte) (half * 255);

                    if (half > 1)
                    {
                        colorByte = 255;
                    }

                    colorSetData.Add(colorByte);
                }

                texMapData.ColorSet = new TexInfo { Width = 4, Height = 16, Data = colorSetData.ToArray() }; ;
            }

            return texMapData;
        }

        /// <summary>
        /// Equalizes the size of the textures by scaling to the largest known texture size
        /// </summary>
        /// <param name="texMapData">The texture map data containing the texture bytes</param>
        /// <returns>The width and height that the textures were equalized to.</returns>
        private async Task<(int Width, int Height)> EqualizeTextureSizes(TexMapData texMapData)
        {
            // Normal map is chosen because almost every item has a normal map, diffuse is chosen otherwise
            var width = 0;
            var height = 0;
            var largestSize = 0;
            if (texMapData.Normal != null)
            {
                width = texMapData.Normal.Width;
                height = texMapData.Normal.Height;
                largestSize = width * height;
            }
            else if (texMapData.Diffuse != null)
            {
                width = texMapData.Diffuse.Width;
                height = texMapData.Diffuse.Height;
                largestSize = width * height;
            }

            var scaleDown = false;
            var scale = 1;

            if (texMapData.Normal != null)
            {
                var size = texMapData.Normal.Width * texMapData.Normal.Height;

                if (size > largestSize)
                {
                    largestSize = size;
                    width = texMapData.Normal.Width;
                    height = texMapData.Normal.Height;
                }
            }

            if (texMapData.Diffuse != null)
            {
                var size = texMapData.Diffuse.Width * texMapData.Diffuse.Height;

                if (size > largestSize)
                {
                    largestSize = size;
                    width = texMapData.Diffuse.Width;
                    height = texMapData.Diffuse.Height;
                }
            }

            if (texMapData.Specular != null)
            {
                var size = texMapData.Specular.Width * texMapData.Specular.Height;

                if (size > largestSize)
                {
                    largestSize = size;
                    width = texMapData.Specular.Width;
                    height = texMapData.Specular.Height;
                }
            }

            if (texMapData.Multi != null)
            {
                var size = texMapData.Multi.Width * texMapData.Multi.Height;

                if (size > largestSize)
                {
                    largestSize = size;
                    width = texMapData.Multi.Width;
                    height = texMapData.Multi.Height;
                }
            }

            if (texMapData.Skin != null)
            {
                var size = texMapData.Skin.Width * texMapData.Skin.Height;

                if (size > largestSize)
                {
                    largestSize = size;
                    width = texMapData.Skin.Width;
                    height = texMapData.Skin.Height;
                }
            }

            if (width > 4000 || height > 4000)
            {
                scale = 4;
                scaleDown = true;
            }
            //else if (width > 2000 || height > 2000)
            //{
            //    scale = 2;
            //    scaleDown = true;
            //}

            width = width / scale;
            height = height / scale;
            largestSize = width * height;

            await Task.Run(() =>
            {
                if (texMapData.Normal != null && largestSize > texMapData.Normal.Width * texMapData.Normal.Height ||
                    scaleDown)
                {
                    var pixelSettings =
                        new PixelReadSettings(texMapData.Normal.Width, texMapData.Normal.Height, StorageType.Char,
                            PixelMapping.RGBA);

                    using (var image = new MagickImage(texMapData.Normal.Data, pixelSettings))
                    {
                        var size = new MagickGeometry(width, height);
                        size.IgnoreAspectRatio = true;
                        image.Resize(size);

                        texMapData.Normal.Width = width;
                        texMapData.Normal.Height = height;

                        texMapData.Normal.Data = image.ToByteArray(MagickFormat.Rgba);
                    }
                }

                if (texMapData.Diffuse != null &&
                    (largestSize > texMapData.Diffuse.Width * texMapData.Diffuse.Height || scaleDown))
                {
                    var pixelSettings =
                        new PixelReadSettings(texMapData.Diffuse.Width, texMapData.Diffuse.Height, StorageType.Char,
                            PixelMapping.RGBA);

                    using (var image = new MagickImage(texMapData.Diffuse.Data, pixelSettings))
                    {
                        image.Alpha(AlphaOption.Off);
                        var size = new MagickGeometry(width, height);
                        size.IgnoreAspectRatio = true;
                        image.Resize(size);

                        texMapData.Diffuse.Width = width;
                        texMapData.Diffuse.Height = height;

                        texMapData.Diffuse.Data = image.ToByteArray(MagickFormat.Rgba);
                    }
                }

                if (texMapData.Specular != null &&
                    (largestSize > texMapData.Specular.Width * texMapData.Specular.Height || scaleDown))
                {
                    var pixelSettings =
                        new PixelReadSettings(texMapData.Specular.Width, texMapData.Specular.Height, StorageType.Char,
                            PixelMapping.RGBA);

                    using (var image = new MagickImage(texMapData.Specular.Data, pixelSettings))
                    {
                        var size = new MagickGeometry(width, height);
                        size.IgnoreAspectRatio = true;
                        image.Resize(size);

                        texMapData.Specular.Width = width;
                        texMapData.Specular.Height = height;

                        texMapData.Specular.Data = image.ToByteArray(MagickFormat.Rgba);
                    }
                }

                if (texMapData.Multi != null &&
                    (largestSize > texMapData.Multi.Width * texMapData.Multi.Height || scaleDown))
                {
                    var pixelSettings =
                        new PixelReadSettings(texMapData.Multi.Width, texMapData.Multi.Height, StorageType.Char,
                            PixelMapping.RGBA);

                    using (var image = new MagickImage(texMapData.Multi.Data, pixelSettings))
                    {
                        var size = new MagickGeometry(width, height);
                        size.IgnoreAspectRatio = true;
                        image.Resize(size);

                        texMapData.Multi.Width = width;
                        texMapData.Multi.Height = height;

                        texMapData.Multi.Data = image.ToByteArray(MagickFormat.Rgba);
                    }
                }

                if (texMapData.Skin != null &&
                    (largestSize > texMapData.Skin.Width * texMapData.Skin.Height || scaleDown))
                {
                    var pixelSettings =
                        new PixelReadSettings(texMapData.Skin.Width, texMapData.Skin.Height, StorageType.Char,
                            PixelMapping.RGBA);

                    using (var image = new MagickImage(texMapData.Skin.Data, pixelSettings))
                    {
                        var size = new MagickGeometry(width, height);
                        size.IgnoreAspectRatio = true;
                        image.Resize(size);

                        texMapData.Skin.Width = width;
                        texMapData.Skin.Height = height;

                        texMapData.Skin.Data = image.ToByteArray(MagickFormat.Rgba);
                    }
                }
            });

            return (width, height);
        }

        /// <summary>Blends the specified colors together.</summary>
        /// <param name="color">Color to blend onto the background color.</param>
        /// <param name="backColor">Color to blend the other color onto.</param>
        /// <param name="amount">How much of <paramref name="color"/> to keep,
        /// “on top of” <paramref name="backColor"/>.</param>
        /// <returns>The blended colors.</returns>
        private static Color Blend(Color color, Color backColor, double amount)
        {
            var r = (byte)((color.R * amount) + backColor.R * (1 - amount));
            var g = (byte)((color.G * amount) + backColor.G * (1 - amount));
            var b = (byte)((color.B * amount) + backColor.B * (1 - amount));
            return new Color(r, g, b);
        }

        private string GetMaterialType(string mtrlPath)
        {
            if (mtrlPath.Contains("bgcommon/"))
            {
                return "housing";
            }

            if (mtrlPath.Contains("/hair/h"))
            {
                return "hair";
            }

            if (mtrlPath.Contains("/body/b"))
            {
                return "body";
            }

            if (mtrlPath.Contains("/face/f"))
            {
                if (mtrlPath.Contains("_etc_"))
                {
                    return "etc";
                }

                if (mtrlPath.Contains("_iri"))
                {
                    return "iris";
                }

                return "fac";
            }

            if (mtrlPath.Contains("/tail/t"))
            {
                if (mtrlPath.Contains("1301") || mtrlPath.Contains("1304") || mtrlPath.Contains("1401") ||
                    mtrlPath.Contains("1404"))
                {
                    return "other";
                }

                return "tail";
            }

            if (mtrlPath.Contains("/zear/z") && !mtrlPath.Contains("_fac_"))
            {
                return "ears";
            }

            return "other";
        }

        public class TexMapData
        {
            public TexInfo Diffuse { get; set; }

            public TexInfo Specular { get; set; }

            public TexInfo Normal { get; set; }

            public TexInfo Multi { get; set; }

            public TexInfo ColorSet { get; set; }

            public TexInfo Skin { get; set; }

            public bool HasDummyTextures { get; set; }
        }

        public class TexInfo
        {
            public int Width { get; set; }

            public int Height { get; set; }

            public byte[] Data { get; set; }
        }
    }


}