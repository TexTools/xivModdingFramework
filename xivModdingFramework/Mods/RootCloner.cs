using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Materials.FileTypes;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Variants.FileTypes;

namespace xivModdingFramework.Mods
{
    public static class RootCloner
    {
        /// <summary>
        /// Copies the entirety of a given root to a new root.  
        /// </summary>
        /// <param name="Source">Original Root to copy from.</param>
        /// <param name="Destination">Destination root to copy to.</param>
        /// <param name="ApplicationSource">Application to list as the source for the resulting mod entries.</param>
        /// <returns></returns>
        public static async Task CloneRoot(XivDependencyRoot Source, XivDependencyRoot Destination, string ApplicationSource)
        {

            var df = IOUtil.GetDataFileFromPath(Source.ToString());

            var _imc = new Imc(XivCache.GameInfo.GameDirectory);
            var _mdl = new Mdl(XivCache.GameInfo.GameDirectory, df);
            var _dat = new Dat(XivCache.GameInfo.GameDirectory);
            var _index = new Index(XivCache.GameInfo.GameDirectory);
            var _mtrl = new Mtrl(XivCache.GameInfo.GameDirectory, df, XivCache.GameInfo.GameLanguage);

            // First, try to get everything, to ensure it's all valid.
            var originalMetadata = await ItemMetadata.GetMetadata(Source);
            var originalModelPaths = await Source.GetModelFiles();
            var originalMaterialPaths = await Source.GetMaterialFiles();
            var originalTexturePaths= await Source.GetTextureFiles();


            // Time to start editing things.

            // First, get a new, clean copy of the metadata, pointed at the new root.
            var newMetadata = await ItemMetadata.GetMetadata(Source);
            newMetadata.Root = Destination.Info.ToFullRoot();

            // Now figure out the path names for all of our new paths.
            // These dictionarys map Old Path => New Path
            Dictionary<string, string> newModelPaths = new Dictionary<string, string>();
            Dictionary<string, string> newMaterialPaths = new Dictionary<string, string>();
            Dictionary<string, string> newMaterialFileNames = new Dictionary<string, string>();
            Dictionary<string, string> newTexturePaths = new Dictionary<string, string>();


            // For each path, replace any instances of our primary and secondary types.
            foreach (var path in originalModelPaths)
            {
                newModelPaths.Add(path, UpdatePath(Source, Destination, path));
            }
            
            foreach (var path in originalMaterialPaths)
            {
                var nPath = UpdatePath(Source, Destination, path);
                newMaterialPaths.Add(path, nPath);
                var fName = Path.GetFileName(path);

                if (!newMaterialFileNames.ContainsKey(fName)) {
                    newMaterialFileNames.Add(fName, Path.GetFileName(nPath));
                }
            }

            foreach (var path in originalTexturePaths)
            {
                newTexturePaths.Add(path, UpdatePath(Source, Destination, path));
            }

            var destItem = Destination.GetFirstItem();
            var iCat = destItem.SecondaryCategory;
            var iName = destItem.Name;


            // Load, Edit, and resave the model files.
            foreach (var kv in newModelPaths)
            {
                var src = kv.Key;
                var dst = kv.Value;
                var xmdl = await _mdl.GetRawMdlData(src);
                var tmdl = TTModel.FromRaw(xmdl);
                tmdl.Source = dst;
                xmdl.MdlPath = dst;

                // Replace any material references as needed.
                foreach(var m in tmdl.MeshGroups)
                {
                    foreach(var matKv in newMaterialFileNames)
                    {
                        m.Material = m.Material.Replace(matKv.Key, matKv.Value);
                    }
                }

                // Save new Model.
                var bytes = await _mdl.MakeNewMdlFile(tmdl, xmdl, null);
                await _dat.WriteToDat(bytes.ToList(), null, dst, iCat, iName, df, ApplicationSource, 3);
            }

            // Raw Copy all Texture files to the new destinations to avoid having the MTRL save functions auto-generate blank textures.
            foreach (var kv in newTexturePaths)
            {
                var src = kv.Key;
                var dst = kv.Value;

                await _dat.CopyFile(src, dst, iCat, iName, ApplicationSource, true);
            }

            // Load every Material file and edit the texture references to the new texture paths.
            foreach(var kv in newMaterialPaths)
            {
                var src = kv.Key;
                var dst = kv.Value;
                var offset = await _index.GetDataOffset(src);
                var xivMtrl = await _mtrl.GetMtrlData(offset, src, 11);
                xivMtrl.MTRLPath = dst;

                for(int i = 0; i < xivMtrl.TexturePathList.Count; i++)
                {
                    foreach (var tkv in newTexturePaths)
                    {
                        xivMtrl.TexturePathList[i] = xivMtrl.TexturePathList[i].Replace(tkv.Key, tkv.Value);
                    }
                }

                await _mtrl.ImportMtrl(xivMtrl, destItem, ApplicationSource);
            }

            // Save the new Metadata file.
            await ItemMetadata.SaveMetadata(newMetadata, ApplicationSource);

            // TODO -- Copy AVFX file(s) over
            // TODO -- Validate all variants/material sets for valid materials, and copy materials as needed to fix.
        }

        const string CommonPath = "chara/common/";
        private static readonly Regex RemoveRootPathRegex = new Regex("chara\\/[a-z]+\\/[a-z][0-9]{4}(?:\\/obj\\/[a-z]+\\/[a-z][0-9]{4})?\\/(.+)");

        private static string UpdatePath(XivDependencyRoot Source, XivDependencyRoot Destination, string path)
        {
            // Things that live in the common folder get to stay there/don't get copied.
            if (path.StartsWith(CommonPath)) return path;


            var file = UpdateFileName(Source, Destination, path);
            var folder = UpdateFolder(Source, Destination, path);

            return folder + "/" + file;
        }

        private static string UpdateFolder(XivDependencyRoot Source, XivDependencyRoot Destination, string path)
        {
            // So first off, just copy anything from the old root folder to the new one.
            var match = RemoveRootPathRegex.Match(path);
            if(match.Success)
            {
                // The item existed in an old root path, so we can just clone the same post-root path into the new root folder.
                var afterRootPath = match.Groups[1].Value;
                path = Destination.Info.GetRootFolder() + afterRootPath;
                path = Path.GetDirectoryName(path);
                path = path.Replace('\\', '/');
                return path;
            }

            // Okay, stuff at this point didn't actually exist in any root path, and didn't exist in the common path either.
            // Just copy this crap into our root folder.

            // The only way we can really get here is if some mod author created textures in a totally arbitrary path.
            path = Path.GetDirectoryName(Destination.Info.GetRootFolder());
            path = path.Replace('\\', '/');
            return path;
        }

        private static string UpdateFileName(XivDependencyRoot Source, XivDependencyRoot Destination, string path)
        {
            var file = Path.GetFileName(path);

            var rex = new Regex("[a-z][0-9]{4}([a-z][0-9]{4})");
            var match = rex.Match(file);

            if (Source.Info.SecondaryType == null)
            {
                // Equipment/Accessory items. Only replace the back half of the file names.
                var srcString = match.Groups[1].Value;
                var dstString = Destination.Info.GetBaseFileName(false);

                file = file.Replace(srcString, dstString);
            } else
            {
                // Replace the entire root chunk for roots that have two identifiers.
                var srcString = match.Groups[0].Value;
                var dstString = Destination.Info.GetBaseFileName(false);

                file = file.Replace(srcString, dstString);
            }

            return file;
        }
    }
}
