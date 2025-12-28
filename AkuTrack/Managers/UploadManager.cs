using AkuTrack.ApiTypes;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
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

        public async Task<bool> DoUpload(string target, List<AkuGameObject> payload)
        {
            try
            {
                return await Task.Run(async () =>
                {
                    var str = JsonConvert.SerializeObject(payload);
                    var httpContent = new StringContent(str, Encoding.UTF8, "application/json");
                    log.Debug($"Sending <{str}> to AkuAPI.");
                    var response = await httpClient.PostAsync($"{baseUrl}/{target}", httpContent);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        log.Debug("Uploaded to AkuAPI.");
                        return true;
                    }
                    else
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();
                        log.Debug($"Uploaded failed {response.StatusCode}; {responseBody}");
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
