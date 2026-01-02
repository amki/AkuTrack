using Dalamud.Configuration;
using System;
using System.Numerics;

namespace AkuTrack;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool DrawDebugSquares { get; set; } = false;

    public Vector4 TextColor { get; set; } = new Vector4(1.0f, 0.0f, 1.0f, 1.0f);


    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
