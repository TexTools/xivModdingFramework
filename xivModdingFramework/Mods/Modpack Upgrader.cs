using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Helpers;
using System.IO;
using System.Diagnostics;
using xivModdingFramework.Mods;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Materials.FileTypes;
using xivModdingFramework.Materials.DataContainers;
using static xivModdingFramework.Mods.EndwalkerUpgrade;
using System.Text.RegularExpressions;

namespace xivModdingFramework.Mods
{
    /// <summary>
    /// Full modpack upgrade handler.
    /// This lives in the UI project as it relies on the WizardData handler for simplification of modpack type handling.
    /// </summary>
    public static class ModpackUpgrader
    {

        private static bool AnyChanges(Dictionary<string, FileStorageInformation> original, Dictionary<string, FileStorageInformation> newFiles)
        {
            if (original.Count != newFiles.Count)
            {
                return true;
            }
            else
            {
                foreach (var kv in original)
                {
                    if (!newFiles.ContainsKey(kv.Key))
                    {
                        return true;
                    }

                    var o = kv.Value;
                    var n = newFiles[kv.Key];
                    if (!o.Equals(n))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static async Task<(WizardData Data, bool AnyChanges)> UpgradeModpack(string path, bool includePartials = true)
        {
            if (Directory.Exists(path))
            {
                path = Path.GetFullPath(Path.Combine(path, "meta.json"));
            }

            var data = await WizardData.FromModpack(path);
            var textureUpgradeTargets = new Dictionary<string, EndwalkerUpgrade.UpgradeInfo>();

            var allTextures = new HashSet<string>();
            var anyChanges = false;

            var originals = new Dictionary<WizardOptionEntry, Dictionary<string, FileStorageInformation>>();

            // Store original data for comparison later.
            foreach (var p in data.DataPages)
            {
                foreach (var g in p.Groups)
                {
                    if (g == null) continue;
                    foreach (var o in g.Options)
                    {
                        if (o.StandardData != null)
                        {
                            originals.Add(o, new Dictionary<string, FileStorageInformation>(o.StandardData.Files));
                        }
                    }
                }
            }

            // Pre-Upgrades - Specifically can hair to see if we can resolve some highlight-related stuff.
            await ResolveHighlightOptionsAndMashupHair(data);


            // First Round Upgrade -
            // This does models and base MTRLS only, and caches their texture information.
            foreach (var p in data.DataPages)
            {
                foreach (var g in p.Groups)
                {
                    if (g == null) continue;
                    foreach (var o in g.Options)
                    {
                        if (o.StandardData != null)
                        {
                            try
                            {
                                var missing = await EndwalkerUpgrade.UpdateEndwalkerFiles(o.StandardData.Files);
                                foreach (var kv in missing)
                                {
                                    if (!textureUpgradeTargets.ContainsKey(kv.Key))
                                    {
                                        textureUpgradeTargets.Add(kv.Key, kv.Value);
                                    }
                                }

                                var textures = o.StandardData.Files.Select(x => x.Key).Where(x => x.EndsWith(".tex"));
                                allTextures.UnionWith(textures);

                            }
                            catch (Exception ex)
                            {
                                var mes = "An error occurred while updating Group: " + g.Name + " - Option: " + o.Name + "\n\n" + ex.Message; ;
                                throw new Exception(mes);
                            }
                        }
                    }
                }
            }


            // Second Round Upgrade - This does textures based on the collated upgrade information from the previous pass
            foreach (var p in data.DataPages)
            {
                foreach (var g in p.Groups)
                {
                    if (g == null) continue;
                    foreach (var o in g.Options)
                    {
                        if (o.StandardData != null)
                        {
                            try
                            {
                                await EndwalkerUpgrade.UpgradeRemainingTextures(o.StandardData.Files, textureUpgradeTargets);
                            }
                            catch (Exception ex)
                            {
                                var mes = "An error occurred while updating Group: " + g.Name + " - Option: " + o.Name + "\n\n" + ex.Message; ;
                                throw new Exception(mes);
                            }
                        }
                    }
                }
            }


            if (includePartials)
            {
                // Find all un-referenced textures.
                var unusedTextures = new HashSet<string>(
                    allTextures.Where(t =>
                        !textureUpgradeTargets.Any(x =>
                            x.Value.Files.ContainsValue(t)
                        )));


                // Third Round Upgrade - This inspects as-of-yet unupgraded textures for possible jank-upgrades,
                // Which is to say, upgrades where we can infer their usage and pairing, but the base mtrl was not included.
                foreach (var p in data.DataPages)
                {
                    foreach (var g in p.Groups)
                    {
                        if (g == null) continue;
                        foreach (var o in g.Options)
                        {
                            if (o.StandardData != null)
                            {
                                var contained = unusedTextures.Where(x => o.StandardData.Files.ContainsKey(x));
                                await EndwalkerUpgrade.UpdateUnclaimedHairTextures(contained.ToList(), "Unused", null, null, o.StandardData.Files);

                                foreach (var possibleMask in contained)
                                {
                                    await EndwalkerUpgrade.UpdateEyeMask(possibleMask, "Unused", null, null, o.StandardData.Files);
                                }
                            }
                        }
                    }
                }
            }

            // Evaluate success
            foreach(var p in data.DataPages)
            {
                foreach(var g in p.Groups)
                {
                    foreach(var o in g.Options)
                    {
                        if(o.StandardData != null)
                        {
                            if (!anyChanges)
                            {
                                anyChanges = AnyChanges(originals[o], o.StandardData.Files);
                            }
                        }

                        if (anyChanges) break;
                    }
                    if (anyChanges) break;
                }
                if (anyChanges) break;
            }

            return (data, anyChanges);
        }

        public static async Task<bool> UpgradeModpack(string path, string newPath, bool includePartials = true)
        {
            var data = await UpgradeModpack(path, includePartials);

            await data.Data.WriteModpack(newPath, true);
            return data.AnyChanges;
        }


        private static async Task ForAllFiles(WizardData data, Func<KeyValuePair<string, FileStorageInformation>, Task> act)
        {
            // Get all texture pairs.
            foreach (var p in data.DataPages)
            {
                if (p == null) continue;
                foreach (var g in p.Groups)
                {
                    if (g == null) continue;
                    foreach (var o in g.Options)
                    {
                        if (o == null) continue;
                        if (o.StandardData == null) continue;

                        foreach (var f in o.StandardData.Files)
                        {
                            await act(f);
                        }
                    }
                }
            }
        }

        private static async Task ForAllOptions(WizardData data, Func<WizardStandardOptionData, Task> act)
        {
            // Get all texture pairs.
            foreach (var p in data.DataPages)
            {
                if (p == null) continue;
                foreach (var g in p.Groups)
                {
                    if (g == null) continue;
                    foreach (var o in g.Options)
                    {
                        if (o == null) continue;
                        if (o.StandardData == null) continue;
                        await act(o.StandardData);
                    }
                }
            }
        }

        private static async Task ResolveHighlightOptionsAndMashupHair(WizardData data)
        {
            // This needs to scan the entire set of options first, rip all the mtrls,
            // and rip their Normal/Mask pairs.

            List<(string Normal, string Mask)> mData = new List<(string Normal, string Mask)>();
            var hairMaterials = new HashSet<string>();

            await ForAllFiles(data, async (f) =>
            {
                if (f.Key.EndsWith(".mtrl"))
                {
                    try
                    {
                        var raw = await TransactionDataHandler.GetUncompressedFile(f.Value);
                        XivMtrl mtrl;
                        try
                        {
                            mtrl = Mtrl.GetXivMtrl(raw, f.Key);
                        }
                        catch
                        {
                            return;
                        }

                        if (mtrl.ShaderPack != Materials.DataContainers.ShaderHelpers.EShaderPack.Hair) return;

                        var norm = mtrl.Textures.FirstOrDefault(x => x.Sampler.SamplerId == ShaderHelpers.ESamplerId.g_SamplerNormal);
                        var mask = mtrl.Textures.FirstOrDefault(x => x.Sampler.SamplerId == ShaderHelpers.ESamplerId.g_SamplerMask);

                        if (norm == null || mask == null) return;
                        hairMaterials.Add(f.Key);
                        mData.Add((norm.Dx11Path, mask.Dx11Path));
                    }
                    catch
                    {
                        return;
                    }
                }
            });

            if(mData.Count == 0)
            {
                return;
            }

            // Construct the list of bad options and relevant texture containing options.
            var containers = new Dictionary<string, List<WizardStandardOptionData>>();
            var badOptions = new List<WizardStandardOptionData>();
            await ForAllOptions(data, async (o) =>
            {
                foreach(var pair in mData)
                {
                    var hasMask = o.Files.ContainsKey(pair.Mask);
                    var hasNorm = o.Files.ContainsKey(pair.Normal);

                    if (hasNorm)
                    {
                        if (!containers.ContainsKey(pair.Normal))
                        {
                            containers.Add(pair.Normal, new List<WizardStandardOptionData>());
                        }
                        containers[pair.Normal].Add(o);
                    }
                    if (hasMask)
                    {
                        if (!containers.ContainsKey(pair.Mask))
                        {
                            containers.Add(pair.Mask, new List<WizardStandardOptionData>());
                        }
                        containers[pair.Mask].Add(o);
                    }

                    if (hasMask && hasNorm) continue;
                    if (!hasMask && !hasNorm) continue;
                    badOptions.Add(o);
                }
            });

            if (badOptions.Count == 0)
            {
                if(containers.Count == 0)
                {
                    // This is a material-only Mashup hair.
                    // We can typically fix these via material repathing.
                    await RepathHairMashups(data);
                }
                return;
            }

            // Resolve if there is only one container for the missing item or not.
            foreach(var o in badOptions)
            {
                foreach (var pair in mData)
                {
                    var hasMask = o.Files.ContainsKey(pair.Mask);
                    var hasNorm = o.Files.ContainsKey(pair.Normal);

                    var missingTex = hasMask ? pair.Normal : pair.Mask;

                    if (containers[missingTex].Count != 1)
                    {
                        throw new InvalidDataException("Cannot upgrade modpack - Highlight/Visibility options are unresolveable either due to missing files or too much complexity.\nTry installing the modpack and creating an updated pack from the desired options.");
                    }

                    // If there is only one exact source, staple in the copy to this option.
                    var file = containers[missingTex][0].Files[missingTex];
                    o.Files.Add(missingTex, file);
                }
            }
        }

        private static async Task RepathHairMashups(WizardData data)
        {
            await RepathHairMashups(data, new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/hair.*\\.mtrl"));
            await RepathHairMashups(data, new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/zear.*\\.mtrl"));
            await RepathHairMashups(data, new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/tail.*\\.mtrl"));
        }
        private static async Task RepathHairMashups(WizardData data, Regex mtrlRegex)
        {
            // Need this for validating paths.
            var rtx = ModTransaction.BeginReadonlyTransaction();

            await ForAllOptions(data, async (o) =>
            {
                var files = new Dictionary<string, FileStorageInformation>(o.Files);
                foreach(var f in files)
                {
                    if (!mtrlRegex.IsMatch(f.Key)) continue;

                    var m = f.Key;
                    var data = await TransactionDataHandler.GetUncompressedFile(files[m]);
                    var mtrl = Mtrl.GetXivMtrl(data, m);

                    if(mtrl.ShaderPack != ShaderHelpers.EShaderPack.Hair && mtrl.ShaderPack != ShaderHelpers.EShaderPack.Character)
                    {
                        continue;
                    }

                    var norm = mtrl.Textures.FirstOrDefault(x => x.Sampler.SamplerId == ShaderHelpers.ESamplerId.g_SamplerNormal);
                    var mask = mtrl.Textures.FirstOrDefault(x => x.Sampler.SamplerId == ShaderHelpers.ESamplerId.g_SamplerMask);
                    var diff = mtrl.Textures.FirstOrDefault(x => x.Sampler.SamplerId == ShaderHelpers.ESamplerId.g_SamplerDiffuse);

                    if (norm == null || mask == null) continue;
                    var nPath = norm.Dx11Path;
                    var mPath = mask.Dx11Path;

                    if (!await rtx.FileExists(nPath, true))
                    {
                        var newPath = nPath.Replace("_n.tex", "_norm.tex").Replace("--","");
                        if(await rtx.FileExists(newPath, true))
                        {
                            norm.TexturePath = norm.TexturePath.Replace("_n.tex", "_norm.tex").Replace("--", "");
                        }
                    }

                    if (!await rtx.FileExists(mPath, true))
                    {
                        var newPath = mPath.Replace("_m.tex", "_mask.tex").Replace("--", "");
                        var found = false;
                        if (await rtx.FileExists(newPath, true) && !found)
                        {
                            mask.TexturePath = mask.TexturePath.Replace("_m.tex", "_mask.tex").Replace("--", "");
                            found = true;
                        }

                        newPath = mPath.Replace("_m.tex", "_mult.tex").Replace("--", "");
                        if (await rtx.FileExists(newPath, true) && !found)
                        {
                            mask.TexturePath = mask.TexturePath.Replace("_m.tex", "_mult.tex").Replace("--", "");
                            found = true;
                        }

                        newPath = mPath.Replace("_s.tex", "_mask.tex").Replace("--", "");
                        if (await rtx.FileExists(newPath, true) && !found)
                        {
                            mask.TexturePath = mask.TexturePath.Replace("_s.tex", "_mask.tex").Replace("--", "");
                            found = true;
                        }

                        newPath = mPath.Replace("_s.tex", "_mult.tex").Replace("--", "");
                        if (await rtx.FileExists(newPath, true) && !found)
                        {
                            mask.TexturePath = mask.TexturePath.Replace("_s.tex", "_mult.tex").Replace("--", "");
                            found = true;
                        }
                    }

                    if(diff != null && !await rtx.FileExists(diff.Dx11Path))
                    {
                        var dPath = diff.Dx11Path;
                        var newPath = dPath.Replace("_d.tex", "_base.tex").Replace("--", "");
                        if (await rtx.FileExists(newPath, true))
                        {
                            diff.TexturePath = diff.TexturePath.Replace("_d.tex", "_base.tex").Replace("--", "");
                        }
                    }


                    data = Mtrl.XivMtrlToUncompressedMtrl(mtrl);

                    var path = IOUtil.GetFrameworkTempFile();
                    File.WriteAllBytes(path, data);

                    var info = new FileStorageInformation()
                    {
                        FileSize = data.Length,
                        RealOffset = 0,
                        RealPath = path,
                        StorageType = EFileStorageType.UncompressedIndividual
                    };

                    o.Files[m] = info;
                }
            });
        }

    }
}
