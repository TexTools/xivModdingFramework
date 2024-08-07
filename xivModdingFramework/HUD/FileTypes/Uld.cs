﻿// xivModdingFramework
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Mods;
using xivModdingFramework.SqPack.FileTypes;

using Index = xivModdingFramework.SqPack.FileTypes.Index;

namespace xivModdingFramework.HUD.FileTypes
{
    /// <summary>
    ///  This class contains the methods that deal with the .uld file type 
    /// </summary>
    public static class Uld
    {
        /// <summary>
        /// Gets the texture paths from the uld file
        /// </summary>
        /// <returns>List of texture paths from the uld file</returns>
        public static async Task<List<string>> GetTexFromUld(ModTransaction tx = null)
        {
            var uldLock = new object();
            var hashedFolder = HashGenerator.GetHash("ui/uld");

            var uldStringList = new HashSet<string>();
            var uldOffsetList = await Index.GetAllFileOffsetsInFolder(hashedFolder, XivDataFile._06_Ui, tx);

            var tasks = new List<Task>();
            foreach(var offset in uldOffsetList)
            {
                tasks.Add(Task.Run (async () =>
                {
                    byte[] uldData;
                    var type = await tx.GetSqPackType(XivDataFile._06_Ui, offset, false);
                    if (type == 2)
                    {
                        uldData = await tx.ReadFile(XivDataFile._06_Ui, offset);
                    }
                    else
                    {
                        return;
                    }

                    if (uldData.Length < 10) return;

                    using (var br = new BinaryReader(new MemoryStream(uldData)))
                    {
                        var signature = br.ReadInt32();

                        if (signature != 1751411829) return;

                        br.ReadBytes(56);

                        int pathCount = br.ReadByte();

                        br.ReadBytes(7);

                        for (var i = 0; i < pathCount; i++)
                        {
                            var pathNum = br.ReadInt32();
                            while (pathNum != i + 1)
                            {
                                pathNum = br.ReadInt32();
                            }

                            var path = Encoding.UTF8.GetString(br.ReadBytes(48)).Replace("\0", "");
                            path = new string(path.Where(c => !char.IsControl(c)).ToArray());

                            if (path.Length <= 2 || !path.Contains("uld")) continue;

                            var uldPath = path.Substring(0, path.LastIndexOf(".", StringComparison.Ordinal) + 4);

                            lock (uldLock)
                            {
                                uldStringList.Add(uldPath);
                            }
                        }
                    }
                }));
            }

            await Task.WhenAll(tasks);

            return uldStringList.ToList();
        }
    }
}