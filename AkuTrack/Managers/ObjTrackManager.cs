using AkuTrack.ApiTypes;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AkuTrack.Managers
{
    public class ObjTrackManager
    {
        private readonly IChatGui chat;
        private readonly IPluginLog log;
        private readonly IObjectTable objectTable;
        private readonly IFramework framework;
        private readonly IClientState clientState;
        private readonly IDataManager dataManager;
        private readonly UploadManager uploadManager;

        public Dictionary<string, AkuGameObject> seenList = new();
        public Dictionary<ulong, AkuGameObject> seenObjTable = new();
        public List<AkuGameObject> toUpload = new();

        public ConcurrentDictionary<string, AkuGameObject> downloadList = new();

        private TimeSpan lastUpdate = new(0);
        private TimeSpan execDelay = new(0, 0, 1);

        public ObjTrackManager(
            IFramework framework,
            IClientState clientState,
            IDataManager dataManager,
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
            this.dataManager = dataManager;
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
                if(obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc ||
                    obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Mount ||
                    obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion ||
                    obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.HousingEventObject ||
                    obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Ornament ||
                    obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Retainer
                    ) {
                    continue;
                }
                if(obj is IBattleNpc bnpc) {
                    if(bnpc.BattleNpcKind == Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Pet ||
                        bnpc.BattleNpcKind == Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Buddy ||
                        bnpc.BattleNpcKind == Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.RaceChocobo ||
                        bnpc.BattleNpcKind == Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.NpcPartyMember) {
                        continue;
                    }
                }
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
                    if (obj.ObjectKind.ToString() != oldObj.t || obj.BaseId != oldObj.bid)
                    {
                        log.Error($"Something terrible happened! oldObj and obj:");
                        log.Error($"{oldObj.t} || {obj.ObjectKind.ToString()}");
                        log.Error($"{oldObj.mid} || {clientState.MapId}");
                        log.Error($"{oldObj.bid} || {obj.BaseId}");
                    }
                    if(obj is ICharacter c) {
                        if(c.NameId != oldObj.nid) {
                            log.Error($"Something terrible happened! oldObj and obj:");
                            log.Error($"{oldObj.nid} || {c.NameId}");
                        }
                    }
                    continue;
                }
                // Check if this object is owned by a player (e.g. a battlepet) or has been aggroed
                var owner = objectTable.SearchById(obj.OwnerId);
                if (owner != null && owner.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc)
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
                // Remove here because it could also be that the go was here and changed
                seenObjTable.Remove(obj.GameObjectId);
                seenObjTable.Add(obj.GameObjectId, upObj);
                objects.Add(upObj);
            }
            return objects;
        }

        private bool HasTableContentChanged(IGameObject obj, AkuGameObject akuObj) {
            if(obj.BaseId == akuObj.bid && obj.ObjectKind.ToString() == akuObj.t) {
                return false;
            }
            log.Debug($"Obj changed in table old: {obj.Name}({obj.BaseId}) new: {obj.Name}/{obj.BaseId}");
            return true;
        }

        public async void FetchAkuGameObjectsFromAkuAPI(uint mid)
        {
            await Task.Run(async () =>
            {
                var objs = await uploadManager.DownloadMapContentFromAPI(mid);
                downloadList.Clear();
                foreach (var obj in objs)
                {
                    if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc)
                    {
                        try
                        {
                            var y = dataManager.GetExcelSheet<Lumina.Excel.Sheets.ENpcResident>(clientState.ClientLanguage).GetRow(obj.bid);
                            obj.name = StringExtensions.ToUpper(y.Singular.ToString(), true, true, false, clientState.ClientLanguage);
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            log.Debug($"{obj.t} ID {obj.bid} is not in range of ENpcResident");
                        }
                    }
                    if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc)
                    {
                        if (obj.nid == null)
                            continue;
                        try
                        {
                            var y = dataManager.GetExcelSheet<Lumina.Excel.Sheets.BNpcName>(clientState.ClientLanguage).GetRow((uint)obj.nid);
                            obj.name = StringExtensions.ToUpper(y.Singular.ToString(), true, true, false, clientState.ClientLanguage);
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            log.Debug($"{obj.t} ID {obj.nid} is not in range of BNpcName");
                        }
                    }
                    if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj)
                    {
                        try
                        {
                            var y = dataManager.GetExcelSheet<Lumina.Excel.Sheets.EObjName>(clientState.ClientLanguage).GetRow(obj.bid);
                            obj.name = StringExtensions.ToUpper(y.Singular.ToString(), true, true, false, clientState.ClientLanguage);
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            log.Debug($"{obj.t} ID {obj.bid} is not in range of EObjName");
                        }
                    }
                    if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.GatheringPoint)
                    {
                        try
                        {
                            var y = dataManager.GetExcelSheet<Lumina.Excel.Sheets.GatheringPoint>(clientState.ClientLanguage).GetRow(obj.bid);
                            //FIXME: Find the gathering node's name. It is in GatheringPointName but how to get there?
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            log.Debug($"{obj.t} ID {obj.bid} is not in range of GatheringPoint");
                        }
                    }
                    if (obj.GetUniqueId() == null)
                    {
                        log.Debug($"ERROR: Could not GetUniqueId of obj.bid {obj.bid} name {obj.name}");
                        continue;
                    }
                    if (!downloadList.TryAdd(obj.GetUniqueId()!, obj))
                    {
                        log.Verbose($"AkuAPI Download: Duplicate Key {obj.GetUniqueId()}");
                    }
                }
                log.Debug($"{downloadList.Count} objects added to downloadList");
            });
        }
    }
}
