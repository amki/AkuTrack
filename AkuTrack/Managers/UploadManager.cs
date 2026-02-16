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
            var queryUrl = $"{baseUrl}/api.php?t=None&mid={mid}&sort=created_at_desc&offset=0";
            log.Debug($"AkuAPI Download: Querying {queryUrl}");
            var result = new List<AkuGameObject>();
            var response = await httpClient.GetAsync(queryUrl);
            var responseBody = await response.Content.ReadAsStringAsync();
            try
            {
                JObject answer = JObject.Parse(responseBody);
                if (answer == null)
                {
                    log.Debug($"AkuAPI Download: Answer was null?");
                    log.Debug($"AkuAPI Download: response: {responseBody}");
                    return result;
                }
                if (!answer.ContainsKey("items"))
                {
                    log.Debug($"AkuAPI Download: No items ins answer?");
                    log.Debug($"AkuAPI Download: response: {responseBody}");
                    return result;
                }
                IList<JToken> results = answer["items"]!.Children().ToList();
                foreach (JToken res in results)
                {
                    // JToken.ToObject is a helper method that uses JsonSerializer internally
                    var dgo = res.ToObject<DownloadGameObject>();
                    if (dgo != null)
                    {
                        result.Add(new AkuGameObject(dgo));
                    }
                    else
                    {
                        log.Debug($"AkuAPI Download: Could not deserialize DownloadGameObject!");
                    }
                }
                log.Debug($"AkuAPI Download: Found {result.Count} downloads.");
                return result;
            } catch(JsonReaderException) {
                log.Debug($"AkuAPI Download: Could not read from JsonReader.");
                log.Debug($"AkuAPI Download: response: {responseBody}");
                return result;
            }
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
            } catch (HttpRequestException e)
            {
                log.Debug($"HttpRequestException: {e.Message}");
                return false;
            }
        }
    }
}
