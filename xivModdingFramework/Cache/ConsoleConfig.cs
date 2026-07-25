using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;

namespace xivModdingFramework.Cache
{
    /// <summary>
    /// Simple class for storing basic configuration information in the working directory.
    /// This is a basic way for framework applications to hook the Framework without having to manually
    /// supply things like game path which may already be configured by TexTools/etc.
    /// </summary>
    public class ConsoleConfig
    {
        public const string ConfigPath = "console_config.json";

        public string XivPath { get; set; } = "";

        public string Language { get; set; } = "en";

        [JsonIgnore]
        public XivLanguage XivLanguage
        {
            get
            {
                try
                {
                    return XivLanguages.GetXivLanguage(Language);
                }
                catch
                {
                    return XivLanguage.English;
                }
            }
        }


        public static void Update(Action<ConsoleConfig> action)
        {
            var c = Get();
            action(c);
            c.Save();
        }

        public static ConsoleConfig Get()
        {
            var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            var path = Path.Combine(cwd, ConfigPath);

            if (!File.Exists(path))
            {
                return new ConsoleConfig();
            }

            try
            {
                return JsonConvert.DeserializeObject<ConsoleConfig>(File.ReadAllText(path));
            }
            catch
            {
                return new ConsoleConfig();
            }
        }

        public void Save()
        {
            var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            var path = Path.Combine(cwd, ConsoleConfig.ConfigPath);
            var text = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(path, text);
        }


        public static async Task InitCacheFromConfig(bool runWorker = false)
        {
            var c = Get();
            if (string.IsNullOrWhiteSpace(c.XivPath))
            {
                throw new ArgumentException("Console Config file does not have a valid FFXIV File path configured.");
            }
            await XivCache.SetGameInfo(new DirectoryInfo(c.XivPath), c.XivLanguage, runWorker);

            // Set a unique temp path.
            var tempDir = IOUtil.GetUniqueSubfolder(Path.GetTempPath(), "xivct");
            XivCache.FrameworkSettings.TempDirectory = tempDir;
        }
    }
}
