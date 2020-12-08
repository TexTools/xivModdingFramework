using System;
using System.Collections.Generic;
using System.Text;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;

namespace xivModdingFramework.Models.DataContainers
{
    /// <summary>
    /// Class Representing a Extra Skeletons Table Entry for a Equipment Set.
    /// </summary>
    public class ExtraSkeletonEntry
    {

        public ExtraSkeletonEntry(XivRace race, ushort setId)
        {
            Race = race;
            SetId = setId;
            SkelId = 0;
        }
        public ExtraSkeletonEntry(XivRace race, ushort setId, ushort skelId)
        {
            SetId = setId;
            Race = race;
            SkelId = skelId;
        }

        public static void Write(byte[] data, ExtraSkeletonEntry entry, int count, int index)
        {
            int offset = (int)(4 + (index * 4));
            short raceId = Int16.Parse(XivRaces.GetRaceCode(entry.Race));
            IOUtil.ReplaceBytesAt(data, BitConverter.GetBytes(entry.SetId), offset);
            IOUtil.ReplaceBytesAt(data, BitConverter.GetBytes(raceId), offset + 2);

            var baseOffset = 4 + (count * 4);
            offset = (int)(baseOffset + (index * 2));
            IOUtil.ReplaceBytesAt(data, BitConverter.GetBytes(entry.SkelId), offset);
        }
        public static ExtraSkeletonEntry Read(byte[] data, uint count, uint index)
        {

            int offset = (int)(4 + (index * 4));

            var setId = BitConverter.ToUInt16(data, offset);
            var raceId = BitConverter.ToUInt16(data, offset + 2);
            var race = XivRaces.GetXivRace(raceId.ToString().PadLeft(4, '0'));


            var baseOffset = 4 + (count * 4);
            offset = (int)(baseOffset + (index * 2));

            var skelId = BitConverter.ToUInt16(data, offset);

            var ret = new ExtraSkeletonEntry(race, setId, skelId);
            return ret;
        }

        public ushort SetId;
        public XivRace Race;
        public ushort SkelId;




    }
}
