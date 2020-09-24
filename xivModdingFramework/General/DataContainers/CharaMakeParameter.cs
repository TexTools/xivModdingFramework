using HelixToolkit.SharpDX.Core.Model;
using Ionic.Zip;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.General.DataContainers
{
    /// <summary>
    /// Data container representing an entire CMP file
    /// </summary>
    public class CharaMakeParameterSet
    {
        private List<RacialScalingParameter> RawRacialData = new List<RacialScalingParameter>();
        private List<byte[]> ColorPixels = new List<byte[]>();

        /// <summary>
        /// The point in the human.cmp file at which the racial scaling metadata begins.
        /// </summary>
        public const int MetadataStart = 0x2a800;

        public CharaMakeParameterSet(byte[] data)
        {
            ColorPixels.Capacity = MetadataStart / 4;

            var nextOffset = 0;
            for(int i = 0; i < MetadataStart; i+= 4)
            {
                var r = data[i];
                var g = data[i + 1];
                var b = data[i + 2];
                var a = data[i + 3];

                ColorPixels.Add(new byte[4] { r, g, b, a });
                nextOffset = i + 4;
            }

            if (nextOffset != MetadataStart) throw new Exception("CMP Format Changed - Unable to read all CMP data.");

            var rem = data.Length - MetadataStart;
            var entries = rem / RacialScalingParameter.TotalByteSize;

            for (int i = 0; i < entries; i++)
            {
                var offset = MetadataStart + (i * RacialScalingParameter.TotalByteSize);
                var arr = new byte[RacialScalingParameter.TotalByteSize];

                Array.Copy(data, offset, arr, 0, RacialScalingParameter.TotalByteSize);

                var rsp = new RacialScalingParameter(arr);
                RawRacialData.Add(rsp);

                nextOffset = offset + RacialScalingParameter.TotalByteSize;
            }

            if (nextOffset != data.Length) throw new Exception("CMP Format Changed - Unable to read all CMP data.");

        }

        public RacialGenderScalingParameter GetScalingParameter(XivSubRace Race, XivGender Gender)
        {
            var offset = ((int)Race.GetBaseRace() * 10) + Race.GetSubRaceId();
            var rsp = RawRacialData[offset];
            var rgsp = new RacialGenderScalingParameter(rsp, Race, Gender);

            return rgsp;
        }

        public void SetScalingParameters(List<RacialGenderScalingParameter> data) {
            foreach(var rgsp in data)
            {
                SetScalingParameter(rgsp);
            }
        }

        public void SetScalingParameter(RacialGenderScalingParameter rgsp)
        {
            var offset = ((int)rgsp.Race.GetBaseRace() * 10) + rgsp.Race.GetSubRaceId();
            var rsp = RawRacialData[offset];

            if(rgsp.Gender == XivGender.Male)
            {
                rsp.MaleMinSize = rgsp.MinSize;
                rsp.MaleMaxSize = rgsp.MaxSize;
                rsp.MaleMinTail = rgsp.MinTail;
                rsp.MaleMaxTail = rgsp.MaxTail;

            } else
            {
                rsp.FemaleMinSize = rgsp.MinSize;
                rsp.FemaleMaxSize = rgsp.MaxSize;
                rsp.FemaleMinTail = rgsp.MinTail;
                rsp.FemaleMaxTail = rgsp.MaxTail;

                rsp.BustMinX = rgsp.BustMinX;
                rsp.BustMinY = rgsp.BustMinY;
                rsp.BustMinZ = rgsp.BustMinZ;

                rsp.BustMaxX = rgsp.BustMaxX;
                rsp.BustMaxY = rgsp.BustMaxY;
                rsp.BustMaxZ = rgsp.BustMaxZ;

            }

        }

        public byte[] GetBytes()
        {
            var size = ColorPixels.Count * 4;
            size += RawRacialData.Count * RacialScalingParameter.TotalByteSize;
            byte[] data = new byte[size];

            var offset = 0;
            for(int i = 0; i < ColorPixels.Count; i++)
            {
                Array.Copy(ColorPixels[i], 0, data, offset, 4);
                offset += 4;
            }

            for(int i = 0; i < RawRacialData.Count; i++)
            {
                var bytes = RawRacialData[i].GetBytes();

                Array.Copy(bytes, 0, data, offset, RacialScalingParameter.TotalByteSize);
                offset += RacialScalingParameter.TotalByteSize;
            }

            return data;
        }
    }

    /// <summary>
    /// Data container representing a single subrace's information
    /// </summary>
    public class RacialScalingParameter
    {
        public const int TotalByteSize = 56;

        public float MaleMinSize;
        public float MaleMaxSize;
        public float MaleMinTail;
        public float MaleMaxTail;

        public float FemaleMinSize;
        public float FemaleMaxSize;
        public float FemaleMinTail;
        public float FemaleMaxTail;

        public float BustMinX;
        public float BustMinY;
        public float BustMinZ;

        public float BustMaxX;
        public float BustMaxY;
        public float BustMaxZ;


        public byte[] GetBytes()
        {
            List<byte> data = new List<byte>();
            data.AddRange(BitConverter.GetBytes(MaleMinSize));
            data.AddRange(BitConverter.GetBytes(MaleMaxSize));
            data.AddRange(BitConverter.GetBytes(MaleMinTail));
            data.AddRange(BitConverter.GetBytes(MaleMaxTail));

            data.AddRange(BitConverter.GetBytes(FemaleMinSize));
            data.AddRange(BitConverter.GetBytes(FemaleMaxSize));
            data.AddRange(BitConverter.GetBytes(FemaleMinTail));
            data.AddRange(BitConverter.GetBytes(FemaleMaxTail));


            data.AddRange(BitConverter.GetBytes(BustMinX));
            data.AddRange(BitConverter.GetBytes(BustMinY));
            data.AddRange(BitConverter.GetBytes(BustMinZ));

            data.AddRange(BitConverter.GetBytes(BustMaxX));
            data.AddRange(BitConverter.GetBytes(BustMaxY));
            data.AddRange(BitConverter.GetBytes(BustMaxZ));

            return data.ToArray();
        }


        public RacialScalingParameter()
        {

        }

        public RacialScalingParameter(byte[] data)
        {
            if (data.Length != TotalByteSize) throw new InvalidDataException("Invalid data length for racial scaling parameter.");

            var offset = 0;
            MaleMinSize = BitConverter.ToSingle(data, offset);
            offset += 4;
            MaleMaxSize = BitConverter.ToSingle(data, offset);
            offset += 4;

            MaleMinTail = BitConverter.ToSingle(data, offset);
            offset += 4;
            MaleMaxTail = BitConverter.ToSingle(data, offset);
            offset += 4;

            FemaleMinSize = BitConverter.ToSingle(data, offset);
            offset += 4;
            FemaleMaxSize = BitConverter.ToSingle(data, offset);
            offset += 4;

            FemaleMinTail = BitConverter.ToSingle(data, offset);
            offset += 4;
            FemaleMaxTail = BitConverter.ToSingle(data, offset);
            offset += 4;

            BustMinX = BitConverter.ToSingle(data, offset);
            offset += 4;
            BustMinY = BitConverter.ToSingle(data, offset);
            offset += 4;
            BustMinZ = BitConverter.ToSingle(data, offset);
            offset += 4;

            BustMaxX = BitConverter.ToSingle(data, offset);
            offset += 4;
            BustMaxY = BitConverter.ToSingle(data, offset);
            offset += 4;
            BustMaxZ = BitConverter.ToSingle(data, offset);
            offset += 4;
        }

    }
    
    /// <summary>
    /// Simple data holder for half of a racial scaling parameter, for easy reduction into per-gender info.
    /// </summary>
    public class RacialGenderScalingParameter
    {
        private const ushort Version = 2;
        public XivSubRace Race { get; private set; }
        public XivGender Gender { get; private set; }

        public float MinSize;
        public float MaxSize;
        public float MinTail;
        public float MaxTail;

        public float BustMinX;
        public float BustMinY;
        public float BustMinZ;

        public float BustMaxX;
        public float BustMaxY;
        public float BustMaxZ;

        public RacialGenderScalingParameter(RacialScalingParameter rsp, XivSubRace race, XivGender gender)
        {
            Race = race;
            Gender = gender;

            if (gender == XivGender.Male)
            {
                MinSize = rsp.MaleMinSize;
                MaxSize = rsp.MaleMaxSize;
                MinTail = rsp.MaleMinTail;
                MaxTail = rsp.MaleMaxTail;


            } else if(gender == XivGender.Female)
            {
                MinSize = rsp.FemaleMinSize;
                MaxSize = rsp.FemaleMaxSize;
                MinTail = rsp.FemaleMinTail;
                MaxTail = rsp.FemaleMaxTail;

                BustMinX = rsp.BustMinX;
                BustMinY = rsp.BustMinY;
                BustMinZ = rsp.BustMinZ;

                BustMaxX = rsp.BustMaxX;
                BustMaxY = rsp.BustMaxY;
                BustMaxZ = rsp.BustMaxZ;
            }

        }

        public RacialGenderScalingParameter(byte[] data)
        {
            var offset = 0;
            var byte0 = data[offset];

            ushort version = 0;
            if(byte0 != 255)
            {
                version = 1;
            } else
            {
                offset++;

                version = BitConverter.ToUInt16(data, offset);
                offset += 2;
            }

            Race = (XivSubRace) data[offset];
            offset++;
            Gender = (XivGender) data[offset];
            offset++;

            MinSize = BitConverter.ToSingle(data, offset);
            offset += 4;
            MaxSize = BitConverter.ToSingle(data, offset);
            offset += 4;

            MinTail = BitConverter.ToSingle(data, offset);
            offset += 4;
            MaxTail = BitConverter.ToSingle(data, offset);
            offset += 4;

            BustMinX = BitConverter.ToSingle(data, offset);
            offset += 4;
            BustMinY = BitConverter.ToSingle(data, offset);
            offset += 4;
            BustMinZ = BitConverter.ToSingle(data, offset);
            offset += 4;

            BustMaxX = BitConverter.ToSingle(data, offset);
            offset += 4;
            BustMaxY = BitConverter.ToSingle(data, offset);
            offset += 4;
            BustMaxZ = BitConverter.ToSingle(data, offset);
            offset += 4;
        }

        public byte[] GetBytes()
        {
            List<byte> data = new List<byte>();

            data.Add((byte)255);
            data.AddRange(BitConverter.GetBytes(Version));

            data.Add((byte)Race);
            data.Add((byte)Gender);

            data.AddRange(BitConverter.GetBytes(MinSize));
            data.AddRange(BitConverter.GetBytes(MaxSize));
            data.AddRange(BitConverter.GetBytes(MinTail));
            data.AddRange(BitConverter.GetBytes(MaxTail));

            data.AddRange(BitConverter.GetBytes(BustMinX));
            data.AddRange(BitConverter.GetBytes(BustMinY));
            data.AddRange(BitConverter.GetBytes(BustMinZ));

            data.AddRange(BitConverter.GetBytes(BustMaxX));
            data.AddRange(BitConverter.GetBytes(BustMaxY));
            data.AddRange(BitConverter.GetBytes(BustMaxZ));

            return data.ToArray();
        }
    } 
}
