using AkuTrack.ApiTypes;
using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
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
using System;
using System.ComponentModel.Design;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using static Lumina.Data.Parsing.Layer.LayerCommon;

namespace AkuTrack.Windows;

public class MapWindow : Window, IDisposable
{
    private readonly string goatImagePath;
    private readonly Plugin plugin;
    private readonly ObjTrackManager objTrackManager;
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
    private string blendedPath = string.Empty;
    public float ZoomSpeed = 0.25f;

    private readonly MapContextMenu mapContextMenu = new();
    private readonly AkuObjectContextMenu akuObjectContextMenu = new();

    // We give this window a hidden ID using ##.
    // The user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public MapWindow(
        ObjTrackManager objTrackManager,
        IDataManager dataManager,
        IClientState clientState,
        ITextureProvider textureProvider,
        ITextureSubstitutionProvider textureSubstitutionProvider,
        IPluginLog log
        )
        : base("AkuTrack - Map##With a hidden ID", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.log = log;
        this.dataManager = dataManager;
        this.clientState = clientState;
        this.objTrackManager = objTrackManager;
        this.textureProvider = textureProvider;
        this.textureSubstitutionProvider = textureSubstitutionProvider;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() { }

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

    public unsafe override void Draw()
    {
        UpdateDrawOffset();
        /*
        var x = dataManager.GetExcelSheet<Lumina.Excel.Sheets.TripleTriadCardResident>().ToList();
        ImGui.Text("TripleTriadCardResident");
        foreach (var y in x)
        {
            ImGui.Text($"{y.RowId.ToString()}: {y.AcquisitionType.Value.Text.Value.Text}");
        }
        */

        HoveredFlags = HoverFlags.Nothing;

        if (IsBoundedBy(ImGui.GetMousePos(), ImGui.GetCursorScreenPos(), ImGui.GetCursorScreenPos() + ImGui.GetContentRegionMax()))
        {
            HoveredFlags |= HoverFlags.Window;
        }

        using (var renderChild = ImRaii.Child("render_child", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar))
        {
            
            DrawMapElements();

            // Reset Draw Position for Overlay Extras
            ImGui.SetCursorPos(Vector2.Zero);
            //DrawToolbar();
            //DrawCoordinateBar();
        }


        if (ImGui.IsItemHovered())
        {
            HoveredFlags |= HoverFlags.WindowInnerFrame;
        }
        ProcessInputs();
    
    }

    private void DrawMapElements() {
        DrawMapBackground();
        if (ImGui.IsItemHovered())
        {
            HoveredFlags |= HoverFlags.MapTexture;
        }
        if (clientState.LocalPlayer is { } localPlayer)
        {
            DrawIcon(60443, localPlayer.Position, localPlayer.Rotation);
        }
        foreach (var o in objTrackManager.seenList)
        {
            if (o.Value.mid != clientState.MapId)
                continue;
            if (o.Value.t == "EventNpc") { 
                DrawIcon(60424, o.Value.pos, o.Value.r);
            }
            else if(o.Value.t == "EventObj") {
                if (o.Value.bid == 2000401)
                    DrawIcon(60425, o.Value.pos, o.Value.r);
                else if (o.Value.bid == 2000402)
                    DrawIcon(60570, o.Value.pos, o.Value.r);
                else if (o.Value.bid == 2000470)
                    DrawIcon(60460, o.Value.pos, o.Value.r);
                else
                    DrawIcon(60353, o.Value.pos, o.Value.r);
            }
            else if (o.Value.t == "BattleNpc") {
                DrawIcon(60422, o.Value.pos, o.Value.r);
            }
            else if (o.Value.t == "Aetheryte") {
                var x = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>().GetRowOrDefault(o.Value.bid);
                if (x.Value.AethernetName.Value.Name.ToString() != string.Empty && x.Value.PlaceName.Value.Name.ToString() == string.Empty)
                {
                    DrawIcon(60430, o.Value.pos, 3.14f);
                }
                else
                {
                    DrawIcon(60453, o.Value.pos, 3.14f);
                }
            }
            else if (o.Value.t == "GatheringPoint") {
                var x = dataManager.GetExcelSheet<Lumina.Excel.Sheets.GatheringPoint>().GetRowOrDefault(o.Value.bid);
                DrawIcon(x.Value.GatheringPointBase.Value.GatheringType.Value.IconMain, o.Value.pos, o.Value.r);
            }
            else
                DrawIcon(60515, o.Value.pos, o.Value.r);
            if (ImGui.IsItemHovered()) {
                ImGui.SetTooltip($"Name: {o.Value.name}\nType: {o.Value.t}\nBaseID: {o.Value.bid}");
            }
        }
    }

    private void ProcessInputs() {
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            ImGui.OpenPopup("AkuTrack_Context_Menu");
        }
        else
        {
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
            ProcessMapDragDragging();
            ProcessMapDragEnd();
        }
        mapContextMenu.Draw(DrawOffset);
    }


    private unsafe void DrawMapBackground()
    {
        if (AgentMap.Instance()->SelectedMapBgPath.Length is 0)
        {
            var texture = textureProvider.GetFromGame($"{AgentMap.Instance()->SelectedMapPath.ToString()}.tex").GetWrapOrEmpty();

            ImGui.SetCursorPos(DrawPosition);
            ImGui.Image(texture.Handle, texture.Size * Scale);
        }
        else
        {
            if (blendedPath != AgentMap.Instance()->SelectedMapBgPath.ToString())
            {
                //fogTexture = null;
                blendedTexture?.Dispose();
                blendedTexture = LoadTexture();
                blendedPath = AgentMap.Instance()->SelectedMapBgPath.ToString();
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

    private void UpdateDrawOffset()
    {
        var childCenterOffset = ImGui.GetContentRegionAvail() / 2.0f;
        var mapCenterOffset = new Vector2(1024.0f, 1024.0f) * Scale;

        DrawPosition = childCenterOffset - mapCenterOffset + DrawOffset * Scale;
    }

    private void DrawIcon(int iconid, Vector3 position, float rotation)
    {
        var texture = textureProvider.GetFromGameIcon(iconid).GetWrapOrEmpty();
        //var angle = -rotation + MathF.PI / 2.0f;


        //var vectors = GetRotationVectors(angle, position, texture.Size / 2.0f * Scale);


        var p = ((GetMapCoordinateFor3D(position)) * Scale) + DrawPosition - (texture.Size / 4.0f * Scale);

        ImGui.SetCursorPos(p);
        //log.Debug($"@ {position} Drawing to {p} with scale {Scale} DrawPosition: {DrawPosition}");
        ImGui.Image(texture.Handle, texture.Size / 2.0f * Scale);
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
    private static Vector2 ImRotate(Vector2 v, float cosA, float sinA) => new(v.X * cosA - v.Y * sinA, v.X * sinA + v.Y * cosA);

    /// <summary>
    /// Offset Vector of SelectedX, SelectedY, scaled with SelectedSizeFactor
    /// </summary>
    public static Vector2 GetMapOffsetVector() => GetRawMapOffsetVector() * GetMapScaleFactor();

    /// <summary>
    /// Unscaled Vector of SelectedX, SelectedY
    /// </summary>
    public static unsafe Vector2 GetRawMapOffsetVector() => new(AgentMap.Instance()->SelectedOffsetX, AgentMap.Instance()->SelectedOffsetY);

    /// <summary>
    /// Selected Scale Factor
    /// </summary>
    public static unsafe float GetMapScaleFactor() => AgentMap.Instance()->SelectedMapSizeFactorFloat;

    /// <summary>
    /// 1024 vector, center offset vector
    /// </summary>
    public static Vector2 GetMapCenterOffsetVector() => new(1024.0f, 1024.0f);

    /// <summary>
    /// Offset for the top left corner of the drawn map
    /// </summary>
    public static Vector2 GetCombinedOffsetVector() => -GetMapOffsetVector() + GetMapCenterOffsetVector();

    private void ProcessMouseScroll()
    {
        if (ImGui.GetIO().MouseWheel is 0) return;
        if (!HoveredFlags.HasFlag(HoverFlags.WindowInnerFrame)) return;

        Scale += ZoomSpeed * ImGui.GetIO().MouseWheel;
        Scale = Math.Clamp(Scale, 0.25f, 100.0f);
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

    private void ProcessMapDragStart()
    {
        // Don't allow a drag to start if the window size is changing
        if (ImGui.GetWindowSize() == lastWindowSize)
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
}
