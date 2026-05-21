using AkuTrack.ApiTypes;
using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.FontIdentifier;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Data.Files;
using Lumina.Excel.Sheets;
using Lumina.Excel.Sheets.Experimental;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using System.Threading;
using System.Threading.Tasks;
using static Lumina.Data.Parsing.Layer.LayerCommon;

namespace AkuTrack.Windows;

public class MapWindow : Window, IDisposable
{
    private readonly record struct ClickedPlayer(string Name, uint EntityId, Vector3 Position, bool IsFriend);
    private readonly record struct ClickedAetheryte(string Name, uint AetheryteId, byte SubIndex, uint GilCost);
    private readonly record struct TreasureMapSpotInfo(uint RankId, ushort SpotIndex, string RankName, byte TextureId, Vector3 Position);
    private readonly record struct LocalMapCategoryMarker(string Category, uint RowId, string Name, uint IconId, Vector3 Position, float Radius);
    private readonly record struct MapMarkerLink(uint MapId, uint TerritoryId, string Name);
    private readonly record struct ClickedMapElement(string Type, uint RowId, string Name, Vector3 Position, MapMarkerLink? Link);

    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly ObjTrackManager objTrackManager;
    private readonly UploadManager uploadManager;
    private readonly WindowSystem windowSystem;
    private readonly IDataManager dataManager;
    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IPartyList partyList;
    private readonly IFateTable fateTable;
    private readonly IAetheryteList aetheryteList;
    private readonly IPluginLog log;
    private readonly ITextureProvider textureProvider;
    private readonly ITextureSubstitutionProvider textureSubstitutionProvider;
    private readonly EnpcShopResolver enpcShopResolver;

    private float Scale { get; set; } = 1;
    private const uint FlagTextCommandParamId = 1048;
    public Vector2 DrawOffset { get; set; }
    public HoverFlags HoveredFlags { get; private set; }
    public Vector2 DrawPosition { get; private set; }
    private Vector2 lastWindowSize;
    private bool isDragStarted = false;
    private bool keepPlayerCenteredPaused = false;
    private IDalamudTextureWrap? blendedTexture;
    private string currentPath = string.Empty;
    private uint currentMap = 0;
    private uint currentTerritory = 0;
    private string selectedMapPath = string.Empty;
    private string selectedMapBgPath = string.Empty;
    private static Vector2 cachedRawMapOffsetVector = Vector2.Zero;
    private static float cachedMapScaleFactor = 100.0f;
    private string currentWindowTitle = "AkuTrack - Map##akutrack_map";
    private uint linkedMapPathTarget;
    private string linkedMapParentPath = string.Empty;
    public float ZoomSpeed = 0.25f;
    private Vector2 currentMapPixelSize = new(0, 0);
    private Vector2 currentMapScreenPosition = new(0, 0);
    private string currentCursorPositionText = string.Empty;
    private bool suppressFlagPlacement;
    private bool pendingFlagFocus;
    private (uint TerritoryId, uint MapId, float X, float Y)? lastFocusedFlag;
    private uint treasureMapSpotCacheMap;
    private List<TreasureMapSpotInfo> treasureMapSpotCache = new();
    private uint localCategoryMarkerCacheMap;
    private List<LocalMapCategoryMarker> localCategoryMarkerCache = new();
    private Dictionary<uint, List<MapMarkerLink>>? fallbackMapLinksByPlaceName;
    private Dictionary<string, List<MapMarkerLink>>? fallbackMapLinksByName;
    private Dictionary<uint, uint>? contentFinderTypeByPlaceName;
    private Dictionary<string, uint>? contentFinderTypeByName;
    private HashSet<uint>? contentFinderMapIds;
    private Dictionary<string, List<MapMarkerLink>>? territoryMapLinksByName;
    private List<AkuGameObject> clickedObjects = new();
    private List<ClickedPlayer> clickedPlayers = new();
    private List<ClickedAetheryte> clickedAetherytes = new();
    private List<SightseeingLogEntryInfo> clickedSightseeingLogEntries = new();
    private List<ClickedMapElement> clickedMapElements = new();

    private readonly MapContextMenu mapContextMenu = new();
    private readonly TopBar topBar;
    private readonly BottomBar bottomBar;

    public ConcurrentDictionary<string, AkuGameObject> downloadList = new();

