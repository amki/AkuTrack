using AkuTrack.ApiTypes;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Serilog;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace AkuTrack.Windows
{
    public class AkuObjectContextMenu
    {
        public void Draw()
        {
            using var contextMenu = ImRaii.ContextPopup("AkuTrack_AkuObject_Context_Menu");
            if (!contextMenu) return;

            if (ImGui.MenuItem("Ich bin ein AKUOBJEKT"))
            {
                Log.Debug("Klick!");
            }
        }
    }
}
