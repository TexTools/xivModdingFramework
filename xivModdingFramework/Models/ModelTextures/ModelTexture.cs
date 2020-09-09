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
using xivModdingFramework.Materials.FileTypes;
using HelixToolkit.SharpDX.Core.Model.Scene2D;
using HelixToolkit.SharpDX.Core;

namespace xivModdingFramework.Models.ModelTextures
{

    /// <summary>
    /// Data holder for the entire set of custom colors needed to render everything that supports custom colors.
    /// </summary>
    public class CustomModelColors
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

        public bool InvertNormalGreen = false;



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
            return _defaultColors;
        }


        /// <summary>
        /// Gets the customized texture map data for a model.
        /// Null custom model colors uses the defaults at ModelTexture.GetCustomColors().
        /// </summary>
        /// <param name="gameDirectory"></param>
        /// <param name="mtrl"></param>
        /// <param name="colors"></param>
        /// <returns></returns>
        public static async Task<ModelTextureData> GetModelMaps(DirectoryInfo gameDirectory, XivMtrl mtrl, CustomModelColors colors = null)
        {
            var tex = new Tex(gameDirectory);
            return await GetModelMaps(tex, mtrl);
        }

        /// <summary>
        /// Gets the texture maps for the model
        /// </summary>
        /// <returns>The texture maps in byte arrays inside a ModelTextureData class</returns>
        public static async Task<ModelTextureData> GetModelMaps(Tex tex, XivMtrl mtrl, CustomModelColors colors = null)
        {

            // Use static values as needed.
            if(colors == null)
            {
                colors = GetCustomColors();
            }

            var shaderInfo = mtrl.GetShaderInfo();
            var mtrlMaps = mtrl.GetAllMapInfos();

            var texMapData = await GetTexMapData(tex, mtrl);

            var dimensions = await EqualizeTextureSizes(texMapData);

            var diffuseMap = new List<byte>();
            var normalMap = new List<byte>();
            var specularMap = new List<byte>();
            var emissiveMap = new List<byte>();
            var alphaMap = new List<byte>();

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
                for (var i = 3; i < dataLength; i += 4)
                {
                // Load the individual pixels into memory.
                    Color baseNormalColor = new Color(127, 127, 255, 255);
                    Color baseDiffuseColor = new Color(255, 255, 255, 255);
                    Color baseSpecularColor = new Color(255, 255, 255, 255);


                    if (normalPixels != null)
                    {
                        baseNormalColor = new Color(normalPixels[i - 3], normalPixels[i - 2], normalPixels[i - 1], normalPixels[i]);
                    }

                    if (diffusePixels != null)
                    {
                        baseDiffuseColor = new Color(diffusePixels[i - 3], diffusePixels[i - 2], diffusePixels[i - 1], diffusePixels[i]);
                    }

                    if(specularPixels != null)
                    { 
                        baseSpecularColor = new Color(specularPixels[i - 3], specularPixels[i - 2], specularPixels[i - 1], specularPixels[i]);
                    }

                    if(colors != null && colors.InvertNormalGreen)
                    {
                        baseNormalColor[1] = (byte)(255 - baseNormalColor[1]);
                    }

                    byte colorsetValue = baseNormalColor.A;

                    // Calculate real colors from the inputs and shader.
                    Color normalColor, diffuseColor, specularColor;
                    byte opacity;
                    ComputeShaderColors(colors, shaderInfo, baseNormalColor, baseDiffuseColor, baseSpecularColor, out normalColor, out diffuseColor, out specularColor, out opacity);
                    Color alphaColor = new Color(opacity, opacity, opacity, opacity);

                    // Apply colorset if needed.  (This could really be baked into ComputeShaderColors)
                    Color emissiveColor = new Color(0, 0, 0, 0);
                    if(mtrl.ColorSetData.Count > 0)
                    {
                        var cs = texMapData.ColorSet.Data;
                        Color finalDiffuseColor, finalSpecularColor;
                        ComputeColorsetBlending(mtrl, colorsetValue, cs, diffuseColor, specularColor, out finalDiffuseColor, out finalSpecularColor, out emissiveColor);
                        diffuseColor = finalDiffuseColor;
                        specularColor = finalSpecularColor;
                    }

                    // White out the opacity channels where appropriate.
                    diffuseColor.A = opacity;
                    specularColor.A = 255;
                    normalColor.A = 255;


                    diffuseMap.AddRange(BitConverter.GetBytes(diffuseColor.ToRgba()));
                    specularMap.AddRange(BitConverter.GetBytes(specularColor.ToRgba()));
                    emissiveMap.AddRange(BitConverter.GetBytes(emissiveColor.ToRgba()));
                    alphaMap.AddRange(BitConverter.GetBytes(alphaColor.ToRgba()));
                    normalMap.AddRange(BitConverter.GetBytes(normalColor.ToRgba()));
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
                Alpha = alphaMap.ToArray(),
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
                    var texData = await tex.GetTexData(ttp);
                    var imageData = await tex.GetImageData(texData);

                    switch (ttp.Type)
                    {
                        case XivTexType.Diffuse:
                            texMapData.Diffuse = new TexInfo { Width = texData.Width, Height = texData.Height, Data = imageData };
                            break;
                        case XivTexType.Specular:
                        case XivTexType.Multi:
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
                    using (var img = Image.LoadPixelData<Rgba32>(texMapData.Normal.Data, texMapData.Normal.Width,
                        texMapData.Normal.Height))
                    {
                        img.Mutate(x => x.Resize(width, height));

                        texMapData.Normal.Data = MemoryMarshal.AsBytes(img.GetPixelSpan()).ToArray();
                    }
                }

                if (texMapData.Diffuse != null &&
                    (largestSize > texMapData.Diffuse.Width * texMapData.Diffuse.Height || scaleDown))
                {
                    using (var img = Image.LoadPixelData<Rgba32>(texMapData.Diffuse.Data, texMapData.Diffuse.Width,
                        texMapData.Diffuse.Height))
                    {
                        for (int i = 0; i < img.Height; i++)
                        {
                            var pixelRowSpan = img.GetPixelRowSpan(i);
                            for (int j = 0; j < img.Width; j++)
                            {
                                pixelRowSpan[j] = new Rgba32(pixelRowSpan[j].R, pixelRowSpan[j].G, pixelRowSpan[j].B, 255);
                            }
                        }
                        img.Mutate(x => x.Resize(width, height));

                        texMapData.Diffuse.Data = MemoryMarshal.AsBytes(img.GetPixelSpan()).ToArray();
                    }
                }

                if (texMapData.Specular != null &&
                    (largestSize > texMapData.Specular.Width * texMapData.Specular.Height || scaleDown))
                {
                    using (var img = Image.LoadPixelData<Rgba32>(texMapData.Specular.Data, texMapData.Specular.Width,
                        texMapData.Specular.Height))
                    {
                        img.Mutate(x => x.Resize(width, height));

                        texMapData.Specular.Data = MemoryMarshal.AsBytes(img.GetPixelSpan()).ToArray();
                    }
                }
            });

            return (width, height);
        }

        private static void ComputeShaderColors(ShaderInfo info, Color baseNormal, Color baseDiffuse, Color baseSpecular, out Color newNormal, out Color newDiffuse, out Color newSpecular, out byte opacity)
        {
            ComputeShaderColors(GetCustomColors(), info, baseNormal, baseDiffuse, baseSpecular, out newNormal, out newDiffuse, out newSpecular, out opacity);
        }

        private static void ComputeShaderColors(CustomModelColors colors,  ShaderInfo info, Color baseNormal, Color baseDiffuse, Color baseSpecular, out Color newNormal, out Color newDiffuse, out Color newSpecular, out byte opacity)
        {
            // This is basically codifying this document: https://docs.google.com/spreadsheets/d/1kIKvVsW3fOnVeTi9iZlBDqJo6GWVn6K6BCUIRldEjhw/edit#gid=2112506802
            opacity = 255;
            newNormal = baseNormal;
            newDiffuse = baseDiffuse;
            newSpecular = baseSpecular;


            // This var is technically defined in the Shaders parameters.
            // But we can use a constant copy of it for now, since it's largely non-changeable.
            const float PlayerColorMultiplier = 1.4f;
            const float BrightPlayerColorMultiplier = 3.0f;

            if (info.Shader == MtrlShader.Standard || info.Shader == MtrlShader.Glass)
            {
                // Common
                // Base color here is diffuse if we have one...
                if(info.Preset == MtrlShaderPreset.DiffuseSpecular)
                {
                    // Has a raw diffuse.
                    newDiffuse = baseDiffuse;
                    newDiffuse.A = baseNormal.B;

                    // Has a raw specular.
                    newSpecular = baseSpecular;

                } else if(info.Preset == MtrlShaderPreset.DiffuseMulti || info.Preset == MtrlShaderPreset.Monster)
                {
                    // Has a raw diffuse.
                    newDiffuse = baseDiffuse;

                    // But we also have to modulate that diffuse color by the multi red channel.
                    //newDiffuse = MultiplyColor(newDiffuse, baseSpecular.R);

                    newDiffuse.A = baseNormal.B;

                    // Uses multi green/blue in some fashion.
                    // We'll just show green for now.
                    newSpecular = new Color(baseSpecular.G, baseSpecular.G, baseSpecular.G, (byte)255);

                } else
                {
                    // Uses multi channel Red as a base/ao map.
                    newDiffuse = new Color(baseSpecular.R, baseSpecular.R, baseSpecular.R, (byte)255);
                    newDiffuse.A = baseNormal.B;

                    // Uses multi green/blue in some fashion.
                    // We'll just show green for now.
                    newSpecular = new Color(baseSpecular.G, baseSpecular.G, baseSpecular.G, (byte)255);

                }

                // Normal is the same for all of them.
                newNormal = new Color(baseNormal.R, baseNormal.G, (byte)255, (byte)255);
                opacity = baseNormal.B;



            } else if(info.Shader == MtrlShader.Furniture || info.Shader == MtrlShader.DyeableFurniture)
            {
                // Furniture
                newDiffuse = new Color(baseDiffuse.R, baseDiffuse.G, baseDiffuse.B, (byte)255);

                if(info.Shader == MtrlShader.DyeableFurniture)
                {
                    float colorInfluence = ByteToFloat(baseDiffuse.A);
                    Color furnitureColor = MultiplyColor(colors.FurnitureColor, 1.0f);

                    newDiffuse = Blend(baseDiffuse, furnitureColor, colorInfluence);

                }

                newSpecular = new Color(baseSpecular.G, baseSpecular.G, baseSpecular.G, (byte)255);
                newNormal = new Color(baseNormal.R, baseNormal.G, baseNormal.B, (byte)255);

                // This needs some more research all around

            } else if(info.Shader == MtrlShader.Skin)
            {

                newNormal = new Color(baseNormal.R, baseNormal.G, (byte)255, (byte)255);
                newSpecular = new Color(baseSpecular.G, baseSpecular.G, baseSpecular.G,(byte) 255);
                opacity = 255;

                // This is an arbitrary number.  There's likely some value in the shader params for skin that
                // tones down the specularity here, but without it the skin is hyper reflective.
                newSpecular = MultiplyColor(newSpecular, 0.25f);

                // New diffuse starts from regular diffuse file.
                // Then factors in the player's skin color multiplied by the shader value.
                float skinInfluence = ByteToFloat(baseSpecular.R);
                var coloredSkin = MultiplyColor(baseDiffuse, colors.SkinColor);

                newDiffuse = Blend(baseDiffuse, coloredSkin, skinInfluence);

                if (info.Preset == MtrlShaderPreset.Face)
                {
                    // Face shaders also allow for lip color.
                    var coloredLip = MultiplyColor(baseDiffuse, colors.LipColor);
                    float lipInfluence = ByteToFloat(baseSpecular.B);
                    newDiffuse = Blend(newDiffuse, coloredLip, lipInfluence);

                    // For lipstick, increase the specular value slightly.
                    float specAmp = 1.0f + (lipInfluence * 0.25f);
                    newSpecular = MultiplyColor(newSpecular, specAmp);

                    // Face shader supports alpha, unlike normal skin textures.
                    opacity = baseNormal.B;
                }


            } else if(info.Shader == MtrlShader.Hair)
            {

                newNormal = new Color(baseNormal.R, baseNormal.G, (byte)255, (byte)255);
                newSpecular = new Color(baseSpecular.G, baseSpecular.G, baseSpecular.G, (byte)255);
                opacity = baseNormal.A;

                // This is an arbitrary number.  There's likely some value in the shader params for skin that
                // tones down the specularity here, but without it the skin is hyper reflective.
                newSpecular = MultiplyColor(newSpecular, 0.25f);

                // The influence here determines which base color we use.
                float influenceStrength = ByteToFloat(baseSpecular.A);

                // Starting from the original hair color...
                var baseColor = MultiplyColor(colors.HairColor, 1.0f);

                // Hair highlight color if available.
                var targetColor = (Color)(colors.HairHighlightColor != null ? colors.HairHighlightColor : colors.HairColor);


                // But wait! If we're actually a tattoo preset, that changes instead to tattoo color.
                if (info.Preset == MtrlShaderPreset.Face)
                {
                    targetColor = MultiplyColor(colors.TattooColor, 1.0f);
                } else if(info.Preset == MtrlShaderPreset.FaceBright)
                {
                    // Multiplier here is 3.0 instead of 1.4
                    targetColor = MultiplyColor(colors.TattooColor, BrightPlayerColorMultiplier / PlayerColorMultiplier);

                }

                // This gets us our actual base color.
                baseColor = Blend(baseColor, targetColor, influenceStrength);

                // Now this needs to be straight multiplied with the multi channel red.
                newDiffuse = MultiplyColor(baseColor, baseSpecular.R);

            } else if(info.Shader == MtrlShader.Iris)
            {
                // Eyes
                newNormal = new Color(baseNormal.R, baseNormal.G, (byte)255, (byte)255);
                newSpecular = new Color(baseSpecular.G, baseSpecular.G, baseSpecular.G, (byte)255);
                opacity = baseNormal.A;


                // Base color is the selected eye color.
                var baseColor = colors.EyeColor;

                // Pretty sure some data is missing in here.
                // Catchlight is also not factored in atm.

                // Now this needs to be straight multiplied with the multi channel red.
                newDiffuse = MultiplyColor(baseColor, baseSpecular.R);


            } else
            {
                // Fall through just shows stuff as is.
                newNormal = baseNormal;
                newDiffuse = baseDiffuse;
                newSpecular = baseSpecular;
            }

            // Transparency filtering.
            if (!info.TransparencyEnabled)
            {
                opacity = (byte)(opacity < 128 ? 0 : 255);
            }
        }

        private static void ComputeColorsetBlending(XivMtrl mtrl, byte colorsetByte, byte[] colorSetData, Color baseDiffuse, Color baseSpecular, out Color newDiffuse, out Color newSpecular, out Color emissiveColor)
        {
            int rowNumber = colorsetByte / 17;
            int nextRow = rowNumber >= 15 ? 15 : rowNumber + 1;
            int blendAmount = (colorsetByte % 17);
            float fBlendAmount = blendAmount / 17.0f;

            Color diffuse1, diffuse2;
            Color spec1, spec2;
            Color emiss1, emiss2;

            // Byte offset to rows
            var row1Offset = Clamp(rowNumber * 16);
            var row2Offset = Clamp(nextRow * 16);

            diffuse1 = new Color(colorSetData[row1Offset + 0], colorSetData[row1Offset + 1], colorSetData[row1Offset + 2], (byte)255);
            diffuse2 = new Color(colorSetData[row2Offset + 0], colorSetData[row2Offset + 1], colorSetData[row2Offset + 2], (byte)255);

            spec1 = new Color(colorSetData[row1Offset + 4], colorSetData[row1Offset + 5], colorSetData[row1Offset + 6], (byte)255);
            spec2 = new Color(colorSetData[row2Offset + 4], colorSetData[row2Offset + 5], colorSetData[row2Offset + 6], (byte)255);

            emiss1 = new Color(colorSetData[row1Offset + 8], colorSetData[row1Offset + 9], colorSetData[row1Offset + 10], (byte)255);
            emiss2 = new Color(colorSetData[row2Offset + 8], colorSetData[row2Offset + 9], colorSetData[row2Offset + 10], (byte)255);


            // These are now our base values to multiply the base values by.
            Color diffuse = Blend(diffuse1, diffuse2, fBlendAmount);
            Color specular = Blend(spec1, spec2, fBlendAmount);
            Color emissive = Blend(emiss1, emiss2, fBlendAmount);

            newDiffuse = MultiplyColor(baseDiffuse, diffuse);
            newSpecular = MultiplyColor(baseSpecular, specular);
            emissiveColor = emissive;  // Nothing to multiply by here.

        }


        #region Math Helpers
        /// <summary>Blends the specified colors together.</summary>
        /// <param name="backColor">Color to blend the other color onto.</param>
        /// <param name="color">Color to blend onto the background color.</param>
        /// <param name="amount">How much of <paramref name="color"/> to keep,
        /// “on top of” <paramref name="backColor"/>.</param>
        /// <returns>The blended colors.</returns>
        private static Color Blend(Color backColor, Color color, float amount)
        {
            var r = (byte)Clamp(Math.Round((color.R * amount) + backColor.R * (1 - amount)));
            var g = (byte)Clamp(Math.Round((color.G * amount) + backColor.G * (1 - amount)));
            var b = (byte)Clamp(Math.Round((color.B * amount) + backColor.B * (1 - amount)));
            return new Color(r, g, b);
        }

        private static int Clamp(int i)
        {
            if (i > 255)
            {
                return 255;
            }
            else if (i < 0)
            {
                return 0;
            }
            else
            {
                return i;
            }
        }
        private static float Clamp(double f)
        {
            if (f > 255)
            {
                return 255;
            }
            else if (f < 0)
            {
                return 0;
            }
            else
            {
                return (float)f;
            }
        }
        /// <summary>
        /// Performs a basic alpha blending of two colors.
        /// </summary>
        /// <param name="bg"></param>
        /// <param name="fg"></param>
        /// <returns></returns>
        private static float ByteToFloat(byte b)
        {
            float f = b / 255f;
            return f;
        }

        private static byte FloatToByte(float f)
        {
            byte b;

            if (f > 1)
            {
                b = 255;
            }
            else if (f < 0)
            {
                b = 0;
            }
            else
            {
                b = (byte)Math.Round(f * 255f);
            }

            return b;
        }

        private static Color MultiplyColor(Color c1, byte b)
        {
            var ret = new Color();
            ret.R = FloatToByte(ByteToFloat(c1.R) * ByteToFloat(b));
            ret.G = FloatToByte(ByteToFloat(c1.G) * ByteToFloat(b));
            ret.B = FloatToByte(ByteToFloat(c1.B) * ByteToFloat(b));
            ret.A = FloatToByte(ByteToFloat(c1.A) * ByteToFloat(b));
            return ret;

        }
        private static Color MultiplyColor(Color c1, float f)
        {
            var ret = new Color();
            ret.R = FloatToByte(ByteToFloat(c1.R) * f);
            ret.G = FloatToByte(ByteToFloat(c1.G) * f);
            ret.B = FloatToByte(ByteToFloat(c1.B) * f);
            ret.A = FloatToByte(ByteToFloat(c1.A) * f);
            return ret;

        }
        private static Color MultiplyColor(Color c1, Color c2)
        {
            var ret = new Color();
            ret.R = FloatToByte(ByteToFloat(c1.R) * ByteToFloat(c2.R));
            ret.G = FloatToByte(ByteToFloat(c1.G) * ByteToFloat(c2.G));
            ret.B = FloatToByte(ByteToFloat(c1.B) * ByteToFloat(c2.B));
            ret.A = FloatToByte(ByteToFloat(c1.A) * ByteToFloat(c2.A));
            return ret;
        }
        #endregion
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