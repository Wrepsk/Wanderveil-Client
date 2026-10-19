using System;
using System.Threading.Tasks;
using Wanderveil.Features;

namespace Wanderveil.Infrastructure.Storage;

// Saves snapshots on a background task, one at a time, coalesced: only the newest snapshot waiting is written.
// Snapshots are taken on the UI thread, so a save never touches live state. Closing waits briefly for a save
// in progress. A read-only saver never saves, so a file this version could not safely read is never overwritten.
internal sealed class BackgroundSaver<T>(Action<T> save, bool readOnly, Action<Exception>? report = null) where T : class
{
    private readonly object gate = new();
    private T? pending;
    private Task worker = Task.CompletedTask;
    private volatile SaveState state = readOnly ? SaveState.ReadOnly : SaveState.Saved;
    private bool closed;
    // Whether Drain is still taking snapshots; read and written only under gate.
    private bool running;

    internal SaveState State => state;

    internal void Queue(T snapshot)
    {
        if (state == SaveState.ReadOnly) return;
        lock (gate)
        {
            if (closed) return;
            pending = snapshot;
            state = SaveState.Saving;
            if (running) return;
            running = true;
            worker = Task.Run(Drain);
        }
    }

    // Waits for saves in progress or queued; for shutdown and checks.
    internal bool Flush(TimeSpan timeout)
    {
        Task current;
        lock (gate) current = worker;
        try { return current.Wait(timeout); }
        catch (AggregateException) { return true; }
    }

    // Takes no more snapshots, then waits up to wait for the last one to be written.
    internal void Close(TimeSpan wait)
    {
        lock (gate)
        {
            if (closed) return;
            closed = true;
        }
        Flush(wait);
    }

    private void Drain()
    {
        while (true)
        {
            T snapshot;
            lock (gate)
            {
                if (pending == null) { running = false; return; }
                snapshot = pending;
                pending = null;
            }
            try
            {
                save(snapshot);
                lock (gate) if (pending == null) state = SaveState.Saved;
            }
            catch (Exception exception)
            {
                lock (gate) if (pending == null) state = SaveState.Failed;
                report?.Invoke(exception);
            }
        }
    }
}
