using AkuTrack.ApiTypes;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Serilog;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace AkuTrack.Windows
{
    public class BottomBar
    {
        private readonly IPluginLog log;
        private readonly Configuration configuration;
        private readonly ConfigWindow configWindow;
        private readonly SearchWindow searchWindow;
        public BottomBar(IPluginLog log,
            ConfigWindow configWindow,
            SearchWindow searchWindow,
            Configuration configuration) {
            this.log = log;
            this.configWindow = configWindow;
            this.searchWindow = searchWindow;
            this.configuration = configuration;
        }
        public unsafe void Draw(bool isMapHovered, Vector2 currentMapPixelSize, Vector2 DrawPosition, Vector2 DrawOffset, float Scale, string playerPositionText)
        {
            using (ImRaii.Group())
            {
                var bottomBarSize = new Vector2(ImGui.GetContentRegionMax().X, 30.0f * ImGuiHelpers.GlobalScale);
                ImGui.SetCursorPos(ImGui.GetContentRegionMax() - bottomBarSize);
                using var childBackgroundStyle = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.085f, 0.043f, 0.92f));
                using var bottomBar = ImRaii.Child("bottom_child", bottomBarSize);
                if (ImGui.Button("Config"))
                {
                    configWindow.Toggle();
                }
                ImGui.SameLine();
                if (ImGui.Button("Search"))
                {
                    searchWindow.Toggle();
                }

                if (!string.IsNullOrWhiteSpace(playerPositionText))
                {
                    var playerPositionSize = ImGui.CalcTextSize(playerPositionText);
                    ImGui.SameLine((ImGui.GetContentRegionMax().X - playerPositionSize.X) * 0.5f);
                    ImGui.TextColored(configuration.TextColor, playerPositionText);
                }

                DrawFilterControlsRightAligned();
            }
        }

        private void DrawFilterControlsRightAligned()
        {
            var scale = ImGuiHelpers.GlobalScale;
            var clearButtonWidth = string.IsNullOrWhiteSpace(configuration.MapSearchFilterText)
                ? 0.0f
                : ImGui.CalcTextSize("Clear").X + ImGui.GetStyle().FramePadding.X * 2.0f + ImGui.GetStyle().ItemSpacing.X;
            var inputWidth = configuration.MapSearchFilterEnabled ? 180.0f * scale + ImGui.GetStyle().ItemSpacing.X : 0.0f;
            var filterWidth = ImGui.CalcTextSize("Filter").X + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X + inputWidth + clearButtonWidth;
            ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetContentRegionMax().X - filterWidth - 8.0f * scale));
            ImGui.SetCursorPosY(4.0f * scale);

            var mapFilterEnabled = configuration.MapSearchFilterEnabled;
            if (ImGui.Checkbox("Filter##map_search_filter_enabled", ref mapFilterEnabled))
            {
                configuration.MapSearchFilterEnabled = mapFilterEnabled;
                configuration.Save();
            }

            if (!configuration.MapSearchFilterEnabled)
            {
                return;
            }

            ImGui.SameLine();
            var filterText = configuration.MapSearchFilterText;
            ImGui.SetNextItemWidth(180.0f * scale);
            if (ImGui.InputTextWithHint("##map_search_filter_text", "Search map", ref filterText, 128))
            {
                configuration.MapSearchFilterText = filterText;
                configuration.Save();
            }

            if (string.IsNullOrWhiteSpace(configuration.MapSearchFilterText))
            {
                return;
            }

            ImGui.SameLine();
            if (ImGui.Button("Clear##map_search_filter_clear"))
            {
                configuration.MapSearchFilterText = string.Empty;
                configuration.Save();
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
