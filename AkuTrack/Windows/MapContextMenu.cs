using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Serilog;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace AkuTrack.Windows
{
    public class MapContextMenu
    {
        public void Draw(Vector2 mapDrawOffset)
        {
            using var contextMenu = ImRaii.ContextPopup("AkuTrack_Context_Menu");
            if (!contextMenu) return;

            if (ImGui.MenuItem("Place Flag"))
            {
                Log.Debug("Klick!");
            }
        }
    }
}
