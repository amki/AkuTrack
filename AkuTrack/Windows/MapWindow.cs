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
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
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
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly ObjTrackManager objTrackManager;
    private readonly UploadManager uploadManager;
    private readonly WindowSystem windowSystem;
    private readonly IDataManager dataManager;
    private readonly IClientState clientState;
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
    private string currentPath = string.Empty;
    private uint currentMap = 0;
    private uint currentTerritory = 0;
    public float ZoomSpeed = 0.25f;
    private Vector2 currentMapPixelSize = new(0, 0);

    private List<AkuGameObject> clickedObjects = new();

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
        IClientState clientState,
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
        this.clientState = clientState;
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

    public unsafe override void Draw()
    {
        UpdateDrawOffset();

        HoveredFlags = HoverFlags.Nothing;

        if (IsBoundedBy(ImGui.GetMousePos(), ImGui.GetCursorScreenPos(), ImGui.GetCursorScreenPos() + ImGui.GetContentRegionMax()))
        {
            HoveredFlags |= HoverFlags.Window;
        }

        using (var renderChild = ImRaii.Child("render_child", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar))
        {
            
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
        if (clickedObjects.Count > 0)
        {
            DrawAkuObjectContextMenu(clickedObjects);
            if(!ImGui.IsPopupOpen("AkuTrack_AkuObject_Context_Menu")) {
                if(clickedObjects.Count > 0)
                    clickedObjects.Clear();
            }
        }
        DrawMapBackground();
        if (ImGui.IsItemHovered())
        {
            HoveredFlags |= HoverFlags.MapTexture;
        }
        
        // Only draw player and from ObjectTable if we are looking at the map we are currently in
        if (currentMap == clientState.MapId)
        {
            if (clientState.LocalPlayer is { } localPlayer)
            {
                DrawPlayerIcon(localPlayer.Position, localPlayer.Rotation);
            }
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
                DrawMapIcon(row.Icon, pos, 3.14f, row.PlaceNameSubtext.Value.Name.ToString(), row.SubtextOrientation);
            }
        } catch(ArgumentOutOfRangeException) {
            // FIXME: How to get markers from region maps?!?
            //log.Debug($"Could not find Markers for Territory {currentTerritory}");
        }
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
            DrawIcon(60424, obj.pos, obj.r, obj.tint);
        }
        else if (obj.t == "EventObj")
        {
            if (!configuration.DrawEObj)
                return;
            if (obj.bid == 2000401)
                DrawIcon(60425, obj.pos, obj.r, obj.tint);
            else if (obj.bid == 2000402)
                DrawIcon(60570, obj.pos, obj.r, obj.tint);
            else if (obj.bid == 2000470)
                DrawIcon(60460, obj.pos, obj.r, obj.tint);
            else
                DrawIcon(60353, obj.pos, obj.r, obj.tint);
        }
        else if (obj.t == "BattleNpc")
        {
            if(!configuration.DrawBNpc)
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
        else
            DrawIcon(60515, obj.pos, obj.r, obj.tint);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
            clickedObjects.Add(obj);
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

    public void DrawAkuObjectContextMenu(List<AkuGameObject> objs)
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

    private void DrawMapIcon(int iconid, Vector2 position, float rotation, string text, byte subtextOrientation)
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
