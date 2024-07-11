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

            // First Round Upgrade -
            // This does models and base MTRLS only.
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
                                originals.Add(o, new Dictionary<string, FileStorageInformation>(o.StandardData.Files));
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
    }
}
