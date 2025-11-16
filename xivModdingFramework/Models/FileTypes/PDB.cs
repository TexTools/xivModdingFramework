using Newtonsoft.Json;
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Mods;
using xivModdingFramework.SqPack.DataContainers;
using HelixToolkit.SharpDX.Core;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.Models.FileTypes
{
    public static class PDB
    {
        public class BoneDeformSet
        {

            public ushort RaceId;
            public ushort TreeIndex;
            public uint DataOffset;
            public float Scale;
            public Dictionary<string, BoneDeform> Deforms = new Dictionary<string, BoneDeform>();
            public BoneDeformTreeEntry TreeEntry;
            public BoneDeformSet Parent;
            public List<BoneDeformSet> Children = new List<BoneDeformSet>();

            public BoneDeformSet()
            {

            }
        }

        public struct BoneDeformTreeEntry
        {
            public ushort ParentIndex;
            public ushort FirstChildIndex;
            public ushort NextSiblingIndex;
            public ushort DeformerIndex;
        }

        public struct BoneDeform
        {
            public string Name;
            public float[] Matrix;
        }
        public const string BoneDeformFile = "chara/xls/bonedeformer/human.pbd";

        public static async Task<BoneDeformSet> GetBoneDeformSet(XivRace race, ModTransaction tx = null)
        {
            return await GetBoneDeformSet((ushort)race.GetRaceCodeInt());
        }
        public static async Task<BoneDeformSet> GetBoneDeformSet(ushort raceCode, ModTransaction tx = null)
        {
            var sets = await GetBoneDeformSets(tx);

            return sets.FirstOrDefault(x => x.Value.RaceId == raceCode).Value;
        }
        public static async Task<Dictionary<ushort, BoneDeformSet>> GetBoneDeformSets(ModTransaction tx = null)
        {
            if(tx == null)
            {
                // Readonly TX if we don't have one.
                tx = ModTransaction.BeginReadonlyTransaction();
            }
            var data = await Dat.ReadFile(BoneDeformFile, false, tx);

            var result = new BoneDeform();
            var numSets = 0;
            Dictionary<ushort, BoneDeformSet> DeformSets = new Dictionary<ushort, BoneDeformSet>();
            using (var br = new BinaryReader(new MemoryStream(data)))
            {
                numSets = br.ReadInt32();

                // Read Set Headers
                for (int setId = 0; setId < numSets; setId++)
                {
                    var bs = new BoneDeformSet();
                    bs.RaceId = br.ReadUInt16();
                    bs.TreeIndex = br.ReadUInt16();
                    bs.DataOffset = br.ReadUInt32();
                    bs.Scale = br.ReadSingle();
                    if (bs.RaceId == ushort.MaxValue)
                    {
                        continue;
                    }

                    if (DeformSets.ContainsKey(bs.RaceId))
                    {
                        throw new Exception("Multiple entries of same Race ID in Deformation PDB File.");
                    }

                    DeformSets.Add(bs.RaceId, bs);
                }

                // Read Tree entries.
                for (int treeId = 0; treeId < numSets; treeId++)
                {
                    var te = new BoneDeformTreeEntry();

                    te.ParentIndex = br.ReadUInt16();
                    te.FirstChildIndex = br.ReadUInt16();
                    te.NextSiblingIndex = br.ReadUInt16();
                    te.DeformerIndex = br.ReadUInt16();

                    var owner = DeformSets.FirstOrDefault(x => x.Value.TreeIndex == treeId).Value;
                    if (owner.RaceId == 0)
                    {
                        throw new Exception("Un-owned deformation tree entry in Deformation PDB File.");
                    }
                    owner.TreeEntry = te;
                    DeformSets[owner.RaceId] = owner;
                }

                // Bind parent-child relationships.
                foreach(var s in DeformSets.Values)
                {
                    var parentTreeId = s.TreeEntry.ParentIndex;
                    var parent = DeformSets.Values.FirstOrDefault(x => x.TreeIndex == parentTreeId);
                    if (parent == null) continue;

                    s.Parent = parent;
                    parent.Children.Add(s);
                }

                // Read Data for each set.
                foreach (var kv in DeformSets)
                {
                    var bs = kv.Value;
                    if (bs.DataOffset == 0)
                    {
                        continue;
                    }

                    br.BaseStream.Seek(bs.DataOffset, SeekOrigin.Begin);
                    var start = br.BaseStream.Position;

                    var numBones = br.ReadInt32();

                    // Read bone names.
                    List<uint> boneOffsets = new List<uint>();
                    List<string> bones = new List<string>();
                    for (int i = 0; i < numBones; i++)
                    {
                        boneOffsets.Add(br.ReadUInt16() + (uint)start);
                    }

                    var current = br.BaseStream.Position;
                    for (int i = 0; i < numBones; i++)
                    {
                        br.BaseStream.Seek(boneOffsets[i], SeekOrigin.Begin);
                        bones.Add(IOUtil.ReadNullTerminatedString(br));
                    }
                    br.BaseStream.Seek(current, SeekOrigin.Begin);

                    // Padded to 4 bytes.
                    while (br.BaseStream.Position % 4 != 0)
                    {
                        br.ReadByte();
                    }

                    // Read the deformation matrix for every bone.
                    for (int i = 0; i < numBones; i++)
                    {
                        float[] matrixData = new float[16];
                        for (int fi = 0; fi < 12; fi++)
                        {
                            var f = br.ReadSingle();
                            matrixData[fi] = f;
                        }
                        // SE doesn't store the final row (or column, depending on how you look at it), which is 0,0,0,1 for a standard transform matrix.
                        matrixData[15] = 1;

                        var defEntry = new BoneDeform();
                        defEntry.Matrix = matrixData;
                        defEntry.Name = bones[i];
                        bs.Deforms.Add(bones[i], defEntry);
                    }
                }
            }

            return DeformSets;
        }


        #region Model Deformation Handling
        /// <summary>
        /// Classes used in reading bone deformation data.
        /// </summary>
        private class DeformationBoneSet
        {
            public List<DeformationBoneData> Data = new List<DeformationBoneData>();
        }
        private class DeformationBoneData
        {
            public string Name;
            public float[] Matrix = new float[16];
        }


        public class DeformationCollection
        {
            public Dictionary<string, Matrix> Deformations = new Dictionary<string, Matrix>();
            public Dictionary<string, Matrix> InvertedDeformations = new Dictionary<string, Matrix>();
            public Dictionary<string, Matrix> NormalDeformations = new Dictionary<string, Matrix>();
            public Dictionary<string, Matrix> InvertedNormalDeformations = new Dictionary<string, Matrix>();

        }

        /// <summary>
        /// Retrieves the full set of calculated deformation matrices for a given race.
        /// </summary>
        /// <param name="race"></param>
        /// <param name="deformations"></param>
        /// <param name="recalculated"></param>
        public static async Task<DeformationCollection> GetDeformationMatrices(XivRace race, ModTransaction tx = null)
        {
            var ret = new DeformationCollection();

            var deformSet = await PDB.GetBoneDeformSet(race, tx);

            foreach (var set in deformSet.Deforms)
            {
                ret.Deformations.Add(set.Value.Name, new Matrix(set.Value.Matrix));
            }

            var skelName = "c" + race.GetRaceCode();
            var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            var skeletonFile = cwd + "/Skeletons/" + skelName + "b0001.skel";

            if (!File.Exists(skeletonFile))
            {
                // Need to extract the Skel file real quick like.
                var tempRoot = new XivDependencyRootInfo();
                tempRoot.PrimaryType = XivItemType.equipment;
                tempRoot.PrimaryId = 0;
                tempRoot.Slot = "top";
                Task.Run(async () =>
                {
                    await Sklb.GetBaseSkeletonFile(tempRoot, race);
                }).Wait();
            }

            var skeletonData = File.ReadAllLines(skeletonFile);
            var FullSkel = new Dictionary<string, SkeletonData>();

            foreach (var b in skeletonData)
            {
                if (b == "") continue;
                var j = JsonConvert.DeserializeObject<SkeletonData>(b);

                FullSkel.Add(j.BoneName, j);
            }

            // Scan the midlander skel and snap in any missing bones.
            // Their matrices don't matter, only their inheritance.
            var mSkel = GetMidlanderSkeleton();
            foreach(var b in mSkel)
            {
                if (!FullSkel.ContainsKey(b.Key))
                {
                    var p = mSkel.FirstOrDefault(x => x.Value.BoneNumber == b.Value.BoneParent);
                    if(p.Key != null)
                    {
                        var parent = p.Value.BoneName;
                        var toAdd = b;

                        var newParent = FullSkel.FirstOrDefault(x => x.Value.BoneName == parent);
                        if(newParent.Key != null)
                        {
                            var bc = (SkeletonData)b.Value.Clone();
                            bc.BoneParent = newParent.Value.BoneNumber;
                            FullSkel.Add(bc.BoneName, bc);
                        }
                    }
                }
            }

            var root = FullSkel["n_root"];


            BuildNewTransfromMatrices(root, FullSkel, ret);
            return ret;
        }

        private static Dictionary<string, SkeletonData> GetMidlanderSkeleton()
        {

            var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            var midSkel = cwd + "/Skeletons/c0101b0001.skel";
            if (!File.Exists(midSkel))
            {
                // Need to extract the Skel file real quick like.
                var tempRoot = new XivDependencyRootInfo();
                tempRoot.PrimaryType = XivItemType.equipment;
                tempRoot.PrimaryId = 0;
                tempRoot.Slot = "top";
                Task.Run(async () =>
                {
                    await Sklb.GetBaseSkeletonFile(tempRoot, XivRace.Hyur_Midlander_Male);
                }).Wait();
            }

            var midSkelData = File.ReadAllLines(midSkel);
            var skel = new Dictionary<string, SkeletonData>();
            foreach (var b in midSkelData)
            {
                if (b == "") continue;
                var j = JsonConvert.DeserializeObject<SkeletonData>(b);
                skel.Add(j.BoneName, j);
            }

            return skel;
        }

        /// <summary>
        /// Builds the full set of forward/backwards and normalmodifier matrices from the original deformation matrices.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="skeletonData"></param>
        /// <param name="def"></param>
        private static void BuildNewTransfromMatrices(SkeletonData node, Dictionary<string, SkeletonData> skeletonData, DeformationCollection def)
        {
            // Already processed somehow.  Double listed?
            if (def.InvertedDeformations.ContainsKey(node.BoneName))
                return;

            if (def.Deformations.ContainsKey(node.BoneName))
            {
                var invertedMatrix = def.Deformations[node.BoneName].Inverted();
                def.InvertedDeformations.Add(node.BoneName, invertedMatrix);

                var normalMatrix = invertedMatrix;
                normalMatrix.Transpose();
                def.NormalDeformations.Add(node.BoneName, normalMatrix);

                var invertedNormalMatrix = normalMatrix.Inverted();
                def.InvertedNormalDeformations.Add(node.BoneName, invertedNormalMatrix);
            }
            else
            {
                if (node.BoneParent == -1 || !skeletonData.ContainsKey(node.BoneName))
                {
                    def.Deformations[node.BoneName] = Matrix.Identity;
                    def.InvertedDeformations[node.BoneName] = Matrix.Identity;
                    def.NormalDeformations[node.BoneName] = Matrix.Identity;
                    def.InvertedNormalDeformations[node.BoneName] = Matrix.Identity;
                }
                else
                {
                    var skelEntry = skeletonData[node.BoneName];
                    while (skelEntry != null)
                    {
                        if (def.Deformations.ContainsKey(skelEntry.BoneName))
                        {
                            // This parent has a deform.
                            def.Deformations[node.BoneName] = def.Deformations[skelEntry.BoneName];
                            def.InvertedDeformations[node.BoneName] = def.InvertedDeformations[skelEntry.BoneName];
                            def.NormalDeformations[node.BoneName] = def.NormalDeformations[skelEntry.BoneName];
                            def.InvertedNormalDeformations[node.BoneName] = def.InvertedNormalDeformations[skelEntry.BoneName];
                            break;
                        }

                        // Seek our next parent.
                        skelEntry = skeletonData.FirstOrDefault(x => x.Value.BoneNumber == skelEntry.BoneParent).Value;
                    }

                    if (skelEntry == null)
                    {
                        def.Deformations[node.BoneName] = Matrix.Identity;
                        def.InvertedDeformations[node.BoneName] = Matrix.Identity;
                        def.NormalDeformations[node.BoneName] = Matrix.Identity;
                        def.InvertedNormalDeformations[node.BoneName] = Matrix.Identity;
                    }
                }
            }

            var children = skeletonData.Where(x => x.Value.BoneParent == node.BoneNumber);
            foreach (var c in children)
            {
                BuildNewTransfromMatrices(c.Value, skeletonData, def);
            }
        }

        #endregion
    }
}
