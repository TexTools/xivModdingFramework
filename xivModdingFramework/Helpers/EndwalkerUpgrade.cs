﻿using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Materials.DataContainers;
using xivModdingFramework.Materials.FileTypes;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.FileTypes;
using static xivModdingFramework.Materials.DataContainers.ShaderHelpers;
using xivModdingFramework.Mods;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.Enums;
using xivModdingFramework.Textures.FileTypes;
using xivModdingFramework.Textures;
using xivModdingFramework.Textures.DataContainers;
using System.Diagnostics;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.FileTypes.PMP;
using xivModdingFramework.Mods.Interfaces;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Tga;
using Point = SixLabors.ImageSharp.Point;
using xivModdingFramework.General.Enums;

namespace xivModdingFramework.Helpers
{
    public static class EndwalkerUpgrade
    {
        /// <summary>
        /// Enum representing the type of upgrade a given texture needs to go through.
        /// </summary>
        public enum EUpgradeTextureUsage
        {
            IndexMaps,
            HairMaps,
        };

        public struct UpgradeInfo
        {
            public EUpgradeTextureUsage Usage;
            public Dictionary<string, string> Files;
        }
        


        /// <summary>
        /// Performs Endwalker => Dawntrail Upgrades on an arbitrary set of internal files as part of a transaction.
        /// This is used primarily during Modpack installs.
        /// 
        /// Returns a collection of file upgrade information.
        /// </summary>
        /// <param name="filePaths"></param>
        /// <param name="source"></param>
        /// <param name="states"></param>
        /// <param name="progress"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        public static async Task<Dictionary<string, UpgradeInfo>> UpdateEndwalkerFiles(IEnumerable<string> filePaths, string source, Dictionary<string, TxFileState> states, bool includePartials = true, IProgress<(int current, int total, string message)> progress = null, ModTransaction tx = null)
        {

            var ret = new Dictionary<string, UpgradeInfo>();
#if ENDWALKER
            return ret;
#endif

            HashSet<string> _ConvertedTextures = new HashSet<string>();

            var fixableMdlsRegex = new Regex("chara\\/.*\\.mdl");
            var fixableMdls = filePaths.Where(x => fixableMdlsRegex.Match(x).Success).ToList();

            var fixableMtrlsRegex = new Regex("chara\\/.*\\.mtrl");
            var fixableMtrls = filePaths.Where(x => fixableMtrlsRegex.Match(x).Success).ToList();

            ret = await EndwalkerUpgrade.UpdateEndwalkerMaterials(fixableMtrls, source, tx, progress, _ConvertedTextures);

            var idx = 0;
            var total = fixableMdls.Count;
            foreach (var path in fixableMdls)
            {
                progress?.Report((idx, total, "Updating Endwalker Models..."));
                idx++;
                await EndwalkerUpgrade.UpdateEndwalkerModel(path, source, tx);
            }

            if (includePartials)
            {
                progress?.Report((0, total, "Updating Endwalker partial Hair Mods..."));
                await EndwalkerUpgrade.CheckImportForOldHairJank(filePaths.ToList(), source, tx, _ConvertedTextures);

                progress?.Report((0, total, "Updating Endwalker partial Eye Mods..."));
                foreach (var path in filePaths)
                {
                    await EndwalkerUpgrade.UpdateEyeMask(path, source, tx, _ConvertedTextures);
                }
            }


            progress?.Report((0, total, "Endwalker Upgrades Complete..."));
            return ret;
        }

        /// <summary>
        /// Performs Endwalker => Dawntrail Upgrades on an arbitrary set of files that do not exist in the transaction file system.
        /// This is used primarily for in-place modpack upgrades.
        /// 
        /// Returns a collection of file upgrade information to be used for texture upgrades.
        /// NOTE : Does not run texture upgrades in this pass for Materials.
        /// </summary>
        /// <param name="files"></param>
        /// <returns></returns>
        public static async Task<Dictionary<string, UpgradeInfo>> UpdateEndwalkerFiles(Dictionary<string, FileStorageInformation> files, IProgress<(int current, int total, string message)> progress = null)
        {
            var ret = new Dictionary<string, UpgradeInfo>();
#if ENDWALKER
            return ret;
#endif

            HashSet<string> _ConvertedTextures = new HashSet<string>();
            var filePaths = files.Keys;

            var fixableMdlsRegex = new Regex("chara\\/.*\\.mdl");
            var fixableMdls = filePaths.Where(x => fixableMdlsRegex.Match(x).Success).ToList();

            var fixableMtrlsRegex = new Regex("chara\\/.*\\.mtrl");
            var fixableMtrls = filePaths.Where(x => fixableMtrlsRegex.Match(x).Success).ToList();

            var source = "Unused";
            ModTransaction tx = null;

            ret = await EndwalkerUpgrade.UpdateEndwalkerMaterials(fixableMtrls, source, tx, progress, _ConvertedTextures, files);

            var idx = 0;
            var total = fixableMdls.Count;
            foreach (var path in fixableMdls)
            {
                progress?.Report((idx, total, "Updating Endwalker Models..."));
                idx++;
                await EndwalkerUpgrade.UpdateEndwalkerModel(path, source, tx, files);
            }

            // Texture-only upgrades are ignored in this route, and handled by the TT modpack upgrader afterwards.

            return ret;

        }


