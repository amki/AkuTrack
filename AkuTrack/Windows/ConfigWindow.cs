using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Common.Configuration;
using Serilog;
using System;
using System.Numerics;
using static System.Net.Mime.MediaTypeNames;

namespace AkuTrack.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly IPluginLog log;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Configuration configuration, IPluginLog log) : base("AkuTrack - Config###akutrack_config")
    {
        /*Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;
                */

        this.log = log;
        this.configuration = configuration;
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
        DrawSection("Map behavior", configuration.ConfigMapBehaviorOpen, value => configuration.ConfigMapBehaviorOpen = value, () =>
        {
            DrawCheckbox("Sync with game map (M)", configuration.ToggleMapWithGameMap, value => configuration.ToggleMapWithGameMap = value);
            DrawCheckbox("Center on player when opening", configuration.CenterOnPlayerWhenOpening, value => configuration.CenterOnPlayerWhenOpening = value);
            DrawCheckbox("Keep player centered until manual pan", configuration.KeepPlayerCentered, value => configuration.KeepPlayerCentered = value);
        });

        DrawSection("Players", configuration.ConfigPlayersOpen, value => configuration.ConfigPlayersOpen = value, () =>
        {
            DrawCheckbox("Show party members", configuration.DrawPartyMembers, value => configuration.DrawPartyMembers = value);
            DrawCheckbox("Show other players", configuration.DrawOtherPlayers, value => configuration.DrawOtherPlayers = value);
            DrawCheckbox("Color player markers by class", configuration.ColorPlayerMarkersByClass, value => configuration.ColorPlayerMarkersByClass = value);
            DrawCheckbox("Show camera cone", configuration.DrawCameraCone, value => configuration.DrawCameraCone = value);
        });

        DrawSection("World content", configuration.ConfigWorldContentOpen, value => configuration.ConfigWorldContentOpen = value, () =>
        {
            DrawCheckbox("Show remote markers", configuration.DrawRemoteMarker, value => configuration.DrawRemoteMarker = value);
            DrawCheckbox("Show battle NPCs", configuration.DrawBNpc, value => configuration.DrawBNpc = value);
            DrawCheckbox("Show event NPCs", configuration.DrawENpc, value => configuration.DrawENpc = value);
            DrawCheckbox("Show event objects", configuration.DrawEObj, value => configuration.DrawEObj = value);
            DrawCheckbox("Show gathering points", configuration.DrawGatheringPoint, value => configuration.DrawGatheringPoint = value);
            DrawCheckbox("Show treasure", configuration.DrawTreasure, value => configuration.DrawTreasure = value);
            DrawCheckbox("Show FATEs", configuration.DrawFates, value => configuration.DrawFates = value);
            DrawCheckbox("Show sightseeing log entries", configuration.DrawSightseeingLogEntries, value => configuration.DrawSightseeingLogEntries = value);
        });

        DrawSection("Map markers", configuration.ConfigMapMarkersOpen, value => configuration.ConfigMapMarkersOpen = value, () =>
        {
            DrawCheckbox("Show icon markers with labels", configuration.DrawMapMarkersWithIcons, value => configuration.DrawMapMarkersWithIcons = value);
            DrawCheckbox("Show label-only markers", configuration.DrawMapMarkerLabelsOnly, value => configuration.DrawMapMarkerLabelsOnly = value);
        });

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

    private void DrawCheckbox(string label, bool currentValue, Action<bool> setValue)
    {
        var value = currentValue;
        if (ImGui.Checkbox(label, ref value))
        {
            setValue(value);
            configuration.Save();
        }
    }
}
