using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using System;
using System.Numerics;

namespace AkuTrack.Windows
{
    public class BottomBar
    {
        private readonly IPluginLog log;
        private readonly IClientState clientState;
        private readonly Configuration configuration;
        private readonly MapStateManager mapStateManager;
        private readonly ConfigWindow configWindow;
        private readonly SearchWindow searchWindow;

        public BottomBar(IPluginLog log,
            IClientState clientState,
            ConfigWindow configWindow,
            SearchWindow searchWindow,
            Configuration configuration,
            MapStateManager mapStateManager) {
            this.log = log;
            this.clientState = clientState;
            this.configWindow = configWindow;
            this.searchWindow = searchWindow;
            this.configuration = configuration;
            this.mapStateManager = mapStateManager;
        }
        public void Draw(bool isMapHovered, Vector2 currentMapPixelSize, Vector2 DrawPosition, Vector2 DrawOffset, float Scale, string playerPositionText)
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

                if(mapStateManager.currentMap.RowId != clientState.MapId) {
                    if (ImGui.Button("Sync"))
                    {
                        mapStateManager.SwitchMap(clientState.MapId);
                    }
                    ImGui.SameLine();
                }

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
            var clearButtonWidth = mapStateManager.filterEnabled && !string.IsNullOrWhiteSpace(mapStateManager.filterExpression)
                ? ImGui.CalcTextSize("Clear").X + ImGui.GetStyle().FramePadding.X * 2.0f + ImGui.GetStyle().ItemSpacing.X
                : 0.0f;
            var inputWidth = mapStateManager.filterEnabled ? 180.0f * scale + ImGui.GetStyle().ItemSpacing.X : 0.0f;
            var filterWidth = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize("Filter").X + inputWidth + clearButtonWidth;
            ImGui.SetCursorPosX(MathF.Max(0.0f, barWidth - filterWidth - rightPadding));
            ImGui.SetCursorPosY(topPadding);

            ImGui.Checkbox("Filter##map_search_filter_enabled", ref mapStateManager.filterEnabled);

            if (!mapStateManager.filterEnabled)
            {
                return;
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(180.0f * scale);
            ImGui.InputTextWithHint("##map_search_filter_text", "Search map", ref mapStateManager.filterExpression, 128);

            if (string.IsNullOrWhiteSpace(mapStateManager.filterExpression))
            {
                return;
            }

            ImGui.SameLine();
            if (ImGui.Button("Clear##map_search_filter_clear"))
            {
                mapStateManager.filterExpression = string.Empty;
            }
        }
    }
}
