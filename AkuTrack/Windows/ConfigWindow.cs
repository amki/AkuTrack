using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Common.Configuration;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static System.Net.Mime.MediaTypeNames;

namespace AkuTrack.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Configuration configuration, IDataManager dataManager, IPluginLog log) : base("AkuTrack - Config###akutrack_config")
    {
        /*Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;
                */

        this.log = log;
        this.configuration = configuration;
        this.dataManager = dataManager;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(100, 100),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() { }

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
        DrawSection("Appearance and debug", configuration.ConfigAppearanceDebugOpen, value => configuration.ConfigAppearanceDebugOpen = value, () =>
        {
            DrawCheckbox("Draw debug squares", configuration.DrawDebugSquares, value => configuration.DrawDebugSquares = value);

            //ImGui.ColorEdit4("EINEFARBE##1", (float*)&color, ImGuiColorEditFlags_NoInputs | ImGuiColorEditFlags_NoLabel | base_flags);

            ImGui.TextUnformatted("Map text color");
            ImGui.SameLine();
            var textColor = configuration.TextColor;
            if (ImGui.ColorEdit4("##map_text_color", ref textColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.DefaultOptions))
            {
                log.Debug($"Set TextColor to {textColor}");
                configuration.TextColor = textColor;
                configuration.Save();
            }
        });

        DrawSection("Map behavior", configuration.ConfigMapBehaviorOpen, value => configuration.ConfigMapBehaviorOpen = value, () =>
        {
            DrawCheckbox("Center on player when opening", configuration.CenterOnPlayerWhenOpening, value => configuration.CenterOnPlayerWhenOpening = value);
            DrawCheckbox("Keep player centered until manual pan", configuration.KeepPlayerCentered, value => configuration.KeepPlayerCentered = value);
            DrawCheckbox("Sync with game map (M)", configuration.ToggleMapWithGameMap, value => configuration.ToggleMapWithGameMap = value);
        });

        DrawSection("Players", configuration.ConfigPlayersOpen, value => configuration.ConfigPlayersOpen = value, () =>
        {
            DrawCheckbox("Color player markers by class", configuration.ColorPlayerMarkersByClass, value => configuration.ColorPlayerMarkersByClass = value);
            DrawCheckbox("Show camera cone", configuration.DrawCameraCone, value => configuration.DrawCameraCone = value);
            DrawCheckbox("Show other players", configuration.DrawOtherPlayers, value => configuration.DrawOtherPlayers = value);
            DrawCheckbox("Show party members", configuration.DrawPartyMembers, value => configuration.DrawPartyMembers = value);
        });

        DrawSection("World content", configuration.ConfigWorldContentOpen, value => configuration.ConfigWorldContentOpen = value, () =>
        {
            DrawCheckbox("Show battle NPCs", configuration.DrawBNpc, value => configuration.DrawBNpc = value);
            DrawCheckbox("Show critical engagements", configuration.DrawCriticalEngagements, value => configuration.DrawCriticalEngagements = value);
            DrawCheckbox("Show event NPCs", configuration.DrawENpc, value => configuration.DrawENpc = value);
            DrawCheckbox("Show event objects", configuration.DrawEObj, value => configuration.DrawEObj = value);
            DrawCheckbox("Show FATEs", configuration.DrawFates, value => configuration.DrawFates = value);
            DrawIconCategorySettings("Fishingspot", "Show fishing spots", GetFishingSpotIconOptions());
            DrawIconCategorySettings("GatheringPoint", "Show gathering points", GetGatheringPointIconOptions());
            DrawCheckbox("Show housing map markers", configuration.DrawHousingMapMarkers, value => configuration.DrawHousingMapMarkers = value);
            DrawCheckbox("Show map markers with icons and labels", configuration.DrawMapMarkersWithIcons, value => configuration.DrawMapMarkersWithIcons = value);
            DrawCheckbox("Show map markers with labels only", configuration.DrawMapMarkerLabelsOnly, value => configuration.DrawMapMarkerLabelsOnly = value);
            DrawIconCategorySettings("Quest", "Show quest markers", GetQuestIconOptions());
            DrawCheckbox("Show remote markers", configuration.DrawRemoteMarker, value => configuration.DrawRemoteMarker = value);
            DrawCheckbox("Show sightseeing log entries", configuration.DrawSightseeingLogEntries, value => configuration.DrawSightseeingLogEntries = value);
            DrawIconCategorySettings("SpearfishingNotebook", "Show spearfishing spots", GetSpearfishingSpotIconOptions());
            DrawCheckbox("Show treasure", configuration.DrawTreasure, value => configuration.DrawTreasure = value);
            DrawTreasureMapSettings();
        });
    }

    private void DrawSection(string label, bool currentOpen, Action<bool> setOpen, Action drawContents)
    {
        ImGui.SetNextItemOpen(currentOpen, ImGuiCond.Once);
        var open = ImGui.CollapsingHeader(label);
        if (open != currentOpen)
        {
            setOpen(open);
            configuration.Save();
        }

        if (open)
        {
            ImGui.Indent();
            drawContents();
            ImGui.Unindent();
        }
    }

    private void DrawTreasureMapSettings()
    {
        var ranks = GetTreasureMapRanks().OrderBy(rank => rank.Name).ToList();
        if (ranks.Count <= 0)
        {
            ImGui.TextDisabled("Show treasure map spots");
            return;
        }

        var open = configuration.IsIconCategoryOpen("TreasureMaps");
        ImGui.SetNextItemOpen(open, ImGuiCond.Once);
        var nextOpen = ImGui.TreeNode("Show treasure map spots##TreasureMaps_types");
        if (nextOpen != open)
        {
            configuration.SetIconCategoryOpen("TreasureMaps", nextOpen);
            configuration.Save();
        }

        if (!nextOpen)
        {
            return;
        }

        ImGui.Indent();
        var allChecked = ranks.All(rank => configuration.IsTreasureMapRankEnabled(rank.Id));
        if (ImGui.Checkbox("All##TreasureMaps_all", ref allChecked))
        {
            foreach (var rank in ranks)
            {
                configuration.SetTreasureMapRankEnabled(rank.Id, allChecked);
            }
            configuration.Save();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Check all##TreasureMaps_check_all"))
        {
            foreach (var rank in ranks)
            {
                configuration.SetTreasureMapRankEnabled(rank.Id, true);
            }
            configuration.Save();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Uncheck all##TreasureMaps_uncheck_all"))
        {
            foreach (var rank in ranks)
            {
                configuration.SetTreasureMapRankEnabled(rank.Id, false);
            }
            configuration.Save();
        }

        foreach (var rank in ranks)
        {
            DrawCheckbox($"{rank.Name}##TreasureMaps_{rank.Id}", configuration.IsTreasureMapRankEnabled(rank.Id), value => configuration.SetTreasureMapRankEnabled(rank.Id, value));
        }

        ImGui.Unindent();
        ImGui.TreePop();
    }

    private IEnumerable<TreasureMapRankConfigInfo> GetTreasureMapRanks()
    {
        var spots = dataManager.GetSubrowExcelSheet<Lumina.Excel.Sheets.TreasureSpot>();
        foreach (var rank in dataManager.GetExcelSheet<Lumina.Excel.Sheets.TreasureHuntRank>().OrderBy(rank => rank.RowId))
        {
            if (rank.RowId == 0 || rank.Icon == 0 || !rank.ItemName.IsValid || !spots.HasRow(rank.RowId))
            {
                continue;
            }

            var name = rank.ItemName.Value.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            yield return new TreasureMapRankConfigInfo(rank.RowId, name);
        }
    }

    private void DrawIconCategorySettings(string category, string label, IEnumerable<IconCategoryOption> optionsSource)
    {
        var options = optionsSource.OrderBy(option => option.IconId).ThenBy(option => option.Label).ToList();
        if (options.Count <= 1)
        {
            foreach (var option in options)
            {
                DrawCheckbox(label, configuration.IsIconCategoryEntryEnabled(category, option.IconId), value => configuration.SetIconCategoryEntryEnabled(category, option.IconId, value));
            }
            return;
        }

        var open = configuration.IsIconCategoryOpen(category);
        ImGui.SetNextItemOpen(open, ImGuiCond.Once);
        var nextOpen = ImGui.TreeNode($"{label}##{category}_icon_types");
        if (nextOpen != open)
        {
            configuration.SetIconCategoryOpen(category, nextOpen);
            configuration.Save();
        }

        if (!nextOpen)
        {
            return;
        }

        ImGui.Indent();
        var allChecked = options.All(option => configuration.IsIconCategoryEntryEnabled(category, option.IconId));
        if (ImGui.Checkbox($"All##{category}_all", ref allChecked))
        {
            foreach (var option in options)
            {
                configuration.SetIconCategoryEntryEnabled(category, option.IconId, allChecked);
            }
            configuration.Save();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton($"Check all##{category}_check_all"))
        {
            foreach (var option in options)
            {
                configuration.SetIconCategoryEntryEnabled(category, option.IconId, true);
            }
            configuration.Save();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton($"Uncheck all##{category}_uncheck_all"))
        {
            foreach (var option in options)
            {
                configuration.SetIconCategoryEntryEnabled(category, option.IconId, false);
            }
            configuration.Save();
        }

        foreach (var option in options)
        {
            DrawCheckbox($"{option.Label} ({option.IconId:000000})##{category}_{option.IconId}", configuration.IsIconCategoryEntryEnabled(category, option.IconId), value => configuration.SetIconCategoryEntryEnabled(category, option.IconId, value));
        }

        ImGui.Unindent();
        ImGui.TreePop();
    }

    private IEnumerable<IconCategoryOption> GetGatheringPointIconOptions()
    {
        return dataManager.GetExcelSheet<Lumina.Excel.Sheets.GatheringPoint>()
            .Where(row => row.RowId != 0 && row.GatheringPointBase.IsValid && row.GatheringPointBase.Value.GatheringType.IsValid)
            .SelectMany(row =>
            {
                var gatheringType = row.GatheringPointBase.Value.GatheringType.Value;
                var label = gatheringType.Name.ToString();
                var iconId = (uint)gatheringType.IconMain;
                return new[] { new IconCategoryOption(iconId, string.IsNullOrWhiteSpace(label) ? "Gathering point" : label) };
            })
            .Where(option => option.IconId != 0)
            .DistinctBy(option => option.IconId);
    }

    private IEnumerable<IconCategoryOption> GetFishingSpotIconOptions()
    {
        return dataManager.GetExcelSheet<Lumina.Excel.Sheets.FishingSpot>()
            .Where(row => row.RowId != 0)
            .Select(row => new IconCategoryOption(row.Rare ? 60466u : 60465u, row.Rare ? "Rare fishing spot" : "Fishing spot"))
            .DistinctBy(option => option.IconId);
    }

    private IEnumerable<IconCategoryOption> GetSpearfishingSpotIconOptions()
    {
        return dataManager.GetExcelSheet<Lumina.Excel.Sheets.SpearfishingNotebook>()
            .Where(row => row.RowId != 0)
            .Select(row => new IconCategoryOption(row.IsShadowNode ? 60930u : 60929u, row.IsShadowNode ? "Shadow spearfishing spot" : "Spearfishing spot"))
            .DistinctBy(option => option.IconId);
    }

    private IEnumerable<IconCategoryOption> GetQuestIconOptions()
    {
        return dataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>()
            .Where(row => row.RowId != 0 && row.EventIconType.IsValid)
            .Select(row => new IconCategoryOption(GetQuestMapIconId(row), GetQuestIconLabel(row.EventIconType.RowId)))
            .Where(option => option.IconId != 0)
            .DistinctBy(option => option.IconId);
    }

    private static uint GetQuestMapIconId(Lumina.Excel.Sheets.Quest quest)
    {
        if (quest.EventIconType.IsValid)
        {
            var iconId = quest.EventIconType.RowId switch
            {
                1 => 71221u,
                3 => 71201u,
                4 => 71222u,
                8 or 10 => 71341u,
                33 => 62521u,
                34 => 62523u,
                _ => 0u,
            };
            if (iconId != 0)
            {
                return iconId;
            }

            return quest.EventIconType.Value.MapIconAvailable;
        }

        return quest.Icon;
    }

    private static string GetQuestIconLabel(uint eventIconTypeId)
    {
        return eventIconTypeId switch
        {
            1 => "Side quest",
            3 => "Main scenario quest",
            4 => "Small side quest",
            8 or 10 => "Feature quest",
            33 => "Old Sharlayan quest",
            34 => "Tuliyollal quest",
            _ => $"Quest type {eventIconTypeId}",
        };
    }

    private static string GetIconCategoryLabel(string category)
    {
        return category switch
        {
            "GatheringPoint" => "Gathering point",
            "Fishingspot" => "Fishing spot",
            "SpearfishingNotebook" => "Spearfishing",
            "Quest" => "Quest marker",
            _ => category,
        };
    }

    private void DrawCheckbox(string label, bool currentValue, Action<bool> setValue)
    {
        var value = currentValue;
        if (ImGui.Checkbox(label, ref value))
        {
            setValue(value);
            configuration.Save();
        }
    }

    private readonly record struct TreasureMapRankConfigInfo(uint Id, string Name);
    private readonly record struct IconCategoryOption(uint IconId, string Label);
}
