using SharpDX;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Materials;
using xivModdingFramework.Materials.DataContainers;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.FileTypes;

namespace xivModdingFramework.Textures
{
    public class TextureHelpers
    {


        /// <summary>
        /// Paralellizes a pixel a modify action into a series of smaller task chunks,
        /// calling the given Action with the byte offset for the given pixel.
        /// </summary>
        /// <param name="action"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public static async Task ModifyPixels(Action<int> action, int width, int height)
        {
            List<Task> tasks = new List<Task>(height);
            for (int i = 0; i < height; i++)
            {
                // Reassign necessary to prevent threading shenanigans.
                var y = i;
                tasks.Add(Task.Run(() =>
                {
                    for (int x = 0; x < width; x++)
                    {
                        var offset = ((width * y) + x) * 4;
                        action(offset);
                    }
                }));
            }
            await Task.WhenAll(tasks);
        }


        /// <summary>
        /// Overlay an image onto a base image, while ignoring the base image's alpha channel/treating it as if it is 1.0.
        /// </summary>
        /// <param name="baseImage"></param>
        /// <param name="overlayImage"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public static async Task OverlayImagePreserveAlpha(byte[] baseImage, byte[] overlayImage, int width, int height)
        {
            var expectedSize = width * height * 4;
            if (expectedSize != baseImage.Length
                || expectedSize != overlayImage.Length)
            {
                throw new InvalidDataException("Images were not the expected size.");
            }

            await ModifyPixels((int offset) =>
            {
                var overlayAlpha = overlayImage[offset + 3] / 255f;


                for(int i = 0; i < 3; i++)
                {
                    var ov = overlayImage[offset + i];
                    var bv = baseImage[offset + i];

                    var c0 = ((ov * overlayAlpha) + (bv * (1 - overlayAlpha)));

                    baseImage[offset + i] = (byte)c0;
                }
            }, width, height);
        }

        /// <summary>
        /// Copies the alpha data from the mask into the base image.
        /// </summary>
        public static async Task MaskImage(byte[] baseImage, byte[] mask, int width, int height)
        {
            var expectedSize = width * height * 4;
            if (expectedSize != baseImage.Length
                || expectedSize != mask.Length)
            {
                throw new InvalidDataException("Images were not the expected size.");
            }

            await ModifyPixels((int offset) =>
            {
                baseImage[offset + 3] = mask[offset + 3];
            }, width, height);
        }

        /// <summary>
        /// Merges a greyscale alpha overlay into the base image's alpha channel.
        /// </summary>
        /// <param name="baseImage"></param>
        /// <param name="overlayImage"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        /// <exception cref="InvalidDataException"></exception>
        public static async Task AddAlphaOverlay(byte[] baseImage, byte[] overlayImage, int width, int height)
        {
            var expectedSize = width * height * 4;
            if (expectedSize != baseImage.Length
                || expectedSize != overlayImage.Length)
            {
                throw new InvalidDataException("Images were not the expected size.");
            }

            await ModifyPixels((int offset) =>
            {
                var overlayAlpha = overlayImage[offset + 3] / 255f;


                // Use red channel as base.
                var ov = overlayImage[offset + 0];

                if(overlayImage[offset + 0] != overlayImage[offset + 1]
                || overlayImage[offset + 0] != overlayImage[offset + 2])
                {
                    // Image is not already greyscaled.  Just take a simple naive greyscale of it.
                    ov = (byte) Math.Round((overlayImage[offset + 0] * (1f / 3f)) 
                    + (overlayImage[offset + 1] * (1f / 3f))
                    + (overlayImage[offset + 2] * (1f / 3f)));
                                
                }


                // Target is alpha channel
                var bv = baseImage[offset + 3];

                var c0 = ((ov * overlayAlpha) + (bv * (1 - overlayAlpha)));

                baseImage[offset + 3] = (byte)c0;
            }, width, height);
        }

