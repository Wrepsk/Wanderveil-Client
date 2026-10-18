using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Infrastructure.Media;

internal sealed class PassportAssets : IBookAssets, IDisposable
{
    // Order matches FontScope: title, heading, body, display, mono, signature.
    private static readonly string[] FontFiles = ["CormorantGaramond-Bold.ttf", "CormorantGaramond-SemiBold.ttf",
        "CormorantGaramond-Medium.ttf", "Cinzel-Bold.ttf", "CourierPrime-Regular.ttf", "Caveat.ttf"];
    private const float BaseSizePx = 24f;
    // Each distinct pixel size is a separate atlas font; the cap bounds atlas growth while resizing.
    private const int MaxSizedFonts = 48;
    // Size copies written by tools/passport-art: full, then .half/.quarter/.eighth where present.
    private static readonly string[] Levels = ["", ".half", ".quarter", ".eighth"];
    private static readonly string[] TextureNames = ["book-open", "book-cover", "demo-portrait", "stamp-atlas",
        "gridania-cover", "gridania-banner", "gridania-map-overhead", "gridania-crest"];
    private readonly (ISharedImmediateTexture Texture, int Width)[][] textures;
    private readonly IFontAtlas atlas;
    private readonly byte[][] fontBytes;
    private readonly IFontHandle[] fonts;
    private readonly Dictionary<(int Face, int Pixels), IFontHandle> sized = [];
    private readonly FontScope scope;

    internal PassportAssets(ITextureProvider provider, IFontAtlas atlas)
    {
        var assembly = typeof(PassportAssets).Assembly;
        this.atlas = atlas;
        var resources = new HashSet<string>(assembly.GetManifestResourceNames(), StringComparer.Ordinal);
        textures = Array.ConvertAll(TextureNames, name =>
        {
            var levels = new List<(ISharedImmediateTexture, int)>();
            foreach (var level in Levels)
            {
                var resource = "Wanderveil.Assets." + name + level + ".png";
                if (resources.Contains(resource)) levels.Add((provider.GetFromManifestResource(assembly, resource), PngWidth(assembly, resource)));
            }
            return levels.ToArray();
        });
        fontBytes = Array.ConvertAll(FontFiles, name => ReadFont(assembly, name));
        fonts = new IFontHandle[FontFiles.Length];
        try { for (var index = 0; index < fonts.Length; index++) fonts[index] = CreateFont(index, BaseSizePx); }
        catch { DisposeFonts(); throw; }
        scope = new FontScope(this);
    }

    public bool TryGet(BookAsset asset, out BookImage image, float drawWidth = 0f)
    {
        // The host asynchronously loads/caches shared resources; these frame wraps must not be retained/disposed.
        image = default;
        if ((uint)asset >= textures.Length || textures[(int)asset].Length == 0) return false;
        var levels = textures[(int)asset];
        Span<int> widths = stackalloc int[levels.Length];
        for (var index = 0; index < levels.Length; index++) widths[index] = levels[index].Width;
        var wanted = BookImageLevels.Pick(widths, drawWidth);
        // While the wanted copy loads, draw whichever copy is ready, nearest in size first.
        for (var distance = 0; distance < levels.Length; distance++)
            foreach (var index in new[] { wanted - distance, wanted + distance })
            {
                if ((uint)index >= levels.Length) continue;
                var wrap = levels[index].Texture.GetWrapOrDefault();
                if (wrap == null) continue;
                image = new BookImage(wrap.Handle, wrap.Width, wrap.Height);
                return true;
            }
        return false;
    }

    private static int PngWidth(Assembly assembly, string resource)
    {
        // The IHDR width sits at bytes 16-19 of every PNG.
        using var stream = assembly.GetManifestResourceStream(resource)!;
        Span<byte> header = stackalloc byte[24];
        stream.ReadExactly(header);
        return (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
    }

    // Locks the base fonts for one frame; sized fonts are locked on first use and released with the scope.
    internal FontScope LockFonts()
    {
        scope.Begin(fonts);
        return scope;
    }

    public void Dispose() => DisposeFonts();

    private void DisposeFonts()
    {
        scope?.Dispose();
        foreach (var handle in sized.Values) handle.Dispose();
        sized.Clear();
        for (var index = fonts.Length - 1; index >= 0; index--) fonts[index]?.Dispose();
    }

    private IFontHandle? SizedHandle(int face, int pixels)
    {
        if (sized.TryGetValue((face, pixels), out var handle)) return handle;
        if (sized.Count >= MaxSizedFonts) return null;
        handle = CreateFont(face, pixels / MathF.Max(0.01f, ImGuiHelpers.GlobalScale));
        sized.Add((face, pixels), handle);
        return handle;
    }

    private IFontHandle CreateFont(int face, float sizePx)
    {
        var bytes = fontBytes[face];
        var name = FontFiles[face];
        // The plugin atlas applies the host global scale, so sizePx is requested in unscaled pixels.
        return atlas.NewDelegateFontHandle(step => step.OnPreBuild(toolkit =>
            toolkit.AddFontFromMemory(bytes, new SafeFontConfig { SizePx = sizePx }, name)));
    }

    private static byte[] ReadFont(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream("Wanderveil.Assets." + name)
            ?? throw new FileNotFoundException("Bundled passport font is missing.", name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    internal sealed class FontScope(PassportAssets owner) : IUiFonts, IDisposable
    {
        private readonly ILockedImFont?[] locks = new ILockedImFont?[FontFiles.Length];
        private readonly List<ILockedImFont> extra = [];
        private readonly Dictionary<(int Face, int Pixels), ImFontPtr> frame = [];

        internal ImFontPtr Title => Font(0);
        internal ImFontPtr Heading => Font(1);
        internal ImFontPtr Body => Font(2);
        internal ImFontPtr Display => Font(3);
        internal ImFontPtr Mono => Font(4);
        internal ImFontPtr Signature => Font(5);

        internal void Begin(IFontHandle[] handles)
        {
            Dispose();
            try { for (var index = 0; index < handles.Length; index++) locks[index] = handles[index].Available ? handles[index].Lock() : null; }
            catch { Dispose(); throw; }
        }

        public unsafe ImFontPtr Sized(ImFontPtr font, float pixels)
        {
            var face = -1;
            for (var index = 0; index < locks.Length; index++)
                if (locks[index] is { } locked && (nint)locked.ImFont.Handle == (nint)font.Handle) { face = index; break; }
            var size = (int)MathF.Round(pixels);
            if (face < 0 || size < 6 || size > 200) return font;
            if (frame.TryGetValue((face, size), out var cached)) return cached;
            var result = font;
            // A size still building draws once at the scaled base size instead of waiting.
            if (owner.SizedHandle(face, size) is { Available: true } handle)
            {
                var locked = handle.Lock();
                extra.Add(locked);
                result = locked.ImFont;
            }
            frame[(face, size)] = result;
            return result;
        }

        private ImFontPtr Font(int index) => locks[index]?.ImFont ?? default;

        public void Dispose()
        {
            frame.Clear();
            for (var index = extra.Count - 1; index >= 0; index--) extra[index].Dispose();
            extra.Clear();
            for (var index = locks.Length - 1; index >= 0; index--) { locks[index]?.Dispose(); locks[index] = null; }
        }
    }
}
