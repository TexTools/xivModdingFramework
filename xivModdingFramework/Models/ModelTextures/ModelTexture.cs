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
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Processing.Processors;
using SixLabors.ImageSharp.Processing.Processors.Filters;
using xivModdingFramework.Materials.DataContainers;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Textures.Enums;
using xivModdingFramework.Textures.FileTypes;
using Color = SharpDX.Color;
using Color4 = SharpDX.Color4;
using xivModdingFramework.Materials.FileTypes;
using HelixToolkit.SharpDX.Core.Model.Scene2D;
using HelixToolkit.SharpDX.Core;
using System.Linq;
using System.Runtime.CompilerServices;
using static xivModdingFramework.Models.DataContainers.ShapeData;
using static xivModdingFramework.Materials.DataContainers.ShaderHelpers;
using xivModdingFramework.Textures.DataContainers;

namespace xivModdingFramework.Models.ModelTextures
{

    /// <summary>
    /// Data holder for the entire set of custom colors needed to render everything that supports custom colors.
    /// </summary>
    public class CustomModelColors : ICloneable
    {
        public Color SkinColor;
        public Color EyeColor;      // Off eye color customization isn't really sanely doable since it's managed by Vertex Color.
        public Color LipColor;
        public Color HairColor;

        // Also known as Limbal Color or Etc. Color.  Just depends on race.
        // Most have tattoo color, so that's the default name.
        public Color TattooColor;

        public Color FurnitureColor;

        // Optional values.
        public Color? HairHighlightColor;

        public bool InvertNormalGreen;



        // One for each colorset row.  Null for undyed.
        public List<Color?> DyeColors;

        public CustomModelColors()
        {
            // These match with the Hex Colors in TexTools Settings.Settings file.
            // They don't really *have* to, but it probably makes sense to use the 
            // Same defaults.

            SkinColor = new Color(204, 149, 120, 255);
            EyeColor = new Color(21, 176, 172, 255);
            LipColor = new Color(173, 105, 105, 255);
            HairColor = new Color(130, 64, 13, 255);
            TattooColor = new Color(0, 255, 66, 255);
            FurnitureColor = new Color(141, 60, 204, 255);

            HairHighlightColor = new Color(77, 126, 240, 255);
            InvertNormalGreen = false;

            DyeColors = new List<Color?>(16);
            for(int i = 0; i < 16; i++)
            {
                DyeColors.Add(null);
            }
        }

        public object Clone()
        {
            var clone = (CustomModelColors) MemberwiseClone();
            clone.DyeColors = new List<Color?>(DyeColors.ToList());
            return clone;
        }
    }

    public static class ModelTexture
    {
        // Static level default value accessor.
        // This is effectively the user's color settings as far as the 
        // framework lib is concerned.
        private static CustomModelColors _defaultColors;
        public static void SetCustomColors(CustomModelColors c)
        {
            _defaultColors = c;
        }
        public static CustomModelColors GetCustomColors()
        {
            if (_defaultColors == null)
            {
                // Make a default entry if none was ever set.
                _defaultColors = new CustomModelColors();
            }

            // Return a clone to prevent the get function copy from bashing the stored copy.
            return (CustomModelColors)_defaultColors.Clone();
        }