        public static Color4 AlphaBlendExplicit(Color4 a, Color4 b, float blend)
        {
            var res = new Color4(1.0f, 1.0f, 1.0f, a.Alpha);

            for (int i = 0; i < 3; i++)
            {
                var baseValue = a[i];
                var overlayValue = b[i];

                var c0 = ((overlayValue * blend) + (baseValue * (1 - blend)));

                res[i] = c0;
            }
            return res;
        }

        /// <summary>
        /// Swizzle Red/Blue channels for switching between RGBA and BGRA formats.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public static async Task SwizzleRB(byte[] data, int width, int height)
        {
            Action<int> act = (i) =>
            {
                var original = data[i + 0];
                data[i + 0] = data[i + 2];
                data[i + 2] = original;
            };
            await ModifyPixels(act, width, height);
        }


        /// <summary>
        /// Converts a single channel into a greyscale map on all color channels.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public static async Task ExpandChannel(byte[] data, int channel, int width, int height, bool includeAlpha = false)
        {
            var max = includeAlpha ? 4 : 3;
            Action<int> act = (i) =>
            {
                var val = data[i + channel];
                for (int z = 0; z < max; z++)
                {
                    data[i + z] = val;
                }
            };
            await ModifyPixels(act, width, height);
        }

        internal static async Task FillChannel(byte[] data, int width, int height, int channel, byte value)
        {
            Action<int> act = (i) =>
            {
                data[i + channel] = value;
            };
            await ModifyPixels(act, width, height);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte RemapByte(byte value, byte oldMin, byte oldMax, byte newMin, byte newMax)
        {
            var z = (float)(value - oldMin) / (float)(oldMax - oldMin) * (float)(newMax - newMin) + newMin;
            return (byte)Math.Max(Math.Min(Math.Round(z), 255), 0);
        }

        public static async Task CreateIndexTexture(byte[] normalPixelData, byte[] indexPixelData, int width, int height)
        {
            await ModifyPixels((int offset) =>
            {
                var originalCset = normalPixelData[offset + 3];

                // We could try to run a blend on this to add more degrees of gradient potentially?
                var blendRem = originalCset % 34;
                var originalRow = originalCset / 17;

                if (blendRem > 17)
                {
                    if (blendRem < 26)
                    {
                        // Stays in this row, clamped to the closer row.
                        blendRem = 17;
                    }
                    else
                    {
                        // Goes to next row, clamped to the closer row.
                        blendRem = 0;
                        originalRow++;
                    }
                }

                var newBlend = (byte)(255 - Math.Round((blendRem / 17.0f) * 255.0f));
                var newRow = (byte) (((originalRow / 2) * 17) + 4);


                // RGBA format output.
                indexPixelData[offset + 0] = newRow;
                indexPixelData[offset + 1] = newBlend;
                indexPixelData[offset + 2] = 0;
                indexPixelData[offset + 3] = 255;
            }, width, height);
        }
        public static async Task CreateHairMaps(byte[] normalPixelData, byte[] maskPixelData, int width, int height)
        {
            await ModifyPixels((int offset) =>
            {
                var newGreen = (byte)(255 - maskPixelData[offset]);

                // Lift floor slightly.  This is a bit of artistic interpretation -
                // Except for the fact that the new engine will break with roughness value 0, so you have to 
                // at least bump 0 to 1.
                newGreen = RemapByte(newGreen, 0, 255, 10, 255);

                // Output is RGBA

                // Normal Blue - Highlight Color
                normalPixelData[offset + 2] = maskPixelData[offset + 3];

                // Mask Alpha - Albedo
                maskPixelData[offset + 3] = maskPixelData[offset + 0];
                // Mask Red - Specular Power
                maskPixelData[offset + 0] = maskPixelData[offset + 1];
                // Mask Green - Roughness
                maskPixelData[offset + 1] = newGreen;
                // Mask Blue - SSS Thickness Map
                maskPixelData[offset + 2] = 49;

            }, width, height);
        }
        public static async Task UpgradeGearMask(byte[] maskPixelData, int width, int height)
        {
            await ModifyPixels((int offset) =>
            {
                // Take the old gloss/metalness value and invert it.
                var newRoughness = (byte)(255 - maskPixelData[offset + 2]);

                // Output is RGBA

                var spec = maskPixelData[offset + 1];

                // Mask Blue - Diffuse
                maskPixelData[offset + 2] = maskPixelData[offset + 0];

                // Mask Red - Specular
                maskPixelData[offset + 0] = spec;

                // Mask Green - Roughness
                maskPixelData[offset + 1] = newRoughness;

            }, width, height);
        }

        /// <summary>
        /// Resizes two textures to the sum largest size between them, using ImageSharp to do the processing.
        /// </summary>
        /// <param name="texA"></param>
        /// <param name="texB"></param>
        /// <returns></returns>
        public static async Task<(byte[] TexA, byte[] TexB, int Width, int Height)> ResizeImages(XivTex texA, XivTex texB)
        {
            var maxW = Math.Max(texA.Width, texB.Width);
            var maxH = Math.Max(texA.Height, texB.Height);

            var timgA = ResizeImage(texA, maxW, maxH);
            var timgB = ResizeImage(texB, maxW, maxH);

            await Task.WhenAll(timgA, timgB);

            return (timgA.Result, timgB.Result, maxW, maxH);
        }

        public static async Task<(byte[] TexA, byte[] TexB, int Width, int Height)> ResizeImages(byte[] imgA, int widthA, int heightA, byte[] imgB, int widthB, int heightB)
        {
            var maxW = Math.Max(widthA, widthB);
            var maxH = Math.Max(heightA, heightB);

            var timgA = ResizeImage(imgA, widthA, heightA, maxW, maxH);
            var timgB = ResizeImage(imgB, widthB, heightB, maxW, maxH);

            await Task.WhenAll(timgA, timgB);

            return (timgA.Result, timgB.Result, maxW, maxH);
        }


        /// <summary>
        /// Resize a texture to the given size, returning the raw pixel data.
        /// </summary>
        /// <param name="tex"></param>
        /// <param name="newWidth"></param>
        /// <param name="newHeight"></param>
        /// <returns></returns>
        public static async Task<byte[]> ResizeImage(XivTex tex, int newWidth, int newHeight, bool nearestNeighbor = false)
        {
            var pixels = await tex.GetRawPixels();
            if (newWidth == tex.Width && newHeight == tex.Height)
            {
                return pixels;
            }
            return await ResizeImage(pixels, tex.Width, tex.Height, newWidth, newHeight, nearestNeighbor);
        }

        /// <summary>
        /// Resize an image to the given size, returning the raw pixel data.
        /// Assumes RGBA 8.8.8.8 data as the incoming byte array.
        /// </summary>
        /// <param name="tex"></param>
        /// <param name="newWidth"></param>
        /// <param name="newHeight"></param>
        /// <returns></returns>
        public static async Task<byte[]> ResizeImage(byte[] pixelData, int width, int height, int newWidth, int newHeight, bool nearestNeighbor = false)
        {
            return await Task.Run(() =>
            {
                using var img = Image.LoadPixelData<Rgba32>(pixelData, width, height);
                img.Mutate(x => x.Resize(
                    new ResizeOptions
                    {
                        Size = new Size(newWidth, newHeight),
                        PremultiplyAlpha = false,
                        Mode = ResizeMode.Stretch,
                        Sampler = nearestNeighbor ? KnownResamplers.NearestNeighbor : KnownResamplers.Bicubic,
                    })
                );
                var data = new byte[newWidth * newHeight * 4];
                img.CopyPixelDataTo(data.AsSpan());
                return data;
            });
        }
    }
}
