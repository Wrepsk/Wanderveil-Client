using System;
using System.Collections.Generic;
using System.Net.Http;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Wanderveil.Infrastructure.Media;
using Wanderveil.Infrastructure.Online;
using Wanderveil.UI;

namespace Wanderveil;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;
    [PluginService] private static ITextureProvider TextureProvider { get; set; } = null!;
    [PluginService] private static ITextureReadbackProvider TextureReadbackProvider { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IKeyState KeyState { get; set; } = null!;
    [PluginService] private static IPlayerState PlayerState { get; set; } = null!;
    [PluginService] private static ITargetManager TargetManager { get; set; } = null!;

    private const string CommandName = "/wanderveil";

    private readonly WindowSystem windowSystem = new("Wanderveil");
    private readonly Configuration configuration;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private PassportAssets? assets;
    private SessionStore? sessions;
    private OnlineAccount? account;
    private SignInWindow? signIn;
    // The logged-in, signed-in character's passport; none until one is (ADR-0004: sign in first).
    private CharacterBook? book;
    // Characters already asked to sign in this session, so the sheet opens by itself only once each.
    private readonly HashSet<ulong> asked = [];
    // Opens the passport once the character it is waiting for has one.
    private bool openWhenReady;
    // The player targeted in game, read on the framework thread each frame, for asking to see their passport.
    private volatile Tuple<string, string>? target;
    private bool commandRegistered;
    private bool disposed;

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        try
        {
            assets = new PassportAssets(TextureProvider, PluginInterface.UiBuilder.FontAtlas);
            sessions = new SessionStore(PluginInterface.GetPluginConfigDirectory(), report: exception => Log.Error(exception, "Could not save Wanderveil sessions."));
            if (sessions.Protection != "dpapi") Log.Warning("Windows data protection is unavailable; Wanderveil session tokens are stored unprotected.");
            var version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            account = new OnlineAccount(sessions, http, version, () => configuration.ServerUrl);
            signIn = new SignInWindow(account, assets, () => configuration.ServerUrl, SaveServer, () => openWhenReady = true);
            windowSystem.AddWindow(signIn);

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

        // Waits briefly for saves in progress, so collecting just before unloading is not lost.
        book?.Dispose();
        windowSystem.RemoveAllWindows();
        account?.Dispose();
        sessions?.Dispose();
        http.Dispose();
        assets?.Dispose();
    }

    // A chosen portrait and a server passport are taken in before the windows draw; the file picker draws after them.
    private void Draw()
    {
        book?.Poll();
        // The camera's guide stays visible in group pose (the game's own posing tool) and when the game's UI is hidden,
        // by its Hide UI key or by the game itself, so a picture can be taken without the HUD.
        var camera = book?.Window.CameraOpen ?? false;
        PluginInterface.UiBuilder.DisableGposeUiHide = camera;
        PluginInterface.UiBuilder.DisableUserUiHide = camera;
        PluginInterface.UiBuilder.DisableAutomaticUiHide = camera;
        assets?.BeginFrame();
        windowSystem.Draw();
        book?.DrawDialog();
    }

    private void OnCommand(string command, string arguments)
    {
        if (string.Equals(arguments.Trim(), "settings", StringComparison.OrdinalIgnoreCase))
            OpenSettings();
        else if (book != null)
            book.Window.ToggleAnimated();
        else
            AskToSignIn();
    }

    // On the framework thread: follows the logged-in character, swaps its passport in or out, and asks a character
    // without a session to sign in (once). When the passport has a use for Escape, it takes the key from the game.
    private void OnFrameworkUpdate(IFramework framework)
    {
        if (disposed || account == null) return;
        if (PlayerState.IsLoaded)
            account.SetCharacter(PlayerState.ContentId, PlayerState.CharacterName.ToString(), PlayerState.HomeWorld.Value.Name.ExtractText());
        else
            account.SetCharacter(0, "", "");
        target = TargetManager.Target is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player
            ? Tuple.Create(player.Name.TextValue, player.HomeWorld.Value.Name.ExtractText())
            : null;
        Follow();
        if (account.State is AccountState.SignedOut or AccountState.NoServer && asked.Add(account.ContentId)) AskToSignIn();
        if (book != null && openWhenReady) { openWhenReady = false; book.Window.Summon(); }

        if (book == null || !book.Window.WantsEscape || !KeyState[VirtualKey.ESCAPE]) return;
        KeyState[VirtualKey.ESCAPE] = false;
        book.Window.RequestEscape();
    }

    // The passport for the signed-in character, built or put away as that changes. Not from a draw callback: it
    // loads (and finishes saving) local files.
    private void Follow()
    {
        var session = account!.State == AccountState.SignedIn ? account.Session : null;
        if (book?.Session.Profile == session?.Profile && book?.Session.ContentId == session?.ContentId) return;
        var wasOpen = book?.Window.IsOpen == true;
        book?.Dispose();
        book = null;
        if (session == null)
        {
            // Signed out while reading (by the holder, or the server ending the session): the sheet says why.
            if (wasOpen && account.State == AccountState.SignedOut) AskToSignIn();
            return;
        }
        try
        {
            book = new CharacterBook(session, account, PluginInterface, TextureProvider, TextureReadbackProvider, Framework, PlayerState, Log, assets!, configuration, SaveConfiguration, windowSystem,
                () => target is { } held ? (held.Item1, held.Item2) : null);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not open the Wanderveil passport for {Profile}.", session.Profile);
        }
    }

    private void AskToSignIn()
    {
        if (signIn != null) signIn.IsOpen = true;
    }

    private void OpenMain()
    {
        if (book != null) book.Window.Summon();
        else AskToSignIn();
    }

    private void OpenSettings()
    {
        if (book != null) book.Window.OpenSettings();
        else AskToSignIn();
    }

    private void SaveServer(string server)
    {
        configuration.ServerUrl = server;
        SaveConfiguration();
    }

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
