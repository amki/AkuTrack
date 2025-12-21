using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace AkuTrack.Managers
{
    internal class ObjTrackManager
    {
        private readonly IChatGui chat;
        private readonly IPluginLog log;
        private readonly IObjectTable objectTable;
        private readonly IFramework framework;
        private readonly IClientState clientState;

        private Dictionary<ulong, Vector3> seenList = new();

        private TimeSpan _lastUpdate = new(0);
        private TimeSpan _execDelay = new(0, 0, 1);

        public ObjTrackManager(
            IFramework framework,
            IClientState clientState,
            IDalamudPluginInterface pluginInterface,
            IChatGui chat,
            IPluginLog log,
            IObjectTable objectTable
        )
        {
            log.Debug($"ObjTrackManager online!");
            this.chat = chat;
            this.log = log;
            this.objectTable = objectTable;
            this.framework = framework;
            this.clientState = clientState;

            framework.Update += Tick;
        }

        private void Tick(IFramework framework)
        {
            _lastUpdate += framework.UpdateDelta;
            if (_lastUpdate > _execDelay)
            {
                DoUpdate(framework);
                _lastUpdate = new(0);
            }
        }

        private unsafe void DoUpdate(IFramework framework)
        {
            log.Debug("Tick!");
            CheckObjectTable();
        }

        private void CheckObjectTable()
        {
            foreach (var obj in objectTable)
            {
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
        }
    }
}
