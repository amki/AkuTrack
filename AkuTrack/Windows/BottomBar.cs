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
                var scale = ImGuiHelpers.GlobalScale;
                var bottomBarSize = new Vector2(ImGui.GetContentRegionAvail().X, 30.0f * scale);
                using var childBackgroundStyle = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.085f, 0.043f, 0.92f));
                using var bottomBar = ImRaii.Child("bottom_child", bottomBarSize, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                if (!bottomBar)
                {
                    return;
                }

                var padding = 8.0f * scale;
                var topPadding = MathF.Max(3.0f * scale, (bottomBarSize.Y - ImGui.GetFrameHeight()) * 0.5f);
                ImGui.SetCursorPos(new Vector2(padding, topPadding));
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
                    ImGui.SameLine(MathF.Max(ImGui.GetCursorPosX() + ImGui.GetStyle().ItemSpacing.X, (bottomBarSize.X - playerPositionSize.X) * 0.5f));
                    ImGui.TextColored(configuration.TextColor, playerPositionText);
                }

                DrawFilterControlsRightAligned(bottomBarSize.X, padding, topPadding);
            }
        }

        private void DrawFilterControlsRightAligned(float barWidth, float rightPadding, float topPadding)
        {
            var scale = ImGuiHelpers.GlobalScale;
            var clearButtonWidth = configuration.MapSearchFilterEnabled && !string.IsNullOrWhiteSpace(configuration.MapSearchFilterText)
                ? ImGui.CalcTextSize("Clear").X + ImGui.GetStyle().FramePadding.X * 2.0f + ImGui.GetStyle().ItemSpacing.X
                : 0.0f;
            var inputWidth = configuration.MapSearchFilterEnabled ? 180.0f * scale + ImGui.GetStyle().ItemSpacing.X : 0.0f;
            var filterWidth = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize("Filter").X + inputWidth + clearButtonWidth;
            ImGui.SetCursorPosX(MathF.Max(0.0f, barWidth - filterWidth - rightPadding));
            ImGui.SetCursorPosY(topPadding);

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
