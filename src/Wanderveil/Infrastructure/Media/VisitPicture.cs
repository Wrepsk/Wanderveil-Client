using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Wanderveil.Features.Passport;
using Wanderveil.UI.Components;

namespace Wanderveil.Infrastructure.Media;

// A visited passport's picture: decoded from the bytes read for the visit, in memory only, scaled down once when tall.
// The owner's placement in the frame comes with their identity. Nothing here can be chosen, taken, or removed.
internal sealed class VisitPicture : IPassportPortrait, IDisposable
{
    private const int ShownHeight = 1200;
    private readonly CancellationTokenSource stopping = new();
    private readonly object gate = new();
    private IDalamudTextureWrap? shown;
    private IDalamudTextureWrap? arrived;
    private volatile PortraitState state;
    private bool disposed;

    internal VisitPicture(ITextureProvider provider, byte[]? bytes)
    {
        if (bytes == null) return;
        state = PortraitState.Loading;
        Task.Run(async () =>
        {
            IDalamudTextureWrap? wrap = null;
            try
            {
                wrap = await provider.CreateFromImageAsync(bytes, "Wanderveil visited portrait", stopping.Token);
                if (wrap.Height > ShownHeight)
                {
                    var width = Math.Max(1, (int)Math.Round(wrap.Width * (double)ShownHeight / wrap.Height));
                    wrap = await provider.CreateFromExistingTextureAsync(wrap, new TextureModificationArgs { NewWidth = width, NewHeight = ShownHeight }, false, "Wanderveil visited portrait", stopping.Token);
                }
                lock (gate)
                {
                    if (disposed) { wrap.Dispose(); return; }
                    arrived = wrap;
                }
            }
            catch (OperationCanceledException) { wrap?.Dispose(); }
            catch (Exception)
            {
                wrap?.Dispose();
                state = PortraitState.Failed;
            }
        });
    }

    public PortraitState State => state;
    public string? Problem => state == PortraitState.Failed ? "This picture could not be shown." : null;

    public bool TryGet(out BookImage image)
    {
        image = default;
        if (shown == null) return false;
        image = new BookImage(shown.Handle, shown.Width, shown.Height);
        return true;
    }

    public void Choose() { }

    public void Remove() { }

    // At the start of each frame, on the draw thread.
    internal void Poll()
    {
        IDalamudTextureWrap? next;
        lock (gate) { next = arrived; arrived = null; }
        if (next == null) return;
        shown?.Dispose();
        shown = next;
        state = PortraitState.Shown;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            arrived?.Dispose();
            arrived = null;
        }
        stopping.Cancel();
        shown?.Dispose();
        shown = null;
    }
}
