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

using System;
using System.IO;
using xivModdingFramework.General.Enums;

namespace xivModdingFramework
{
    public class GameInfo
    {
        public const string GameVersionFileName = "ffxivgame.ver";

        /// <summary>
        /// The directory in which the game is installed.
        /// </summary>
        public DirectoryInfo GameDirectory { get; }

        /// <summary>
        /// The current version of the game.
        /// </summary>
        public Version GameVersion { get; }

        public string GameVersionFile { get
            {
                return GetGameVersionFile();
            } 
        }


        /// <summary>
        /// The language used when parsing the game data.
        /// </summary>
        public XivLanguage GameLanguage { get; }


        /// <summary>
        /// Constructor for GameInfo
        /// </summary>
        /// <param name="gameDirectory">The directory in which the game is installed.</param>
        /// <param name="xivLanguage">The language to use when parsing the game data.</param>
        public GameInfo(DirectoryInfo gameDirectory, XivLanguage xivLanguage)
        {
            GameDirectory = gameDirectory;
            GameLanguage  = xivLanguage;
            try
            {
                GameVersion = GetGameVersion();
            } catch
            {
                // Default.  No version file is a non-critical bug.
                GameVersion = new Version("0.0.0.0");
            }

            if (!gameDirectory.FullName.Contains(Path.Combine("game", "sqpack", "ffxiv")))
            {
                throw new DirectoryNotFoundException("The given directory is incorrect.\n\nThe directory sould point to the \\game\\sqpack\\ffxiv folder");
            }
        }

        private string GetGameVersionFile()
        {
            try
            {
                var versionBasePath = GameDirectory.FullName.Substring(0, GameDirectory.FullName.IndexOf("sqpack", StringComparison.Ordinal));
                var versionFile = Path.Combine(versionBasePath, GameVersionFileName);

                if (!File.Exists(versionFile))
                {
                    return null;
                }

                return versionFile;
            }
            catch
            {
                return null;
            }

        }

        public static Version ReadVersionFile(string file)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                {
                    return new Version();
                }
                var versionData = File.ReadAllLines(file);
                return new Version(versionData[0].Substring(0, versionData[0].LastIndexOf(".", StringComparison.Ordinal)));
            } catch {
                return new Version();
            }
        }

        /// <summary>
        /// Gets the games current version.
        /// </summary>
        /// <returns>The game version.</returns>
        private Version GetGameVersion()
        {
            try
            {
                var file = GetGameVersionFile();
                if (string.IsNullOrWhiteSpace(file))
                {
                    return new Version();
                }
                return ReadVersionFile(file);
            }
            catch
            {
                return new Version();
            }
        }
    }
}