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

using HelixToolkit.SharpDX.Core.Utilities;
using Newtonsoft.Json;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.Models.FileTypes
{
    /// <summary>
    /// This class deals with .sklb Skeleton files
    /// </summary>
    public class Sklb
    {
        private readonly DirectoryInfo _gameDirectory;
        private const string SkeletonsFolder = "Skeletons";
        public Sklb(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
        }

        /// <summary>
        /// Retrieves the base racial or body skeleton for a given model file, parsing it from the base
        /// game files to generate it, if necessary.
        /// </summary>
        /// <param name="fullMdlPath"></param>
        /// <returns></returns>
        public static async Task<string> GetBaseSkeletonFile(string fullMdlPath)
        {
            var file = await GetBaseSkelbPath(fullMdlPath);
            var skelName = Path.GetFileNameWithoutExtension(file).Replace("skl_", "");

            var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            var parsedFile = Path.Combine(cwd, SkeletonsFolder, skelName + ".skel");

            if(File.Exists(parsedFile))
            {
                return parsedFile;
            }
            await ExtractAndParseSkel(file);
            return parsedFile;

        }

        /// <summary>
        /// Retrieves the Ex skeleton file for a given model file, parsing it from the base
        /// game files to generate it, if necessary.
        /// </summary>
        /// <param name="fullMdlPath"></param>
        /// <returns></returns>
        public static async Task<string> GetExtraSkeletonFile(string fullMdlPath)
        {
            var file = await GetExtraSkelbPath(fullMdlPath);
            if (file == null) return null;

            var skelName = Path.GetFileNameWithoutExtension(file).Replace("skl_", "");

            var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            var parsedFile = Path.Combine(cwd, SkeletonsFolder, skelName + ".skel");

            if (File.Exists(parsedFile))
            {
                return parsedFile;
            }
            await ExtractAndParseSkel(file);
            return parsedFile;
        }

        private static async Task ExtractAndParseSkel(string file)
        {

            var skelName = Path.GetFileNameWithoutExtension(file).Replace("skl_", "");
            var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            var parsedFile = Path.Combine(cwd, SkeletonsFolder, skelName + ".skel");

            // Create skel folder if needed.
            Directory.CreateDirectory(Path.Combine(cwd, SkeletonsFolder));

            var rawFile = await ExtractSkelb(file);

            // Conver that to XML.
            var xmlFile = Path.ChangeExtension(parsedFile, ".xml");
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = cwd + "/NotAssetCc.exe",
                    Arguments = "\"" + rawFile + "\" \"" + xmlFile + "\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            proc.Start();
            proc.WaitForExit();

            ParseSkeleton(xmlFile);

            File.Delete(xmlFile);
            File.Delete(rawFile);
        }

        private static async Task<string> GetExtraSkelbPath(string fullMdlPath)
        {
            var root = await XivCache.GetFirstRoot(fullMdlPath);

            var type = Est.GetEstType(root);

            // Type doesn't use extra skels.
            if (type == Est.EstType.Invalid) return null;

            var fileName = Path.GetFileNameWithoutExtension(fullMdlPath);

            var id = (ushort)root.Info.PrimaryId;
            if (type == Est.EstType.Face || type == Est.EstType.Hair)
            {
                id = (ushort)root.Info.SecondaryId;
            }

            XivRace race = XivRace.All_Races;
            // Hair and face types have a race defined at root level.
            if (type == Est.EstType.Face || type == Est.EstType.Hair)
            {
                var ret = new Dictionary<XivRace, ExtraSkeletonEntry>();
                race = XivRaces.GetXivRace(root.Info.PrimaryId);
            } else
            {
                // Gear have race defined at model level.
                var rc = fileName.Substring(1, 4);
                race = XivRaces.GetXivRace(rc);
            }


            var entry = await Est.GetExtraSkeletonEntry(type, race, id);
            var skelId = entry.SkelId;
            if(skelId == 0)
            {
                // No extra skeleton for this model.
                return null;
            }

            var prefix = Est.GetSystemPrefix(type);
            var slot = Est.GetSystemSlot(type);
            var raceCode = XivRaces.GetRaceCode(race);
            var skelCode = skelId.ToString().PadLeft(4, '0');
            var path = $"chara/human/c{raceCode}/skeleton/{slot}/{prefix}{skelCode}/skl_c{raceCode}{prefix}{skelCode}.sklb";
            return path;
        }
        /// <summary>
        /// Retrieves the file path to the model's base skeleton file.
        /// </summary>
        /// <param name="fullMdlPath"></param>
        private static async Task<string> GetBaseSkelbPath(string fullMdlPath)
        {
            var skelFolder = "";
            var skelFile = "";
            if (IsNonhuman(fullMdlPath))
            {
                // Weapons / Monsters / Demihumans are simple enough cases, we just have to use different formatting strings.
                if (IsWeapon(fullMdlPath))
                {
                    skelFolder = string.Format(XivStrings.WeapSkelFolder, fullMdlPath.Substring(1, 4), "0001");
                    skelFile = string.Format(XivStrings.WeapSkelFile, fullMdlPath.Substring(1, 4), "0001");
                }
                else if (IsMonster(fullMdlPath))
                {
                    skelFolder = string.Format(XivStrings.MonsterSkelFolder, fullMdlPath.Substring(1, 4), "0001");
                    skelFile = string.Format(XivStrings.MonsterSkelFile, fullMdlPath.Substring(1, 4), "0001");
                }
                else if (IsDemihuman(fullMdlPath))
                {
                    skelFolder = string.Format(XivStrings.DemiSkelFolder, fullMdlPath.Substring(1, 4), "0001");
                    skelFile = string.Format(XivStrings.DemiSkelFile, fullMdlPath.Substring(1, 4), "0001");
                }
            } else
            {
                var fileName = Path.GetFileNameWithoutExtension(fullMdlPath);

                var raceCode = fileName.Substring(1, 4);
                var bodyCode = "0001";
                skelFolder = $"chara/human/c{raceCode}/skeleton/base/b{bodyCode}";
                skelFile = $"skl_c{raceCode}b{bodyCode}.sklb";
            }

            return skelFolder + "/" + skelFile;
        }

        /// <summary>
        /// Resolves the original skeleton path in the FFXIV file system and raw extracts it.
        /// </summary>
        /// <param name="fullMdlPath">Full path to the MDL.</param>
        /// <param name="internalSkelName">Internal skeleton name (for hair).  This can be resolved if missing, though it is slightly expensive to do so.</param>
        private static async Task<string> ExtractSkelb(string skelBPath)
        {
            var index = new Index(XivCache.GameInfo.GameDirectory);
            var dat = new Dat(XivCache.GameInfo.GameDirectory);
            var dataFile = IOUtil.GetDataFileFromPath(skelBPath);

            var offset = await index.GetDataOffset(skelBPath);

            if (offset == 0)
            {
                throw new Exception($"Could not find offset for {skelBPath}");
            }

            var sklbData = await dat.GetType2Data(offset, dataFile);

            using (var br = new BinaryReader(new MemoryStream(sklbData)))
            {
                br.BaseStream.Seek(0, SeekOrigin.Begin);

                var magic = br.ReadInt32();
                var format = br.ReadInt32();

                br.ReadBytes(2);

                if (magic != 0x736B6C62)
                {
                    throw new FormatException();
                }

                var dataOffset = 0;

                switch (format)
                {
                    case 0x31323030:
                        dataOffset = br.ReadInt16();
                        break;
                    case 0x31333030:
                    case 0x31333031:
                        br.ReadBytes(2);
                        dataOffset = br.ReadInt16();
                        break;
                    default:
                        throw new Exception($"Unkown Data Format ({format})");
                }

                br.BaseStream.Seek(dataOffset, SeekOrigin.Begin);

                var havokData = br.ReadBytes(sklbData.Length - dataOffset);

                var skelName = Path.GetFileNameWithoutExtension(skelBPath).Replace("skl_", "");
                var outputFile = skelName + ".sklb";

                var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
                var parsedFile = Path.Combine(cwd, SkeletonsFolder, outputFile);
                File.WriteAllBytes(parsedFile, havokData);
                return parsedFile;
            }
        }

        /// <summary>
        /// Parses the sklb file
        /// </summary>
        /// <param name="xmlFile">The location of the skeleton file</param>
        private static void ParseSkeleton(string xmlFile)
        {
            var skelData = new SkeletonData();
            var jsonBones = new List<string>();

            var parentIndices = new List<int>();
            var boneNames = new List<string>();
            var matrix = new List<Matrix>();
            var boneCount = 0;
            //var rawSkelName = Path.GetFileNameWithoutExtension(skelLoc);

            using (var reader = XmlReader.Create(xmlFile))
            {
                while (reader.Read())
                {
                    if (!reader.IsStartElement()) continue;

                    if (!reader.Name.Equals("hkparam")) continue;

                    var name = reader["name"];

                    if (name.Equals("parentIndices"))
                    {
                        parentIndices.AddRange((int[])reader.ReadElementContentAs(typeof(int[]), null));
                    }

                    if (!name.Equals("bones")) continue;

                
                    boneCount = int.Parse(reader["numelements"]);

                    while (reader.Read())
                    {
                        if (!reader.IsStartElement()) continue;

                        if (!reader.Name.Equals("hkparam")) continue;

                        name = reader["name"];
                        if (name.Equals("name"))
                        {
                            boneNames.Add(reader.ReadElementContentAsString());
                        }
                        if (name.Equals("referencePose"))
                        {
                            break;
                        }
                    }

                    var referencepose = reader.ReadElementContentAsString();

                    const string pattern = @"\(([^\(\)]+)\)";

                    var matches = Regex.Matches(referencepose, pattern);

                    for (var i = 0; i < matches.Count; i += 3)
                    {
                        var t = matches[i].Groups[1].Value.Split(' ');
                        var translation = new Vector3(float.Parse(t[0]), float.Parse(t[1]), float.Parse(t[2]));

                        var r = matches[i + 1].Groups[1].Value.Split(' ');
                        var rotation = new Vector4(float.Parse(r[0]), float.Parse(r[1]), float.Parse(r[2]), float.Parse(r[3]));

                        var s = matches[i + 2].Groups[1].Value.Split(' ');
                        var scale = new Vector3(float.Parse(s[0]), float.Parse(s[1]), float.Parse(s[2]));

                        var tMatrix = Matrix.Scaling(scale) * Matrix.RotationQuaternion(new Quaternion(rotation)) * Matrix.Translation(translation);

                        matrix.Add(tMatrix);

                    }

                    break;
                }
            }

            var referencePose = new Matrix[boneCount];
            for (var target = 0; target < boneCount; ++target)
            {
                var current = target;
                referencePose[target] = Matrix.Identity;
                while (current >= 0)
                {
                    referencePose[target] = referencePose[target] * matrix[current];

                    current = parentIndices[current];
                }
            }

            for (var i = 0; i < boneCount; i++)
            {
                skelData.BoneNumber = i;
                skelData.BoneName = boneNames[i];
                skelData.BoneParent = parentIndices[i];

                var poseMatrix = new List<float>();

                var rpl = matrix[i].ToArray();

                foreach (var f in rpl)
                {
                    if (f > 0.999 && f < 1)
                    {
                        poseMatrix.Add(1f);
                    }
                    else if (f > -0.001 && f < 0)
                    {
                        poseMatrix.Add(0f);
                    }
                    else if (f < 0.001 && f > 0)
                    {
                        poseMatrix.Add(0f);
                    }
                    else
                    {
                        poseMatrix.Add(f);
                    }
                }

                skelData.PoseMatrix = poseMatrix.ToArray();

                var inversePose = referencePose.Select(Matrix.Invert).ToArray();

                var iposeMatrix = new List<float>();

                var ipl = inversePose[i].ToArray();

                foreach (var f in ipl)
                {
                    if (f > 0.999 && f < 1)
                    {
                        iposeMatrix.Add(1f);
                    }
                    else if (f > -0.001 && f < 0)
                    {
                        iposeMatrix.Add(0f);
                    }
                    else if (f < 0.001 && f > 0)
                    {
                        iposeMatrix.Add(0f);
                    }
                    else
                    {
                        iposeMatrix.Add(f);
                    }
                }

                skelData.InversePoseMatrix = iposeMatrix.ToArray();

                jsonBones.Add(JsonConvert.SerializeObject(skelData));
            }



            File.WriteAllLines(Path.ChangeExtension(xmlFile, ".skel"), jsonBones.ToArray());
        }

        private static bool IsWeapon(string fullMdlPath)
        {
            return fullMdlPath.Contains("chara/weapon/");
        }
        private static bool IsMonster(string fullMdlPath)
        {
            return fullMdlPath.Contains("chara/monster");
        }
        private static bool IsDemihuman(string fullMdlPath)
        {
            return fullMdlPath.Contains("chara/demihuman");
        }
        
        private static bool IsNonhuman(string fullMdlPath)
        {
            return IsWeapon(fullMdlPath) || IsMonster(fullMdlPath) || IsDemihuman(fullMdlPath);
        }

    }
}