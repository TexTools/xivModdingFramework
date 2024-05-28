using Ionic.Zip;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.Mods.Interfaces;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.Mods
{
    internal class TTMPWriter : IDisposable
    {
        private class AddedFile
        {
            // Files can be added as an already available byte[], or as a read operation to be completed on demand
            // Tasks should not be executed until a point where user is being updated on progress
            public Func<Task<byte[]>> DataTask;
            public byte[] Data;
            public ModsJson Mod;

            public AddedFile(byte[] data, ModsJson mod)
            {
                DataTask = null;
                Data = data;
                Mod = mod;
            }

            public AddedFile(Func<Task<byte[]>> dataTask, ModsJson mod)
            {
                DataTask = dataTask;
                Data = null;
                Mod = mod;
            }

            public async Task<byte[]> GetData()
            {
                if (Data == null)
                    Data = await DataTask();
                return Data;
            }
        }

        private ModPackJson _modPackJson;
        private string _tmpDir;
        private List<string> _imageList = new();
        private List<AddedFile> _addedFiles = new();

        public int PageCount => _modPackJson.ModPackPages?.Count ?? 0;
        public int ModCount => _addedFiles.Count;

        public TTMPWriter(IModPackData modPackData, char modPackTypeCode)
        {
            Version version = modPackData.Version ?? new Version(1, 0, 0, 0);

            _modPackJson = new ModPackJson
            {
                TTMPVersion = TTMP._currentTTMPVersion + modPackTypeCode,
                MinimumFrameworkVersion = TTMP._minimumAssembly,
                Name = modPackData.Name,
                Author = modPackData.Author,
                Version = version.ToString(),
                Description = modPackData.Description,
                Url = modPackData.Url
            };

            if (modPackTypeCode == TTMP._typeCodeWizard)
                _modPackJson.ModPackPages = new();
            else
                _modPackJson.SimpleModsList = new();

            var guid = Guid.NewGuid();
            _tmpDir = Path.Combine(Path.GetTempPath(), guid.ToString());
            Directory.CreateDirectory(_tmpDir);
        }

        // Add a page to a wizard modpack
        public ModPackPageJson AddPage(ModPackData.ModPackPage modPage)
        {
            if (_modPackJson.ModPackPages == null)
                throw new InvalidOperationException("Wrong modpack type");

            var modPackPageJson = new ModPackPageJson
            {
                PageIndex = modPage.PageIndex,
                ModGroups = new List<ModGroupJson>()
            };

            _modPackJson.ModPackPages.Add(modPackPageJson);

            return modPackPageJson;
        }

        // Add a group to a wizard modpack page
        public ModGroupJson AddGroup(ModPackPageJson page, ModGroup modGroup)
        {
            if (_modPackJson.ModPackPages == null)
                throw new InvalidOperationException("Wrong modpack type");

            var modGroupJson = new ModGroupJson
            {
                GroupName = modGroup.GroupName,
                SelectionType = modGroup.SelectionType,
                OptionList = new List<ModOptionJson>()
            };

            page.ModGroups.Add(modGroupJson);

            return modGroupJson;
        }

        // Add an option to a wizard modpack group
        public ModOptionJson AddOption(ModGroupJson group, ModOption modOption)
        {
            if (_modPackJson.ModPackPages == null)
                throw new InvalidOperationException("Wrong modpack type");

            var imageFileName = "";

            // Copy images in to the temp directory to be included in the TTMP file
            if (modOption.Image != null)
            {
                var fname = Path.GetFileName(modOption.ImageFileName);
                imageFileName = Path.Combine(_tmpDir, fname);
                if (!File.Exists(imageFileName))
                {
                    File.Copy(modOption.ImageFileName, imageFileName);
                    _imageList.Add(imageFileName);
                }
            }

            var fn = imageFileName == "" ? "" : "images/" + Path.GetFileName(imageFileName);

            var modOptionJson = new ModOptionJson
            {
                Name = modOption.Name,
                Description = modOption.Description,
                ImagePath = fn,
                GroupName = modOption.GroupName,
                SelectionType = modOption.SelectionType,
                IsChecked = modOption.IsChecked,
                ModsJsons = new List<ModsJson>()
            };

            group.OptionList.Add(modOptionJson);

            return modOptionJson;
        }

        // Add a file to a wizard modpack
        public ModsJson AddFile(ModOptionJson option, string fullPath, ModData modData)
        {
            if (_modPackJson.ModPackPages == null)
                throw new InvalidOperationException("Wrong modpack type");

            var dataFile = IOUtil.GetDataFileFromPath(fullPath);

            if (TTMP.ForbiddenModTypes.Contains(Path.GetExtension(fullPath)))
                return null;

            var modsJson = new ModsJson
            {
                Name = modData.Name,
                Category = modData.Category.GetEnDisplayName(),
                FullPath = fullPath,
                IsDefault = modData.IsDefault,
                ModSize = -1, // Will be updated on Write()
                ModOffset = -1, // Will be updated on Write()
                DatFile = dataFile.GetFileName()
            };

            option.ModsJsons.Add(modsJson);

            // Add the file to a list to be deduplicated and written out by _writeMPD()
            // ModSize/ModOffset will be assigned at that time
            _addedFiles.Add(new AddedFile(modData.ModDataBytes, modsJson));

            return modsJson;
        }

        // Add a mod file to a simple/backup modpack
        // Since the data is not immediately available, a datafile reference is required to read data from
        public ModsJson AddFile(SimpleModData modData, ModTransaction tx)
        {
            if (_modPackJson.SimpleModsList == null)
                throw new Exception("Wrong modpack type");

            if (TTMP.ForbiddenModTypes.Contains(Path.GetExtension(modData.FullPath)))
                return null;

            var modsJson = new ModsJson
            {
                Name = modData.Name,
                Category = modData.Category.GetEnDisplayName(),
                FullPath = modData.FullPath,
                IsDefault = modData.IsDefault,
                ModSize = -1, // Will be updated on Write()
                ModOffset = -1, // Will be updated on Write()
                DatFile = modData.DatFile
            };

            _modPackJson.SimpleModsList.Add(modsJson);

            Func<Task<byte[]>> modDataTask = async () =>
            {

                // Get the data at the mod offset, regardless of it it's enabled or not.
                var rawData = await tx.ReadFile(IOUtil.GetDataFileFromPath(modsJson.FullPath), modData.ModOffset, true);

                if (rawData == null)
                {
                    throw new Exception("Unable to obtain data for the following mod\n\n" +
                                        $"Name: {modData.Name}\nFull Path: {modData.FullPath}\n" +
                                        $"Mod Offset: {modData.ModOffset}\nData File: {modData.DatFile}\n\n" +
                                        $"Unselect the above mod and try again.");
                }

                return rawData;
            };

            // Add the file to a list to be deduplicated and written out by _writeMPD()
            // ModSize/ModOffset will be assigned at that time
            _addedFiles.Add(new AddedFile(modDataTask, modsJson));

            return modsJson;
        }

        private struct SHA1HashKey
        {
            public ulong A;
            public ulong B;
            public uint C;

            public SHA1HashKey(byte[] data)
            {
                A = BitConverter.ToUInt64(data, 0);
                B = BitConverter.ToUInt64(data, 8);
                C = BitConverter.ToUInt32(data, 16);
            }
        }

        // Write MPD file while updating ModSize and ModOffset values in the modpack json data
        private async Task _writeMPD(string tempMPD, IProgress<(int current, int total, string message)> progress)
        {
            // Keeps track of previously written files for deduplication
            var seenFiles = new Dictionary<SHA1HashKey, long>();

            using var binaryWriter = new BinaryWriter(File.Open(tempMPD, FileMode.Create));
            using var sha1 = SHA1.Create();

            int modCount = 0;

            foreach (var addedFile in _addedFiles)
            {
                progress?.Report((modCount++, _addedFiles.Count, string.Empty));

                // This may execute a file-read operation
                var data = await addedFile.GetData();

                var dedupeHash = new SHA1HashKey(sha1.ComputeHash(data));
                long offset = -1;

                // File was not seen before, write it to the MPD file
                if (!seenFiles.TryGetValue(dedupeHash, out offset))
                {
                    offset = binaryWriter.BaseStream.Position;
                    binaryWriter.Write(data);
                    seenFiles.Add(dedupeHash, offset);
                }

                addedFile.Mod.ModSize = data.Length;
                addedFile.Mod.ModOffset = offset;
            }
        }

        // Determine the filename for a mod pack to be written to
        private string _determineModPackPath(string destination, bool overwriteModpack)
        {
            var modPackName = _modPackJson.Name;
            var modPackPath = Path.Combine(destination, $"{modPackName}.ttmp2");

            if (destination.EndsWith(".ttmp2"))
            {
                // Explicit path already provided.
                modPackPath = destination;
                modPackName = Path.GetFileNameWithoutExtension(modPackPath);
                overwriteModpack = true;
            }

            if (File.Exists(modPackPath) && !overwriteModpack)
            {
                var fileNum = 1;
                modPackPath = Path.Combine(destination, $"{modPackName}({fileNum}).ttmp2");
                while (File.Exists(modPackPath))
                {
                    fileNum++;
                    modPackPath = Path.Combine(destination, $"{modPackName}({fileNum}).ttmp2");
                }
            }
            else if (File.Exists(modPackPath) && overwriteModpack)
            {
                File.Delete(modPackPath);
            }

            return modPackPath;
        }

        public async Task Write(IProgress<(int current, int total, string message)> progress, string destination, bool overwriteModpack)
        {
            string tempMPD = Path.Combine(_tmpDir, "TTMPD.mpd");
            string tempMPL = Path.Combine(_tmpDir, "TTMPL.mpl");

            // Write MPD
            await _writeMPD(tempMPD, progress);

            // Write MPL
            File.WriteAllText(tempMPL, JsonConvert.SerializeObject(_modPackJson));

            // Write TTMP (zip)
            var modPackPath = _determineModPackPath(destination, overwriteModpack);

            progress?.Report((_addedFiles.Count, _addedFiles.Count, GeneralStrings.TTMP_Creating));
            var zf = new ZipFile();
            zf.UseZip64WhenSaving = Zip64Option.AsNecessary;
            zf.CompressionLevel = Ionic.Zlib.CompressionLevel.None;

            zf.AddFile(tempMPL, "");
            zf.AddFile(tempMPD, "");

            // Wizard-style modpacks can also contain images
            foreach (var image in _imageList)
                zf.AddFile(image, "images");

            zf.Save(modPackPath);

            File.Delete(tempMPD);
            File.Delete(tempMPL);
        }

        // Clean up temporary directory contents
        public void Dispose()
        {
            Directory.Delete(_tmpDir, true);
        }
    }
}
