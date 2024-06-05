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
using xivModdingFramework.Mods;
using xivModdingFramework.SqPack.DataContainers;
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
                tx = ModTransaction.BeginTransaction();
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
    }
}
