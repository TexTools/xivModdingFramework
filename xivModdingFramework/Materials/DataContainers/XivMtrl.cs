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
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Materials.FileTypes;
using xivModdingFramework.Mods;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.Enums;
using xivModdingFramework.Textures.FileTypes;
using static xivModdingFramework.Materials.DataContainers.ShaderHelpers;

namespace xivModdingFramework.Materials.DataContainers
{

    [Flags]
    public enum EMaterialFlags1 : ushort
    {
        ShowBackfaces = 0x01,
        Unknown0002 = 0x02,
        Unknown0004 = 0x04,
        Unknown0008 = 0x08,
        EnableTranslucency = 0x10,
        Unknown0020 = 0x020,
        Unknown0040 = 0x040,
        Unknown0080 = 0x080,
        Unknown0100 = 0x100,
        Unknown0200 = 0x200,
        Unknown0400 = 0x400,
        Unknown0800 = 0x800,
        Unknown1000 = 0x1000,
        Unknown2000 = 0x1000,
        Unknown4000 = 0x1000,
        Unknown8000 = 0x1000,
    };
    [Flags]
    public enum EMaterialFlags2 : ushort
    {
        Unknown0001 = 0x01,
        Unknown0002 = 0x02,
        Unknown0004 = 0x04,
        Unknown0008 = 0x08,
        Unknown0010 = 0x10,
        Unknown0020 = 0x20,
        Unknown0040 = 0x40,
        Unknown0080 = 0x80,
        Unknown0100 = 0x100,
        Unknown0200 = 0x200,
        Unknown0400 = 0x400,
        Unknown0800 = 0x800,
        Unknown1000 = 0x1000,
        Unknown2000 = 0x1000,
        Unknown4000 = 0x1000,
        Unknown8000 = 0x1000,
    };

    /// <summary>
    /// This class holds the information for an MTRL file
    /// </summary>
    public class XivMtrl : ICloneable
    {
        public const string ItemPathToken = "{item_folder}";
        public const string VariantToken = "{variant}";
        public const string TextureNameToken = "{default_name}";
        public const string CommonPathToken = "{shared_folder}";

        /// <summary>
        /// The MTRL file signature
        /// </summary>
        /// <remarks>
        /// 0x00000301 (16973824)
        /// </remarks>
        public int Signature { get; set; } = 0x00000301;


        /// <summary>
        /// The size of the ColorSet Data section
        /// </summary>
        /// <remarks>
        /// Can be 0 if there is no ColorSet Data
        /// </remarks>
        public ushort ColorSetDataSize { get {
            var size = ColorSetData.Count * 2;
            size += ColorSetDyeData == null ? 0 : ColorSetDyeData.Length;
            return (ushort) size;
        } }

        public List<MtrlString> MapStrings { get; set; } = new List<MtrlString>();

        public List<MtrlString> ColorsetStrings { get; set; } = new List<MtrlString>();

        public EShaderPack ShaderPack
        {
            get
            {
                return ShaderHelpers.GetShpkFromString(ShaderPackRaw);
            }
            set
            {
                ShaderPackRaw = ShaderHelpers.GetEnumDescription(value);
            }
        }

        /// <summary>
        /// The name of the shader used by the item
        /// </summary>
        public string ShaderPackRaw { get; set; } = "character.shpk";

        /// <summary>
        /// Unknown value
        /// </summary>
        public byte[] AdditionalData { get; set; } = new byte[4];

#if DAWNTRAIL
        /// <summary>
        /// The list of half floats containing the ColorSet data
        /// </summary>
        public List<Half> ColorSetData { get; set; } = new List<Half>(new Half[1024]);
        /// <summary>
        /// The byte array containing the extra ColorSet data
        /// </summary>
        public byte[] ColorSetDyeData { get; set; } = new byte[128];

#else
        /// <summary>
        /// The list of half floats containing the ColorSet data
        /// </summary>
        public List<Half> ColorSetData { get; set; } = new List<Half>(new Half[256]);
        /// <summary>
        /// The byte array containing the extra ColorSet data
        /// </summary>
        public byte[] ColorSetDyeData { get; set; } = new byte[32];

#endif

        /// <summary>
        /// The size of the additional MTRL Data
        /// </summary>
        public ushort ShaderConstantsDataSize { 
            get {
                var size = 0;
                ShaderConstants.ForEach(x => {
                    size += x.Values.Count * 4;
                }
                );

                return (ushort)size;
            }
            set
            {
                //No-Op
                throw new Exception("Attempted to directly set AdditionalDataSize");
            } 
        }