        public static async Task UpdateEndwalkerModel(string path, string source, ModTransaction tx, Dictionary<string, FileStorageInformation> files = null)
        {
            var uncomp = await ResolveFile(path, files, tx);

            using (var ms = new MemoryStream(uncomp))
            {
                using (var br = new BinaryReader(ms))
                {
                    using (var bw = new BinaryWriter(ms))
                    {
                        var anyChanges = EndwalkerUpgrade.FastMdlv6Upgrade(br, bw);
                        if (!anyChanges)
                        {
                            return;
                        }
                    }
                }
            }

            await WriteFile(uncomp, path, files, tx, source);
        }

        /// <summary>
        /// Reads an uncompressed v5 MDL and retrieves the offsets to the bone lists, in order to update them.
        /// </summary>
        /// <param name="br"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        private static bool FastMdlv6Upgrade(BinaryReader br, BinaryWriter bw, long offset = -1)
        {
#if ENDWALKER
            return false;
#endif

            if(offset < 0)
            {
                offset = br.BaseStream.Position;
            }

            br.BaseStream.Seek(offset, SeekOrigin.Begin);
            bw.BaseStream.Seek(offset, SeekOrigin.Begin);

            var version = br.ReadUInt16();
            if (version != 5)
            {
                return false;
            }


            br.BaseStream.Seek(offset + 12, SeekOrigin.Begin);
            var meshCount = br.ReadUInt16();

            if(meshCount == 0)
            {
                // Uhh..?
                return false;
            }

            br.BaseStream.Seek(offset + 64, SeekOrigin.Begin);
            var lodOffset = br.BaseStream.Position;
            var lods = br.ReadByte();

            var endOfVertexHeaders = offset + Mdl._MdlHeaderSize + (Mdl._VertexDataHeaderSize * meshCount);

            br.BaseStream.Seek(endOfVertexHeaders + 4, SeekOrigin.Begin);
            var pathBlockSize = br.ReadUInt32();

            br.ReadBytes((int)pathBlockSize);

            // Mesh data block.
            var mdlDataOffset = br.BaseStream.Position;
            var mdlData = MdlModelData.Read(br);

            if(mdlData.BoneSetCount == 0 || mdlData.BoneCount == 0)
            {
                // Not 100% sure how to update boneless meshes to v6 yet, so don't upgrade for safety.
                return false;
            }

            br.ReadBytes(mdlData.ElementIdCount * 32);

            // LoD Headers
            br.ReadBytes(60 * 3);

            if ((mdlData.Flags2 & EMeshFlags2.HasExtraMeshes) != 0)
            {
                // Extra Mesh Info Block.
                br.ReadBytes(60);
            }


            // Mesh Group headers.
            br.ReadBytes(36 * meshCount);

            // Attribute pointers.
            br.ReadBytes(4 * mdlData.AttributeCount);

            // Mesh Part information.
            br.ReadBytes(16 * mdlData.MeshPartCount);

            // Show Mesh Part Information.
            br.ReadBytes(12 * mdlData.TerrainShadowPartCount);

            // Material Pointers.
            br.ReadBytes(4 * mdlData.MaterialCount);

            // Bone Pointers.
            br.ReadBytes(4 * mdlData.BoneCount);


            var bonesetStart = br.BaseStream.Position;
            var boneSets = new List<(long Offset, ushort BoneCount, byte[] data)>();
            for (int i = 0; i < mdlData.BoneSetCount; i++)
            {
                // Bone List
                var data = br.ReadBytes(64 * 2);

                // Bone List Size.
                var countOffset = br.BaseStream.Position;
                var count = br.ReadUInt32();

                boneSets.Add((countOffset, (ushort)count, data));
            }

            // Write version information.
            bw.BaseStream.Seek(offset, SeekOrigin.Begin);
            bw.Write((ushort)6);


            // Write LoD count.
            bw.BaseStream.Seek(lodOffset, SeekOrigin.Begin);
            bw.Write((byte)1);


            // Write bone set size.
            short boneSetSize = (short) (64 * mdlData.BoneSetCount);
            mdlData.BoneSetSize = (short)boneSetSize;
            mdlData.LoDCount = 1;
            bw.BaseStream.Seek(mdlDataOffset, SeekOrigin.Begin);
            mdlData.Write(bw);

            // Upgrade bone sets to v6 format.
            bw.BaseStream.Seek(bonesetStart, SeekOrigin.Begin);
            List<long> headerOffsets = new List<long>();
            foreach (var bs in boneSets)
            {
                headerOffsets.Add(bw.BaseStream.Position);
                bw.Write((short)0);
                bw.Write((short)bs.BoneCount);
            }

            var idx = 0;
            foreach(var bs in boneSets)
            {
                var headerOffset = headerOffsets[idx];
                var distance = (short)((bw.BaseStream.Position - headerOffset) / 4);

                bw.Write(bs.data, 0, bs.BoneCount * 2);

                if(bs.BoneCount % 2 != 0)
                {
                    // Expected Padding
                    bw.Write((short)0);
                }


                var pos = bw.BaseStream.Position;
                bw.BaseStream.Seek(headerOffset, SeekOrigin.Begin);
                bw.Write(distance);
                bw.BaseStream.Seek(pos, SeekOrigin.Begin);

                idx++;
            }

            var end = bonesetStart + boneSetSize;
            while(bw.BaseStream.Position < end)
            {
                // Fill out the remainder of the block with 0s.
                bw.Write((byte)0);
            }

            return true;
        }

