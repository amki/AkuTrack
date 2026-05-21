using AkuTrack.ApiTypes;
using AkuTrack.Managers;
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
        private readonly IFramework framework;
        private readonly IDataManager dataManager;
        private readonly ITextureProvider textureProvider;
        private readonly WindowSystem windowSystem;
        private readonly UploadManager uploadManager;
        private readonly AllaganToolsIpc allaganToolsIpc;
        private readonly Configuration configuration;
        private bool de = false;
        private bool en = false;
        private bool fr = false;
        private bool ja = false;
        private string input = "";
        private IEnumerable<Lumina.Excel.Sheets.Item>? results;
        public SearchWindow(IPluginLog log,
            IFramework framework,
            IDataManager dataManager,
            ITextureProvider textureProvider,
            WindowSystem windowSystem,
            UploadManager uploadManager,
            AllaganToolsIpc allaganToolsIpc,
            ConfigWindow configWindow,
            Configuration configuration) : base("AkuTrack - Search##akutrack_search") {
            this.log = log;
            this.framework = framework;
            this.dataManager = dataManager;
            this.textureProvider = textureProvider;
            this.windowSystem = windowSystem;
            this.uploadManager = uploadManager;
            this.allaganToolsIpc = allaganToolsIpc;
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
                using (ImRaii.PushId($"search_item_{itemRow.RowId}"))
                {
                    ImGui.BeginGroup();
                    try
                    {
                        ImGui.TextUnformatted(itemRow.Name.ToString());
                        if (ImGui.SmallButton("Extra item data"))
                        {
                            OpenItemExtraDataWindow(itemRow.RowId);
                        }
                    }
                    finally
                    {
                        ImGui.EndGroup();
                    }
                }
                c += 1;
                if (c > 100)
                    break;
            }
        }

        private void OpenItemExtraDataWindow(uint itemId)
        {
            var newName = $"akutrack_item_extra_{itemId}";
            foreach (var window in windowSystem.Windows)
            {
                var splitName = window.WindowName.Split("##");
                if (splitName.Length == 2 && splitName[1] == newName)
                {
                    window.IsOpen = true;
                    return;
                }
            }

            var itemWindow = new ItemExtraDataWindow(windowSystem, log, framework, dataManager, textureProvider, uploadManager, allaganToolsIpc, itemId);
            windowSystem.AddWindow(itemWindow);
            itemWindow.IsOpen = true;
        }
    }
}
