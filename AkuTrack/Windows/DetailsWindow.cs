using AkuTrack.ApiTypes;
using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.FontIdentifier;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
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
    private readonly IClientState clientState;
    private readonly IDataManager dataManager;
    private readonly ITextureProvider textureProvider;

    private readonly AkuGameObject obj;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public DetailsWindow(WindowSystem windowSystem, IPluginLog log, IClientState clienState, IDataManager dataManager, ITextureProvider textureProvider, AkuGameObject obj) : base($"AkuTrack - Details for {obj.bid}##akutrack_details_{obj.bid}")
    {
        this.windowSystem = windowSystem;
        this.log = log;
        this.clientState = clienState;
        this.dataManager = dataManager;
        this.textureProvider = textureProvider;
        this.log.Debug("Construct Window");
        this.obj = obj;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(250, 350),
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
            if(!dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>().TryGetRow(obj.bid, out var aetheryte)) {
                return;
            }
            ImGui.LabelText("", "Aetheryte");

        }
        else if (obj.t == "GatheringPoint") {
            DrawGatheringPointDetails();
        }
    }

    private void DrawENpcDetails() {
        ImGui.LabelText("", "EventNpc");
        if(!dataManager.GetExcelSheet<Lumina.Excel.Sheets.ENpcResident>().TryGetRow(obj.bid, out var eNpcResident)) {
            return;
        }
        ImGui.LabelText("", $"Name: {StringExtensions.ToUpper(eNpcResident.Singular.ToString(), true, true, false, clientState.ClientLanguage)}");
    }

    private void DrawGatheringPointDetails() {
        ImGui.LabelText("", "GatheringPoint");
        if(!dataManager.GetExcelSheet<Lumina.Excel.Sheets.GatheringPoint>().TryGetRow(obj.bid, out var gatheringPointRow)) {
            return;
        }
        ImGui.LabelText("", $"Type: {gatheringPointRow.GatheringPointBase.Value.GatheringType.Value.Name}");
        ImGui.LabelText("", $"Level: {gatheringPointRow.GatheringPointBase.Value.GatheringLevel}");
        ImGui.LabelText("", $"PlaceName: {gatheringPointRow.PlaceName.Value.Name}");
        var c = 0;
        foreach (var item in gatheringPointRow.GatheringPointBase.Value.Item)
        {
            c++;
            if (item.TryGetValue<GatheringItem>(out var gatheringItemRow))
            {
                if (gatheringItemRow.RowId == 0)
                    continue;
                if (gatheringItemRow.Item.TryGetValue<Item>(out var itemRow))
                {
                    var texture = textureProvider.GetFromGameIcon(new GameIconLookup(itemRow.Icon)).GetWrapOrEmpty();
                    ImGui.Image(texture.Handle, texture.Size / 2.0f);
                    ImGui.SameLine();
                    ImGui.LabelText("", $"Item {c}: {itemRow.Name} ({gatheringItemRow.GatheringItemLevel.Value.GatheringItemLevel})");
                }
                else if (gatheringItemRow.Item.TryGetValue<EventItem>(out var eventItemRow))
                {
                    var texture = textureProvider.GetFromGameIcon(new GameIconLookup(eventItemRow.Icon)).GetWrapOrEmpty();
                    ImGui.Image(texture.Handle, texture.Size / 2.0f);
                    ImGui.SameLine();
                    ImGui.LabelText("", $"Item {c}: {eventItemRow.Name} ({gatheringItemRow.GatheringItemLevel.Value.GatheringItemLevel}) | EventItem");
                }
            }
        }
    }
}
