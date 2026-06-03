using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace AkuTrack.Managers
{
    public class MapStateManager
    {
        private IClientState clientState;
        private IDataManager dataManager;

        private ObjTrackManager objTrackManager;

        public string filterExpression = string.Empty;
        public bool filterEnabled = false;

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

        public async void SwitchMap(uint mapId) {
            currentMap = dataManager.GetExcelSheet<Map>().GetRow(mapId);
            var objs = await objTrackManager.FetchAkuGameObjectsFromAkuAPI(mapId);
            objTrackManager.downloadHashList.Clear();
            foreach (var obj in objs)
            {
                var uniqueId = obj.GetUniqueId();
                if (uniqueId is not null)
                {
                    objTrackManager.downloadHashList.TryAdd(uniqueId, obj);
                }
            }
        }
    }
}
