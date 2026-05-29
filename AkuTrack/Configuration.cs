using Dalamud.Configuration;
using Dalamud.Game.ClientState.Objects.Enums;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace AkuTrack;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public Dictionary<ObjectKind, bool> shouldDraw = new() { 
        { ObjectKind.None , false },
        { ObjectKind.Pc, false },
        { ObjectKind.BattleNpc, true },
        { ObjectKind.EventNpc, true },
        { ObjectKind.Treasure, true },
        { ObjectKind.Aetheryte, true },
        { ObjectKind.GatheringPoint, true },
        { ObjectKind.EventObj, true },
        { ObjectKind.Mount, false },
        { ObjectKind.Companion, false },
        { ObjectKind.Retainer, false },
        { ObjectKind.AreaObject, false },
        { ObjectKind.HousingEventObject, false },
        { ObjectKind.Cutscene, false },
        { ObjectKind.ReactionEventObject, false },
        { ObjectKind.Ornament, false },
        { ObjectKind.CardStand, false }
    };
    public Dictionary<ObjectKind, bool> forceDraw = new() {
        { ObjectKind.None , false },
        { ObjectKind.Pc, false },
        { ObjectKind.BattleNpc, false },
        { ObjectKind.EventNpc, false },
        { ObjectKind.Treasure, false },
        { ObjectKind.Aetheryte, false },
        { ObjectKind.GatheringPoint, false },
        { ObjectKind.EventObj, false },
        { ObjectKind.Mount, false },
        { ObjectKind.Companion, false },
        { ObjectKind.Retainer, false },
        { ObjectKind.AreaObject, false },
        { ObjectKind.HousingEventObject, false },
        { ObjectKind.Cutscene, false },
        { ObjectKind.ReactionEventObject, false },
        { ObjectKind.Ornament, false },
        { ObjectKind.CardStand, false }
    };

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool DrawRemoteMarker { get; set; } = true;
    public bool DrawBNpc { get; set; } = true;
    public bool DrawENpc { get; set; } = true;
    public bool DrawEObj { get; set; } = true;
    public bool DrawGatheringPoint { get; set; } = true;
    public bool DrawCameraCone { get; set; } = true;
    public bool DrawDebugSquares { get; set; } = false;
    public bool CenterOnPlayerWhenOpening { get; set; } = false;

    public Vector4 TextColor { get; set; } = new Vector4(1.0f, 0.0f, 1.0f, 1.0f);


    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
