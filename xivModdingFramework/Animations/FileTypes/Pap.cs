using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using xivModdingFramework.Animations.DataContainers;
using xivModdingFramework.Animations.Enums;
using xivModdingFramework.General.Enums;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.Animations.FileTypes
{
    public class Pap
    {
        private DirectoryInfo _gameDirectory;

        public Pap(DirectoryInfo gameDirectory, XivDataFile dataFile, XivLanguage lang)
        {
            _gameDirectory = gameDirectory;
        }

        public async void ParsePapFile(string papPath)
        {
            var dat = new Dat(_gameDirectory);

            var papData = await dat.GetType2Data(papPath, false);

            var papModel = new PapModel();

            using (var br = new BinaryReader(new MemoryStream(papData)))
            {
                
                // Header   Length: 26 bytes
                var papHeader = new PapModel.PapHeader();

                br.ReadInt32(); // Magic - always 0x70617020 (pap)
                papHeader.Unknown1 = br.ReadInt16();
                papHeader.Unknown2 = br.ReadInt16();
                papHeader.AnimationCount = br.ReadInt16();
                papHeader.Unknown3 = br.ReadInt16();
                papHeader.Unknown4 = br.ReadInt16();
                papHeader.HeaderSize = br.ReadInt16(); // probably header size, might be wrong
                papHeader.Unknown5 = br.ReadInt16();
                var havokDataOffset = br.ReadInt32();
                var parameterDataOffset = br.ReadInt32();

                papModel.Header = papHeader;

                // Animation Header     Length: 40 bytes
                for (var i = 0; i < papHeader.AnimationCount; i++)
                {
                    var animationHeader = new PapModel.PapAnimationHeader();

                    // The first 30 bytes seem to be reserved for the animation name
                    var stringBytes = br.ReadBytes(30); 

                    animationHeader.AnimationName = Encoding.ASCII.GetString(stringBytes.ToArray()).Replace("\0", "");

                    animationHeader.Unknown1 = br.ReadInt16();
                    animationHeader.Unknown2 = br.ReadInt16();
                    animationHeader.AnimationIndex = br.ReadInt16();
                    animationHeader.Unknown3 = br.ReadInt16();
                    animationHeader.Unknown4 = br.ReadInt16(); 

                    papModel.AnimationHeaders.Add(animationHeader);
                }

                // Havok Data
                var havokDataLength = parameterDataOffset - havokDataOffset;
                papModel.HavokData = br.ReadBytes(havokDataLength);

                // Parameter Data
                // There seems to be 1 set of parameter data per animation
                for (var i = 0; i < papHeader.AnimationCount; i++)
                {
                    var papParameter = new PapModel.PapParameter();

                    br.ReadInt32(); // Magic - Seems to mostly be 0x544D4C42 (TMLB)
                    papParameter.ParameterLength = br.ReadInt32(); // The length of this particular parameter
                    papParameter.ParameterPropertyCount = br.ReadInt32(); // The number of properties this parameter has

                    // Parameter Property Data
                    for (var j = 0; j < papParameter.ParameterPropertyCount; j++)
                    {
                        var papProperty = new PapModel.PapParameterProperty();

                        var prMagic = br.ReadInt32(); // Seems to differ but mostly TM** or C****
                        papProperty.PropertyLength = br.ReadInt32();  // The length of this particular property

                        switch (prMagic)
                        {
                            case (int)PapPropertyType.TMDH:
                                papProperty.TMDH = new PapModel.PapTMDH
                                {
                                    Index = br.ReadInt32(),
                                    FrameCount = br.ReadInt16(), // The number of frames in the animation
                                    Unknown1 = br.ReadInt16() // something count?
                                };
                                break;

                            case (int)PapPropertyType.TMAL:
                                br.ReadInt32(); // length to the end of parameter from this point

                                papProperty.TMAL = new PapModel.PapTMAL
                                {
                                    Unknown1 = br.ReadInt16(), // something count?
                                    Unknown2 = br.ReadInt16()
                                };
                                break;

                            case (int)PapPropertyType.TMAC:
                                papProperty.TMAC = new PapModel.PapTMAC
                                {
                                    Index = br.ReadInt32(),
                                    Unknown1 = br.ReadInt32(),
                                    Unknown2 = br.ReadInt32(),
                                };

                                br.ReadInt32(); // offset from the beginning of this data (after propertyLength)
                                papProperty.TMAC.PapTMTRCount = br.ReadInt32();
                                break;

                            case (int)PapPropertyType.TMTR:
                                var tmtr = new PapModel.PapTMTR
                                {
                                    Index = br.ReadInt32(),
                                };
                                var animIndexOffset = br.ReadInt32(); // Offset to index of animation (C###) to be used, from the beginning of this data (after propertyLength)

                                tmtr.AnimationCount = br.ReadInt32(); // Number of animations to be used (indexes to get/read (int16) once in above offset)
                                tmtr.Unknown1 = br.ReadInt32();

                                // Save position
                                var tmtrSavedPos = br.BaseStream.Position;

                                // Get the animation indices
                                br.BaseStream.Seek(animIndexOffset - 16, SeekOrigin.Current);
                                for (var k = 0; k < tmtr.AnimationCount; k++)
                                {
                                    tmtr.AnimationIndices.Add(br.ReadInt16());
                                }

                                // Go back to saved position
                                br.BaseStream.Seek(tmtrSavedPos, SeekOrigin.Begin);

                                papProperty.TMTRList.Add(tmtr);
                                break;

                            case (int)PapPropertyType.C009: // This seems to be the animation start or parent animation
                                papProperty.C9 = new PapModel.PapC9
                                {
                                    Index = br.ReadInt32(),
                                    FrameCount = br.ReadInt32(), // seems to match TMDH frame count (can probably be different for combined animations)
                                    Unknown1 = br.ReadInt32(),

                                };

                                var c9OffsetToAnim = br.ReadInt32(); // Offset to the animation name from the beginning of this data (after propertyLength)

                                // Save position
                                var c9SavedPos = br.BaseStream.Position;

                                // Get the animation names
                                br.BaseStream.Seek(c9OffsetToAnim - 16, SeekOrigin.Current);

                                papProperty.C9.AnimationName = GetNameOfUnknownLength(br);

                                // Go back to saved position
                                br.BaseStream.Seek(c9SavedPos, SeekOrigin.Begin);
                                break;

                            case (int)PapPropertyType.C010: // This is probably facial animations, or animations in general maybe?
                                var c10 = new PapModel.PapC10
                                {
                                    Index = br.ReadInt16(),
                                    Unknown1 = br.ReadInt16(), // maybe time or frames
                                    Unknown2 = br.ReadInt16(), // maybe time or frames
                                    Unknown3 = br.ReadInt32(),
                                    Unknown4 = br.ReadInt16(),
                                    Unknown5 = br.ReadInt16(), // something count?
                                    Unknown6 = br.ReadInt32(),
                                    Unknown7 = br.ReadInt32(),
                                    Unknown8 = br.ReadInt16() // index to something?
                                };

                                var c10OffsetToAnim = br.ReadInt16(); // Offset to the animation name from the beginning of this data (after propertyLength)

                                // Save Position
                                var c10SavedPos = br.BaseStream.Position;

                                // Get the animation names
                                br.BaseStream.Seek(c10OffsetToAnim - 26, SeekOrigin.Current);

                                c10.AnimationName = GetNameOfUnknownLength(br);

                                // Go back to saved position
                                br.BaseStream.Seek(c10SavedPos, SeekOrigin.Begin);

                                c10.Unknown9 = br.ReadInt16();
                                c10.Unknown10 = br.ReadInt32();

                                papProperty.C10List.Add(c10);
                                break;

                            case (int)PapPropertyType.C042: // Animation End?
                                papProperty.C42 = new PapModel.PapC42
                                {
                                    Index = br.ReadInt16(),
                                    Unknown1 = br.ReadInt16(), // maybe time or frames
                                    Unknown2 = br.ReadInt16(), // maybe time or frames
                                    Unknown3 = br.ReadInt16(),
                                    Unknown4 = br.ReadInt16(),
                                    Unknown5 = br.ReadInt16(),
                                    Unknown6 = br.ReadInt16()
                                };
                                break;
                        }

                        papParameter.PapParameterProperties.Add(papProperty);
                    }

                    for (var j = 0; j < papParameter.ParameterPropertyCount - 2; j++)
                    {
                        papParameter.PropertyIndexList.Add(br.ReadInt16());
                    }

                    papModel.PapParameters.Add(i, papParameter);
                }
            }
        }


        private string GetNameOfUnknownLength(BinaryReader br)
        {
            byte n;
            var name = new List<byte>();
            while ((n = br.ReadByte()) != 0)
            {
                name.Add(n);
            }

            return Encoding.ASCII.GetString(name.ToArray()).Replace("\0", "");
        }
    }
}