using AkuTrack.ApiTypes;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
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

        private readonly Dictionary<uint, HashSet<ulong>> seenNpcIdsByZone = new();
        private readonly List<AkuGameObject> downloadedZoneNpcs = new();
        private readonly object downloadedZoneNpcsLock = new();
        private uint downloadedZoneId;
        private uint downloadingZoneId;
        private Task? downloadZoneNpcsTask;

        private TimeSpan lastUpdate = new(0);
        private TimeSpan execDelay = new(0, 0, 1);
        private const float NpcRoughPositionDistance = 10.0f;

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
            seenNpcIdsByZone.Clear();
            lock (downloadedZoneNpcsLock)
            {
                downloadedZoneNpcs.Clear();
            }

            downloadedZoneId = 0;
            downloadingZoneId = 0;
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
            StartDownloadedZoneNpcsRefresh();
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

                if (IsTrackableNpc(obj) && HasSeenNpcInCurrentZone(obj))
                {
                    continue;
                }

                var upObj = new AkuGameObject(obj, clientState);

                if (IsKnownDownloadedNpc(upObj))
                {
                    log.Debug($"Skipping upload for known downloaded {upObj.t} bid {upObj.bid} nid {upObj.nid} gameId {upObj.unique_ingame_id} zone {upObj.zid} map {upObj.mid} at {upObj.pos.X:F1}/{upObj.pos.Y:F1}/{upObj.pos.Z:F1}.");
                    MarkNpcSeenInCurrentZone(obj);
                    continue;
                }

                // Check if there has been an object in this slot already and if it is likely still the same one but has moved (moving changes the uid hash)
                if (seenObjTable.ContainsKey(obj.GameObjectId) && !HasTableContentChanged(obj, upObj)) {
                    //log.Debug($"Obj {obj.GameObjectId} has moved but was already sent.");
                    continue;
                }
                seenList.Add(uid, upObj);
                MarkNpcSeenInCurrentZone(obj);
                // Remove here because it could also be that the go was here and changed
                seenObjTable.Remove(obj.GameObjectId);
                seenObjTable.Add(obj.GameObjectId, upObj);
                objects.Add(upObj);
            }
            return objects;
        }

        private void StartDownloadedZoneNpcsRefresh()
        {
            var zoneId = clientState.TerritoryType;
            if (zoneId == 0 || downloadedZoneId == zoneId || downloadingZoneId == zoneId)
            {
                return;
            }

            if (downloadZoneNpcsTask is { IsCompleted: false })
            {
                return;
            }

            downloadingZoneId = zoneId;
            downloadZoneNpcsTask = RefreshDownloadedZoneNpcs(zoneId);
        }

        private async Task RefreshDownloadedZoneNpcs(uint zoneId)
        {
            try
            {
                var downloads = await uploadManager.DownloadZoneContentFromAPI(zoneId);
                var zoneNpcs = downloads
                    .Where(obj => IsTrackableNpc(obj) && obj.unique_ingame_id is not null)
                    .ToList();

                lock (downloadedZoneNpcsLock)
                {
                    downloadedZoneNpcs.Clear();
                    downloadedZoneNpcs.AddRange(zoneNpcs);
                    downloadedZoneId = zoneId;
                }

                log.Debug($"AkuAPI Download: Cached {zoneNpcs.Count} zone NPCs with unique ingame ids for zone {zoneId}.");
            }
            catch (Exception ex)
            {
                log.Debug($"AkuAPI Download: Could not refresh zone NPC cache for zone {zoneId}: {ex.Message}");
            }
            finally
            {
                if (downloadingZoneId == zoneId)
                {
                    downloadingZoneId = 0;
                }
            }
        }

        private bool IsKnownDownloadedNpc(AkuGameObject obj)
        {
            if (!IsTrackableNpc(obj))
            {
                return false;
            }

            lock (downloadedZoneNpcsLock)
            {
                return downloadedZoneNpcs.Any(downloaded =>
                    IsExactDownloadedEventNpcMatch(downloaded, obj) ||
                    downloaded.zid == obj.zid &&
                    downloaded.t == obj.t &&
                    downloaded.bid == obj.bid &&
                    downloaded.nid == obj.nid &&
                    IsRoughlySamePosition(downloaded.pos, obj.pos));
            }
        }

        private static bool IsExactDownloadedEventNpcMatch(AkuGameObject downloaded, AkuGameObject current)
        {
            return current.t == "EventNpc" &&
                downloaded.t == current.t &&
                downloaded.unique_ingame_id is not null &&
                downloaded.unique_ingame_id == current.unique_ingame_id;
        }

        private static bool IsRoughlySamePosition(Vector3 left, Vector3 right)
        {
            return Vector3.Distance(left, right) <= NpcRoughPositionDistance;
        }

        private bool IsTrackableNpc(IGameObject obj)
        {
            return obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc ||
                obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc;
        }

        private static bool IsTrackableNpc(AkuGameObject obj)
        {
            return obj.t == "EventNpc" || obj.t == "BattleNpc";
        }

        private bool HasSeenNpcInCurrentZone(IGameObject obj)
        {
            return seenNpcIdsByZone.TryGetValue(clientState.TerritoryType, out var seenNpcIds) &&
                seenNpcIds.Contains(obj.GameObjectId);
        }

        private void MarkNpcSeenInCurrentZone(IGameObject obj)
        {
            if (!IsTrackableNpc(obj))
            {
                return;
            }

            if (!seenNpcIdsByZone.TryGetValue(clientState.TerritoryType, out var seenNpcIds))
            {
                seenNpcIds = new HashSet<ulong>();
                seenNpcIdsByZone.Add(clientState.TerritoryType, seenNpcIds);
            }

            seenNpcIds.Add(obj.GameObjectId);
        }

        private bool HasTableContentChanged(IGameObject obj, AkuGameObject akuObj) {
            if(obj.BaseId == akuObj.bid && obj.ObjectKind.ToString() == akuObj.t) {
                return false;
            }
            log.Debug($"Obj changed in table old: {obj.Name}({obj.BaseId}) new: {obj.Name}/{obj.BaseId}");
            return true;
        }
    }
}
