using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Wanderveil.Features.Passport;
using Wanderveil.Infrastructure.Online;
using Wanderveil.Infrastructure.Storage;
using Wanderveil.UI.Components;

namespace Wanderveil.Infrastructure.Media;

// The holder's own picture. Taking one captures the game's own view (before any plugin window draws, so none is in it)
// inside the frame guide, saved as PNG through Dalamud's encoder. Choosing one opens Dalamud's file picker; the file is then read, decoded, and
// copied beside the passport (portrait-<profile>.image) on a background task, so a large picture never stalls
// a frame. Pictures taller than ShownHeight are scaled down once on the GPU, since textures have no mipmaps.
// A new texture is swapped in at the start of a frame (Poll), when no draw list still refers to the old one.
internal sealed class PortraitPicture : IPassportPortrait, IPictureHost, IDisposable
{
    private const int ShownHeight = 1200;
    private readonly ITextureProvider provider;
    private readonly ITextureReadbackProvider? readback;
    private readonly PassportStore store;
    private readonly string profileName;
    private readonly PassportProfile profile;
    private readonly Action<Exception>? report;
    private readonly FileDialogManager dialog = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly object gate = new();
    private IDalamudTextureWrap? shown;
    // A picture taken out mid-frame: this frame's draw list may still show it, so it is freed at the next Poll.
    private IDalamudTextureWrap? retired;
    // Written by background work under gate, taken by Poll on the draw thread.
    private IDalamudTextureWrap? arrived;
    private bool center;
    // Whether the arrived picture came from the server, whose placement in the frame comes with the identity.
    private bool fromServer;
    private string? failure;
    // Each choice or removal starts a new generation; work from an older one is discarded when it finishes.
    private int generation;
    private volatile PortraitState state;
    private volatile string? problem;
    private bool disposed;

    internal PortraitPicture(ITextureProvider provider, PassportStore store, string profileName, PassportProfile profile, Action<Exception>? report = null, ITextureReadbackProvider? readback = null)
    {
        this.provider = provider;
        this.readback = readback;
        this.store = store;
        this.profileName = profileName;
        this.profile = profile;
        this.report = report;
        if (profile.Identity.Portrait == null) return;
        state = PortraitState.Loading;
        var current = generation;
        Task.Run(() => Load(current, () => Task.FromResult(store.ReadPicture(profileName) ?? throw new FileNotFoundException("The saved portrait is missing.")), keep: false, recenter: false));
    }

    public PortraitState State => state;

    // The holder chose, took, or removed a picture here (raised from a background task once it is saved or gone).
    public event Action? Changed;

    // The picture as saved on this PC, or null. Worker thread.
    public byte[]? ReadStored() => store.ReadPicture(profileName);

