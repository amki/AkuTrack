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

        private Dictionary<ulong, Vector3> seenList = new();

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

        private unsafe void DoUpdate(IFramework framework)
        {
            //log.Debug("Tick!");
            CheckObjectTable();
        }

        private unsafe void CheckObjectTable()
        {
            List<AkuGameObject> objects = new();
            foreach (var obj in objectTable)
            {
                if(obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player || obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.MountType || obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion) {
                    continue;
                }
                if (seenList.ContainsKey(obj.GameObjectId))
                {
                    var oldPos = seenList[obj.GameObjectId];
                    if (oldPos == obj.Position)
                        continue;
                    log.Debug($"Known obj {obj.Name} is now @ x/y/z: {obj.Position.X}/{obj.Position.Y}/{obj.Position.Z}");
                    seenList[obj.GameObjectId] = obj.Position;
                    
                }
                else {
                    log.Debug($"Found new obj {obj.Name} @ x/y/z: {obj.Position.X}/{obj.Position.Y}/{obj.Position.Z}");
                    seenList.Add(obj.GameObjectId, obj.Position);
                    var upObj = new AkuGameObject(obj);
                    upObj.mid = clientState.MapId;
                    upObj.zid = clientState.TerritoryType;

                    if (obj is ICharacter c) {
                        upObj.nid = c.NameId;
                        FFXIVClientStructs.FFXIV.Client.Game.Character.Character* chr = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)c.Address;
                        upObj.moid = chr->ModelContainer.ModelCharaId;
                    }


                    objects.Add(upObj);
                    /*
                        if (obj is not IBattleNpc mob)
                        {
                            log.Debug($"Found NOT bNpc {obj.Name} @ x/y/z: {obj.Position.X}/{obj.Position.Y}/{obj.Position.Z}");
                        }
                        else
                        {
                            var battlenpc = mob as IBattleNpc;
                            log.Debug($"Found BNpc {mob.Name} @ x/y/z: {obj.Position.X}/{obj.Position.Y}/{obj.Position.Z}");
                        }
                        */
                }
                
            }
            if(objects.Count > 1)
                _ = uploadManager.DoUpload("duckit/", objects);
        }
    }
}
