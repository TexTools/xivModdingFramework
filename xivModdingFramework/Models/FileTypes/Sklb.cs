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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Newtonsoft.Json;
using SharpDX;
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
        private readonly XivDataFile _dataFile;

        public Sklb(DirectoryInfo gameDirectory, XivDataFile dataFile)
        {
            _gameDirectory = gameDirectory;
            _dataFile = dataFile;
        }


        public void CreateSkelFromSklb(IItem item, XivMdl model)
        {
            var hasAssetcc = File.Exists(Directory.GetCurrentDirectory() + "/AssetCc2.exe");

            if (hasAssetcc)
            {
                var mdlFile = Path.GetFileNameWithoutExtension(model.MdlPath.File);

                var sklbName = mdlFile.Substring(0, 5);

                if (item.ItemCategory.Equals(XivStrings.Head))
                {
                    sklbName = mdlFile.Substring(5, 5);
                }

                var skelLoc = Directory.GetCurrentDirectory() + "\\Skeletons\\";

                if (!File.Exists(skelLoc + sklbName + ".xml"))
                {
                    GetSkeleton(mdlFile, item.ItemCategory);

                    var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = Directory.GetCurrentDirectory() + "/AssetCc2.exe",
                            Arguments = "-s \"" + skelLoc + "\\" + sklbName + ".sklb\" \"" + skelLoc + "\\" + sklbName + ".xml\"",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    proc.Start();
                    proc.WaitForExit();
                }

                ParseSkeleton(skelLoc + sklbName + ".xml");
            }
            else
            {
                throw new Exception("AssetCc2 could not be found.");
            }
        }

        private void GetSkeleton(string modelName, string category)
        {
            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);

            var skelFolder = "";
            var skelFile = "";
            if (modelName[0].Equals('w'))
            {
                skelFolder = string.Format(XivStrings.WeapSkelFolder, modelName.Substring(1, 4), "0001");
                skelFile = string.Format(XivStrings.WeapSkelFile, modelName.Substring(1, 4), "0001");
            }
            else if (modelName[0].Equals('m'))
            {
                skelFolder = string.Format(XivStrings.MonsterSkelFolder, modelName.Substring(1, 4), "0001");
                skelFile = string.Format(XivStrings.MonsterSkelFile, modelName.Substring(1, 4), "0001");
            }
            else if (modelName[0].Equals('d'))
            {
                skelFolder = string.Format(XivStrings.DemiSkelFolder, modelName.Substring(1, 4), "0001");
                skelFile = string.Format(XivStrings.DemiSkelFile, modelName.Substring(1, 4), "0001");
            }
            else
            {
                var abr = SlotAbbreviationDictionary[category];

                skelFolder = string.Format(XivStrings.EquipSkelFolder, modelName.Substring(1, 4), abr, abr[0], modelName.Substring(6, 4));
                skelFile = string.Format(XivStrings.EquipSkelFile, modelName.Substring(1, 4), abr[0], modelName.Substring(6, 4));
            }

            // Continue only if the skeleton file exists
            if (!index.FileExists(HashGenerator.GetHash(skelFile), HashGenerator.GetHash(skelFolder), _dataFile)
            ) return;

            var offset = index.GetDataOffset(HashGenerator.GetHash(skelFolder), HashGenerator.GetHash(skelFile), _dataFile);

            var sklbData = dat.GetType2Data(offset, _dataFile);

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

                var mName = modelName.Substring(0, 5);

                if (category.Equals(XivStrings.Head))
                {
                    mName = modelName.Substring(5, 5);
                }

                File.WriteAllBytes(Directory.GetCurrentDirectory() + "/Skeletons/" + mName + ".sklb", havokData);
            }
        }

        private static void ParseSkeleton(string skelLoc)
        {
            var skelData = new SkeletonData();
            var jsonBones = new List<string>();

            var parentIndices = new List<int>();
            var boneNames = new List<string>();
            var matrix = new List<Matrix>();
            var boneCount = 0;

            using (var reader = XmlReader.Create(skelLoc))
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

            File.WriteAllLines(Path.ChangeExtension(skelLoc, ".skel"), jsonBones.ToArray());

            File.Delete(Path.ChangeExtension(skelLoc, ".sklb"));
            File.Delete(Path.ChangeExtension(skelLoc, ".xml"));
        }


        private static readonly Dictionary<string, string> SlotAbbreviationDictionary = new Dictionary<string, string>
        {
            {XivStrings.Head, "met"},
            {XivStrings.Hands, "glv"},
            {XivStrings.Legs, "dwn"},
            {XivStrings.Feet, "sho"},
            {XivStrings.Body, "top"},
            {XivStrings.Ears, "ear"},
            {XivStrings.Neck, "nek"},
            {XivStrings.Rings, "rir"},
            {XivStrings.Wrists, "wrs"},
            {XivStrings.Head_Body, "top"},
            {XivStrings.Body_Hands, "top"},
            {XivStrings.Body_Hands_Legs, "top"},
            {XivStrings.Body_Legs_Feet, "top"},
            {XivStrings.Body_Hands_Legs_Feet, "top"},
            {XivStrings.Legs_Feet, "top"},
            {XivStrings.All, "top"},
            {XivStrings.Face, "fac"},
            {XivStrings.Iris, "iri"},
            {XivStrings.Etc, "etc"},
            {XivStrings.Accessory, "acc"},
            {XivStrings.Hair, "hir"}
        };
    }
}