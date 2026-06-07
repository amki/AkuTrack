using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Lumina.Excel.Sheets;

namespace AkuTrack.Windows
{
    public class TopBar
    {
        private readonly IPluginLog log;
        private readonly IClientState clientState;
        private readonly IDataManager dataManager;
        private readonly Configuration configuration;
        private readonly MapStateManager mapStateManager;
        private readonly ConfigWindow configWindow;
        private readonly SearchWindow searchWindow;
        private Dictionary<uint, string> regions = new();
        private Dictionary<uint, string> places = new();
        private Dictionary<uint, string> subs = new();
        private int selectedRegionIndex = 0;
        private int selectedPlacesIndex = 0;
        private int selectedSubsIndex = 0;

        private static float comboBoxWidth = 200.0f;

        public TopBar(IPluginLog log,
            IClientState clientState,
            IDataManager dataManager,
            ConfigWindow configWindow,
            SearchWindow searchWindow,
            Configuration configuration,
            MapStateManager mapStateManager) {
            this.log = log;
            this.clientState = clientState;
            this.dataManager = dataManager;
            this.configWindow = configWindow;
            this.searchWindow = searchWindow;
            this.configuration = configuration;
            this.mapStateManager = mapStateManager;

            mapStateManager.RegionSelectedItemChanged += RegionChanged;
            mapStateManager.PlaceSelectedItemChanged += PlaceChanged;

            var mapSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>();
            foreach (var map in mapSheet)
            {
                var region = map.PlaceNameRegion.Value;
                // Skip all the filtered regions
                if (Enum.IsDefined(typeof(MapStateManager.FilteredRegions), region.RowId))
                {
                    continue;
                } 
                regions.TryAdd(region.RowId,region.Name.ToString());
            }
        }

        private void RegionChanged(uint rowId)
        {
            log.Debug($"The region has been changed to {rowId}");
            places.Clear();
            selectedPlacesIndex = 0;
            var p = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>().Where(x => x.PlaceNameRegion.RowId == rowId);
            foreach (var place in p)
            {
                if (places.ContainsValue(place.PlaceName.Value.Name.ToString()))
                {
                    continue;
                }
                places.Add(place.RowId,place.PlaceName.Value.Name.ToString());
            }
        }

        private void PlaceChanged(uint rowId)
        {
            log.Debug($"The place has been changed to {rowId}");
            var name = dataManager.GetExcelSheet<Map>().First(x => x.RowId == rowId).PlaceName.Value.Name.ToString();
            var s = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>().Where(x => x.PlaceName.Value.Name.ToString() == name).ToList();
            if (s.Count > 1)
            {
                subs.Clear();
                foreach (var place in s)
                {
                    subs.Add(place.RowId,$"{place.PlaceNameSub.Value.Name.ToString()} ({place.RowId})");
                }
            }
            else
            {
                subs.Clear();
                selectedSubsIndex = 0;
            }
            mapStateManager.SwitchMap(rowId);
        }
        
        public unsafe void Draw(bool isMapHovered, Vector2 currentMapPixelSize, Vector2 DrawPosition, Vector2 DrawOffset, float Scale)
        {
            using (ImRaii.Group())
            {
                // Start drawing top left
                ImGui.SetCursorPos(Vector2.Zero);
                var bottomBarSize = new Vector2(0, 60.0f * ImGuiHelpers.GlobalScale);
                using var childBackgroundStyle = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero with { W = 0.33f });
                // Draw the topBar, then draw into it
                using var topBar = ImRaii.Child("top_child", bottomBarSize);
                string[] options = new[] { "Option 1", "Option 2", "Option 3" };

                ImGui.SetNextItemWidth(comboBoxWidth);
                if (ImGui.BeginCombo("Region", regions.ElementAt(selectedRegionIndex).Value))
                {
                    for (int i = 0; i < regions.Count; i++)
                    {
                        var kvp = regions.ElementAt(i);
                        bool isSelected = (selectedRegionIndex == i);

                        if (ImGui.Selectable(kvp.Value, isSelected))
                        {
                            selectedRegionIndex = i;
                            mapStateManager.RegionChange(regions.ElementAt(selectedRegionIndex).Key);
                        }

                        if (isSelected)
                            ImGui.SetItemDefaultFocus(); // scrolls into view on first open
                    }
                    ImGui.EndCombo();
                }

                if (places.Count > 0)
                {
                    ImGui.SetNextItemWidth(comboBoxWidth);
                    if (ImGui.BeginCombo("Place", places.ElementAt(selectedPlacesIndex).Value))
                    {
                        for (int i = 0; i < places.Count; i++)
                        {
                            var kvp = places.ElementAt(i);
                            bool isSelected = (selectedPlacesIndex == i);

                            if (ImGui.Selectable(kvp.Value, isSelected))
                            {
                                selectedPlacesIndex = i;
                                mapStateManager.PlaceChange(places.ElementAt(selectedPlacesIndex).Key);
                            }
                            if (isSelected)
                                ImGui.SetItemDefaultFocus(); // scrolls into view on first open
                        }
                        ImGui.EndCombo();
                    }

                    if (subs.Count > 0)
                    {
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(comboBoxWidth);
                        if (ImGui.BeginCombo("Sub", subs.ElementAt(selectedSubsIndex).Value))
                        {
                            for (int i = 0; i < subs.Count; i++)
                            {
                                var kvp = subs.ElementAt(i);
                                bool isSelected = (selectedSubsIndex == i);

                                if (ImGui.Selectable(kvp.Value, isSelected))
                                {
                                    selectedSubsIndex = i;
                                    mapStateManager.PlaceChange(subs.ElementAt(selectedSubsIndex).Key);
                                }
                                if (isSelected)
                                    ImGui.SetItemDefaultFocus(); // scrolls into view on first open
                            }
                            ImGui.EndCombo();
                        }   
                    }
                }
                
                ImGui.SameLine();
                if (ImGui.Button("TOP BAR!"))
                {
                    
                }
            }
        }

        public unsafe Vector2 TexturePixelToIngameCoord(Vector2 textureCoord) {
            // Aku's "ReverseToMapPixel"
            var tmp = (textureCoord - new Vector2(1024, 1024)) / GetMapScaleFactor() - GetRawMapOffsetVector();
            var result = new Vector2(0, 0);
            // Aku's "toMapCoordinate"
            tmp.X *= GetMapScaleFactor();
            tmp.Y *= GetMapScaleFactor();
            result.X = (float)Math.Round(((41.0f / GetMapScaleFactor() * ((tmp.X + 1024.0f) / 2048.0f) + 1) * 100) / 100,1);
            result.Y = (float)Math.Round(((41.0f / GetMapScaleFactor() * ((tmp.Y + 1024.0f) / 2048.0f) + 1) * 100) / 100,1);
            return result;
        }

        public static unsafe Vector2 GetRawMapOffsetVector() => new(AgentMap.Instance()->SelectedOffsetX, AgentMap.Instance()->SelectedOffsetY);
        public static unsafe float GetMapScaleFactor() => AgentMap.Instance()->SelectedMapSizeFactorFloat;
    }
}
