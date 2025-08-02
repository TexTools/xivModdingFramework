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
using xivModdingFramework.Textures;
using System.Text.RegularExpressions;
using System.ComponentModel;
using SharpDX.Direct3D11;

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
        public Color LightColor;

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

            SkinColor = new Color(219, 184, 154, 255);
            EyeColor = new Color(172, 113, 159, 255);
            LipColor = new Color(139, 55, 46, 153);
            HairColor = new Color(110, 77, 35, 255);
            LightColor = new Color(0, 0, 0, 255);
            HairHighlightColor = new Color(91, 110, 129, 255);
            TattooColor = new Color(48, 112, 102, 255);
            FurnitureColor = new Color(141, 60, 204, 255);

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
        public const float _ColorsetMul = 17.0f;

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
        private static void EncodeColorBytes(byte[] buf, int offset, Color4 c, bool srgb = true)
        {

            if (srgb)
            {
                buf[offset] = (byte)(Clamp((float)Math.Sqrt(c.Red)) * 255.0f);
                buf[offset + 1] = (byte)(Clamp((float)Math.Sqrt(c.Green)) * 255.0f);
                buf[offset + 2] = (byte)(Clamp((float)Math.Sqrt(c.Blue)) * 255.0f);
                buf[offset + 3] = (byte)(Clamp((float)Math.Sqrt(c.Alpha)) * 255.0f);
            } else
            {
                buf[offset] = (byte)(Clamp(c.Red) * 255f);
                buf[offset + 1] = (byte)(Clamp(c.Green) * 255f);
                buf[offset + 2] = (byte)(Clamp(c.Blue) * 255f);
                buf[offset + 3] = (byte)(Clamp(c.Alpha) * 255f);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Color4 ReadInputPixel(byte[] pixels, int i, Color4 color, bool srgb = true)
        {
            if (pixels != null)
            {
                var red     = pixels[i + 0] / 255.0f;
                var green   = pixels[i + 1] / 255.0f;
                var blue    = pixels[i + 2] / 255.0f;
                var alpha   = pixels[i + 3] / 255.0f;
                if (srgb)
                {
                    color = new Color4(red * red, green * green, blue * blue, alpha);
                }
                else
                {
                    color = new Color4(red, green, blue, alpha);
                }

            }
            return color;
        }


        public static Color4 GetLinearColor(Color color, bool convertFromSrgb = true)
        {
            var red = color[0] / 255.0f;
            var green = color[1] / 255.0f;
            var blue = color[2] / 255.0f;
            var alpha = color[3] / 255.0f;
            if (convertFromSrgb)
            {
                return new Color4(red * red, green * green, blue * blue, alpha);
            }
            else
            {
                return new Color4(red, green, blue, alpha);
            }
        }

        /// <summary>
        /// Gets the texture maps for the model
        /// </summary>
        /// <returns>The texture maps in byte arrays inside a ModelTextureData class</returns>
        public static async Task<ModelTextureData> GetModelMaps(XivMtrl mtrl, bool pbrMaps = false, CustomModelColors colors = null, int highlightedRow = -1, ModTransaction tx = null)
        {
            if (colors == null)
                colors = GetCustomColors();

            var limitTextureSize = !pbrMaps;
            var texMapData = await GetTexMapData(mtrl, tx);
            var dimensions = await EqualizeTextureSizes(texMapData, limitTextureSize);

            var diffusePixels = texMapData.Diffuse?.Data;
            var normalPixels = texMapData.Normal?.Data;
            var multiPixels = texMapData.Mask?.Data;
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

                if (pbrMaps)
                {
                    empty.Roughness = new byte[0];
                    empty.Metalness = new byte[0];
                    empty.Occlusion = new byte[0];
                    empty.Subsurface = new byte[0];
                }
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
                Alpha = new byte[dimensions.Width * dimensions.Height],
                MaterialPath = mtrl.MTRLPath.Substring(mtrl.MTRLPath.LastIndexOf('/'))
            };

            if (pbrMaps)
            {
                result.Roughness = new byte[dimensions.Width * dimensions.Height];
                result.Metalness = new byte[dimensions.Width * dimensions.Height];
                result.Occlusion = new byte[dimensions.Width * dimensions.Height];
                result.Subsurface = new byte[dimensions.Width * dimensions.Height];
            }

            if((mtrl.MaterialFlags & EMaterialFlags1.HideBackfaces) == 0)
            {
                result.RenderBackfaces = true;
            }

            if (mtrl.Textures.Count > 0)
            {
                result.UTilingMode = mtrl.Textures[0].Sampler.UTilingMode;
                result.VTilingMode = mtrl.Textures[0].Sampler.VTilingMode;
            }

            var settings = new ShaderMapperSettings()
            {
                HighlightedRow = highlightedRow,
                GeneratePbrMaps = pbrMaps,
                //VisualizeColorset = true,
                //UseColorset = false,
            };


            var dataLength = normalPixels != null ? normalPixels.Length : diffusePixels.Length;
            var shaderFn = GetShaderMapper(colors, mtrl, settings);
            bool invertNormalGreen = colors.InvertNormalGreen;

            await Task.Run(() =>
            {
                Parallel.ForEach(Partitioner.Create(0, dataLength / 4), range =>
                {
                    for (int i = range.Item1 * 4; i < range.Item2 * 4; i += 4)
                    {
                        // Load the individual pixels into memory.
                        Color4 baseDiffuseColor = ReadInputPixel(diffusePixels, i, new Color4(1.0f, 1.0f, 1.0f, 1.0f));
                        Color4 baseNormalColor = ReadInputPixel(normalPixels, i, new Color4(0.5f, 0.5f, 1.0f, 1.0f), false);
                        Color4 baseIndexColor = ReadInputPixel(indexPixels, i, new Color4(1.0f, 1.0f, 0.0f, 1.0f), false);

                        // Sometimes Mask data is used as sRGB, and Sometimes Linear.
                        // On average it's more commonly used as sRGB, but it's important to keep in mind that some
                        // things will need to sqrt() the pixel data of individual channels in certain cases.
                        Color4 baseMultiColor = ReadInputPixel(multiPixels, i, new Color4(1.0f, 1.0f, 1.0f, 1.0f));

                        if (invertNormalGreen)
                            baseNormalColor.Green = 1.0f - baseNormalColor.Green;

                        var shaderResult = shaderFn(baseDiffuseColor, baseNormalColor, baseMultiColor, baseIndexColor);
                        Color4 diffuseColor = shaderResult.Diffuse;
                        Color4 normalColor = shaderResult.Normal;
                        Color4 specularColor = shaderResult.Specular;
                        Color4 emissiveColor = shaderResult.Emissive;

                        // White out the opacity channels where appropriate.
                        specularColor.Alpha = 1.0f;
                        normalColor.Alpha = 1.0f;
                        
                        // Copy alpha to diffuse for Blender.
                        diffuseColor.Alpha = shaderResult.Alpha;

                        EncodeColorBytes(result.Diffuse, i, diffuseColor);
                        EncodeColorBytes(result.Normal, i, normalColor, false);
                        EncodeColorBytes(result.Specular, i, specularColor);
                        EncodeColorBytes(result.Emissive, i, emissiveColor);

                        result.Alpha[i/4] = (byte)(shaderResult.Alpha * 255.0f);

                        if (pbrMaps)
                        {
                            result.Roughness[i / 4] = (byte)(shaderResult.Roughness * 255.0f);
                            result.Metalness[i / 4] = (byte)(shaderResult.Metalness * 255.0f);
                            result.Occlusion[i / 4] = (byte)(shaderResult.Occlusion * 255.0f);
                            result.Subsurface[i / 4] = (byte)(shaderResult.Subsurface * 255.0f);
                        }

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
            var textures = mtrl.Textures;

            var dummyRegex = new Regex("bgcommon\\/texture\\/.*dummy.*\\.tex");
            textures.RemoveAll(x => string.IsNullOrWhiteSpace(x.Dx11Path) || dummyRegex.IsMatch(x.Dx11Path));


            // Decode compressed textures
            foreach (var tex in textures)
            {
                var type = mtrl.ResolveFullUsage(tex);
                // Skip loading textures that aren't supported in model previews
                if (type != XivTexType.Diffuse && type != XivTexType.Specular && type != XivTexType.Mask
                 && type != XivTexType.Skin && type != XivTexType.Normal && type != XivTexType.Index)
                {
                    continue;
                }

                if (tex.Sampler.SamplerId == ESamplerId.g_SamplerSpecularMap1
                    || tex.Sampler.SamplerId == ESamplerId.g_SamplerColorMap1
                    || tex.Sampler.SamplerId == ESamplerId.g_SamplerNormalMap1
                    || tex.Sampler.SamplerId == ESamplerId.g_SamplerWaveMap1)
                {
                    // Uv2 samplers
                    continue;
                }

                var texData = await Tex.GetXivTex(tex.Dx11Path, false, tx);
                var imageData = await texData.GetRawPixels();

                switch (type)
                {
                    case XivTexType.Diffuse:
                        if(tex.Sampler.SamplerId == ESamplerId.g_SamplerColorMap1)
                        {
                            // Uv2 sampler;
                            continue;
                        }

                        texMapData.Diffuse = new TexInfo { Width = texData.Width, Height = texData.Height, Data = imageData };
                        break;
                    case XivTexType.Specular:
                    case XivTexType.Mask:
                    case XivTexType.Skin:
                        if (texMapData.Mask != null)
                        {
                            continue;
                        }

                        texMapData.Mask = new TexInfo { Width = texData.Width, Height = texData.Height, Data = imageData };
                        break;
                    case XivTexType.Normal:
                        if (texMapData.Normal != null)
                        {
                            continue;
                        }
                        texMapData.Normal = new TexInfo { Width = texData.Width, Height = texData.Height, Data = imageData };
                        break;
                    case XivTexType.Index:
                        if (texMapData.Index != null)
                        {
                            continue;
                        }
                        texMapData.Index = new TexInfo { Width = texData.Width, Height = texData.Height, Data = imageData };
                        break;
                }
            }

            return texMapData;
        }

        private static void ResizeTexture(TexInfo texInfo, int width, int height, bool nearestNeighbor = false)
        {
            using var img = Image.LoadPixelData<Rgba32>(texInfo.Data, texInfo.Width, texInfo.Height);

            var options = new ResizeOptions
                {
                    Size = new Size(width, height),
                    PremultiplyAlpha = false,
                    Mode = ResizeMode.Stretch,
            };
            if (nearestNeighbor)
            {
                options.Sampler = KnownResamplers.NearestNeighbor;
            }
            // ImageSharp pre-multiplies the RGB by the alpha component during resize, if alpha is 0 (colourset row 0)
            // this ends up causing issues and destroying the RGB values resulting in an invisible preview model
            // https://github.com/SixLabors/ImageSharp/issues/1498#issuecomment-757519563
            img.Mutate(x => x.Resize(options));

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
        private static async Task<(int Width, int Height)> EqualizeTextureSizes(TexMapData texMapData, bool limitSize = true)
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

            if (texMapData.Mask != null)
            {
                var size = texMapData.Mask.Width * texMapData.Mask.Height;

                if (size > largestSize)
                {
                    largestSize = size;
                    width = texMapData.Mask.Width;
                    height = texMapData.Mask.Height;
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

            if ((width > 4000 || height > 4000) && limitSize)
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
                    if (texMapData.Mask != null && (largestSize > texMapData.Mask.Width * texMapData.Mask.Height || scaleDown))
                        ResizeTexture(texMapData.Mask, width, height);
                }, () => {
                    if (texMapData.Index != null && (largestSize > texMapData.Index.Width * texMapData.Index.Height || scaleDown))
                        ResizeTexture(texMapData.Index, width, height, true);
                });
            });

            return (width, height);
        }

        private struct ShaderMapperResult
        {
            public Color4 Diffuse;
            public Color4 Normal;
            public Color4 Specular;
            public Color4 Emissive;
            public float Alpha;
            public float Roughness;
            public float Metalness;
            public float Occlusion;
            public float Subsurface;
        }

        private class ShaderMapperSettings
        {
            public bool UseTextures = true;
            public bool UseColorset = true;
            public bool VisualizeColorset = false;
            public int HighlightedRow = -1;
            public bool GeneratePbrMaps;
        }

        private delegate ShaderMapperResult ShaderMapperDelegate(Color4 diffuse, Color4 normal, Color4 mask, Color4 index);

        private static bool thrownException1 = false;

        private static ShaderMapperDelegate GetShaderMapper(CustomModelColors colors, XivMtrl mtrl, ShaderMapperSettings settings)
        {
            // Based on https://docs.google.com/spreadsheets/d/1iY4C6zSJ0K2vpBXNh5BLsTj6_7Ah-fHsznsC5xqMmaw

            var shaderPack = mtrl.ShaderPack;

            bool hasDiffuse = mtrl.GetTexture(XivTexType.Diffuse) != null;
            bool hasSpecular = mtrl.GetTexture(XivTexType.Specular) != null;
            bool hasMulti = mtrl.GetTexture(XivTexType.Mask) != null;

            bool allowTranslucency = (mtrl.MaterialFlags & EMaterialFlags1.EnableTranslucency) != 0;

            // Arbitrary floor used for allowing non-metals to have specular reflections.
            const float _MetalFloor = 0.1f;

            // Arbitrary multplier used to enhance metal specular strength.
            const float _MetalMultiplier = 1.5f;

            // How specularly reflective eyes should be.
            var _EyeSpecular = new Color4(_MetalFloor * 2, _MetalFloor * 2, _MetalFloor * 2, 1.0f);

            // Arbitrary multiplier used to reduce hair shininess.
            var _HairSpecMultiplier = new Color4(_MetalFloor, _MetalFloor, _MetalFloor, 1.0f);

            // Arbitrary multiplier used to reduce skin shininess.
            var _SkinSpecMultiplier = new Color4(_MetalFloor, _MetalFloor, _MetalFloor, 1.0f);

            // Arbitrary color to use for charactertattoo.shpk base color.
            // No clue where it comes from in game.  It is used for moles and the Keeper facial option forehead tear thing.
            var _MoleColor = SrgbToLinear(new Color4( 56 / 255f, 24 / 255f, 8 / 255f, 1.0f));

            // Arbitrary multiplier that darkens the body fur to loosely match most hairs.
            // This is caused by hair having an extra diffuse mask/multiplier that skin lacks.
            // I have no idea how SE resolves this on their end.  Possibly material var or reuse of another map.
            var _BodyFurMultiplier = new Color4(0.4f, 0.4f, 0.4f, 1.0f);


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
            } else if (alphaConst != null && alphaConst.Values[0] == 0)
            {
                alphaMultiplier = 255;
            }


            // Color constants
            var diffuseColorMul = GetConstColor(mtrl, 0x2C2A34DD, new Color4(1.0f));
            var specularColorMul = GetConstColor(mtrl, 0x141722D5, new Color4(1.0f));
            var emissiveColorMul = GetConstColor(mtrl, 0x38A64362, new Color4(0,0,0, 1.0f));

            // Convert from SRGB to linear color space.
            diffuseColorMul *= diffuseColorMul;
            specularColorMul *= specularColorMul;
            emissiveColorMul *= emissiveColorMul;

            List<SharpDX.Half> colorset = null;
            if(mtrl.ColorSetData != null && mtrl.ColorSetData.Count >= 1024)
            {
                // Clone the list in case the data is accessed or changed while we're working.
                colorset = mtrl.ColorSetData.ToList();
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

                // Default 1.0 emissive color for these, since it gets nuked by colorset typically.
                emissiveColorMul = GetConstColor(mtrl, 0x38A64362, new Color4(1, 1, 1, 1.0f));

                return (Color4 diffuse, Color4 normal, Color4 mask, Color4 index) => {
                    Color4 specular = new Color4(1.0f);
                    var roughness = 0.0f;
                    var metalness = 0.0f;
                    var occlusion = 1.0f;

                    if (useTextures)
                    {
                        if (!hasDiffuse)
                        {
                            diffuse = new Color4(1.0f);
                        }


                        if (hasMulti)
                        {
                            float diffuseMask, specMask;
                            // Construct specular from mask
                            if (shaderPack == EShaderPack.CharacterLegacy)
                            {
                                // Specular/Gloss flow

                                diffuseMask = mask.Blue;
                                specMask = mask.Red;
                                roughness = 1 - mask.Green;

                                // Kind of jank, but works.
                                metalness = 1 - diffuseMask;
                                //metalness = 1 - multi.Blue;

                            }
                            else
                            {
                                diffuseMask = mask.Blue;
                                specMask = mask.Red;
                                roughness = mask.Green;
                            }

                            if (!settings.GeneratePbrMaps)
                            {
                                diffuse *= diffuseMask;
                            }
                            else
                            {
                                occlusion = diffuseMask;
                            }

                            specular *= specMask;

                        } else if(hasSpecular)
                        {
                            specular = mask;
                        } else
                        {
                            // ???
                            specular = new Color4(0.1f, 0.1f, 0.1f, 1.0f);
                        }

                    } else
                    {
                        specular = new Color4(1.0f);
                        diffuse = new Color4(1.0f);
                    }

                    var alpha = normal.Blue;
                    var emissive = new Color4(0, 0, 0, 1.0f);
                    if (useColorset)
                    {
                        var row = GetColorsetRow(colorset, index[0], index[1], visualizeColorset, highlightRow);

                        var diffusePixel = new Color4(row[0], row[1], row[2], 1.0f);
                        var specPixel = new Color4(row[4], row[5], row[6], 1.0f);
                        var emissPixel = new Color4(row[8], row[9], row[10], 1.0f);
                        emissive = emissPixel;

                        emissive *= emissiveColorMul;

                        Color4 invRoughPixel;
                        float invRough = 0.5f;
                        if (shaderPack != EShaderPack.CharacterLegacy)
                        {
                            var roughPixel = Math.Max(Math.Min(row[16], 1), 0);

                            // Apply rough pixel by screen blending mode.
                            roughness = 1 - ((1 - roughness) * (1 - roughPixel));

                            metalness = row[18];
                            //var metalPixel = new Color4(row[18], row[18], row[18], 1.0f);
                        }
                        else
                        {
                            // Arbitrary estimation for SE-gloss to inverse roughness.
                            var roughPixel = (1 - row[3] / 16);
                            roughness = 1 - ((1 - roughness) * (1 - roughPixel));
                        }

                        if (!settings.GeneratePbrMaps)
                        {
                            // Some semi-arbitrary math to loosely simulate metalness in our bad spec-diffuse system.
                            specular *= _MetalFloor + (metalness * _MetalMultiplier);

                            if (mtrl.ShaderPack != EShaderPack.CharacterLegacy)
                            {
                                // As metalness rises, the diffuse/specular colors merge.
                                diffusePixel = Color4.Lerp(diffusePixel, diffusePixel * specPixel, metalness);
                                specPixel = Color4.Lerp(specPixel, diffusePixel * specPixel, metalness);
                            }
                        }
                        else if(mtrl.ShaderPack == EShaderPack.CharacterLegacy)
                        {
                            metalness = 0;
                        }

                        diffuse *= diffusePixel;
                        specular *= specPixel;

                        if (mtrl.ShaderPack == EShaderPack.CharacterLegacy && !settings.GeneratePbrMaps)
                        {
                            // As metalness rises, the diffuse becomes specular.
                            diffuse = Color4.Lerp(diffuse, specular, metalness);
                        }
                    }

                    if (!settings.GeneratePbrMaps)
                    {
                        var invRough = 1 - roughness;
                        specular *= invRough;
                    }

                    alpha *= alphaMultiplier;
                    alpha = allowTranslucency ? alpha : (alpha < 1 ? 0 : 1);

                    if(highlightRow >= 0)
                    {
                        alpha = 1.0f;
                    }

                    return new ShaderMapperResult()
                    {
                        Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, alpha),
                        Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                        Specular = specular,
                        Alpha = alpha,
                        Emissive = emissive,
                        Roughness = roughness,
                        Metalness = metalness,
                        Occlusion = occlusion,
                    };
                };
            }
            else if (shaderPack == EShaderPack.Skin)
            {
                var skinColor = colors.SkinColor;
                var bonusColor = GetSkinBonusColor(mtrl, colors);
                var highlightColor = GetSkinBonusColor2(mtrl, colors);
                var metalnessConst = 1 - GetFloatConst(mtrl, 0x59BDA0B1, 1.0f);
                var isHroth = mtrl.ShaderKeys.FirstOrDefault(x => x.KeyId == 0x380CAED0 && x.Value == 0x57FF3B64) != null;

                 return (Color4 diffuse, Color4 normal, Color4 mask, Color4 index) => {
                    var roughness = 0.0f;
                    var metalness = metalnessConst;
                    var occlusion = 1.0f;

                    float skinInfluence = (float)normal.Blue;// * normal.Blue;
                    var sColor = Color4.Lerp(new Color4(1.0f), skinColor, skinInfluence);
                    diffuse *= sColor;

                    var specMask = mask.Red;
                    var specular = new Color4(specMask, specMask, specMask, 1.0f);

                    roughness = mask.Green;
                    float bonusInfluence = normal.Alpha;
                    if (bonusColor.Color != null)
                    {
                        if (bonusColor.Blend)
                        {
                            var c = SrgbToLinear(bonusColor.Color.Value);
                            c.Alpha = 1.0f;
                            diffuse = Color4.Lerp(diffuse, c, bonusInfluence * bonusColor.Color.Value.Alpha);
                        } else
                        {
                            var hairColor = bonusColor.Color.Value;
                            if (highlightColor.Color != null)
                            {
                                hairColor = Color4.Lerp(bonusColor.Color.Value, highlightColor.Color.Value, mask.Alpha);
                            }

                            hairColor *= _BodyFurMultiplier;

                            // Blend in hair color/hroth fur
                            var delta = Math.Min(Math.Max(bonusInfluence - skinInfluence, 0), 1.0f);
                            diffuse = Color4.Lerp(diffuse, hairColor, delta);

                            // Arbitrary darkening to attempt to match hair better.
                        }
                    }


                    //diffuse *= diffuseColorMul;

                    if (!settings.GeneratePbrMaps)
                    {
                        specular *= (1 - roughness);

                        if (metalness < 0.5f)
                        {
                            specular *= _SkinSpecMultiplier;
                        } else
                        {
                            specular *= _MetalFloor + (metalness * _MetalMultiplier);

                            // As metalness rises, the diffuse/specular colors merge.
                            diffuse = Color4.Lerp(diffuse, diffuse * specular, metalness);
                            specular = Color4.Lerp(specular, diffuse * specular, metalness);
                        }
                    }


                    var alpha = diffuse.Alpha * alphaMultiplier;
                    alpha = allowTranslucency ? alpha : (alpha < 1 ? 0 : 1);

                    if (isHroth)
                    {
                        alpha = 1.0f;
                    }

                    var emissive = emissiveColorMul;
                    var sss = mask.Blue;

                    return new ShaderMapperResult()
                    {
                        Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, alpha),
                        Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                        Specular = specular,
                        Emissive = emissive,
                        Alpha = alpha,
                        Roughness = roughness,
                        Metalness = metalness,
                        Occlusion = occlusion,
                        Subsurface = sss,
                    };
                };
            }
            else if (shaderPack == EShaderPack.Hair)
            {
                var hairColor = (Color4)colors.HairColor;
                var bonusColor = GetHairBonusColor(mtrl, colors, colors.HairHighlightColor != null ? colors.HairHighlightColor.Value : colors.HairColor);
                var lightColor = SrgbToLinear(new Color4(1, 0, 1, 1));

                //bonusColor = SrgbToLinear(bonusColor);

                return (Color4 diffuse, Color4 normal, Color4 mask, Color4 index) => {
                    var roughness = 0.0f;
                    var metalness = 0.0f;
                    var occlusion = 1.0f;
                    float bonusInfluence = normal.Blue;
                    float lightInfluence = (float)Math.Pow(mask.Red, 4.25f);

                    //Console.WriteLine($"light influence: {lightInfluence}");

                    roughness = mask.Green;
                    var specular = new Color4(mask.Red, mask.Red, mask.Red, 1.0f);

                    var baseHairColor = Color4.Lerp(hairColor, bonusColor, bonusInfluence);
                    diffuse = Color4.Lerp(baseHairColor, colors.LightColor, lightInfluence);
                    diffuse *= diffuseColorMul;

                    occlusion = (mask.Alpha * mask.Alpha);
                    if (!settings.GeneratePbrMaps)
                    {
                        specular *= (1- roughness);
                        specular *= _HairSpecMultiplier;
                        diffuse *= occlusion;
                    }

                    var sss = mask.Blue;

                    var alpha = normal.Alpha * alphaMultiplier;
                    alpha = allowTranslucency ? alpha : (alpha < 1 ? 0 : 1);

                    diffuse.Alpha = alpha;
                    return new ShaderMapperResult()
                    {
                        Diffuse = diffuse,
                        Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                        Specular = specular,
                        Alpha = alpha,
                        Roughness = roughness,
                        Metalness = metalness,
                        Occlusion = occlusion,
                        Subsurface = sss,
                    };
                };
            }
            else if (shaderPack == EShaderPack.CharacterTattoo)
            {
                var bonusColor = GetHairBonusColor(mtrl, colors, colors.TattooColor);


                var tattooColor = colors.TattooColor;
                // Very similar to hair.shpk but without an extra texture
                return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                    var roughness = 0.0f;
                    var metalness = 0.0f;
                    var occlusion = 1.0f;
                    float tattooInfluence = normal.Blue;

                    diffuse = Color4.Lerp(_MoleColor, tattooColor, tattooInfluence);

                    var alpha = normal.Alpha * alphaMultiplier;
                    alpha = allowTranslucency ? alpha : (alpha < 1 ? 0 : 1);

                    diffuse *= diffuseColorMul;

                    diffuse.Alpha = alpha;
                    return new ShaderMapperResult()
                    {
                        Diffuse = diffuse,
                        Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                        Specular = Color4.Black,
                        Alpha = alpha,
                        Roughness = roughness,
                        Metalness = metalness,
                        Occlusion = occlusion,
                    };
                };
            }
            else if (shaderPack == EShaderPack.CharacterOcclusion)
            {
                // Not sure how this should be rendered if at all, so its fully transparent
                return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                    var roughness = 0.0f;
                    var metalness = 0.0f;
                    var occlusion = 1.0f;
                    return new ShaderMapperResult()
                    {
                        Diffuse = Color4.White,
                        Normal = new Color4(0.5f, 0.5f, 1.0f, 1.0f),
                        Specular = Color4.Black,
                        Alpha = 0.0f,
                        Roughness = roughness,
                        Metalness = metalness,
                        Occlusion = occlusion,
                    };
                };
            }
            else if (shaderPack == EShaderPack.Iris)
            {
                var irisColor = colors.EyeColor;
                var scleraColor = GetConstColor(mtrl, 0x11C90091, new Color4(1.0f));

                irisColor *= irisColor;
                scleraColor *= scleraColor;

                //g_SpecularColorMask
                var reflectionColor = GetConstColor(mtrl, 0xCB0338DC, new Color4(1.0f));


                return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                    var roughness = 0.0f;
                    var metalness = 0.0f;
                    var occlusion = 1.0f;


                    float colorInfluence = multi.Blue;
                    diffuse = Color4.Lerp(diffuse * scleraColor, diffuse * irisColor, colorInfluence);

                    var emissive = new Color4(multi.Red, multi.Red, multi.Red, 1.0f);
                    emissive *= emissiveColorMul;

                    var specular = _EyeSpecular;

                    if (!settings.GeneratePbrMaps)
                    {
                        specular *= reflectionColor;
                    }

                    diffuse *= diffuseColorMul;

                    return new ShaderMapperResult()
                    {
                        Diffuse = diffuse,
                        Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                        Specular = specular,
                        Alpha = 1.0f,
                        Emissive = emissive,
                        Roughness = roughness,
                        Metalness = metalness,
                        Occlusion = occlusion,
                    };
                };
            }
            else if (shaderPack == EShaderPack.Bg 
                || mtrl.ShaderPack == EShaderPack.BgProp 
                || mtrl.ShaderPack == EShaderPack.BgCrestChange
                || mtrl.ShaderPack == EShaderPack.BgUvScroll )
            {
                var useAlpha = mtrl.ShaderKeys.Any(x => x.KeyId == 0xA9A3EE25);
                return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {
                    var roughness = 0.0f;
                    var metalness = 0.0f;
                    var occlusion = 1.0f;

                    if (!useAlpha)
                    {
                        diffuse.Alpha = 1.0f;
                    }

                    var savedAlpha = diffuse.Alpha;

                    diffuse *= diffuseColorMul;

                    var specular = new Color4(1.0f);
                    if (!hasMulti)
                    {
                        specular = new Color4(0, 0, 0, 1);
                    }
                    else
                    {
                        roughness = multi.Green;
                        if (!settings.GeneratePbrMaps)
                        {
                            specular *= (1 - roughness);
                        }

                        specular *= multi.Red;
                        specular *= multi.Blue;
                        specular *= specularColorMul;
                    }

                    var emissive = emissiveColorMul * multi.Alpha * diffuse;

                    diffuse.Alpha = savedAlpha;

                    return new ShaderMapperResult()
                    {
                        Diffuse = diffuse,
                        Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                        Specular = hasMulti ? new Color4(multi.Green, multi.Green, multi.Green, 1.0f) : Color4.Black,
                        Emissive = emissive,
                        Alpha = savedAlpha,
                        Roughness = roughness,
                        Metalness = metalness,
                        Occlusion = occlusion,
                    };
                };
            }
            else if (shaderPack == EShaderPack.BgColorChange)
            {
                var useAlpha = mtrl.ShaderKeys.Any(x => x.KeyId == 0xA9A3EE25);
                return (Color4 diffuse, Color4 normal, Color4 multi, Color4 index) => {

                    var roughness = 0.0f;
                    var metalness = 0.0f;
                    var occlusion = 1.0f;

                    var alpha = normal.Alpha;
                    float colorInfluence = diffuse.Alpha;

                    // This shpk seems to ignore the diffuse color var even though it's typically set in the material.
                    // It usually seems to roughly reflect the default dye color, but not always.
                    // Probably a vestigial dev value.
                    var baseDiffuse = diffuse * multi.Red;// * diffuseColorMul;
                    var dyeDiffuse = diffuse * colors.FurnitureColor;

                    diffuse = Color4.Lerp(baseDiffuse, dyeDiffuse, colorInfluence);

                    var specular = new Color4(1.0f);
                    if (!hasMulti)
                    {
                        specular = new Color4(0, 0, 0, 1);
                    }
                    else
                    {
                        var invRough = 1 - multi.Green;
                        specular *= invRough;
                        specular *= multi.Red;
                        specular *= multi.Blue;
                        specular *= specularColorMul;
                    }

                    // Spec Power
                    specular *= multi.Green;

                    // Gloss - We just multiply this through to badly simulate the effect.
                    specular *= multi.Blue;

                    var emissive = emissiveColorMul * diffuse;

                    return new ShaderMapperResult()
                    {
                        Diffuse = diffuse,
                        Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                        Specular = hasMulti ? new Color4(multi.Green, multi.Green, multi.Green, 1.0f) : Color4.Black,
                        Emissive = emissive,
                        Alpha = alpha,
                        Roughness = roughness,
                        Metalness = metalness,
                        Occlusion = occlusion,
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
                        Alpha = 1.0f,
                        Roughness = 0.0f,
                        Metalness = 0.0f,
                        Occlusion = 1.0f,
                    };
                };
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color4 LinearToSrgb(Color4 color)
        {
            color.Red = (float)Math.Sqrt(color.Red);
            color.Green = (float)Math.Sqrt(color.Green);
            color.Blue = (float)Math.Sqrt(color.Blue);
            return color;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color4 SrgbToLinear(Color4 color)
        {
            color.Red *= color.Red;
            color.Green *= color.Green;
            color.Blue *= color.Blue;
            return color;
        }

        private static Color4 GetHairBonusColor(XivMtrl mtrl, CustomModelColors colors, Color4 defaultColor)
        {
            var bonusColor = defaultColor;

            var bonusColorKey = mtrl.ShaderKeys.FirstOrDefault(x => x.KeyId == 0x24826489);

            if (bonusColorKey != null)
            {
                // PART_HAIR
                if (bonusColorKey.Value == 0xF7B8956E)
                {
                    if (colors.HairHighlightColor != null)
                    {
                        bonusColor = colors.HairHighlightColor.Value;
                    }
                    else
                    {
                        bonusColor = colors.HairColor;
                    }
                }
                // PART_FACE
                else if (bonusColorKey.Value == 0x6E5B8F10)
                {
                    bonusColor = colors.TattooColor;
                }
            }

            return bonusColor;
        }


        private static float GetFloatConst(XivMtrl mtrl, uint constant, float defaultValue)
        {
            var mtrlConst = mtrl.ShaderConstants.FirstOrDefault(x => x.ConstantId == constant);
            if (mtrlConst != null)
            {
                defaultValue = mtrlConst.Values[0];
            }

            return defaultValue;
        }

        private static Color4 GetConstColor(XivMtrl mtrl, uint constant, Color4 defaultColor)
        {
            var mtrlConst = mtrl.ShaderConstants.FirstOrDefault(x => x.ConstantId == constant);
            if (mtrlConst != null)
            {
                defaultColor = new Color4(mtrlConst.Values[0], mtrlConst.Values[1], mtrlConst.Values[2], 1.0f);
            }

            return defaultColor;
        }

        private static (Color4? Color, bool Blend) GetSkinBonusColor(XivMtrl mtrl, CustomModelColors colors)
        {
            Color4? bonusColor = null;
            var compatMode = mtrl.ShaderKeys.FirstOrDefault(x => x.KeyId == 0xB616DC5A && x.Value == 0x600EF9DF) != null;
            if (compatMode)
            {
                return (null, false);
            }


            var bonusColorKey = mtrl.ShaderKeys.FirstOrDefault(x => x.KeyId == 0x380CAED0);

            if (bonusColorKey != null)
            {
                // PART_BODY
                if (bonusColorKey.Value == 0x2BDB45F1)
                {
                    // Unused
                    bonusColor = null;
                }
                // PART_FACE
                else if (bonusColorKey.Value == 0xF5673524)
                {
                    bonusColor = colors.LipColor;
                    return (bonusColor, true);
                }
                // PART_HRO
                else if (bonusColorKey.Value == 0x57FF3B64)
                {
                    bonusColor = colors.HairColor;
                    return (bonusColor, false);
                }
            } else
            {
                // Default usage is as face.
                bonusColor = colors.LipColor;
                return (bonusColor, true);
            }

            return (bonusColor, false);
        }
        private static (Color4? Color, bool Blend) GetSkinBonusColor2(XivMtrl mtrl, CustomModelColors colors)
        {
            Color4? bonusColor = null;
            var compatMode = mtrl.ShaderKeys.FirstOrDefault(x => x.KeyId == 0xB616DC5A && x.Value == 0x600EF9DF) != null;
            if (compatMode)
            {
                return (null, false);
            }

            var bonusColorKey = mtrl.ShaderKeys.FirstOrDefault(x => x.KeyId == 0x380CAED0);

            if (bonusColorKey != null)
            {
                // PART_HRO
                if (bonusColorKey.Value == 0x57FF3B64)
                {
                    if (colors.HairHighlightColor != null)
                    {
                        bonusColor = colors.HairHighlightColor.Value;
                        return (bonusColor, false);
                    }
                }
            }

            return (bonusColor, false);
        }


        /// <summary>
        /// Retrieves the blended colorset row from the colorset data.
        /// </summary>
        /// <param name="colorsetData"></param>
        /// <param name="rowNumber"></param>
        /// <returns></returns>
        public static SharpDX.Half[] GetColorsetRow(List<SharpDX.Half> colorsetData, float indexRed, float indexGreen, bool visualizeOnly = false, int highlightRow = -1)
        {
            if(colorsetData == null || colorsetData.Count == 0) {
                return null;
            }

            const int _Width = 8;
            const int _Height = 32;
            const int _RowsPerBlend = 2;

            const int _PerPixel = 4;
            const int _RowSize = _Width * _PerPixel;

            var values = ReadColorIndex(indexRed, indexGreen);


            var row0Offset = _RowSize * (values.RowId * _RowsPerBlend);
            var row1Offset = 0;

            if((values.RowId * _RowsPerBlend + 1) < _Height)
            {
                row1Offset = _RowSize * ((values.RowId * _RowsPerBlend) + 1);
            }
            else
            {
                row1Offset = row0Offset;
            }


            var rowData = new SharpDX.Half[_RowSize];
            if (visualizeOnly)
            {
                for(int i = 0; i < 3; i++) { 
                    // Render the colorset as a continuous 0.0 -> 1.0 color on the diffuse.
                    rowData[i] = ((values.RowId + values.Blend) / (_ColorsetMul - 1));
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
                var baseRow2 = values.RowId;

                if (baseRow == baseRow2)
                {
                    var blend = values.Blend;
                    if (highlightRow % 2 == 0)
                    {
                        blend = 1 - blend;
                    }

                    rowData[0] = blend;
                    rowData[1] = blend;
                    rowData[2] = blend;


                    rowData[8] = blend;
                    rowData[9] = blend;
                    rowData[10] = blend;

                } else
                {
                    // Mute the colors on non-selected rows.
                    rowData[0] *= 0.1f;
                    rowData[1] *= 0.1f;
                    rowData[2] *= 0.1f;

                }
                if (rowData.Length > 18)
                {
                    rowData[18] = 0;
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
        public static SharpDX.Half LerpHalf(SharpDX.Half a, SharpDX.Half b, float f)
        {
            return a * (1.0f - f) + (b * f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (int RowId, float Blend) ReadColorIndex(float indexRed, float indexGreen)
        {
            int byteRed = (int) Math.Round(indexRed * 255.0f);
            int rowNumber = (int) Math.Round(byteRed / (_ColorsetMul));

            float blendAmount = 1.0f - indexGreen;

            return (rowNumber, blendAmount);
        }

        private class TexMapData
        {
            public TexInfo Diffuse;
            public TexInfo Normal;
            public TexInfo Mask;
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
