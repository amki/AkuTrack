using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace AkuTrack.ApiTypes
{
    public class DownloadGameObject
    {
        public DateTimeOffset created_at { get; set; }
        public string objecttype { get; set; }
        public uint zone_id { get; set; }
        public uint map_id { get; set; }
        public uint base_id { get; set; }
        public int moid { get; set; }
        public uint? nid { get; set; }
        public uint? npiid { get; set; }
        public float hit_radius { get; set; }
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
        public float rotation { get; set; }
    }
}
