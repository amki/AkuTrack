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

        public Dictionary<ulong, IGameObject> seenList = new();
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
                log.Debug($"Uploading was {res}");
                if (res) {
                    toUpload.Clear();
                }
            }
        }

        private unsafe List<AkuGameObject> LookForNewObjects()
        {
            List<AkuGameObject> objects = new();
            foreach (var obj in objectTable)
            {
                // no players, mounts, minion pets, housing items, wings/umbrellas
                if(obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player ||
                    obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.MountType ||
                    obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion ||
                    obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Housing ||
                    obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Ornament
                    ) {
                    continue;
                }
                if (seenList.ContainsKey(obj.GameObjectId))
                {
                    var oldObj = seenList[obj.GameObjectId];
                    if (oldObj.Position == obj.Position)
                        continue;
                    log.Debug($"Known obj {obj.Name} is now @ x/y/z: {obj.Position.X}/{obj.Position.Y}/{obj.Position.Z}");
                    seenList[obj.GameObjectId] = obj;
                } else {
                    var owner = objectTable.SearchById(obj.OwnerId);
                    log.Debug($"Found new obj {obj.Name} @ x/y/z: {obj.Position.X}/{obj.Position.Y}/{obj.Position.Z}");
                    seenList.Add(obj.GameObjectId, obj);
                    var upObj = new AkuGameObject(obj);
                    upObj.mid = clientState.MapId;
                    upObj.zid = clientState.TerritoryType;
                    
                    if (obj is ICharacter c) {
                        upObj.nid = c.NameId;
                        FFXIVClientStructs.FFXIV.Client.Game.Character.Character* chr = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)c.Address;
                        upObj.moid = chr->ModelContainer.ModelCharaId;
                        upObj.npiid = chr->NamePlateIconId;
                    }

                    if (owner != null && owner.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                    {
                        log.Debug($"Obj {obj.Name} is player owned. Not sending. @ x/y/z: {obj.Position.X}/{obj.Position.Y}/{obj.Position.Z}");
                        continue;
                    } else {
                        objects.Add(upObj);
                    }
                }
            }
            return objects;
        }
    }
}
