using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Components;

namespace Wanderveil.UiChecks;

internal sealed class PreviewTextures : IBookAssets
{
    internal sealed record Texture(int Width, int Height, byte[] Pixels);
    private readonly Dictionary<ulong, Texture> textures = [];
    private readonly Dictionary<BookAsset, BookImage> images = [];
    private readonly Dictionary<string, BookImage> studies = [];

    internal PreviewTextures()
    {
        var names = new[] { "book-open.png", "book-cover.png", "demo-portrait.png", "stamp-atlas.png" };
        for (var index = 0; index < names.Length; index++)
        {
            using var bitmap = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Assets", names[index]));
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new byte[bitmap.Width * bitmap.Height * 4];
                for (var y = 0; y < bitmap.Height; y++)
                    Marshal.Copy(data.Scan0 + (y * data.Stride), pixels, y * bitmap.Width * 4, bitmap.Width * 4);
                // GDI's BGRA source is converted to the same straight RGBA sampling as ImGui's font atlas.
                for (var pixel = 0; pixel < pixels.Length; pixel += 4)
                    (pixels[pixel], pixels[pixel + 2]) = (pixels[pixel + 2], pixels[pixel]);
                var id = (ulong)index + 2;
                textures[id] = new Texture(bitmap.Width, bitmap.Height, pixels);
                images[(BookAsset)index] = new BookImage(new ImTextureID(id), bitmap.Width, bitmap.Height);
            }
            finally { bitmap.UnlockBits(data); }
        }
        foreach (var name in new[] { "cover", "banner", "map-overhead", "crest" }) LoadStudy(name);
        images[BookAsset.EditionCover] = Study("cover");
        images[BookAsset.NationBanner] = Study("banner");
        images[BookAsset.NationMap] = Study("map-overhead");
        images[BookAsset.EditionCrest] = Study("crest");
    }

    public bool TryGet(BookAsset asset, out BookImage image, float drawWidth = 0f) => images.TryGetValue(asset, out image);

    // Recoloured plates, made at once (the plugin builds them in the background) from the generated files.
    private readonly Dictionary<(string, PlateTint, BookAsset), BookImage> recoloured = [];
    internal int Recolourings { get; private set; }

    public bool TryGet(BookAsset asset, string edition, PlateTint tint, out BookImage image, float drawWidth = 0f)
    {
        if (recoloured.TryGetValue((edition, tint, asset), out image)) return true;
        var plate = EditionRecolour.Plates.First(entry => entry.Asset == asset).Plate;
        var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Editions", edition);
        var full = EditionRecolour.Levels(name => File.Exists(Path.Combine(folder, name)) ? File.OpenRead(Path.Combine(folder, name)) : null, plate, tint)[0];
        var id = 7000UL + (ulong)recoloured.Count;
        Register(id, full.Width, full.Height, full.Rgba);
        image = new BookImage(new ImTextureID(id), full.Width, full.Height);
        recoloured[(edition, tint, asset)] = image;
        Recolourings++;
        return true;
    }

    // Other editions' plates load from disk the first time one is drawn.
    private readonly Dictionary<(string, BookAsset), BookImage> editions = [];

    public bool TryGet(BookAsset asset, string edition, out BookImage image, float drawWidth = 0f)
    {
        if (edition == "gridania" || !IBookAssets.IsEditionPlate(asset)) return TryGet(asset, out image, drawWidth);
        if (editions.TryGetValue((edition, asset), out image)) return true;
        var file = Path.Combine(AppContext.BaseDirectory, "Assets", "Editions", edition, asset switch
        {
            BookAsset.Open => "book-open.png",
            BookAsset.EditionCover => "cover.png",
            BookAsset.NationBanner => "banner.png",
            BookAsset.NationMap => "map-overhead.png",
            _ => "crest.png",
        });
        if (!File.Exists(file)) return false;
        using var bitmap = new Bitmap(file);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[bitmap.Width * bitmap.Height * 4];
            for (var y = 0; y < bitmap.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bitmap.Width * 4, bitmap.Width * 4);
            for (var pixel = 0; pixel < pixels.Length; pixel += 4) (pixels[pixel], pixels[pixel + 2]) = (pixels[pixel + 2], pixels[pixel]);
            var id = 5000UL + (ulong)editions.Count;
            Register(id, bitmap.Width, bitmap.Height, pixels);
            image = new BookImage(new ImTextureID(id), bitmap.Width, bitmap.Height);
            editions[(edition, asset)] = image;
            return true;
        }
        finally { bitmap.UnlockBits(data); }
    }
    internal Texture Get(ulong id) => textures[id];
    internal void Register(ulong id, int width, int height, byte[] pixels) => textures.Add(id, new Texture(width, height, pixels));

    internal void LoadStudy(string name)
    {
        if (studies.ContainsKey(name)) return;
        using var bitmap = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Assets", "Studies", "Gridania", name + ".png"));
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[bitmap.Width * bitmap.Height * 4];
            for (var y = 0; y < bitmap.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bitmap.Width * 4, bitmap.Width * 4);
            for (var pixel = 0; pixel < pixels.Length; pixel += 4) (pixels[pixel], pixels[pixel + 2]) = (pixels[pixel + 2], pixels[pixel]);
            var id = 2000UL + (ulong)studies.Count;
            Register(id, bitmap.Width, bitmap.Height, pixels);
            studies.Add(name, new BookImage(new ImTextureID(id), bitmap.Width, bitmap.Height));
        }
        finally { bitmap.UnlockBits(data); }
    }

    internal BookImage Study(string name) => studies[name];
}
