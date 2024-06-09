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
using xivModdingFramework.Mods;
using SharpDX;
using System.Diagnostics;
using System.ComponentModel.Design;

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
        public static async Task<ModelTextureData> GetModelMaps(XivMtrl mtrl, CustomModelColors colors = null, int highlightedRow = -1, ModTransaction tx = null)
        {
            if (colors == null)
                colors = GetCustomColors();

            var texMapData = await GetTexMapData(mtrl, tx);
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

            var settings = new ShaderMapperSettings()
            {
                HighlightedRow = highlightedRow
            };

            var dataLength = normalPixels != null ? normalPixels.Length : diffusePixels.Length;
            var shaderFn = GetShaderMapper(GetCustomColors(), mtrl, settings);
            bool invertNormalGreen = colors.InvertNormalGreen;

            await Task.Run(() =>
            {
                Parallel.ForEach(Partitioner.Create(0, dataLength / 4), range =>
                {
                    for (int i = range.Item1 * 4; i < range.Item2 * 4; i += 4)
                    {
                        // Load the individual pixels into memory.
                        Color4 baseDiffuseColor = readInputPixel(diffusePixels, i, new Color4(1.0f, 1.0f, 1.0f, 1.0f));
                        Color4 baseNormalColor = readInputPixel(normalPixels, i, new Color4(0.5f, 0.5f, 1.0f, 1.0f));
                        Color4 baseMultiColor = readInputPixel(multiPixels, i, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
                        Color4 baseIndexColor = readInputPixel(indexPixels, i, new Color4(1.0f, 1.0f, 0.0f, 1.0f));

                        if (invertNormalGreen)
                            baseNormalColor.Green = 1.0f - baseNormalColor.Green;

                        var shaderResult = shaderFn(baseDiffuseColor, baseNormalColor, baseMultiColor, baseIndexColor);
                        Color4 diffuseColor = shaderResult.Diffuse;
                        Color4 normalColor = shaderResult.Normal;
                        Color4 specularColor = shaderResult.Specular;
                        Color4 alphaColor = shaderResult.Alpha;
                        Color4 emissiveColor = Color4.Black;

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
        private static async Task<TexMapData> GetTexMapData(XivMtrl mtrl, ModTransaction tx = null)
        {
            var texMapData = new TexMapData();

            // Use the function that returns proper sane reuslts.
            var ttps = mtrl.GetTextureTypePathList();

            var keepNormDummy = ttps.Count(x => x.Type == XivTexType.Normal) == 1;
            ttps.RemoveAll(x => {
                var isDummy = x.Path.StartsWith("bgcommon/texture/dummy_");
                if (!isDummy) return false;
                if (keepNormDummy && x.Path.EndsWith("_n.tex")) return false;
                return true;
            });

            // Decode compressed textures
            foreach (var ttp in ttps)
            {
                // Skip loading textures that aren't supported in model previews
                if (ttp.Type != XivTexType.Diffuse && ttp.Type != XivTexType.Specular && ttp.Type != XivTexType.Mask
                 && ttp.Type != XivTexType.Skin && ttp.Type != XivTexType.Normal && ttp.Type != XivTexType.Index)
                {
                    continue;
                }

                var texData = await Tex.GetXivTex(ttp.Path, false, tx);
                var imageData = await texData.GetRawPixels();

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

        private class ShaderMapperSettings
        {
            public bool UseTextures = true;
            public bool UseColorset = true;
            public bool VisualizeColorset = false;
            public int HighlightedRow = -1;
        }

        private delegate ShaderMapperResult ShaderMapperDelegate(Color4 diffuse, Color4 normal, Color4 mask, Color4 index);

#if DAWNTRAIL
        private static bool thrownException1 = false;

        private static ShaderMapperDelegate GetShaderMapper(CustomModelColors colors, XivMtrl mtrl, ShaderMapperSettings settings)
        {
            // Based on https://docs.google.com/spreadsheets/d/1iY4C6zSJ0K2vpBXNh5BLsTj6_7Ah-fHsznsC5xqMmaw

            var shaderPack = mtrl.ShaderPack;

            bool hasDiffuse = mtrl.GetTexture(XivTexType.Diffuse) != null;
            bool hasSpecular = mtrl.GetTexture(XivTexType.Specular) != null;
            bool hasMulti = mtrl.GetTexture(XivTexType.Mask) != null;

            // Arbitrary floor used for allowing non-metals to have specula reflections.
            const float metalFloor = 0.1f;

            bool useTextures = settings.UseTextures;
            bool useColorset = settings.UseColorset;
            bool visualizeColorset = settings.VisualizeColorset;
            var highlightRow = settings.HighlightedRow;

            var alphaMultiplier = 1.0f;

            // Alpha Threshold Constant
            var alphaConst = mtrl.ShaderConstants.FirstOrDefault(x => x.ConstantId == 699138595);
            if(alphaConst != null && alphaConst.Values[0] != 0)
            {
                alphaMultiplier = (float) (1.0f / alphaConst.Values[0]);
            }

            List<Half> colorset = null;
            if(mtrl.ColorSetData != null && mtrl.ColorSetData.Count >= 1024)
            {
                // Clone the list in case the data is accessed or changed while we're working.
                colorset = mtrl.ColorSetData.ToList();

                // TODO: If gamma adjustment on CSet needs to happen it should happen here.
            } else
            {
                useColorset = false;
                visualizeColorset = false;
            }

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

                if (hasMulti)
                {

                    return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                        

                        Color4 specular;
                        if (useTextures)
                        {
                            if (!hasDiffuse)
                            {
                                diffuse = new Color4(1.0f);
                            }


                            if (!hasSpecular)
                            {
                                var occlusion = new Color4(multi.Red, multi.Red, multi.Red, 1.0f);
                                diffuse *= occlusion;
                                specular = occlusion;

                                // Construct specular from mask
                                if (shaderPack == EShaderPack.CharacterLegacy)
                                {
                                    // Specular/Gloss flow
                                    var specPower = new Color4(multi.Green, multi.Green, multi.Green, 1.0f);
                                    var gloss = new Color4(multi.Blue, multi.Blue, multi.Blue, 1.0f);
                                    specular = occlusion * specPower * gloss;
                                }
                                else
                                {
                                    // Roughness/Metalness Flow
                                    var invRoughness = new Color4(1 - multi.Green, 1 - multi.Green, 1 - multi.Green, 1.0f);
                                    var metalness = new Color4(multi.Blue, multi.Blue, multi.Blue, 1.0f);
                                    metalness += new Color4(metalFloor);
                                    specular *= invRoughness * metalness;
                                }
                            } else
                            {
                                specular = multi;
                            }
                        } else
                        {
                            specular = new Color4(1.0f);
                            diffuse = new Color4(1.0f);
                        }

                        if (useColorset)
                        {
                            var row = GetColorsetRow(colorset, index[0], index[1], visualizeColorset, highlightRow);

                            var diffusePixel = new Color4(row[0], row[1], row[2], 1.0f);
                            var specPixel = new Color4(row[4], row[5], row[6], 1.0f);
                            var emissPixel = new Color4(row[8], row[9], row[10], 1.0f);

                            Color4 invRoughPixel;
                            float invRough = 0.5f;
                            if (shaderPack != EShaderPack.CharacterLegacy)
                            {
                                invRough = 1 - Math.Max(Math.Min(row[16], 1), 0);
                                var metalPixel = new Color4(row[18], row[18], row[18], 1.0f);
                                metalPixel += new Color4(metalFloor);
                                specular *= metalPixel;
                            } else
                            {
                                // Arbitrary estimation for SE-gloss to inverse roughness.
                                invRough = row[3] / 16;
                            }

                            invRoughPixel = new Color4(invRough, invRough, invRough, 1.0f);

                            diffuse *= diffusePixel;
                            specular *= invRoughPixel;
                            specular *= specPixel;
                        }

                        var alpha = normal.Blue * alphaMultiplier;

                        return new ShaderMapperResult()
                        {
                            Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, alpha),
                            Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                            Specular = specular,
                            Alpha = new Color4(alpha)
                        };
                    };
                }
                else if (hasSpecular) // "Multi" is actually a full specular map
                {

                    int multiAOChannel = shaderPack == EShaderPack.CharacterLegacy ? 0 : 2;
                    return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                        // Use AO as the diffuse if there is no diffuse texture
                        if (!hasDiffuse)
                            diffuse = new Color4(multi[multiAOChannel], multi[multiAOChannel], multi[multiAOChannel], 1.0f);



                        var alpha = normal.Blue * alphaMultiplier;
                        return new ShaderMapperResult()
                        {
                            Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, alpha),
                            Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                            Specular = multi,
                            Alpha = new Color4(alpha)
                        };
                    };
                }
                else // No mask or specular
                {
                    int multiAOChannel = shaderPack == EShaderPack.CharacterLegacy ? 0 : 2;
                    return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                        // Use AO as the diffuse if there is no diffuse texture
                        if (!hasDiffuse)
                            diffuse = new Color4(multi[multiAOChannel], multi[multiAOChannel], multi[multiAOChannel], 1.0f);

                        
                        var alpha = normal.Blue * alphaMultiplier;
                        return new ShaderMapperResult()
                        {
                            Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, alpha),
                            Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                            Specular = Color4.Black,
                            Alpha = new Color4(alpha)
                        };
                    };
                }
            }
            else if (shaderPack == EShaderPack.Skin)
            {
                var skinColor = (Color4)colors.SkinColor;
                var lipColor = (Color4)colors.LipColor;

                return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                    float skinInfluence = multi.Blue;
                    diffuse = Color4.Lerp(diffuse, diffuse * skinColor, skinInfluence);

                    float lipsInfluence = multi.Alpha;
                    diffuse = Color4.Lerp(diffuse, diffuse * lipColor, 1.0f - lipsInfluence);

                    var alpha = diffuse.Alpha * alphaMultiplier;
                    return new ShaderMapperResult()
                    {
                        Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, alpha),
                        Normal = new Color4(0.5f, 0.5f, 1.0f, 1.0f),
                        Specular = new Color4(multi.Red, multi.Red, multi.Red, 1.0f) * 0.25f, // Hard-coded reduction in specular for skin
                        Alpha = new Color4(alpha)
                    };
                };
            }
            else if (shaderPack == EShaderPack.SkinLegacy)
            {
                // Meaning of the skinlegacy Mask texture is the same as it was pre-Dawntrail
                var skinColor = (Color4)colors.SkinColor;
                var lipColor = (Color4)colors.LipColor;

                return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                    float skinInfluence = multi.Red;
                    diffuse = Color4.Lerp(diffuse, diffuse * skinColor, skinInfluence);

                    float lipsInfluence = multi.Blue;
                    diffuse = Color4.Lerp(diffuse, diffuse * lipColor, 1.0f - lipsInfluence);

                    var alpha = diffuse.Alpha * alphaMultiplier;
                    return new ShaderMapperResult()
                    {
                        Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, alpha),
                        Normal = new Color4(0.5f, 0.5f, 1.0f, 1.0f),
                        Specular = new Color4(multi.Green, multi.Green, multi.Green, 1.0f) * 0.25f, // Hard-coded reduction in specular for skin
                        Alpha = new Color4(alpha)
                    };
                };
            }
            else if (shaderPack == EShaderPack.Hair)
            {
                var hairColor = (Color4)colors.HairColor;
                var highlightColor = hairColor;

                if (colors.HairHighlightColor != null)
                    highlightColor = (Color4)colors.HairHighlightColor;

                return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                    float highlightInfluence = normal.Blue;

                    var occlusion = new Color4(multi.Alpha, multi.Alpha, multi.Alpha, 1.0f);
                    diffuse = Color4.Lerp(hairColor, hairColor * highlightColor, highlightInfluence);
                    diffuse = Color4.Modulate(diffuse,occlusion);

                    var spec = new Color4(multi.Red, multi.Red, multi.Red, 1.0f);
                    spec = Color4.Modulate(spec, occlusion);

                    var alpha = normal.Alpha * alphaMultiplier;
                    diffuse.Alpha = alpha;
                    return new ShaderMapperResult()
                    {
                        Diffuse = diffuse,
                        Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                        Specular = spec,
                        Alpha = new Color4(alpha)
                    };
                };
            }
            else if (shaderPack == EShaderPack.CharacterTattoo)
            {
                var tattooColor = (Color4)colors.TattooColor;
                // Very similar to hair.shpk but without an extra texture
                return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                    float tattooInfluence = normal.Blue;
                    diffuse = Color4.Lerp(diffuse, tattooColor, tattooInfluence);

                    var alpha = normal.Alpha * alphaMultiplier;
                    diffuse.Alpha = alpha;
                    return new ShaderMapperResult()
                    {
                        Diffuse = diffuse,
                        Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                        Specular = Color4.Black,
                        Alpha = new Color4(alpha)
                    };
                };
            }
            else if (shaderPack == EShaderPack.CharacterOcclusion)
            {
                // Not sure how this should be rendered if at all, so its fully transparent
                return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                    return new ShaderMapperResult()
                    {
                        Diffuse = Color4.White,
                        Normal = new Color4(0.5f, 0.5f, 1.0f, 1.0f),
                        Specular = Color4.Black,
                        Alpha = new Color4(0.0f)
                    };
                };
            }
            else if (shaderPack == EShaderPack.Iris)
            {
                var irisColor = (Color4)colors.EyeColor;

                return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
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
            else if (shaderPack == EShaderPack.Furniture || shaderPack == EShaderPack.Prop)
            {
                return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                    var alpha = normal.Alpha * alphaMultiplier;
                    return new ShaderMapperResult()
                    {
                        Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, alpha),
                        Normal = new Color4(normal.Red, normal.Green, 0.0f, 1.0f),
                        Specular = new Color4(multi.Green, multi.Green, multi.Green, 1.0f),
                        Alpha = new Color4(alpha)
                    };
                };
            }
            else if (shaderPack == EShaderPack.DyeableFurniture)
            {
                Color4 furnitureColor = colors.FurnitureColor;
                return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                    // TODO : Investigate Dyeable Furniture Rendering.


                    float colorInfluence = diffuse.Alpha;
                    var alpha = normal.Alpha * alphaMultiplier;


                    return new ShaderMapperResult()
                    {
                        Diffuse = Color4.Lerp(new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, alpha), furnitureColor, colorInfluence),
                        Normal = new Color4(normal.Red, normal.Green, 0.0f, 1.0f),
                        Specular = new Color4(multi.Green, multi.Green, multi.Green, 1.0f),
                        Alpha = new Color4(alpha)
                    };
                };
            }
            else
            {
                return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                    return new ShaderMapperResult()
                    {
                        Diffuse = diffuse,
                        Normal = normal,
                        Specular = Color4.Black,
                        Alpha = new Color4(1.0f)
                    };
                };
            }
        }
