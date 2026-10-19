using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Wanderveil.Features.Passport;
using Wanderveil.Infrastructure.Storage;
using Wanderveil.UI.Components;

namespace Wanderveil.Infrastructure.Media;

// The holder's own picture. Choosing one opens Dalamud's file picker; the file is then read, decoded, and
// copied beside the passport (portrait-<profile>.image) on a background task, so a large picture never stalls
// a frame. Pictures taller than ShownHeight are scaled down once on the GPU, since textures have no mipmaps.
// A new texture is swapped in at the start of a frame (Poll), when no draw list still refers to the old one.
internal sealed class PortraitPicture : IPassportPortrait, IDisposable
{
    private const int ShownHeight = 1200;
    private readonly ITextureProvider provider;
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
    private string? failure;
    // Each choice or removal starts a new generation; work from an older one is discarded when it finishes.
    private int generation;
    private volatile PortraitState state;
    private volatile string? problem;
    private bool disposed;

    internal PortraitPicture(ITextureProvider provider, PassportStore store, string profileName, PassportProfile profile, Action<Exception>? report = null)
    {
        this.provider = provider;
        this.store = store;
        this.profileName = profileName;
        this.profile = profile;
        this.report = report;
        if (profile.Identity.Portrait == null) return;
        state = PortraitState.Loading;
        var current = generation;
        Task.Run(() => Load(current, () => store.ReadPicture(profileName) ?? throw new FileNotFoundException("The saved portrait is missing."), keep: false, recenter: false));
    }

    public PortraitState State => state;
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
            return File.ReadAllBytes(path);
        }, keep: true, recenter: true));
    }, 1, null, true);

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
        });
    }

    // At the start of each frame, on the draw thread: shows a picture that has arrived, or reports why not.
    internal void Poll()
    {
        retired?.Dispose();
        retired = null;
        IDalamudTextureWrap? next;
        bool recenter;
        string? failed;
        lock (gate)
        {
            next = arrived;
            recenter = center;
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
        if (recenter || profile.Identity.Portrait == null) profile.Update(profile.Identity with { Portrait = PortraitCrop.Centered });
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

    private async Task Load(int current, Func<byte[]> read, bool keep, bool recenter)
    {
        IDalamudTextureWrap? wrap = null;
        try
        {
            var bytes = read();
            try { wrap = await provider.CreateFromImageAsync(bytes, "Wanderveil portrait", stopping.Token); }
            catch (Exception exception) when (exception is not OperationCanceledException) { throw new InvalidDataException("That file is not a picture Wanderveil can read.", exception); }
            if (wrap.Height > ShownHeight)
            {
                var width = Math.Max(1, (int)Math.Round(wrap.Width * (double)ShownHeight / wrap.Height));
                wrap = await provider.CreateFromExistingTextureAsync(wrap, new TextureModificationArgs { NewWidth = width, NewHeight = ShownHeight }, false, "Wanderveil portrait", stopping.Token);
            }
            // Only a picture that decoded is kept, so a saved portrait is always one that can be shown.
            if (keep) store.SavePicture(profileName, bytes);
            lock (gate)
            {
                if (current != generation || disposed) { wrap.Dispose(); return; }
                arrived?.Dispose();
                arrived = wrap;
                center = recenter;
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
