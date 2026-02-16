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
        public unsafe void Draw(bool isMapHovered, Vector2 currentMapPixelSize, Vector2 DrawPosition, Vector2 DrawOffset, float Scale)
        {
            using (ImRaii.Group())
            {
                var bottomBarSize = new Vector2(ImGui.GetContentRegionMax().X, 30.0f * ImGuiHelpers.GlobalScale);
                ImGui.SetCursorPos(ImGui.GetContentRegionMax() - bottomBarSize);
                using var childBackgroundStyle = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero with { W = 0.33f });
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

                if (true /*isMapHovered*/)
                {
                    // Set cursorPosition to top left corner
                    var cursorPosition = ImGui.GetMousePos() - ImGui.GetWindowPos();
                    cursorPosition.Y -= 30.0f * ImGuiHelpers.GlobalScale - currentMapPixelSize.Y;

                    // Set cursorPosition to top left corner of map
                    cursorPosition -= DrawPosition;
                    cursorPosition /= Scale;

                    // cursorPosition is now relative to map texture and always (0,0) / (2048/2048)

                    var cursorMapPosition = TexturePixelToIngameCoord(cursorPosition);

                    var cursorPositionString = $"Cursor  {cursorMapPosition.X:F1}  {cursorMapPosition.Y:F1}";
                    var cursorStringSize = ImGui.CalcTextSize(cursorPositionString);
                    ImGui.SameLine(ImGui.GetContentRegionMax().X * 2.0f / 3.0f - cursorStringSize.X / 2.0f);
                    ImGui.TextColored(configuration.TextColor, cursorPositionString);
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
