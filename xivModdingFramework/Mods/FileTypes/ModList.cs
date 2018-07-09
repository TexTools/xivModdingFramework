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

namespace xivModdingFramework.Mods.FileTypes
{
    /// <summary>
    /// This class contains the methods that deal with the .modlist file
    /// </summary>
    public class ModList
    {
        private readonly string _modListName;
        private readonly DirectoryInfo _modlistDirectory;


        /// <summary>
        /// Sets the modlist with a provided name
        /// </summary>
        /// <param name="modlistDirectory">The directory in which to place the Modlist</param>
        /// <param name="modListName">The name to give the modlist file</param>
        public ModList(DirectoryInfo modlistDirectory, string modListName)
        {
            _modlistDirectory = modlistDirectory;
            _modListName = modListName + ".modlist";
        }

        /// <summary>
        /// Sets the modlist with the default name
        /// </summary>
        /// <param name="modlistDirectory"></param>
        public ModList(DirectoryInfo modlistDirectory)
        {
            _modlistDirectory = modlistDirectory;
            _modListName = "TexTools.modlist";
        }


        /// <summary>
        /// Creates the Mod List that is used to keep track of mods.
        /// </summary>
        public void CreateModlist()
        {
            var modListPath = _modlistDirectory.FullName + "\\" + _modListName;
            Directory.CreateDirectory(_modlistDirectory.FullName);

            if (!File.Exists(modListPath))
            {
                File.Create(modListPath);
            }
        }


    }
}