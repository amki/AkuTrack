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
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static System.Net.Mime.MediaTypeNames;

namespace AkuTrack.Windows;

public class DetailsWindow : Window, IDisposable
{
    private static readonly Vector2 ItemIconSize = new(60, 60);

    private sealed class RewardDisplayInfo
    {
        public required string Name { get; init; }
        public required uint IconId { get; init; }
        public required string Details { get; init; }
        public required ChestDropReward Reward { get; init; }
    }

    private readonly WindowSystem windowSystem;
    private readonly IPluginLog log;
    private readonly IClientState clientState;
    private readonly IDataManager dataManager;
    private readonly ITextureProvider textureProvider;
    private readonly UploadManager uploadManager;

    private readonly AkuGameObject obj;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public DetailsWindow(WindowSystem windowSystem, IPluginLog log, IClientState clienState, IDataManager dataManager, ITextureProvider textureProvider, UploadManager uploadManager, AkuGameObject obj) : base($"AkuTrack - Details for {obj.bid}##akutrack_details_{obj.bid}")
    {
        this.windowSystem = windowSystem;
        this.log = log;
        this.clientState = clienState;
        this.dataManager = dataManager;
        this.textureProvider = textureProvider;
        this.uploadManager = uploadManager;
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
        else if (obj.t == "EventObj" || obj.t == "Treasure") {
            DrawEventObjDetails();
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

    private void DrawEventObjDetails()
    {
        ImGui.LabelText("", obj.t);
        ImGui.LabelText("", $"Name: {obj.name}");
        ImGui.LabelText("", $"BaseId: {obj.bid}");

        var chestEntries = FindMatchingChestEntries();
        if (chestEntries.Count == 0)
        {
            ImGui.Separator();
            ImGui.LabelText("", "Chest data: no matching entries found for this object.");
            return;
        }

        ImGui.Separator();

        foreach (var chest in chestEntries)
        {

            if (!string.IsNullOrWhiteSpace(chest.DutyName))
            {
                ImGui.LabelText("", $"Duty: {chest.DutyName}");
            }

            if (!string.IsNullOrWhiteSpace(chest.PlaceNameSub))
            {
                ImGui.LabelText("", $"Area: {chest.PlaceNameSub}");
            }

            var rewardDisplayInfo = (chest.Rewards ?? []).Select(BuildRewardDisplayInfo).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var reward in rewardDisplayInfo)
            {
                var texture = textureProvider.GetFromGameIcon(new GameIconLookup(reward.IconId)).GetWrapOrEmpty();
                ImGui.Image(texture.Handle, ItemIconSize);
                ImGui.SameLine();
                ImGui.BeginGroup();
                try
                {
                    ImGui.TextUnformatted(reward.Name);
                    if (!string.IsNullOrWhiteSpace(reward.Details))
                    {
                        ImGui.TextDisabled(reward.Details.TrimStart(' ', '|'));
                    }
                }
                finally
                {
                    ImGui.EndGroup();
                }
            }
        }
    }

    private List<ChestDropEntry> FindMatchingChestEntries()
    {
        var mapEntries = uploadManager.GetChestDropsForMap(obj.mid).ToList();
        if (mapEntries.Count == 0)
        {
            return [];
        }

        var entries = mapEntries
            .Where(x => x.TerritoryId == obj.zid)
            .ToList();

        if (entries.Count == 0)
        {
            entries = mapEntries;
        }

        var chestIdMatches = entries
            .Where(x => (uint)x.Id == obj.bid)
            .ToList();

        if (chestIdMatches.Count > 0)
        {
            return chestIdMatches;
        }

        return mapEntries
            .Where(x => (uint)x.Id == obj.bid)
            .ToList();
    }

    private RewardDisplayInfo BuildRewardDisplayInfo(ChestDropReward reward)
    {
        var details = $"Amount: {reward.Min}-{reward.Max} | Chance: {reward.Pct:P1}";

        if (dataManager.GetExcelSheet<Item>().TryGetRow(reward.Id, out var itemRow))
        {
            if (itemRow.EquipSlotCategory.RowId != 0)
            {
                var classText = itemRow.ClassJobCategory.Value.Name.ToString();
                details += $"\nilvl {itemRow.LevelItem.RowId} | req lvl {itemRow.LevelEquip}";
                if (!string.IsNullOrWhiteSpace(classText))
                {
                    details += $"\n{classText}";
                }
            }

            return new RewardDisplayInfo
            {
                Name = itemRow.Name.ToString(),
                IconId = itemRow.Icon,
                Details = details,
                Reward = reward,
            };
        }

        if (dataManager.GetExcelSheet<EventItem>().TryGetRow(reward.Id, out var eventItemRow))
        {
            return new RewardDisplayInfo
            {
                Name = eventItemRow.Name.ToString(),
                IconId = eventItemRow.Icon,
                Details = $"{details}\nEventItem",
                Reward = reward,
            };
        }

        return new RewardDisplayInfo
        {
            Name = $"Item #{reward.Id}",
            IconId = 0,
            Details = details,
            Reward = reward,
        };
    }
}
