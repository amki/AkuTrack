using AkuTrack.ApiTypes;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading.Channels;
using System.Security.Cryptography;

namespace AkuTrack.Managers
{
    public class ObjTrackManager
    {
        private readonly IChatGui chat;
        private readonly IPluginLog log;
        private readonly IObjectTable objectTable;
        private readonly IFramework framework;
        private readonly IClientState clientState;
        private readonly UploadManager uploadManager;

        public Dictionary<string, AkuGameObject> seenList = new();
        public Dictionary<ulong, AkuGameObject> seenObjTable = new();
        public List<AkuGameObject> toUpload = new();

        private TimeSpan lastUpdate = new(0);
        private TimeSpan execDelay = new(0, 0, 1);

        public ObjTrackManager(
            IFramework framework,
            IClientState clientState,
            IDalamudPluginInterface pluginInterface,
            IChatGui chat,
            IPluginLog log,
            IObjectTable objectTable,
            UploadManager uploadManager
        )
        {
            log.Debug($"ObjTrackManager online!");
            this.chat = chat;
            this.log = log;
            this.objectTable = objectTable;
            this.framework = framework;
            this.clientState = clientState;
            this.uploadManager = uploadManager;

            framework.Update += Tick;
        }

        public void CleanSeen() {
            seenList.Clear();
            seenObjTable.Clear();
            toUpload.Clear();
        }

        private void Tick(IFramework framework)
        {
            lastUpdate += framework.UpdateDelta;
            if (lastUpdate > execDelay)
            {
                DoUpdate(framework);
                lastUpdate = new(0);
            }
        }

        private async void DoUpdate(IFramework framework)
        {
            //log.Debug("Tick!");
            var ups = LookForNewObjects();
            if (ups.Count > 0)
            {
                toUpload.AddRange(ups);
                var res = await uploadManager.DoUpload("duckit/", toUpload);
                //log.Debug($"Uploading was {res}");
                if (res)
                {
                    toUpload.Clear();
                }
                else
                {
                    log.Debug($"Uploading failed!");
                }
            }
        }

        private List<AkuGameObject> LookForNewObjects()
        {
            List<AkuGameObject> objects = new();
            foreach (var obj in objectTable)
            {
                // no players, mounts, minion pets, housing items, wings/umbrellas, retainers
                if(obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player ||
                    obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.MountType ||
                    obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion ||
                    obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Housing ||
                    obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Ornament ||
                    obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Retainer
                    ) {
                    continue;
                }
                if(obj is IBattleNpc bnpc) {
                    if(bnpc.BattleNpcKind == Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Pet ||
                        bnpc.BattleNpcKind == Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Chocobo ||
                        bnpc.BattleNpcKind == Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.NpcPartyMember) {
                        continue;
                    }
                }
                // FIXME: For some reason GatheringPoints sometimes spawn without a name but then get it later?
                if (obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.GatheringPoint && obj.Name.ToString() == string.Empty)
                    continue;
                var uid = AkuGameObject.GetUniqueId(obj);
                if(uid == null ) {
                    log.Debug($"ERROR: Could not GetUniqueId from obj.bid {obj.BaseId} name {obj.Name}");
                    continue;
                }
                // Check if this object has already been sent by us
                if (seenList.ContainsKey(uid))
                {
                    var oldObj = seenList[uid];
                    if (uid != oldObj.GetUniqueId())
                    {
                        log.Error($"Something terrible happened! -> Check {uid} but seenList fetches {oldObj.GetUniqueId()}");
                    }
                    if (obj.ObjectKind.ToString() != oldObj.t || obj.Name.ToString() != oldObj.name || obj.BaseId != oldObj.bid)
                    {
                        log.Error($"Something terrible happened! oldObj and obj:");
                        log.Error($"{oldObj.t} || {obj.ObjectKind.ToString()}");
                        log.Error($"{oldObj.mid} || {clientState.MapId}");
                        log.Error($"{oldObj.name} || {obj.Name.ToString()}");
                        log.Error($"{oldObj.bid} || {obj.BaseId}");
                    }
                    continue;
                }
                // Check if this object is owned by a player (e.g. a battlepet) or has been aggroed
                var owner = objectTable.SearchById(obj.OwnerId);
                if (owner != null && owner.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                {
                    log.Debug($"Obj {obj.Name} [{obj.BaseId}] is player owned. Not sending. @ x/y/z: {obj.Position.X}/{obj.Position.Y}/{obj.Position.Z}");
                    continue;
                }

                var upObj = new AkuGameObject(obj, clientState);

                // Check if there has been an object in this slot already and if it is likely still the same one but has moved (moving changes the uid hash)
                if (seenObjTable.ContainsKey(obj.GameObjectId) && !HasTableContentChanged(obj, upObj)) {
                    //log.Debug($"Obj {obj.GameObjectId} has moved but was already sent.");
                    continue;
                }
                seenList.Add(uid, upObj);
                seenObjTable.Remove(obj.GameObjectId);
                seenObjTable.Add(obj.GameObjectId, upObj);
                objects.Add(upObj);
            }
            return objects;
        }

        private bool HasTableContentChanged(IGameObject obj, AkuGameObject akuObj) {
            if(obj.BaseId == akuObj.bid && obj.Name.ToString() == akuObj.name) {
                return false;
            }
            log.Debug($"Obj changed in table old: {obj.Name}({obj.BaseId}) new: {obj.Name}/{obj.BaseId}");
            return true;
        }
    }
}
