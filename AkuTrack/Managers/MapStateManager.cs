using AkuTrack.ApiTypes;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace AkuTrack.Managers
{
    public class MapStateManager
    {
        private IClientState clientState;
        private IDataManager dataManager;

        private ObjTrackManager objTrackManager;

        public Map currentMap { get; private set; }

        public MapStateManager(
        IClientState clientState,
        IDataManager dataManager,
        ObjTrackManager objTrackManager
        ) {
            this.clientState = clientState;
            this.dataManager = dataManager;
            this.objTrackManager = objTrackManager;
            SwitchMap(clientState.MapId);
        }

        public void SwitchMap(uint mapId) {
            currentMap = dataManager.GetExcelSheet<Map>().GetRow(mapId);
            objTrackManager.FetchAkuGameObjectsFromAkuAPI(mapId);
        }
    }
}
