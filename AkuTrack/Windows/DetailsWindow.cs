using AkuTrack.ApiTypes;
using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Serilog;
using System;
using System.Linq;
using System.Numerics;
using static System.Net.Mime.MediaTypeNames;

namespace AkuTrack.Windows;

public class DetailsWindow : Window, IDisposable
{
    private readonly WindowSystem windowSystem;
    private readonly IPluginLog log;
    private readonly IDataManager dataManager;
    private readonly ITextureProvider textureProvider;
    private Vector4 color = new();

    private readonly AkuGameObject obj;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public DetailsWindow(WindowSystem windowSystem, IPluginLog log, IDataManager dataManager, ITextureProvider textureProvider, AkuGameObject obj) : base($"AkuTrack - Details for {obj.bid}##details{obj.bid}")
    {
        this.windowSystem = windowSystem;
        this.log = log;
        this.dataManager = dataManager;
        this.textureProvider = textureProvider;
        this.log.Debug("Construct Window");
        this.obj = obj;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() {   }

    public override void OnClose()
    {
        log.Debug("CLOSE");
        windowSystem.RemoveWindow(this);
    }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        /*
        if (configuration.IsConfigWindowMovable)
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }
        else
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }
        */
    }

    public override void Draw()
    {
        if (obj.t == "EventNpc")
        {
            DrawENpcDetails();
        }
        else if (obj.t == "BattleNpc")
        {
            ImGui.LabelText("", "BattleNpc");
        }
        else if (obj.t == "EventObj") {
            ImGui.LabelText("", "EventObj");
        }
        else if (obj.t == "Aetheryte")
        {
            var x = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>().GetRowOrDefault(obj.bid);
            ImGui.LabelText("", "Aetheryte");

        }
        else if (obj.t == "GatheringPoint") {
            DrawGatheringPointDetails();
        }
    }

    private void DrawENpcDetails() {
        ImGui.LabelText("", "EventNpc");
        var x = dataManager.GetExcelSheet<Lumina.Excel.Sheets.ENpcResident>().GetRowOrDefault(obj.bid);
        ImGui.LabelText("", $"Name: {x.Value.Singular}");
    }

    private void DrawGatheringPointDetails() {
        ImGui.LabelText("", "GatheringPoint");
        var x = dataManager.GetExcelSheet<Lumina.Excel.Sheets.GatheringPoint>().GetRowOrDefault(obj.bid);
        ImGui.LabelText("", $"Type: {x.Value.GatheringPointBase.Value.GatheringType.Value.Name}");
        ImGui.LabelText("", $"Level: {x.Value.GatheringPointBase.Value.GatheringLevel}");
        ImGui.LabelText("", $"PlaceName: {x.Value.PlaceName.Value.Name}");
        //ImGui.LabelText("", $"ItemCount: {x.Value.GatheringPointBase.Value.Item.Count}");
        var items = x.Value.GatheringPointBase.Value.Item.ToList();
        log.Debug($"Found {items.Count} items in node");
        for(var i=0; i<items.Count; i++ ) {
            var item = items[i];
            ImGui.LabelText("", $"Item {i+1}: {item.GetValueOrDefault<GatheringItem>()?.Item.GetValueOrDefault<Item>()?.Name} ({item.GetValueOrDefault<GatheringItem>()?.GatheringItemLevel.Value.GatheringItemLevel})");
            var luminaid = item.GetValueOrDefault<GatheringItem>()?.Item.GetValueOrDefault<Item>()?.Icon;
            if (luminaid != null) {
                int iconid = (int)(luminaid);
                var texture = textureProvider.GetFromGameIcon(iconid).GetWrapOrEmpty();
                ImGui.Image(texture.Handle, texture.Size / 2.0f);
            }
        }
    }
}