        /// <summary>
        /// Gets the customized texture map data for a model.
        /// Null custom model colors uses the defaults at ModelTexture.GetCustomColors().
        /// </summary>
        /// <param name="gameDirectory"></param>
        /// <param name="mtrl"></param>
        /// <param name="colors"></param>
        /// <returns></returns>
        public static async Task<ModelTextureData> GetModelMaps(DirectoryInfo gameDirectory, XivMtrl mtrl, CustomModelColors colors = null, int highlightedRow = -1)
        {
            var tex = new Tex(gameDirectory);
            return await GetModelMaps(tex, mtrl, colors, highlightedRow);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Clamp(float value, float min = 0.0f, float max = 1.0f)
        {
            value = value < min ? min : value;
            return value > max ? max : value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EncodeColorBytes(byte[] buf, int offset, Color4 c)
        {
            buf[offset] = (byte)(Clamp(c.Red) * 255.0f);
            buf[offset + 1] = (byte)(Clamp(c.Green) * 255.0f);
            buf[offset + 2] = (byte)(Clamp(c.Blue) * 255.0f);
            buf[offset + 3] = (byte)(Clamp(c.Alpha) * 255.0f);
        }

        /// <summary>
        /// Gets the texture maps for the model
        /// </summary>
        /// <returns>The texture maps in byte arrays inside a ModelTextureData class</returns>
        public static async Task<ModelTextureData> GetModelMaps(Tex tex, XivMtrl mtrl, CustomModelColors colors = null, int highlightedRow = -1)
        {
            if (colors == null)
                colors = GetCustomColors();

            var texMapData = await GetTexMapData(tex, mtrl);
            var dimensions = await EqualizeTextureSizes(texMapData);

            var diffusePixels = texMapData.Diffuse?.Data;
            var normalPixels = texMapData.Normal?.Data;
            var multiPixels = texMapData.Multi?.Data;
            var indexPixels = texMapData.Index?.Data;

            if (normalPixels == null && diffusePixels == null)
            {
                // This material doesn't actually have any readable data.
                var empty = new ModelTextureData
                {
                    Width = 0,
                    Height = 0,
                    Normal = new byte[0],
                    Diffuse = new byte[0],
                    Specular = new byte[0],
                    Emissive = new byte[0],
                    Alpha = new byte[0],
                    MaterialPath = mtrl.MTRLPath.Substring(mtrl.MTRLPath.LastIndexOf('/'))
                };
                return empty;
            }

            var result = new ModelTextureData
            {
                Width = dimensions.Width,
                Height = dimensions.Height,
                Normal = new byte[dimensions.Width * dimensions.Height * 4],
                Diffuse = new byte[dimensions.Width * dimensions.Height * 4],
                Specular = new byte[dimensions.Width * dimensions.Height * 4],
                Emissive = new byte[dimensions.Width * dimensions.Height * 4],
                Alpha = new byte[dimensions.Width * dimensions.Height * 4],
                MaterialPath = mtrl.MTRLPath.Substring(mtrl.MTRLPath.LastIndexOf('/'))
            };

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            Color4 readInputPixel(byte[] pixels, int i, Color4 color)
            {
                if (pixels != null)
                    color = new Color(pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
                return color;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            (int, float) readColorIndex(int i)
            {
                byte colorsetByte = indexPixels[i]; // Index Red channel
                byte blendByte = indexPixels[i + 1]; // Index Green channel
                int rowNumber = ((colorsetByte + 8) / 17) * 2;
                float blendAmount = 1.0f - blendByte / 255.0f;
                return (rowNumber, blendAmount);
            }

            // This was how color sets were selected pre-Dawntrail
            // As of the Dawntrail benchmark, this method of color mapping appears to no longer be used
            /*
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            (int, float) readColorIndex(int i)
            {
                byte colorsetByte = normalPixels[i + 3]; // Normal Alpha channel
                int rowNumber = colorsetByte / 17;
                float blendAmount = (colorsetByte % 17) / 17.0f;
                return (rowNumber, blendAmount);
            }
            */

            var dataLength = normalPixels != null ? normalPixels.Length : diffusePixels.Length;
            var shaderFn = GetShaderMapper(GetCustomColors(), mtrl);
            var colorSetFn = GetColorSetMapper(texMapData.ColorSet, mtrl, highlightedRow);
            bool invertNormalGreen = colors.InvertNormalGreen;

            // No id map, just disable color sets
            if (indexPixels == null)
                colorSetFn = null;

            await Task.Run(() =>
            {
                Parallel.ForEach(Partitioner.Create(0, dataLength / 4), range =>
                {
                    for (int i = range.Item1 * 4; i < range.Item2 * 4; i += 4)
                    {
                        // Load the individual pixels into memory.
                        Color4 baseDiffuseColor = readInputPixel(diffusePixels, i, new Color4(1.0f, 1.0f, 1.0f, 1.0f));
                        Color4 baseNormalColor = readInputPixel(normalPixels, i, new Color4(0.5f, 0.5f, 0.5f, 1.0f));
                        Color4 baseMultiColor = readInputPixel(multiPixels, i, new Color4(1.0f, 1.0f, 1.0f, 1.0f));

                        if (invertNormalGreen)
                            baseNormalColor.Green = 1.0f - baseNormalColor.Green;

                        var shaderResult = shaderFn(baseDiffuseColor, baseNormalColor, baseMultiColor);
                        Color4 diffuseColor = shaderResult.Diffuse;
                        Color4 normalColor = shaderResult.Normal;
                        Color4 specularColor = shaderResult.Specular;
                        Color4 alphaColor = shaderResult.Alpha;
                        Color4 emissiveColor = Color4.Black;

                        // Apply colorset if needed.
                        if (colorSetFn != null)
                        {
                            var (rowNumber, blendAmount) = readColorIndex(i);
                            var colorSetMapResult = colorSetFn(rowNumber, blendAmount, diffuseColor, specularColor);
                            diffuseColor = colorSetMapResult.Diffuse;
                            specularColor = colorSetMapResult.Specular;
                            emissiveColor = colorSetMapResult.Emissive;
                        }

                        // White out the opacity channels where appropriate.
                        specularColor.Alpha = 1.0f;
                        normalColor.Alpha = 1.0f;

                        EncodeColorBytes(result.Diffuse, i, diffuseColor);
                        EncodeColorBytes(result.Normal, i, normalColor);
                        EncodeColorBytes(result.Specular, i, specularColor);
                        EncodeColorBytes(result.Alpha, i, alphaColor);
                        EncodeColorBytes(result.Emissive, i, emissiveColor);
                    }
                });
            });

            return result;
        }

        /// <summary>
        /// Retreives the raw pixel data for each texture, collated into a class to hold them.
        /// </summary>
        /// <returns>The texure map data</returns>
        private static async Task<TexMapData> GetTexMapData(Tex tex, XivMtrl mtrl)
        {
            var texMapData = new TexMapData();

            // Use the function that returns proper sane reuslts.
            var ttps = mtrl.GetTextureTypePathList();

            // Decode compressed textures
            foreach (var ttp in ttps)
            {
                // Skip loading textures that aren't supported in model previews
                if (ttp.Type != XivTexType.Diffuse && ttp.Type != XivTexType.Specular && ttp.Type != XivTexType.Mask
                 && ttp.Type != XivTexType.Skin && ttp.Type != XivTexType.Normal && ttp.Type != XivTexType.Index)
                {
                    continue;
                }

                var texData = await tex.GetTexData(ttp.Path, ttp.Type);
                var imageData = await tex.GetImageData(texData);

                switch (ttp.Type)
                {
                    case XivTexType.Diffuse:
                        texMapData.Diffuse = new TexInfo { Width = texData.Width, Height = texData.Height, Data = imageData };
                        break;
                    case XivTexType.Specular:
                    case XivTexType.Mask:
                    case XivTexType.Skin:
                        texMapData.Multi = new TexInfo { Width = texData.Width, Height = texData.Height, Data = imageData };
                        break;
                    case XivTexType.Normal:
                        texMapData.Normal = new TexInfo { Width = texData.Width, Height = texData.Height, Data = imageData };
                        break;
                    case XivTexType.Index:
                        texMapData.Index = new TexInfo { Width = texData.Width, Height = texData.Height, Data = imageData };
                        break;
                }
            }

            if (mtrl.ColorSetDataSize > 0)
            {
                int count = (mtrl.ColorSetData.Count < 1024) ? 16 : 32;
                int stride = (mtrl.ColorSetData.Count < 1024) ? 16 : 32;

                // All 32 color entries are allocated even if the original material only had 16
                var colorSetInfo = new ColorSetInfo()
                {
                    Diffuse = new Color4[32],
                    Specular = new Color4[32],
                    Emissive = new Color4[32],
                };

                for (int i = 0; i < count; ++i)
                {
                    int offset = i * stride;
                    colorSetInfo.Diffuse[i] = new Color4(mtrl.ColorSetData[offset], mtrl.ColorSetData[offset + 1], mtrl.ColorSetData[offset + 2], 1.0f);
                    colorSetInfo.Specular[i] = new Color4(mtrl.ColorSetData[offset + 4], mtrl.ColorSetData[offset + 5], mtrl.ColorSetData[offset + 6], 1.0f);
                    colorSetInfo.Emissive[i] = new Color4(mtrl.ColorSetData[offset + 8], mtrl.ColorSetData[offset + 9], mtrl.ColorSetData[offset + 10], 1.0f);
                }

                texMapData.ColorSet = colorSetInfo;
            }

            return texMapData;
        }

        private static void ResizeTexture(TexInfo texInfo, int width, int height)
        {
            using var img = Image.LoadPixelData<Rgba32>(texInfo.Data, texInfo.Width, texInfo.Height);

            // ImageSharp pre-multiplies the RGB by the alpha component during resize, if alpha is 0 (colourset row 0)
            // this ends up causing issues and destroying the RGB values resulting in an invisible preview model
            // https://github.com/SixLabors/ImageSharp/issues/1498#issuecomment-757519563
            img.Mutate(x => x.Resize(
                new ResizeOptions
                {
                    Size = new Size(width, height),
                    PremultiplyAlpha = false,
                })
            );

            texInfo.Data = new byte[width * height * 4];
            img.CopyPixelDataTo(texInfo.Data.AsSpan());

            texInfo.Width = width;
            texInfo.Height = height;
        }

        /// <summary>
        /// Equalizes the size of the textures by scaling to the largest known texture size
        /// </summary>
        /// <param name="texMapData">The texture map data containing the texture bytes</param>
        /// <returns>The width and height that the textures were equalized to.</returns>
        private static async Task<(int Width, int Height)> EqualizeTextureSizes(TexMapData texMapData)
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

            if (texMapData.Index != null)
            {
                var size = texMapData.Index.Width * texMapData.Index.Height;

                if (size > largestSize)
                {
                    largestSize = size;
                    width = texMapData.Index.Width;
                    height = texMapData.Index.Height;
                }
            }

            if (width > 4000 || height > 4000)
            {
                scale = 2;
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
                Parallel.Invoke(() => {
                    if (texMapData.Normal != null && (largestSize > texMapData.Normal.Width * texMapData.Normal.Height || scaleDown))
                        ResizeTexture(texMapData.Normal, width, height);
                }, () => {
                    if (texMapData.Diffuse != null && (largestSize > texMapData.Diffuse.Width * texMapData.Diffuse.Height || scaleDown))
                        ResizeTexture(texMapData.Diffuse, width, height);
                }, () => {
                    if (texMapData.Multi != null && (largestSize > texMapData.Multi.Width * texMapData.Multi.Height || scaleDown))
                        ResizeTexture(texMapData.Multi, width, height);
                }, () => {
                    if (texMapData.Index != null && (largestSize > texMapData.Index.Width * texMapData.Index.Height || scaleDown))
                        ResizeTexture(texMapData.Index, width, height);
                });
            });

            return (width, height);
        }

        private struct ShaderMapperResult
        {
            public Color4 Diffuse;
            public Color4 Normal;
            public Color4 Specular;
            public Color4 Alpha;
        }

        private delegate ShaderMapperResult ShaderMapperDelegate(Color4 diffuse, Color4 normal, Color4 mask);

        private static bool thrownException1 = false;

        private static ShaderMapperDelegate GetShaderMapper(CustomModelColors colors, XivMtrl mtrl)
        {
            // Based on https://docs.google.com/spreadsheets/d/1iY4C6zSJ0K2vpBXNh5BLsTj6_7Ah-fHsznsC5xqMmaw

            var shaderPack = mtrl.ShaderPack;

            bool hasDiffuse = mtrl.Textures.Any(x => x.Usage == XivTexType.Diffuse);
            bool hasSpecular = mtrl.Textures.Any(x => x.Usage == XivTexType.Specular);
            bool hasMulti = mtrl.Textures.Any(x => x.Usage == XivTexType.Mask);

            if (!thrownException1 && hasMulti && hasSpecular)
            {
                thrownException1 = true;
                throw new Exception("Model has both a mask and a specular -- unimplemented!");
            }

            if (shaderPack == EShaderPack.Character || shaderPack == EShaderPack.CharacterLegacy || shaderPack == EShaderPack.CharacterGlass
             || shaderPack == EShaderPack.CharacterScroll || shaderPack == EShaderPack.CharacterInc)
            {
                // This is the most common family of shaders that appears on gear, monsters, etc.
                // Many of its features should be controlled by shader parameters that aren't implemented

                if (hasMulti && shaderPack != EShaderPack.CharacterLegacy)
                {
                    // Cheap specularity adjustments based on the material type
                    // These were chosen based on nothing except what looked nice
                    var discountSpecularMaterialScale = new float[4]{
                        0.40f, // Default
                        0.80f, // Metal
                        0.20f, // Leather
                        0.10f, // Cloth
                    };

                    return (Color4 diffuse, Color4 normal, Color4 multi) => {
                        // Select a specular intensity based on the material type
                        var specScale = discountSpecularMaterialScale[(int)(multi.Green / 64.0f) & 3];
                        // Invert the cavity map to use as a specular texture
                        var discountSpecular = (1.0f - multi.Red) * specScale;
                        // Use AO (?) as the diffuse if there is no diffuse texture
                        if (!hasDiffuse)
                            diffuse = new Color4(multi.Blue, multi.Blue, multi.Blue, 1.0f);
                        return new ShaderMapperResult()
                        {
                            Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, normal.Blue),
                            Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                            Specular = new Color4(discountSpecular, discountSpecular, discountSpecular, 1.0f),
                            Alpha = new Color4(normal.Blue)
                        };
                    };
                }
                else if (hasMulti && shaderPack == EShaderPack.CharacterLegacy)
                {
                    // Meaning of the characterlegacy Mask texture is the same as it was pre-Dawntrail
                    return (Color4 diffuse, Color4 normal, Color4 multi) => {
                        // Use AO as the diffuse if there is no diffuse texture
                        if (!hasDiffuse)
                            diffuse = new Color4(multi.Red, multi.Red, multi.Red, 1.0f);
                        return new ShaderMapperResult()
                        {
                            Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, normal.Blue),
                            Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                            Specular = new Color4(multi.Green, multi.Green, multi.Green, 1.0f),
                            Alpha = new Color4(normal.Blue)
                        };
                    };
                }
                else if (hasSpecular) // "Multi" is actually a full specular map
                {
                    int multiAOChannel = shaderPack == EShaderPack.CharacterLegacy ? 0 : 2;
                    return (Color4 diffuse, Color4 normal, Color4 multi) => {
                        // Use AO as the diffuse if there is no diffuse texture
                        if (!hasDiffuse)
                            diffuse = new Color4(multi[multiAOChannel], multi[multiAOChannel], multi[multiAOChannel], 1.0f);
                        return new ShaderMapperResult()
                        {
                            Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, normal.Blue),
                            Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                            Specular = multi,
                            Alpha = new Color4(normal.Blue)
                        };
                    };
                }
                else // No mask or specular
                {
                    int multiAOChannel = shaderPack == EShaderPack.CharacterLegacy ? 0 : 2;
                    return (Color4 diffuse, Color4 normal, Color4 multi) => {
                        // Use AO as the diffuse if there is no diffuse texture
                        if (!hasDiffuse)
                            diffuse = new Color4(multi[multiAOChannel], multi[multiAOChannel], multi[multiAOChannel], 1.0f);
                        return new ShaderMapperResult()
                        {
                            Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, normal.Blue),
                            Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                            Specular = Color4.Black, // no shiny
                            Alpha = new Color4(normal.Blue)
                        };
                    };
                }
            }
            else if (shaderPack == EShaderPack.Skin)
            {
                var skinColor = (Color4)colors.SkinColor;
                var lipColor = (Color4)colors.LipColor;

                return (Color4 diffuse, Color4 normal, Color4 multi) => {
                    float skinInfluence = multi.Blue;
                    diffuse = Color4.Lerp(diffuse, diffuse * skinColor, skinInfluence);

                    float lipsInfluence = multi.Alpha;
                    diffuse = Color4.Lerp(diffuse, diffuse * lipColor, 1.0f - lipsInfluence);

                    return new ShaderMapperResult()
                    {
                        Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, 1.0f),
                        Normal = new Color4(0.5f, 0.5f, 1.0f, 1.0f),
                        Specular = new Color4(multi.Red, multi.Red, multi.Red, 1.0f) * 0.25f, // Hard-coded reduction in specular for skin
                        Alpha = new Color4(diffuse.Alpha)
                    };
                };
            }
            else if (shaderPack == EShaderPack.SkinLegacy)
            {
                // Meaning of the skinlegacy Mask texture is the same as it was pre-Dawntrail
                var skinColor = (Color4)colors.SkinColor;
                var lipColor = (Color4)colors.LipColor;

                return (Color4 diffuse, Color4 normal, Color4 multi) => {
                    float skinInfluence = multi.Red;
                    diffuse = Color4.Lerp(diffuse, diffuse * skinColor, skinInfluence);

                    float lipsInfluence = multi.Blue;
                    diffuse = Color4.Lerp(diffuse, diffuse * lipColor, 1.0f - lipsInfluence);

                    return new ShaderMapperResult()
                    {
                        Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, 1.0f),
                        Normal = new Color4(0.5f, 0.5f, 1.0f, 1.0f),
                        Specular = new Color4(multi.Green, multi.Green, multi.Green, 1.0f) * 0.25f, // Hard-coded reduction in specular for skin
                        Alpha = new Color4(diffuse.Alpha)
                    };
                };
            }
            else if (shaderPack == EShaderPack.Hair)
            {
                var hairColor = (Color4)colors.HairColor;
                var highlightColor = hairColor;

                if (colors.HairHighlightColor != null)
                    highlightColor = (Color4)colors.HairHighlightColor;

                return (Color4 diffuse, Color4 normal, Color4 multi) => {
                    float highlightInfluence = normal.Blue;
                    diffuse = Color4.Lerp(hairColor, hairColor * highlightColor, highlightInfluence);
                    return new ShaderMapperResult()
                    {
                        Diffuse = diffuse,
                        Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                        Specular = new Color4(multi.Red, multi.Red, multi.Red, 1.0f) * 0.5f, // Hard-coded reduction in specular for hair
                        Alpha = new Color4(normal.Alpha)
                    };
                };
            }
            else if (shaderPack == EShaderPack.CharacterTattoo)
            {
                var tattooColor = (Color4)colors.TattooColor;
                // Very similar to hair.shpk but without an extra texture
                return (Color4 diffuse, Color4 normal, Color4 multi) => {
                    float tattooInfluence = normal.Blue;
                    diffuse = Color4.Lerp(diffuse, tattooColor, tattooInfluence);
                    return new ShaderMapperResult()
                    {
                        Diffuse = diffuse,
                        Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                        Specular = new Color4(0.0f), // ?
                        Alpha = new Color4(normal.Alpha)
                    };
                };
            }
            else if (shaderPack == EShaderPack.CharacterOcclusion)
            {
                // Not sure how this should be rendered if at all, so its fully transparent
                return (Color4 diffuse, Color4 normal, Color4 multi) => {
                    return new ShaderMapperResult()
                    {
                        Diffuse = Color4.White,
                        Normal = new Color4(0.5f, 0.5f, 1.0f, 1.0f),
                        Specular = new Color4(0.0f),
                        Alpha = new Color4(0.0f)
                    };
                };
            }
            else if (shaderPack == EShaderPack.Iris)
            {
                var irisColor = (Color4)colors.EyeColor;

                return (Color4 diffuse, Color4 normal, Color4 multi) => {
                    float colorInfluence = multi.Blue;
                    diffuse = Color4.Lerp(diffuse, diffuse * irisColor, colorInfluence);
                    // This map covers everything except the pupil, so use it to disable specular there
                    float spec = 1.0f - multi.Blue;
                    return new ShaderMapperResult()
                    {
                        Diffuse = diffuse,
                        Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                        Specular = new Color4(spec, spec, spec, 1.0f),
                        Alpha = new Color4(1.0f)
                    };
                };
            }
            else if (mtrl.ShaderPack == EShaderPack.Furniture || mtrl.ShaderPack == EShaderPack.Prop)
            {
                return (Color4 diffuse, Color4 normal, Color4 multi) => {
                    return new ShaderMapperResult()
                    {
                        Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, 1.0f),
                        Normal = new Color4(normal.Red, normal.Green, diffuse.Blue, 1.0f),
                        Specular = new Color4(multi.Green, multi.Green, multi.Green, 1.0f),
                        Alpha = new Color4(normal.Blue)
                    };
                };
            }
            else if (mtrl.ShaderPack == EShaderPack.DyeableFurniture)
            {
                Color4 furnitureColor = colors.FurnitureColor;
                return (Color4 diffuse, Color4 normal, Color4 multi) => {
                    float colorInfluence = diffuse.Alpha;
                    return new ShaderMapperResult()
                    {
                        Diffuse = Color4.Lerp(new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, 1.0f), furnitureColor, colorInfluence),
                        Normal = new Color4(normal.Red, normal.Green, diffuse.Blue, 1.0f),
                        Specular = new Color4(multi.Green, multi.Green, multi.Green, 1.0f),
                        Alpha = new Color4(normal.Blue)
                    };
                };
            }
            else
            {
                return (Color4 diffuse, Color4 normal, Color4 multi) => {
                    return new ShaderMapperResult()
                    {
                        Diffuse = diffuse,
                        Normal = normal,
                        Specular = new Color4(0.0f),
                        Alpha = new Color4(1.0f)
                    };
                };
            }
        }

        private struct ColorSetMapperResult
        {
            public Color4 Diffuse;
            public Color4 Specular;
            public Color4 Emissive;
        }

        private delegate ColorSetMapperResult ColorSetMapperDelegate(int rowNumber, float blendAmount, Color4 diffuse, Color4 specular);

        private static ColorSetMapperDelegate GetColorSetMapper(ColorSetInfo colorSet, XivMtrl mtrl, int highlightRow = -1)
        {
            // No color set data, don't map color sets
            if (colorSet == null || mtrl.ColorSetData.Count == 0)
                return null;

            // Clone color set data to apply highlight coloring
            if (highlightRow >= 0)
            {
                int count = colorSet.Diffuse.Length;
                colorSet = new ColorSetInfo();

                // Initialized to black by default
                colorSet.Diffuse = new Color4[count];
                colorSet.Specular = new Color4[count];
                colorSet.Emissive = new Color4[count];

                colorSet.Diffuse[highlightRow] = Color4.White;
                colorSet.Specular[highlightRow] = Color4.White;
                colorSet.Emissive[highlightRow] = Color4.White;
            }

            return (int rowNumber, float blendAmount, Color4 diffuse, Color4 specular) =>
            {
                int nextRow = (rowNumber + 1) & 0x1F;

                Color4 diffuse1 = colorSet.Diffuse[rowNumber];
                Color4 diffuse2 = colorSet.Diffuse[nextRow];

                Color4 spec1 = colorSet.Specular[rowNumber];
                Color4 spec2 = colorSet.Specular[nextRow];

                Color4 emiss1 = colorSet.Emissive[rowNumber];
                Color4 emiss2 = colorSet.Emissive[nextRow];

                // These are now our base values to multiply the base values by.
                Color4 colorDiffuse = Color4.Lerp(diffuse1, diffuse2, blendAmount);
                Color4 colorSpecular = Color4.Lerp(spec1, spec2, blendAmount);
                Color4 colorEmissive = Color4.Lerp(emiss1, emiss2, blendAmount);

                return new ColorSetMapperResult()
                {
                    Diffuse = diffuse * colorDiffuse,
                    Specular = specular * colorSpecular,
                    Emissive = colorEmissive
                };
            };
        }

        private class TexMapData
        {
            public TexInfo Diffuse;
            public TexInfo Normal;
            public TexInfo Multi;
            public TexInfo Index;
            public ColorSetInfo ColorSet;
        }

        private class TexInfo
        {
            public int Width;
            public int Height;
            public byte[] Data;
        }

        private class ColorSetInfo
        {
            public Color4[] Diffuse;
            public Color4[] Specular;
            public Color4[] Emissive;
        }
    }


}
