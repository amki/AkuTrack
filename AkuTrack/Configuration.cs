using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace AkuTrack;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool DrawRemoteMarker { get; set; } = true;
    public bool DrawBNpc { get; set; } = true;
    public bool DrawENpc { get; set; } = true;
    public bool DrawEObj { get; set; } = true;
    public bool DrawGatheringPoint { get; set; } = true;
    public bool DrawTreasure { get; set; } = true;
    public bool DrawTreasureMaps { get; set; } = false;
    public bool DrawFishingSpots { get; set; } = false;
    public bool DrawSpearfishingSpots { get; set; } = false;
    public bool DrawQuestMarkers { get; set; } = false;
    public bool DrawHousingMapMarkers { get; set; } = false;
    public bool DrawCriticalEngagements { get; set; } = false;
    public bool DrawFates { get; set; } = true;
    public bool DrawMapMarkersWithIcons { get; set; } = true;
    public bool DrawMapMarkerLabelsOnly { get; set; } = true;
    public bool DrawSightseeingLogEntries { get; set; } = false;
    public bool DrawPartyMembers { get; set; } = true;
    public bool DrawOtherPlayers { get; set; } = false;
    public bool ColorPlayerMarkersByClass { get; set; } = false;
    public bool DrawCameraCone { get; set; } = true;
    public bool DrawDebugSquares { get; set; } = false;
    public bool ToggleMapWithGameMap { get; set; } = false;
    public bool CenterOnPlayerWhenOpening { get; set; } = false;
    public bool KeepPlayerCentered { get; set; } = false;
    public bool ConfigMapBehaviorOpen { get; set; } = false;
    public bool ConfigPlayersOpen { get; set; } = false;
    public bool ConfigWorldContentOpen { get; set; } = false;
    public bool ConfigMapMarkersOpen { get; set; } = false;
    public bool ConfigAppearanceDebugOpen { get; set; } = false;
    public Dictionary<uint, bool> TreasureMapRankToggles { get; set; } = new();
    public Dictionary<string, bool> IconCategoryToggles { get; set; } = new();
    public Dictionary<string, bool> ConfigIconCategoryOpen { get; set; } = new();

    public Vector4 TextColor { get; set; } = new Vector4(1.0f, 0.0f, 1.0f, 1.0f);

    public bool IsTreasureMapRankEnabled(uint rankId)
    {
        return TreasureMapRankToggles.TryGetValue(rankId, out var enabled) && enabled;
    }

    public void SetTreasureMapRankEnabled(uint rankId, bool enabled)
    {
        TreasureMapRankToggles[rankId] = enabled;
    }

    public bool IsIconCategoryEntryEnabled(string category, uint iconId)
    {
        return !IconCategoryToggles.TryGetValue(GetIconCategoryKey(category, iconId), out var enabled) || enabled;
    }

    public void SetIconCategoryEntryEnabled(string category, uint iconId, bool enabled)
    {
        IconCategoryToggles[GetIconCategoryKey(category, iconId)] = enabled;
    }

    public bool IsIconCategoryOpen(string category)
    {
        return ConfigIconCategoryOpen.TryGetValue(category, out var open) && open;
    }

    public void SetIconCategoryOpen(string category, bool open)
    {
        ConfigIconCategoryOpen[category] = open;
    }

    private static string GetIconCategoryKey(string category, uint iconId) => $"{category}:{iconId}";

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
