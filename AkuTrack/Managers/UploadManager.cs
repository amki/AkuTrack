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
        private readonly string chestDropsUrl = "https://raw.githubusercontent.com/Infiziert90/FFXIVGachaSpreadsheet/refs/heads/master/website/static/data/ChestDrops.json";
        private readonly HttpClient httpClient;
        private Dictionary<uint, List<ChestDropEntry>> chestDropsByMap = new();
        private Dictionary<uint, List<ChestDropEntry>> chestDropsByItem = new();
        public UploadManager(
            IPluginLog log
        ) {
            this.log = log;
            httpClient = new HttpClient();
        }

        public IReadOnlyList<ChestDropEntry> GetChestDropsForMap(uint mapId)
        {
            return chestDropsByMap.TryGetValue(mapId, out var entries)
                ? entries
                : Array.Empty<ChestDropEntry>();
        }

        public IReadOnlyList<ChestDropEntry> GetChestDropsForItem(uint itemId)
        {
            return chestDropsByItem.TryGetValue(itemId, out var entries)
                ? entries
                : Array.Empty<ChestDropEntry>();
        }

        public async Task ReloadChestDropsAsync()
        {
            try
            {
                log.Debug($"ChestDrops Download: Querying {chestDropsUrl}");
                var response = await httpClient.GetAsync(chestDropsUrl);
                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync();
                var categories = JsonConvert.DeserializeObject<List<ChestDropCategory>>(responseBody) ?? [];
                var flattened = new Dictionary<uint, List<ChestDropEntry>>();
                var byItem = new Dictionary<uint, List<ChestDropEntry>>();

                foreach (var category in categories)
                {
                    foreach (var expansion in category.Expansions ?? [])
                    {
                        foreach (var header in expansion.Headers ?? [])
                        {
                            foreach (var duty in header.Duties ?? [])
                            {
                                foreach (var chest in duty.Chests ?? [])
                                {
                                    chest.DutyName = duty.Name;
                                    if (!flattened.TryGetValue(chest.MapId, out var entries))
                                    {
                                        entries = [];
                                        flattened[chest.MapId] = entries;
                                    }

                                    entries.Add(chest);
                                    foreach (var reward in chest.Rewards ?? [])
                                    {
                                        if (!byItem.TryGetValue(reward.Id, out var itemEntries))
                                        {
                                            itemEntries = [];
                                            byItem[reward.Id] = itemEntries;
                                        }

                                        itemEntries.Add(chest);
                                    }
                                }
                            }
                        }
                    }
                }

                chestDropsByMap = flattened;
                chestDropsByItem = byItem;
                log.Debug($"ChestDrops Download: Loaded {chestDropsByMap.Values.Sum(x => x.Count)} chest entries across {chestDropsByMap.Count} maps.");
            }
            catch (Exception ex)
            {
                log.Warning($"ChestDrops Download failed: {ex.Message}");
            }
        }

        public async Task<List<AkuGameObject>> DownloadMapContentFromAPI(uint mid) {
            var queryUrl = $"{baseUrl}/api.php?t=None&mid={mid}&sort=created_at_desc&offset=0";
            return await DownloadContentFromAPI(queryUrl);
        }

        public async Task<List<AkuGameObject>> DownloadZoneContentFromAPI(uint zid) {
            var queryUrl = $"{baseUrl}/api.php?t=None&zid={zid}&sort=created_at_desc&offset=0";
            return await DownloadContentFromAPI(queryUrl);
        }

        private async Task<List<AkuGameObject>> DownloadContentFromAPI(string queryUrl) {
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
                log.Debug($"HttpRequestException: {e.Message} | {JsonConvert.SerializeObject(payload)}");
                return false;
            }
        }
    }
}
