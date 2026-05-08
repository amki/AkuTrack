using AkuTrack.Managers;
using AkuTrack.Windows;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;

namespace AkuTrack;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    public ServiceProvider serviceProvider { get; private set; }

    public Configuration Configuration { get; init; }

    public readonly WindowSystem windowSystem = new("AkuTrack");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private MapWindow MapWindow { get; init; }
    private SearchWindow SearchWindow { get; init; }
    private UploadManager UploadManager { get; init; }
    private bool wasGameMapVisible;
    private readonly Hook<OpenMapDelegate> openMapHook;

    private unsafe delegate void OpenMapDelegate(AgentMap* thisPtr, OpenMapInfo* data);

    public Plugin(
        IFramework framework,
        IClientState clientState,
        IDalamudPluginInterface pluginInterface,
        IDataManager dataManager,
        ITextureProvider textureProvider,
        IChatGui chatGui,
        IGameGui gameGui,
        IPluginLog pluginLog,
        IObjectTable objectTable,
        IPartyList partyList,
        IFateTable fateTable,
        IGameInteropProvider gameInteropProvider,
        ITextureSubstitutionProvider textureSubstitutionProvider)
    {
        // You might normally want to embed resources and load them from the manifest stream
        //var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        var configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        configuration.Version = 0;
        pluginInterface.SavePluginConfig(configuration);

        serviceProvider = new ServiceCollection()
            .AddSingleton(this)
            .AddSingleton(framework)
            .AddSingleton(clientState)
            .AddSingleton(partyList)
            .AddSingleton(pluginInterface)
            .AddSingleton(dataManager)
            .AddSingleton(textureProvider)
            .AddSingleton(chatGui)
            .AddSingleton(gameGui)
            .AddSingleton(pluginLog)
            .AddSingleton(objectTable)
            .AddSingleton(fateTable)
            .AddSingleton(textureSubstitutionProvider)
            .AddSingleton(configuration)
            .AddSingleton<MainWindow>()
            .AddSingleton<ConfigWindow>()
            .AddSingleton<MapWindow>()
            .AddSingleton<SearchWindow>()
            .AddSingleton<UploadManager>()
            .AddSingleton<ObjTrackManager>()
            .AddSingleton<BottomBar>()
            .AddSingleton(windowSystem)
            .AddTransient<DetailsWindow>()
            .BuildServiceProvider();

        MainWindow = serviceProvider.GetRequiredService<MainWindow>();
        ConfigWindow = serviceProvider.GetRequiredService<ConfigWindow>();
        MapWindow = serviceProvider.GetRequiredService<MapWindow>();
        SearchWindow = serviceProvider.GetRequiredService<SearchWindow>();
        Configuration = serviceProvider.GetRequiredService<Configuration>();
        UploadManager = serviceProvider.GetRequiredService<UploadManager>();
        unsafe
        {
            openMapHook = gameInteropProvider.HookFromAddress<OpenMapDelegate>((nint)AgentMap.MemberFunctionPointers.OpenMap, OpenMapDetour);
        }


        windowSystem.AddWindow(ConfigWindow);
        windowSystem.AddWindow(MainWindow);
        windowSystem.AddWindow(MapWindow);
        windowSystem.AddWindow(SearchWindow);

        CommandManager.AddHandler("/akut", new CommandInfo((string command, string args) => { ToggleMainUi(); })
        {
            HelpMessage = "Opens the main menu with debug information."
        });

        CommandManager.AddHandler("/akum", new CommandInfo((string command, string args) => { MapWindow.Toggle(); }) {
            HelpMessage = "Opens the map window."
        });
        CommandManager.AddHandler("/akuc", new CommandInfo((string command, string args) => { ToggleConfigUi(); }) {
            HelpMessage = "Opens the configuration window."
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        clientState.Login += OnLogin;
        framework.Update += OnFrameworkUpdate;
        openMapHook.Enable();
        _ = UploadManager.ReloadChestDropsAsync();

        // Add a simple message to the log with level set to information
        // Use /xllog to open the log window in-game
        // Example Output: 00:57:54.959 | INF | [SamplePlugin] ===A cool log message from Sample Plugin===
        Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");
    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        ClientState.Login -= OnLogin;
        serviceProvider.GetRequiredService<IFramework>().Update -= OnFrameworkUpdate;
        openMapHook.Dispose();
        
        windowSystem.RemoveAllWindows();

        MapWindow.Dispose();
        SearchWindow.Dispose();
        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler("/akut");
        CommandManager.RemoveHandler("/akum");
        CommandManager.RemoveHandler("/aku");
    }
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();

    private void OnFrameworkUpdate(IFramework framework)
    {
        var isGameMapVisible = IsGameMapVisible();
        MapWindow.FocusCurrentFlagMarkerIfNeeded();

        if (!Configuration.ToggleMapWithGameMap)
        {
            wasGameMapVisible = isGameMapVisible;
            return;
        }

        if (isGameMapVisible == wasGameMapVisible)
        {
            return;
        }

        wasGameMapVisible = isGameMapVisible;
        MapWindow.IsOpen = isGameMapVisible;
    }

    private void OnLogin()
    {
        _ = UploadManager.ReloadChestDropsAsync();
    }

    private unsafe void OpenMapDetour(AgentMap* thisPtr, OpenMapInfo* data)
    {
        var isFlagMapOpen = data != null && data->Type == MapType.FlagMarker;
        openMapHook.Original(thisPtr, data);

        if (isFlagMapOpen)
        {
            MapWindow.FocusCurrentFlagMarkerOnNextDraw();
        }
    }

    private static bool IsGameMapVisible()
    {
        var areaMap = GameGui.GetAddonByName("AreaMap", 1);
        if (!areaMap.IsNull && areaMap.IsVisible)
        {
            return true;
        }

        var naviMap = GameGui.GetAddonByName("NaviMap", 1);
        return !naviMap.IsNull && naviMap.IsVisible;
    }
}
