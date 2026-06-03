using AkuTrack.ApiTypes;
using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Data.Files;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace AkuTrack.Windows;

public class MapWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly MapStateManager mapStateManager;
    private readonly ObjTrackManager objTrackManager;
    private readonly UploadManager uploadManager;
    private readonly WindowSystem windowSystem;
    private readonly IDataManager dataManager;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly ITextureProvider textureProvider;
    private readonly ITextureSubstitutionProvider textureSubstitutionProvider;

    private float Scale { get; set; } = 1;
    public Vector2 DrawOffset { get; set; }
    public HoverFlags HoveredFlags { get; private set; }
    public Vector2 DrawPosition { get; private set; }
    private Vector2 lastWindowSize;
    private bool isDragStarted = false;

    private IDalamudTextureWrap? blendedTexture;
    private uint lastRenderedMapId;
    private bool isBlendedTexture;
    private string currentMapBgPath;
    private string currentMapFgPath;
    private uint capturedAgentMapId;
    private string capturedAgentMapBgPath = string.Empty;
    private string capturedAgentMapFgPath = string.Empty;
    private Vector2 capturedAgentRawMapOffset;
    private float capturedAgentMapScaleFactor;

    private Vector2 currentMapPixelSize = new(0, 0);
    private Vector2 currentMapScreenPosition = new(0, 0);

    public float ZoomSpeed = 0.25f;

    private List<AkuGameObject> clickedObjects = new();
    private List<Lumina.Excel.Sheets.MapMarker> clickedMarkers = new();
    private HashSet<uint>? contentFinderTerritoryIds;

    private readonly MapContextMenu mapContextMenu = new();
    private readonly TopBar topBar;
    private readonly BottomBar bottomBar;
    private string currentCursorPositionText = string.Empty;



    public enum IconIds : int {
        Aetheryte = 60453,
        AethernetShard = 60430,
        CompanyChest = 60460,
        MarketBoard = 60570,
        SummoningBell = 60425,
        Treasure = 60354,
        Unknown = 60515,
        EventNpc = 60424,
        EventObj = 60353,
        BattleNpc = 60422,
        Hover = 60429,
        MapChanger = 60441
    }

    public bool IsMapMarker(int iconid) {
        if (iconid == (int)IconIds.Aetheryte || iconid == (int)IconIds.AethernetShard || iconid == (int)IconIds.SummoningBell || iconid == (int)IconIds.MarketBoard || iconid == (int)IconIds.CompanyChest)
            return true;
        return false;
    }

    // We give this window a hidden ID using ##.
    // The user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public MapWindow(
        Plugin plugin,
        Configuration configuration,
        MapStateManager mapStateManager,
        ObjTrackManager objTrackManager,
        UploadManager uploadManager,
        TopBar topBar,
        BottomBar bottomBar,
        WindowSystem windowSystem,
        IDataManager dataManager,
        IClientState clientState,
        IObjectTable objectTable,
        ITextureProvider textureProvider,
        ITextureSubstitutionProvider textureSubstitutionProvider,
        IPluginLog log
        )
        : base("AkuTrack - Map##akutrack_map", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        this.mapStateManager = mapStateManager;
        this.configuration = configuration;
        this.log = log;
        this.dataManager = dataManager;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.objTrackManager = objTrackManager;
        this.uploadManager = uploadManager;
        this.topBar = topBar;
        this.bottomBar = bottomBar;
        this.windowSystem = windowSystem;
        this.textureProvider = textureProvider;
        this.textureSubstitutionProvider = textureSubstitutionProvider;
        this.currentMapBgPath = "";
        this.currentMapFgPath = "";
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() { }

    public unsafe void CaptureSelectedMapFromAgent()
    {
        var agentMap = AgentMap.Instance();
        if (agentMap == null || agentMap->SelectedMapId == 0)
        {
            return;
        }

        capturedAgentMapId = agentMap->SelectedMapId;
        capturedAgentMapFgPath = agentMap->SelectedMapPath.ToString();
        capturedAgentMapBgPath = agentMap->SelectedMapBgPath.ToString();
        capturedAgentRawMapOffset = new Vector2(agentMap->SelectedOffsetX * -1, agentMap->SelectedOffsetY * -1);
        capturedAgentMapScaleFactor = agentMap->SelectedMapSizeFactorFloat;
        mapStateManager.SwitchMap(agentMap->SelectedMapId);
    }

    public override void OnOpen() {

        if (!configuration.CenterOnPlayerWhenOpening)
        {
            return;
        }

        CenterOnLocalPlayer();
    }

    public override void Draw()
    {
        UpdateDrawOffset();

        HoveredFlags = HoverFlags.Nothing;

        if (IsBoundedBy(ImGui.GetMousePos(), ImGui.GetCursorScreenPos(), ImGui.GetCursorScreenPos() + ImGui.GetContentRegionMax()))
        {
            HoveredFlags |= HoverFlags.Window;
        }

        topBar.Draw(GetCurrentMapDisplayPath(), GetTopBarCursorPositionText());

        using (var childStyle = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 0.0f))
        using (var renderChild = ImRaii.Child("render_child", GetMapCanvasSize(), false, ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar))
        {
            currentMapScreenPosition = ImGui.GetWindowPos();
            DrawMapElements();
            currentMapPixelSize = ImGui.GetWindowSize();

            if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                HoveredFlags |= HoverFlags.WindowInnerFrame;
            }
        }

        bottomBar.Draw(HoveredFlags.HasFlag(HoverFlags.MapTexture), currentMapPixelSize, DrawPosition, DrawOffset, Scale, GetBottomBarPlayerPositionText());
        ProcessInputs();
    }

    private string GetCurrentMapDisplayPath()
    {
        var placeName = mapStateManager.currentMap.PlaceName.ValueNullable?.Name.ToString();
        var territoryName = mapStateManager.currentMap.TerritoryType.ValueNullable?.PlaceName.ValueNullable?.Name.ToString();

        if (!string.IsNullOrWhiteSpace(territoryName) && !string.Equals(territoryName, placeName, StringComparison.CurrentCultureIgnoreCase))
        {
            return $"{territoryName} / {placeName}";
        }

        return !string.IsNullOrWhiteSpace(placeName)
            ? placeName
            : $"Map {mapStateManager.currentMap.RowId}";
    }

    private string GetTopBarCursorPositionText()
    {
        if (IsMouseInsideMapCanvas())
        {
            var cursor = TexturePixelToIngameCoord(GetMouseMapCoordinate());
            currentCursorPositionText = $"X:{cursor.X:F1} Y:{cursor.Y:F1}";
        }

        return currentCursorPositionText;
    }

    private Vector2 GetMapCanvasSize()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var footerHeight = 30.0f * scale;
        var available = ImGui.GetContentRegionAvail();
        return new Vector2(available.X, MathF.Max(120.0f * scale, available.Y - footerHeight));
    }

    private string GetBottomBarPlayerPositionText()
    {
        return mapStateManager.currentMap.RowId == clientState.MapId && objectTable.LocalPlayer is { } player
            ? FormatPlayerMapPosition(player.Position)
            : string.Empty;
    }

    private void DrawMapElements() {
        DrawContextMenu();
        DrawMapBackground();
        if (ImGui.IsItemHovered())
        {
            HoveredFlags |= HoverFlags.MapTexture;
        }

        var drawPlayerMarkersInBackground = ImGui.GetIO().KeyCtrl;
        if (drawPlayerMarkersInBackground)
        {
            DrawPlayerAndCone();
        }

        DrawAkuObjects();
        DrawMapMarkers();

        if (!drawPlayerMarkersInBackground)
        {
            DrawPlayerAndCone();
        }
    }

    private void DrawMapBackground() {
        if (mapStateManager.currentMap.RowId != lastRenderedMapId)
        {
            var idSplits = mapStateManager.currentMap.Id.ToString().Split('/');
            currentMapBgPath = $"ui/map/{idSplits[0]}/{idSplits[1]}/{idSplits[0]}{idSplits[1]}m_m.tex";
            currentMapFgPath = $"ui/map/{idSplits[0]}/{idSplits[1]}/{idSplits[0]}{idSplits[1]}_m.tex";
            // FIXME: ARR housing areas have black bg textures that need to be ignored...
            if (mapStateManager.currentMap.RowId == 192 || mapStateManager.currentMap.RowId == 193 || mapStateManager.currentMap.RowId == 194)
                currentMapBgPath = "";
            //log.Debug($"Drawing map BG: {mapBgPath} || FG: {mapFgPath}");
            //log.Debug($"OG Paths BG: {AgentMap.Instance()->SelectedMapBgPath} || FG: {AgentMap.Instance()->SelectedMapPath}");
            blendedTexture?.Dispose();
            var loadedTexture = LoadTexture(currentMapBgPath, currentMapFgPath);
            if (loadedTexture is not null)
            {
                isBlendedTexture = true;
                blendedTexture = loadedTexture;
            } else {
                isBlendedTexture = false;
            }
        }

        IDalamudTextureWrap? currentTexture = null;

        if(isBlendedTexture) {
            currentTexture = blendedTexture;
        } else {
            currentTexture = textureProvider.GetFromGame(currentMapFgPath).GetWrapOrEmpty();

        }
        if(currentTexture is null) {
            log.Debug("Trying to draw null texture... Skip!");
            return;
        }
        ImGui.SetCursorPos(DrawPosition);
        ImGui.Image(currentTexture.Handle, currentTexture.Size * Scale);
        lastRenderedMapId = mapStateManager.currentMap.RowId;
    }

    private unsafe void RefreshCapturedAgentMapTransform()
    {
        var agentMap = AgentMap.Instance();
        if (agentMap == null || agentMap->SelectedMapId != mapStateManager.currentMap.RowId)
        {
            return;
        }

        var foregroundPath = agentMap->SelectedMapPath.ToString();
        var backgroundPath = agentMap->SelectedMapBgPath.ToString();
        if (string.IsNullOrWhiteSpace(foregroundPath) && string.IsNullOrWhiteSpace(backgroundPath))
        {
            return;
        }

        capturedAgentMapId = agentMap->SelectedMapId;
        capturedAgentMapFgPath = foregroundPath;
        capturedAgentMapBgPath = backgroundPath;
        capturedAgentRawMapOffset = new Vector2(agentMap->SelectedOffsetX * -1, agentMap->SelectedOffsetY * -1);
        capturedAgentMapScaleFactor = agentMap->SelectedMapSizeFactorFloat;
    }



    private IDalamudTextureWrap? LoadTexture(string bgPath, string fgPath)
    {

        var bgFile = GetTexFile(bgPath);
        var fgFile = GetTexFile(fgPath);

        if (bgFile is null || fgFile is null)
        {
            return null;
        }

        var backgroundBytes = bgFile.GetRgbaImageData();
        var foregroundBytes = fgFile.GetRgbaImageData();

        // Blend textures together
        Parallel.For(0, 2048 * 2048, i =>
        {
            var index = i * 4;

            // Blend, R, G, B, skip A.
            backgroundBytes[index + 0] = (byte)(backgroundBytes[index + 0] * foregroundBytes[index + 0] / 255);
            backgroundBytes[index + 1] = (byte)(backgroundBytes[index + 1] * foregroundBytes[index + 1] / 255);
            backgroundBytes[index + 2] = (byte)(backgroundBytes[index + 2] * foregroundBytes[index + 2] / 255);
        });

        return textureProvider.CreateFromRaw(RawImageSpecification.Rgba32(2048, 2048), backgroundBytes);
    }

    private TexFile? GetTexFile(string rawPath)
    {
        var path = textureSubstitutionProvider.GetSubstitutedPath(rawPath);

        if (Path.IsPathRooted(path))
        {
            return dataManager.GameData.GetFileFromDisk<TexFile>(path);
        }

        return dataManager.GetFile<TexFile>(path);
    }

    private void DrawTooltip(AkuGameObject obj)
    {
        ImGui.SetTooltip($"Created: {obj.created_at}\nLastSeen: {obj.lastseen_at}\n\nName: {obj.name}\nType: {obj.t}\nBaseID: {obj.bid}");
    }

    private void DrawContextMenu()
    {
        if (clickedObjects.Count > 0 || clickedMarkers.Count > 0)
        {
            DrawAkuObjectContextMenu(clickedObjects, clickedMarkers);
            if (!ImGui.IsPopupOpen("AkuTrack_AkuObject_Context_Menu"))
            {
                if (clickedObjects.Count > 0)
                    clickedObjects.Clear();
                if (clickedMarkers.Count > 0)
                    clickedMarkers.Clear();
            }
        }
    }

    private void DrawAkuObjects()
    {
        var scope = GetCurrentContentScope();
        if (ShouldDrawContent("RemoteMarker", scope))
        {
            foreach (var o in objTrackManager.downloadHashList)
            {
                if (!objTrackManager.seenHashList.ContainsKey(o.Key))
                    DrawAkuGameObject(o.Value, MapObjectSource.Downloaded, scope);
            }
        }

        if (mapStateManager.currentMap.RowId == clientState.MapId)
        {
            foreach (var o in objTrackManager.liveAkuObjects)
            {
                DrawAkuGameObject(o, MapObjectSource.SelfFound, scope);
            }
        }
    }

    private void DrawPlayerAndCone()
    {
        // Only draw player and from ObjectTable if we are looking at the map we are currently in
        if (mapStateManager.currentMap.RowId == clientState.MapId)
        {
            if (objectTable.LocalPlayer is { } localPlayer)
            {
                if (configuration.DrawCameraCone)
                {
                    DrawCameraCone(localPlayer.Position);
                }

                DrawPlayerIcon(localPlayer.Position, localPlayer.Rotation);
            }

        }
    }

    private void DrawMapMarkers() {
        try
        {
            var scope = GetCurrentContentScope();
            var rows = dataManager.GetSubrowExcelSheet<Lumina.Excel.Sheets.MapMarker>().GetRow(mapStateManager.currentMap.MapMarkerRange);
            foreach (var row in rows)
            {
                if (row.X == 0 && row.Y == 0)
                {
                    continue;
                }
                if (!ShouldDrawMapMarker(row.Icon, scope))
                {
                    continue;
                }
                if (mapStateManager.filterEnabled && mapStateManager.filterExpression != string.Empty)
                {
                    bool doDraw = false;
                    /*
                    if (row.DataKey.TryGetValue<Lumina.Excel.Sheets.PlaceName>(out var rowPlaceName)) {
                        if (mapStateManager.filterExpression.Contains(rowPlaceName.Name.ToString()))
                            doDraw = true;
                    }
                    */
                    if (row.PlaceNameSubtext.Value.Name.ToString().Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase))
                    {
                        doDraw = true;
                    }
                    if (!doDraw)
                        continue;
                }
                var pos = new Vector2(row.X, row.Y);
                //log.Debug($"Icon {row.Icon} to {pos} {row.RowOffset} |{row.PlaceNameSubtext.Value.Name}|");
                DrawMapIcon(row.Icon, pos, 3.14f, row.PlaceNameSubtext.Value.Name.ToString(), row.SubtextOrientation);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
                    AddClickedMarker(row);
                    AddNearbyElementsToSelection(ImGui.GetMousePos());
                }
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // FIXME: How to get markers from region maps?!?
            //log.Debug($"Could not find Markers for Territory {currentTerritory}");
        }
    }

    private void DrawAkuGameObject(AkuGameObject obj, MapObjectSource source, MapContentScope scope) {
        if (obj.mid != mapStateManager.currentMap.RowId)
            return;
        if(!ShouldDrawObjectKind(obj.objectKind, source, scope)) {
            return;
        }
        if (IsLocalPlayerObject(obj))
        {
            return;
        }
        if(mapStateManager.filterEnabled && mapStateManager.filterExpression != string.Empty) {
            bool doDraw = false;
            if ((obj.name?.Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
            obj.t.Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase) ||
            obj.bid.ToString().Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase))
            {
                doDraw = true;
            }
            if(obj.nid is not null && obj.nid.Value.ToString().Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase)) {
                doDraw = true;
            }
            if (obj.npiid is not null && obj.npiid.Value.ToString().Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase))
            {
                doDraw = true;
            }
            if (!doDraw)
                return;
        }
        var handledClickAndHover = false;
        if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc)
        {
            if (ShouldHideDownloadedNpcWithoutUniqueIngameId(obj, source))
                return;
            DrawIcon((int)IconIds.EventNpc, obj);
        }
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj)
        {
            if (!configuration.IsObjectSourceEnabled(scope, "EventObj", source))
                return;
            var iconId = GetEventObjIconId(obj.bid);
            if (!configuration.IsIconCategoryEntryEnabled(scope, "EventObj", iconId))
                return;
            if (obj.bid == 2000401) // summoning bell
                DrawIcon((int)IconIds.SummoningBell, obj);
            else if (obj.bid == 2000402) // market board
                DrawIcon((int)IconIds.MarketBoard, obj);
            else if (obj.bid == 2000470) // company chest
                DrawIcon((int)IconIds.CompanyChest, obj);
            else
                DrawIcon((int)IconIds.EventObj, obj);
        }
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc)
        {
            if (ShouldHideDownloadedNpcWithoutUniqueIngameId(obj, source))
                return;
            DrawIcon((int)IconIds.BattleNpc, obj);
        }
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Aetheryte)
        {
            if(dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>().TryGetRow(obj.bid, out var aetheryte)) {
                if (aetheryte.AethernetName.Value.Name.ToString() != string.Empty && aetheryte.PlaceName.Value.Name.ToString() == string.Empty)
                {
                    DrawIcon((int)IconIds.AethernetShard, obj);
                } else {
                    DrawIcon((int)IconIds.Aetheryte, obj);
                }
            }
        }
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.GatheringPoint)
        {
            if (!dataManager.GetExcelSheet<Lumina.Excel.Sheets.GatheringPoint>().TryGetRow(obj.bid, out var gatheringPointRow))
            {
                log.Debug($"GatheringPoint {obj.bid} did not have a row in GatheringPoint sheet.");
                return;
            }
            var iconId = (uint)gatheringPointRow.GatheringPointBase.Value.GatheringType.Value.IconMain;
            if (!configuration.IsIconCategoryEntryEnabled(scope, "GatheringPoint", iconId))
                return;
            DrawIcon((int)iconId, obj);
        }
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure)
            DrawIcon((int)IconIds.Treasure, obj);
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc)
            handledClickAndHover = DrawActorDot(obj);
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion)
            handledClickAndHover = DrawActorDot(obj);
        else
            DrawIcon((int)IconIds.Unknown, obj);
        if (handledClickAndHover)
        {
            return;
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
            clickedObjects.Add(obj);
        }
        if (ImGui.IsItemHovered())
        {
            DrawTooltip(obj);
            if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc ||
                obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion)
            {
                DrawActorDot(obj, true, false);
            }
            else
            {
                DrawIcon((int)IconIds.Hover, obj);
            }
        }
    }

    private bool ShouldDrawObjectKind(Dalamud.Game.ClientState.Objects.Enums.ObjectKind objectKind, MapObjectSource source, MapContentScope scope)
    {
        var category = GetObjectKindCategory(objectKind);
        if (category is null)
        {
            return objectKind switch
            {
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Aetheryte => true,
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc => configuration.DrawOtherPlayers,
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion => configuration.DrawOtherPlayers,
                _ => true,
            };
        }

        return configuration.IsObjectSourceEnabled(scope, category, source) && ShouldDrawContent(category, scope);
    }

    private static string? GetObjectKindCategory(Dalamud.Game.ClientState.Objects.Enums.ObjectKind objectKind)
    {
        return objectKind switch
        {
            Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc => "EventNpc",
            Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj => "EventObj",
            Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc => "BattleNpc",
            Dalamud.Game.ClientState.Objects.Enums.ObjectKind.GatheringPoint => "GatheringPoint",
            Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure => "Treasure",
            _ => null,
        };
    }

    private bool ShouldHideDownloadedNpcWithoutUniqueIngameId(AkuGameObject obj, MapObjectSource source)
    {
        return source == MapObjectSource.Downloaded
            && configuration.OnlyDrawDownloadedNpcsWithUniqueIngameId
            && (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc ||
                obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc)
            && obj.unique_ingame_id is null;
    }

    private MapContentScope GetCurrentContentScope()
    {
        var territoryId = mapStateManager.currentMap.TerritoryType.RowId;
        return IsContentFinderTerritory(territoryId) ? MapContentScope.ContentFinder : MapContentScope.World;
    }

    private bool IsContentFinderTerritory(uint territoryId)
    {
        if (contentFinderTerritoryIds is null)
        {
            contentFinderTerritoryIds = dataManager.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>()
                .Where(row => row.RowId != 0 && row.TerritoryType.RowId != 0)
                .Select(row => row.TerritoryType.RowId)
                .ToHashSet();
        }

        return territoryId != 0 && contentFinderTerritoryIds.Contains(territoryId);
    }

    private bool ShouldDrawContent(string category, MapContentScope scope)
    {
        return scope switch
        {
            MapContentScope.World => category switch
            {
                "BattleNpc" => configuration.DrawBNpc,
                "CriticalEngagements" => configuration.DrawCriticalEngagements,
                "EventNpc" => configuration.DrawENpc,
                "EventObj" => configuration.DrawEObj,
                "FATE" => configuration.DrawFates,
                "GatheringPoint" => configuration.DrawGatheringPoint,
                "HousingMapMarkerInfo" => configuration.DrawHousingMapMarkers,
                "MapMarkerLabelsOnly" => configuration.DrawMapMarkerLabelsOnly,
                "MapMarkersWithIcons" => configuration.DrawMapMarkersWithIcons,
                "RemoteMarker" => configuration.DrawRemoteMarker,
                "SightseeingLog" => configuration.DrawSightseeingLogEntries,
                "Treasure" => configuration.DrawTreasure,
                "TreasureMaps" => configuration.DrawTreasureMaps,
                _ => true,
            },
            MapContentScope.ContentFinder => category switch
            {
                "BattleNpc" => configuration.DrawContentFinderBNpc,
                "CriticalEngagements" => configuration.DrawContentFinderCriticalEngagements,
                "EventNpc" => configuration.DrawContentFinderENpc,
                "EventObj" => configuration.DrawContentFinderEObj,
                "FATE" => configuration.DrawContentFinderFates,
                "GatheringPoint" => configuration.DrawContentFinderGatheringPoint,
                "HousingMapMarkerInfo" => configuration.DrawContentFinderHousingMapMarkers,
                "MapMarkerLabelsOnly" => configuration.DrawContentFinderMapMarkerLabelsOnly,
                "MapMarkersWithIcons" => configuration.DrawContentFinderMapMarkersWithIcons,
                "RemoteMarker" => configuration.DrawContentFinderRemoteMarker,
                "SightseeingLog" => configuration.DrawContentFinderSightseeingLogEntries,
                "Treasure" => configuration.DrawContentFinderTreasure,
                "TreasureMaps" => configuration.DrawContentFinderTreasureMaps,
                _ => true,
            },
            _ => true,
        };
    }

    private bool ShouldDrawMapMarker(uint iconId, MapContentScope scope)
    {
        var category = IsRegionIcon((int)iconId) ? "MapMarkerLabelsOnly" : "MapMarkersWithIcons";
        return ShouldDrawContent(category, scope);
    }

    private static uint GetEventObjIconId(uint baseId)
    {
        return baseId switch
        {
            2000401 => (uint)IconIds.SummoningBell,
            2000402 => (uint)IconIds.MarketBoard,
            2000470 => (uint)IconIds.CompanyChest,
            2007457 => 60033,
            _ => (uint)IconIds.EventObj,
        };
    }

    private bool IsLocalPlayerObject(AkuGameObject obj)
    {
        return obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc &&
            objectTable.LocalPlayer is { } localPlayer &&
            obj.unique_ingame_id == localPlayer.GameObjectId;
    }

    public void DrawAkuObjectContextMenu(List<AkuGameObject> objs, List<Lumina.Excel.Sheets.MapMarker> markers)
    {
        using var contextMenu = ImRaii.ContextPopup("AkuTrack_AkuObject_Context_Menu");
        if (!contextMenu) return;

        foreach (var obj in objs)
        {
            if (ImGui.MenuItem($"{obj.t} {obj.name}({obj.bid})"))
            {
                string newName = $"akutrack_details_{obj.bid}";
                foreach (var w in windowSystem.Windows)
                {
                    var wName = w.WindowName.Split("##")[1];
                    if (wName == newName)
                        return;
                }
                var dw = ActivatorUtilities.CreateInstance<DetailsWindow>(plugin.serviceProvider, new object[] { obj });
                windowSystem.AddWindow(dw);
                dw.Toggle();
            }
        }
        foreach(var mark in markers) {
            if(ImGui.MenuItem($"MapMarker ({mark.RowId}.{mark.SubrowId}) {mark.PlaceNameSubtext.Value.Name.ToString()}")) {
                if (mark.DataKey.TryGetValue<Lumina.Excel.Sheets.Map>(out var dataKeyMap))
                {
                    log.Debug($"Found map {dataKeyMap.PlaceName.Value.Name.ToString()}");
                    mapStateManager.SwitchMap(dataKeyMap.RowId);
                } else {
                    log.Debug("Tut nix beim klicken, sorry.");
                }
            }
        }
    }

    private void DrawIcon(int iconid, AkuGameObject obj)
    {
        var texture = textureProvider.GetFromGameIcon(iconid).GetWrapOrEmpty();

        var p = ((GetMapCoordinateFor3D(obj.pos)) * Scale) + DrawPosition - (texture.Size / 4.0f);

        if (configuration.DrawDebugSquares)
        {
            ImGui.SetCursorPos(p);
            var cursorPos = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddRect(cursorPos, cursorPos + (texture.Size / 2.0f), ImGui.GetColorU32(configuration.TextColor), 3.0f);
        }
        ImGui.SetCursorPos(p);
        //log.Debug($"@ {position} Drawing to {p} with scale {Scale} DrawPosition: {DrawPosition}");
        if (obj.isDownloaded && !IsMapMarker(iconid))
            ImGui.Image(texture.Handle, texture.Size / 2.0f, Vector2.Zero, Vector2.One, new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
        else
            ImGui.Image(texture.Handle, texture.Size / 2.0f, Vector2.Zero, Vector2.One, new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
    }

    private bool DrawActorDot(AkuGameObject obj, bool hover = false, bool interactive = true)
    {
        var center = currentMapScreenPosition +
                     DrawPosition +
                     (GetPlayerMapPosition(obj.pos) +
                      GetMapOffsetVector() +
                      GetMapCenterOffsetVector()) * Scale;
        var isFriend = IsFriendPlayer(obj);
        var radius = hover ? 5.0f : 4.0f;
        var hitRadius = MathF.Max(radius + 3.0f, 8.0f);
        var fillColor = ImGui.GetColorU32(GetActorDotColor(obj, isFriend));
        var borderColor = ImGui.GetColorU32(new Vector4(0.02f, 0.025f, 0.03f, 0.85f));
        var isHovered = false;

        if (interactive)
        {
            ImGui.SetCursorScreenPos(center - new Vector2(hitRadius));
            ImGui.InvisibleButton($"##actor_dot_{obj.objectKind}_{obj.unique_ingame_id}_{obj.uuid}", new Vector2(hitRadius * 2.0f));
            isHovered = ImGui.IsItemHovered();
        }

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddCircleFilled(center, radius, fillColor, 16);
        drawList.AddCircle(center, radius, borderColor, 16, MathF.Max(1.0f, ImGuiHelpers.GlobalScale));

        if (!interactive)
        {
            return false;
        }

        if (isHovered)
        {
            DrawTooltip(obj);
            DrawActorDot(obj, true, false);
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
            AddClickedObject(obj);
            AddNearbyElementsToSelection(ImGui.GetMousePos());
        }

        return true;
    }

    private void AddNearbyElementsToSelection(Vector2 screenPosition)
    {
        const float selectionRadius = 16.0f;
        var scope = GetCurrentContentScope();

        foreach (var obj in objTrackManager.liveAkuObjects)
        {
            if (IsObjectSelectableNear(obj, MapObjectSource.SelfFound, scope, screenPosition, selectionRadius))
            {
                AddClickedObject(obj);
            }
        }

        if (ShouldDrawContent("RemoteMarker", scope))
        {
            foreach (var obj in objTrackManager.downloadHashList.Values)
            {
                if (IsObjectSelectableNear(obj, MapObjectSource.Downloaded, scope, screenPosition, selectionRadius))
                {
                    AddClickedObject(obj);
                }
            }
        }

        AddNearbyMapMarkersToSelection(screenPosition, selectionRadius, scope);
    }

    private bool IsObjectSelectableNear(AkuGameObject obj, MapObjectSource source, MapContentScope scope, Vector2 screenPosition, float selectionRadius)
    {
        return obj.mid == mapStateManager.currentMap.RowId &&
            !IsLocalPlayerObject(obj) &&
            ShouldDrawObjectKind(obj.objectKind, source, scope) &&
            MatchesMapSearch(obj) &&
            Vector2.Distance(screenPosition, GetMapScreenPosition(obj.pos)) <= selectionRadius;
    }

    private void AddNearbyMapMarkersToSelection(Vector2 screenPosition, float selectionRadius, MapContentScope scope)
    {
        try
        {
            var rows = dataManager.GetSubrowExcelSheet<Lumina.Excel.Sheets.MapMarker>().GetRow(mapStateManager.currentMap.MapMarkerRange);
            foreach (var row in rows)
            {
                if (row.X == 0 && row.Y == 0 || !ShouldDrawMapMarker(row.Icon, scope) || !MatchesMapSearch(row))
                {
                    continue;
                }

                var markerScreenPosition = currentMapScreenPosition + DrawPosition + new Vector2(row.X, row.Y) * Scale;
                if (Vector2.Distance(screenPosition, markerScreenPosition) <= selectionRadius)
                {
                    AddClickedMarker(row);
                }
            }
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private void AddClickedObject(AkuGameObject obj)
    {
        var key = GetObjectSelectionKey(obj);
        if (clickedObjects.Any(clicked => GetObjectSelectionKey(clicked) == key))
        {
            return;
        }

        clickedObjects.Add(obj);
    }

    private static string GetObjectSelectionKey(AkuGameObject obj)
    {
        return $"{obj.objectKind}:{obj.unique_ingame_id}:{obj.uuid}:{obj.bid}:{obj.pos.X:F2}:{obj.pos.Y:F2}:{obj.pos.Z:F2}";
    }

    private void AddClickedMarker(Lumina.Excel.Sheets.MapMarker marker)
    {
        if (clickedMarkers.Any(clicked => clicked.RowId == marker.RowId && clicked.SubrowId == marker.SubrowId))
        {
            return;
        }

        clickedMarkers.Add(marker);
    }

    private bool MatchesMapSearch(AkuGameObject obj)
    {
        if (!mapStateManager.filterEnabled || mapStateManager.filterExpression == string.Empty)
        {
            return true;
        }

        return (obj.name?.Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
            obj.t.Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase) ||
            obj.bid.ToString().Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase) ||
            (obj.nid is not null && obj.nid.Value.ToString().Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase)) ||
            (obj.npiid is not null && obj.npiid.Value.ToString().Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase));
    }

    private bool MatchesMapSearch(Lumina.Excel.Sheets.MapMarker marker)
    {
        return !mapStateManager.filterEnabled ||
            mapStateManager.filterExpression == string.Empty ||
            marker.PlaceNameSubtext.Value.Name.ToString().Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase);
    }

    private Vector2 GetMapScreenPosition(Vector3 position)
    {
        return currentMapScreenPosition +
               DrawPosition +
               (GetPlayerMapPosition(position) +
                GetMapOffsetVector() +
                GetMapCenterOffsetVector()) * Scale;
    }

    private static Vector4 GetActorDotColor(AkuGameObject obj, bool isFriend)
    {
        if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion)
        {
            return new Vector4(1.0f, 0.72f, 0.18f, 0.95f);
        }

        return isFriend
            ? new Vector4(0.1f, 1.0f, 0.55f, 0.95f)
            : new Vector4(0.15f, 0.65f, 1.0f, 0.9f);
    }

    private bool IsFriendPlayer(AkuGameObject obj)
    {
        if (obj.unique_ingame_id is not { } gameObjectId)
        {
            return false;
        }

        return objectTable.SearchById(gameObjectId) is ICharacter character &&
            character.StatusFlags.HasFlag(StatusFlags.Friend);
    }

    private void DrawMapIcon(int iconid, Vector2 position, float rotation, string text, byte subtextOrientation)
    {
        if (IsDoubleHousingArea(iconid))
            return;
        var texture = textureProvider.GetFromGameIcon(iconid).GetWrapOrEmpty();
            //log.Debug($"@ {position} Drawing to {p} with scale {Scale} DrawPosition: {DrawPosition}");
        if (IsRegionIcon(iconid)) {
            var regionScaleFactor = 0.84f;
            // FIXME: Shading of region icons is broken (they are white)
            var p = (position * Scale) + DrawPosition - (texture.Size * regionScaleFactor / 4.0f * Scale);
            ImGui.SetCursorPos(p);
            ImGui.Image(texture.Handle, texture.Size * regionScaleFactor / 2.0f * Scale);
            if(configuration.DrawDebugSquares) {
                ImGui.SetCursorPos(p);
                var cursorPos = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRect(cursorPos, cursorPos + (texture.Size * regionScaleFactor / 2.0f * Scale), ImGui.GetColorU32(configuration.TextColor), 3.0f);
            }
            if (text != string.Empty)
            {
                var ap = p + (texture.Size * regionScaleFactor / 4.0f * Scale);
                ImGui.SetCursorPos(ap);
                ImGui.TextColored(configuration.TextColor, text.ToString());
            }
        } else {
            var p = (position * Scale) + DrawPosition - (texture.Size / 4.0f);
            ImGui.SetCursorPos(p);
            ImGui.Image(texture.Handle, texture.Size / 2.0f);
            if(configuration.DrawDebugSquares)
            {
                ImGui.SetCursorPos(p);
                var cursorPos = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRect(cursorPos, cursorPos + (texture.Size / 2.0f), ImGui.GetColorU32(configuration.TextColor), 3.0f);
            }
            if (text != string.Empty)
            {
                var ap = p;
                // FIXME: Map Icon Text is moved (left of marker, above marker, right of marker etc) on the ingame map whereas we render it just at the marker's position
                /*
                switch(subtextOrientation) {
                    case 1:
                        ap.Y += texture.Size.Y / 2.0f / 4.0f;
                        break;
                    case 2:
                        ap.Y += texture.Size.Y / 2.0f / 4.0f;
                        break;
                    case 3:
                        break;
                    case 4:
                        break;
                    default:
                        break;
                }
                */
                ImGui.SetCursorPos(ap);
                ImGui.TextColored(configuration.TextColor, text.ToString());
            }
        }
    }

    public static bool IsRegionIcon(int iconId) =>
       iconId switch
       {
           >= 63200 and < 63900 => true,
           >= 62620 and < 62800 => true,
           _ => false,
       };

    public static bool IsDoubleHousingArea(int iconId) {
        if (iconId == 63249 /* Goblet */ || iconId == 63210 /* Mist */ || iconId == 63228 /* Lavender Beds */ || iconId == 63383 /* Shirogane */ || iconId == 63266 /* Empyreum */)
            return true;
        return false;
    }

    private void DrawPlayerIcon(Vector3 pos, float rotation)
    {
        var texture = textureProvider.GetFromGameIcon(60443).GetWrapOrEmpty();
        var angle = -rotation + MathF.PI / 2.0f;
        var scaledSize = texture.Size / 2.0f * Scale;
        var minimumSize = new Vector2(36.0f * ImGuiHelpers.GlobalScale);
        var maximumSize = new Vector2(96.0f * ImGuiHelpers.GlobalScale);
        var size = Vector2.Clamp(scaledSize, minimumSize, maximumSize);

        var p = ImGui.GetWindowPos() +
                           DrawPosition +
                           (GetPlayerMapPosition(pos) +
                            GetMapOffsetVector() +
                            GetMapCenterOffsetVector()) * Scale;
        //var p = ((GetMapCoordinateFor3D(pos)) * Scale) + DrawPosition - (texture.Size / 4.0f * Scale);
        var vectors = GetRotationVectors(angle, p, size);

        //log.Debug($"@ {position} Drawing to {p} with scale {Scale} DrawPosition: {DrawPosition}");
        ImGui.GetWindowDrawList().AddImageQuad(texture.Handle, vectors[0], vectors[1], vectors[2], vectors[3]);
    }

    private unsafe void DrawCameraCone(Vector3 pos)
    {
        var cameraManager = CameraManager.Instance();
        if (cameraManager == null)
        {
            return;
        }

        var camera = cameraManager->GetActiveCamera();
        if (camera == null)
        {
            return;
        }

        var center = currentMapScreenPosition +
                     DrawPosition +
                     (GetPlayerMapPosition(pos) +
                      GetMapOffsetVector() +
                      GetMapCenterOffsetVector()) * Scale;

        var angle = -camera->CalculateSceneCameraYaw() + MathF.PI * 1.5f;
        const float halfConeAngle = 75.0f * MathF.PI / 360.0f;
        var coneOrigin = center;
        var coneLength = 43.0f * Scale;
        var left = coneOrigin + AngleToDirection(angle - halfConeAngle) * coneLength;
        var right = coneOrigin + AngleToDirection(angle + halfConeAngle) * coneLength;

        var fillColor = ImGui.GetColorU32(new Vector4(0.05f, 0.75f, 1.0f, 0.28f));
        var lineColor = ImGui.GetColorU32(new Vector4(0.25f, 0.9f, 1.0f, 0.85f));
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddTriangleFilled(coneOrigin, left, right, fillColor);
        drawList.AddTriangle(coneOrigin, left, right, lineColor, MathF.Max(1.0f, Scale));
    }

    private static Vector2 AngleToDirection(float angle)
    {
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    private void ProcessInputs() {
        if (HoveredFlags.Any())
        {
            if (ImGui.GetIO().KeyShift)
            {
                Flags &= ~ImGuiWindowFlags.NoMove;
            }
            else
            {
                ProcessMouseScroll();
                ProcessMapDragStart();
                Flags |= ImGuiWindowFlags.NoMove;
            }
        }
        else
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }
        ProcessMapDragDragging();
        ProcessMapDragEnd();
    }

    private void ProcessMouseScroll()
    {
        if (ImGui.GetIO().MouseWheel is 0) return;
        if (!HoveredFlags.HasFlag(HoverFlags.WindowInnerFrame)) return;

        Scale += ZoomSpeed * ImGui.GetIO().MouseWheel;
        Scale = Math.Clamp(Scale, 0.25f, 100.0f);
    }

    private void ProcessMapDragStart()
    {
        // Don't allow a drag to start if the window size is changing
        if (ImGui.GetWindowSize() == lastWindowSize && HoveredFlags != HoverFlags.Nothing)
        {
            if (HoveredFlags.HasFlag(HoverFlags.MapTexture) && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !isDragStarted)
            {
                isDragStarted = true;
                //System.SystemConfig.FollowPlayer = false;
            }
        }
        else
        {
            lastWindowSize = ImGui.GetWindowSize();
            isDragStarted = false;
        }
    }

    private void ProcessMapDragDragging()
    {
        if (ImGui.IsMouseDragging(ImGuiMouseButton.Left) && isDragStarted)
        {
            DrawOffset += ImGui.GetMouseDragDelta() / Scale;
            ImGui.ResetMouseDragDelta();
        }
    }

    private void ProcessMapDragEnd()
    {
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            isDragStarted = false;
        }
    }

    private void UpdateDrawOffset()
    {
        var childCenterOffset = ImGui.GetContentRegionAvail() / 2.0f;
        var mapCenterOffset = new Vector2(1024.0f, 1024.0f) * Scale;

        DrawPosition = childCenterOffset - mapCenterOffset + (DrawOffset * Scale);
    }

    private void CenterOnLocalPlayer()
    {
        if (objectTable.LocalPlayer is not { } localPlayer)
        {
            return;
        }

        DrawOffset = -(GetPlayerMapPosition(localPlayer.Position) + GetMapOffsetVector());
    }

    private string FormatPlayerMapPosition(Vector3 worldPosition)
    {
        var mapPosition = TexturePixelToIngameCoord(GetMapCoordinateFor3D(worldPosition));
        return $"X:{mapPosition.X:F1} Y:{mapPosition.Y:F1} Z:{worldPosition.Y:F1}";
    }

    private Vector2 GetMouseMapCoordinate()
    {
        return (ImGui.GetMousePos() - currentMapScreenPosition - DrawPosition) / Scale;
    }

    private bool IsMouseInsideMapCanvas()
    {
        return HoveredFlags.HasFlag(HoverFlags.Window) &&
            IsBoundedBy(ImGui.GetMousePos(), currentMapScreenPosition, currentMapScreenPosition + currentMapPixelSize);
    }

    private Vector2 TexturePixelToIngameCoord(Vector2 textureCoord)
    {
        var rawMapPosition = (textureCoord - GetMapCenterOffsetVector()) / GetMapScaleFactor() - GetRawMapOffsetVector();
        var mapPixelPosition = rawMapPosition * GetMapScaleFactor();
        return new Vector2(
            (float)Math.Round(((41.0f / GetMapScaleFactor() * ((mapPixelPosition.X + 1024.0f) / 2048.0f) + 1) * 100) / 100, 1),
            (float)Math.Round(((41.0f / GetMapScaleFactor() * ((mapPixelPosition.Y + 1024.0f) / 2048.0f) + 1) * 100) / 100, 1));
    }

    private static Vector2[] GetRotationVectors(float angle, Vector2 center, Vector2 size)
    {
        var cosA = MathF.Cos(angle + 0.5f * MathF.PI);
        var sinA = MathF.Sin(angle + 0.5f * MathF.PI);

        Vector2[] vectors =
        [
            center + ImRotate(new Vector2(-size.X * 0.5f, -size.Y * 0.5f), cosA, sinA),
            center + ImRotate(new Vector2(+size.X * 0.5f, -size.Y * 0.5f), cosA, sinA),
            center + ImRotate(new Vector2(+size.X * 0.5f, +size.Y * 0.5f), cosA, sinA),
            center + ImRotate(new Vector2(-size.X * 0.5f, +size.Y * 0.5f), cosA, sinA),
        ];
        return vectors;
    }

    public Vector2 GetMapCoordinateFor3D(Vector3 pos)
    {
        var twoD = new Vector2(pos.X, pos.Z);
        var mapcoord = ((twoD + GetRawMapOffsetVector()) * GetMapScaleFactor()) + GetMapCenterOffsetVector();
        return mapcoord;
    }
    public Vector2 GetPlayerMapPosition(Vector3 vec) => new Vector2(vec.X, vec.Z) * GetMapScaleFactor();
    private static Vector2 ImRotate(Vector2 v, float cosA, float sinA) => new(v.X * cosA - v.Y * sinA, v.X * sinA + v.Y * cosA);

    /// <summary>
    /// Offset Vector of SelectedX, SelectedY, scaled with SelectedSizeFactor
    /// </summary>
    public Vector2 GetMapOffsetVector() => GetRawMapOffsetVector() * GetMapScaleFactor();

    /// <summary>
    /// Unscaled Vector of SelectedX, SelectedY
    /// </summary>
    public Vector2 GetRawMapOffsetVector()
    {
        if (capturedAgentMapId == mapStateManager.currentMap.RowId && capturedAgentMapScaleFactor > 0)
        {
            return capturedAgentRawMapOffset;
        }

        return new Vector2(mapStateManager.currentMap.OffsetX, mapStateManager.currentMap.OffsetY);
    }

    /// <summary>
    /// Selected Scale Factor
    /// </summary>
    public float GetMapScaleFactor()
    {
        if (capturedAgentMapId == mapStateManager.currentMap.RowId && capturedAgentMapScaleFactor > 0)
        {
            return capturedAgentMapScaleFactor;
        }

        return mapStateManager.currentMap.SizeFactor / 100;
    }

    /// <summary>
    /// 1024 vector, center offset vector
    /// </summary>
    public static Vector2 GetMapCenterOffsetVector() => new(1024.0f, 1024.0f);

    public static bool IsBoundedBy(Vector2 cursor, Vector2 minBounds, Vector2 maxBounds)
    {
        if (cursor.X >= minBounds.X && cursor.Y >= minBounds.Y)
        {
            if (cursor.X <= maxBounds.X && cursor.Y <= maxBounds.Y)
            {
                return true;
            }
        }

        return false;
    }
}
