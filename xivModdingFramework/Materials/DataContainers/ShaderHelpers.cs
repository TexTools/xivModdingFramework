using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Textures.Enums;
using static xivModdingFramework.Cache.XivCache;

namespace xivModdingFramework.Materials.DataContainers
{
    public static class ShaderHelpers
    {
        const string _Default = "Default";

        /// <summary>
        /// Simple encapsulating struct for sanity.
        /// </summary>
        public struct ShaderConstantInfo
        {
            public string Name;
            public Dictionary<string, List<float>> KnownValues;
            public uint Id;

            public List<float> DefaultValues
            {
                get
                {
                    if (KnownValues.ContainsKey(_Default))
                    {
                        return KnownValues[_Default];
                    }
                    return new List<float>() { 0.0f };
                }
            }

            public ShaderConstantInfo(uint key, string name, List<float> defaultValue)
            {
                Id = key;
                Name = name;
                KnownValues = new Dictionary<string, List<float>>()
                {
                    { _Default, defaultValue }
                };
            }

            /// <summary>
            /// Returns a slightly prettier UI friendly version of the name that also includes the key.
            /// </summary>
            public string UIName
            {
                get
                {
                    if (String.IsNullOrWhiteSpace(Name))
                    {
                        return Id.ToString("X8");
                    }

                    return Id.ToString("X8") + " - " + Name;
                }
            }
        }

        /// <summary>
        /// Simple encapsulating struct for sanity.
        /// </summary>
        public struct ShaderKeyInfo
        {
            public uint Key;
            public string Name;
            public Dictionary<uint, string> KnownValues;
            public uint DefaultValue;

            public ShaderKeyInfo(uint key, string name, Dictionary<uint, string> values, uint defaultValue)
            {
                Key = key;
                if(values == null || values.Count == 0)
                {
                    values = new Dictionary<uint, string>();
                }
                Name = name;
                KnownValues = values;
                DefaultValue = defaultValue;
            }


            /// <summary>
            /// Returns a slightly prettier UI friendly version of the name that also includes the key.
            /// </summary>
            public string UIName
            {
                get
                {
                    if(String.IsNullOrWhiteSpace(Name))
                    {
                        return Key.ToString("X8");
                    }

                    return Key.ToString("X8") + " - " + Name;
                }
            }
        }


        /// <summary>
        /// Dictionary of known Shader Constants by ShaderPack
        /// </summary>
        public static Dictionary<EShaderPack, Dictionary<uint, ShaderConstantInfo>> ShaderConstants;

        /// <summary>
        /// Dictionary of known Shader Keys by ShaderPack
        /// </summary>
        public static Dictionary<EShaderPack, Dictionary<uint, ShaderKeyInfo>> ShaderKeys;

        // Load our Shader Constants and Shader Keys from JSON.
        static ShaderHelpers()
        {
            // Kick this off asynchronously so we don't block.
            Task.Run(LoadShaderInfo);
        }

        public static bool UsesColorset(this EShaderPack shpk)
        {
            switch (shpk)
            {
                case EShaderPack.Character:
                case EShaderPack.CharacterLegacy:
                case EShaderPack.BgColorChange:
                    return true;
                default:
                    return false;
            }
        }