#endif

#if ENDWALKER
        private static ShaderMapperDelegate GetShaderMapper(CustomModelColors colors, XivMtrl mtrl, ShaderMapperSettings settings)
        {
            // Based on https://docs.google.com/spreadsheets/d/1kIKvVsW3fOnVeTi9iZlBDqJo6GWVn6K6BCUIRldEjhw
            // Although there is some overlap between this function and the DAWNTRAIL version,
            // there are not legacy equivalents of all of these shaders.

            // This var is technically defined in the Shaders parameters.
            // But we can use a constant copy of it for now, since it's largely non-changeable.
            const float PlayerColorMultiplier = 1.4f;
            const float BrightPlayerColorMultiplier = 3.0f;

            var shaderPack = mtrl.ShaderPack;

            bool hasDiffuse = mtrl.GetTexture(XivTexType.Diffuse) != null;
            bool hasSpecular = mtrl.GetTexture(XivTexType.Specular) != null;
            bool hasMulti = mtrl.GetTexture(XivTexType.Mask) != null;

            bool useTextures = settings.UseTextures;
            bool useColorset = settings.UseColorset;
            bool visualizeColorset = settings.VisualizeColorset;
            var highlightRow = settings.HighlightedRow;

            var alphaMultiplier = 1.0f;

            // Alpha Threshold Constant
            var alphaConst = mtrl.ShaderConstants.FirstOrDefault(x => x.ConstantId == 699138595);
            if (alphaConst != null && alphaConst.Values[0] != 0)
            {
                alphaMultiplier = (float)(1.0f / alphaConst.Values[0]);
            }

            List<Half> colorset = null;
            if (mtrl.ColorSetData != null && mtrl.ColorSetData.Count >= 256)
            {
                // Clone the list in case the data is accessed or changed while we're working.
                colorset = mtrl.ColorSetData.ToList();

                // TODO: If gamma adjustment on CSet needs to happen it should happen here.
            }
            else
            {
                useColorset = false;
                visualizeColorset = false;
            }


            if (shaderPack == EShaderPack.Character || shaderPack == EShaderPack.CharacterGlass)
            {
                if (hasMulti)
                {

                    return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {


                        Color4 specular;
                        if (useTextures)
                        {
                            if (!hasDiffuse)
                            {
                                diffuse = new Color4(1.0f);
                            }


                            if (!hasSpecular)
                            {
                                var occlusion = new Color4(multi.Red, multi.Red, multi.Red, 1.0f);
                                diffuse *= occlusion;
                                specular = occlusion;

                                // Specular/Gloss flow
                                var specPower = new Color4(multi.Green, multi.Green, multi.Green, 1.0f);
                                var gloss = new Color4(multi.Blue, multi.Blue, multi.Blue, 1.0f);
                                specular = occlusion * specPower * gloss;
                            }
                            else
                            {
                                specular = multi;
                            }
                        }
                        else
                        {
                            specular = new Color4(1.0f);
                            diffuse = new Color4(1.0f);
                        }

                        if (useColorset)
                        {
                            var row = GetColorsetRow(colorset, normal[3], 0.0f, visualizeColorset, highlightRow);

                            var diffusePixel = new Color4(row[0], row[1], row[2], 1.0f);
                            var specPixel = new Color4(row[4], row[5], row[6], 1.0f);
                            var emissPixel = new Color4(row[8], row[9], row[10], 1.0f);

                            Color4 invRoughPixel;
                            float invRough = 0.5f;
                            // Arbitrary estimation for SE-gloss to inverse roughness.
                            invRough = row[7] / 32;

                            invRoughPixel = new Color4(invRough, invRough, invRough, 1.0f);

                            diffuse *= diffusePixel;
                            specular *= invRoughPixel;
                            specular *= specPixel;
                        }

                        var alpha = normal.Blue * alphaMultiplier;

                        return new ShaderMapperResult()
                        {
                            Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, alpha),
                            Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                            Specular = specular,
                            Alpha = new Color4(alpha)
                        };
                    };
                }
                else if (hasSpecular) // "Multi" is actually a full specular map
                {

                    int multiAOChannel = shaderPack == EShaderPack.CharacterLegacy ? 0 : 2;
                    return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                        // Use AO as the diffuse if there is no diffuse texture
                        if (!hasDiffuse)
                            diffuse = new Color4(multi[multiAOChannel], multi[multiAOChannel], multi[multiAOChannel], 1.0f);



                        var alpha = normal.Blue * alphaMultiplier;
                        return new ShaderMapperResult()
                        {
                            Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, alpha),
                            Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                            Specular = multi,
                            Alpha = new Color4(alpha)
                        };
                    };
                }
                else // No mask or specular
                {
                    int multiAOChannel = shaderPack == EShaderPack.CharacterLegacy ? 0 : 2;
                    return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                        // Use AO as the diffuse if there is no diffuse texture
                        if (!hasDiffuse)
                            diffuse = new Color4(multi[multiAOChannel], multi[multiAOChannel], multi[multiAOChannel], 1.0f);


                        var alpha = normal.Blue * alphaMultiplier;
                        return new ShaderMapperResult()
                        {
                            Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, alpha),
                            Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                            Specular = Color4.Black,
                            Alpha = new Color4(alpha)
                        };
                    };
                }
            }
            else if (shaderPack == EShaderPack.Furniture || mtrl.ShaderPack == EShaderPack.Prop)
            {
                return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                    return new ShaderMapperResult()
                    {
                        Diffuse = diffuse,
                        Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                        Specular = hasMulti ? new Color4(multi.Green, multi.Green, multi.Green, 1.0f) : Color4.Black,
                        Alpha = new Color4(diffuse.Alpha)
                    };
                };
            }
            else if (shaderPack == EShaderPack.DyeableFurniture)
            {
                Color4 furnitureColor = colors.FurnitureColor;
                return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                    float colorInfluence = diffuse.Alpha;
                    return new ShaderMapperResult()
                    {
                        Diffuse = Color4.Lerp(new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, 1.0f), furnitureColor, colorInfluence),
                        Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                        Specular = hasMulti ? new Color4(multi.Green, multi.Green, multi.Green, 1.0f) : Color4.Black,
                        Alpha = new Color4(1.0f)
                    };
                };
            }
            else if (shaderPack == EShaderPack.Skin)
            {
                var skinColor = colors.SkinColor;
                var lipColor = colors.LipColor;

                ShaderMapperDelegate skinShader = (Color4 diffuse, Color4 normal, Color4 specular, Color4 index) => {
                    Color4 newNormal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f);
                    Color4 newSpecular = new Color4(specular.Green, specular.Green, specular.Green, 1.0f);
                    // This is an arbitrary number.  There's likely some value in the shader params for skin that
                    // tones down the specularity here, but without it the skin is hyper reflective.
                    newSpecular = Color4.Scale(newSpecular, 0.25f);
                    // New diffuse starts from regular diffuse file.
                    // Then factors in the player's skin color multiplied by the shader value.
                    float skinInfluence = specular.Red;
                    var coloredSkin = diffuse * skinColor;
                    Color4 newDiffuse = Color4.Lerp(diffuse, coloredSkin, skinInfluence);
                    return new ShaderMapperResult()
                    {
                        Diffuse = newDiffuse,
                        Normal = newNormal,
                        Specular = newSpecular,
                        Alpha = new Color4(1.0f)
                    };
                };

                ShaderMapperDelegate faceShader = (Color4 diffuse, Color4 normal, Color4 specular, Color4 index) => {
                    ShaderMapperResult result = skinShader(diffuse, normal, specular, index);
                    // Face shaders also allow for lip color.
                    var coloredLip = diffuse * lipColor;
                    float lipInfluence = specular.Blue;
                    result.Diffuse = Color4.Lerp(result.Diffuse, coloredLip, lipInfluence);
                    // For lipstick, increase the specular value slightly.
                    float specAmp = 1.0f + (lipInfluence * 0.25f);
                    result.Specular = result.Specular * specAmp;
                    // Face shader supports alpha, unlike normal skin textures.
                    result.Alpha = new Color4(normal.Blue);
                    return result;
                };

                if (mtrl.ShaderKeys.Any(x => x.KeyId == 0xF52CCF05 && x.Value == 0xA7D2FF60)) // Face
                    return faceShader;
                else
                    return skinShader;
            }
            else if (shaderPack == EShaderPack.Hair)
            {
                var hairHighlightColor = (Color)(colors.HairHighlightColor != null ? colors.HairHighlightColor : colors.HairColor);
                var hairTargetColor = (Color)(colors.HairHighlightColor != null ? colors.HairHighlightColor : colors.HairColor);

                // Starting from the original hair color...
                var baseColor = colors.HairColor;

                // Hair highlight color if available.
                // But wait! If we're actually a tattoo preset, that changes instead to tattoo color.
                Color4 targetColor;

                if (mtrl.ShaderKeys.Any(x => x.KeyId == 0x24826489 && x.Value == 0x6E5B8F10)) // Face
                    targetColor = colors.TattooColor;
                else if (mtrl.ShaderConstants.Any(x => x.ConstantId == 0x2C2A34DD && x.Values[0] == 3.0f)) // Limbal ring brightness
                    // Multiplier here is 3.0 instead of 1.4
                    targetColor = Color4.Scale(colors.TattooColor, BrightPlayerColorMultiplier / PlayerColorMultiplier);
                else
                    targetColor = hairHighlightColor;

                return (Color4 diffuse, Color4 normal, Color4 specular, Color4 index) => {
                    Color4 newNormal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f);
                    Color4 newSpecular = new Color4(specular.Green, specular.Green, specular.Green, 1.0f);
                    // This is an arbitrary number.  There's likely some value in the shader params for skin that
                    // tones down the specularity here, but without it the skin is hyper reflective.
                    newSpecular = Color4.Scale(newSpecular, 0.25f);
                    // The influence here determines which base color we use.
                    float influenceStrength = specular.Alpha;
                    Color4 newDiffuse = Color4.Lerp(baseColor, targetColor, influenceStrength);
                    newDiffuse = Color4.Scale(newDiffuse, specular.Red);
                    return new ShaderMapperResult()
                    {
                        Diffuse = newDiffuse,
                        Normal = newNormal,
                        Specular = newSpecular,
                        Alpha = new Color4(normal.Alpha)
                    };
                };
            }
            else if (shaderPack == EShaderPack.Iris)
            {
                return (Color4 diffuse, Color4 normal, Color4 specular, Color4 index) =>
                {
                    return new ShaderMapperResult()
                    {
                        Diffuse = Color4.Scale(colors.EyeColor, specular.Red),
                        Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                        Specular = new Color4(specular.Green, specular.Green, specular.Green, 1.0f),
                        Alpha = new Color4(normal.Alpha)
                    };
                };
            }
            else
            {
                return (Color4 diffuse, Color4 normal, Color4 specular, Color4 index) => {
                    return new ShaderMapperResult()
                    {
                        Diffuse = diffuse,
                        Normal = normal,
                        Specular = Color4.Black,
                        Alpha = new Color4(1.0f)
                    };
                };
            }
        }
