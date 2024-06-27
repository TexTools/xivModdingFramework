using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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


    }
}
