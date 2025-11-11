using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace xivModdingFramework.Helpers
{
    /// <summary>
    /// Simple thin static class that handles poking the Penumbra HTTP API
    /// Most functions just return a boolean for success status.
    /// </summary>
    public static class PenumbraAPI
    {
        /// <summary>
        /// Calls /redraw on the Penumbra API.
        /// </summary>
        /// <returns></returns>
        public static async Task<bool> Redraw()
        {
            return await Request("/redraw");

        }

        /// <summary>
        /// Calls /redraw on the Penumbra API to redraw only the local player.
        /// </summary>
        /// <returns></returns>
        public static async Task<bool> RedrawSelf()
        {
            Dictionary<string, string> args = new()
            {
                { "ObjectTableIndex", "0" }
            };
            return await Request("/redraw", args);
        }

        /// <summary>
        /// Calls /reloadmod on the Penumbra API.
        /// </summary>
        /// <returns></returns>
        public static async Task<bool> ReloadMod(string path, string name = null)
        {
            Dictionary<string, string> args = new Dictionary<string, string>();

            if (name != null)
            {
                args.Add("Name", name);
            }
            if (path != null)
            {
                args.Add("Path", path);
            }

            return await Request("/reloadmod", args);
        }

        private static HttpClient _Client = new HttpClient() { BaseAddress = new System.Uri("http://localhost:42069") };

        private static async Task<bool> Request(string urlPath, object data = null)
        {
            data = data == null ? new object() : data;
            return await Task.Run(async () => {
                try
                {
                    using StringContent jsonContent = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                    using HttpResponseMessage response = await _Client.PostAsync("api/" + urlPath, jsonContent);

                    response.EnsureSuccessStatusCode();

                    return true;
                }
                catch (Exception ex)
                {
                    return false;
                    //throw;
                }
            });
        }


        public static string GetQuickLauncherGameDirectory()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "launcherConfigV3.json");
            if (!File.Exists(path))
            {
                return "";
            }

            try
            {
                var obj = JObject.Parse(File.ReadAllText(path));
                var st = (string)obj["GamePath"];
                return st;
            }
            catch (Exception ex)
            {
                return "";
            }
        }

        public static string GetPenumbraDirectory()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "pluginConfigs", "Penumbra.json");
            if (!File.Exists(path))
            {
                return "";
            }

            try
            {
                var obj = JObject.Parse(File.ReadAllText(path));
                var st = (string)obj["ModDirectory"];
                return st;
            }
            catch (Exception ex)
            {
                return "";
            }
        }

        public static bool IsPenumbraInstalled()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "dalamudConfig.json");
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                var obj = JObject.Parse(File.ReadAllText(path));
                var profile = (JObject)obj["DefaultProfile"];
                var plugins = (JObject)profile["Plugins"];
                var pValues = (JArray)plugins["$values"];

                var penumbra = pValues.FirstOrDefault(x => (string)x["InternalName"] == "Penumbra");

                if(penumbra != null)
                {
                    // Normally we'd stop here.  But while Penumbra is in testing we need to check that part.
                    var optIns = (JObject)obj["PluginTestingOptIns"];
                    var oValues = (JArray)optIns["$values"];
                    var pTesting = oValues.FirstOrDefault(x => (string)x["InternalName"] == "Penumbra");
                    if ((string)pTesting["Branch"] == "testing-live")
                    {
                        return true;
                    }

                    return false;
                } else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                return false;
            }
        }
    }
}
