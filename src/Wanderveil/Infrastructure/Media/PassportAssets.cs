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
    private readonly IFontHandle title;
    private readonly IFontHandle heading;
    private readonly IFontHandle signature;

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
        title = CreateFont(atlas, assembly, "SourceSerif4-Semibold.ttf");
        try { heading = CreateFont(atlas, assembly, "SourceSerif4-Regular.ttf"); }
        catch { title.Dispose(); throw; }
        try { signature = CreateFont(atlas, assembly, "Caveat.ttf"); }
        catch { heading.Dispose(); title.Dispose(); throw; }
    }

    public bool TryGet(BookAsset asset, out BookImage image)
    {
        // The host asynchronously loads/caches shared resources; these frame wraps must not be retained/disposed.
        if ((uint)asset >= textures.Length) { image = default; return false; }
        var wrap = textures[(int)asset].GetWrapOrDefault();
        image = wrap == null ? default : new BookImage(wrap.Handle, wrap.Width, wrap.Height);
        return wrap != null;
    }

    internal FontScope LockFonts() => new(title, heading, signature);

    public void Dispose()
    {
        signature.Dispose();
        heading.Dispose();
        title.Dispose();
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
        private readonly ILockedImFont? title;
        private readonly ILockedImFont? heading;
        private readonly ILockedImFont? signature;
        internal FontScope(IFontHandle titleHandle, IFontHandle headingHandle, IFontHandle signatureHandle)
        {
            title = titleHandle.Available ? titleHandle.Lock() : null;
            try { heading = headingHandle.Available ? headingHandle.Lock() : null; }
            catch { title?.Dispose(); throw; }
            try { signature = signatureHandle.Available ? signatureHandle.Lock() : null; }
            catch { heading?.Dispose(); title?.Dispose(); throw; }
        }
        internal Dalamud.Bindings.ImGui.ImFontPtr Title => title?.ImFont ?? default;
        internal Dalamud.Bindings.ImGui.ImFontPtr Heading => heading?.ImFont ?? default;
        internal Dalamud.Bindings.ImGui.ImFontPtr Signature => signature?.ImFont ?? default;
        public void Dispose() { signature?.Dispose(); heading?.Dispose(); title?.Dispose(); }
    }
}