        #region Endwalker => Dawntrail Material Conversion
        private static async Task<Dictionary<string, UpgradeInfo>> UpdateEndwalkerMaterials(List<string> paths, string source, ModTransaction tx, IProgress<(int current, int total, string message)> progress, HashSet<string> _ConvertedTextures = null, Dictionary<string, FileStorageInformation> files = null)
        {
            var ret = new Dictionary<string, UpgradeInfo>();
#if ENDWALKER
            return ret;
#endif
            if (_ConvertedTextures == null)
            {
                _ConvertedTextures = new HashSet<string>();
            }
            var total = paths.Count;
            var materials = new List<XivMtrl>();
            var i = 0;
            foreach (var path in paths)
            {
                progress?.Report((i, total, "Scanning for Endwalker Materials..."));
                i++;

                var file = await ResolveFile(path, files, tx);
                if(file == null)
                {
                    continue;
                }

                var mtrl = Mtrl.GetXivMtrl(file, path);
                if (!DoesMtrlNeedDawntrailUpdate(mtrl))
                {
                    continue;
                }

                materials.Add(mtrl);
            }

            total = materials.Count;
            i = 0;
            foreach (var mtrl in materials)
            {
                progress?.Report((i, total, "Updating Endwalker Materials..."));
                var missingFiles = await UpdateEndwalkerMaterial(mtrl, source, true, tx, _ConvertedTextures, files);

                // Merge missing files in.
                foreach(var kv in missingFiles)
                {
                    if (!ret.ContainsKey(kv.Key))
                    {
                        ret.Add(kv.Key, kv.Value);
                    }
                }

                i++;
            }

            return ret;
        }

        private const uint _OldShaderConstant1 = 0x36080AD0; // == 1
        private const uint _OldShaderConstant2 = 0x992869AB; // == 3 (skin) or 4 (hair)

        public static bool DoesMtrlNeedDawntrailUpdate(XivMtrl mtrl)
        {
            if (mtrl.ColorSetData != null && mtrl.ColorSetData.Count == 256)
            {
                // Any old colorset, regardless of shader needs to be updated.
                return true;
            }

            // OLD

            if (mtrl.ShaderPack == EShaderPack.Skin)
            {
                // NEW
                var sheenRate = 0x800EE35F;
                var SSAOMask = 0xB7FA33E2;


                if (mtrl.ShaderConstants.Any(x => x.ConstantId == _OldShaderConstant1)
                    && mtrl.ShaderConstants.Any(x => x.ConstantId == _OldShaderConstant2))
                {
                    return true;
                }
            }

            if (mtrl.ShaderPack == EShaderPack.Hair)
            {
                if (mtrl.ShaderConstants.Any(x => x.ConstantId == _OldShaderConstant1)
                    && mtrl.ShaderConstants.Any(x => x.ConstantId == _OldShaderConstant2))
                {
                    return true;
                }
            }

            return false;
        }

        public static async Task<Dictionary<string, UpgradeInfo>> UpdateEndwalkerMaterial(XivMtrl mtrl, string source, bool createTextures, ModTransaction tx = null, HashSet<string> _ConvertedTextures = null, Dictionary<string, FileStorageInformation> files = null)
        {
            var ret = new Dictionary<string, UpgradeInfo>();

            if (!createTextures)
            {
                // Should we really allow this?
                throw new NotImplementedException();
            }

            if (!DoesMtrlNeedDawntrailUpdate(mtrl))
            {
                return ret;
            }
            if (files == null)
            {
                var boiler = await TxBoiler.BeginWrite(tx);
                tx = boiler.Transaction;
                try
                {
                    ret = await UpdateEndwalkerMaterial(mtrl, source, tx, _ConvertedTextures);
                    await boiler.Commit();
                    return ret;
                }
                catch
                {
                    await boiler.Catch();
                    throw;
                }
            } else
            {
                return await UpdateEndwalkerMaterial(mtrl, source, null, _ConvertedTextures, files);
            }
        }

        /// <summary>
        /// Updates an individual material, potentially as part of a larger block of tasks.
        /// </summary>
        /// <param name="mtrl"></param>
        /// <param name="states"></param>
        /// <param name="source"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        private static async Task<Dictionary<string, UpgradeInfo>> UpdateEndwalkerMaterial(XivMtrl mtrl, string source, ModTransaction tx, HashSet<string> _ConvertedTextures = null, Dictionary<string, FileStorageInformation> files = null)
        {
            var ret = new Dictionary<string, UpgradeInfo>();
            if (_ConvertedTextures == null)
            {
                _ConvertedTextures = new HashSet<string>();
            }

            if (mtrl.ColorSetDataSize > 0)
            {
                var texInfo = await UpdateEndwalkerColorset(mtrl, source, tx, files);

                ret.Add(texInfo.normalToCreateFrom, new UpgradeInfo()
                {
                     Usage = EUpgradeTextureUsage.IndexMaps,
                      Files = new Dictionary<string, string>()
                      {
                          { "normal", texInfo.normalToCreateFrom },
                          { "index", texInfo.indexTextureToCreate }
                      },
                });

                if (!_ConvertedTextures.Contains(texInfo.normalToCreateFrom))
                {
                    var data = await CreateIndexFromNormal(texInfo.indexTextureToCreate, texInfo.normalToCreateFrom, tx, files);
                    if (files == null)
                    {
                        if (data.data != null)
                        {
                            await WriteFile(data.data, data.indexFilePath, files, tx, source);
                        }
                        else
                        {

                            // Resave the material with texture validation to create dummy textures if none exist.
                            await Mtrl.ImportMtrl(mtrl, null, source, true, tx);
                        }
                    }
                    _ConvertedTextures.Add(texInfo.normalToCreateFrom);
                }
            }
            else if (mtrl.ShaderPack == EShaderPack.Hair)
            {
                ret = await UpdateEndwalkerHairMaterial(mtrl, source, tx, _ConvertedTextures, files);
            }
            return ret;
        }

