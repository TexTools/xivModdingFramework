using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Helpers;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.FileTypes;

namespace xivModdingFramework.Mods
{
    public static class ShrinkRay
    {
        public class ShrinkRaySettings
        {
            public bool ResaveModels = true;
            public bool RemoveExtraFiles = true;
            public int MaxTextureSize = 2048;
        }

        public static async Task<WizardData> ShrinkModpack(string modpackPath, ShrinkRaySettings settings = null)
        {
            var data = await WizardData.FromModpack(modpackPath);
            if (data == null)
            {
                throw new ArgumentException("The given modpack does not exist or is invalid.");
            }

            return await ShrinkModpack(data);
        }
        public static async Task<WizardData> ShrinkModpack(WizardData modpack, ShrinkRaySettings settings = null)
        {
            if(settings == null)
            {
                settings = new ShrinkRaySettings();
            }

            modpack.ClearNulls();

            await Task.Run(async () =>
            {
                var rtx = ModTransaction.BeginReadonlyTransaction();
                await ForEachOptionParalell(modpack, async (opt) =>
                {
                    var newKeys = new Dictionary<string, FileStorageInformation>(opt.Files.Count);
                    foreach (var kv in opt.Files)
                    {
                        var path = kv.Key;
                        var file = kv.Value;
                        var ext = Path.GetExtension(path);

                        if (settings.ResaveModels && ext == ".mdl")
                        {
                            file = await ResaveModel(path, file);
                        }
                        else if (ext == ".tex" || ext == ".atex")
                        {
                            file = await ResaveTexture(path, file, settings.MaxTextureSize);
                        }

                        newKeys.Add(path, file);
                    }

                    opt.Files = newKeys;
                });

                if (settings.RemoveExtraFiles)
                {
                    modpack.ExtraFiles = new Dictionary<string, string>();
                }
            });

            return modpack;
        }

        private static async Task ForEachOptionParalell(WizardData data, Func<WizardStandardOptionData, Task> act)
        {
            var tasks = new List<Task>();
            foreach (var p in data.DataPages)
            {
                foreach (var g in p.Groups)
                {
                    foreach (var o in g.Options)
                    {
                        var sData = o.StandardData;
                        if (sData == null) continue;

                        tasks.Add(Task.Run(async () => await act(sData)));
                    }
                }
            }
            await Task.WhenAll(tasks);
        }

        private static async Task<FileStorageInformation> ResaveModel(string path, FileStorageInformation model)
        {
            var file = model;
            try
            {
                var data = await TransactionDataHandler.GetUncompressedFile(model);
                var raw = Mdl.GetXivMdl(data, path);
                var ttm = TTModel.FromRaw(raw);
                ttm.MdlVersion = 6;

                var res = Mdl.MakeUncompressedMdlFile(ttm, raw);
                var tempPath = IOUtil.GetFrameworkTempFile();
                File.WriteAllBytes(tempPath, res);

                file = new FileStorageInformation()
                {
                    FileSize = res.Length,
                    RealOffset = 0,
                    RealPath = tempPath,
                    StorageType = EFileStorageType.UncompressedIndividual,
                };
            } catch(Exception ex)
            {
                Trace.WriteLine(ex);
            }
            return file;
        }


        private static async Task<FileStorageInformation> ResaveTexture(string path, FileStorageInformation texture, int maxSize = -1)
        {
            var file = texture;
            try
            {
                var data = await TransactionDataHandler.GetUncompressedFile(texture);
                var xTex = XivTex.FromUncompressedTex(data);

                await Tex.EnsureValidSize(xTex, maxSize);
                var res = xTex.ToUncompressedTex();
                var tempPath = IOUtil.GetFrameworkTempFile();
                File.WriteAllBytes(tempPath, res);

                file = new FileStorageInformation()
                {
                    FileSize = res.Length,
                    RealOffset = 0,
                    RealPath = tempPath,
                    StorageType = EFileStorageType.UncompressedIndividual,
                };
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
            }
            return file;
        }
    }
}
