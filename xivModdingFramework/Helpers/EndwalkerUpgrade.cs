using SharpDX;
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

namespace xivModdingFramework.Helpers
{
    public static class EndwalkerUpgrade
    {

        public static async Task UpdateEndwalkerFiles(IEnumerable<string> filePaths, string source, Dictionary<string, TxFileState> states, IProgress<(int current, int total, string message)> progress, ModTransaction tx = null)
        {
#if ENDWALKER
            return;
#endif

            var fixableMdlsRegex = new Regex("chara\\/.*\\.mdl");
            var fixableMdls = filePaths.Where(x => fixableMdlsRegex.Match(x).Success).ToList();

            var fixableMtrlsRegex = new Regex("chara\\/.*\\.mtrl");
            var fixableMtrls = filePaths.Where(x => fixableMtrlsRegex.Match(x).Success).ToList();

            await EndwalkerUpgrade.UpdateEndwalkerMaterials(fixableMtrls, source, tx, progress);

            var idx = 0;
            var total = fixableMdls.Count;
            foreach (var path in fixableMdls)
            {
                progress?.Report((idx, total, "Updating Endwalker Models..."));
                idx++;
                await EndwalkerUpgrade.UpdateEndwalkerModels(path, source, tx);
            }

            progress?.Report((0, total, "Updating Endwalker partial Hair Mods..."));
            await EndwalkerUpgrade.CheckImportForOldHairJank(filePaths.ToList(), source, tx);

            progress?.Report((0, total, "Endwalker Upgrades Complete..."));
        }