        /// <summary>
        /// Updates a given Endwalker style Material to a Dawntrail style material, returning a tuple containing the Index Map that should be created after,
        /// and the normal map that should be used in the creation.
        /// </summary>
        /// <param name="mtrl"></param>
        /// <param name="updateShaders"></param>
        /// <param name="source"></param>
        /// <returns></returns>
        private static async Task<(string indexTextureToCreate, string normalToCreateFrom)> UpdateEndwalkerColorset(XivMtrl mtrl, string source, ModTransaction tx, Dictionary<string, FileStorageInformation> files = null)
        {
            if (mtrl.ColorSetData.Count != 256)
            {
                // This is already upgraded.
                return (null, null);
            }

            if (mtrl.ShaderPack == EShaderPack.Character)
            {
                mtrl.ShaderPack = EShaderPack.CharacterLegacy;
            }
            else
            {
                // Don't need to change the shaderpack for anything else here.
            }

            if (mtrl.ColorSetData == null)
            {
                await Mtrl.ImportMtrl(mtrl, null, source, false, tx);
                return (null, null);
            }

            // Update Colorset
            List<Half> newData = new List<Half>(1024);
            for (int i = 0; i < mtrl.ColorSetData.Count; i += 16)
            {
                var pixel = i + 0;

                // Diffuse Pixel
                newData.Add(mtrl.ColorSetData[pixel + 0]);
                newData.Add(mtrl.ColorSetData[pixel + 1]);
                newData.Add(mtrl.ColorSetData[pixel + 2]);
                newData.Add(mtrl.ColorSetData[pixel + 7]);  // SE flipped Specular Power and Gloss values for some reason.

                pixel += 4;

                // Specular Pixel
                newData.Add(mtrl.ColorSetData[pixel + 0]);
                newData.Add(mtrl.ColorSetData[pixel + 1]);
                newData.Add(mtrl.ColorSetData[pixel + 2]);
                newData.Add(mtrl.ColorSetData[pixel - 1]);  // SE flipped Specular Power and Gloss values for some reason.

                pixel += 4;
                // Emissive Pixel
                newData.Add(mtrl.ColorSetData[pixel + 0]);
                newData.Add(mtrl.ColorSetData[pixel + 1]);
                newData.Add(mtrl.ColorSetData[pixel + 2]);
                newData.Add(1.0f);

                //Unknown1
                newData.Add(0);
                newData.Add(0);
                newData.Add(2.0f);
                newData.Add(0);

                //Unknown2
                newData.Add(0.5f);
                newData.Add(0);
                newData.Add(0);
                newData.Add(0);

                //Unknown3
                newData.Add(0);
                newData.Add(0);
                newData.Add(0);
                newData.Add(0);

                //Unknown + subsurface material id
                newData.Add(0);
                newData.Add(mtrl.ColorSetData[pixel + 3]);
                newData.Add(1.0f);  //  Subsurface Material Alpha
                newData.Add(0);

                pixel += 4;
                //Subsurface scaling data.
                newData.Add(mtrl.ColorSetData[pixel + 0]);
                newData.Add(mtrl.ColorSetData[pixel + 1]);
                newData.Add(mtrl.ColorSetData[pixel + 2]);
                newData.Add(mtrl.ColorSetData[pixel + 3]);

                // Add a blank row after, since only populating every other row.
            }

            for (int i = 0; i < 16; i++)
            {
                // Add empty rows after.
                newData.AddRange(GetDefaultColorsetRow());
            }

            mtrl.ColorSetData = newData;
            if (mtrl.ColorSetDyeData != null && mtrl.ColorSetDyeData.Length > 0)
            {
                // Update Dye information.
                var newDyeData = new byte[128];
                // Update old dye information
                for (int i = 0; i < 16; i++)
                {
                    var oldOffset = i * 2;
                    var newOffset = (i * 2) * 4;

                    var newDyeBlock = (uint)0;
                    var oldDyeBlock = BitConverter.ToUInt16(mtrl.ColorSetDyeData, oldOffset);

                    // Old dye bitmask was 5 bits long.
                    uint dyeBits = (uint)(oldDyeBlock & 0x1F);
                    uint oldTemplate = (uint)(oldDyeBlock >> 5);

                    newDyeBlock |= (oldTemplate << 16);
                    newDyeBlock |= dyeBits;

                    var newDyeBytes = BitConverter.GetBytes(newDyeBlock);

                    Array.Copy(newDyeBytes, 0, newDyeData, newOffset, newDyeBytes.Length);
                }

                mtrl.ColorSetDyeData = newDyeData;
            }


            var normalTex = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Normal);
            var idTex = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Index);
            string idPath = null;
            string normalPath = null;


            // If we don't have an ID Texture, and we have a colorset + normal map, create one.
            if (normalTex != null && idTex == null)
            {
                idPath = normalTex.Dx11Path.Replace(".tex", "_id.tex");
                normalPath = normalTex.Dx11Path;

                var tex = new MtrlTexture();
                tex.TexturePath = idPath;
                tex.Sampler = new TextureSampler()
                {
                    SamplerSettingsRaw = 0x000F8340,
                    SamplerIdRaw = 1449103320,
                };
                mtrl.Textures.Add(tex);
            }

            var specTex = mtrl.Textures.FirstOrDefault(x => x.Sampler.SamplerId == ESamplerId.g_SamplerSpecular);
            if (specTex != null)
            {
                specTex.Sampler.SamplerId = ESamplerId.g_SamplerMask;
            }

            var data = Mtrl.XivMtrlToUncompressedMtrl(mtrl);
            await WriteFile(data, mtrl.MTRLPath, files, tx);

