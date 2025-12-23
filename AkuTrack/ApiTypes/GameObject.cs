using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace AkuTrack.ApiTypes
{
    public class AkuGameObject
    {
        public AkuGameObject(IGameObject obj) {
            this.t = obj.ObjectKind.ToString();
            this.x = obj.Position.X;
            this.y = obj.Position.Y;
            this.z = obj.Position.Z;
            this.bid = obj.BaseId;
            this.hr = obj.HitboxRadius;
        }
        public string t { get; set; }
        public uint mid { get; set; }
        public uint zid { get; set; }
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
        public uint bid { get; set; }
        public int moid { get; set; }
        //public bool v { get; set; }
        public float hr { get; set; }

        // Only BattleNpc
        public uint? nid { get; set; }
    }
}
