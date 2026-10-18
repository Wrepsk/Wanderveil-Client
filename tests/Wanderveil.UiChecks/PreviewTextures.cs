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

    public bool TryGet(BookAsset asset, out BookImage image) => images.TryGetValue(asset, out image);
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
