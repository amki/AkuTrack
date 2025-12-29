using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using Lumina.Excel.Sheets.Experimental;
using System;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace AkuTrack.Windows;

public class MapWindow : Window, IDisposable
{
    private readonly string goatImagePath;
    private readonly Plugin plugin;
    private readonly ObjTrackManager objTrackManager;
    private readonly IDataManager dataManager;
    private readonly IClientState clientState;
    private readonly ITextureProvider textureProvider;

    // We give this window a hidden ID using ##.
    // The user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public MapWindow(ObjTrackManager objTrackManager, IDataManager dataManager, IClientState clientState, ITextureProvider textureProvider)
        : base("AkuTrack - Map##With a hidden ID", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.dataManager = dataManager;
        this.clientState = clientState;
        this.objTrackManager = objTrackManager;
        this.textureProvider = textureProvider;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

    }

    public void Dispose() { }

    public unsafe override void Draw()
    {
        /*
        var x = dataManager.GetExcelSheet<Lumina.Excel.Sheets.TripleTriadCardResident>().ToList();
        ImGui.Text("TripleTriadCardResident");
        foreach (var y in x)
        {
            ImGui.Text($"{y.RowId.ToString()}: {y.AcquisitionType.Value.Text.Value.Text}");
        }
        */
        if (AgentMap.Instance()->SelectedMapBgPath.Length is 0)
        {
            var texture = textureProvider.GetFromGame($"{AgentMap.Instance()->SelectedMapPath.ToString()}.tex").GetWrapOrEmpty();

            ImGui.Image(texture.Handle, texture.Size);
        }
    }
}
