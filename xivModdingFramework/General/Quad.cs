using System;
using System.Linq;

namespace xivModdingFramework.General
{
    [Serializable]
    public struct Quad
    {
        public short[] Values;

        public Quad(long data)
        {
            Values = new short[4];
            Values[0] = (short)data;
            Values[1] = (short)(data >> 16);
            Values[2] = (short)(data >> 32);
            Values[3] = (short)(data >> 48);
        }

        public static Quad Read(byte[] buffer, int offset)
        {
            var data = BitConverter.ToInt64(buffer, offset);
            return new Quad(data);
        }
        public static Quad ReadBE(byte[] buffer, int offset)
        {
            // Assume endianness is reversed.
            var data = BitConverter.ToInt64(buffer.Reverse().ToArray(), offset);
            return new Quad(data);
        }
    }
}
