using AkuTrack.ApiTypes;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
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
        public BottomBar(IPluginLog log) {
            this.log = log;
        }
        public void Draw()
        {
            using (ImRaii.Group())
            {
                var bottomBarSize = new Vector2(ImGui.GetContentRegionMax().X, 20.0f * ImGuiHelpers.GlobalScale);
                ImGui.SetCursorPos(ImGui.GetContentRegionMax() - bottomBarSize);
                using var bottomBar = ImRaii.Child("bottom_child", bottomBarSize);
                if (ImGui.Button("Das ist ein kleiner Testknopf mit viel Text!"))
                {
                    log.Debug("KLIKC");
                }
            }
        }
    }
}