        public static async Task UpdateEndwalkerModels(string path, string source, ModTransaction tx)
        {
            var uncomp = await tx.ReadFile(path, false, false);

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

            await Dat.WriteModFile(uncomp, path, source, null, tx, false);
        }
        /// <summary>
        /// Reads an uncompressed v5 MDL and retrieves the offsets to the bone lists, in order to update them.
        /// </summary>
        /// <param name="br"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static bool FastMdlv6Upgrade(BinaryReader br, BinaryWriter bw, long offset = -1)
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
        public static async Task UpdateEndwalkerMaterials(List<string> paths, string source, ModTransaction tx, IProgress<(int current, int total, string message)> progress)
        {
#if ENDWALKER
            return;
#endif
            var total = paths.Count;
            var materials = new List<XivMtrl>();
            var i = 0;
            foreach (var path in paths)
            {
                progress?.Report((i, total, "Scanning for Endwalker Materials..."));
                i++;
                if (!await tx.FileExists(path))
                {
                    continue;
                }

                var mtrl = await Mtrl.GetXivMtrl(path, false, tx);
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
                await UpdateEndwalkerMaterial(mtrl, source, true, tx);
                i++;
            }
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

        public static async Task UpdateEndwalkerMaterial(XivMtrl mtrl, string source, bool createTextures, ModTransaction tx = null)
        {
            if (!createTextures)
            {
                // Should we really allow this?
                throw new NotImplementedException();
            }

            if (!DoesMtrlNeedDawntrailUpdate(mtrl))
            {
                return;
            }

            var boiler = await TxBoiler.BeginWrite(tx);
            tx = boiler.Transaction;
            try
            {
                await UpdateEndwalkerMaterial(mtrl, source, tx);
                await boiler.Commit();
            }
            catch
            {
                await boiler.Catch();
                throw;
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
        private static async Task UpdateEndwalkerMaterial(XivMtrl mtrl, string source, ModTransaction tx)
        {

            if (mtrl.ColorSetDataSize > 0)
            {
                var texInfo = await UpdateEndwalkerColorset(mtrl, source, tx);

                var data = await CreateIndexFromNormal(texInfo.indexTextureToCreate, texInfo.normalToCreateFrom, tx);
                if (data.data != null)
                {
                    await Dat.WriteModFile(data.data, data.indexFilePath, source, null, tx, false);
                }
                else
                {
                    // Resave the material with texture validation to create dummy textures if none exist.
                    await Mtrl.ImportMtrl(mtrl, null, source, true, tx);
                }
            }
            else if (mtrl.ShaderPack == EShaderPack.Skin)
            {
                await UpdateEndwalkerSkinMaterial(mtrl, source, tx);
            }
            else if (mtrl.ShaderPack == EShaderPack.Hair)
            {
                await UpdateEndwalkerHairMaterial(mtrl, source, tx);
            }
        }

        /// <summary>
        /// Updates a given Endwalker style Material to a Dawntrail style material, returning a tuple containing the Index Map that should be created after,
        /// and the normal map that should be used in the creation.
        /// </summary>
        /// <param name="mtrl"></param>
        /// <param name="updateShaders"></param>
        /// <param name="source"></param>
        /// <returns></returns>
        private static async Task<(string indexTextureToCreate, string normalToCreateFrom)> UpdateEndwalkerColorset(XivMtrl mtrl, string source, ModTransaction tx)
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

            await Mtrl.ImportMtrl(mtrl, null, source, false, tx);

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
        private static async Task<(string indexFilePath, byte[] data)> CreateIndexFromNormal(string indexPath, string sourceNormalPath, ModTransaction tx = null)
        {

            if (!await tx.FileExists(sourceNormalPath))
            {
                // Can't create an index.  Return null to indicate mtrl should be resaved with texture validation.
                return (indexPath, null);
            }

            // Read normal file.
            var normalTex = await Tex.GetXivTex(sourceNormalPath, false, tx);
            var normalData = await normalTex.GetRawPixels();

            var indexData = new byte[normalTex.Width * normalTex.Height * 4];
            await TextureHelpers.CreateIndexTexture(normalData, indexData, normalTex.Width, normalTex.Height);

            // Create MipMaps (And DDS header that we don't really need)
            indexData = await Tex.ConvertToDDS(indexData, XivTexFormat.A8R8G8B8, true, normalTex.Height, normalTex.Width, true);

            // Convert DDS to uncompressed Tex
            indexData = Tex.DDSToUncompressedTex(indexData);

            return (indexPath, indexData);
        }

        private static async Task UpdateEndwalkerSkinMaterial(XivMtrl mtrl, string source, ModTransaction tx)
        {
            // Disabled for now.  SkinLegacy causes the Benchmark to crash in Benchmark 1.1
            return;

            // ShaderPack update is all we have for this one for now.
            /*
            mtrl.ShaderPack = EShaderPack.SkinLegacy;
            var mtrlData = Mtrl.XivMtrlToUncompressedMtrl(mtrl);
            await Dat.WriteModFile(mtrlData, mtrl.MTRLPath, source, null, tx, false);
            */

        }
        private static async Task UpdateEndwalkerHairMaterial(XivMtrl mtrl, string source, ModTransaction tx)
        {
            var normalTexSampler = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Normal);
            var maskTexSampler = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Mask);

            if (normalTexSampler == null || maskTexSampler == null)
            {
                // Not Resolveable.
                return;
            }

            // Arbitrary base game hair file to use to replace our shader constants.
            var constantBase = await Mtrl.GetXivMtrl("chara/human/c0801/obj/hair/h0115/material/v0001/mt_c0801h0115_hir_a.mtrl", true, tx);
            mtrl.ShaderConstants = constantBase.ShaderConstants;

            if (await tx.FileExists(normalTexSampler.Dx11Path) && await tx.FileExists(maskTexSampler.Dx11Path))
            {
                await UpdateEndwalkerHairTextures(normalTexSampler.Dx11Path, maskTexSampler.Dx11Path, source, tx);

                // Direct call to writemodfile is a little faster than going full import route.
                var mtrlData = Mtrl.XivMtrlToUncompressedMtrl(mtrl);
                await Dat.WriteModFile(mtrlData, mtrl.MTRLPath, source, null, tx, false);
            }
            else
            {
                // Use slower material import path here to do texture stubbing.
                await Mtrl.ImportMtrl(mtrl, null, source, true, tx);
            }

        }

        private static async Task UpdateEndwalkerHairTextures(string normalPath, string maskPath, string source, ModTransaction tx)
        {
            if (!await tx.FileExists(normalPath) || !await tx.FileExists(maskPath))
            {
                return;
            }

            // Read normal file.
            var normalTex = await Tex.GetXivTex(normalPath, false, tx);
            var maskTex = await Tex.GetXivTex(maskPath, false, tx);

            // Resize to be same size.
            var data = await TextureHelpers.ResizeImages(normalTex, maskTex);

            // Create the final hair pixel data.
            await TextureHelpers.CreateHairMaps(data.TexA, data.TexB, data.Width, data.Height);

            // Create MipMaps (And DDS header that we don't really need)
            var normalData = await Tex.ConvertToDDS(data.TexA, XivTexFormat.A8R8G8B8, true, data.Height, data.Width, true);
            var maskData = await Tex.ConvertToDDS(data.TexB, XivTexFormat.A8R8G8B8, true, data.Height, data.Width, true);

            // Convert DDS to uncompressed Tex
            normalData = Tex.DDSToUncompressedTex(normalData);
            maskData = Tex.DDSToUncompressedTex(maskData);

            // Write final files.
            await Dat.WriteModFile(normalData, normalPath, source, null, tx, false);
            await Dat.WriteModFile(maskData, maskPath, source, null, tx, false);
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
        internal static async Task CheckImportForOldHairJank(List<string> files, string source, ModTransaction tx)
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
                    if (!await tx.FileExists(material))
                    {
                        // Weird, but nothing to be done.
                        continue;
                    }
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

                        await Dat.CopyFile(tex.Path, newPath, source, true, item, tx);
                        files.Add(newPath);
                    }

                    var newNorm = string.Format(NewHairTextureFormat, race, hair, "norm");
                    var newMask = string.Format(NewHairTextureFormat, race, hair, "mask");
                    await UpdateEndwalkerHairTextures(newNorm, newMask, source, tx);
                }
            }
        }

        #endregion

    }
}