        /// <summary>
        /// The number of type 1 data sturctures 
        /// </summary>
        public ushort ShaderKeyCount { get { return (ushort)ShaderKeys.Count; } set
            {
                //No-Op
                throw new Exception("Attempted to directly set TextureUsageCount");
            }
        }

        /// <summary>
        /// The number of type 2 data structures
        /// </summary>
        public ushort ShaderConstantsCount
        {
            get { return (ushort)ShaderConstants.Count; }
            set
            {
                //No-Op
                throw new Exception("Attempted to directly set ShaderParameterCount");
            }
        }

        public List<MtrlTexture> Textures = new List<MtrlTexture>();

        public EMaterialFlags1 MaterialFlags { get; set; }


        /// <summary>
        /// Unknown Value
        /// </summary>
        public EMaterialFlags2 MaterialFlags2 { get; set; }

        /// <summary>
        /// The list of Type 1 data structures
        /// </summary>
        public List<ShaderKey> ShaderKeys { get; set; } = new List<ShaderKey>();

        /// <summary>
        /// The list of Type 2 data structures
        /// </summary>
        public List<ShaderConstant> ShaderConstants { get; set; } = new List<ShaderConstant>();

        /// <summary>
        /// The internal MTRL path
        /// </summary>
        public string MTRLPath { get; set; }

        public string GetSuffix()
        {
            return IOUtil.GetMaterialSuffix(MTRLPath);
        }


        /// <summary>
        /// Gets a given shader Parameter by ID.
        /// </summary>
        /// <param name="parameterId"></param>
        /// <returns></returns>
        private ShaderConstant GetShaderConstant(uint parameterId, bool clone = true)
        {
            var val = ShaderConstants.FirstOrDefault(x => x.ConstantId == parameterId);
            if(val != null && clone)
            {
                val = (ShaderConstant) val.Clone();
            }
            return val;
        }

        /// <summary>
        /// Sets or adds a given shader parameter.
        /// </summary>
        /// <param name="parameterId"></param>
        /// <param name="data"></param>
        private void SetShaderConstant(uint parameterId, List<float> data = null)
        {
            var value = ShaderConstants.FirstOrDefault(x => x.ConstantId == parameterId);

            if (value != null && data != null)
            {
                // Altering Existing Constant Value
                value.Values = data;
            } else if(value != null && data == null)
            {
                // Removing Constant
                ShaderConstants.Remove(value);
            }
            else if(value == null && data != null)
            {
                //Adding a constant
                var c = new ShaderConstant() { ConstantId = parameterId, Values = data };
                ShaderConstants.Add(c);
            }
        }

        /// <summary>
        /// Get the 'TexTypePath' List that TT expects to populate the texture listing.
        /// Generated via the actual sampler and shader key settings.
        /// </summary>
        /// <returns></returns>
        public List<TexTypePath> GetTextureTypePathList()
        {
            var list = new List<TexTypePath>();
            foreach(var tex in Textures)
            {
                var ttp = new TexTypePath()
                {
                    DataFile = IOUtil.GetDataFileFromPath(tex.TexturePath),
                    Path = tex.Dx11Path,
                    Type = ResolveFullUsage(tex),
                    Name = Path.GetFileNameWithoutExtension(tex.TexturePath)
                };
                list.Add(ttp);
            }
            // Include the colorset as its own texture if we have one.
            if (ColorsetStrings.Count > 0 && ColorSetData.Count > 0)
            {
                var ttp = new TexTypePath
                {
                    Path = MTRLPath,
                    Type = XivTexType.ColorSet,
                    DataFile = GetDataFile(),
                    Name = Path.GetFileNameWithoutExtension(MTRLPath)
                };
                list.Add(ttp);
            }
            return list;
        }

        /// <summary>
        /// Retrieves the Data File this MTRL resides in based on the path of the MTRL file.
        /// </summary>
        /// <returns></returns>
        public XivDataFile GetDataFile()
        {
            return Helpers.IOUtil.GetDataFileFromPath(MTRLPath);
        }


