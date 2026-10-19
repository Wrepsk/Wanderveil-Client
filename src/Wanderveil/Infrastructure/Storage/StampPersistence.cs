using System;
using System.Collections.Generic;
using Wanderveil.Features;
using Wanderveil.Features.Stamps;

namespace Wanderveil.Infrastructure.Storage;

// Keeps one profile's stamp collection on disk. It loads once when created (startup, not a draw callback),
// then saves a snapshot on a background task each time a stamp is collected or placed. Disposing waits
// briefly for a save in progress.
internal sealed class StampPersistence : ISavedState, IDisposable
{
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(3);
    private readonly BackgroundSaver<StampBook> saver;

    internal StampPersistence(StampStore store, string profile, Func<DateTimeOffset>? clock = null, Action<Exception>? report = null)
    {
        var load = store.Load(profile);
        Loaded = load.Outcome;
        Kept = load.Kept;
        // Never overwrite a file this version could not safely read.
        saver = new BackgroundSaver<StampBook>(book => store.Save(profile, book), load.Outcome is LoadOutcome.Newer or LoadOutcome.Inaccessible, report);
        Collection = new StampCollection(clock, load.Book.Stamps, load.Book.Placements);
        Collection.Changed += Queue;
    }

    internal StampCollection Collection { get; }
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
        Collection.Changed -= Queue;
        saver.Close(DisposeWait);
    }

    // On the UI thread: takes a snapshot so the background save never touches the live list.
    private void Queue()
    {
        if (saver.State != SaveState.ReadOnly) saver.Queue(StampBook.Of(Collection));
    }
}
