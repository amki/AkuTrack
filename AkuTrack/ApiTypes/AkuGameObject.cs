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
    public class AkuGameObject
    {
        public unsafe AkuGameObject(IGameObject obj, IClientState clientState) {
            this.t = obj.ObjectKind.ToString();
            this.pos = obj.Position;
            this.bid = obj.BaseId;
            this.hr = obj.HitboxRadius;
            this.name = obj.Name.ToString();
            this.mid = clientState.MapId;
            this.zid = clientState.TerritoryType;
            this.r = obj.Rotation;

            if (obj is ICharacter c)
            {
                this.nid = c.NameId;
                FFXIVClientStructs.FFXIV.Client.Game.Character.Character* chr = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)c.Address;
                this.moid = chr->ModelContainer.ModelCharaId;
                this.npiid = chr->NamePlateIconId;
            }
        }

        public AkuGameObject(DownloadGameObject dgo) {
            this.t = dgo.objecttype;
            this.name = "<downloaded>";
            this.mid = dgo.map_id;
            this.zid = dgo.zone_id;
            this.pos = new Vector3(dgo.x,dgo.y, dgo.z);
            this.r = dgo.rotation;
            this.bid = dgo.base_id;
            this.npiid = dgo.npiid;
            this.moid = dgo.moid;
            this.hr = dgo.hit_radius;
            this.nid = dgo.nid;
        }
        public string t { get; set; }
        [JsonIgnore]
        public string name { get; set; }
        public uint mid { get; set; }
        public uint zid { get; set; }
        public Vector3 pos { get; set; }
        public float r { get; set; }
        public uint bid { get; set; }
        public uint? npiid {  get; set; }
        public int moid { get; set; }
        //public bool v { get; set; }
        public float hr { get; set; }
        // Only BattleNpc
        public uint? nid { get; set; }
        public string GetUniqueId()
        {
            string input = $"{t},{bid},{pos.X},{pos.Y},{pos.Z},{name}";
            return CalculateUniqueId(input);
        }

        private static string CalculateUniqueId(string input) {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            string hashString = string.Empty;
            foreach (byte x in hash)
            {
                hashString += String.Format("{0:x2}", x);
            }
            //log.Debug($"Hashing {input} to {hashString}");
            return hashString;
        }

        public static string GetUniqueId(IGameObject o)
        {
            string input = $"{o.ObjectKind},{o.BaseId},{o.Position.X},{o.Position.Y},{o.Position.Z},{o.Name.ToString()}";
            return CalculateUniqueId(input);
        }
    }
}
