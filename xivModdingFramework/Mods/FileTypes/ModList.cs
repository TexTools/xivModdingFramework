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

using Newtonsoft.Json;
using System;
using System.IO;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.Enums;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.Mods.FileTypes
{
    /// <summary>
    /// This class contains the methods that deal with the .modlist file
    /// </summary>
    public class ModList
    {
        private readonly DirectoryInfo _gameDirectory;


        /// <summary>
        /// Sets the modlist with a provided name
        /// </summary>
        /// <param name="modlistDirectory">The directory in which to place the Modlist</param>
        /// <param name="modListName">The name to give the modlist file</param>
        public ModList(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
        }

        /// <summary>
        /// Creates the Mod List that is used to keep track of mods.
        /// </summary>
        public void CreateModlist()
        {
            var gameDir = _gameDirectory.Parent.Parent;

            var modListPath = gameDir.FullName + "\\" + XivStrings.ModlistFilePath;

            if (!File.Exists(modListPath))
            {
                File.Create(modListPath);
            }
        }

        /// <summary>
        /// Tries to get the mod entry for the given internal file path, return null otherwise
        /// </summary>
        /// <param name="internalFilePath">The internal file path to find</param>
        /// <returns>ModInfo and line number if it exists, Null otherwise</returns>
        public (ModInfo ModInfo, int LineNum)? TryGetModEntry(string internalFilePath)
        {
            var modListDirectory = new DirectoryInfo(Path.Combine(_gameDirectory.Parent.Parent.FullName, XivStrings.ModlistFilePath));

            var lineNum = 0;
            using (var streamReader = new StreamReader(modListDirectory.FullName))
            {
                string line;
                while ((line = streamReader.ReadLine()) != null)
                {
                    var modInfo = JsonConvert.DeserializeObject<ModInfo>(line);
                    if (modInfo.fullPath.Equals(internalFilePath))
                    {
                        return (modInfo, lineNum);
                    }
                    lineNum++;
                }

            }

            return null;
        }

        /// <summary>
        /// Checks to see whether the mod is currently enabled
        /// </summary>
        /// <param name="internalPath">The internal path of the file</param>
        /// <param name="dataFile">The data file to check in</param>
        /// <returns></returns>
        public XivModStatus IsModEnabled(string internalPath)
        {
            var modListDirectory = new DirectoryInfo(Path.Combine(_gameDirectory.Parent.Parent.FullName, XivStrings.ModlistFilePath));

            if (!File.Exists(modListDirectory.FullName))
            {
                return XivModStatus.Original;
            }

            var index = new Index(_gameDirectory);

            var modInfo = TryGetModEntry(internalPath)?.ModInfo;

            if (modInfo == null)
            {
                return XivModStatus.Original;
            }

            var originalOffset = modInfo.originalOffset;
            var moddedOffset = modInfo.modOffset;

            var offset = index.GetDataOffset(HashGenerator.GetHash(Path.GetDirectoryName(internalPath).Replace("\\", "/")),
                HashGenerator.GetHash(Path.GetFileName(internalPath)), XivDataFiles.GetXivDataFile(modInfo.datFile));

            if (offset.Equals(originalOffset))
            {
                return XivModStatus.Disabled;
            }

            if (offset.Equals(moddedOffset))
            {
                return XivModStatus.Enabled;
            }

            throw new Exception("Offset in Index does not match either original or modded offset in modlist.");


        }


        public void ToggleModStatus(string internalFilePath, bool enable)
        {
            var index = new Index(_gameDirectory);

            var modInfo = TryGetModEntry(internalFilePath)?.ModInfo;

            if (modInfo == null)
            {
                throw new Exception("Unable to find mod entry in modlist.");
            }

            if (enable)
            {
                index.UpdateIndex(modInfo.modOffset, internalFilePath, XivDataFiles.GetXivDataFile(modInfo.datFile));
                index.UpdateIndex2(modInfo.modOffset, internalFilePath, XivDataFiles.GetXivDataFile(modInfo.datFile));
            }
            else
            {
                index.UpdateIndex(modInfo.originalOffset, internalFilePath, XivDataFiles.GetXivDataFile(modInfo.datFile));
                index.UpdateIndex2(modInfo.originalOffset, internalFilePath, XivDataFiles.GetXivDataFile(modInfo.datFile));
            }

        }
    }
}