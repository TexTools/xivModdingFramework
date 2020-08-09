using System.Collections.Generic;
using xivModdingFramework.Animations.Enums;

namespace xivModdingFramework.Animations.DataContainers
{
    public class PapModel
    {
        public PapHeader Header { get; set; }

        public List<PapAnimationHeader> AnimationHeaders { get; set; } = new List<PapAnimationHeader>();

        public byte[] HavokData { get; set; }

        public Dictionary<int, PapParameter> PapParameters { get; set; } = new Dictionary<int, PapParameter>();

        public class PapHeader
        {
            public short Unknown1 { get; set; }
            public short Unknown2 { get; set; }
            public short AnimationCount { get; set; }
            public short Unknown3 { get; set; }
            public short Unknown4 { get; set; }
            public short HeaderSize { get; set; }
            public short Unknown5 { get; set; }
        }

        public class PapAnimationHeader
        {
            public string AnimationName { get; set; }
            public short Unknown1 { get; set; }
            public short Unknown2 { get; set; }
            public short AnimationIndex { get; set; }
            public short Unknown3 { get; set; }
            public short Unknown4 { get; set; }
        }

        public class PapParameter
        {
            public int ParameterLength { get; set; }
            public int ParameterPropertyCount { get; set; }
            public List<PapParameterProperty> PapParameterProperties { get; set; } = new List<PapParameterProperty>();
            public List<short> PropertyIndexList { get; set; } = new List<short>();
        }

        public class PapParameterProperty
        {
            public PapPropertyType Type { get; set; }
            public int PropertyLength { get; set; }

            public PapTMDH TMDH { get; set; }

            public PapTMAL TMAL { get; set; }

            public PapTMAC TMAC { get; set; }

            public List<PapTMTR> TMTRList { get; set; } = new List<PapTMTR>();

            public PapC9 C9 { get; set; }

            public List<PapC10> C10List { get; set; } = new List<PapC10>();

            public PapC42 C42 { get; set; }
        }

        public class PapTMDH
        {
            public int Index { get; set; }
            public short FrameCount { get; set; }
            public short Unknown1 { get; set; }
        }


        public class PapTMAL
        {
            public short Unknown1 { get; set; }
            public short Unknown2 { get; set; }
        }

        public class PapTMAC
        {
            public int Index { get; set; }
            public int Unknown1 { get; set; }
            public int Unknown2 { get; set; }
            public int PapTMTRCount { get; set; }
        }

        public class PapTMTR
        {
            public int Index { get; set; }
            public List<int> AnimationIndices { get; set; } = new List<int>();
            public int AnimationCount { get; set; }
            public int Unknown1 { get; set; }
        }

        public class PapC9
        {
            public int Index { get; set; }
            public int FrameCount { get; set; }
            public int Unknown1 { get; set; }
            public string AnimationName { get; set; }
        }

        public class PapC10
        {
            public int Index { get; set; }
            /// <summary>
            /// Maybe Time or Frames?
            /// </summary>
            public short Unknown1 { get; set; }
            /// <summary>
            /// Maybe Time or Frames?
            /// </summary>
            public short Unknown2 { get; set; }
            public int Unknown3 { get; set; }
            public short Unknown4 { get; set; }
            /// <summary>
            /// Something Count?
            /// </summary>
            public short Unknown5 { get; set; }
            public int Unknown6 { get; set; }
            public int Unknown7 { get; set; }
            /// <summary>
            /// Index to something?
            /// </summary>
            public short Unknown8 { get; set; }

            public string AnimationName { get; set; }
            public short Unknown9 { get; set; }
            public int Unknown10 { get; set; }
        }

        public class PapC42
        {
            public int Index { get; set; }
            /// <summary>
            /// Maybe Time or Frames?
            /// </summary>
            public short Unknown1 { get; set; }
            /// <summary>
            /// Maybe Time or Frames?
            /// </summary>
            public short Unknown2 { get; set; }
            public short Unknown3 { get; set; }
            public int Unknown4 { get; set; }
            public int Unknown5 { get; set; }
            public int Unknown6 { get; set; }
        }
    }
}