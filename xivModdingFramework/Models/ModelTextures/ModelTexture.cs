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

            // Use static values as needed.
            if(colors == null)
            {
                colors = GetCustomColors();
            }

            var texMapData = await GetTexMapData(tex, mtrl);

            var dimensions = await EqualizeTextureSizes(texMapData);

            var diffuseMap = new byte[dimensions.Width * dimensions.Height * 4];
            var normalMap = new byte[dimensions.Width * dimensions.Height * 4];
            var specularMap = new byte[dimensions.Width * dimensions.Height * 4];
            var emissiveMap = new byte[dimensions.Width * dimensions.Height * 4];
            var alphaMap = new byte[dimensions.Width * dimensions.Height * 4];

            var diffuseColorList = new List<Color>();
            var specularColorList = new List<Color>();
            var emissiveColorList = new List<Color>();

            byte[] diffusePixels = null, specularPixels = null, normalPixels = null;

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

            if(normalPixels == null && diffusePixels == null)
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

            var dataLength = normalPixels != null ? normalPixels.Length : diffusePixels.Length;

            await Task.Run(() =>
            {
                var colorShader = GetShaderColorMapper(GetCustomColors(), mtrl);
                bool invertNormalGreen = colors != null && colors.InvertNormalGreen;

                for (var i = 0; i < dataLength - 3; i += 4)
                {
                    // Load the individual pixels into memory.
                    Color4 baseDiffuseColor;
                    Color4 baseNormalColor;
                    Color4 baseSpecularColor;

                    if (diffusePixels != null)
                        baseDiffuseColor = new Color(diffusePixels[i], diffusePixels[i + 1], diffusePixels[i + 2], diffusePixels[i + 3]);
                    else
                        baseDiffuseColor = new Color4(1.0f, 1.0f, 1.0f, 1.0f);

                    if (normalPixels != null)
                        baseNormalColor = new Color(normalPixels[i], normalPixels[i + 1], normalPixels[i + 2], normalPixels[i + 3]);
                    else
                        baseNormalColor = new Color4(0.5f, 0.5f, 1.0f, 1.0f);

                    if (specularPixels != null)
                        baseSpecularColor = new Color(specularPixels[i], specularPixels[i + 1], specularPixels[i + 2], specularPixels[i + 3]);
                    else
                        baseSpecularColor = new Color4(1.0f, 1.0f, 1.0f, 1.0f);

                    if (colors != null && colors.InvertNormalGreen)
                        baseNormalColor.Green = 1.0f - baseNormalColor.Green;

                    var shaderResult = colorShader(baseDiffuseColor, baseNormalColor, baseSpecularColor);
                    Color4 diffuseColor = shaderResult.Diffuse;
                    Color4 normalColor = shaderResult.Normal;
                    Color4 specularColor = shaderResult.Specular;
                    Color4 alphaColor = new Color4(shaderResult.Opacity, shaderResult.Opacity, shaderResult.Opacity, shaderResult.Opacity);
                    Color4 emissiveColor = Color4.Black;

                    // Apply colorset if needed.
                    if (mtrl.ColorSetData.Count > 0)
                    {
                        byte colorsetValue = normalPixels[i + 3]; // Normal Alpha channel
                        byte[] colorSetData = texMapData.ColorSet.Data;
                        Color4 finalDiffuseColor, finalSpecularColor;
                        ComputeColorsetBlending(mtrl, colorsetValue, colorSetData, diffuseColor, specularColor, out finalDiffuseColor, out finalSpecularColor, out emissiveColor, highlightedRow);
                        diffuseColor = finalDiffuseColor;
                        specularColor = finalSpecularColor;
                    }

                    // White out the opacity channels where appropriate.
                    specularColor.Alpha = 1.0f;
                    normalColor.Alpha = 1.0f;

                    EncodeColorBytes(diffuseMap, i, diffuseColor);
                    EncodeColorBytes(normalMap, i, normalColor);
                    EncodeColorBytes(specularMap, i, specularColor);
                    EncodeColorBytes(alphaMap, i, alphaColor);
                    EncodeColorBytes(emissiveMap, i, emissiveColor);
                }
            });

            var modelTextureData = new ModelTextureData
            {
                Width = dimensions.Width,
                Height = dimensions.Height,
                Normal = normalMap,
                Diffuse = diffuseMap,
                Specular = specularMap,
                Emissive = emissiveMap,
                Alpha = alphaMap,
                MaterialPath = mtrl.MTRLPath.Substring(mtrl.MTRLPath.LastIndexOf('/'))
            };

            return modelTextureData;
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

            foreach (var ttp in ttps)
            {
                if (ttp.Type != XivTexType.ColorSet)
                {
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
                            texMapData.Specular = new TexInfo { Width = texData.Width, Height = texData.Height, Data = imageData }; ;
                            break;
                        case XivTexType.Normal:
                            texMapData.Normal = new TexInfo { Width = texData.Width, Height = texData.Height, Data = imageData }; ;
                            break;
                        default:
                            // Do not render textures that we do not know how to use
                            break;
                    }
                }
            }

            if (mtrl.ColorSetDataSize > 0)
            {
                var colorSetData = new List<byte>();
                foreach (var half in mtrl.ColorSetData)
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
                if (texMapData.Normal != null && (largestSize > texMapData.Normal.Width * texMapData.Normal.Height || scaleDown))
                    ResizeTexture(texMapData.Normal, width, height);

                if (texMapData.Diffuse != null && (largestSize > texMapData.Diffuse.Width * texMapData.Diffuse.Height || scaleDown))
                    ResizeTexture(texMapData.Diffuse, width, height);

                if (texMapData.Specular != null && (largestSize > texMapData.Specular.Width * texMapData.Specular.Height || scaleDown))
                    ResizeTexture(texMapData.Specular, width, height);
            });

            return (width, height);
        }
        private struct ColorMapperResult
        {
            public Color4 Diffuse;
            public Color4 Normal;
            public Color4 Specular;
            public float Opacity;
        }

        delegate ColorMapperResult ShaderColorMapperDelegate(Color4 diffuse, Color4 normal, Color4 specular);
        private static ShaderColorMapperDelegate GetShaderColorMapper(CustomModelColors colors, XivMtrl mtrl)
        {
            // This is basically codifying this document: https://docs.google.com/spreadsheets/d/1kIKvVsW3fOnVeTi9iZlBDqJo6GWVn6K6BCUIRldEjhw/edit#gid=2112506802

            // This var is technically defined in the Shaders parameters.
            // But we can use a constant copy of it for now, since it's largely non-changeable.
            const float PlayerColorMultiplier = 1.4f;
            const float BrightPlayerColorMultiplier = 3.0f;

            if (mtrl.Shader == ShaderHelpers.EShaderPack.Character || mtrl.Shader == ShaderHelpers.EShaderPack.CharacterGlass)
            {
                if (mtrl.Textures.Any(x => x.Usage == XivTexType.Diffuse))
                {
                    return (Color4 diffuse, Color4 normal, Color4 specular) => {
                        return new ColorMapperResult()
                        {
                            Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, normal.Blue),
                            Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                            Specular = specular,
                            Opacity = normal.Blue
                        };
                    };
                }
                else if (mtrl.Textures.Any(x => x.Usage == XivTexType.Diffuse) && mtrl.Textures.Any(x => x.Usage == XivTexType.Mask ))
                {
                    return (Color4 diffuse, Color4 normal, Color4 specular) => {
                        return new ColorMapperResult()
                        {
                            Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, normal.Blue),
                            Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                            Specular = new Color4(specular.Green, specular.Green, specular.Green, 1.0f),
                            Opacity = normal.Blue
                        };
                    };
                }
                else
                {
                    return (Color4 diffuse, Color4 normal, Color4 specular) => {
                        return new ColorMapperResult()
                        {
                            Diffuse = new Color4(diffuse.Red, diffuse.Red, diffuse.Red, normal.Blue),
                            Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                            Specular = new Color4(specular.Green, specular.Green, specular.Green, 1.0f),
                            Opacity = normal.Blue
                        };
                    };
                }
            }
            else if (mtrl.Shader == EShaderPack.Furniture)
            {
                return (Color4 diffuse, Color4 normal, Color4 specular) => {
                    return new ColorMapperResult()
                    {
                        Diffuse = new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, 1.0f),
                        Normal = new Color4(normal.Red, normal.Green, diffuse.Blue, 1.0f),
                        Specular = new Color4(specular.Green, specular.Green, specular.Green, 1.0f),
                        Opacity = normal.Blue
                    };
                };
            }
            else if (mtrl.Shader == EShaderPack.DyeableFurniture)
            {
                Color4 furnitureColor = colors.FurnitureColor;
                return (Color4 diffuse, Color4 normal, Color4 specular) => {
                    float colorInfluence = diffuse.Alpha;
                    return new ColorMapperResult()
                    {
                        Diffuse = Color4.Lerp(new Color4(diffuse.Red, diffuse.Green, diffuse.Blue, 1.0f), furnitureColor, colorInfluence),
                        Normal = new Color4(normal.Red, normal.Green, diffuse.Blue, 1.0f),
                        Specular = new Color4(specular.Green, specular.Green, specular.Green, 1.0f),
                        Opacity = normal.Blue
                    };
                };
            }
            else if (mtrl.Shader == EShaderPack.Skin)
            {
                var skinColor = colors.SkinColor;
                var lipColor = colors.LipColor;

                ShaderColorMapperDelegate skinShader = (Color4 diffuse, Color4 normal, Color4 specular) => {
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

                    return new ColorMapperResult()
                    {
                        Diffuse = newDiffuse,
                        Normal = newNormal,
                        Specular = newSpecular,
                        Opacity = 1.0f
                    };
                };

                ShaderColorMapperDelegate faceShader = (Color4 diffuse, Color4 normal, Color4 specular) => {
                    ColorMapperResult result = skinShader(diffuse, normal, specular);

                    // Face shaders also allow for lip color.
                    var coloredLip = diffuse * lipColor;
                    float lipInfluence = specular.Blue;
                    result.Diffuse = Color4.Lerp(result.Diffuse, coloredLip, lipInfluence);

                    // For lipstick, increase the specular value slightly.
                    float specAmp = 1.0f + (lipInfluence * 0.25f);
                    result.Specular = result.Specular * specAmp;

                    // Face shader supports alpha, unlike normal skin textures.
                    result.Opacity = normal.Blue;

                    return result;
                };


                // TODO: Need to fix this based on Shader Key settings
                if (mtrl.Shader == EShaderPack.Skin)
                    return faceShader;
                else
                    return skinShader;
            }
            else if (mtrl.Shader == EShaderPack.Hair)
            {
                var hairHighlightColor = (Color)(colors.HairHighlightColor != null ? colors.HairHighlightColor : colors.HairColor);
                var hairTargetColor = (Color)(colors.HairHighlightColor != null ? colors.HairHighlightColor : colors.HairColor);

                // Starting from the original hair color...
                var baseColor = colors.HairColor;

                // Hair highlight color if available.
                // But wait! If we're actually a tattoo preset, that changes instead to tattoo color.
                Color4 targetColor;

                // TODO: Need to fix this based on Shader Key settings
                if (false) //info.Preset == MtrlShaderPreset.Face)
                    targetColor = colors.TattooColor;

                // TODO: Need to fix this based on Shader Key settings
                else if (false)//info.Preset == MtrlShaderPreset.FaceBright)
                    // Multiplier here is 3.0 instead of 1.4
                    targetColor = Color4.Scale(colors.TattooColor, BrightPlayerColorMultiplier / PlayerColorMultiplier);
                else
                    targetColor = hairHighlightColor;

                return (Color4 diffuse, Color4 normal, Color4 specular) => {
                    Color4 newNormal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f);
                    Color4 newSpecular = new Color4(specular.Green, specular.Green, specular.Green, 1.0f);

                    // This is an arbitrary number.  There's likely some value in the shader params for skin that
                    // tones down the specularity here, but without it the skin is hyper reflective.
                    newSpecular = Color4.Scale(newSpecular, 0.25f);

                    // The influence here determines which base color we use.
                    float influenceStrength = specular.Alpha;

                    Color4 newDiffuse = Color4.Lerp(baseColor, targetColor, influenceStrength);
                    newDiffuse = Color4.Scale(newDiffuse, specular.Red);

                    return new ColorMapperResult()
                    {
                        Diffuse = newDiffuse,
                        Normal = newNormal,
                        Specular = newSpecular,
                        Opacity = normal.Alpha
                    };
                };
            }
            else if (mtrl.Shader == EShaderPack.Iris)
            {
                return (Color4 diffuse, Color4 normal, Color4 specular) =>
                {
                    return new ColorMapperResult()
                    {
                        Diffuse = Color4.Scale(colors.EyeColor, specular.Red),
                        Normal = new Color4(normal.Red, normal.Green, 1.0f, 1.0f),
                        Specular = new Color4(specular.Green, specular.Green, specular.Green, 1.0f),
                        Opacity = normal.Alpha
                    };
                };
            }
            else
            {
                return (Color4 diffuse, Color4 normal, Color4 specular) => {
                    return new ColorMapperResult()
                    {
                        Diffuse = diffuse,
                        Normal = normal,
                        Specular = specular,
                        Opacity = 1.0f
                    };
                };
            }
        }

        // This is only called in one place, and benefits greatly from being inlined
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ComputeColorsetBlending(XivMtrl mtrl, byte colorsetByte, byte[] colorSetData, Color4 baseDiffuse, Color4 baseSpecular, out Color4 newDiffuse, out Color4 newSpecular, out Color4 emissiveColor, int highlightRow = -1)
        {
            int rowNumber = colorsetByte / 17;
            int nextRow = rowNumber >= 15 ? 15 : rowNumber + 1;
            int blendAmount = (colorsetByte % 17);
            float fBlendAmount = blendAmount / 17.0f;

            Color4 diffuse1, diffuse2;
            Color4 spec1, spec2;
            Color4 emiss1, emiss2;

            // Byte offset to rows
            var row1Offset = rowNumber * 16;
            var row2Offset = nextRow * 16;

            if (highlightRow >= 0)
            {
                if (rowNumber == highlightRow)
                {
                    diffuse1 = Color4.White;
                    diffuse2 = Color4.Black;

                    spec1 = Color4.White;
                    spec2 = Color4.Black;

                    emiss1 = Color4.White;
                    emiss2 = Color4.Black;
                }
                else if (nextRow == highlightRow)
                {
                    diffuse1 = Color4.Black;
                    diffuse2 = Color4.White;

                    spec1 = Color4.Black;
                    spec2 = Color4.White;

                    emiss1 = Color4.Black;
                    emiss2 = Color4.White;
                } else
                {
                    diffuse1 = Color4.Black;
                    diffuse2 = Color4.Black;

                    spec1 = Color4.Black;
                    spec2 = Color4.Black;

                    emiss1 = Color4.Black;
                    emiss2 = Color4.Black;
                }
            }
            else
            {
                diffuse1 = new Color(colorSetData[row1Offset + 0], colorSetData[row1Offset + 1], colorSetData[row1Offset + 2], (byte)255);
                diffuse2 = new Color(colorSetData[row2Offset + 0], colorSetData[row2Offset + 1], colorSetData[row2Offset + 2], (byte)255);

                spec1 = new Color(colorSetData[row1Offset + 4], colorSetData[row1Offset + 5], colorSetData[row1Offset + 6], (byte)255);
                spec2 = new Color(colorSetData[row2Offset + 4], colorSetData[row2Offset + 5], colorSetData[row2Offset + 6], (byte)255);

                emiss1 = new Color(colorSetData[row1Offset + 8], colorSetData[row1Offset + 9], colorSetData[row1Offset + 10], (byte)255);
                emiss2 = new Color(colorSetData[row2Offset + 8], colorSetData[row2Offset + 9], colorSetData[row2Offset + 10], (byte)255);
            }


            // These are now our base values to multiply the base values by.
            Color4 diffuse = Color4.Lerp(diffuse1, diffuse2, fBlendAmount);
            Color4 specular = Color4.Lerp(spec1, spec2, fBlendAmount);
            Color4 emissive = Color4.Lerp(emiss1, emiss2, fBlendAmount);

            newDiffuse = baseDiffuse * diffuse;
            newSpecular = baseSpecular * specular;
            emissiveColor = emissive;  // Nothing to multiply by here.

        }

        public class TexMapData
        {
            public TexInfo Diffuse { get; set; }

            public TexInfo Specular { get; set; }

            public TexInfo Normal { get; set; }

            public TexInfo ColorSet { get; set; }
        }

        public class TexInfo
        {
            public int Width { get; set; }

            public int Height { get; set; }

            public byte[] Data { get; set; }
        }
    }


}