    // A copy the server takes (PNG or JPEG, at most 4 MiB and 2048 px): the stored file when it already is; else
    // decoded, scaled to fit, and encoded as PNG, or as JPEG when the PNG is still too large. Worker thread.
    public async Task<(byte[] Bytes, string ContentType)> ForUpload(byte[] stored, CancellationToken cancellationToken)
    {
        if (PictureCheck.Fits(stored)) return (stored, PictureCheck.Inspect(stored)!.Value.ContentType);
        if (readback is not { } saver) throw new InvalidDataException("This picture cannot be prepared for upload here.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
        IDalamudTextureWrap wrap = await provider.CreateFromImageAsync(stored, "Wanderveil portrait upload", linked.Token);
        try
        {
            foreach (var side in new[] { PictureCheck.MaximumSide, PictureCheck.MaximumSide / 2 })
            {
                var scale = Math.Min(1.0, side / (double)Math.Max(wrap.Width, wrap.Height));
                using var sized = scale < 1.0
                    ? await provider.CreateFromExistingTextureAsync(wrap, new TextureModificationArgs { NewWidth = Math.Max(1, (int)(wrap.Width * scale)), NewHeight = Math.Max(1, (int)(wrap.Height * scale)) }, true, "Wanderveil portrait upload", linked.Token)
                    : null;
                var source = sized ?? wrap;
                foreach (var (extension, type) in new[] { (".png", "image/png"), (".jpg", "image/jpeg") })
                {
                    var encoder = saver.GetSupportedImageEncoderInfos().FirstOrDefault(info => info.Extensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase)));
                    if (encoder == null) continue;
                    using var stream = new MemoryStream();
                    await saver.SaveToStreamAsync(source, encoder.ContainerGuid, stream, null, true, true, linked.Token);
                    var bytes = stream.ToArray();
                    if (PictureCheck.Fits(bytes)) return (bytes, type);
                }
            }
        }
        finally { wrap.Dispose(); }
        throw new InvalidDataException("This picture could not be made small enough to save online.");
    }

    // The server's picture taken on this PC (or none): kept and shown, without counting as the holder's change.
    // Where it sits in the frame comes with the identity. UI thread.
    public void Replace(byte[]? bytes)
    {
        int current;
        lock (gate)
        {
            current = ++generation;
            arrived?.Dispose();
            arrived = null;
            failure = null;
        }
        if (bytes == null)
        {
            retired?.Dispose();
            retired = shown;
            shown = null;
            state = PortraitState.None;
            problem = null;
            Task.Run(() =>
            {
                try { store.DeletePicture(profileName); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { report?.Invoke(exception); }
            });
            return;
        }
        state = PortraitState.Loading;
        problem = null;
        Task.Run(() => Load(current, () => Task.FromResult(bytes), keep: true, recenter: false, holder: false));
    }
    public string? Problem => problem;

    public bool TryGet(out BookImage image)
    {
        image = default;
        if (shown == null) return false;
        image = new BookImage(shown.Handle, shown.Width, shown.Height);
        return true;
    }

    public void Choose() => dialog.OpenFileDialog("Choose a portrait", "Pictures{.png,.jpg,.jpeg,.webp,.bmp}", (chosen, paths) =>
    {
        if (!chosen || paths.Count == 0) return;
        var path = paths[0];
        int current;
        lock (gate) current = ++generation;
        state = PortraitState.Loading;
        problem = null;
        Task.Run(() => Load(current, () =>
        {
            if (new FileInfo(path).Length > PassportStore.MaximumPictureBytes) throw new InvalidDataException("That picture is larger than 16 MB.");
            return Task.FromResult(File.ReadAllBytes(path));
        }, keep: true, recenter: true));
    }, 1, null, true);

    public bool CanTake => readback != null;

    public void Take(Vector2 min, Vector2 max)
    {
        if (readback is not { } saver) return;
        var viewport = ImGui.GetMainViewport();
        var size = Vector2.Max(Vector2.One, viewport.Size);
        var uv0 = Vector2.Clamp((min - viewport.Pos) / size, Vector2.Zero, Vector2.One);
        var uv1 = Vector2.Clamp((max - viewport.Pos) / size, Vector2.Zero, Vector2.One);
        if (uv1.X - uv0.X <= 0 || uv1.Y - uv0.Y <= 0) return;
        int current;
        lock (gate) current = ++generation;
        state = PortraitState.Loading;
        problem = null;
        // Started on the draw thread; the capture completes after the next frame renders.
        var capture = provider.CreateFromImGuiViewportAsync(new ImGuiViewportTextureArgs { ViewportId = viewport.ID, TakeBeforeImGuiRender = true, KeepTransparency = false, Uv0 = uv0, Uv1 = uv1 }, "Wanderveil camera", stopping.Token);
        Task.Run(() => Load(current, async () =>
        {
            using var shot = await capture;
            var png = saver.GetSupportedImageEncoderInfos().FirstOrDefault(info => info.Extensions.Any(extension => string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)))
                ?? throw new InvalidDataException("The picture could not be saved: no PNG encoder.");
            using var stream = new MemoryStream();
            await saver.SaveToStreamAsync(shot, png.ContainerGuid, stream, null, true, true, stopping.Token);
            return stream.ToArray();
        }, keep: true, recenter: true));
    }

    public void Remove()
    {
        lock (gate)
        {
            generation++;
            arrived?.Dispose();
            arrived = null;
            failure = null;
        }
        retired?.Dispose();
        retired = shown;
        shown = null;
        state = PortraitState.None;
        problem = null;
        profile.Update(profile.Identity with { Portrait = null });
        Task.Run(() =>
        {
            try { store.DeletePicture(profileName); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { report?.Invoke(exception); }
            Changed?.Invoke();
        });
    }

    // At the start of each frame, on the draw thread: shows a picture that has arrived, or reports why not.
    internal void Poll()
    {
        retired?.Dispose();
        retired = null;
        IDalamudTextureWrap? next;
        bool recenter;
        bool fromHolder;
        string? failed;
        lock (gate)
        {
            next = arrived;
            recenter = center;
            fromHolder = !fromServer;
            failed = failure;
            arrived = null;
            failure = null;
        }
        if (failed != null)
        {
            state = PortraitState.Failed;
            problem = failed;
        }
        if (next == null) return;
        shown?.Dispose();
        shown = next;
        state = PortraitState.Shown;
        problem = null;
        if (recenter || profile.Identity.Portrait == null && fromHolder) profile.Update(profile.Identity with { Portrait = PortraitCrop.Centered });
    }

    // The file picker's own window; drawn by the plugin each frame after its windows.
    internal void DrawDialog() => dialog.Draw();

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            generation++;
            arrived?.Dispose();
            arrived = null;
        }
        stopping.Cancel();
        shown?.Dispose();
        shown = null;
        retired?.Dispose();
        retired = null;
    }

    private async Task Load(int current, Func<Task<byte[]>> read, bool keep, bool recenter, bool holder = true)
    {
        IDalamudTextureWrap? wrap = null;
        try
        {
            var bytes = await read();
            try { wrap = await provider.CreateFromImageAsync(bytes, "Wanderveil portrait", stopping.Token); }
            catch (Exception exception) when (exception is not OperationCanceledException) { throw new InvalidDataException("That file is not a picture Wanderveil can read.", exception); }
            if (wrap.Height > ShownHeight)
            {
                var width = Math.Max(1, (int)Math.Round(wrap.Width * (double)ShownHeight / wrap.Height));
                wrap = await provider.CreateFromExistingTextureAsync(wrap, new TextureModificationArgs { NewWidth = width, NewHeight = ShownHeight }, false, "Wanderveil portrait", stopping.Token);
            }
            // Only a picture that decoded is kept, so a saved portrait is always one that can be shown.
            if (keep) store.SavePicture(profileName, bytes);
            if (keep && holder) Changed?.Invoke();
            lock (gate)
            {
                if (current != generation || disposed) { wrap.Dispose(); return; }
                arrived?.Dispose();
                arrived = wrap;
                center = recenter;
                fromServer = !holder;
            }
        }
        catch (OperationCanceledException) { wrap?.Dispose(); }
        catch (Exception exception)
        {
            wrap?.Dispose();
            if (exception is not InvalidDataException) report?.Invoke(exception);
            lock (gate)
            {
                if (current != generation || disposed) return;
                failure = exception switch
                {
                    InvalidDataException => exception.Message,
                    FileNotFoundException => "Your saved portrait is missing, so the placeholder shows. Choose a picture again.",
                    _ => "That picture could not be read or saved.",
                };
            }
        }
    }
}
