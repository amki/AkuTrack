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
        // Can't ref a property, so use a local copy
        var drawRemoteMarker = configuration.DrawRemoteMarker;
        if(ImGui.Checkbox("Draw remote?", ref drawRemoteMarker)) {
            configuration.DrawRemoteMarker = drawRemoteMarker;
            configuration.Save();
        }
        var drawBNpc = configuration.DrawBNpc;
        if (ImGui.Checkbox("Draw BNpc?", ref drawBNpc)) {
            configuration.DrawBNpc = drawBNpc;
            configuration.Save();
        }
        ImGui.SameLine();
        var drawENpc = configuration.DrawENpc;
        if (ImGui.Checkbox("Draw ENpc?", ref drawENpc))
        {
            configuration.DrawENpc = drawENpc;
            configuration.Save();
        }

        var drawEObj = configuration.DrawEObj;
        if (ImGui.Checkbox("Draw EObj?", ref drawEObj))
        {
            configuration.DrawEObj = drawEObj;
            configuration.Save();
        }

        var drawGatheringPoint = configuration.DrawGatheringPoint;
        if (ImGui.Checkbox("Draw GatheringPoint?", ref drawGatheringPoint))
        {
            configuration.DrawGatheringPoint = drawGatheringPoint;
            configuration.Save();
        }

        
        var drawDebugSquares = configuration.DrawDebugSquares;
        if (ImGui.Checkbox("Draw debug squares?", ref drawDebugSquares))
        {
            configuration.DrawDebugSquares = drawDebugSquares;
            // Can save immediately on change if you don't want to provide a "Save and Close" button
            configuration.Save();
        }

        //ImGui.ColorEdit4("EINEFARBE##1", (float*)&color, ImGuiColorEditFlags_NoInputs | ImGuiColorEditFlags_NoLabel | base_flags);

        ImGui.TextColored(new Vector4(1.0f, 0.0f, 1.0f, 1.0f), "Map Text Color:");
        var textColor = configuration.TextColor;
        ImGui.ColorEdit4("EINEFARBE##1", ref textColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.DefaultOptions);
        if (ImGui.Button("Sef"))
        {
            log.Debug($"Set TextColor to {textColor}");
            configuration.TextColor = textColor;
            configuration.Save();
        }
    }
}