        /// <summary>
        /// Simplified one-line accessor for ease of use.
        /// </summary>
        /// <param name="usage"></param>
        /// <returns></returns>
        public MtrlTexture GetTexture(XivTexType usage, bool clone = false)
        {

            var val = Textures.FirstOrDefault(x => ResolveFullUsage(x) == usage);
            if(val != null && clone)
            {
                val = (MtrlTexture)val.Clone();
            }
            return val;
        }

        /// <summary>
        /// Performs a better usage resolve than just MtrlTexture.Usage, properly accounting for shader keys.
        /// </summary>
        /// <param name="tex"></param>
        /// <returns></returns>
        public XivTexType ResolveFullUsage(MtrlTexture tex)
        {
            if(tex.Sampler == null)
            {
                return XivTexType.Other;
            }
            return ShaderHelpers.SamplerIdToTexUsage(tex.Sampler.SamplerId, this);
        }

        /// <summary>
        /// Retreives the base texture directory this material references.
        /// </summary>
        /// <returns></returns>
        public string GetTextureRootDirectoy()
        {
            string root = "";
            if(MTRLPath == null)
            {
                return "";
            }
            // Attempt to logically deduce base texture path.
            if (MTRLPath.Contains("material/"))
            {
                var match = Regex.Match(MTRLPath, "(.*)material/");
                if(match.Success)
                {
                    root = match.Groups[1].Value + "texture";
                }
            } else
            {
                // Default to common texture root.
                root = GetCommonTextureDirectory();
            }

            return root;
        }

        /// <summary>
        /// Gets the item version this MTRL is attached to, based on directory.
        /// </summary>
        /// <returns></returns>
        public uint GetVersion()
        {
            var match = Regex.Match(MTRLPath, "/v([0-9]{4})/");
            if (match.Success)
            {
                return (uint)Int32.Parse(match.Groups[1].Value);
            }
            return 0;
        }

        /// <summary>
        /// Gets the variant string based on the item version. Ex. 'v03_'
        /// </summary>
        /// <returns></returns>
        public string GetVariantString()
        {
            var version = GetVersion();

            var versionString = "";
            if (version > 0)
            {
                versionString += 'v' + (version.ToString().PadLeft(2, '0'));
            }
            return versionString;
        }

        /// <summary>
        /// Retrieves the /STATED/ slot for this MTRL, based on its name.
        /// This is not necessarily the slot it is actually used in, or a slot where a model exists at all.
        /// To get the real slot, resolve the MTRL's root and check that.
        /// </summary>
        /// <returns></returns>
        public string GetFakeSlot()
        {
            return IOUtil.GetMaterialSlot(MTRLPath);
        }

        /// <summary>
        /// Gets the default texture name for this material for a given texture type.
        /// </summary>
        /// <returns></returns>
        public string GetDefaultTexureName(XivTexType texType, bool includeVariant = true)
        {

            var ret = "";

            // When available, version number prefixes the texture name.
            if (includeVariant && (MTRLPath.StartsWith("chara/equipment/") || MTRLPath.StartsWith("chara/accessory/")))
            {
                ret += GetVariantString() + "_";
            }

            // Followed by the character and secondary identifier.
            var match = Regex.Match(MTRLPath, "c[0-9]{4}[a-z][0-9]{4}");
            if (match.Success)
            {
                ret += match.Value;
            }
            else {
                ret += "unknown";
            }

            ret += GetItemTypeIdentifier();

            // Followed by the material identifier, if above [a].
            var identifier = GetMaterialIdentifier();
            if(identifier != "a")
            {
                ret += "_" + identifier;
            }

            

            if(texType == XivTexType.Normal)
            {
                ret += MTRLPath.Contains("chara/human/c") ? "_n" : "_norm";
            } else if(texType == XivTexType.Specular)
            {
                ret += MTRLPath.Contains("chara/human/c") ? "_s" : "_spec";
            }
            else if (texType == XivTexType.Mask)
            {
                // Not a Typo.  SE is dumb.
                ret += MTRLPath.Contains("chara/human/c") ? "_s" : "_mask";
            }
            else if (texType == XivTexType.Diffuse)
            {
                ret += MTRLPath.Contains("chara/human/c") ? "_d" : "_diff";
            }
            else if (texType == XivTexType.Index)
            {
                ret += MTRLPath.Contains("chara/human/c") ? "_id" : "_id";
            }
            else
            {
                ret += "_o";
            }

            ret += ".tex";
            return ret;
        }