#endif



        /// <summary>
        /// Retrieves the blended colorset row from the colorset data.
        /// </summary>
        /// <param name="colorsetData"></param>
        /// <param name="rowNumber"></param>
        /// <returns></returns>
        public static Half[] GetColorsetRow(List<Half> colorsetData, float indexRed, float indexGreen, bool visualizeOnly = false, int highlightRow = -1)
        {
            if(colorsetData == null || colorsetData.Count == 0) {
                return null;
            }

#if DAWNTRAIL
            const int _Width = 8;
            const int _Height = 32;
            const int _RowsPerBlend = 2;
#else
            const int _Width = 4;
            const int _Height = 16;
            const int _RowsPerBlend = 1;
#endif
            const int _PerPixel = 4;
            const int _RowSize = _Width * _PerPixel;

            var values = ReadColorIndex(indexRed, indexGreen);


            var row0Offset = _RowSize * (values.RowId * _RowsPerBlend);
            var row1Offset = 0;

            if((values.RowId * _RowsPerBlend + 1)< _Height)
            {
                row1Offset = _RowSize * ((values.RowId * _RowsPerBlend) + 1);
            }
            else
            {
                row1Offset = row0Offset;
            }


            var rowData = new Half[_RowSize];
            if (visualizeOnly)
            {
                for(int i = 0; i < 3; i++) { 
                    // Render the colorset as a continuous 0.0 -> 1.0 color on the diffuse.
                    rowData[i] = ((values.RowId + values.Blend) / 15.0f);
                }
            }
            else
            {
                for (int i = 0; i < _RowSize; i++)
                {
                    rowData[i] = LerpHalf(colorsetData[row0Offset + i], colorsetData[row1Offset + i], values.Blend);
                }
            }
            
            if(highlightRow >= 0)
            {
                var baseRow = highlightRow / 2;
                if(values.RowId == baseRow)
                {
                    var blend = values.Blend;
                    if (highlightRow % 2 == 1)
                    {
                        blend = 1 - blend;
                    }

                    rowData[0] = blend;
                    rowData[1] = blend;
                    rowData[2] = blend;

                } else
                {
                    // Mute the colors on non-selected rows.
                    rowData[0] *= 0.1f;
                    rowData[1] *= 0.1f;
                    rowData[2] *= 0.1f;
                }
                rowData[4] = 0f;
                rowData[5] = 0f;
                rowData[6] = 0f;
                rowData[8] = 0f;
                rowData[9] = 0f;
                rowData[10] = 0f;

            }

            return rowData;
        }



        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half LerpHalf(Half a, Half b, float f)
        {
            return a * (1.0f - f) + (b * f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (int RowId, float Blend) ReadColorIndex(float indexRed, float indexGreen)
        {
            int rowNumber = (int)(indexRed * 15);

#if DAWNTRAIL
            float blendAmount = 1.0f - indexGreen;
#else
            float blendAmount  = indexRed % (1.0f / 15.0f);
#endif

            return (rowNumber, blendAmount);
        }

        private class TexMapData
        {
            public TexInfo Diffuse;
            public TexInfo Normal;
            public TexInfo Multi;
            public TexInfo Index;
        }

        private class TexInfo
        {
            public int Width;
            public int Height;
            public byte[] Data;
        }

    }


}
