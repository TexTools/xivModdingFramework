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

using System.IO;
using xivModdingFramework.Resources;

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


    }
}