        /// <summary>
        /// Gets the Material/Part identifier letter based on the file path.
        /// </summary>
        /// <returns></returns>
        public string GetMaterialIdentifier()
        {
            var match = Regex.Match(MTRLPath, "_([a-z0-9]+)\\.mtrl");
            if(match.Success)
            {
                return match.Groups[1].Value;
            }
            return "a";
        }

        public string GetItemTypeIdentifier()
        {
            // This regex feels a little janky, but it's good enough for now.
            var match = Regex.Match(MTRLPath, "_([a-z]{3})_[a-z0-9]\\.mtrl");
            if (match.Success)
            {
                return "_" + match.Groups[1].Value;
            } else if (MTRLPath.Contains("/obj/tail/t")) {
                // Tails have their textures (but not materials) listed as _etc parts.
                return "_etc";
            }
            return "";
        }

        public XivRace GetRace()
        {
            var races= Enum.GetValues(typeof(XivRace)).Cast<XivRace>();
            foreach(var race in races)
            {
                // Test the root path for the racial identifier.
                var match = Regex.Match(MTRLPath, "c" + race.GetRaceCode());
                if(match.Success)
                {
                    return race;
                }
            }
            return XivRace.All_Races;
        }


        /// <summary>
        /// Get the root shared common texture directory for FFXIV.
        /// </summary>
        /// <returns></returns>
        public static string GetCommonTextureDirectory()
        {
            return "chara/common/texture";
        }
        
        /// <summary>
        /// Tokenize a given path string using this Material's settings to create and resolve tokens.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="usage"></param>
        /// <returns></returns>
        public string TokenizePath(string path, XivTexType usage)
        {
            path = path.Replace(GetTextureRootDirectoy(), XivMtrl.ItemPathToken);

            var commonPath = XivMtrl.GetCommonTextureDirectory();
            path = path.Replace(commonPath, XivMtrl.CommonPathToken);


            var version = GetVariantString();
            if (version != "")
            {
                path = path.Replace(version, XivMtrl.VariantToken);
            }

            //var texName = GetDefaultTexureName(usage, false);
            //path = path.Replace(texName, XivMtrl.TextureNameToken);
            return path;
        }

        /// <summary>
        /// Detokenize a given path string using this Material's settings to create and resolve tokens.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="usage"></param>
        /// <returns></returns>
        public string DetokenizePath(string path, MtrlTexture texture)
        {
            var rootPath = GetTextureRootDirectoy();

            // No path, assign it by default.
            if (path == "")
            {
                path = rootPath + "/" + GetDefaultTexureName(ResolveFullUsage(texture));
                return path;
            }
            var variantString = GetVariantString();


            path = path.Replace(ItemPathToken, rootPath);
            path = path.Replace(VariantToken, variantString);
            path = path.Replace(CommonPathToken, GetCommonTextureDirectory());
            return path;
        }


        public object Clone()
        {
            var clone = (XivMtrl) MemberwiseClone();

            clone.Textures = new List<MtrlTexture>();
            for (int i = 0; i < Textures.Count; i++)
            {
                clone.Textures.Add((MtrlTexture)Textures[i].Clone());
            }

            clone.ShaderConstants= new List<ShaderConstant>();
            for (int i = 0; i < ShaderConstants.Count; i++)
            {
                clone.ShaderConstants.Add((ShaderConstant)ShaderConstants[i].Clone());
            }

            clone.ShaderKeys = new List<ShaderKey>();
            for (int i = 0; i < ShaderKeys.Count; i++)
            {
                clone.ShaderKeys.Add((ShaderKey)ShaderKeys[i].Clone());
            }

            if (AdditionalData != null)
            {
                clone.AdditionalData = (byte[])AdditionalData.Clone();
            }

            if (ColorSetData != null)
            {
                clone.ColorSetData = ColorSetData.ToList();
            }
            if (ColorSetDyeData != null)
            {
                clone.ColorSetDyeData = (byte[])ColorSetDyeData.Clone();
            }


            return clone;
        }

    }

    public class MtrlString : ICloneable
    {
        public string Value;
        public ushort Flags;

        public object Clone()
        {
            return MemberwiseClone();
        }
    }

    /// <summary>
    /// Class representing the sum of information used as a texture reference in a material.
    /// </summary>
    public class MtrlTexture : ICloneable
    {
        public string TexturePath { get; set; }

        public ushort Flags { get; set; }

        public TextureSampler Sampler { get; set; }

