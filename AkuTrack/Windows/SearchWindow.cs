using AkuTrack.ApiTypes;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
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
    public class SearchWindow : Window, IDisposable
    {
        private readonly IPluginLog log;
        private readonly Configuration configuration;
        public SearchWindow(IPluginLog log,
            ConfigWindow configWindow,
            Configuration configuration) : base("AkuTrack - Search##akutrack_search") {
            this.log = log;
            this.configuration = configuration;
        }

        public void Dispose()
        {
        }

        public override void Draw()
        {
            ImGui.LabelText("", "Dies ist eine Suche. Guck nich so.");
        }
    }
}
