using AkuTrack.ApiTypes;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using Lumina.Excel.Sheets.Experimental;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace AkuTrack.Windows
{
    public class SearchWindow : Window, IDisposable
    {
        private readonly IPluginLog log;
        private readonly IDataManager dataManager;
        private readonly ITextureProvider textureProvider;
        private readonly Configuration configuration;
        private bool de = false;
        private bool en = false;
        private bool fr = false;
        private bool ja = false;
        private string input = "";
        private IEnumerable<Lumina.Excel.Sheets.Item> results;
        public SearchWindow(IPluginLog log,
            IDataManager dataManager,
            ITextureProvider textureProvider,
            ConfigWindow configWindow,
            Configuration configuration) : base("AkuTrack - Search##akutrack_search") {
            this.log = log;
            this.dataManager = dataManager;
            this.textureProvider = textureProvider;
            this.configuration = configuration;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(200, 300),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };
        }

        public void Dispose()
        {
        }

        public override void Draw()
        {
            ImGui.LabelText("", "Itemsearch");
            ImGui.Checkbox("de", ref de);
            ImGui.SameLine();
            ImGui.Checkbox("en", ref en);
            ImGui.SameLine();
            ImGui.Checkbox("fr", ref fr);
            ImGui.SameLine();
            ImGui.Checkbox("ja", ref ja);
            ImGui.InputText("", ref input);
            if (ImGui.Button("Search"))
            {
                log.Debug("KLICK=");
                results = new List<Lumina.Excel.Sheets.Item>();
                if (de || en || fr || ja)
                {
                    log.Debug($"Search {input}");
                    if (de)
                        results = results.Concat(dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>(Dalamud.Game.ClientLanguage.German).Where(i => i.Name.ToString().Contains(input)));
                    if (en)
                        results = results.Concat(dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>(Dalamud.Game.ClientLanguage.English).Where(i => i.Name.ToString().Contains(input)));
                    if (fr)
                        results = results.Concat(dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>(Dalamud.Game.ClientLanguage.French).Where(i => i.Name.ToString().Contains(input)));
                    if (ja)
                        results = results.Concat(dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>(Dalamud.Game.ClientLanguage.Japanese).Where(i => i.Name.ToString().Contains(input)));
                }
                else
                {
                    log.Debug($"Search {input}");
                    results = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>().Where(i => i.Name.ToString().Contains(input));
                }
            }
            
            if (results == null)
                return;
            var c = 0;
            foreach (var itemRow in results) {
                var texture = textureProvider.GetFromGameIcon(new GameIconLookup(itemRow.Icon)).GetWrapOrEmpty();
                ImGui.Image(texture.Handle, texture.Size / 2.0f);
                ImGui.SameLine();
                if (ImGui.MenuItem($"{itemRow.Name.ToString()}")) {
                    log.Debug($"CLIK? {itemRow.RowId}");
                    var gps = dataManager.GetExcelSheet<Lumina.Excel.Sheets.GatheringPoint>().ToList();
                    foreach (var gatheringPointRow in gps)
                    {
                        foreach (var item in gatheringPointRow.GatheringPointBase.Value.Item)
                        {
                            if (item.TryGetValue<Lumina.Excel.Sheets.GatheringItem>(out var gatheringItemRow)) {
                                if (gatheringItemRow.Item.TryGetValue<Lumina.Excel.Sheets.Item>(out var itemR)) {
                                    if(itemR.RowId == itemRow.RowId) {
                                        log.Debug($"Found node {gatheringPointRow.RowId} in {gatheringPointRow.TerritoryType.Value.PlaceName.Value.Name}");
                                        
                                        break;
                                    }
                                }
                                else if (gatheringItemRow.Item.TryGetValue<Lumina.Excel.Sheets.EventItem>(out var eventItemRow)) {
                                    if (eventItemRow.RowId == itemRow.RowId)
                                    {
                                        log.Debug($"Found node {gatheringPointRow.RowId} in {gatheringPointRow.TerritoryType.Value.PlaceName.Value.Name}");
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                c += 1;
                if (c > 100)
                    break;
            }
        }
    }
}
