using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Wanderveil.Features.Stamps;

namespace Wanderveil.Infrastructure.Storage;

// Keeps one profile's stamp collection on disk. It loads once when created (startup, not a draw callback),
// then saves a snapshot on a background task each time a stamp is collected or placed. Saves run one at a time and
// coalesce: only the newest snapshot waiting is written. Disposing waits briefly for a save in progress.
internal sealed class StampPersistence : IStampStorage, IDisposable
{
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(3);
    private readonly StampStore store;
    private readonly string profile;
    private readonly Action<Exception>? report;
    private readonly object gate = new();
    private StampBook? pending;
    private Task worker = Task.CompletedTask;
    private volatile StampSaveState state;
    private bool disposed;
    // Whether Drain is still taking snapshots; read and written only under gate.
    private bool running;

    internal StampPersistence(StampStore store, string profile, Func<DateTimeOffset>? clock = null, Action<Exception>? report = null)
    {
        this.store = store;
        this.profile = profile;
        this.report = report;
        var load = store.Load(profile);
        Loaded = load.Outcome;
        Kept = load.Kept;
        // Never overwrite a file this version could not safely read.
        state = load.Outcome is StampLoadOutcome.Newer or StampLoadOutcome.Inaccessible ? StampSaveState.ReadOnly : StampSaveState.Saved;
        Collection = new StampCollection(clock, load.Book.Stamps, load.Book.Placements);
        Collection.Changed += Queue;
    }

    internal StampCollection Collection { get; }
    // Files set aside at load because they could not be read.
    internal IReadOnlyList<string> Kept { get; }
    public StampLoadOutcome Loaded { get; }
    public StampSaveState State => state;

    public void Retry()
    {
        if (state == StampSaveState.Failed) Queue();
    }

    // Waits for saves in progress or queued; for shutdown and checks.
    internal bool Flush(TimeSpan timeout)
    {
        Task current;
        lock (gate) current = worker;
        try { return current.Wait(timeout); }
        catch (AggregateException) { return true; }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        Collection.Changed -= Queue;
        Flush(DisposeWait);
    }

    // On the UI thread: takes a snapshot so the background save never touches the live list.
    private void Queue()
    {
        if (state == StampSaveState.ReadOnly) return;
        var snapshot = StampBook.Of(Collection);
        lock (gate)
        {
            if (disposed) return;
            pending = snapshot;
            state = StampSaveState.Saving;
            if (running) return;
            running = true;
            worker = Task.Run(Drain);
        }
    }

    private void Drain()
    {
        while (true)
        {
            StampBook snapshot;
            lock (gate)
            {
                if (pending == null) { running = false; return; }
                snapshot = pending;
                pending = null;
            }
            try
            {
                store.Save(profile, snapshot);
                lock (gate) if (pending == null) state = StampSaveState.Saved;
            }
            catch (Exception exception)
            {
                lock (gate) if (pending == null) state = StampSaveState.Failed;
                report?.Invoke(exception);
            }
        }
    }
}
