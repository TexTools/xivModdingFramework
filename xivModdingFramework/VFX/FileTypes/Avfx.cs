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

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.General.Enums;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.VFX.FileTypes
{
    public class Avfx
    {
        private readonly DirectoryInfo _gameDirectory;
        private readonly XivDataFile _dataFile;

        public Avfx(DirectoryInfo gameDirectory, XivDataFile dataFile)
        {
            _gameDirectory = gameDirectory;
            _dataFile = dataFile;
        }

        /// <summary>
        /// Gets the .atex paths that are within the .avfx file
        /// </summary>
        /// <param name="offset">The offset to the avfx file</param>
        /// <returns>A list of atex paths</returns>
        public async Task<List<string>> GetATexPaths(int offset)
        {
            var atexList = new List<string>();

            var dat = new Dat(_gameDirectory);

            var avfxData = await dat.GetType2Data(offset, _dataFile);

            await Task.Run(() =>
            {
                using (var br = new BinaryReader(new MemoryStream(avfxData)))
                {
                    var data = br.ReadInt32();

                    // Advance to the path data header
                    while (data != 5531000)
                    {
                        data = br.ReadInt32();
                    }

                    // While the data is a path data header
                    while (data == 5531000)
                    {
                        var pathLength = br.ReadInt32();

                        atexList.Add(Encoding.UTF8.GetString(br.ReadBytes(pathLength)).Replace("\0", ""));

                        try
                        {
                            while (br.PeekChar() != 120)
                            {
                                if (br.PeekChar() == -1)
                                {
                                    break;
                                }

                                br.ReadByte();
                            }

                            data = br.ReadInt32();
                        }
                        catch
                        {
                            break;
                        }

                    }
                }
            });

            return atexList;
        }
    }
}