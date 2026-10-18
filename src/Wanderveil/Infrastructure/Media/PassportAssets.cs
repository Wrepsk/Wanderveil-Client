using System;
using System.IO;
using System.Reflection;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using Wanderveil.UI.Components;

namespace Wanderveil.Infrastructure.Media;

internal sealed class PassportAssets : IBookAssets, IDisposable
{
    private readonly ISharedImmediateTexture[] textures;
    // Order matches FontScope: title, heading, body, display, mono, signature.
    private static readonly string[] FontFiles = ["CormorantGaramond-Bold.ttf", "CormorantGaramond-SemiBold.ttf",
        "CormorantGaramond-Medium.ttf", "Cinzel-Bold.ttf", "CourierPrime-Regular.ttf", "Caveat.ttf"];
    private readonly IFontHandle[] fonts;

    internal PassportAssets(ITextureProvider provider, IFontAtlas atlas)
    {
        var assembly = typeof(PassportAssets).Assembly;
        textures = [provider.GetFromManifestResource(assembly, "Wanderveil.Assets.book-open.png"),
            provider.GetFromManifestResource(assembly, "Wanderveil.Assets.book-cover.png"),
            provider.GetFromManifestResource(assembly, "Wanderveil.Assets.demo-portrait.png"),
            provider.GetFromManifestResource(assembly, "Wanderveil.Assets.stamp-atlas.png"),
            provider.GetFromManifestResource(assembly, "Wanderveil.Assets.gridania-cover.png"),
            provider.GetFromManifestResource(assembly, "Wanderveil.Assets.gridania-banner.png"),
            provider.GetFromManifestResource(assembly, "Wanderveil.Assets.gridania-map.png"),
            provider.GetFromManifestResource(assembly, "Wanderveil.Assets.gridania-crest.png")];
        fonts = new IFontHandle[FontFiles.Length];
        try { for (var index = 0; index < fonts.Length; index++) fonts[index] = CreateFont(atlas, assembly, FontFiles[index]); }
        catch { DisposeFonts(); throw; }
    }

    public bool TryGet(BookAsset asset, out BookImage image)
    {
        // The host asynchronously loads/caches shared resources; these frame wraps must not be retained/disposed.
        if ((uint)asset >= textures.Length) { image = default; return false; }
        var wrap = textures[(int)asset].GetWrapOrDefault();
        image = wrap == null ? default : new BookImage(wrap.Handle, wrap.Width, wrap.Height);
        return wrap != null;
    }

    internal FontScope LockFonts() => new(fonts);

    public void Dispose() => DisposeFonts();

    private void DisposeFonts()
    {
        for (var index = fonts.Length - 1; index >= 0; index--) fonts[index]?.Dispose();
    }

    private static IFontHandle CreateFont(IFontAtlas atlas, Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream("Wanderveil.Assets." + name)
            ?? throw new FileNotFoundException("Bundled passport font is missing.", name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        return atlas.NewDelegateFontHandle(step => step.OnPreBuild(toolkit =>
            toolkit.AddFontFromMemory(bytes, new SafeFontConfig { SizePx = 24f }, name)));
    }

    internal readonly struct FontScope : IDisposable
    {
        private readonly ILockedImFont?[] locks;
        internal FontScope(IFontHandle[] handles)
        {
            locks = new ILockedImFont?[handles.Length];
            try { for (var index = 0; index < handles.Length; index++) locks[index] = handles[index].Available ? handles[index].Lock() : null; }
            catch { Dispose(); throw; }
        }
        internal Dalamud.Bindings.ImGui.ImFontPtr Title => Font(0);
        internal Dalamud.Bindings.ImGui.ImFontPtr Heading => Font(1);
        internal Dalamud.Bindings.ImGui.ImFontPtr Body => Font(2);
        internal Dalamud.Bindings.ImGui.ImFontPtr Display => Font(3);
        internal Dalamud.Bindings.ImGui.ImFontPtr Mono => Font(4);
        internal Dalamud.Bindings.ImGui.ImFontPtr Signature => Font(5);
        private Dalamud.Bindings.ImGui.ImFontPtr Font(int index) => locks[index]?.ImFont ?? default;
        public void Dispose() { for (var index = locks.Length - 1; index >= 0; index--) locks[index]?.Dispose(); }
    }
}
