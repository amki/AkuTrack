using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AkuTrack.Windows
{
    public class TopBar
    {
        private const float ComboBoxWidth = 200.0f;

        private readonly IPluginLog log;
        private readonly IDataManager dataManager;
        private readonly MapStateManager mapStateManager;
        private readonly Vector4 panelColor = new(0.11f, 0.085f, 0.043f, 0.92f);
        private readonly Vector4 textColor = new(1.0f, 0.92f, 0.68f, 1.0f);

        private readonly Dictionary<uint, string> regions = new();
        private readonly Dictionary<uint, string> places = new();
        private readonly Dictionary<uint, string> subs = new();
        private int selectedRegionIndex;
        private int selectedPlacesIndex;
        private int selectedSubsIndex;

        public TopBar(
            IPluginLog log,
            IDataManager dataManager,
            MapStateManager mapStateManager)
        {
            this.log = log;
            this.dataManager = dataManager;
            this.mapStateManager = mapStateManager;

            mapStateManager.RegionSelectedItemChanged += RegionChanged;
            mapStateManager.PlaceSelectedItemChanged += PlaceChanged;
            mapStateManager.SubSelectedItemChanged += SubChanged;

            foreach (var map in dataManager.GetExcelSheet<Map>())
            {
                var region = map.PlaceNameRegion.Value;
                if (region.RowId == 0 || Enum.IsDefined(typeof(MapStateManager.FilteredRegions), region.RowId))
                {
                    continue;
                }

                regions.TryAdd(region.RowId, region.Name.ToString());
            }
        }

        public void Draw(string mapPath, string cursorPositionText)
        {
            var scale = ImGuiHelpers.GlobalScale;
            var barSize = new Vector2(ImGui.GetContentRegionAvail().X, 58.0f * scale);

            using var childBackgroundStyle = ImRaii.PushColor(ImGuiCol.ChildBg, panelColor);
            using var topBar = ImRaii.Child("top_child", barSize, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (!topBar)
            {
                return;
            }

            ImGui.SetCursorPos(new Vector2(8.0f * scale, 5.0f * scale));
            DrawMapPicker();

            var textY = 35.0f * scale;
            ImGui.SetCursorPos(new Vector2(8.0f * scale, textY));
            ImGui.TextColored(textColor, mapPath);

            if (!string.IsNullOrWhiteSpace(cursorPositionText))
            {
                ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(cursorPositionText).X - 12.0f * scale);
                ImGui.TextColored(textColor, cursorPositionText);
            }
        }

        private void DrawMapPicker()
        {
            if (regions.Count == 0)
            {
                return;
            }

            selectedRegionIndex = Math.Clamp(selectedRegionIndex, 0, regions.Count - 1);
            ImGui.SetNextItemWidth(ComboBoxWidth * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginCombo("Region", regions.ElementAt(selectedRegionIndex).Value))
            {
                for (var i = 0; i < regions.Count; i++)
                {
                    var region = regions.ElementAt(i);
                    var isSelected = selectedRegionIndex == i;
                    if (ImGui.Selectable(region.Value, isSelected))
                    {
                        selectedRegionIndex = i;
                        mapStateManager.RegionChange(region.Key);
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }

            if (places.Count == 0)
            {
                return;
            }

            ImGui.SameLine();
            selectedPlacesIndex = Math.Clamp(selectedPlacesIndex, 0, places.Count - 1);
            ImGui.SetNextItemWidth(ComboBoxWidth * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginCombo("Place", places.ElementAt(selectedPlacesIndex).Value))
            {
                for (var i = 0; i < places.Count; i++)
                {
                    var place = places.ElementAt(i);
                    var isSelected = selectedPlacesIndex == i;
                    if (ImGui.Selectable(place.Value, isSelected))
                    {
                        selectedPlacesIndex = i;
                        mapStateManager.PlaceChange(place.Key);
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }

            if (subs.Count == 0)
            {
                return;
            }

            ImGui.SameLine();
            selectedSubsIndex = Math.Clamp(selectedSubsIndex, 0, subs.Count - 1);
            ImGui.SetNextItemWidth(ComboBoxWidth * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginCombo("Sub", subs.ElementAt(selectedSubsIndex).Value))
            {
                for (var i = 0; i < subs.Count; i++)
                {
                    var sub = subs.ElementAt(i);
                    var isSelected = selectedSubsIndex == i;
                    if (ImGui.Selectable(sub.Value, isSelected))
                    {
                        selectedSubsIndex = i;
                        mapStateManager.SubChange(sub.Key);
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }
        }

        private void RegionChanged(uint rowId)
        {
            log.Debug($"The region has been changed to {rowId}");
            places.Clear();
            subs.Clear();
            selectedPlacesIndex = 0;
            selectedSubsIndex = 0;

            foreach (var map in dataManager.GetExcelSheet<Map>().Where(map => map.PlaceNameRegion.RowId == rowId))
            {
                var placeName = map.PlaceName.Value.Name.ToString();
                if (string.IsNullOrWhiteSpace(placeName) || places.ContainsValue(placeName))
                {
                    continue;
                }

                places.Add(map.RowId, placeName);
            }
        }

        private void PlaceChanged(uint rowId)
        {
            log.Debug($"The place has been changed to {rowId}");
            if (!dataManager.GetExcelSheet<Map>().TryGetRow(rowId, out var selectedMap))
            {
                return;
            }

            var name = selectedMap.PlaceName.Value.Name.ToString();
            var maps = dataManager.GetExcelSheet<Map>()
                .Where(map => map.PlaceName.Value.Name.ToString() == name)
                .ToList();

            subs.Clear();
            selectedSubsIndex = 0;
            if (maps.Count > 1)
            {
                foreach (var map in maps)
                {
                    subs.TryAdd(map.RowId, $"{map.PlaceNameSub.Value.Name} ({map.RowId})");
                }
            }

            mapStateManager.SwitchMap(rowId);
        }

        private void SubChanged(uint rowId)
        {
            mapStateManager.SwitchMap(rowId);
        }
    }
}
