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
using xivModdingFramework.Mods;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.VFX.FileTypes
{
    public static class Avfx
    {

        /// <summary>
        /// Gets the .atex paths that are within the .avfx file
        /// </summary>
        /// <param name="offset">The offset to the avfx file</param>
        /// <returns>A list of atex paths</returns>
        public static async Task<List<string>> GetATexPaths(string path, bool forceOriginal = false, ModTransaction tx = null)
        {
            var atexList = new List<string>();


            var avfxData = await Dat.ReadFile(path, forceOriginal, tx);

            await Task.Run(() =>
            {
                using (var br = new BinaryReader(new MemoryStream(avfxData)))
                {
                    var data = br.ReadInt32();

                    // Advance to the path data header
                    while (data != 5531000)
                    {
                        try
                        {
                            data = br.ReadInt32();
                        }
                        catch (EndOfStreamException)
                        {
                            return;
                        }                        
                    }

                    // While the data is a path data header
                    while (data == 5531000)
                    {
                        var pathLength = br.ReadInt32();

                        atexList.Add(Encoding.UTF8.GetString(br.ReadBytes(pathLength)).Replace("\0", ""));

                        try
                        {
                            byte ch = 0;

                            // Read until a byte of 120 or end of stream...?
                            var read = 0;
                            while(ch != 120)
                            {
                                if(br.BaseStream.Length == br.BaseStream.Position + 1)
                                {
                                    // End of Stream.
                                    break;
                                }

                                if(ch != 0)
                                {
                                    // End of block.
                                    break;
                                }
                                read++;
                                ch = br.ReadByte();
                            }
                            br.BaseStream.Seek(-1, SeekOrigin.Current);
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