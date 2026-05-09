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
    private readonly record struct ClickedSightseeingLogEntry(uint RowId, string Name, string Description, string Time, string Weather, string Emote);

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
    public float ZoomSpeed = 0.25f;
    private Vector2 currentMapPixelSize = new(0, 0);
    private Vector2 currentMapScreenPosition = new(0, 0);
    private bool suppressFlagPlacement;
    private bool pendingFlagFocus;
    private (uint TerritoryId, uint MapId, float X, float Y)? lastFocusedFlag;

    private List<AkuGameObject> clickedObjects = new();
    private List<ClickedPlayer> clickedPlayers = new();
    private List<ClickedAetheryte> clickedAetherytes = new();
    private List<ClickedSightseeingLogEntry> clickedSightseeingLogEntries = new();

    private readonly MapContextMenu mapContextMenu = new();
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

        using (var renderChild = ImRaii.Child("render_child", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar))
        {
            currentMapScreenPosition = ImGui.GetWindowPos();
            DrawMapElements();
            currentMapPixelSize = ImGui.GetWindowSize();

            // Reset Draw Position for Overlay Extras
            ImGui.SetCursorPos(Vector2.Zero);
            //DrawToolbar();
            bottomBar.Draw(HoveredFlags.HasFlag(HoverFlags.MapTexture), currentMapPixelSize, DrawPosition, DrawOffset, Scale);
        }


        if (ImGui.IsItemHovered())
        {
            HoveredFlags |= HoverFlags.WindowInnerFrame;
        }
        ProcessInputs();
    }

    private void DrawMapElements() {
        if (clickedObjects.Count > 0 || clickedPlayers.Count > 0 || clickedAetherytes.Count > 0 || clickedSightseeingLogEntries.Count > 0)
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
            }
        }
        DrawMapBackground();
        if (ImGui.IsItemHovered())
        {
            HoveredFlags |= HoverFlags.MapTexture;
        }
        ProcessPendingFlagFocus();

        var drawPlayerMarkersInBackground = ImGui.GetIO().KeyCtrl;

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
                DrawAkuGameObject(o.Value);
            }
        }
        if(configuration.DrawRemoteMarker) {
            foreach (var o in downloadList)
            {
                if (!objTrackManager.seenList.ContainsKey(o.Key))
                    DrawAkuGameObject(o.Value);
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
                DrawMapIcon(row.Icon, pos, 3.14f, row.PlaceNameSubtext.Value.Name.ToString(), row.SubtextOrientation, row.PlaceNameSubtext.RowId);
            }
        } catch(ArgumentOutOfRangeException) {
            // FIXME: How to get markers from region maps?!?
            //log.Debug($"Could not find Markers for Territory {currentTerritory}");
        }

        DrawFateMarkers();
        DrawSightseeingLogMarkers();
        DrawPlacedMapMarkers();
        DrawFlagMarker();

        if (currentMap == clientState.MapId && !drawPlayerMarkersInBackground)
        {
            DrawLocalPlayerAndPartyIcons();
        }

        DrawFieldMarkers();
    }

    private unsafe void DrawMapBackground()
    {
        if (AgentMap.Instance()->SelectedMapBgPath.Length is 0)
        {
            var gameMapPath = $"{AgentMap.Instance()->SelectedMapPath.ToString()}.tex";
            if (currentPath != gameMapPath)
            {
                log.Debug($"MapWindow: FLAT| Texture switched. oldMid {currentMap} new: {AgentMap.Instance()->SelectedMapId} old: {currentPath} new: {gameMapPath}");
                if (gameMapPath.Contains("region"))
                {
                    log.Debug("REGION MAP DETECTED!");
                    var vanillaBgPath = $"{AgentMap.Instance()->SelectedMapBgPath.ToString()}.tex";
                    var vanillaFgPath = $"{AgentMap.Instance()->SelectedMapPath.ToString()}.tex";
                    log.Debug($"BG Path: {vanillaBgPath} sFG Path: {vanillaFgPath}");
                }
                currentPath = gameMapPath;
                currentMap = AgentMap.Instance()->SelectedMapId;
                currentTerritory = AgentMap.Instance()->SelectedTerritoryId;
                FetchAkuGameObjectsFromAkuAPI(AgentMap.Instance()->SelectedMapId);
            }
            var texture = textureProvider.GetFromGame($"{AgentMap.Instance()->SelectedMapPath.ToString()}.tex").GetWrapOrEmpty();

            ImGui.SetCursorPos(DrawPosition);
            ImGui.Image(texture.Handle, texture.Size * Scale);
        }
        else
        {
            var gameMapPath = $"{AgentMap.Instance()->SelectedMapBgPath.ToString()}.tex";
            if (currentPath != gameMapPath)
            {
                log.Debug($"MapWindow: BLEND| Texture switched. oldMid {currentMap} new: {AgentMap.Instance()->SelectedMapId} old: {currentPath} new: {gameMapPath}");
                currentPath = gameMapPath;
                currentMap = AgentMap.Instance()->SelectedMapId;
                currentTerritory = AgentMap.Instance()->SelectedTerritoryId;
                FetchAkuGameObjectsFromAkuAPI(AgentMap.Instance()->SelectedMapId);
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

    private unsafe IDalamudTextureWrap? LoadTexture()
    {
        var vanillaBgPath = $"{AgentMap.Instance()->SelectedMapBgPath.ToString()}.tex";
        var vanillaFgPath = $"{AgentMap.Instance()->SelectedMapPath.ToString()}.tex";

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

    private void DrawAkuGameObject(AkuGameObject obj) {
        if (obj.mid != currentMap)
            return;
        if (obj.t == "EventNpc")
        {
            if (!configuration.DrawENpc)
                return;
            // check if we need special icons for ENPCs currently implemented for TrippleTriad
            DrawIcon(enpcShopResolver.GetPreferredMapIconId(obj.bid), obj.pos, obj.r, obj.tint);
        }
        else if (obj.t == "EventObj")
        {
            if (!configuration.DrawEObj)
                return;
            if (obj.bid == 2000401) // summoning bell
                DrawIcon(60425, obj.pos, obj.r, obj.tint);
            else if (obj.bid == 2000402) // market board
                DrawIcon(60570, obj.pos, obj.r, obj.tint);
            else if (obj.bid == 2000470) // company chest
                DrawIcon(60460, obj.pos, obj.r, obj.tint);
            else
                DrawIcon(60353, obj.pos, obj.r, obj.tint);
        }
        else if (obj.t == "BattleNpc")
        {
            if (!configuration.DrawBNpc)
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
            if (!configuration.DrawGatheringPoint)
                return;
            if (!dataManager.GetExcelSheet<Lumina.Excel.Sheets.GatheringPoint>().TryGetRow(obj.bid, out var gatheringPointRow))
            {
                log.Debug($"GatheringPoint {obj.bid} did not have a row in GatheringPoint sheet.");
                return;
            }
            DrawIcon(gatheringPointRow.GatheringPointBase.Value.GatheringType.Value.IconMain, obj.pos, obj.r, obj.tint);
        }
        else if (obj.t == "Treasure")
            DrawIcon(60354, obj.pos, obj.r, obj.tint);
        else
            DrawIcon(60515, obj.pos, obj.r, obj.tint);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
            clickedObjects.Add(obj);
            AddNearbyOtherPlayersToSelection();
        }
        if (ImGui.IsItemHovered())
        {
            DrawTooltip(obj);
            DrawIcon(60429, obj.pos, 3.14f, obj.tint);
        }
    }

    private void DrawTooltip(AkuGameObject obj) {
        ImGui.SetTooltip($"Created: {obj.created_at}\nLastSeen: {obj.lastseen_at}\n\nName: {obj.name}\nType: {obj.t}\nBaseID: {obj.bid}");
    }

    public void DrawAkuObjectContextMenu()
    {
        using var contextMenu = ImRaii.ContextPopup("AkuTrack_AkuObject_Context_Menu");
        if (!contextMenu) return;

        foreach (var obj in clickedObjects)
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
            if (ImGui.BeginMenu($"Vista #{entry.RowId}: {entry.Name}"))
            {
                DrawSightseeingLogEntryDetails(entry);
                ImGui.EndMenu();
            }
        }
    }

    private static void DrawSightseeingLogEntryDetails(ClickedSightseeingLogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            ImGui.TextWrapped(entry.Description);
        }

        if (!string.IsNullOrWhiteSpace(entry.Emote))
        {
            ImGui.TextUnformatted($"Emote: {entry.Emote}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Time))
        {
            ImGui.TextUnformatted($"Time: {entry.Time}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Weather))
        {
            ImGui.TextUnformatted($"Weather: {entry.Weather}");
        }
    }

    private void DrawIcon(int iconid, Vector3 position, float rotation, Vector4 tint)
    {
        var texture = textureProvider.GetFromGameIcon(iconid).GetWrapOrEmpty();

        var p = ((GetMapCoordinateFor3D(position)) * Scale) + DrawPosition - (texture.Size / 4.0f);

        if (configuration.DrawDebugSquares)
        {
            ImGui.SetCursorPos(p);
            var cursorPos = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddRect(cursorPos, cursorPos + (texture.Size / 2.0f), ImGui.GetColorU32(configuration.TextColor), 3.0f);
        }
        ImGui.SetCursorPos(p);
        //log.Debug($"@ {position} Drawing to {p} with scale {Scale} DrawPosition: {DrawPosition}");
        ImGui.Image(texture.Handle, texture.Size / 2.0f, Vector2.Zero, Vector2.One, tint);
    }

    private void DrawMapIcon(int iconid, Vector2 position, float rotation, string text, byte subtextOrientation, uint placeNameSubtextId = 0)
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
            }
        } else {
            var p = (position * Scale) + DrawPosition - (texture.Size / 4.0f);
            ImGui.SetCursorPos(p);
            ImGui.Image(texture.Handle, texture.Size / 2.0f);
            ProcessAetheryteMapIconClick(placeNameSubtextId);
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
        var size = texture.Size / 2.0f * Scale * iconScale;

        var p = currentMapScreenPosition +
                           DrawPosition +
                           (GetPlayerMapPosition(pos) +
                            GetMapOffsetVector() +
                            GetMapCenterOffsetVector()) * Scale;
        //var p = ((GetMapCoordinateFor3D(pos)) * Scale) + DrawPosition - (texture.Size / 4.0f * Scale);
        var vectors = GetRotationVectors(angle, p, size);

        //log.Debug($"@ {position} Drawing to {p} with scale {Scale} DrawPosition: {DrawPosition}");
        ImGui.GetWindowDrawList().AddImageQuad(texture.Handle, vectors[0], vectors[1], vectors[2], vectors[3], Vector2.Zero, new Vector2(1, 0), Vector2.One, new Vector2(0, 1), ImGui.GetColorU32(tint));
        return IsBoundedBy(ImGui.GetMousePos(), p - size / 2.0f, p + size / 2.0f);
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
        var coneOrigin = center - direction * (7.0f * Scale);
        var coneLength = 36.0f * Scale;
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

            var tint = GetPlayerMarkerTint(member.ClassJob.RowId, new Vector4(0.3f, 0.85f, 1.0f, 1.0f));
            if (DrawPlayerIcon(memberObject.Position, memberObject.Rotation, tint, 0.75f))
            {
                ImGui.SetTooltip($"Party Member: {member.Name}");
            }
        }
    }

    private void ProcessAetheryteMapIconClick(uint placeNameSubtextId)
    {
        if (placeNameSubtextId == 0 || !ImGui.IsItemClicked(ImGuiMouseButton.Left))
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
            ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
        }
    }

    private static unsafe void TeleportToAetheryte(ClickedAetheryte aetheryte)
    {
        Telepo.Instance()->Teleport(aetheryte.AetheryteId, aetheryte.SubIndex);
    }

    private void DrawSightseeingLogMarkers()
    {
        if (!configuration.DrawSightseeingLogEntries)
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
            DrawSightseeingLogMarker(BuildSightseeingLogEntry(entry), position);
        }
    }

    private void DrawSightseeingLogMarker(ClickedSightseeingLogEntry entry, Vector3 position)
    {
        var texture = textureProvider.GetFromGameIcon(60071).GetWrapOrEmpty();
        var center = GetMapScreenPosition(position);
        var size = texture.Size / 2.0f;
        var min = center - size / 2.0f;

        ImGui.SetCursorPos(min - currentMapScreenPosition);
        ImGui.Image(texture.Handle, size);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"Vista #{entry.RowId}: {entry.Name}");
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            clickedSightseeingLogEntries.Clear();
            clickedSightseeingLogEntries.Add(entry);
            ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
        }
    }

    private static ClickedSightseeingLogEntry BuildSightseeingLogEntry(Lumina.Excel.Sheets.Adventure entry)
    {
        var time = string.Empty;
        var weather = string.Empty;
        var emote = entry.Emote.Value.Name.ToString();

        return new ClickedSightseeingLogEntry(
            entry.RowId,
            entry.Name.ToString(),
            entry.Description.ToString(),
            time,
            weather,
            emote);
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
            if (DrawOtherPlayerCircle(gameObject.Position, isFriend))
            {
                ImGui.SetTooltip($"{(isFriend ? "Friend" : "Player")}: {character.Name}");
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
                    AddClickedPlayer(character, isFriend);
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

        return IsBoundedBy(ImGui.GetMousePos(), center - new Vector2(radius), center + new Vector2(radius));
    }

    private void DrawFateMarkers()
    {
        if (!configuration.DrawFates)
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
        if (Vector2.Distance(ImGui.GetMousePos(), center) <= MathF.Max(iconHoverRadius, MathF.Min(radius, 32.0f)))
        {
            ImGui.SetTooltip($"FATE: {fate.Name}\nLevel: {fate.Level}\nProgress: {fate.Progress}%\nTime: {FormatTimeRemaining(fate.TimeRemaining)}");
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

        var position = new Vector3(flag.XFloat, 0, flag.YFloat);
        var iconId = flag.MapMarker.IconId == 0 ? 60561 : flag.MapMarker.IconId;
        DrawFlagIcon((int)iconId, position);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"Flag: {flag.XFloat:F1}, {flag.YFloat:F1}");
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

        for (var i = 0; i < agentMap->MapMarkerCount && i < agentMap->MapMarkers.Length; i++)
        {
            var marker = agentMap->MapMarkers[i];
            if (marker.DataType != 0 || marker.DataKey != 0)
            {
                continue;
            }

            DrawPlacedMapMarker(marker.MapMarker, string.Empty);
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
        if (ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(tooltip))
        {
            ImGui.SetTooltip(tooltip);
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
        var center = GetMapScreenPosition(position);
        var iconId = GetFieldMarkerIconId(markerIndex);
        var texture = textureProvider.GetFromGameIcon(iconId).GetWrapOrEmpty();
        var size = new Vector2(39.0f * ImGuiHelpers.GlobalScale);

        ImGui.GetWindowDrawList().AddImage(texture.Handle, center - size / 2.0f, center + size / 2.0f);

        if (Vector2.Distance(ImGui.GetMousePos(), center) <= size.X / 2.0f)
        {
            ImGui.SetTooltip($"Field Marker {label}: {position.X:F1}, {position.Z:F1}");
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
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !isDragStarted)
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
    public static unsafe Vector2 GetRawMapOffsetVector() => new(AgentMap.Instance()->SelectedOffsetX * -1, AgentMap.Instance()->SelectedOffsetY * -1);

    /// <summary>
    /// Selected Scale Factor
    /// </summary>
    public static unsafe float GetMapScaleFactor() => AgentMap.Instance()->SelectedMapSizeFactorFloat;

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
