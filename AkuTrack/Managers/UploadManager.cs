using AkuTrack.ApiTypes;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AkuTrack.Managers
{
    public class UploadManager
    {
        private readonly IPluginLog log;
        private readonly string baseUrl = "https://akutrack.akurosia.org";
        private readonly HttpClient httpClient;
        public UploadManager(
            IPluginLog log
        ) {
            this.log = log;
            httpClient = new HttpClient();
        }

        public async Task<List<AkuGameObject>> DownloadMapContentFromAPI(uint mid) {
            var result = new List<AkuGameObject>();
            var response = await httpClient.GetAsync($"{baseUrl}/api.php?t=None&mid={mid}&sort=created_at_desc&offset=0");
            var responseBody = await response.Content.ReadAsStringAsync();
            JObject answer = JObject.Parse(responseBody);
            IList<JToken> results = answer["items"]!.Children().ToList();
            foreach (JToken res in results)
            {
                // JToken.ToObject is a helper method that uses JsonSerializer internally
                DownloadGameObject dgo = res.ToObject<DownloadGameObject>()!;
                result.Add(new AkuGameObject(dgo!));
            }
            log.Debug($"Found {result.Count} downloads.");
            return result;
        }

        public async Task<bool> DoUpload(string target, List<AkuGameObject> payload)
        {
            try
            {
                return await Task.Run(async () =>
                {
                    var str = JsonConvert.SerializeObject(payload);
                    var httpContent = new StringContent(str, Encoding.UTF8, "application/json");
                    //log.Debug($"Sending <{str}> to AkuAPI.");
                    var response = await httpClient.PostAsync($"{baseUrl}/{target}", httpContent);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return true;
                    }
                    else
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();
                        log.Debug($"Upload failed: {responseBody}");
                        return false;
                    }
                });
            } catch(Exception e) {
                log.Debug($"Exception: {e.Message}");
                return false;
            }
        }
    }
}
