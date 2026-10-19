using System;
using System.Collections.Generic;
using Wanderveil.Features;
using Wanderveil.Features.Passport;

namespace Wanderveil.Infrastructure.Storage;

// Keeps one profile's passport identity on disk. It loads once when created (startup, not a draw callback),
// then saves a snapshot on a background task each time the holder changes it. A passport seen for the first
// time (or whose saves could not be read) is issued now, with its own number, and saved straight away so the
// number stays the same. Disposing waits briefly for a save in progress.
internal sealed class PassportPersistence : ISavedState, IDisposable
{
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(3);
    private readonly BackgroundSaver<PassportIdentity> saver;

    internal PassportPersistence(PassportStore store, string profile, Func<DateTimeOffset>? clock = null, Random? random = null, Action<Exception>? report = null)
    {
        var load = store.Load(profile);
        Loaded = load.Outcome;
        Kept = load.Kept;
        // Never overwrite a file this version could not safely read.
        var readOnly = load.Outcome is LoadOutcome.Newer or LoadOutcome.Inaccessible;
        saver = new BackgroundSaver<PassportIdentity>(identity => store.Save(profile, identity), readOnly, report);
        var issued = load.Identity ?? PassportIdentity.Issue(DateOnly.FromDateTime((clock?.Invoke() ?? DateTimeOffset.Now).Date), random ?? Random.Shared);
        Profile = new PassportProfile(issued);
        Profile.Changed += Queue;
        if (load.Identity == null) Queue();
    }

    internal PassportProfile Profile { get; }
    // Files set aside at load because they could not be read.
    internal IReadOnlyList<string> Kept { get; }
    public LoadOutcome Loaded { get; }
    public SaveState State => saver.State;

    public void Retry()
    {
        if (saver.State == SaveState.Failed) Queue();
    }

    // Waits for saves in progress or queued; for shutdown and checks.
    internal bool Flush(TimeSpan timeout) => saver.Flush(timeout);

    public void Dispose()
    {
        Profile.Changed -= Queue;
        saver.Close(DisposeWait);
    }

    private void Queue()
    {
        if (saver.State != SaveState.ReadOnly) saver.Queue(Profile.Identity);
    }
}