            return (idPath, normalPath);
        }


        /// <summary>
        /// Creates the actual index file data from the constituent parts.
        /// Returns the bytes of an uncompressed Tex file.
        /// </summary>
        /// <param name="indexPath"></param>
        /// <param name="sourceNormalPath"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        private static async Task<(string indexFilePath, byte[] data)> CreateIndexFromNormal(string indexPath, string sourceNormalPath, ModTransaction tx, Dictionary<string, FileStorageInformation> files)
        {

            var data = await ResolveFile(sourceNormalPath, files, tx);
            if (data == null)
            {
                // Can't create an index.  Return null to indicate mtrl should be resaved with texture validation.
                return (indexPath, null);
            }

            // Read normal file.
            var normalTex = XivTex.FromUncompressedTex(data);
            var normalData = await normalTex.GetRawPixels();

            var indexData = new byte[normalTex.Width * normalTex.Height * 4];
            await TextureHelpers.CreateIndexTexture(normalData, indexData, normalTex.Width, normalTex.Height);

            // Create MipMaps (And DDS header that we don't really need)
            indexData = await Tex.ConvertToDDS(indexData, XivTexFormat.A8R8G8B8, true, normalTex.Height, normalTex.Width, true);

            // Convert DDS to uncompressed Tex
            indexData = Tex.DDSToUncompressedTex(indexData);

            return (indexPath, indexData);
        }

        private static async Task<Dictionary<string, UpgradeInfo>> UpdateEndwalkerHairMaterial(XivMtrl mtrl, string source, ModTransaction tx, HashSet<string> _ConvertedTextures, Dictionary<string, FileStorageInformation> files)
        {
            var ret = new Dictionary<string, UpgradeInfo>();
            var normalTexSampler = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Normal);
            var maskTexSampler = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Mask);

            if (normalTexSampler == null || maskTexSampler == null)
            {
                // Not resolveable.  Mtrl has some weird stuff going on.
                return ret;
            }

            // Arbitrary base game hair file to use to replace our shader constants.
            var constantBase = await Mtrl.GetXivMtrl("chara/human/c0801/obj/hair/h0115/material/v0001/mt_c0801h0115_hir_a.mtrl", true, tx);
            mtrl.ShaderConstants = constantBase.ShaderConstants;

            ret.Add(normalTexSampler.Dx11Path, new UpgradeInfo()
            {
                Usage = EUpgradeTextureUsage.HairMaps,
                Files = new Dictionary<string, string>()
                      {
                          { "normal", normalTexSampler.Dx11Path },
                          { "mask", maskTexSampler.Dx11Path }
                      },
            });

            if (files == null)
            {
                if (await Exists(normalTexSampler.Dx11Path, files, tx) && await Exists(maskTexSampler.Dx11Path, files, tx))
                {
                    await UpdateEndwalkerHairTextures(normalTexSampler.Dx11Path, maskTexSampler.Dx11Path, source, tx, _ConvertedTextures, files);

                    var mtrlData = Mtrl.XivMtrlToUncompressedMtrl(mtrl);
                    await WriteFile(mtrlData, mtrl.MTRLPath, files, tx, source);
                }
                else
                {
                    // Use slower material import path here to do texture stubbing.
                    await Mtrl.ImportMtrl(mtrl, null, source, true, tx);
                }
            }

            return ret;

        }

        private static async Task UpdateEndwalkerHairTextures(string normalPath, string maskPath, string source, ModTransaction tx, HashSet<string> _ConvertedTextures, Dictionary<string, FileStorageInformation> files)
        {
            if (_ConvertedTextures == null)
            {
                _ConvertedTextures = new HashSet<string>();
            }
            var oldNormData = await ResolveFile(normalPath, files, tx);
            var oldMaskData = await ResolveFile(maskPath, files, tx);

            if(oldNormData == null || oldMaskData == null)
            {
                // Shouldn't ever be able to hit this, but if we do, nothing to be done about it.
                throw new FileNotFoundException("Unable to properly resolve existing Hair Normal/Mask texture.");
            }


            // Read normal file.
            var normalTex = XivTex.FromUncompressedTex(oldNormData);
            var maskTex = XivTex.FromUncompressedTex(oldMaskData);

            // Resize to be same size.
            var data = await TextureHelpers.ResizeImages(normalTex, maskTex);

            // Create the final hair pixel data.
            await TextureHelpers.CreateHairMaps(data.TexA, data.TexB, data.Width, data.Height);

            if (!_ConvertedTextures.Contains(normalPath))
            {
                // Normal
                var normalData = await Tex.ConvertToDDS(data.TexA, XivTexFormat.A8R8G8B8, true, data.Height, data.Width, true);
                normalData = Tex.DDSToUncompressedTex(normalData);
                await WriteFile(normalData, normalPath, files, tx, source);
                _ConvertedTextures.Add(normalPath);
            }

            if (!_ConvertedTextures.Contains(maskPath))
            {
                // Mask
                var maskData = await Tex.ConvertToDDS(data.TexB, XivTexFormat.A8R8G8B8, true, data.Height, data.Width, true);
                maskData = Tex.DDSToUncompressedTex(maskData);
                await WriteFile(maskData, maskPath, files, tx, source);
                _ConvertedTextures.Add(maskPath);
            }
        }

        private static Half[] GetDefaultColorsetRow()
        {
            var row = new Half[32];

            // Diffuse pixel base
            for (int i = 0; i < 8; i++)
            {
                row[i] = 1.0f;
            }

            row[11] = 1.0f;
            row[12] = 0.09997559f;
            row[13] = 0.1999512f;
            row[14] = 5.0f;


            row[16] = 0.5f;
            row[25] = 0.0078125f;
            row[26] = 1.0f;


            row[7 * 4 + 0] = 16.0f;
            row[7 * 4 + 3] = 16.0f;
            return row;
        }

        // Resolves for old-style default hair textures.
        private static Regex OldHairTextureRegex = new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/hair\\/h[0-9]{4}\\/texture\\/(?:--)?c([0-9]{4})h([0-9]{4})_hir_([ns])\\.tex");
        private static Regex OldHairMaterialRegex = new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/hair\\/h[0-9]{4}\\/material\\/v0001\\/mt_c([0-9]{4})h([0-9]{4})_hir_a\\.mtrl");
        private static string OldHairMaterialFormat = "chara/human/c{0}/obj/hair/h{1}/material/v0001/mt_c{0}h{1}_hir_a.mtrl";
        private static string NewHairTextureFormat = "chara/human/c{0}/obj/hair/h{1}/texture/c{0}h{1}_hir_{2}.tex";


        /// <summary>
        /// This function does some jank analysis of inbound hair texture files,
        /// automatically copying them to SE's new pathing /if/ they were included by themselves, without their 
        /// associated default material.
        /// </summary>
        /// <param name="files"></param>
        /// <param name="source"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        public static async Task CheckImportForOldHairJank(List<string> files, string source, ModTransaction tx, HashSet<string> _ConvertedTextures, Dictionary<string, FileStorageInformation> fileInfos = null)
        {
            var results = new Dictionary<int, Dictionary<int, List<(string Path, XivTexType TexType)>>>();

            var materials = new List<(int Race, int Hair)>();
            foreach (var file in files)
            {
                var matMatch = OldHairMaterialRegex.Match(file);
                if (matMatch.Success)
                {
                    var rid = Int32.Parse(matMatch.Groups[1].Value);
                    var hid = Int32.Parse(matMatch.Groups[2].Value);
                    materials.Add((rid, hid));
                    continue;
                }

                var match = OldHairTextureRegex.Match(file);
                if (!match.Success) continue;

                var raceId = Int32.Parse(match.Groups[1].Value);
                var hairId = Int32.Parse(match.Groups[2].Value);
                var tex = match.Groups[3].Value;
                if (!results.ContainsKey(raceId))
                {
                    results.Add(raceId, new Dictionary<int, List<(string Path, XivTexType TexType)>>());
                }

                if (!results[raceId].ContainsKey(hairId))
                {
                    results[raceId].Add(hairId, new List<(string Path, XivTexType TexType)>());
                }

                var tt = tex == "n" ? XivTexType.Normal : XivTexType.Specular;

                if (results[raceId][hairId].Any(x => x.TexType == tt))
                {
                    var prev = results[raceId][hairId].First(x => x.TexType == tt);
                    if (prev.Path.Contains("--"))
                    {
                        // Dx11 wins out.
                        continue;
                    }
                    else
                    {
                        results[raceId][hairId].RemoveAll(x => x.TexType == tt);
                    }
                }
                results[raceId][hairId].Add((file, tt));
            }

            // Winnow list to only entries with both types, that also lack material files.
            var races = results.Keys.ToList();
            foreach (var r in races)
            {
                var hairs = results[r].Keys.ToList();
                foreach (var h in hairs)
                {
                    if (results[r][h].Count < 2)
                    {
                        results[r].Remove(h);
                    }
                    else if (materials.Any(x => x.Hair == h && x.Race == r))
                    {
                        results[r].Remove(h);
                    }
                }

                if (results[r].Count == 0)
                {
                    results.Remove(r);
                }
            }

            if (results.Count == 0) return;

            foreach (var rKv in results)
            {
                var race = rKv.Key.ToString("D4");
                foreach (var hKv in rKv.Value)
                {
                    var hair = hKv.Key.ToString("D4");
                    var material = string.Format(OldHairMaterialFormat, race, hair);
                    var root = await XivCache.GetFirstRoot(material);
                    IItem item = null;
                    if (root != null)
                    {
                        item = root.GetFirstItem();
                    }

                    foreach (var tex in hKv.Value)
                    {
                        var suffix = tex.TexType == XivTexType.Normal ? "norm" : "mask";
                        var newPath = string.Format(NewHairTextureFormat, race, hair, suffix);

                        if (files.Contains(newPath))
                        {
                            continue;
                        }

                        if (fileInfos != null)
                        {
                            var data = await ResolveFile(tex.Path, fileInfos, tx);
                            await WriteFile(data, newPath, fileInfos, tx, source);
                        }
                        else
                        {
                            await Dat.CopyFile(tex.Path, newPath, source, true, item, tx);
                        }

                        files.Add(newPath);
                    }

                    var newNorm = string.Format(NewHairTextureFormat, race, hair, "norm");
                    var newMask = string.Format(NewHairTextureFormat, race, hair, "mask");
                    await UpdateEndwalkerHairTextures(newNorm, newMask, source, tx, _ConvertedTextures, fileInfos);
                }
            }
        }

        #endregion




        private static async Task<bool> Exists(string path, Dictionary<string, FileStorageInformation> files, ModTransaction tx, bool modifiedOnly = false)
        {
            if(files != null && files.ContainsKey(path))
            {
                return true;
            }

            if (modifiedOnly)
            {
                if(tx != null)
                {
                    if (tx.ModifiedFiles.Contains(path))
                    {
                        return true;
                    }
                }
            }
            else
            {
                if (tx != null && await tx.FileExists(path))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Handles the boilerplate of resolving a file between a file list and transaction store.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="files"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        private static async Task<byte[]> ResolveFile(string path, Dictionary<string, FileStorageInformation> files, ModTransaction tx)
        {

            if(files != null && files.ContainsKey(path))
            {
                return await TransactionDataHandler.GetUncompressedFile(files[path]);
            }

            if(tx != null && await tx.FileExists(path))
            {
                return await tx.ReadFile(path);
            }

            return null;
        }


        /// <summary>
        /// Handles the boilerplate of writing a file to TX or temporary file.
        /// </summary>
        /// <param name="uncompData"></param>
        /// <param name="path"></param>
        /// <param name="files"></param>
        /// <param name="tx"></param>
        /// <param name="sourceApplication"></param>
        /// <returns></returns>
        private static async Task WriteFile(byte[] uncompData, string path, Dictionary<string, FileStorageInformation> files, ModTransaction tx, string sourceApplication="Unknown")
        {
            if(files != null)
            {
                var exPath = IOUtil.GetFrameworkTempFile();

                var info = new FileStorageInformation()
                {
                    FileSize = uncompData.Length,
                    RealOffset = 0,
                    RealPath = exPath,
                    StorageType = EFileStorageType.UncompressedIndividual,
                };

                File.WriteAllBytes(exPath, uncompData);

                if (files.ContainsKey(path)){
                    files[path] = info;
                }
                else
                {
                    files.Add(path, info);
                }
            }
            else
            {
                await Dat.WriteModFile(uncompData, path, sourceApplication, null, tx, false);
            }
        }

        /// <summary>
        /// Function to scan for any missing texture pieces in a file collection.
        /// Returns a hash set of the files which were found/upgraded.
        /// </summary>
        /// <param name="files"></param>
        /// <param name="missing"></param>
        /// <returns></returns>
        public static async Task UpgradeRemainingTextures(Dictionary<string, FileStorageInformation> files, Dictionary<string, UpgradeInfo> upgrades)
        {
            foreach(var kv in upgrades)
            {
                var upgrade = kv.Value;

                if(upgrade.Usage == EUpgradeTextureUsage.IndexMaps)
                {
                    if (files.ContainsKey(upgrade.Files["normal"]))
                    {
                        var res = await CreateIndexFromNormal(upgrade.Files["index"], upgrade.Files["normal"], null, files);
                        if(res.data == null)
                        {
                            throw new InvalidDataException("Failed to create Normal map from Index file");
                        }
                        await WriteFile(res.data, res.indexFilePath, files, null);
                    }
                } else if(upgrade.Usage == EUpgradeTextureUsage.HairMaps)
                {
                    if (files.ContainsKey(upgrade.Files["normal"])
                        && files.ContainsKey(upgrade.Files["mask"]))
                    {

                        await UpdateEndwalkerHairTextures(upgrade.Files["normal"], upgrade.Files["mask"], "Unused", null, null, files);

                    } else if(files.ContainsKey(upgrade.Files["normal"])
                        || files.ContainsKey(upgrade.Files["mask"]))
                    {
                        // One but not both.
                        throw new FileNotFoundException("Unable to upgrade Hair Normal/Mask - Normal/Mask do not exist in the same file set.\n" + upgrade.Files["normal"] +"\n" + upgrade.Files["mask"]);
                    }
                }

            }
        }



        private static TgaEncoder Encoder = new TgaEncoder()
        {
            BitsPerPixel = TgaBitsPerPixel.Pixel32,
            Compression = TgaCompression.None
        };


        /// <summary>
        /// Takes the raw data of an old Endwalker mask image, and converts it into a Dawntrail style diffuse.
        /// This only makes use of the Mask.Red channel data, all other data is discarded as it cannot be replicated.
        /// </summary>
        /// <param name="maskData"></param>
        /// <param name="originalMaskWidth"></param>
        /// <param name="originalMaskHeight"></param>
        /// <returns></returns>
        public static async Task<(byte[] PixelData, int Width, int Height)> ConvertEyeMaskToDiffuse(byte[] maskData, int originalMaskWidth, int originalMaskHeight)
        {
            // The Ratio of Iris to Sclera is 92/100 in old textures, but is
            // 1/2.55 roughly in the new.
            // Multiplying these terms together results in a ratio of roughly .44
            double ratio = 0.442;


            // In order to guarantee we're resizing up, not down, and are still a power-of-two, we have to 4x the
            // dimensions of the original mask file, as a 2x would result in some amount of compression.
            var w = originalMaskWidth * 4;
            var h = originalMaskHeight * 4;

            var irisW = (int)(w * ratio);
            var irisH = (int)(h * ratio);

            // Pull the base game eye files as our baseline.
            var rTx = ModTransaction.BeginReadonlyTransaction();
            var baseDiffuseTex = await Tex.GetXivTex("chara/common/texture/eye/eye01_base.tex", true, rTx);
            var frameTex = await Tex.GetXivTex("chara/common/texture/eye/eye01_mask.tex", true, rTx);

            var diffuseData = await baseDiffuseTex.GetRawPixels();
            var frameData = await frameTex.GetRawPixels();

            // Convert mask to greyscale copy of just the red channel data.
            await TextureHelpers.ExpandChannel(maskData, 0, originalMaskWidth, originalMaskHeight);
            var resizedMask = await TextureHelpers.ResizeImage(maskData, originalMaskWidth, originalMaskHeight, irisW, irisH);

            // Convert eye frame to just the actual framing information
            await TextureHelpers.ExpandChannel(frameData, 2, frameTex.Width, frameTex.Height, true);


            // Resize and blur the frame slightly.
            using (var frameImage = Image.LoadPixelData<Rgba32>(frameData, frameTex.Width, frameTex.Height))
            {
                var resizeOptions = new ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size(w, h),
                    PremultiplyAlpha = false,
                    Mode = SixLabors.ImageSharp.Processing.ResizeMode.Stretch,
                    Sampler = SixLabors.ImageSharp.Processing.KnownResamplers.NearestNeighbor,
                };
                frameImage.Mutate(x => x.Resize(resizeOptions));

                // Box-blur the mask just a hair to reduce the harshness at the edges.
                // This looks a little nicer than just bicubic upscaling the mask.
                frameImage.Mutate(x => x.BoxBlur(w / 128));
                frameData = IOUtil.GetImageSharpPixels(frameImage);
                frameImage.SaveAsTga("E:\\img.tga", Encoder);
            }

            var maskPixels = new byte[w * h * 4];

            // Draw the mask onto a new blank canvas and get the byte data back.
            using (var blankImage = Image.LoadPixelData<Rgba32>(maskPixels, w, h))
            {
                using (var maskImage = Image.LoadPixelData<Rgba32>(resizedMask, irisW, irisH))
                {
                    var pt = new Point((w / 2) - (irisW / 2), (h / 2) - (irisH / 2));
                    blankImage.Mutate(x => x.DrawImage(maskImage, pt, 1.0f));

                    maskPixels = IOUtil.GetImageSharpPixels(blankImage);

                }
            }


            // Use the frame to mask the mask.
            await TextureHelpers.MaskImage(maskPixels, frameData, w, h);

            // And finally, resize the diffuse and draw the masked image back in.
            using (var mainImage = Image.LoadPixelData<Rgba32>(diffuseData, baseDiffuseTex.Width, baseDiffuseTex.Height))
            {
                using (var maskImage = Image.LoadPixelData<Rgba32>(maskPixels, w, h))
                {
                    maskImage.SaveAsTga("E:\\img2.tga", Encoder);
                    var resizeOptions = new ResizeOptions
                    {
                        Size = new SixLabors.ImageSharp.Size(w, h),
                        PremultiplyAlpha = false,
                        Mode = SixLabors.ImageSharp.Processing.ResizeMode.Stretch,
                        Sampler = SixLabors.ImageSharp.Processing.KnownResamplers.Bicubic,
                    };
                    mainImage.Mutate(x => x.Resize(resizeOptions));

                    var ops = new GraphicsOptions()
                    {
                        AlphaCompositionMode = PixelAlphaCompositionMode.SrcAtop,
                    };
                    mainImage.Mutate(x => x.DrawImage(maskImage, ops));

                    var finalData = IOUtil.GetImageSharpPixels(mainImage);
                    return (finalData, mainImage.Width, mainImage.Height);
                }
            }
        }

        private static Regex EyeMaskPathRegex = new Regex("chara/human/c[0-9]{4}/obj/face/f[0-9]{4}/texture/--c[0-9]{4}f[0-9]{4}_iri_s.tex");

        public static async Task UpdateEyeMask(string maskPath, string source, ModTransaction tx, HashSet<string> _ConvertedTextures, Dictionary<string, FileStorageInformation> files = null)
        {
            if (!EyeMaskPathRegex.IsMatch(maskPath))
            {
                return;
            }

            if(_ConvertedTextures == null)
            {
                _ConvertedTextures = new HashSet<string>();
            }

            if(!await Exists(maskPath, files, tx))
            {
                return;
            }

            if (_ConvertedTextures.Contains(maskPath))
            {
                return;
            }
            var newTexPath = maskPath.Replace(".tex", "_diffuse.tex").Replace("--","");

            var data = await ResolveFile(maskPath, files, tx);

            var tex = XivTex.FromUncompressedTex(data);

            var facex = new Regex("f([0-9]{4})");
            var match = facex.Match(Path.GetFileName(maskPath));
            if (!match.Success)
            {
                return;
            }

            var race = IOUtil.GetRaceFromPath(maskPath);
            var face = Int32.Parse(match.Groups[1].Value);

            var irisFormat = "chara/human/c{0}/obj/face/f{1}/material/mt_c{0}f{1}_iri_a.mtrl";
            var irisPath = string.Format(irisFormat, race.GetRaceCode(), face.ToString("D4"));

            var rTx = ModTransaction.BeginReadonlyTransaction();

            if(!await rTx.FileExists(irisPath, true))
            {
                // Hmmm...
                return;
            }

            bool replaceMtrl = true;
            // Update Material
            var baseMaterial = Mtrl.GetXivMtrl(await rTx.ReadFile(irisPath, true), irisPath);

            var mtrlTex = baseMaterial.Textures.FirstOrDefault(x => x.Sampler != null && x.Sampler.SamplerId == ESamplerId.g_SamplerDiffuse);
            mtrlTex.TexturePath = newTexPath;
            var mtrlData = Mtrl.XivMtrlToUncompressedMtrl(baseMaterial);

            // Convert Mask to Diffuse
            var pixels = await tex.GetRawPixels();

            var updated = await ConvertEyeMaskToDiffuse(pixels, tex.Width, tex.Height);

            await TextureHelpers.SwizzleRB(updated.PixelData, updated.Width, updated.Height);

            // Create MipMaps (And DDS header that we don't really need)
            var maskData = await Tex.ConvertToDDS(updated.PixelData, XivTexFormat.A8R8G8B8, true, updated.Height, updated.Width);


            // Convert DDS to uncompressed Tex
            maskData = Tex.DDSToUncompressedTex(maskData);


            // Write the updated material and texture.
            await WriteFile(maskData, newTexPath, files, tx, source);

            if (replaceMtrl)
            {
                await WriteFile(mtrlData, irisPath, files, tx, source);
            }

            _ConvertedTextures.Add(maskPath);
        }

    }
}
