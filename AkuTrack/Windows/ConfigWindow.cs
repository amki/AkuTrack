using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Serilog;
using System;
using System.Numerics;
using static System.Net.Mime.MediaTypeNames;

namespace AkuTrack.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly IPluginLog log;
    private Vector4 color = new();

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Configuration configuration, IPluginLog log) : base("A Wonderful Configuration Window###With a constant ID")
    {
        /*Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;
                */

        this.log = log;
        this.configuration = configuration;
        Size = new Vector2(232, 200);
        SizeCondition = ImGuiCond.Always;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        if (configuration.IsConfigWindowMovable)
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }
        else
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }
    }

    public override void Draw()
    {
        // Can't ref a property, so use a local copy
        var configValue = configuration.SomePropertyToBeSavedAndWithADefault;
        if (ImGui.Checkbox("Random Config Bool", ref configValue))
        {
            configuration.SomePropertyToBeSavedAndWithADefault = configValue;
            // Can save immediately on change if you don't want to provide a "Save and Close" button
            configuration.Save();
        }

        //ImGui.ColorEdit4("EINEFARBE##1", (float*)&color, ImGuiColorEditFlags_NoInputs | ImGuiColorEditFlags_NoLabel | base_flags);

        ImGui.TextColored(new Vector4(1.0f, 0.0f, 1.0f, 1.0f), "Map Text Color:");
        ImGui.ColorEdit4("EINEFARBE##1", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.DefaultOptions);
        if (ImGui.Button("Sef"))
        {
            log.Debug($"Set TextColor to {color}");
            configuration.TextColor = color;
            configuration.Save();
        }

        var movable = configuration.IsConfigWindowMovable;
        if (ImGui.Checkbox("Movable Config Window", ref movable))
        {
            configuration.IsConfigWindowMovable = movable;
            configuration.Save();
        }
    }
}