        /// <summary>
        /// Asynchronously (re)loads all shader reference info from the Shader DB.
        /// </summary>
        /// <returns></returns>
        public static async Task LoadShaderInfo()
        {
            await Task.Run(() =>
            {
                ShaderConstants = new Dictionary<EShaderPack, Dictionary<uint, ShaderConstantInfo>>();
                ShaderKeys = new Dictionary<EShaderPack, Dictionary<uint, ShaderKeyInfo>>();

                foreach (EShaderPack shpk in Enum.GetValues(typeof(EShaderPack)))
                {
                    if (!ShaderConstants.ContainsKey(shpk))
                    {
                        ShaderConstants.Add(shpk, new Dictionary<uint, ShaderConstantInfo>());
                    }
                }

                foreach (EShaderPack shpk in Enum.GetValues(typeof(EShaderPack)))
                {
                    if (!ShaderKeys.ContainsKey(shpk))
                    {
                        ShaderKeys.Add(shpk, new Dictionary<uint, ShaderKeyInfo>());
                    }
                }

                try
                {
                    const string _dbPath = "./Resources/DB/shader_info.db";
                    var connectionString = "Data Source=" + _dbPath + ";Pooling=False;";

                    // Spawn a DB connection to do the raw queries.
                    using (var db = new SQLiteConnection(connectionString))
                    {
                        db.Open();
                        // Using statements help ensure we don't accidentally leave any connections open and lock the file handle.

                        // Create Default value entries first.
                        var query = "select * from view_shader_key_defaults;";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            using (var reader = new CacheReader(cmd.ExecuteReader()))
                            {
                                while (reader.NextRow())
                                {

                                    var shpk = GetShpkFromString(reader.GetString("shader_pack"));
                                    var key = reader.GetInt64("key_id");
                                    var def = reader.GetInt64("value");
                                    var name = reader.GetString("name");

                                    var info = new ShaderKeyInfo((uint)key, name, null, (uint)def);
                                    ShaderKeys[shpk].Add((uint)key, info);
                                }
                            }
                        }

                        // Then load full corpus of possible values..
                        query = "select * from view_shader_keys_reference;";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            using (var reader = new CacheReader(cmd.ExecuteReader()))
                            {
                                while (reader.NextRow())
                                {

                                    var shpk = GetShpkFromString(reader.GetString("shader_pack"));
                                    var key = reader.GetInt64("key_id");
                                    var value = reader.GetInt64("value");
                                    ShaderKeys[shpk][(uint)key].KnownValues.Add((uint)value, "");
                                }
                            }
                        }


                        // Constants
                        query = "select * from view_shader_constant_defaults;";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            using (var reader = new CacheReader(cmd.ExecuteReader()))
                            {
                                while (reader.NextRow())
                                {

                                    var shpk = GetShpkFromString(reader.GetString("shader_pack"));
                                    var key = reader.GetInt64("constant_id");
                                    var len = reader.GetInt64("length");
                                    var name = reader.GetString("name");

                                    
                                    var ct = reader.GetFloat("length");
                                    var def = new List<float>();
                                    for(int i = 0 ; i < len; i++)
                                    {

                                        def.Add(reader.GetFloat("value" + i));
                                    }

                                    var info = new ShaderConstantInfo((uint)key, name, def);
                                    if (!ShaderConstants[shpk].ContainsKey((uint)key))
                                    {
                                        ShaderConstants[shpk].Add((uint)key, info);
                                    }
                                }
                            }
                        }

                    }


                    AddCustomNamesAndValues();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }

            });
        }
        /// Updates a given Shader Constant name if it exists and doesn't already have a name.
        private static void UpdateConstantName(EShaderPack shpk, uint constantId, string name, bool overwrite = false) {
            if(ShaderConstants.ContainsKey(shpk) && ShaderConstants[shpk].ContainsKey(constantId) && (overwrite || String.IsNullOrWhiteSpace(ShaderConstants[shpk][constantId].Name)))
            {
                var sc = ShaderConstants[shpk][constantId];
                sc.Name = name;
                ShaderConstants[shpk][constantId] = sc;
            }
        }

        // Updates a given Shader Key name if it exists and doesn't already have a name.
        private static void UpdateKeyName(EShaderPack shpk, uint keyId, string name, bool overwrite = false)
        {
            if (ShaderKeys.ContainsKey(shpk) && ShaderKeys[shpk].ContainsKey(keyId) && (overwrite || String.IsNullOrWhiteSpace(ShaderKeys[shpk][keyId].Name)))
            {
                var sc = ShaderKeys[shpk][keyId];
                sc.Name = name;
                ShaderKeys[shpk][keyId] = sc;
            }
        }
        // Updates a given Shader Key name if it exists and doesn't already have a name.
        private static void UpdateKeyValueName(EShaderPack shpk, uint keyId, uint value, string name)
        {
            if (ShaderKeys.ContainsKey(shpk) && ShaderKeys[shpk].ContainsKey(keyId))
            {
                var sc = ShaderKeys[shpk][keyId];
                if(sc.KnownValues.ContainsKey(value))
                {
                    sc.KnownValues[value] = name;
                } else
                {
                    sc.KnownValues.Add(value, name);
                }
            }
        }

        /// <summary>
        /// Adds custom hand-written names and values to shader keys/constants which
        /// are derived from observation or shader analysis, and not the direct
        /// memory dumps used to create the JSON files.
        /// </summary>
        private static void AddCustomNamesAndValues()
        {
            foreach(var shKv in ShaderKeys)
            {
                // Names from CRC
                UpdateKeyName(shKv.Key, 0xD2777173, "Decal Mode", true);
                UpdateKeyName(shKv.Key, 0xB616DC5A, "Texture Mode", true);
                UpdateKeyName(shKv.Key, 0xC8BD1DEF, "Specular Mode", true);
                UpdateKeyName(shKv.Key, 0xF52CCF05, "Vertex Color Mode", true);
                UpdateKeyName(shKv.Key, 0x380CAED0, "Skin Type", true);
                UpdateKeyName(shKv.Key, 0x24826489, "Sub Color Mode", true);

                // Names from testing/observation
                UpdateKeyName(shKv.Key, 0x40D1481E, "Flow Mode?", true);
                UpdateKeyName(shKv.Key, 0x4F4F0636, "BG Vertex Paint", true);
                UpdateKeyName(shKv.Key, 0xA9A3EE25, "BG Use Diffuse Alpha", true);


                // Texture Mode
                UpdateKeyValueName(shKv.Key, 0xB616DC5A, 0x5CC605B5, "MODE_DEFAULT");
                UpdateKeyValueName(shKv.Key, 0xB616DC5A, 0x22A4AABF, "MODE_SIMPLE");
                UpdateKeyValueName(shKv.Key, 0xB616DC5A, 0x600EF9DF, "MODE_COMPATIBILITY");

                // Sepcular Color
                UpdateKeyValueName(shKv.Key, 0xC8BD1DEF, 0x198D11CD, "COMPAT_DEFAULT");
                UpdateKeyValueName(shKv.Key, 0xC8BD1DEF, 0xA02F4828, "COMPAT_MASK");

                // Decal Mode
                UpdateKeyValueName(shKv.Key, 0xD2777173, 0x4242B842, "DECAL_OFF");
                UpdateKeyValueName(shKv.Key, 0xD2777173, 0x584265DD, "DECAL_ALPHA");
                UpdateKeyValueName(shKv.Key, 0xD2777173, 0xF35F5131, "DECAL_COLOR");

                // Vertex Color
                UpdateKeyValueName(shKv.Key, 0xF52CCF05, 0xF5673524, "VERTEX_COLOR");
                UpdateKeyValueName(shKv.Key, 0xF52CCF05, 0xA7D2FF60, "VERTEX_MASK");

                // Skin Values
                UpdateKeyValueName(shKv.Key, 0x380CAED0, 0xF5673524, "PART_FACE");
                UpdateKeyValueName(shKv.Key, 0x380CAED0, 0x2BDB45F1, "PART_BODY");
                UpdateKeyValueName(shKv.Key, 0x380CAED0, 0x57FF3B64, "PART_BODY_HRO");

                // Hair Values
                UpdateKeyValueName(shKv.Key, 0x24826489, 0xF7B8956E, "PART_HAIR");
                UpdateKeyValueName(shKv.Key, 0x24826489, 0x6E5B8F10, "PART_FACE");

                // Flow Mode
                UpdateKeyValueName(shKv.Key, 0x40D1481E, 0x337C6BC4, "Default?");
                UpdateKeyValueName(shKv.Key, 0x40D1481E, 0x71ADA939, "Use Flow Map?");


                // Names based on user observation.
                UpdateConstantName(shKv.Key, 0x36080AD0, "Dither?");
                UpdateConstantName(shKv.Key, 0xCB0338DC, "Reflection Color?");
                UpdateConstantName(shKv.Key, 0x58DE06E2, "Limbal Color?");
                UpdateConstantName(shKv.Key, 0x59BDA0B1, "Inverse Metalness?");
                UpdateConstantName(shKv.Key, 0x141722D5, "Specular Color");

                // Names based on analyzing shader code.
                UpdateConstantName(shKv.Key, 0x62E44A4F, "Skin Fresnel");
                UpdateConstantName(shKv.Key, 0x2E60B071, "Skin Tile Multiplier");
                UpdateConstantName(shKv.Key, 0x2C2A34DD, "Skin Color");
                UpdateConstantName(shKv.Key, 2569562539, "Skin Wetness Lerp");
                UpdateConstantName(shKv.Key, 1112929012, "Skin Tile Material");
                UpdateConstantName(shKv.Key, 0x59BDA9B1, "Subsurface/Fur Index", true);

                // Brute-Forced CRCs
                UpdateConstantName(shKv.Key, 0x29AC0223, "g_AlphaThreshold", true);
                UpdateConstantName(shKv.Key, 0x2C2A34DD, "g_DiffuseColor", true);
                UpdateConstantName(shKv.Key, 0x11C90091, "g_WhiteEyeColor", true);
                UpdateConstantName(shKv.Key, 0x38A64362, "g_EmissiveColor", true);
                UpdateConstantName(shKv.Key, 3086627810, "g_SSAOMask", true);
                UpdateConstantName(shKv.Key, 1112929012, "g_TileIndex", true);
                UpdateConstantName(shKv.Key, 778088561, "g_TileScale", true);
                UpdateConstantName(shKv.Key, 315010207, "g_TileAlpha", true);
                UpdateConstantName(shKv.Key, 3042205627, "g_NormalScale", true);
                UpdateConstantName(shKv.Key, 2148459359, "g_SheenRate", true);
                UpdateConstantName(shKv.Key, 522602647, "g_SheenTintRate", true);
                UpdateConstantName(shKv.Key, 4103141230, "g_SheenAperture", true);
                UpdateConstantName(shKv.Key, 1357081942, "g_IrisRingColor", true);
                UpdateConstantName(shKv.Key, 1724464446, "g_IrisThickness", true);
                UpdateConstantName(shKv.Key, 3593204584, "g_AlphaAperture", true);
                UpdateConstantName(shKv.Key, 3497683557, "g_AlphaOffset", true);
                UpdateConstantName(shKv.Key, 1648149758, "g_OutlineColor", true);
                UpdateConstantName(shKv.Key, 2289092920, "g_OutlineWidth", true);
                UpdateConstantName(shKv.Key, 0x39551220, "g_TextureMipBias", true);
                UpdateConstantName(shKv.Key, 0x7801E004, "g_GlassIOR", true);
                UpdateConstantName(shKv.Key, 0xDF15112D, "g_ToonIndex", true);
                UpdateConstantName(shKv.Key, 0x3632401A, "g_LipRoughnessScale", true);
                UpdateConstantName(shKv.Key, 0x7DABA471, "g_IrisRingEmissiveIntensity", true);
                UpdateConstantName(shKv.Key, 0xCB0338DC, "g_SpecularColorMask", true);


                UpdateConstantName(shKv.Key, 0xCB0338DC, "g_IrisAPrefersRg", true);
                UpdateConstantName(shKv.Key, 0xC4647F37, "g_GlassThicknessMax", true);
            }

        }


        /// <summary>
        /// Converts Sampler usage to XixTexType
        /// - Note that this is not 1:1 reversable.
        /// XiVTexType should primarily only be used for naive user display purposes.
        /// </summary>
        /// <param name="samplerId"></param>
        /// <param name="mtrl">Option - Material that is using the texture.  Allows proper resolution for specular maps using mask sampler.</param>
        /// <returns></returns>
        public static XivTexType SamplerIdToTexUsage(ESamplerId samplerId, XivMtrl mtrl = null)
        {
            // Compatibility mode shader keys...
#if DAWNTRAIL
            if (mtrl != null && mtrl.ShaderPack == EShaderPack.CharacterLegacy && mtrl.ShaderKeys.Any(x => x.KeyId == 0xB616DC5A && x.Value == 0x600EF9DF)) {
                if (!mtrl.ShaderKeys.Any(x => x.KeyId == 0xC8BD1DEF && x.Value == 0xA02F4828)) {
                    if (samplerId == ESamplerId.g_SamplerMask)
                    {
                        return XivTexType.Specular;
                    }
                }
            }
#endif

#if ENDWALKER
            if (mtrl != null && mtrl.ShaderPack == EShaderPack.Character && mtrl.ShaderKeys.Any(x => x.KeyId == 0xB616DC5A && x.Value == 0x600EF9DF)) {
                if (mtrl.ShaderKeys.Any(x => x.KeyId == 0xC8BD1DEF && x.Value == 0xA02F4828)) {
                    if (samplerId == ESamplerId.g_SamplerSpecular)
                    {
                        return XivTexType.Mask;
                    }
                }
            }
#endif

            // At least for furniture, these are unconditionally masks and not specular maps
            if(mtrl != null && (mtrl.ShaderPack == EShaderPack.Bg || mtrl.ShaderPack == EShaderPack.BgProp || mtrl.ShaderPack == EShaderPack.BgColorChange)){
                if (samplerId == ESamplerId.g_SamplerSpecularMap0 || samplerId == ESamplerId.g_SamplerSpecularMap1)
                {
                    return XivTexType.Mask;
                }
            }

            if(samplerId == ESamplerId.g_SamplerNormal || samplerId == ESamplerId.g_SamplerNormal2 || samplerId == ESamplerId.g_SamplerNormalMap0 || samplerId == ESamplerId.g_SamplerNormalMap1 || samplerId == ESamplerId.g_SamplerTileNormal)
            {
                return XivTexType.Normal;
            } else if(samplerId == ESamplerId.g_SamplerMask || samplerId == ESamplerId.g_SamplerWrinklesMask || samplerId == ESamplerId.g_SamplerTileOrb)
            {
                return XivTexType.Mask;
            } else if(samplerId == ESamplerId.g_SamplerIndex)
            {
                return XivTexType.Index;
            } else if(samplerId == ESamplerId.g_SamplerDiffuse || samplerId == ESamplerId.g_SamplerColorMap0 || samplerId == ESamplerId.g_SamplerColorMap1)
            {
                return XivTexType.Diffuse;
            } else if(samplerId == ESamplerId.g_SamplerReflectionArray || samplerId == ESamplerId.g_SamplerSphereMap)
            {
                return XivTexType.Reflection;
            } else if(samplerId == ESamplerId.g_SamplerDecal)
            {
                return XivTexType.Decal;
            } else if(samplerId == ESamplerId.g_SamplerSpecularMap0 || samplerId == ESamplerId.g_SamplerSpecular || samplerId == ESamplerId.g_SamplerSpecularMap1)
            {
                return XivTexType.Specular;
            }
            return XivTexType.Other;
        }

        /// <summary>
        /// Sampler IDs obtained via dumping RAM stuff.
        /// These are the same across all shpks/etc. so we can just use a simple enum here.
        /// </summary>
        public enum ESamplerId : uint
        {
            // This isn't ALL samplers in existence in FFXIV,
            // But it is all the samplers used with Material Textures,
            // So they're the only ones we care about.
            Invalid = 0,
            g_SamplerNormal = 0x0C5EC1F1,
            g_SamplerNormalMap0 = 0xAAB4D9E9,
            g_SamplerNormalMap1 = 0xDDB3E97F,
            g_SamplerNormal2 = 0x0261CDCB,
            g_SamplerSpecular = 0x2B99E025,
            g_SamplerSpecularMap0 = 0x1BBC2F12,
            g_SamplerSpecularMap1 = 0x6CBB1F84,
            g_SamplerDiffuse = 0x115306BE,
            g_SamplerMask = 0x8A4E82B6,
            g_SamplerIndex = 0x565F8FD8,
            g_SamplerOcclusion = 0x32667BD7,
            g_SamplerFlow = 0xA7E197F6,
            g_SamplerDecal = 0x0237CB94,
            g_SamplerDither = 0x9F467267,
            g_SamplerColorMap0 = 0x1E6FEF9C,
            g_SamplerColorMap1 = 0x6968DF0A,
            g_SamplerWrinklesMask = 0xB3F13975,
            g_SamplerReflection = 0x87F6474D,
            g_SamplerReflectionArray = 0xC5C4CB3C,
            g_SamplerTileOrb = 0x800BE99B,
            g_SamplerTileNormal = 0x92F03E53,
            g_SamplerWaveMap = 0xE6321AFC,
            g_SamplerWaveMap1 = 0xE5338C17,
            g_SamplerWhitecapMap = 0x95E1F64D,
            g_SamplerEnvMap = 0xF8D7957A,
            g_SamplerTable = 0x2005679F,
            g_SamplerGBuffer = 0xEBBB29BD,
            g_SamplerSphereMap = 0x3334D3CA,
            tPerlinNoise2D = 0xC06FEB5B,
        };

        // Enum representation of the format map data is used as.
        public enum ESamplerFormats : ushort
        {
            UsesColorset,
            NoColorset,
            Other
        };

        // Enum representation of the shader names used in mtrl files.
        public enum EShaderPack
        {
            [Description("UNKNOWN")]
            Unknown,

            [Description("character.shpk")]
            Character,

            [Description("characterlegacy.shpk")]
            CharacterLegacy,

            [Description("characterglass.shpk")]
            CharacterGlass,

            [Description("characterstocking.shpk")]
            CharacterStocking,

            [Description("charactertattoo.shpk")]
            CharacterTattoo,

            [Description("skin.shpk")]
            Skin,

            [Description("hair.shpk")]
            Hair,

            [Description("iris.shpk")]
            Iris,

            [Description("bg.shpk")]
            Bg,

            [Description("bgprop.shpk")]
            BgProp,

            [Description("bgcolorchange.shpk")]
            BgColorChange,

            [Description("bguvscroll.shpk")]
            BgUvScroll,

            [Description("bgcrestchange.shpk")]
            BgCrestChange,

            [Description("characterscroll.shpk")]
            CharacterScroll,

            [Description("characterinc.shpk")]
            CharacterInc,

            [Description("characterocclusion.shpk")]
            CharacterOcclusion,

            [Description("water.shpk")]
            Water,

            [Description("river.shpk")]
            River,

            [Description("crystal.shpk")]
            Crystal,

            [Description("lightshaft.shpk")]
            LightShaft,

            [Description("verticalfog.shpk")]
            VerticalFog,

        };

        public static EShaderPack GetShpkFromString(string s)
        {
            return GetValueFromDescription<EShaderPack>(s);
        }

        public static T GetValueFromDescription<T>(string description) where T : Enum
        {
            foreach (var field in typeof(T).GetFields())
            {
                if (Attribute.GetCustomAttribute(field,
                typeof(DescriptionAttribute)) is DescriptionAttribute attribute)
                {
                    if (attribute.Description == description)
                        return (T)field.GetValue(null);
                }
                else
                {
                    if (field.Name == description)
                        return (T)field.GetValue(null);
                }
            }
            return default(T);
        }
        public static string GetEnumDescription(Enum value)
        {
            FieldInfo fi = value.GetType().GetField(value.ToString());

            DescriptionAttribute[] attributes = fi.GetCustomAttributes(typeof(DescriptionAttribute), false) as DescriptionAttribute[];

            if (attributes != null && attributes.Any())
            {
                return attributes.First().Description;
            }

            return value.ToString();
        }


    }
}