    // We give this window a hidden ID using ##.
    // The user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public MapWindow(
        Plugin plugin,
        Configuration configuration,
        ObjTrackManager objTrackManager,
        UploadManager uploadManager,
        TopBar topBar,
        BottomBar bottomBar,
        WindowSystem windowSystem,
        IDataManager dataManager,
        IFramework framework,
        IClientState clientState,
        IObjectTable objectTable,
        IPartyList partyList,
        IFateTable fateTable,
        IAetheryteList aetheryteList,
        ITextureProvider textureProvider,
        ITextureSubstitutionProvider textureSubstitutionProvider,
        IPluginLog log
        )
        : base("AkuTrack - Map##akutrack_map", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        this.configuration = configuration;
        this.log = log;
        this.dataManager = dataManager;
        this.framework = framework;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.partyList = partyList;
        this.fateTable = fateTable;
        this.aetheryteList = aetheryteList;
        this.objTrackManager = objTrackManager;
        this.uploadManager = uploadManager;
        this.topBar = topBar;
        this.bottomBar = bottomBar;
        this.windowSystem = windowSystem;
        this.textureProvider = textureProvider;
        this.textureSubstitutionProvider = textureSubstitutionProvider;
        this.enpcShopResolver = new EnpcShopResolver(dataManager, clientState.ClientLanguage);
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() { }

    public unsafe void FocusCurrentFlagMarkerIfNeeded()
    {
        var agentMap = AgentMap.Instance();
        if (agentMap == null || agentMap->FlagMarkerCount == 0)
        {
            lastFocusedFlag = null;
            return;
        }

        var flag = agentMap->FlagMapMarkers[0];
        var focusedFlag = (flag.TerritoryId, flag.MapId, flag.XFloat, flag.YFloat);
        if (lastFocusedFlag == focusedFlag)
        {
            return;
        }

        lastFocusedFlag = focusedFlag;
        pendingFlagFocus = true;
        keepPlayerCenteredPaused = true;
    }

    public void FocusCurrentFlagMarkerOnNextDraw()
    {
        pendingFlagFocus = true;
        keepPlayerCenteredPaused = true;
    }

    public override void OnOpen()
    {
        keepPlayerCenteredPaused = false;

        if (!configuration.CenterOnPlayerWhenOpening)
        {
            return;
        }

        CenterOnLocalPlayer();
    }

    public unsafe override void Draw()
    {
        UpdateWindowTitle();

        if (configuration.KeepPlayerCentered && !keepPlayerCenteredPaused && currentMap == clientState.MapId)
        {
            CenterOnLocalPlayer();
        }

        UpdateDrawOffset();

        HoveredFlags = HoverFlags.Nothing;

        if (IsBoundedBy(ImGui.GetMousePos(), ImGui.GetCursorScreenPos(), ImGui.GetCursorScreenPos() + ImGui.GetContentRegionMax()))
        {
            HoveredFlags |= HoverFlags.Window;
        }

        topBar.Draw(GetCurrentMapDisplayPath(currentMap), GetTopBarCursorPositionText());

        using (var childStyle = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 0.0f))
        using (var renderChild = ImRaii.Child("render_child", GetMapCanvasSize(), false, ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar))
        {
            currentMapScreenPosition = ImGui.GetWindowPos();
            currentMapPixelSize = ImGui.GetWindowSize();
            DrawMapElements();
            if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                HoveredFlags |= HoverFlags.WindowInnerFrame;
            }
        }

        bottomBar.Draw(HoveredFlags.HasFlag(HoverFlags.MapTexture), currentMapPixelSize, DrawPosition, DrawOffset, Scale, GetBottomBarPlayerPositionText());


        ProcessInputs();
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
        var footerHeight = 24.0f * scale;
        var available = ImGui.GetContentRegionAvail();
        return new Vector2(available.X, MathF.Max(120.0f * scale, available.Y - footerHeight));
    }

    private string GetBottomBarPlayerPositionText()
    {
        return currentMap == clientState.MapId && objectTable.LocalPlayer is { } player
            ? FormatPlayerMapPosition(player.Position)
            : string.Empty;
    }

    private unsafe void UpdateWindowTitle()
    {
        var agentMap = AgentMap.Instance();
        if (agentMap == null)
        {
            return;
        }

        var selectedMapId = agentMap->SelectedMapId;
        if (linkedMapPathTarget != 0 && linkedMapPathTarget != selectedMapId)
        {
            linkedMapPathTarget = 0;
            linkedMapParentPath = string.Empty;
        }

        var visibleTitle = "AkuTrack - Map";
        var windowTitle = $"{visibleTitle}##akutrack_map";

        if (currentWindowTitle == windowTitle)
        {
            return;
        }

        currentWindowTitle = windowTitle;
        WindowName = windowTitle;
    }

    private string GetMapDisplayPath(uint mapId, bool includeRegion = true)
    {
        if (mapId == 0 || !dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>(clientState.ClientLanguage).TryGetRow(mapId, out var map))
        {
            return "Unknown";
        }

        var parts = new List<string>();
        if (includeRegion)
        {
            AddMapPathPart(parts, map.PlaceNameRegion.ValueNullable?.Name.ToString());
        }

        AddMapPathPart(parts, map.PlaceName.ValueNullable?.Name.ToString());
        AddMapPathPart(parts, map.PlaceNameSub.ValueNullable?.Name.ToString());
        return parts.Count == 0 ? $"Map #{mapId}" : string.Join(" > ", parts);
    }

    private string GetCurrentMapDisplayPath(uint mapId)
    {
        var mapPath = GetMapDisplayPath(mapId);
        if (linkedMapPathTarget == mapId && !string.IsNullOrWhiteSpace(linkedMapParentPath))
        {
            mapPath = CombineMapPath(linkedMapParentPath, GetMapDisplayPath(mapId, false));
        }

        return mapPath;
    }

    private static string CombineMapPath(string parentPath, string childPath)
    {
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return childPath;
        }

        if (string.IsNullOrWhiteSpace(childPath) || childPath == "Unknown")
        {
            return parentPath;
        }

        var parts = parentPath.Split(" > ", StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim()).ToList();
        foreach (var childPart in childPath.Split(" > ", StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim()))
        {
            AddMapPathPart(parts, childPart);
        }

        return string.Join(" > ", parts);
    }

    private static void AddMapPathPart(List<string> parts, string? part)
    {
        if (string.IsNullOrWhiteSpace(part))
        {
            return;
        }

        var trimmed = part.Trim();
        if (parts.Any(existing => string.Equals(existing, trimmed, StringComparison.CurrentCultureIgnoreCase)))
        {
            return;
        }

        parts.Add(trimmed);
    }

    private void DrawMapElements() {
        if (clickedObjects.Count > 0 || clickedPlayers.Count > 0 || clickedAetherytes.Count > 0 || clickedSightseeingLogEntries.Count > 0 || clickedMapElements.Count > 0)
        {
            DrawAkuObjectContextMenu();
            if(!ImGui.IsPopupOpen("AkuTrack_AkuObject_Context_Menu")) {
                if(clickedObjects.Count > 0)
                    clickedObjects.Clear();
                if (clickedPlayers.Count > 0)
                    clickedPlayers.Clear();
                if (clickedAetherytes.Count > 0)
                    clickedAetherytes.Clear();
                if (clickedSightseeingLogEntries.Count > 0)
                    clickedSightseeingLogEntries.Clear();
                if (clickedMapElements.Count > 0)
                    clickedMapElements.Clear();
            }
        }
        DrawMapBackground();
        if (ImGui.IsItemHovered() && IsMouseInsideMapCanvas())
        {
            HoveredFlags |= HoverFlags.MapTexture;
        }
        ProcessPendingFlagFocus();

        var drawPlayerMarkersInBackground = ImGui.GetIO().KeyCtrl;
        var contentScope = GetCurrentContentScope();

        // Only draw player and from ObjectTable if we are looking at the map we are currently in
        if (currentMap == clientState.MapId)
        {
            if (drawPlayerMarkersInBackground)
            {
                DrawLocalPlayerAndPartyIcons();
            }

            DrawOtherPlayerIcons();
            foreach (var o in objTrackManager.seenList)
            {
                DrawAkuGameObject(o.Value, MapObjectSource.SelfFound, contentScope);
            }
        }
        if(ShouldDrawContent("RemoteMarker", contentScope)) {
            foreach (var o in downloadList)
            {
                if (!objTrackManager.seenList.ContainsKey(o.Key))
                    DrawAkuGameObject(o.Value, MapObjectSource.Downloaded, contentScope);
            }
        }

        try
        {
            var t = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>().GetRow(currentMap);
            var rows = dataManager.GetSubrowExcelSheet<Lumina.Excel.Sheets.MapMarker>().GetRow(t.MapMarkerRange);
            foreach (var row in rows)
            {
                if (row.X == 0 && row.Y == 0)
                {
                    continue;
                }
                var pos = new Vector2(row.X, row.Y);
                //log.Debug($"Icon {row.Icon} to {pos} {row.RowOffset} |{row.PlaceNameSubtext.Value.Name}|");
                var text = row.PlaceNameSubtext.Value.Name.ToString();
                var links = GetMapMarkerLinks(row);
                var linkNames = links.Count == 0 ? string.Empty : string.Join(" ", links.Select(link => link.Name));
                if (!MatchesMapSearch("MapMarker", text, linkNames, row.PlaceNameSubtext.RowId.ToString(), row.Icon.ToString()))
                {
                    continue;
                }

                if (row.Icon == 0)
                {
                    if (ShouldDrawContent("MapMarkerLabelsOnly", contentScope))
                    {
                        DrawMapLabelOnly(pos, text, links);
                    }

                    continue;
                }

                if (ShouldDrawContent("MapMarkersWithIcons", contentScope))
                {
                    DrawMapIcon(row.Icon, pos, 3.14f, text, row.SubtextOrientation, row.PlaceNameSubtext.RowId, links);
                }
            }
        } catch(ArgumentOutOfRangeException) {
            // FIXME: How to get markers from region maps?!?
            //log.Debug($"Could not find Markers for Territory {currentTerritory}");
        }

        DrawFateMarkers();
        DrawSightseeingLogMarkers();
        DrawTreasureMapSpots();
        DrawLocalCategoryMarkers();
        DrawPlacedMapMarkers();
        DrawFlagMarker();

        if (currentMap == clientState.MapId && !drawPlayerMarkersInBackground)
        {
            DrawLocalPlayerAndPartyIcons();
        }

        DrawFieldMarkers();
    }

    private static string FormatPlayerMapPosition(Vector3 worldPosition)
    {
        var mapPosition = TexturePixelToIngameCoord(GetMapCoordinateFor3D(worldPosition));
        return $"X:{mapPosition.X:F1} Y:{mapPosition.Y:F1} Z:{worldPosition.Y:F1}";
    }

    private static Vector2 TexturePixelToIngameCoord(Vector2 textureCoord)
    {
        var tmp = (textureCoord - GetMapCenterOffsetVector()) / GetMapScaleFactor() + GetRawMapOffsetVector();
        tmp *= GetMapScaleFactor();
        return new Vector2(
            MathF.Round(41.0f / GetMapScaleFactor() * ((tmp.X + 1024.0f) / 2048.0f) + 1.0f, 1),
            MathF.Round(41.0f / GetMapScaleFactor() * ((tmp.Y + 1024.0f) / 2048.0f) + 1.0f, 1));
    }

    public unsafe void CaptureSelectedMapFromAgent()
    {
        var agentMap = AgentMap.Instance();
        if (agentMap == null)
        {
            return;
        }

        var foregroundPath = agentMap->SelectedMapPath.ToString();
        var backgroundPath = agentMap->SelectedMapBgPath.ToString();
        if (string.IsNullOrWhiteSpace(foregroundPath) && string.IsNullOrWhiteSpace(backgroundPath))
        {
            return;
        }

        selectedMapPath = foregroundPath;
        selectedMapBgPath = backgroundPath;
        currentMap = agentMap->SelectedMapId;
        currentTerritory = agentMap->SelectedTerritoryId;
        cachedRawMapOffsetVector = new Vector2(agentMap->SelectedOffsetX * -1, agentMap->SelectedOffsetY * -1);
        cachedMapScaleFactor = agentMap->SelectedMapSizeFactorFloat;
    }

    private unsafe void DrawMapBackground()
    {
        CaptureSelectedMapFromAgent();
        if (string.IsNullOrWhiteSpace(selectedMapBgPath))
        {
            if (string.IsNullOrWhiteSpace(selectedMapPath))
            {
                return;
            }

            var gameMapPath = $"{selectedMapPath}.tex";
            if (currentPath != gameMapPath)
            {
                log.Debug($"MapWindow: FLAT| Texture switched. oldMid {currentMap} new: {currentMap} old: {currentPath} new: {gameMapPath}");
                if (gameMapPath.Contains("region"))
                {
                    log.Debug("REGION MAP DETECTED!");
                    var vanillaBgPath = $"{selectedMapBgPath}.tex";
                    var vanillaFgPath = $"{selectedMapPath}.tex";
                    log.Debug($"BG Path: {vanillaBgPath} sFG Path: {vanillaFgPath}");
                }
                currentPath = gameMapPath;
                FetchAkuGameObjectsFromAkuAPI(currentMap);
            }
            var texture = textureProvider.GetFromGame($"{selectedMapPath}.tex").GetWrapOrEmpty();

            ImGui.SetCursorPos(DrawPosition);
            ImGui.Image(texture.Handle, texture.Size * Scale);
        }
        else
        {
            var gameMapPath = $"{selectedMapBgPath}.tex";
            if (currentPath != gameMapPath)
            {
                log.Debug($"MapWindow: BLEND| Texture switched. oldMid {currentMap} new: {currentMap} old: {currentPath} new: {gameMapPath}");
                currentPath = gameMapPath;
                FetchAkuGameObjectsFromAkuAPI(currentMap);
                //fogTexture = null;
                blendedTexture?.Dispose();
                blendedTexture = LoadTexture();
            }

            if (blendedTexture is not null)
            {
                ImGui.SetCursorPos(DrawPosition);
                ImGui.Image(blendedTexture.Handle, blendedTexture.Size * Scale);
            }
        }
    }

    private IDalamudTextureWrap? LoadTexture()
    {
        var vanillaBgPath = $"{selectedMapBgPath}.tex";
        var vanillaFgPath = $"{selectedMapPath}.tex";

        var bgFile = GetTexFile(vanillaBgPath);
        var fgFile = GetTexFile(vanillaFgPath);

        if (bgFile is null || fgFile is null)
        {
            log.Warning("Failed to load map textures");
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

    private void DrawAkuGameObject(AkuGameObject obj, MapObjectSource source, MapContentScope scope) {
        if (obj.mid != currentMap)
            return;
        if (!MatchesMapSearch(obj.t, obj.name, obj.bid.ToString(), obj.nid?.ToString(), obj.npiid?.ToString()))
            return;
        if (obj.t == "EventNpc")
        {
            if (!configuration.IsObjectSourceEnabled(scope, "EventNpc", source))
                return;
            if (!ShouldDrawContent("EventNpc", scope))
                return;
            // check if we need special icons for ENPCs currently implemented for TrippleTriad
            DrawIcon(enpcShopResolver.GetPreferredMapIconId(obj.bid), obj.pos, obj.r, obj.tint);
        }
        else if (obj.t == "EventObj")
        {
            if (!configuration.IsObjectSourceEnabled(scope, "EventObj", source))
                return;
            var iconId = GetEventObjIconId(obj.bid);
            if (!configuration.IsIconCategoryEntryEnabled(scope, "EventObj", iconId))
                return;
            if (iconId == 60033)
                DrawIcon((int)iconId, obj.pos, obj.r, obj.tint, new Vector2(22.0f * ImGuiHelpers.GlobalScale));
            else
                DrawIcon((int)iconId, obj.pos, obj.r, obj.tint);
        }
        else if (obj.t == "BattleNpc")
        {
            if (!configuration.IsObjectSourceEnabled(scope, "BattleNpc", source))
                return;
            if (!ShouldDrawContent("BattleNpc", scope))
                return;
            DrawIcon(60422, obj.pos, obj.r, obj.tint);
        }
        else if (obj.t == "Aetheryte")
        {
            if(dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>().TryGetRow(obj.bid, out var aetheryte)) {
                if (aetheryte.AethernetName.Value.Name.ToString() != string.Empty && aetheryte.PlaceName.Value.Name.ToString() == string.Empty)
                {
                    DrawIcon(60430, obj.pos, 3.14f, obj.tint);
                }
            }
            DrawIcon(60453, obj.pos, 3.14f, obj.tint);
        }
        else if (obj.t == "GatheringPoint")
        {
            if (!configuration.IsObjectSourceEnabled(scope, "GatheringPoint", source))
                return;
            if (!dataManager.GetExcelSheet<Lumina.Excel.Sheets.GatheringPoint>().TryGetRow(obj.bid, out var gatheringPointRow))
            {
                log.Debug($"GatheringPoint {obj.bid} did not have a row in GatheringPoint sheet.");
                return;
            }
            var iconId = GetGatheringPointIconId(gatheringPointRow);
            if (!configuration.IsIconCategoryEntryEnabled(scope, "GatheringPoint", iconId))
                return;
            DrawIcon((int)iconId, obj.pos, obj.r, obj.tint);
        }
        else if (obj.t == "Treasure")
        {
            if (!configuration.IsObjectSourceEnabled(scope, "Treasure", source))
                return;
            if (!ShouldDrawContent("Treasure", scope))
                return;
            DrawIcon(60354, obj.pos, obj.r, obj.tint);
        }
        else if (obj.t == "Fishingspot")
        {
            if (!configuration.IsObjectSourceEnabled(scope, "Fishingspot", source))
                return;
            var iconId = GetFishingSpotIconId(obj.bid);
            if (!configuration.IsIconCategoryEntryEnabled(scope, "Fishingspot", iconId))
                return;
            DrawIcon((int)iconId, obj.pos, obj.r, obj.tint);
        }
        else if (obj.t == "SpearfishingNotebook")
        {
            if (!configuration.IsObjectSourceEnabled(scope, "SpearfishingNotebook", source))
                return;
            var iconId = GetSpearfishingSpotIconId(obj.bid);
            if (!configuration.IsIconCategoryEntryEnabled(scope, "SpearfishingNotebook", iconId))
                return;
            DrawIcon((int)iconId, obj.pos, obj.r, obj.tint);
        }
        else if (obj.t == "Quest")
        {
            if (!configuration.IsObjectSourceEnabled(scope, "Quest", source))
                return;
            var iconId = GetDownloadedQuestMapIconId(obj.bid);
            if (!configuration.IsIconCategoryEntryEnabled(scope, "Quest", iconId))
                return;
            DrawIcon((int)iconId, obj.pos, obj.r, obj.tint);
        }
        else if (obj.t == "HousingMapMarkerInfo")
        {
            if (!ShouldDrawContent("HousingMapMarkerInfo", scope))
                return;
            DrawIcon(60441, obj.pos, obj.r, obj.tint);
        }
        else if (obj.t == "CEs")
        {
            if (!ShouldDrawContent("CriticalEngagements", scope))
                return;
            DrawIcon(60852, obj.pos, obj.r, obj.tint);
        }
        else if (obj.t == "FATE")
        {
            if (!ShouldDrawContent("FATE", scope))
                return;
            DrawIcon(60501, obj.pos, obj.r, obj.tint);
        }
        else
            DrawIcon(60515, obj.pos, obj.r, obj.tint);
        if (IsMouseInsideMapCanvas() && ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
            clickedObjects.Add(obj);
            AddNearbyOtherPlayersToSelection();
            AddNearbyMapElementsToSelection();
        }
        if (ImGui.IsItemHovered() && IsMouseInsideMapCanvas())
        {
            DrawTooltip(obj);
            DrawIcon(60429, obj.pos, 3.14f, obj.tint);
        }
    }

    private void DrawTooltip(AkuGameObject obj) {
        ImGui.SetTooltip($"Created: {obj.created_at}\nLastSeen: {obj.lastseen_at}\n\nName: {obj.name}\nType: {obj.t}\nBaseID: {obj.bid}");
    }

    private bool IsMapSearchFilterActive()
    {
        return configuration.MapSearchFilterEnabled && !string.IsNullOrWhiteSpace(configuration.MapSearchFilterText);
    }

    private bool MatchesMapSearch(params string?[] values)
    {
        if (!IsMapSearchFilterActive())
        {
            return true;
        }

        var search = configuration.MapSearchFilterText.Trim();
        return values.Any(value => !string.IsNullOrWhiteSpace(value)
                                   && value.Contains(search, StringComparison.CurrentCultureIgnoreCase));
    }

    private MapContentScope GetCurrentContentScope() => IsContentFinderMap(currentMap) ? MapContentScope.ContentFinder : MapContentScope.World;

    private bool IsContentFinderMap(uint mapId)
    {
        if (contentFinderMapIds is null)
        {
            contentFinderMapIds = dataManager.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>(clientState.ClientLanguage)
                .Where(content => content.TerritoryType.IsValid
                                  && content.TerritoryType.Value.Map.IsValid
                                  && content.TerritoryType.Value.Map.RowId != 0)
                .Select(content => content.TerritoryType.Value.Map.RowId)
                .ToHashSet();
        }

        return contentFinderMapIds.Contains(mapId);
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
                "FATE" => configuration.DrawFates,
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
                "FATE" => configuration.DrawContentFinderFates,
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

    public void DrawAkuObjectContextMenu()
    {
        using var contextMenu = ImRaii.ContextPopup("AkuTrack_AkuObject_Context_Menu");
        if (!contextMenu) return;

        foreach (var obj in clickedObjects)
        {
            if (ImGui.MenuItem($"{obj.t} {obj.name}({obj.bid})"))
            {
                OpenDetailsWindow(obj);
            }
        }
        foreach (var obj in clickedObjects)
        {
            foreach (var link in GetObjectMapLinks(obj))
            {
                if (ImGui.MenuItem($"Map link {link.Name}##{obj.t}_{obj.bid}_{link.TerritoryId}_{link.MapId}"))
                {
                    OpenLinkedMap(link);
                }
            }
        }

        foreach (var player in clickedPlayers)
        {
            ImGui.MenuItem($"{(player.IsFriend ? "Friend" : "Player")} {player.Name}");
        }

        foreach (var aetheryte in clickedAetherytes)
        {
            if (ImGui.MenuItem($"Aetheryte {aetheryte.Name} ({aetheryte.GilCost} gil)"))
            {
                TeleportToAetheryte(aetheryte);
            }
        }

        foreach (var entry in clickedSightseeingLogEntries)
        {
            if (ImGui.MenuItem($"Vista #{entry.RowId}: {entry.Name}"))
            {
                OpenSightseeingLogEntryWindow(entry);
            }
        }

        foreach (var element in clickedMapElements)
        {
            var label = element.Link is { } link
                ? $"Map link {link.Name}"
                : $"{GetLocalCategoryDisplayName(element.Type)} {element.Name}({element.RowId})";
            if (ImGui.MenuItem(label))
            {
                if (element.Link is { } selectedLink)
                {
                    OpenLinkedMap(selectedLink);
                }
                else
                {
                    OpenDetailsWindow(CreateMapElementObject(element.Type, element.RowId, element.Name, element.Position));
                }
            }
        }
    }

    private void OpenDetailsWindow(AkuGameObject obj)
    {
        var newName = $"akutrack_details_{obj.t}_{obj.bid}";
        foreach (var window in windowSystem.Windows)
        {
            var splitName = window.WindowName.Split("##");
            if (splitName.Length > 1 && splitName[1] == newName)
            {
                return;
            }
        }

        var detailsWindow = ActivatorUtilities.CreateInstance<DetailsWindow>(plugin.serviceProvider, obj);
        windowSystem.AddWindow(detailsWindow);
        detailsWindow.Toggle();
    }

    private void OpenSightseeingLogEntryWindow(SightseeingLogEntryInfo entry)
    {
        var newName = $"akutrack_sightseeing_{entry.RowId}";
        foreach (var window in windowSystem.Windows)
        {
            var splitName = window.WindowName.Split("##");
            if (splitName.Length > 1 && splitName[1] == newName)
            {
                return;
            }
        }

        var detailsWindow = ActivatorUtilities.CreateInstance<SightseeingLogEntryWindow>(plugin.serviceProvider, entry);
        windowSystem.AddWindow(detailsWindow);
        detailsWindow.Toggle();
    }

    private void DrawIcon(int iconid, Vector3 position, float rotation, Vector4 tint)
    {
        var texture = textureProvider.GetFromGameIcon(iconid).GetWrapOrEmpty();
        DrawIcon(iconid, position, rotation, tint, texture.Size / 2.0f);
    }

    private void DrawIcon(int iconid, Vector3 position, float rotation, Vector4 tint, Vector2 size)
    {
        var texture = textureProvider.GetFromGameIcon(iconid).GetWrapOrEmpty();

        var p = ((GetMapCoordinateFor3D(position)) * Scale) + DrawPosition - (size / 2.0f);

        if (configuration.DrawDebugSquares)
        {
            ImGui.SetCursorPos(p);
            var cursorPos = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddRect(cursorPos, cursorPos + size, ImGui.GetColorU32(configuration.TextColor), 3.0f);
        }
        ImGui.SetCursorPos(p);
        //log.Debug($"@ {position} Drawing to {p} with scale {Scale} DrawPosition: {DrawPosition}");
        ImGui.Image(texture.Handle, size, Vector2.Zero, Vector2.One, tint);
    }

    private void DrawMapIcon(int iconid, Vector2 position, float rotation, string text, byte subtextOrientation, uint placeNameSubtextId = 0, IReadOnlyList<MapMarkerLink>? links = null)
    {
        if (IsDoubleHousingArea(iconid))
            return;
        var texture = textureProvider.GetFromGameIcon(iconid).GetWrapOrEmpty();
            //log.Debug($"@ {position} Drawing to {p} with scale {Scale} DrawPosition: {DrawPosition}");
        if (IsRegionIcon(iconid)) {
            var regionScaleFactor = 0.84f;
            // FIXME: Rendering of region icons is somewhat broken
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
                ProcessMapMarkerClick(placeNameSubtextId, text, position, links);
            }
        } else {
            var p = (position * Scale) + DrawPosition - (texture.Size / 4.0f);
            ImGui.SetCursorPos(p);
            ImGui.Image(texture.Handle, texture.Size / 2.0f);
            if (links is null or { Count: 0 })
            {
                ProcessAetheryteMapIconClick(placeNameSubtextId);
            }
            ProcessMapMarkerClick(placeNameSubtextId, text, position, links);
            if(configuration.DrawDebugSquares)
            {
                ImGui.SetCursorPos(p);
                var cursorPos = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRect(cursorPos, cursorPos + (texture.Size / 2.0f), ImGui.GetColorU32(configuration.TextColor), 3.0f);
            }
            if (text != string.Empty)
            {
                var ap = p;
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
                ProcessMapMarkerClick(placeNameSubtextId, text, position, links);
            }
        }
    }

    private void DrawMapLabelOnly(Vector2 position, string text, IReadOnlyList<MapMarkerLink>? links)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var p = (position * Scale) + DrawPosition;
        ImGui.SetCursorPos(p);
        ImGui.TextColored(configuration.TextColor, text);
        ProcessMapMarkerClick(0, text, position, links);
    }

    public static bool IsRegionIcon(int iconId) =>
       iconId switch
       {
           >= 63200 and < 63900 => true,
           >= 62620 and < 62800 => true,
           _ => false,
       };

    public static bool IsDoubleHousingArea(int iconId) {
        if (iconId == 63249 /* Goblet */ || iconId == 63210 /* Mist */ || iconId == 63228 /* Lavender Beds */ || iconId == 63383 /* Shirogane */)
            return true;
        return false;
    }

    private bool DrawPlayerIcon(Vector3 pos, float rotation)
    {
        return DrawPlayerIcon(pos, rotation, Vector4.One, 1.0f);
    }

    private bool DrawPlayerIcon(Vector3 pos, float rotation, Vector4 tint)
    {
        return DrawPlayerIcon(pos, rotation, tint, 1.0f);
    }

    private bool DrawPlayerIcon(Vector3 pos, float rotation, Vector4 tint, float iconScale)
    {
        var texture = textureProvider.GetFromGameIcon(60443).GetWrapOrEmpty();
        var angle = -rotation + MathF.PI / 2.0f;
        var scaledSize = texture.Size / 2.0f * Scale * iconScale;
        var minimumSize = new Vector2(36.0f * ImGuiHelpers.GlobalScale * iconScale);
        var size = new Vector2(MathF.Max(scaledSize.X, minimumSize.X), MathF.Max(scaledSize.Y, minimumSize.Y));

        var p = currentMapScreenPosition +
                           DrawPosition +
                           (GetPlayerMapPosition(pos) +
                            GetMapOffsetVector() +
                            GetMapCenterOffsetVector()) * Scale;
        //var p = ((GetMapCoordinateFor3D(pos)) * Scale) + DrawPosition - (texture.Size / 4.0f * Scale);
        var vectors = GetRotationVectors(angle, p, size);

        //log.Debug($"@ {position} Drawing to {p} with scale {Scale} DrawPosition: {DrawPosition}");
        ImGui.GetWindowDrawList().AddImageQuad(texture.Handle, vectors[0], vectors[1], vectors[2], vectors[3], Vector2.Zero, new Vector2(1, 0), Vector2.One, new Vector2(0, 1), ImGui.GetColorU32(tint));
        return IsMouseInsideMapCanvas() && IsBoundedBy(ImGui.GetMousePos(), p - size / 2.0f, p + size / 2.0f);
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
        var direction = AngleToDirection(angle);
        const float halfConeAngle = MathF.PI / 5.5f;
        var coneScale = MathF.Max(Scale, ImGuiHelpers.GlobalScale);
        var coneOrigin = center - direction * (7.0f * coneScale);
        var coneLength = 36.0f * coneScale;
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

    private void DrawLocalPlayerAndPartyIcons()
    {
        if (objectTable.LocalPlayer is { } localPlayer)
        {
            if (configuration.DrawCameraCone)
            {
                DrawCameraCone(localPlayer.Position);
            }

            DrawPlayerIcon(localPlayer.Position, localPlayer.Rotation, GetPlayerMarkerTint(localPlayer, Vector4.One));
        }

        DrawPartyMemberIcons();
    }

    private void DrawPartyMemberIcons()
    {
        if (!configuration.DrawPartyMembers || partyList.Length == 0)
        {
            return;
        }

        foreach (var member in partyList)
        {
            if (objectTable.LocalPlayer is { } localPlayer && member.EntityId == localPlayer.GameObjectId)
            {
                continue;
            }

            var memberObject = objectTable.SearchById(member.EntityId);
            if (memberObject is null || memberObject.ObjectKind != ObjectKind.Pc)
            {
                continue;
            }
            if (!MatchesMapSearch("Party member", member.Name.ToString(), member.ClassJob.RowId.ToString()))
            {
                continue;
            }

            var tint = GetPlayerMarkerTint(member.ClassJob.RowId, new Vector4(0.3f, 0.85f, 1.0f, 1.0f));
            if (DrawPlayerIcon(memberObject.Position, memberObject.Rotation, tint, 0.75f))
            {
                ImGui.SetTooltip($"Party Member: {member.Name}");
            }
        }
    }

    private void ProcessAetheryteMapIconClick(uint placeNameSubtextId)
    {
        if (placeNameSubtextId == 0 || !IsMouseInsideMapCanvas() || !ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            return;
        }

        clickedAetherytes.Clear();
        foreach (var aetheryte in aetheryteList)
        {
            if (aetheryte.TerritoryId != currentTerritory)
            {
                continue;
            }

            var data = aetheryte.AetheryteData.Value;
            if (!data.IsAetheryte || data.Invisible || data.PlaceName.RowId != placeNameSubtextId)
            {
                continue;
            }

            var clickedAetheryte = new ClickedAetheryte(data.PlaceName.Value.Name.ToString(), aetheryte.AetheryteId, aetheryte.SubIndex, aetheryte.GilCost);
            if (!clickedAetherytes.Contains(clickedAetheryte))
            {
                clickedAetherytes.Add(clickedAetheryte);
            }
        }

        if (clickedAetherytes.Count > 0)
        {
            AddNearbyMapElementsToSelection();
            AddNearbyOtherPlayersToSelection();
            ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
        }
    }

    private void ProcessMapMarkerClick(uint placeNameSubtextId, string text, Vector2 mapPosition, IReadOnlyList<MapMarkerLink>? links)
    {
        if (!IsMouseInsideMapCanvas() || !ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            return;
        }

        var position = GetWorldPositionForMapCoordinate(mapPosition);
        if (links is { Count: > 0 })
        {
            foreach (var link in links)
            {
                AddClickedMapElement("MapMarker", link.MapId, string.IsNullOrWhiteSpace(text) ? link.Name : text, position, link);
            }
        }
        else
        {
            AddClickedMapElement("MapMarker", placeNameSubtextId, string.IsNullOrWhiteSpace(text) ? "Map marker" : text, position);
        }

        AddNearbyMapElementsToSelection();
        AddNearbyOtherPlayersToSelection();
        ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
    }

    private IReadOnlyList<MapMarkerLink> GetMapMarkerLinks(Lumina.Excel.Sheets.MapMarker marker)
    {
        var originalLink = GetOriginalMapMarkerLink(marker);
        if (originalLink is not null)
        {
            return [originalLink.Value];
        }

        return GetFallbackMapMarkerLinks(marker);
    }

    private MapMarkerLink? GetOriginalMapMarkerLink(Lumina.Excel.Sheets.MapMarker marker)
    {
        if (marker.DataKey.RowId == 0 || marker.DataType is not (1 or 2))
        {
            return null;
        }

        if (!dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>(clientState.ClientLanguage).TryGetRow(marker.DataKey.RowId, out var linkedMap))
        {
            return null;
        }

        if (linkedMap.TerritoryType.RowId == 0)
        {
            return null;
        }

        var name = marker.PlaceNameSubtext.ValueNullable?.Name.ToString();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = linkedMap.PlaceName.ValueNullable?.Name.ToString();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Map #{linkedMap.RowId}";
        }

        return new MapMarkerLink(linkedMap.RowId, linkedMap.TerritoryType.RowId, name);
    }

    private IReadOnlyList<MapMarkerLink> GetFallbackMapMarkerLinks(Lumina.Excel.Sheets.MapMarker marker)
    {
        if (!IsDutyMapMarkerIcon(marker.Icon) || marker.PlaceNameSubtext.RowId == 0)
        {
            return [];
        }

        var linksByPlaceName = GetFallbackMapLinksByPlaceName();
        if (linksByPlaceName.TryGetValue(marker.PlaceNameSubtext.RowId, out var links))
        {
            return links;
        }

        var markerName = marker.PlaceNameSubtext.ValueNullable?.Name.ToString();
        return !string.IsNullOrWhiteSpace(markerName)
               && GetFallbackMapLinksByName().TryGetValue(NormalizeMapLinkName(markerName), out links)
            ? links
            : [];
    }

    private Dictionary<uint, List<MapMarkerLink>> GetFallbackMapLinksByPlaceName()
    {
        if (fallbackMapLinksByPlaceName is not null)
        {
            return fallbackMapLinksByPlaceName;
        }

        fallbackMapLinksByPlaceName = new Dictionary<uint, List<MapMarkerLink>>();
        fallbackMapLinksByName = new Dictionary<string, List<MapMarkerLink>>();
        contentFinderTypeByPlaceName = new Dictionary<uint, uint>();
        contentFinderTypeByName = new Dictionary<string, uint>();
        foreach (var content in dataManager.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>(clientState.ClientLanguage))
        {
            if (!content.TerritoryType.IsValid || string.IsNullOrWhiteSpace(content.Name.ToString()))
            {
                continue;
            }

            var territory = content.TerritoryType.Value;
            if (!territory.Map.IsValid || territory.Map.RowId == 0 || territory.PlaceName.RowId == 0)
            {
                continue;
            }

            var linkName = content.Name.ToString();
            var territoryPlaceName = territory.PlaceName.ValueNullable?.Name.ToString();
            var mapPlaceName = territory.Map.ValueNullable?.PlaceName.ValueNullable?.Name.ToString();
            var link = new MapMarkerLink(
                territory.Map.RowId,
                territory.RowId,
                string.IsNullOrWhiteSpace(linkName) ? territoryPlaceName ?? $"Map #{territory.Map.RowId}" : linkName);
            if (!fallbackMapLinksByPlaceName.TryGetValue(territory.PlaceName.RowId, out var links))
            {
                links = new List<MapMarkerLink>();
                fallbackMapLinksByPlaceName[territory.PlaceName.RowId] = links;
            }

            if (!links.Contains(link))
            {
                links.Add(link);
            }

            AddMapLinkAlias(fallbackMapLinksByName!, linkName, link);
            AddMapLinkAlias(fallbackMapLinksByName!, territoryPlaceName, link);
            AddMapLinkAlias(fallbackMapLinksByName!, mapPlaceName, link);

            if (content.ContentType.IsValid)
            {
                var contentTypeId = content.ContentType.RowId;
                contentFinderTypeByPlaceName[territory.PlaceName.RowId] = contentTypeId;
                AddContentFinderTypeAlias(linkName, contentTypeId);
                AddContentFinderTypeAlias(territoryPlaceName, contentTypeId);
                AddContentFinderTypeAlias(mapPlaceName, contentTypeId);
            }
        }

        foreach (var links in fallbackMapLinksByPlaceName.Values)
        {
            links.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));
        }
        foreach (var links in fallbackMapLinksByName!.Values)
        {
            links.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));
        }

        return fallbackMapLinksByPlaceName;
    }

    private uint? GetContentFinderConditionTypeId(Lumina.Excel.Sheets.MapMarker marker, string markerName)
    {
        if (!IsDutyMapMarkerIcon(marker.Icon))
        {
            return null;
        }

        GetFallbackMapLinksByPlaceName();
        if (marker.PlaceNameSubtext.RowId != 0
            && contentFinderTypeByPlaceName is not null
            && contentFinderTypeByPlaceName.TryGetValue(marker.PlaceNameSubtext.RowId, out var contentTypeId))
        {
            return contentTypeId;
        }

        return !string.IsNullOrWhiteSpace(markerName)
               && contentFinderTypeByName is not null
               && contentFinderTypeByName.TryGetValue(NormalizeMapLinkName(markerName), out contentTypeId)
            ? contentTypeId
            : null;
    }

    private Dictionary<string, List<MapMarkerLink>> GetFallbackMapLinksByName()
    {
        if (fallbackMapLinksByName is null)
        {
            GetFallbackMapLinksByPlaceName();
        }

        return fallbackMapLinksByName!;
    }

    private static void AddMapLinkAlias(Dictionary<string, List<MapMarkerLink>> linksByName, string? name, MapMarkerLink link)
    {
        var normalizedName = NormalizeMapLinkName(name ?? string.Empty);
        if (normalizedName.Length == 0)
        {
            return;
        }

        if (!linksByName.TryGetValue(normalizedName, out var links))
        {
            links = new List<MapMarkerLink>();
            linksByName[normalizedName] = links;
        }

        if (!links.Contains(link))
        {
            links.Add(link);
        }
    }

    private void AddContentFinderTypeAlias(string? name, uint contentTypeId)
    {
        if (string.IsNullOrWhiteSpace(name) || contentFinderTypeByName is null)
        {
            return;
        }

        contentFinderTypeByName[NormalizeMapLinkName(name)] = contentTypeId;
    }

    private IReadOnlyList<MapMarkerLink> GetObjectMapLinks(AkuGameObject obj)
    {
        var candidates = new List<string>();
        AddMapLinkNameCandidate(candidates, obj.name);
        if (obj.t == "EventObj" && dataManager.GetExcelSheet<Lumina.Excel.Sheets.EObjName>(clientState.ClientLanguage).TryGetRow(obj.bid, out var eObjName))
        {
            AddMapLinkNameCandidate(candidates, eObjName.Singular.ToString());
        }
        else if (obj.t == "EventNpc" && dataManager.GetExcelSheet<Lumina.Excel.Sheets.ENpcResident>(clientState.ClientLanguage).TryGetRow(obj.bid, out var eNpc))
        {
            AddMapLinkNameCandidate(candidates, eNpc.Singular.ToString());
        }

        var result = new List<MapMarkerLink>();
        var linksByName = GetTerritoryMapLinksByName();
        foreach (var candidate in candidates.Select(NormalizeMapLinkName).Where(candidate => candidate.Length > 0).Distinct())
        {
            if (!linksByName.TryGetValue(candidate, out var links))
            {
                continue;
            }

            foreach (var link in links)
            {
                if (link.TerritoryId != currentTerritory && !result.Contains(link))
                {
                    result.Add(link);
                }
            }
        }

        return result;
    }

    private static void AddMapLinkNameCandidate(List<string> candidates, string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return;
        }

        candidates.Add(rawName);
        var name = rawName.Trim();
        foreach (var separator in new[] { " to ", " nach ", " zum ", " zur ", " access to ", " passage to ", " gate to ", " exit to " })
        {
            var index = name.LastIndexOf(separator, StringComparison.CurrentCultureIgnoreCase);
            if (index >= 0 && index + separator.Length < name.Length)
            {
                candidates.Add(name[(index + separator.Length)..]);
            }
        }
    }

    private Dictionary<string, List<MapMarkerLink>> GetTerritoryMapLinksByName()
    {
        if (territoryMapLinksByName is not null)
        {
            return territoryMapLinksByName;
        }

        territoryMapLinksByName = new Dictionary<string, List<MapMarkerLink>>();
        foreach (var territory in dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>(clientState.ClientLanguage))
        {
            if (!territory.Map.IsValid || territory.Map.RowId == 0 || !territory.PlaceName.IsValid)
            {
                continue;
            }

            var name = territory.PlaceName.Value.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var link = new MapMarkerLink(territory.Map.RowId, territory.RowId, name);
            AddMapLinkAlias(territoryMapLinksByName, name, link);
            AddMapLinkAlias(territoryMapLinksByName, territory.Map.ValueNullable?.PlaceName.ValueNullable?.Name.ToString(), link);
        }

        foreach (var links in territoryMapLinksByName.Values)
        {
            links.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));
        }

        return territoryMapLinksByName;
    }

    private static string NormalizeMapLinkName(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        foreach (var article in new[] { "the ", "a ", "an ", "der ", "die ", "das ", "den ", "dem ", "des ", "le ", "la ", "les ", "un ", "une ", "l'", "l’" })
        {
            if (normalized.StartsWith(article, StringComparison.Ordinal))
            {
                normalized = normalized[article.Length..].TrimStart();
                break;
            }
        }

        return string.Concat(normalized.Where(char.IsLetterOrDigit));
    }

    private static bool IsDutyMapMarkerIcon(uint iconId)
    {
        return iconId is 60414 or 60428 or 60442;
    }

    private unsafe void OpenLinkedMap(MapMarkerLink link)
    {
        var agentMap = AgentMap.Instance();
        if (agentMap == null)
        {
            return;
        }

        keepPlayerCenteredPaused = true;
        pendingFlagFocus = false;
        linkedMapPathTarget = link.MapId;
        linkedMapParentPath = GetMapDisplayPath(currentMap);
        currentPath = string.Empty;
        agentMap->OpenMap(link.MapId, link.TerritoryId, link.Name, FFXIVClientStructs.FFXIV.Client.UI.Agent.MapType.Centered);
    }

    private static unsafe void TeleportToAetheryte(ClickedAetheryte aetheryte)
    {
        Telepo.Instance()->Teleport(aetheryte.AetheryteId, aetheryte.SubIndex);
    }

    private void DrawSightseeingLogMarkers()
    {
        if (!ShouldDrawContent("SightseeingLog", GetCurrentContentScope()))
        {
            return;
        }

        foreach (var entry in dataManager.GetExcelSheet<Lumina.Excel.Sheets.Adventure>(clientState.ClientLanguage))
        {
            var level = entry.Level.Value;
            if (level.Map.RowId != currentMap)
            {
                continue;
            }

            var position = new Vector3(level.X, 0, level.Z);
            var sightseeingEntry = BuildSightseeingLogEntry(entry);
            if (!MatchesMapSearch("Sightseeing", "Vista", sightseeingEntry.Name, sightseeingEntry.RowId.ToString()))
            {
                continue;
            }

            DrawSightseeingLogMarker(sightseeingEntry, position);
        }
    }

    private void DrawSightseeingLogMarker(SightseeingLogEntryInfo entry, Vector3 position)
    {
        var texture = textureProvider.GetFromGameIcon(60071).GetWrapOrEmpty();
        var center = GetMapScreenPosition(position);
        var size = texture.Size / 2.0f;
        var min = center - size / 2.0f;

        ImGui.SetCursorPos(min - currentMapScreenPosition);
        ImGui.Image(texture.Handle, size);

        if (ImGui.IsItemHovered() && IsMouseInsideMapCanvas())
        {
            ImGui.SetTooltip($"Vista #{entry.RowId}: {entry.Name}");
        }

        if (IsMouseInsideMapCanvas() && ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            clickedSightseeingLogEntries.Clear();
            clickedSightseeingLogEntries.Add(entry);
            AddNearbyMapElementsToSelection();
            AddNearbyOtherPlayersToSelection();
            ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
        }
    }

    private static SightseeingLogEntryInfo BuildSightseeingLogEntry(Lumina.Excel.Sheets.Adventure entry)
    {
        var time = string.Empty;
        var weather = string.Empty;
        var emote = entry.Emote.Value.Name.ToString();

        return new SightseeingLogEntryInfo(
            entry.RowId,
            entry.Name.ToString(),
            entry.Description.ToString(),
            time,
            weather,
            emote);
    }

    private void DrawTreasureMapSpots()
    {
        if (!ShouldDrawContent("TreasureMaps", GetCurrentContentScope()))
        {
            return;
        }

        foreach (var spot in GetTreasureMapSpotsForCurrentMap())
        {
            if (configuration.IsTreasureMapRankEnabled(spot.RankId)
                && MatchesMapSearch("TreasureMaps", "Treasure map", spot.RankName, spot.RankId.ToString()))
            {
                DrawTreasureMapSpot(spot);
            }
        }
    }

    private IReadOnlyList<TreasureMapSpotInfo> GetTreasureMapSpotsForCurrentMap()
    {
        if (treasureMapSpotCacheMap == currentMap)
        {
            return treasureMapSpotCache;
        }

        treasureMapSpotCacheMap = currentMap;
        treasureMapSpotCache = new List<TreasureMapSpotInfo>();

        var spots = dataManager.GetSubrowExcelSheet<Lumina.Excel.Sheets.TreasureSpot>();
        foreach (var rank in dataManager.GetExcelSheet<Lumina.Excel.Sheets.TreasureHuntRank>(clientState.ClientLanguage))
        {
            if (rank.RowId == 0 || rank.Icon == 0 || !rank.ItemName.IsValid || !spots.HasRow(rank.RowId))
            {
                continue;
            }

            var rankName = rank.ItemName.Value.Name.ToString();
            if (string.IsNullOrWhiteSpace(rankName))
            {
                continue;
            }

            foreach (var spot in spots.GetRow(rank.RowId))
            {
                if (!spot.Location.IsValid)
                {
                    continue;
                }

                var level = spot.Location.Value;
                if (level.Map.RowId != currentMap)
                {
                    continue;
                }

                treasureMapSpotCache.Add(new TreasureMapSpotInfo(rank.RowId, spot.SubrowId, rankName, rank.TreasureHuntTexture, new Vector3(level.X, level.Y, level.Z)));
            }
        }

        return treasureMapSpotCache;
    }

    private void DrawTreasureMapSpot(TreasureMapSpotInfo spot)
    {
        var center = GetMapScreenPosition(spot.Position);
        var drawList = ImGui.GetWindowDrawList();
        var texture = textureProvider.GetFromGame(GetTreasureMapTexturePath(spot.TextureId)).GetWrapOrEmpty();

        var size = new Vector2(220.0f, 200.0f) * Scale;
        var min = center - size / 2.0f;
        var max = center + size / 2.0f;

        var baseUv0 = Vector2.Zero;
        var baseUv1 = new Vector2(2.0f / 5.0f, 1.0f);
        drawList.AddImage(texture.Handle, min, max, baseUv0, baseUv1);

        var xSourceSize = texture.Size.Y / 6.0f;

        var xCenter = new Vector2(
            texture.Size.X * (14.0f / 15.0f) + xSourceSize * 0.12f,
            texture.Size.Y * (3.0f / 5.0f) - xSourceSize * 0.18f
        );

        var xSourceMin = xCenter - new Vector2(xSourceSize / 2.0f, xSourceSize / 2.0f);
        var xSourceMax = xCenter + new Vector2(xSourceSize / 2.0f, xSourceSize / 2.0f);

        var xRenderSize = new Vector2(size.X * 0.22f, size.X * 0.22f);
        var xMin = center - xRenderSize / 2.0f;
        var xMax = center + xRenderSize / 2.0f;

        drawList.AddImage(texture.Handle, xMin, xMax, xSourceMin / texture.Size, xSourceMax / texture.Size);

        if (IsMouseInsideMapCanvas() && IsBoundedBy(ImGui.GetMousePos(), min, max))
        {
            ImGui.SetTooltip($"{spot.RankName}\nTreasure map spot: {spot.Position.X:F1}, {spot.Position.Z:F1}");
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                AddClickedMapElement("TreasureMaps", spot.RankId, spot.RankName, spot.Position);
                AddNearbyMapElementsToSelection();
                AddNearbyOtherPlayersToSelection();
                ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
            }
        }
    }

    private static string GetTreasureMapTexturePath(byte textureId)
    {
        return textureId switch
        {
            1 => "ui/uld/treasuremap_relic_hr1.tex",
            2 => "ui/uld/treasuremap_seasonal_hr1.tex",
            4 => "ui/uld/treasuremap_undersea_hr1.tex",
            _ => "ui/uld/treasuremap_hr1.tex",
        };
    }

    private static (Vector2 Uv0, Vector2 Uv1) GetTreasureMapSpriteUv(ushort spotIndex, Vector2 textureSize)
    {
        // Full texture layout:
        // left 2/5 = base treasure map image
        // right 3/5 = X marker sprites
        var baseWidth = textureSize.X * 2.0f / 5.0f;

        const int markerColumns = 5;
        const int markerRows = 2;
        const int markerCount = markerColumns * markerRows;

        var markerAreaX = baseWidth;
        var markerAreaY = textureSize.Y * 3.0f / 5.0f;

        var markerCellWidth = (textureSize.X - baseWidth) / markerColumns;
        var markerCellHeight = (textureSize.Y - markerAreaY) / markerRows;

        var spriteIndex = spotIndex % markerCount;
        var column = spriteIndex % markerColumns;
        var row = spriteIndex / markerColumns;

        var markerMin = new Vector2(markerAreaX + column * markerCellWidth, markerAreaY + row * markerCellHeight);
        var markerMax = markerMin + new Vector2(markerCellWidth, markerCellHeight);

        return (markerMin / textureSize, markerMax / textureSize);
    }

    private void DrawLocalCategoryMarkers()
    {
        foreach (var marker in GetLocalCategoryMarkersForCurrentMap())
        {
            if (!ShouldDrawLocalCategoryMarker(marker))
            {
                continue;
            }

            DrawLocalCategoryMarker(marker);
        }
    }

    private IReadOnlyList<LocalMapCategoryMarker> GetLocalCategoryMarkersForCurrentMap()
    {
        if (localCategoryMarkerCacheMap == currentMap)
        {
            return localCategoryMarkerCache;
        }

        localCategoryMarkerCacheMap = currentMap;
        localCategoryMarkerCache = new List<LocalMapCategoryMarker>();

        AddFishingSpotMarkers(localCategoryMarkerCache);
        AddSpearfishingSpotMarkers(localCategoryMarkerCache);
        AddQuestMarkers(localCategoryMarkerCache);
        AddHousingMapMarkers(localCategoryMarkerCache);

        return localCategoryMarkerCache;
    }

    private bool ShouldDrawLocalCategoryMarker(string category)
    {
        var scope = GetCurrentContentScope();
        return category switch
        {
            "Fishingspot" => true,
            "SpearfishingNotebook" => true,
            "Quest" => true,
            "HousingMapMarkerInfo" => ShouldDrawContent("HousingMapMarkerInfo", scope),
            _ => false,
        };
    }

    private bool ShouldDrawLocalCategoryMarker(LocalMapCategoryMarker marker)
    {
        var scope = GetCurrentContentScope();
        return ShouldDrawLocalCategoryMarker(marker.Category)
               && configuration.IsIconCategoryEntryEnabled(scope, marker.Category, marker.IconId)
               && MatchesMapSearch(marker.Category, GetLocalCategoryDisplayName(marker.Category), marker.Name, marker.RowId.ToString(), marker.IconId.ToString());
    }

    private void AddFishingSpotMarkers(List<LocalMapCategoryMarker> markers)
    {
        foreach (var spot in dataManager.GetExcelSheet<Lumina.Excel.Sheets.FishingSpot>(clientState.ClientLanguage))
        {
            if (!spot.TerritoryType.IsValid || spot.TerritoryType.Value.Map.RowId != currentMap)
            {
                continue;
            }

            var name = spot.PlaceName.IsValid ? spot.PlaceName.Value.Name.ToString() : $"Fishing Spot #{spot.RowId}";
            var position = GetWorldPositionForMapCoordinate(new Vector2(spot.X, spot.Z));
            markers.Add(new LocalMapCategoryMarker("Fishingspot", spot.RowId, name, GetFishingSpotIconId(spot), position, spot.Radius / 6.0f));
        }
    }

    private void AddSpearfishingSpotMarkers(List<LocalMapCategoryMarker> markers)
    {
        foreach (var spot in dataManager.GetExcelSheet<Lumina.Excel.Sheets.SpearfishingNotebook>(clientState.ClientLanguage))
        {
            if (!spot.TerritoryType.IsValid || spot.TerritoryType.Value.Map.RowId != currentMap)
            {
                continue;
            }

            var name = spot.PlaceName.IsValid ? spot.PlaceName.Value.Name.ToString() : $"Spearfishing Spot #{spot.RowId}";
            var position = GetWorldPositionForMapCoordinate(new Vector2(spot.X, spot.Y));
            markers.Add(new LocalMapCategoryMarker("SpearfishingNotebook", spot.RowId, name, GetSpearfishingSpotIconId(spot), position, spot.Radius / 3.0f));
        }
    }

    private void AddQuestMarkers(List<LocalMapCategoryMarker> markers)
    {
        foreach (var quest in dataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>(clientState.ClientLanguage))
        {
            if (!quest.IssuerLocation.IsValid)
            {
                continue;
            }

            var level = quest.IssuerLocation.Value;
            if (level.Map.RowId != currentMap || level.X == 0 && level.Z == 0)
            {
                continue;
            }

            var name = quest.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"Quest #{quest.RowId}";
            }

            var iconId = GetQuestMapIconId(quest);
            markers.Add(new LocalMapCategoryMarker("Quest", quest.RowId, name, iconId, new Vector3(level.X, level.Y, level.Z), 0));
        }
    }

    private static uint GetQuestMapIconId(Lumina.Excel.Sheets.Quest quest)
    {
        if (quest.EventIconType.IsValid)
        {
            var eventIconType = quest.EventIconType.Value;
            var iconId = GetQuestMapIconIdForEventIconType(eventIconType.RowId);
            if (iconId != 0)
            {
                return iconId;
            }

            if (eventIconType.MapIconAvailable != 0)
            {
                return eventIconType.MapIconAvailable;
            }
        }

        return quest.Icon != 0 ? quest.Icon : 71221;
    }

    private static uint GetQuestMapIconIdForEventIconType(uint eventIconTypeId)
    {
        return eventIconTypeId switch
        {
            1 => 71221,
            3 => 71201,
            4 => 71222,
            8 or 10 => 71341,
            33 => 62521,
            34 => 62523,
            _ => 0,
        };
    }

    private uint GetDownloadedQuestMapIconId(uint questId)
    {
        if (dataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>(clientState.ClientLanguage).TryGetRow(questId, out var quest))
        {
            return GetQuestMapIconId(quest);
        }

        return 71221;
    }

    private uint GetEventObjIconId(uint eObjId)
    {
        if (eObjId == 2000401) // summoning bell
        {
            return 60425;
        }

        if (eObjId == 2000402) // market board
        {
            return 60570;
        }

        if (eObjId == 2000470) // company chest
        {
            return 60460;
        }

        if (dataManager.GetExcelSheet<Lumina.Excel.Sheets.EObjName>(clientState.ClientLanguage).TryGetRow(eObjId, out var eObjName)
            && string.Equals(eObjName.Singular.ToString(), "Windätherquelle", StringComparison.OrdinalIgnoreCase))
        {
            return 60033;
        }

        return 60353;
    }

    private uint GetFishingSpotIconId(uint fishingSpotId)
    {
        if (dataManager.GetExcelSheet<Lumina.Excel.Sheets.FishingSpot>(clientState.ClientLanguage).TryGetRow(fishingSpotId, out var spot))
        {
            return GetFishingSpotIconId(spot);
        }

        return 60465;
    }

    private static uint GetFishingSpotIconId(Lumina.Excel.Sheets.FishingSpot spot)
    {
        return spot.Rare ? 60466u : 60465u;
    }

    private uint GetSpearfishingSpotIconId(uint spearfishingSpotId)
    {
        if (dataManager.GetExcelSheet<Lumina.Excel.Sheets.SpearfishingNotebook>(clientState.ClientLanguage).TryGetRow(spearfishingSpotId, out var spot))
        {
            return GetSpearfishingSpotIconId(spot);
        }

        return 60929;
    }

    private static uint GetSpearfishingSpotIconId(Lumina.Excel.Sheets.SpearfishingNotebook spot)
    {
        return spot.IsShadowNode ? 60930u : 60929u;
    }

    private static uint GetGatheringPointIconId(Lumina.Excel.Sheets.GatheringPoint gatheringPoint)
    {
        var gatheringType = gatheringPoint.GatheringPointBase.Value.GatheringType.Value;
        return (uint)gatheringType.IconMain;
    }

    private void AddHousingMapMarkers(List<LocalMapCategoryMarker> markers)
    {
        foreach (var row in dataManager.GetSubrowExcelSheet<Lumina.Excel.Sheets.HousingMapMarkerInfo>())
        {
            foreach (var marker in row)
            {
                if (!marker.Map.IsValid || marker.Map.RowId != currentMap || marker.X == 0 && marker.Z == 0)
                {
                    continue;
                }

                markers.Add(new LocalMapCategoryMarker("HousingMapMarkerInfo", marker.RowId, $"Housing Marker #{marker.RowId}.{marker.SubrowId}", 60441, new Vector3(marker.X, marker.Y, marker.Z), 0));
            }
        }
    }

    private void DrawLocalCategoryMarker(LocalMapCategoryMarker marker)
    {
        var center = GetMapScreenPosition(marker.Position);
        var drawList = ImGui.GetWindowDrawList();
        var radius = marker.Radius > 0 ? marker.Radius * Scale : 10.0f * ImGuiHelpers.GlobalScale;
        var fillColor = GetLocalCategoryColor(marker.Category, 0.26f);
        var outlineColor = GetLocalCategoryColor(marker.Category, 0.92f);

        drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(fillColor), 32);
        drawList.AddCircle(center, radius, ImGui.GetColorU32(outlineColor), 32, MathF.Max(1.0f, ImGuiHelpers.GlobalScale));

        if (marker.IconId != 0)
        {
            var texture = textureProvider.GetFromGameIcon((int)marker.IconId).GetWrapOrEmpty();
            var size = new Vector2(22.0f * ImGuiHelpers.GlobalScale);
            drawList.AddImage(texture.Handle, center - size / 2.0f, center + size / 2.0f);
        }

        if (IsMouseInsideMapCanvas() && Vector2.Distance(ImGui.GetMousePos(), center) <= MathF.Max(radius, 14.0f))
        {
            ImGui.SetTooltip($"{GetLocalCategoryDisplayName(marker.Category)}: {marker.Name}\n{marker.Position.X:F1}, {marker.Position.Z:F1}");
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                AddClickedMapElement(marker.Category, marker.RowId, marker.Name, marker.Position);
                AddNearbyMapElementsToSelection();
                AddNearbyOtherPlayersToSelection();
                ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
            }
        }
    }

    private static Vector4 GetLocalCategoryColor(string category, float alpha)
    {
        return category switch
        {
            "Fishingspot" => new Vector4(0.20f, 0.55f, 1.00f, alpha),
            "SpearfishingNotebook" => new Vector4(0.00f, 0.75f, 0.85f, alpha),
            "Quest" => new Vector4(1.00f, 0.78f, 0.18f, alpha),
            "HousingMapMarkerInfo" => new Vector4(0.95f, 0.48f, 0.20f, alpha),
            _ => new Vector4(1.0f, 1.0f, 1.0f, alpha),
        };
    }

    private static string GetLocalCategoryDisplayName(string category)
    {
        return category switch
        {
            "Fishingspot" => "Fishing spot",
            "SpearfishingNotebook" => "Spearfishing spot",
            "Quest" => "Quest",
            "HousingMapMarkerInfo" => "Housing map marker",
            "TreasureMaps" => "Treasure map",
            "MapMarker" => "Map marker",
            "PlacedMapMarker" => "Placed map marker",
            "Flag" => "Flag",
            "FieldMarker" => "Field marker",
            "FATE" => "FATE",
            "CEs" => "Critical engagement",
            _ => category,
        };
    }

    private void DrawOtherPlayerIcons()
    {
        if (!configuration.DrawOtherPlayers)
        {
            return;
        }

        foreach (var gameObject in objectTable)
        {
            if (gameObject is null || !IsOtherPlayer(gameObject) || gameObject is not ICharacter character)
            {
                continue;
            }

            var isFriend = IsFriend(character);
            if (!MatchesMapSearch(isFriend ? "Friend" : "Player", character.Name.ToString(), character.ClassJob.RowId.ToString()))
            {
                continue;
            }

            if (DrawOtherPlayerCircle(gameObject.Position, isFriend))
            {
                ImGui.SetTooltip($"{(isFriend ? "Friend" : "Player")}: {character.Name}");
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
                    AddClickedPlayer(character, isFriend);
                    AddNearbyMapElementsToSelection();
                }
            }
        }
    }

    private bool DrawOtherPlayerCircle(Vector3 position, bool isFriend)
    {
        var center = GetMapScreenPosition(position);
        var radius = MathF.Max(3.0f, 4.0f * Scale);
        var fillColor = ImGui.GetColorU32(isFriend
            ? new Vector4(0.1f, 1.0f, 0.55f, 0.95f)
            : new Vector4(0.15f, 0.65f, 1.0f, 0.9f));
        var outlineColor = ImGui.GetColorU32(isFriend
            ? new Vector4(0.0f, 0.35f, 0.16f, 1.0f)
            : new Vector4(0.02f, 0.18f, 0.45f, 1.0f));

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddCircleFilled(center, radius, fillColor, 16);
        drawList.AddCircle(center, radius, outlineColor, 16, MathF.Max(1.0f, Scale));

        return IsMouseInsideMapCanvas() && IsBoundedBy(ImGui.GetMousePos(), center - new Vector2(radius), center + new Vector2(radius));
    }

    private void DrawFateMarkers()
    {
        if (!ShouldDrawContent("FATE", GetCurrentContentScope()))
        {
            return;
        }

        foreach (var fate in fateTable)
        {
            if (fate is null || !fateTable.IsValid(fate))
            {
                continue;
            }

            if (fate.TerritoryType.RowId != currentTerritory || fate.State is not (FateState.Preparing or FateState.Running))
            {
                continue;
            }
            if (!MatchesMapSearch("FATE", fate.Name.ToString(), fate.FateId.ToString(), fate.Level.ToString()))
            {
                continue;
            }

            DrawFateMarker(fate);
        }
    }

    private void DrawFateMarker(IFate fate)
    {
        var center = GetMapScreenPosition(fate.Position);
        var radius = fate.Radius * GetMapScaleFactor() * Scale;
        var drawList = ImGui.GetWindowDrawList();
        var radiusFillColor = ImGui.GetColorU32(new Vector4(0.35f, 0.2f, 0.75f, 0.12f));
        var radiusLineColor = ImGui.GetColorU32(new Vector4(0.55f, 0.35f, 1.0f, 0.65f));

        if (radius > 1.0f)
        {
            drawList.AddCircleFilled(center, radius, radiusFillColor, 48);
            drawList.AddCircle(center, radius, radiusLineColor, 48, MathF.Max(1.0f, Scale));
        }

        var iconId = fate.MapIconId != 0 ? fate.MapIconId : fate.IconId;
        if (iconId != 0)
        {
            var texture = textureProvider.GetFromGameIcon(iconId).GetWrapOrEmpty();
            var size = texture.Size / 2.0f;
            drawList.AddImage(texture.Handle, center - size / 2.0f, center + size / 2.0f);
        }

        var iconHoverRadius = 14.0f;
        if (IsMouseInsideMapCanvas() && Vector2.Distance(ImGui.GetMousePos(), center) <= MathF.Max(iconHoverRadius, MathF.Min(radius, 32.0f)))
        {
            ImGui.SetTooltip($"FATE: {fate.Name}\nLevel: {fate.Level}\nProgress: {fate.Progress}%\nTime: {FormatTimeRemaining(fate.TimeRemaining)}");
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                AddClickedMapElement("FATE", fate.FateId, fate.Name.ToString(), fate.Position);
                AddNearbyMapElementsToSelection();
                AddNearbyOtherPlayersToSelection();
                ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
            }
        }
    }

    private static string FormatTimeRemaining(long seconds)
    {
        seconds = Math.Max(0, seconds);
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    private Vector2 GetMapScreenPosition(Vector3 position)
    {
        return currentMapScreenPosition +
               DrawPosition +
               (GetPlayerMapPosition(position) +
                GetMapOffsetVector() +
                GetMapCenterOffsetVector()) * Scale;
    }

    private bool IsMouseInsideMapCanvas()
    {
        return HoveredFlags.HasFlag(HoverFlags.Window)
               && IsBoundedBy(ImGui.GetMousePos(), currentMapScreenPosition, currentMapScreenPosition + currentMapPixelSize);
    }

    private bool IsOtherPlayer(IGameObject gameObject)
    {
        if (gameObject.ObjectKind != ObjectKind.Pc)
        {
            return false;
        }

        if (objectTable.LocalPlayer is { } localPlayer && gameObject.EntityId == localPlayer.EntityId)
        {
            return false;
        }

        return !partyList.Any(member => member.EntityId == gameObject.EntityId);
    }

    private void AddNearbyOtherPlayersToSelection()
    {
        if (!configuration.DrawOtherPlayers)
        {
            return;
        }

        const float selectionRadius = 14.0f;
        var mousePosition = ImGui.GetMousePos();
        foreach (var gameObject in objectTable)
        {
            if (gameObject is null || !IsOtherPlayer(gameObject) || gameObject is not ICharacter character)
            {
                continue;
            }
            if (!MatchesMapSearch(IsFriend(character) ? "Friend" : "Player", character.Name.ToString(), character.ClassJob.RowId.ToString()))
            {
                continue;
            }

            if (Vector2.Distance(mousePosition, GetMapScreenPosition(gameObject.Position)) <= selectionRadius)
            {
                AddClickedPlayer(character, IsFriend(character));
            }
        }
    }

    private void AddClickedPlayer(ICharacter character, bool isFriend)
    {
        if (clickedPlayers.Any(player => player.EntityId == character.EntityId))
        {
            return;
        }

        clickedPlayers.Add(new ClickedPlayer(character.Name.ToString(), character.EntityId, character.Position, isFriend));
    }

    private void AddNearbyMapElementsToSelection()
    {
        const float selectionRadius = 18.0f;
        var mousePosition = ImGui.GetMousePos();

        if (true)
        {
            foreach (var spot in GetTreasureMapSpotsForCurrentMap())
            {
                if (!configuration.IsTreasureMapRankEnabled(spot.RankId))
                {
                    continue;
                }
                if (!MatchesMapSearch("TreasureMaps", "Treasure map", spot.RankName, spot.RankId.ToString()))
                {
                    continue;
                }

                var center = GetMapScreenPosition(spot.Position);
                var size = new Vector2(220.0f, 200.0f) * Scale;
                if (IsBoundedBy(mousePosition, center - size / 2.0f, center + size / 2.0f))
                {
                    AddClickedMapElement("TreasureMaps", spot.RankId, spot.RankName, spot.Position);
                }
            }
        }

        foreach (var marker in GetLocalCategoryMarkersForCurrentMap())
        {
            if (!ShouldDrawLocalCategoryMarker(marker))
            {
                continue;
            }

            var center = GetMapScreenPosition(marker.Position);
            var radius = marker.Radius > 0 ? marker.Radius * Scale : 10.0f * ImGuiHelpers.GlobalScale;
            if (Vector2.Distance(mousePosition, center) <= MathF.Max(radius, selectionRadius))
            {
                AddClickedMapElement(marker.Category, marker.RowId, marker.Name, marker.Position);
            }
        }

        if (ShouldDrawContent("FATE", GetCurrentContentScope()))
        {
            foreach (var fate in fateTable)
            {
                if (fate is null || !fateTable.IsValid(fate) || fate.TerritoryType.RowId != currentTerritory || fate.State is not (FateState.Preparing or FateState.Running))
                {
                    continue;
                }
                if (!MatchesMapSearch("FATE", fate.Name.ToString(), fate.FateId.ToString(), fate.Level.ToString()))
                {
                    continue;
                }

                var center = GetMapScreenPosition(fate.Position);
                var radius = fate.Radius * GetMapScaleFactor() * Scale;
                if (Vector2.Distance(mousePosition, center) <= MathF.Max(selectionRadius, MathF.Min(radius, 32.0f)))
                {
                    AddClickedMapElement("FATE", fate.FateId, fate.Name.ToString(), fate.Position);
                }
            }
        }
    }

    private void AddClickedMapElement(string type, uint rowId, string name, Vector3 position, MapMarkerLink? link = null)
    {
        var element = new ClickedMapElement(type, rowId, name, position, link);
        if (!clickedMapElements.Contains(element))
        {
            clickedMapElements.Add(element);
        }
    }

    private AkuGameObject CreateMapElementObject(string type, uint rowId, string name, Vector3 position)
    {
        return new AkuGameObject
        {
            created_at = DateTimeOffset.Now,
            t = type,
            name = name,
            mid = currentMap,
            zid = currentTerritory,
            pos = position,
            bid = rowId,
            tint = Vector4.One,
        };
    }

    private static bool IsFriend(ICharacter character)
    {
        return character.StatusFlags.HasFlag(StatusFlags.Friend);
    }

    private Vector4 GetPlayerMarkerTint(IGameObject gameObject, Vector4 fallback)
    {
        if (!configuration.ColorPlayerMarkersByClass || gameObject is not ICharacter character)
        {
            return fallback;
        }

        return GetPlayerMarkerTint(character.ClassJob.RowId, fallback);
    }

    private Vector4 GetPlayerMarkerTint(uint classJobId, Vector4 fallback)
    {
        if (!configuration.ColorPlayerMarkersByClass)
        {
            return fallback;
        }

        if (!dataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>().TryGetRow(classJobId, out var classJob))
        {
            return fallback;
        }

        return classJob.JobType switch
        {
            1 => new Vector4(0.25f, 0.48f, 1.0f, 1.0f),
            2 or 6 => new Vector4(0.25f, 0.95f, 0.45f, 1.0f),
            3 or 4 or 5 => new Vector4(1.0f, 0.05f, 0.05f, 1.0f),
            _ when IsClassJobCategory(classJob, "Disciple of the Hand") => new Vector4(0.95f, 0.75f, 0.25f, 1.0f),
            _ when IsClassJobCategory(classJob, "Disciple of the Land") => new Vector4(0.35f, 0.9f, 0.85f, 1.0f),
            _ => fallback,
        };
    }

    private bool IsClassJobCategory(Lumina.Excel.Sheets.ClassJob classJob, string englishCategoryName)
    {
        return dataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJobCategory>(ClientLanguage.English).TryGetRow(classJob.ClassJobCategory.RowId, out var category)
               && category.Name.ToString() == englishCategoryName;
    }

    private unsafe void DrawFlagMarker()
    {
        var agentMap = AgentMap.Instance();
        if (agentMap->FlagMarkerCount == 0)
        {
            return;
        }

        var flag = agentMap->FlagMapMarkers[0];
        if (flag.MapId != currentMap || flag.TerritoryId != currentTerritory)
        {
            return;
        }
        if (!MatchesMapSearch("Flag", "Map flag", currentMap.ToString(), currentTerritory.ToString()))
        {
            return;
        }

        var position = new Vector3(flag.XFloat, 0, flag.YFloat);
        var iconId = flag.MapMarker.IconId == 0 ? 60561 : flag.MapMarker.IconId;
        DrawFlagIcon((int)iconId, position);
        if (ImGui.IsItemHovered() && IsMouseInsideMapCanvas())
        {
            ImGui.SetTooltip($"Flag: {flag.XFloat:F1}, {flag.YFloat:F1}");
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                AddClickedMapElement("Flag", currentMap, "Flag", position);
                AddNearbyMapElementsToSelection();
                AddNearbyOtherPlayersToSelection();
                ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
            }

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                agentMap->FlagMarkerCount = 0;
                lastFocusedFlag = null;
                suppressFlagPlacement = true;
            }
        }
    }

    private unsafe void DrawPlacedMapMarkers()
    {
        var agentMap = AgentMap.Instance();
        if (agentMap == null)
        {
            return;
        }

        for (var i = 0; i < agentMap->TempMapMarkerCount && i < agentMap->TempMapMarkers.Length; i++)
        {
            var marker = agentMap->TempMapMarkers[i];
            DrawPlacedMapMarker(marker.MapMarker, marker.TooltipText.ToString());
        }
    }

    private void DrawPlacedMapMarker(MapMarkerBase marker, string tooltip)
    {
        var iconId = marker.IconId != 0 ? marker.IconId : marker.SecondaryIconId;
        if (iconId == 0)
        {
            return;
        }

        var position = GetMapPositionForMarker(marker);
        if (!IsBoundedBy(position, Vector2.Zero, new Vector2(2048, 2048)))
        {
            return;
        }

        DrawPlacedMapMarkerIcon((int)iconId, position, tooltip);
    }

    private void DrawPlacedMapMarkerIcon(int iconId, Vector2 mapPosition, string tooltip)
    {
        var name = string.IsNullOrWhiteSpace(tooltip) ? "Placed map marker" : tooltip;
        if (!MatchesMapSearch("PlacedMapMarker", "Placed map marker", name, iconId.ToString()))
        {
            return;
        }

        var texture = textureProvider.GetFromGameIcon(iconId).GetWrapOrEmpty();
        var size = texture.Size / 2.0f;
        var p = mapPosition * Scale + DrawPosition - size / 2.0f;

        if (configuration.DrawDebugSquares)
        {
            ImGui.SetCursorPos(p);
            var cursorPos = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddRect(cursorPos, cursorPos + size, ImGui.GetColorU32(configuration.TextColor), 3.0f);
        }

        ImGui.SetCursorPos(p);
        ImGui.Image(texture.Handle, size);
        if (ImGui.IsItemHovered() && IsMouseInsideMapCanvas() && !string.IsNullOrWhiteSpace(tooltip))
        {
            ImGui.SetTooltip(tooltip);
        }

        if (ImGui.IsItemHovered() && IsMouseInsideMapCanvas() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            AddClickedMapElement("PlacedMapMarker", (uint)iconId, name, GetWorldPositionForMapCoordinate(mapPosition));
            AddNearbyMapElementsToSelection();
            AddNearbyOtherPlayersToSelection();
            ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
        }
    }

    private unsafe void DrawFieldMarkers()
    {
        if (currentMap != clientState.MapId)
        {
            return;
        }

        var markingController = MarkingController.Instance();
        if (markingController == null)
        {
            return;
        }

        var labels = new[] { "A", "B", "C", "D", "1", "2", "3", "4" };
        for (var i = 0; i < markingController->FieldMarkers.Length && i < labels.Length; i++)
        {
            var marker = markingController->FieldMarkers[i];
            if (!marker.Active)
            {
                continue;
            }

            DrawFieldMarker(i, labels[i], marker.Position);
        }
    }

    private void DrawFieldMarker(int markerIndex, string label, Vector3 position)
    {
        if (!MatchesMapSearch("FieldMarker", "Field marker", $"Field Marker {label}", label, markerIndex.ToString()))
        {
            return;
        }

        var center = GetMapScreenPosition(position);
        var iconId = GetFieldMarkerIconId(markerIndex);
        var texture = textureProvider.GetFromGameIcon(iconId).GetWrapOrEmpty();
        var size = new Vector2(39.0f * ImGuiHelpers.GlobalScale);

        ImGui.GetWindowDrawList().AddImage(texture.Handle, center - size / 2.0f, center + size / 2.0f);

        if (IsMouseInsideMapCanvas() && Vector2.Distance(ImGui.GetMousePos(), center) <= size.X / 2.0f)
        {
            ImGui.SetTooltip($"Field Marker {label}: {position.X:F1}, {position.Z:F1}");
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                AddClickedMapElement("FieldMarker", (uint)markerIndex, $"Field Marker {label}", position);
                AddNearbyMapElementsToSelection();
                AddNearbyOtherPlayersToSelection();
                ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
            }
        }
    }

    private static uint GetFieldMarkerIconId(int markerIndex)
    {
        return markerIndex switch
        {
            0 => 61241,
            1 => 61242,
            2 => 61243,
            3 => 61247,
            4 => 61244,
            5 => 61245,
            6 => 61246,
            7 => 61248,
            _ => 61241,
        };
    }

    private static Vector2 GetMapPositionForMarker(MapMarkerBase marker)
    {
        var rawPosition = new Vector2(marker.X, marker.Y);
        if (IsBoundedBy(rawPosition, Vector2.Zero, new Vector2(2048, 2048)))
        {
            return rawPosition;
        }

        var worldPosition = new Vector3(marker.X / 16.0f, 0, marker.Y / 16.0f);
        var mapPosition = GetMapCoordinateFor3D(worldPosition);
        if (IsBoundedBy(mapPosition, Vector2.Zero, new Vector2(2048, 2048)))
        {
            return mapPosition;
        }

        return rawPosition / 16.0f;
    }

    private void DrawFlagIcon(int iconid, Vector3 position)
    {
        var texture = textureProvider.GetFromGameIcon(iconid).GetWrapOrEmpty();
        var size = texture.Size / 2.0f;
        var p = ((GetMapCoordinateFor3D(position)) * Scale) + DrawPosition - size / 2.0f;

        if (configuration.DrawDebugSquares)
        {
            ImGui.SetCursorPos(p);
            var cursorPos = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddRect(cursorPos, cursorPos + size, ImGui.GetColorU32(configuration.TextColor), 3.0f);
        }

        ImGui.SetCursorPos(p);
        ImGui.Image(texture.Handle, size);
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
                ProcessMapFlagClick();
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

    private unsafe void ProcessMapFlagClick()
    {
        if (suppressFlagPlacement)
        {
            suppressFlagPlacement = false;
            return;
        }

        if (!HoveredFlags.HasFlag(HoverFlags.MapTexture)) return;
        if (!ImGui.GetIO().KeyCtrl) return;
        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Right)) return;

        var mapCoordinate = GetMouseMapCoordinate();
        if (!IsBoundedBy(mapCoordinate, Vector2.Zero, new Vector2(2048, 2048)))
        {
            return;
        }

        var agentMap = AgentMap.Instance();
        agentMap->SetFlagMapMarker(currentTerritory, currentMap, GetWorldPositionForMapCoordinate(mapCoordinate));
        var flag = agentMap->FlagMapMarkers[0];
        lastFocusedFlag = (flag.TerritoryId, flag.MapId, flag.XFloat, flag.YFloat);
        framework.RunOnTick(() => AgentChatLog.Instance()->InsertTextCommandParam(FlagTextCommandParamId, false));
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
            keepPlayerCenteredPaused = true;
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

        CenterOnWorldPosition(localPlayer.Position);
    }

    private unsafe void ProcessPendingFlagFocus()
    {
        if (!pendingFlagFocus)
        {
            return;
        }

        var agentMap = AgentMap.Instance();
        if (agentMap == null || agentMap->FlagMarkerCount == 0)
        {
            pendingFlagFocus = false;
            return;
        }

        var flag = agentMap->FlagMapMarkers[0];
        if (flag.MapId != currentMap || flag.TerritoryId != currentTerritory)
        {
            return;
        }

        keepPlayerCenteredPaused = true;
        CenterOnWorldPosition(new Vector3(flag.XFloat, 0, flag.YFloat));
        pendingFlagFocus = false;
    }

    private void CenterOnWorldPosition(Vector3 position)
    {
        DrawOffset = GetMapCenterOffsetVector() - GetMapCoordinateFor3D(position);
    }

    private Vector2 GetMouseMapCoordinate()
    {
        return (ImGui.GetMousePos() - currentMapScreenPosition - DrawPosition) / Scale;
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

    public static Vector2 GetMapCoordinateFor3D(Vector3 pos)
    {
        var twoD = new Vector2(pos.X, pos.Z);
        var mapcoord = ((twoD + GetRawMapOffsetVector()) * GetMapScaleFactor()) + GetMapCenterOffsetVector();
        return mapcoord;
    }
    public static Vector3 GetWorldPositionForMapCoordinate(Vector2 mapCoordinate)
    {
        var twoD = ((mapCoordinate - GetMapCenterOffsetVector()) / GetMapScaleFactor()) - GetRawMapOffsetVector();
        return new Vector3(twoD.X, 0, twoD.Y);
    }
    public static Vector2 GetPlayerMapPosition(Vector3 vec) => new Vector2(vec.X, vec.Z) * GetMapScaleFactor();
    private static Vector2 ImRotate(Vector2 v, float cosA, float sinA) => new(v.X * cosA - v.Y * sinA, v.X * sinA + v.Y * cosA);

    /// <summary>
    /// Offset Vector of SelectedX, SelectedY, scaled with SelectedSizeFactor
    /// </summary>
    public static Vector2 GetMapOffsetVector() => GetRawMapOffsetVector() * GetMapScaleFactor();

    /// <summary>
    /// Unscaled Vector of SelectedX, SelectedY
    /// </summary>
    public static unsafe Vector2 GetRawMapOffsetVector()
    {
        var agentMap = AgentMap.Instance();
        if (agentMap != null && (agentMap->SelectedMapPath.Length > 0 || agentMap->SelectedMapBgPath.Length > 0))
        {
            cachedRawMapOffsetVector = new Vector2(agentMap->SelectedOffsetX * -1, agentMap->SelectedOffsetY * -1);
        }

        return cachedRawMapOffsetVector;
    }

    /// <summary>
    /// Selected Scale Factor
    /// </summary>
    public static unsafe float GetMapScaleFactor()
    {
        var agentMap = AgentMap.Instance();
        if (agentMap != null && (agentMap->SelectedMapPath.Length > 0 || agentMap->SelectedMapBgPath.Length > 0))
        {
            cachedMapScaleFactor = agentMap->SelectedMapSizeFactorFloat;
        }

        return cachedMapScaleFactor;
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

    private async void FetchAkuGameObjectsFromAkuAPI(uint mid)
    {
        await Task.Run(async () =>
        {
            var objs = await uploadManager.DownloadMapContentFromAPI(mid);
            downloadList.Clear();
            foreach (var obj in objs)
            {
                if (obj.t == "EventNpc")
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
                if (obj.t == "BattleNpc")
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
                if (obj.t == "EventObj")
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
                if(obj.t == "GatheringPoint") {
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
                if(obj.GetUniqueId() == null) {
                    log.Debug($"ERROR: Could not GetUniqueId of obj.bid {obj.bid} name {obj.name}");
                    continue;
                }
                if (!downloadList.TryAdd(obj.GetUniqueId()!, obj))
                {
                    log.Debug($"AkuAPI Download: Duplicate Key {obj.GetUniqueId()}");
                }
            }
            log.Debug($"{downloadList.Count} objects added to downloadList");
        });
    }

}