        // Shortcut accessor for the texture data.
        public async Task<XivTex> GetTexData(bool forceOriginal = false, ModTransaction tx = null)
        {
            return await Tex.GetXivTex(this, forceOriginal, tx);
        }

        public MtrlTexture()
        {
            TexturePath = "";
            Sampler = new TextureSampler();
        }

        public string Dx11Path
        {
            get
            {
                var texpath = TexturePath;
                if((Flags & 0x8000) != 0)
                {
                    var path = Path.GetDirectoryName(TexturePath).Replace("\\","/");
                    var file = Path.GetFileName(TexturePath);
                    texpath = path + "/--" + file;
                }
                return texpath;
            }
        }

        public string Dx9Path
        {
            get
            {
                if ((Flags & 0x8000) != 0)
                {
                    return TexturePath;
                }
                return null;
            }
        }

        /// <summary>
        /// Retrieves this texture's usage type based on the sampler id.
        /// </summary>
        public XivTexType Usage
        {
            get
            {
                if(Sampler == null)
                {
                    return XivTexType.Other;
                }
                return Sampler.TexType;
            }
        }

        public object Clone()
        {
            var clone = (MtrlTexture) MemberwiseClone();
            clone.Sampler = (TextureSampler) Sampler.Clone();
            return clone;
        }
    }

    /// <summary>
    /// This class contains the information for the shader keys.
    /// Primarily these enable/disable shader functionality at large.
    /// </summary>
    public class ShaderKey : ICloneable
    {
        public uint KeyId; // Mappings for this TextureType value to XivTexType value are available in Mtrl.cs

        public uint Value;

        public ShaderKeyInfo? GetKeyInfo(EShaderPack shpk)
        {
            if (!ShaderHelpers.ShaderKeys[shpk].ContainsKey(KeyId))
            {
                return null;
            }

            return ShaderKeys[shpk][KeyId];
        }

        public object Clone()
        {
            return MemberwiseClone();
        }
    }

    /// <summary>
    /// This class contains the information for the shader parameters at the end the MTRL File
    /// </summary>
    public class ShaderConstant : ICloneable
    {
        public uint ConstantId;

        // The actual data (after extraction).
        public List<float> Values;

        public ShaderConstantInfo? GetConstantInfo(EShaderPack shpk)
        {
            if (!ShaderHelpers.ShaderConstants[shpk].ContainsKey(ConstantId))
            {
                return null;
            }

            return ShaderConstants[shpk][ConstantId];
        }


        public object Clone()
        {
            var clone = (ShaderConstant)MemberwiseClone();
            clone.Values = Values.ToList();
            return clone;
        }
    }

    /// <summary>
    /// This class contains the information for MTRL texture samplers.
    /// These determine how each texture is used.
    /// </summary>
    public class TextureSampler : ICloneable
    {
        /// <summary>
        /// 2-Bit UV Tiling Mode Identifer.
        /// </summary>
        public enum ETilingMode
        {
            Wrap,
            Mirror,
            Clamp,
            Border
        };

        /// <summary>
        /// The core Texture Sampler this object refers to.
        /// </summary>
        public ESamplerId SamplerId
        {
            get
            {
                if (Enum.IsDefined(typeof(ESamplerId), SamplerIdRaw))
                {
                    return (ESamplerId)SamplerIdRaw;
                }
                return ESamplerId.Invalid;
            }
            set
            {
                SamplerIdRaw = (uint)value;
            }
        }

        /// <summary>
        /// The rough 'TexTools' style human-readable approximate texture usage of this texture.
        /// Not 1:1 with SamplerID, since multiple sampler IDs can feed into single TexTools categories.
        /// </summary>
        public XivTexType TexType
        {
            get {
                return SamplerIdToTexUsage(SamplerId);
            }
        }

        /// <summary>
        /// U Direction UV Tiling Mode.
        /// </summary>
        public ETilingMode UTilingMode {
            get
            {
                uint shifted = (SamplerSettingsRaw >> 2);
                uint mode = (shifted & ((uint)0x3));
                return (ETilingMode)mode;
            }
            set
            {
                unchecked
                {
                    SamplerSettingsRaw &= ~((uint)3 << 2);
                    SamplerSettingsRaw |= ((uint)value << 2);
                }
            }
        }

