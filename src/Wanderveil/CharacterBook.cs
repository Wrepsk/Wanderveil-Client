using System;
using System.Linq;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Wanderveil.Features;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Settings;
using Wanderveil.Infrastructure.Game;
using Wanderveil.Infrastructure.Media;
using Wanderveil.Infrastructure.Online;
using Wanderveil.Infrastructure.Storage;
using Wanderveil.UI;

namespace Wanderveil;

// One signed-in character's passport: its own local files (named by Lodestone ID), the window that shows them, and
// the sync that keeps the identity in step with the server. Built when the character signs in or logs in, and
// disposed when it signs out, logs out, or another character takes over; saves in progress finish first.
internal sealed class CharacterBook : IDisposable
{
    private readonly WindowSystem windows;
    private readonly StampPersistence stamps;
    private readonly PassportPersistence identity;
    private readonly MarksPersistence marks;
    private readonly PortraitPicture portrait;

    internal CharacterBook(SavedSession session, OnlineAccount account, IDalamudPluginInterface plugin, ITextureProvider textures, ITextureReadbackProvider readback,
        IFramework framework, IPlayerState player, IPluginLog log, PassportAssets assets, Configuration configuration, Func<bool> saveConfiguration, WindowSystem windows)
    {
        this.windows = windows;
        Session = session;
        var profile = session.Profile;
        var directory = plugin.GetPluginConfigDirectory();
        stamps = new StampPersistence(new StampStore(directory), profile, report: exception => log.Error(exception, "Could not save Wanderveil stamps."));
        Warn(log, "stamps", stamps.Loaded, stamps.Kept);
        var passportStore = new PassportStore(directory);
        identity = new PassportPersistence(passportStore, profile, report: exception => log.Error(exception, "Could not save the Wanderveil passport."));
        Warn(log, "passport", identity.Loaded, identity.Kept);
        marks = new MarksPersistence(new MarksStore(directory), profile, report: exception => log.Error(exception, "Could not save Wanderveil drawing."));
        Warn(log, "drawing", marks.Loaded, marks.Kept);
        portrait = new PortraitPicture(textures, passportStore, profile, identity.Profile, exception => log.Error(exception, "Could not use the Wanderveil portrait."), readback);
        // Only a passport issued just now takes the server's copy; one that could not be read is never replaced.
        Sync = new PassportSync(account, session, identity.Profile, directory, fresh: identity.Loaded == LoadOutcome.New);
        Window = new MainWindow(() => configuration.ReducedMotion, new ConfigurationSettings(configuration, saveConfiguration), assets, stamps.Collection, stamps,
            new PassportServices(identity.Profile, identity, portrait, new GameCharacter(framework, player)), marks.Marks, marks, new BookAccount(account, session, Sync));
        windows.AddWindow(Window);
        windows.AddWindow(Window.Camera);
        Sync.Start();
    }

    internal SavedSession Session { get; }
    internal MainWindow Window { get; }
    internal PassportSync Sync { get; }

    // Each frame, before the windows draw.
    internal void Poll()
    {
        portrait.Poll();
        Sync.Poll();
    }

    internal void DrawDialog() => portrait.DrawDialog();

    public void Dispose()
    {
        windows.RemoveWindow(Window.Camera);
        windows.RemoveWindow(Window);
        Sync.Dispose();
        stamps.Dispose();
        identity.Dispose();
        marks.Dispose();
        portrait.Dispose();
    }

    private static void Warn(IPluginLog log, string part, LoadOutcome outcome, System.Collections.Generic.IReadOnlyList<string> kept)
    {
        if (outcome is not (LoadOutcome.New or LoadOutcome.Loaded))
            log.Warning("Wanderveil {Part} loaded as {Outcome}; files kept aside: {Kept}", part, outcome, string.Join(", ", kept.Select(System.IO.Path.GetFileName)));
    }

    // The account, as the passport's Settings pages show it.
    private sealed class BookAccount(OnlineAccount account, SavedSession session, PassportSync sync) : IBookShellAccount
    {
        public string Name => account.Session?.Account.Character.Name ?? session.Account.Character.Name;
        public string World => account.Session?.Account.Character.World ?? session.Account.Character.World;
        public string VerifiedBy => session.Account.VerifiedBy;
        public DateTimeOffset VerifiedAt => session.Account.VerifiedAt;
        public string? Notice => sync.Notice;

        public string Sync => sync.State switch
        {
            SyncState.Checking => "Comparing your passport with the server.",
            SyncState.Saving => "Saving your passport to the server.",
            SyncState.Synced => "Your passport details are saved online too. Stamps, drawing, and your picture stay on this PC for now.",
            SyncState.Offline => "The server cannot be reached right now. Your passport works from this PC and catches up later.",
            SyncState.Held => "Your passport online is from a newer Wanderveil; update the plugin to keep it in step.",
            _ => "Your passport is kept on this PC.",
        };

        public void SignOut() => account.SignOut();
    }
}
