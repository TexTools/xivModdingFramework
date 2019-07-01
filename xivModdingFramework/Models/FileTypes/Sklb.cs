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
using SharpDX;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
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
        private  IItem _item;
        private  XivMdl _xivMdl;
        private string _hairSklbName;

        public Sklb(DirectoryInfo gameDirectory, XivDataFile dataFile)
        {
            _gameDirectory = gameDirectory;
            _dataFile = dataFile;
        }


        /// <summary>
        /// Creates a Skel file from a Sklb
        /// </summary>
        /// <remarks>
        /// The skel file is the Sklb havok data parsed into json objects
        /// </remarks>
        /// <param name="item">The item we are getting the skeleton for</param>
        /// <param name="model">The model we are getting the skeleton for</param>
        public async Task CreateSkelFromSklb(IItem item, XivMdl model)
        {
            var hasAssetcc = File.Exists(Directory.GetCurrentDirectory() + "\\AssetCc2.exe");
            
            if (hasAssetcc)
            {
                _item = item;
                _xivMdl = model;

                var mdlFile = Path.GetFileNameWithoutExtension(model.MdlPath.File);

                var sklbName = mdlFile.Substring(0, 5);

                if (item.ItemCategory.Equals(XivStrings.Head))
                {
                    sklbName = mdlFile.Substring(5, 5);
                }
                else if (item.ItemCategory.Equals(XivStrings.Hair))
                {
                    var hBones = (from bone in model.PathData.BoneList where bone.Contains("x_h") select bone).ToList();

                    if (hBones.Count > 0)
                    {
                        sklbName = hBones[0].Substring(hBones[0].IndexOf("x_h") + 2, 5);
                    }
                    else
                    {
                        sklbName = mdlFile.Substring(5, 5);
                    }

                    _hairSklbName = sklbName;
                }

                var skelLoc = Directory.GetCurrentDirectory() + "\\Skeletons\\";

                Directory.CreateDirectory(skelLoc);

                if (!File.Exists(skelLoc + sklbName + ".xml"))
                {
                    await GetSkeleton(mdlFile, item.ItemCategory);

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
                throw new FileNotFoundException("AssetCc2 could not be found.");
            }
        }

        /// <summary>
        /// Gets the skeleton sklb file
        /// </summary>
        /// <param name="modelName">The name of the model</param>
        /// <param name="category">The items category</param>
        private async Task GetSkeleton(string modelName, string category)
        {
            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);

            var skelFolder = "";
            var skelFile = "";
            var slotAbr = "";

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
                slotAbr = SlotAbbreviationDictionary[category];

                var id = modelName.Substring(6, 4);

                if (_item.ItemCategory.Equals(XivStrings.Hair))
                {
                    id = _hairSklbName.Substring(1);
                }

                if (slotAbr.Equals("base"))
                {
                    id = "0001";
                }

                skelFolder = string.Format(XivStrings.EquipSkelFolder, modelName.Substring(1, 4), slotAbr, slotAbr[0], id);
                skelFile = string.Format(XivStrings.EquipSkelFile, modelName.Substring(1, 4), slotAbr[0], id);
            }

            // Continue only if the skeleton file exists
            if (!await index.FileExists(HashGenerator.GetHash(skelFile), HashGenerator.GetHash(skelFolder), _dataFile))
            {
                // Sometimes for face skeletons id 0001 does not exist but 0002 does
                if (_item.ItemCategory.Equals(XivStrings.Face))
                {

                    skelFolder = string.Format(XivStrings.EquipSkelFolder, modelName.Substring(1, 4), slotAbr, slotAbr[0], "0002");
                    skelFile = string.Format(XivStrings.EquipSkelFile, modelName.Substring(1, 4), slotAbr[0], "0002");

                    if (!await index.FileExists(HashGenerator.GetHash(skelFile), HashGenerator.GetHash(skelFolder),
                        _dataFile))
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }
            }

            var offset = await index.GetDataOffset(HashGenerator.GetHash(skelFolder), HashGenerator.GetHash(skelFile), _dataFile);

            if (offset == 0)
            {
                throw new Exception($"Could not find offest for {skelFolder}/{skelFile}");
            }

            var sklbData = await dat.GetType2Data(offset, _dataFile);

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
                else if (category.Equals(XivStrings.Hair))
                {
                    mName = _hairSklbName;
                }

                File.WriteAllBytes(Directory.GetCurrentDirectory() + "/Skeletons/" + mName + ".sklb", havokData);
            }
        }

        /// <summary>
        /// Parses the sklb file
        /// </summary>
        /// <param name="skelLoc">The location of the skeleton file</param>
        private void ParseSkeleton(string skelLoc)
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

            if (_item.ItemCategory.Equals(XivStrings.Head) || _item.ItemCategory.Equals(XivStrings.Hair) || _item.ItemCategory.Equals(XivStrings.Face))
            {
                AddToRaceSkeleton(jsonBones);
            }
            else
            {
                File.WriteAllLines(Path.ChangeExtension(skelLoc, ".skel"), jsonBones.ToArray());
            }

            File.Delete(Path.ChangeExtension(skelLoc, ".sklb"));
            File.Delete(Path.ChangeExtension(skelLoc, ".xml"));
        }

        private void AddToRaceSkeleton(List<string> jsonBones)
        {
            var race = "";
            var raceSkeletonData = new List<SkeletonData>();
            var newSkeletonData = new List<SkeletonData>();
            var raceBoneNames = new List<string>();

            if (_item.ItemCategory.Equals(XivStrings.Head) || _item.ItemCategory.Equals(XivStrings.Hair) || _item.ItemCategory.Equals(XivStrings.Face))
            {
                race = _xivMdl.MdlPath.File.Substring(0, 5);
            }

            var skelLoc = Directory.GetCurrentDirectory() + "\\Skeletons\\";

            if (File.Exists(skelLoc + race + ".skel"))
            {
                var skeletonData = File.ReadAllLines(skelLoc + race + ".skel");

                foreach (var b in skeletonData)
                {
                    var j = JsonConvert.DeserializeObject<SkeletonData>(b);

                    raceSkeletonData.Add(j);
                    raceBoneNames.Add(j.BoneName);
                }

                foreach (var jsonBone in jsonBones)
                {
                    var bone = JsonConvert.DeserializeObject<SkeletonData>(jsonBone);
                    newSkeletonData.Add(bone);
                }

                foreach (var jsonBone in jsonBones)
                {
                    var bone = JsonConvert.DeserializeObject<SkeletonData>(jsonBone);

                    if (!raceBoneNames.Contains(bone.BoneName))
                    {
                        var baseBone = (from jBones in newSkeletonData
                            where jBones.BoneNumber == bone.BoneParent
                            select jBones).First();

                        var raceMatchBone = (from rBones in raceSkeletonData
                            where rBones.BoneName.Equals(baseBone.BoneName)
                            select rBones).First();

                        var lastBoneNum = raceSkeletonData.Last().BoneNumber;

                        bone.BoneNumber = lastBoneNum + 1;
                        bone.BoneParent = raceMatchBone.BoneNumber;

                        raceSkeletonData.Add(bone);
                        File.AppendAllText(skelLoc + race + ".skel", Environment.NewLine + JsonConvert.SerializeObject(bone));
                    }
                }
            }
        }


        /// <summary>
        /// A dictionary containing the slot abbreviations
        /// </summary>
        private static readonly Dictionary<string, string> SlotAbbreviationDictionary = new Dictionary<string, string>
        {
            {XivStrings.Head, "met"},
            {XivStrings.Hands, "base"},
            {XivStrings.Legs, "base"},
            {XivStrings.Feet, "base"},
            {XivStrings.Body, "base"},
            {XivStrings.Ears, "base"},
            {XivStrings.Neck, "base"},
            {XivStrings.Rings, "base"},
            {XivStrings.Wrists, "base"},
            {XivStrings.Head_Body, "base"},
            {XivStrings.Body_Hands, "base"},
            {XivStrings.Body_Hands_Legs, "base"},
            {XivStrings.Body_Legs_Feet, "base"},
            {XivStrings.Body_Hands_Legs_Feet, "base"},
            {XivStrings.Legs_Feet, "base"},
            {XivStrings.All, "base"},
            {XivStrings.Face, "face"},
            {XivStrings.Iris, "face"},
            {XivStrings.Etc, "face"},
            {XivStrings.Accessory, "base"},
            {XivStrings.Hair, "hair"},
            {XivStrings.Tail, "base" }
        };
    }
}