using AkuTrack.ApiTypes;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
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
    public class ObjTrackManager : IDisposable
    {
        private readonly record struct ObjectSnapshot(
            Dalamud.Game.ClientState.Objects.Enums.ObjectKind ObjectKind,
            string ObjectKindName,
            string Name,
            uint BaseId,
            ulong GameObjectId,
            ulong OwnerId,
            ushort ObjectIndex,
            nint Address,
            Vector3 Position,
            float Rotation,
            float HitboxRadius,
            uint? NameId,
            int ModelCharaId,
            uint? NamePlateIconId);

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
        private bool updateInProgress;

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

        public void Dispose()
        {
            framework.Update -= Tick;
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
            if (updateInProgress)
            {
                return;
            }

            updateInProgress = true;
            try
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
            catch (Exception ex)
            {
                log.Error(ex, "ObjTrackManager update failed.");
            }
            finally
            {
                updateInProgress = false;
            }
        }

        private List<AkuGameObject> LookForNewObjects()
        {
            List<AkuGameObject> objects = new();
            foreach (var obj in objectTable)
            {
                if (obj is null)
                {
                    continue;
                }

                if (TryGetNewObject(obj, out var upObj))
                {
                    objects.Add(upObj);
                }
            }

            return objects;
        }

        private bool TryGetNewObject(IGameObject obj, out AkuGameObject upObj)
        {
            upObj = null!;
            try
            {
                var snapshot = CreateSnapshot(obj);

                // no players, mounts, minion pets, housing items, wings/umbrellas, retainers
                if(snapshot.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc ||
                    snapshot.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Mount ||
                    snapshot.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion ||
                    snapshot.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.HousingEventObject ||
                    snapshot.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Ornament ||
                    snapshot.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Retainer
                    ) {
                    return false;
                }
                var uid = AkuGameObject.GetUniqueId(snapshot.ObjectKindName, snapshot.BaseId, snapshot.Position, snapshot.NameId);
                if(uid == null ) {
                    log.Debug($"ERROR: Could not GetUniqueId from obj.bid {snapshot.BaseId} name {snapshot.Name}");
                    return false;
                }
                // Check if this object has already been sent by us
                if (seenList.ContainsKey(uid))
                {
                    var oldObj = seenList[uid];
                    if (uid != oldObj.GetUniqueId())
                    {
                        log.Error($"Something terrible happened! -> Check {uid} but seenList fetches {oldObj.GetUniqueId()}");
                    }
                    if (snapshot.ObjectKindName != oldObj.t || snapshot.BaseId != oldObj.bid)
                    {
                        log.Error($"Something terrible happened! oldObj and obj:");
                        log.Error($"{oldObj.t} || {snapshot.ObjectKindName}");
                        log.Error($"{oldObj.mid} || {clientState.MapId}");
                        log.Error($"{oldObj.bid} || {snapshot.BaseId}");
                    }
                    if(snapshot.NameId is not null) {
                        if(snapshot.NameId != oldObj.nid) {
                            log.Error($"Something terrible happened! oldObj and obj:");
                            log.Error($"{oldObj.nid} || {snapshot.NameId}");
                        }
                    }
                    return false;
                }
                // Check if this object is owned by a player (e.g. a battlepet) or has been aggroed
                if (snapshot.OwnerId != 0)
                {
                    var owner = objectTable.SearchById(snapshot.OwnerId);
                    if (owner != null && owner.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc)
                    {
                        log.Debug($"Obj {snapshot.Name} [{snapshot.BaseId}] is player owned. Not sending. @ x/y/z: {snapshot.Position.X}/{snapshot.Position.Y}/{snapshot.Position.Z}");
                        return false;
                    }
                }

                if (IsTrackableNpc(snapshot.ObjectKind) && HasSeenNpcInCurrentZone(snapshot.GameObjectId))
                {
                    return false;
                }

                upObj = new AkuGameObject(
                    snapshot.ObjectKindName,
                    snapshot.Position,
                    snapshot.BaseId,
                    snapshot.GameObjectId,
                    snapshot.HitboxRadius,
                    snapshot.Name,
                    snapshot.Rotation,
                    snapshot.NameId,
                    snapshot.ModelCharaId,
                    snapshot.NamePlateIconId,
                    clientState);

                if (IsKnownDownloadedNpc(upObj))
                {
                    log.Debug($"Skipping upload for known downloaded {upObj.t} bid {upObj.bid} nid {upObj.nid} gameId {upObj.unique_ingame_id} zone {upObj.zid} map {upObj.mid} at {upObj.pos.X:F1}/{upObj.pos.Y:F1}/{upObj.pos.Z:F1}.");
                    MarkNpcSeenInCurrentZone(snapshot.ObjectKind, snapshot.GameObjectId);
                    return false;
                }

                // Check if there has been an object in this slot already and if it is likely still the same one but has moved (moving changes the uid hash)
                if (seenObjTable.ContainsKey(snapshot.GameObjectId) && !HasTableContentChanged(snapshot, upObj)) {
                    //log.Debug($"Obj {obj.GameObjectId} has moved but was already sent.");
                    return false;
                }
                seenList.Add(uid, upObj);
                MarkNpcSeenInCurrentZone(snapshot.ObjectKind, snapshot.GameObjectId);
                // Remove here because it could also be that the go was here and changed
                seenObjTable.Remove(snapshot.GameObjectId);
                seenObjTable.Add(snapshot.GameObjectId, upObj);
                return true;
            }
            catch (Exception ex)
            {
                log.Warning(ex, $"Skipping unstable object while tracking. {GetObjectDebugInfo(obj)}");
                return false;
            }
        }

        private ObjectSnapshot CreateSnapshot(IGameObject obj)
        {
            var objectKind = obj.ObjectKind;
            uint? nameId = obj is ICharacter c ? c.NameId : null;
            var nativeCharacterData = TryReadNativeCharacterData(obj);
            return new ObjectSnapshot(
                objectKind,
                objectKind.ToString(),
                obj.Name.ToString(),
                obj.BaseId,
                obj.GameObjectId,
                obj.OwnerId,
                obj.ObjectIndex,
                obj.Address,
                obj.Position,
                obj.Rotation,
                obj.HitboxRadius,
                nameId,
                nativeCharacterData.ModelCharaId,
                nativeCharacterData.NamePlateIconId);
        }

        private unsafe (int ModelCharaId, uint? NamePlateIconId) TryReadNativeCharacterData(IGameObject obj)
        {
            if (obj is not ICharacter || obj.Address == nint.Zero)
            {
                return default;
            }

            if (obj.ObjectIndex >= objectTable.Length || objectTable.GetObjectAddress(obj.ObjectIndex) != obj.Address)
            {
                return default;
            }

            var chr = (Character*)obj.Address;
            return (chr->ModelContainer.ModelCharaId, chr->NamePlateIconId);
        }

        private static string GetObjectDebugInfo(IGameObject obj)
        {
            try
            {
                return $"Kind: {obj.ObjectKind}, BaseId: {obj.BaseId}, GameObjectId: {obj.GameObjectId}.";
            }
            catch
            {
                return "Object details were unavailable.";
            }
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
            return IsTrackableNpc(obj.ObjectKind);
        }

        private static bool IsTrackableNpc(Dalamud.Game.ClientState.Objects.Enums.ObjectKind objectKind)
        {
            return objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc ||
                objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc;
        }

        private static bool IsTrackableNpc(AkuGameObject obj)
        {
            return obj.t == "EventNpc" || obj.t == "BattleNpc";
        }

        private bool HasSeenNpcInCurrentZone(IGameObject obj)
        {
            return HasSeenNpcInCurrentZone(obj.GameObjectId);
        }

        private bool HasSeenNpcInCurrentZone(ulong gameObjectId)
        {
            return seenNpcIdsByZone.TryGetValue(clientState.TerritoryType, out var seenNpcIds) &&
                seenNpcIds.Contains(gameObjectId);
        }

        private void MarkNpcSeenInCurrentZone(IGameObject obj)
        {
            MarkNpcSeenInCurrentZone(obj.ObjectKind, obj.GameObjectId);
        }

        private void MarkNpcSeenInCurrentZone(Dalamud.Game.ClientState.Objects.Enums.ObjectKind objectKind, ulong gameObjectId)
        {
            if (!IsTrackableNpc(objectKind))
            {
                return;
            }

            if (!seenNpcIdsByZone.TryGetValue(clientState.TerritoryType, out var seenNpcIds))
            {
                seenNpcIds = new HashSet<ulong>();
                seenNpcIdsByZone.Add(clientState.TerritoryType, seenNpcIds);
            }

            seenNpcIds.Add(gameObjectId);
        }

        private bool HasTableContentChanged(ObjectSnapshot obj, AkuGameObject akuObj) {
            if(obj.BaseId == akuObj.bid && obj.ObjectKindName == akuObj.t) {
                return false;
            }
            log.Debug($"Obj changed in table old: {obj.Name}({obj.BaseId}) new: {obj.Name}/{obj.BaseId}");
            return true;
        }
    }
}
