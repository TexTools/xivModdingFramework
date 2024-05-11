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

using HelixToolkit.SharpDX.Core;
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
using System.Xml.Schema;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Mods;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;

using Index = xivModdingFramework.SqPack.FileTypes.Index;

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
        /// 
        /// NOTE: Not Transaction safe... If the base skeleton files were altered during Transaction.
        /// Which seems niche enough to not worry about for now.
        /// </summary>
        /// <param name="fullMdlPath"></param>
        /// <returns></returns>
        public static async Task<string> GetBaseSkeletonFile(string fullMdlPath)
        {
            var root = await XivCache.GetFirstRoot(fullMdlPath);

            var race = XivRace.All_Races;
            if (root.Info.PrimaryType == XivItemType.human || root.Info.PrimaryType == XivItemType.equipment || root.Info.PrimaryType == XivItemType.accessory)
            {
                race = XivRaces.GetXivRace(fullMdlPath.Substring(1, 4));
            }

            return await GetBaseSkeletonFile(root.Info, race);
        }
        public static async Task<string> GetBaseSkeletonFile(XivDependencyRootInfo root, XivRace race, ModTransaction tx = null) 
        {
            var file = await GetBaseSkelbPath(root, race);

            var skelName = Path.GetFileNameWithoutExtension(file).Replace("skl_", "");

            var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            var parsedFile = Path.Combine(cwd, SkeletonsFolder, skelName + ".skel");

            if(File.Exists(parsedFile))
            {
                return parsedFile;
            }
            await ExtractAndParseSkel(file, tx);
            return parsedFile;

        }

        /// <summary>
        /// Retrieves the Ex skeleton file for a given model file, parsing it from the base
        /// game files to generate it, if necessary.
        /// 
        /// NOTE: NOT Transaction safe... If the base EST skeletons were modified during transaction?
        /// This is niche enough to leave for the moment and come back to if it proves an issue.
        /// </summary>
        /// <param name="fullMdlPath"></param>
        /// <returns></returns>
        public static async Task<string> GetExtraSkeletonFile(string fullMdlPath)
        {
            var root = await XivCache.GetFirstRoot(fullMdlPath);

            var estType = Est.GetEstType(root);
            if (estType == Est.EstType.Invalid) return null;

            // This is a hair/hat/face/body model at this point so this is a safe pull.
            var race = XivRaces.GetXivRace(fullMdlPath.Substring(1, 4));

            return await GetExtraSkeletonFile(root.Info, race);
            
        }


        public static async Task<string> GetExtraSkeletonFile(XivDependencyRootInfo root, XivRace race = XivRace.All_Races, ModTransaction tx = null)
        {
            if(root.SecondaryType == XivItemType.ear)
            {
                // Viera ears technically use their associated face's Extra Skeleton to resolve the bones.  Kinda weird, but whatever.
                // The bones are nearly identical for all the faces, so just use face 1
                var nRoot = new XivDependencyRootInfo()
                {
                    PrimaryType = root.PrimaryType,
                    PrimaryId = root.PrimaryId,
                    SecondaryId = 1,
                    SecondaryType = XivItemType.face,
                    Slot = "fac"
                };
                root = nRoot;
            }

            var file = await GetExtraSkelbPath(root, race);
            if (file == null) return null;

            var skelName = Path.GetFileNameWithoutExtension(file).Replace("skl_", "");

            var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            var parsedFile = Path.Combine(cwd, SkeletonsFolder, skelName + ".skel");

            if (File.Exists(parsedFile))
            {
                return parsedFile;
            }

            try
            {
                // In some cases, the extra skeleton doesn't actually exist, despite the 
                // game files saying it should.  In these cases, SE actually intends to 
                // default to the base skel.
                await ExtractAndParseSkel(file, tx);
            }
            catch
            {
                return null;
            }
            return parsedFile;
        }

        private static bool ContainsUnicodeCharacter(string input)
        {
            const int MaxAnsiCode = 255;

            return input.Any(c => c > MaxAnsiCode);
        }

        private static async Task ExtractAndParseSkel(string file, ModTransaction tx = null)
        {

            var skelName = Path.GetFileNameWithoutExtension(file).Replace("skl_", "");
            var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            var parsedFile = Path.Combine(cwd, SkeletonsFolder, skelName + ".skel");

            // Create skel folder if needed.
            Directory.CreateDirectory(Path.Combine(cwd, SkeletonsFolder));

            var rawFile = await ExtractSkelb(file, tx);

            var xmlFile = await ConvertSkelToXml(rawFile);

            ParseSkeleton(xmlFile);

            File.Delete(xmlFile);
            File.Delete(rawFile);
        }
        private static async Task<string> ConvertSkelToXml(string rawFile)
        {
            // Conver that to XML.

            var xmlFile = Path.ChangeExtension(rawFile, ".xml");
            var originalXml = xmlFile;
            var originalRaw = rawFile;

            File.Delete(xmlFile);

            var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);

            var application = "/NotAssetCc.exe";
            var extraFlags = "";

            bool usedTemp = false;

            // Okay, in this case, NotAssetCC won't work.
            if(ContainsUnicodeCharacter(cwd))
            {
                // Does AssetCC exist? Use that.
                if(File.Exists(cwd + "/AssetCc2.exe"))
                {
                    application = "/AssetCc2.exe";
                    extraFlags = "-s ";

                    // Asset CC2 *can* process files that exist in unicode directories, but they must be
                    // provided as relative paths which themselves do not contain unicode.
                    xmlFile = Path.Combine("Skeletons", Path.GetFileName(xmlFile));
                    rawFile = Path.Combine("Skeletons", Path.GetFileName(rawFile));
                } else
                {
                    usedTemp = true;

                    // Unicode path and we don't have AssetCC2.
                    // Check if a temp path has unicode in it.
                    var tempFileXml = Path.GetTempFileName();
                    var tempFileRaw = Path.GetTempFileName();

                    if (!ContainsUnicodeCharacter(tempFileXml) && !ContainsUnicodeCharacter(tempFileRaw))
                    {
                        // Okay, we can use a temp file instead.
                        xmlFile = tempFileXml;
                        rawFile = tempFileRaw;

                        // Copy the raw file to the temp folder.
                        File.Delete(tempFileRaw);
                        File.Delete(tempFileXml);
                        File.Copy(originalRaw, tempFileRaw);
                    } else
                    {
                        // Temp folder ALSO has unicode in it.  See if we can write to root then.
                        DriveInfo cDrive = new DriveInfo("C");
                        var rootDir = cDrive.RootDirectory;
                        var guid = Guid.NewGuid().ToString();
                        tempFileXml = Path.Combine(rootDir.FullName, guid + ".xml");
                        tempFileRaw = Path.Combine(rootDir.FullName, guid + ".sklb");

                        try
                        {
                            // Try to copy the sklb file into their root directory.
                            File.Copy(rawFile, tempFileRaw);

                            xmlFile = tempFileXml;
                            rawFile = tempFileRaw;
                        } catch
                        {
                            // We have a unicode path, asset CC doesn't exist, the Temp path is also unicode, and we can't write to the base root folder.
                            // At this point, we have to just give the user an error to get AssetCC or correct the folders.
                            throw new Exception("Cannot run NotAssetCC with Unicode paths.\nEither obtain AssetCc2.exe or run TexTools in a non-unicode path.");
                        }
                    }
                }
            }


            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = cwd + application,
                    Arguments = extraFlags + "\"" + rawFile + "\" \"" + xmlFile + "\"",
                    WorkingDirectory = cwd,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            proc.WaitForExit();

            if(usedTemp)
            {
                // Copy the file back into the right position if needed and delete temp folder items.
                File.Copy(xmlFile, originalXml);
                File.Delete(xmlFile);
                File.Delete(rawFile);
            }


            return originalXml;
        }

        private static async Task<string> GetExtraSkelbPath(XivDependencyRootInfo root, XivRace race = XivRace.All_Races)
        {

            var type = Est.GetEstType(root);

            // Type doesn't use extra skels.
            if (type == Est.EstType.Invalid) return null;


            var id = (ushort)root.PrimaryId;
            if (type == Est.EstType.Face || type == Est.EstType.Hair)
            {
                id = (ushort)root.SecondaryId;
            }

            // Hair and face types have a race defined at root level.
            if ((type == Est.EstType.Face || type == Est.EstType.Hair) && race == XivRace.All_Races)
            {
                var ret = new Dictionary<XivRace, ExtraSkeletonEntry>();
                race = XivRaces.GetXivRace(root.PrimaryId);
            }


            var entry = await Est.GetExtraSkeletonEntry(type, race, id);
            var skelId = entry.SkelId;
            if(skelId == 0)
            {
                if (race == XivRace.Hyur_Midlander_Male) return null;

                // Okay, this model, *as defined* has no EX Skeleton.  _HOWEVER_ its parent skeleton could.
                // So we need to re-call and check up the chain until we hit Hyur M.

                var parent = XivRaceTree.GetNode(race).Parent;
                if(parent == null)
                {
                    return null;
                }

                // It's worth noting in these cases, the Skeletal bones themselves will still be using the matrices appropriate
                // for their parent race in this method, but that should be sufficient for now.
                return await GetExtraSkelbPath(root, parent.Race);
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
        private static async Task<string> GetBaseSkelbPath(XivDependencyRootInfo root, XivRace race = XivRace.All_Races)
        {
            var skelFolder = "";
            var skelFile = "";
            if (root.PrimaryType != XivItemType.human && root.PrimaryType != XivItemType.equipment && root.PrimaryType != XivItemType.accessory)
            {
                var typeName = XivItemTypes.GetSystemName(root.PrimaryType);
                var prefix = XivItemTypes.GetSystemPrefix(root.PrimaryType);
                var id = root.PrimaryId.ToString().PadLeft(4, '0');
                var bodyCode = "0001";
                var path = $"chara/{typeName}/{prefix}{id}/skeleton/base/b{bodyCode}/skl_{prefix}{id}b{bodyCode}.sklb";
                return path;
            } else
            {

                // Equipment and accessories need an external race passed in.
                var raceCode = race.GetRaceCode();
                if (root.PrimaryType == XivItemType.human && race != XivRace.All_Races)
                {
                    raceCode = root.PrimaryId.ToString().PadLeft(4, '0');
                }

                var bodyCode = "0001";
                skelFolder = $"chara/human/c{raceCode}/skeleton/base/b{bodyCode}";
                skelFile = $"skl_c{raceCode}b{bodyCode}.sklb";
                return skelFolder + "/" + skelFile;
            }
        }

        /// <summary>
        /// Resolves the original skeleton path in the FFXIV file system and raw extracts it.
        /// </summary>
        /// <param name="fullMdlPath">Full path to the MDL.</param>
        /// <param name="internalSkelName">Internal skeleton name (for hair).  This can be resolved if missing, though it is slightly expensive to do so.</param>
        private static async Task<string> ExtractSkelb(string skelBPath, ModTransaction tx = null)
        {
            if (tx == null)
            {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginTransaction(true);
            }

            var index = new Index(XivCache.GameInfo.GameDirectory);
            var dat = new Dat(XivCache.GameInfo.GameDirectory);
            var dataFile = IOUtil.GetDataFileFromPath(skelBPath);

            var offset = await tx.Get8xDataOffset(skelBPath);

            if (offset == 0)
            {
                throw new Exception($"Could not find offset for {skelBPath}");
            }

            var sklbData = await dat.ReadSqPackType2(offset, dataFile);

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