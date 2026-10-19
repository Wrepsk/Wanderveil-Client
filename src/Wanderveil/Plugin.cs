using System;
using System.Linq;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Wanderveil.Features.Settings;
using Wanderveil.Infrastructure.Game;
using Wanderveil.Infrastructure.Media;
using Wanderveil.Infrastructure.Storage;
using Wanderveil.UI;

namespace Wanderveil;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;
    [PluginService] private static ITextureProvider TextureProvider { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IKeyState KeyState { get; set; } = null!;
    [PluginService] private static IPlayerState PlayerState { get; set; } = null!;

    private const string CommandName = "/wanderveil";

    private readonly WindowSystem windowSystem = new("Wanderveil");
    private readonly Configuration configuration;
    private readonly MainWindow mainWindow;
    private PassportAssets? assets;
    // One local passport for now; character-scoped passports come with character identity.
    private const string Profile = "local";
    private StampPersistence? stamps;
    private PassportPersistence? identity;
    private PortraitPicture? portrait;
    private bool commandRegistered;
    private bool disposed;

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        try
        {
            assets = new PassportAssets(TextureProvider, PluginInterface.UiBuilder.FontAtlas);
            stamps = new StampPersistence(new StampStore(PluginInterface.GetPluginConfigDirectory()), Profile,
                report: exception => Log.Error(exception, "Could not save Wanderveil stamps."));
            if (stamps.Loaded is not (Features.LoadOutcome.New or Features.LoadOutcome.Loaded))
                Log.Warning("Wanderveil stamps loaded as {Outcome}; files kept aside: {Kept}", stamps.Loaded, string.Join(", ", stamps.Kept.Select(System.IO.Path.GetFileName)));
            var passportStore = new PassportStore(PluginInterface.GetPluginConfigDirectory());
            identity = new PassportPersistence(passportStore, Profile,
                report: exception => Log.Error(exception, "Could not save the Wanderveil passport."));
            if (identity.Loaded is not (Features.LoadOutcome.New or Features.LoadOutcome.Loaded))
                Log.Warning("Wanderveil passport loaded as {Outcome}; files kept aside: {Kept}", identity.Loaded, string.Join(", ", identity.Kept.Select(System.IO.Path.GetFileName)));
            portrait = new PortraitPicture(TextureProvider, passportStore, Profile, identity.Profile, exception => Log.Error(exception, "Could not use the Wanderveil portrait."));
            mainWindow = new MainWindow(() => configuration.ReducedMotion, new ConfigurationSettings(configuration, SaveConfiguration), assets, stamps.Collection, stamps,
                new Features.Passport.PassportServices(identity.Profile, identity, portrait, new GameCharacter(Framework, PlayerState)));
            windowSystem.AddWindow(mainWindow);

            commandRegistered = CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open or close Wanderveil. Use /wanderveil settings to open the passport at its Settings pages.",
            });

            if (!commandRegistered)
                throw new InvalidOperationException("Could not register /wanderveil; the command is already in use.");

            PluginInterface.UiBuilder.Draw += Draw;
            Framework.Update += OnFrameworkUpdate;
            PluginInterface.UiBuilder.OpenMainUi += OpenMain;
            PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
            Log.Information("Wanderveil loaded.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        PluginInterface.UiBuilder.Draw -= Draw;
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMain;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;

        if (commandRegistered)
        {
            CommandManager.RemoveHandler(CommandName);
            commandRegistered = false;
        }

        windowSystem.RemoveAllWindows();
        // Waits briefly for a stamp save in progress, so collecting just before unloading is not lost.
        stamps?.Dispose();
        identity?.Dispose();
        portrait?.Dispose();
        assets?.Dispose();
    }

    // A chosen portrait is swapped in before the windows draw; the file picker draws after them, on top.
    private void Draw()
    {
        portrait?.Poll();
        windowSystem.Draw();
        portrait?.DrawDialog();
    }

    private void OnCommand(string command, string arguments)
    {
        if (string.Equals(arguments.Trim(), "settings", StringComparison.OrdinalIgnoreCase))
            OpenSettings();
        else
            mainWindow.ToggleAnimated();
    }

    // When the passport has a use for Escape (putting back a carried stamp, closing stamp info, or closing the
    // book), it takes the key from the game's key state, so the game does not also open its system menu.
    private void OnFrameworkUpdate(IFramework framework)
    {
        if (disposed || !mainWindow.WantsEscape || !KeyState[VirtualKey.ESCAPE]) return;
        KeyState[VirtualKey.ESCAPE] = false;
        mainWindow.RequestEscape();
    }

    private void OpenMain() => mainWindow.Summon();

    private void OpenSettings() => mainWindow.OpenSettings();

    private bool SaveConfiguration()
    {
        try
        {
            PluginInterface.SavePluginConfig(configuration);
            return true;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not save Wanderveil settings.");
            return false;
        }
    }
}
