using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using xivModdingFramework.Textures.Enums;

namespace xivModdingFramework.Mods.DataContainers
{
    public class OverlayPack
    {
        private static string _CURRENT_VERSION = "0.1";

        public OverlayPack()
        {
            SystemVersion = _CURRENT_VERSION;
            Files = new Dictionary<string, OverlayFile>();
        }

        // The system version this pack was created with.
        public string SystemVersion;

        // Overlay Pack Name
        public string Name;

        // Author
        public string Author;

        // Author-specified version
        public string ModVersion;

        // Associated URL the modpack was sourced from/etc.
        public string URL;

        // The actual file listing.
        public Dictionary<string, OverlayFile> Files;


        /// <summary>
        /// Writes this overlay pack to disk at the desired location
        /// </summary>
        /// <returns></returns>
        public void WriteToFile(string destination)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            try
            {
                Directory.CreateDirectory(tempPath);


                var filesPath = Path.Combine(tempPath, "files");
                Directory.CreateDirectory(filesPath);

                // Copy files into the directory.
                foreach(var fileKv in Files)
                {
                    var fileDestPath = Path.Combine(filesPath, fileKv.Key);
                    File.Copy(fileKv.Value._OriginalFilePath, fileDestPath);
                }



                var jsonText = JsonConvert.SerializeObject(this);
                var rootFile = "list.json";


                // Copy the listing in.
                var listPath = Path.Combine(tempPath, rootFile);
                File.WriteAllText(listPath, jsonText);


                // Write the zip file.
                File.Delete(destination);
                ZipFile.CreateFromDirectory(tempPath, destination, CompressionLevel.Optimal, false);
            }
            finally
            {
                try
                {
                    Directory.Delete(tempPath, true);
                } catch
                {

                }
            }
        }


        /// <summary>
        /// Adds a new entry into the Files array.
        /// Just a short-hand version of .Files.Add(fileName, new OverlayFile(...))
        /// Returns the file added.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="mtrlPath"></param>
        /// <param name="texType"></param>
        /// <param name="newName"></param>
        public OverlayFile AddFile(string filePath, string mtrlPath, XivTexType texType, string newName = null)
        {
            if(newName == null)
            {
                newName = Path.GetFileName(filePath);
            }

            var file = new OverlayFile(filePath, mtrlPath, texType, newName);
            Files.Add(newName, file);

            return file;
        }

        /// <summary>
        /// Removes an entry from the files array.
        /// Just Shorthand for .Files.Remove(key)
        /// </summary>
        /// <param name="fileName"></param>
        public void RemoveFile(string fileName)
        {
            Files.Remove(fileName);
        }

    }

    public class OverlayFile
    {
        // The MTRL file this overlay should attach to.
        public string MtrlPath;

        // The texture type within the associate MTRL that this overlay should attach to.
        public XivTexType TexType;

        // The path within the parent zip file's /files/ structure.
        // Should be identical to the files's key in the owning OverlayPack.
        public string ZipPath;

        // The original file path to the overlay file.
        // Not stored when written to disk.
        private readonly string _OriginalFile;

        internal string _OriginalFilePath {
            get
            {
                return _OriginalFile;
            }
        }

        public OverlayFile(string filePath, string mtrlPath, XivTexType texType, string newName = null)
        {
            _OriginalFile = filePath;
            MtrlPath = mtrlPath;
            TexType = texType;

            if (newName != null) {
                ZipPath = newName;
            } else
            {
                var name = Path.GetFileName(filePath);
                ZipPath = filePath;
            }
        }
    }
}
