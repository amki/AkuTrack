using AkuTrack.ApiTypes;
using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
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
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Data.Files;
using Lumina.Excel.Sheets;
using Lumina.Excel.Sheets.Experimental;
using Lumina.Models.Materials;
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

    private Vector2 currentMapPixelSize = new(0, 0);
    private Vector2 currentMapScreenPosition = new(0, 0);

    public float ZoomSpeed = 0.25f;

    private List<AkuGameObject> clickedObjects = new();
    private List<Lumina.Excel.Sheets.MapMarker> clickedMarkers = new();

    private readonly MapContextMenu mapContextMenu = new();
    private readonly BottomBar bottomBar;

    

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
        this.bottomBar = bottomBar;
        this.windowSystem = windowSystem;
        this.textureProvider = textureProvider;
        this.textureSubstitutionProvider = textureSubstitutionProvider;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() { }

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
        if (clickedObjects.Count > 0 || clickedMarkers.Count > 0)
        {
            DrawAkuObjectContextMenu(clickedObjects, clickedMarkers);
            if(!ImGui.IsPopupOpen("AkuTrack_AkuObject_Context_Menu")) {
                if (clickedObjects.Count > 0)
                    clickedObjects.Clear();
                if(clickedMarkers.Count > 0)
                    clickedMarkers.Clear();
            }
        }
        DrawMapBackground();
        if (ImGui.IsItemHovered())
        {
            HoveredFlags |= HoverFlags.MapTexture;
        }

        if (configuration.DrawRemoteMarker)
        {
            foreach (var o in objTrackManager.downloadList)
            {
                if (!objTrackManager.seenList.ContainsKey(o.Key))
                    DrawAkuGameObject(o.Value);
            }
        }

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
            foreach (var o in objTrackManager.seenList)
            {
                DrawAkuGameObject(o.Value);
            }
        }
        
        try
        {
            var rows = dataManager.GetSubrowExcelSheet<Lumina.Excel.Sheets.MapMarker>().GetRow(mapStateManager.currentMap.MapMarkerRange);
            foreach (var row in rows)
            {
                if(IsMapMarker(row.Icon)) {
                    continue;
                }
                if (row.X == 0 && row.Y == 0)
                {
                    continue;
                }
                var pos = new Vector2(row.X, row.Y);
                //log.Debug($"Icon {row.Icon} to {pos} {row.RowOffset} |{row.PlaceNameSubtext.Value.Name}|");
                DrawMapIcon(row.Icon, pos, 3.14f, row.PlaceNameSubtext.Value.Name.ToString(), row.SubtextOrientation);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
                    clickedMarkers.Add(row);
                }
            }
        } catch(ArgumentOutOfRangeException) {
            // FIXME: How to get markers from region maps?!?
            //log.Debug($"Could not find Markers for Territory {currentTerritory}");
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

    private void DrawAkuGameObject(AkuGameObject obj) {
        if (obj.mid != mapStateManager.currentMap.RowId)
            return;
        if(!configuration.shouldDraw[obj.objectKind]) {
            return;
        }
        if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc)
        {
            DrawIcon((int)IconIds.EventNpc, obj);
        }
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj)
        {
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
            DrawIcon(gatheringPointRow.GatheringPointBase.Value.GatheringType.Value.IconMain, obj);
        }
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure)
            DrawIcon((int)IconIds.Treasure, obj);
        else
            DrawIcon((int)IconIds.Unknown, obj);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
            clickedObjects.Add(obj);
        }
        if (ImGui.IsItemHovered())
        {
            DrawTooltip(obj);
            DrawIcon((int)IconIds.Hover, obj);
        }
    }

    private void DrawTooltip(AkuGameObject obj) {
        ImGui.SetTooltip($"Created: {obj.created_at}\nLastSeen: {obj.lastseen_at}\n\nName: {obj.name}\nType: {obj.t}\nBaseID: {obj.bid}");
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
            if(ImGui.MenuItem($"MapMarker {mark.PlaceNameSubtext.Value.Name.ToString()}")) {
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

        var p = ImGui.GetWindowPos() +
                           DrawPosition +
                           (GetPlayerMapPosition(pos) +
                            GetMapOffsetVector() +
                            GetMapCenterOffsetVector()) * Scale;
        //var p = ((GetMapCoordinateFor3D(pos)) * Scale) + DrawPosition - (texture.Size / 4.0f * Scale);
        var vectors = GetRotationVectors(angle, p, texture.Size / 2.0f * Scale);

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
    public Vector2 GetRawMapOffsetVector() => new(mapStateManager.currentMap.OffsetX, mapStateManager.currentMap.OffsetY);
    //public unsafe Vector2 GetRawMapOffsetVector() => new(AgentMap.Instance()->SelectedOffsetX * -1, AgentMap.Instance()->SelectedOffsetY * -1);

    /// <summary>
    /// Selected Scale Factor
    /// </summary>
    public float GetMapScaleFactor() => mapStateManager.currentMap.SizeFactor /  100;

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
