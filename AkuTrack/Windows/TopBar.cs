using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AkuTrack.Windows
{
    public class TopBar
    {
        private readonly Vector4 panelColor = new(0.11f, 0.085f, 0.043f, 0.92f);
        private readonly Vector4 textColor = new(1.0f, 0.92f, 0.68f, 1.0f);

        public void Draw(string mapPath, string cursorPositionText)
        {
            var scale = ImGuiHelpers.GlobalScale;
            var barSize = new Vector2(ImGui.GetContentRegionAvail().X, 30.0f * scale);
            using var childBackgroundStyle = ImRaii.PushColor(ImGuiCol.ChildBg, panelColor);
            using var topBar = ImRaii.Child("top_child", barSize, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

            ImGui.SetCursorPosY(4.0f * scale);
            ImGui.SetCursorPosX(8.0f * scale);
            ImGui.TextColored(textColor, mapPath);

            if (string.IsNullOrWhiteSpace(cursorPositionText))
            {
                return;
            }

            ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(cursorPositionText).X - 12.0f * scale);
            ImGui.TextColored(textColor, cursorPositionText);
        }
    }
}