        /// <summary>
        /// V Direction UV Tiling Mode.
        /// </summary>
        public ETilingMode VTilingMode
        {
            get
            {
                var mode = (((byte)SamplerSettingsRaw)) & 0x3;
                return (ETilingMode)mode;
            }
            set
            {

                unchecked
                {
                    SamplerSettingsRaw &= ~((uint)3);
                    SamplerSettingsRaw |= ((uint)value);
                }
            }
        }

        /// <summary>
        /// Minimum MipMap to use.
        /// Typically this should just be 0, but it's an option if you want blurry textures I guess.
        /// </summary>
        public byte MinimumLoDLevel
        {
            get
            {
                var val = SamplerSettingsRaw >> 20;
                val &= 0xF;
                return (byte)val;
            }
            set
            {
                if(value > 15)
                {
                    value = 15;
                }

                unchecked
                {
                    SamplerSettingsRaw &= ~((uint)0xF << 20);
                    SamplerSettingsRaw |= (((uint)value) << 20);
                }
            }
        }

        /// <summary>
        /// Bits 4-10 of the Sampler Settings
        /// Unknown Usage, but sometimes have actual values.
        /// </summary>
        public byte SamplerSettingsLowUnknown
        {
            get
            {
                var val = (((byte)SamplerSettingsRaw) >> 4) & 0x3f;
                return (byte)val;
            }
            set
            {
                unchecked
                {
                    SamplerSettingsRaw &= ~((uint)0x3f << 4);
                    SamplerSettingsRaw |= ((uint)value << 4);
                }
            }
        }

        /// <summary>
        /// Bytes 24-31 of the Sampler Settings
        /// </summary>
        public byte SamplerSettingsHighByte
        {
            get
            {
                var val = SamplerSettingsRaw >> 24;
                return (byte)val;
            }
            set
            {
                unchecked
                {
                    SamplerSettingsRaw &= ~((uint)0xFF << 24);
                    SamplerSettingsRaw |= ((uint)value << 24);
                }
            }
        }

        /// <summary>
        /// LoD Bias.  Lower will make a texture sharper (use higher MipMap levels), but can cause flickering/shimmering.
        /// Higher will make a texture blurrier, but reduce flickering/shimmering.
        /// This is only used with respect to Bilinear/Trilinear filtering, so it's mostly moot on anyone with modern hardware,
        /// since Anisotropic filtering exists.
        /// 
        /// This has a range of -8 to ~+8 since it's a 9 bit value / 64 and signed (And there's 16 MipMap levels.  What a twist.)
        /// </summary>
        public float LoDBias
        {
            get
            {
                // 10 bit signed int.  We need to bit shift off the sign first.
                uint unsigned = (SamplerSettingsRaw & (0x1ff << 10)) >> 10;

                // Then get the sign
                uint signVal = (SamplerSettingsRaw >> 19) & 1;

                int res = 0;
                if(signVal > 0)
                {
                    // If it's negative, we need to interpret it as two's complement.
                    res = (int) (0xFFFFFe00 | unsigned);
                } else
                {
                    // Positive is fine as is.
                    res = (int) unsigned;
                }

                // Then 64 divisor and apply sign.
                var val = res / 64.0f;
                return val;
            }
            set
            {
                // Clamp
                var val = Math.Min(Math.Max(value, -8.0f), 7.984375f);

                // Split into sign and integer body.
                uint x64 = (uint) (val * 64.0f);
                

                // Limit to 9 bits and sign.
                var limited = ((uint)x64 & 0x1FF);
                var shiftedSign = (((uint)x64) >> 31) << 9;
                var combined = limited | shiftedSign;
                unchecked
                {
                    SamplerSettingsRaw &= ~((uint)0x3FF << 10);
                    SamplerSettingsRaw |= ((uint)combined << 10);
                }
            }
        }

        /// <summary>
        /// CRC Identifier for a Texture Sampler.
        /// As of DT Benchmark release, these values and their
        /// string names are all currently known.
        /// </summary>
        public uint SamplerIdRaw;

        // Bitwise field of sampler settings
        // Low 4 bits are V and U tiling mode, 2 each 
        //      wrap, mirror, clamp, border in order.
        // 6 Bytes unknown(?)
        // 10 bits LoD Bias
        // 4 Bits for Minimum LoD level
        // 8 Bits Unknown
        public uint SamplerSettingsRaw;
        public object Clone()
        {
            return MemberwiseClone();
        }
    }


}