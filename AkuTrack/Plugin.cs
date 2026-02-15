using AkuTrack.Managers;
using AkuTrack.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace AkuTrack;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/akut";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("AkuTrack");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private MapWindow MapWindow { get; init; }

    public Plugin(
        IFramework framework,
        IClientState clientState,
        IDalamudPluginInterface pluginInterface,
        IDataManager dataManager,
        ITextureProvider textureProvider,
        IChatGui chatGui,
        IPluginLog pluginLog,
        IObjectTable objectTable,
        ITextureSubstitutionProvider textureSubstitutionProvider)
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // You might normally want to embed resources and load them from the manifest stream
        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        var serviceProvider = new ServiceCollection()
            .AddSingleton(framework)
            .AddSingleton(clientState)
            .AddSingleton(pluginInterface)
            .AddSingleton(dataManager)
            .AddSingleton(textureProvider)
            .AddSingleton(chatGui)
            .AddSingleton(pluginLog)
            .AddSingleton(objectTable)
            .AddSingleton(textureSubstitutionProvider)
            .AddSingleton<Configuration>()
            .AddSingleton<MainWindow>()
            .AddSingleton<ConfigWindow>()
            .AddSingleton<MapWindow>()
            .AddSingleton<UploadManager>()
            .AddSingleton<ObjTrackManager>()
            .AddSingleton<BottomBar>()
            .BuildServiceProvider();

        MainWindow = serviceProvider.GetRequiredService<MainWindow>();
        ConfigWindow = serviceProvider.GetRequiredService<ConfigWindow>();
        MapWindow = serviceProvider.GetRequiredService<MapWindow>();


        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(MapWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the main menu with debug information."
        });

        CommandManager.AddHandler("/akum", new CommandInfo((string command, string args) => { MapWindow.Toggle(); }) {
            HelpMessage = "Opens the map window."
        });
        CommandManager.AddHandler("/akuc", new CommandInfo((string command, string args) => { ConfigWindow.Toggle(); }) {
            HelpMessage = "Opens the configuration window."
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // Add a simple message to the log with level set to information
        // Use /xllog to open the log window in-game
        // Example Output: 00:57:54.959 | INF | [SamplePlugin] ===A cool log message from Sample Plugin===
        Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");
    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        // In response to the slash command, toggle the display status of our main ui
        MainWindow.Toggle();
    }
    
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
