using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.UI;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

internal static class RuntimePassportChecks
{
    internal static void Run(Action<bool, string> check, string? artifacts)
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var plugin = Assembly.LoadFile(Path.Combine(root, "src", "Wanderveil", "bin", "x64", configuration, "Wanderveil.dll"));
        foreach (var asset in new[] { (Resource: "gridania-cover.png", File: "Studies/Gridania/cover.png"), (Resource: "gridania-banner.png", File: "Studies/Gridania/banner.png"), (Resource: "gridania-map-overhead.png", File: "Studies/Gridania/map-overhead.png"), (Resource: "gridania-crest.png", File: "Studies/Gridania/crest.png"), (Resource: "Caveat.ttf", File: "Caveat.ttf"), (Resource: "CormorantGaramond-Bold.ttf", File: "CormorantGaramond-Bold.ttf"), (Resource: "CormorantGaramond-SemiBold.ttf", File: "CormorantGaramond-SemiBold.ttf"), (Resource: "CormorantGaramond-Medium.ttf", File: "CormorantGaramond-Medium.ttf"), (Resource: "Cinzel-Bold.ttf", File: "Cinzel-Bold.ttf"), (Resource: "CourierPrime-Regular.ttf", File: "CourierPrime-Regular.ttf") })
        {
            using var stream = plugin.GetManifestResourceStream("Wanderveil.Assets." + asset.Resource);
            check(stream != null, "Installed-client resource missing: " + asset.Resource);
            using var original = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", asset.File));
            check(SHA256.HashData(stream!).SequenceEqual(SHA256.HashData(original)), "Runtime and preview asset differ: " + asset.Resource);
        }
        check(!plugin.GetManifestResourceNames().Any(name => name.Contains("plum", StringComparison.OrdinalIgnoreCase)), "Unused colorway study entered the runtime assembly.");
        int[] widths = [1254, 627, 314, 157];
        check(BookImageLevels.Pick(widths, 150) == 3 && BookImageLevels.Pick(widths, 158) == 2 && BookImageLevels.Pick(widths, 600) == 1, "Size copy choice does not pick the smallest copy at least as wide as drawn.");
        check(BookImageLevels.Pick(widths, 4000) == 0 && BookImageLevels.Pick(widths, 0) == 3 && BookImageLevels.Pick([1024], 50) == 0, "Size copy choice mishandles oversized, unsized, or single-copy draws.");
        // Every plate ships reduced copies so the client never shrinks a texture more than about 2x.
        // Every edition's own plates are installed, each matching the generated plate byte for byte.
        foreach (var edition in PassportEdition.All)
            foreach (var plate in new[] { "book-open", "cover", "banner", "map-overhead", "crest" })
            {
                using var stream = plugin.GetManifestResourceStream($"Wanderveil.Assets.{edition.Id}-{plate}.png");
                check(stream != null, $"Installed-client resource missing: {edition.Id}-{plate}");
                using var original = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", "Editions", edition.Id, plate + ".png"));
                check(SHA256.HashData(stream!).SequenceEqual(SHA256.HashData(original)), $"Runtime and generated plate differ: {edition.Id}-{plate}");
            }
        foreach (var plate in new[] { "book-cover", "demo-portrait", "stamp-atlas", "symbols" }.Concat(PassportEdition.All.SelectMany(edition => new[] { "book-open", "cover", "banner", "map-overhead", "crest" }.Select(plate => edition.Id + "-" + plate))))
            foreach (var level in new[] { ".half", ".quarter" })
                check(plugin.GetManifestResourceNames().Contains("Wanderveil.Assets." + plate + level + ".png"), "Runtime plate lacks a reduced copy: " + plate + level);
        check(!plugin.GetManifestResourceNames().Any(name => name.Contains("SourceSerif4", StringComparison.Ordinal)), "Replaced Source Serif 4 font entered the runtime assembly.");

        // A reduced copy must be its full plate shrunk, never cropped or shifted (a missing viewBox once cropped covers).
        foreach (var level in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Assets"), "*.png", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".half.png", StringComparison.Ordinal) || file.EndsWith(".quarter.png", StringComparison.Ordinal) || file.EndsWith(".eighth.png", StringComparison.Ordinal)))
        {
            var full = level[..level.LastIndexOf('.', level.Length - 5)] + ".png";
            check(File.Exists(full), "Reduced copy has no full plate: " + Path.GetFileName(level));
            check(LevelDifference(full, level) < 0.06f, "Reduced copy does not match its full plate: " + Path.GetFileName(level));
        }

        foreach (var single in new[] { false, true })
        {
            var viewport = single ? new Vector2(452, 760) : new Vector2(1200, 900);
            using var host = new HeadlessUi(viewport, materials: true);
            var shell = new BookShell(host.Textures!, PassportIdentity.Sample) { ReducedMotion = true };
            var context = new UiContext(1, 18) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont, SignatureFont = host.SignatureFont };
            UiRect bounds = default;
            void Frame() => host.BookFrame(shell.FitSize(viewport - new Vector2(32), context), () =>
            {
                bounds = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize());
                shell.Draw(bounds, context);
                check(ImGui.GetScrollMaxX() == 0 && ImGui.GetScrollMaxY() == 0, "Runtime passport scrolls its host.");
            });
            void Click(Vector2 point)
            {
                ImGui.GetIO().AddMousePosEvent(point.X, point.Y); Frame();
                ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
                ImGui.GetIO().AddMouseButtonEvent(0, false); Frame(); Frame();
            }
            Vector2 Side(bool forward) => BookShellLayout.Create(bounds, context, shell.VisiblePage).TurnPoint(forward, shell.VisiblePage);
            Frame(); Frame();
            Click(BookShellLayout.OpenPoint(bounds) - new Vector2(bounds.Size.X * 0.6f, 0));
            check(!shell.IsBookOpen, "Clicking the left side of the cover opens the passport.");
            Click(BookShellLayout.OpenPoint(bounds));
            check(shell.IsBookOpen && shell.VisiblePage == 0, "Runtime cover does not open onto the nation spread.");
            if (!single && artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "passport-runtime-nation.png"), host);
            var step = single ? 1 : 2;
            for (var page = 0; page < 2; page += step) Click(Side(true));
            check(shell.VisiblePage == 2, "Identity does not follow the nation introduction.");
            ImGui.GetIO().AddMousePosEvent(-100, -100); Frame();
            if (!single && artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "passport-runtime-identity.png"), host);
            for (var page = 0; page < 2; page += step) Click(Side(true));
            check(shell.VisiblePage == 4, "Stamp pages do not follow identity.");
            if (!single && artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "passport-runtime-collection.png"), host);
            // Blank stamp pages carry on past the fixed pages; there is no last page.
            if (single) Click(Side(true));
            Click(Side(true));
            check(shell.VisiblePage == 6, "Stamp pages do not carry on past the first.");
            Click(Side(false));
            check(shell.VisiblePage == (single ? 5 : 4), "The left side cannot turn backward.");
            ImGui.GetIO().AddKeyEvent(ImGuiKey.PageDown, true); Frame();
            ImGui.GetIO().AddKeyEvent(ImGuiKey.PageDown, false); Frame();
            check(shell.VisiblePage == 6, "Page Down does not turn forward.");
            while (shell.IsBookOpen && shell.VisiblePage > 0) Click(Side(false));
            check(shell.IsBookOpen && shell.VisiblePage == 0, "Turning back does not stop at the first page.");
            Click(Side(false));
            check(!shell.IsBookOpen, "The left side of the first page does not return to the cover.");
            ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, true); Frame();
            ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, false); Frame();
            check(shell.IsBookOpen && shell.VisiblePage == 0, "Returning to cover loses keyboard Open or the first spread.");
            shell.BeginSession();
            check(!shell.IsBookOpen && shell.VisiblePage == 0 && shell.Section == BookSection.Passport, "Resummoning does not reset to the closed cover.");
        }

        // Shrinking the book shrinks its printed contents instead of making pages scroll.
        foreach (var steps in new[] { 0, 3, 6, 9 })
        {
            using var host = new HeadlessUi(new Vector2(1600, 1100), materials: true);
            var shell = new BookShell(host.Textures!, PassportIdentity.Sample) { ReducedMotion = true };
            var context = new UiContext(1, 18) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont, SignatureFont = host.SignatureFont };
            UiRect bounds = default;
            void Frame() => host.BookFrame(shell.FitSize(new Vector2(1568, 1068), context), () => { bounds = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize()); shell.Draw(bounds, context); });
            void Press(ImGuiKey key) { ImGui.GetIO().AddKeyEvent(key, true); Frame(); ImGui.GetIO().AddKeyEvent(key, false); for (var frame = 0; frame < 4; frame++) Frame(); }
            Frame(); Frame();
            Press(ImGuiKey.Space);
            for (var step = 0; step < steps; step++)
            {
                var layout = BookShellLayout.Create(bounds, context, shell.VisiblePage);
                ImGui.GetIO().AddMousePosEvent(layout.Tool(0, context).Position.X + 4, layout.Tool(0, context).Position.Y + 4); Frame();
                ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
                ImGui.GetIO().AddMouseButtonEvent(0, false); Frame(); Frame();
            }
            ImGui.GetIO().AddMousePosEvent(-100, -100); Frame();
            for (var page = 0; page < shell.FixedPageCount + 2; page++)
            {
                check(shell.PageOverflow < 1, $"Passport page {shell.VisiblePage + 1} scrolls by {shell.PageOverflow:0.0} at preferred width {shell.PreferredWidth:0} ({bounds.Size.X:0} x {bounds.Size.Y:0}).");
                var before = shell.VisiblePage;
                Press(ImGuiKey.PageDown);
                if (shell.VisiblePage == before) break;
            }
            if (artifacts != null && steps == 9) SoftwarePreview.Save(Path.Combine(artifacts, "passport-runtime-smallest.png"), host);
        }

        var mutable = PassportDocumentView.SamplePages.ToList();
        for (var index = 4; index < 17; index++) mutable.Add(new PassportPage("extra-" + index, PassportPageKind.Blank));
        var expanded = new BookShell(new MissingAssets(), PassportIdentity.Sample, mutable);
        mutable.Clear();
        check(expanded.FixedPageCount == 17, "Logical page catalog is fixed to the demo or aliases mutable input.");
        foreach (var invalid in new[] { Array.Empty<PassportPage>(), new[] { new PassportPage("duplicate", PassportPageKind.Blank), new PassportPage("duplicate", PassportPageKind.Blank) } })
        {
            try { _ = new BookShell(new MissingAssets(), PassportIdentity.Sample, invalid); check(false, "Invalid logical page identifiers accepted."); }
            catch (ArgumentException) { check(true, "Invalid logical page catalog rejected."); }
        }
    }

    // Mean per-channel difference (0-1) between a reduced copy and its full plate scaled to the same size.
    private static float LevelDifference(string fullPath, string levelPath)
    {
        using var level = new System.Drawing.Bitmap(levelPath);
        using var source = new System.Drawing.Bitmap(fullPath);
        using var scaled = new System.Drawing.Bitmap(level.Width, level.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new System.Drawing.Rectangle(0, 0, level.Width, level.Height));
        }
        double total = 0;
        var step = Math.Max(1, Math.Min(level.Width, level.Height) / 96);
        var samples = 0;
        for (var y = 0; y < level.Height; y += step)
            for (var x = 0; x < level.Width; x += step)
            {
                var a = level.GetPixel(x, y);
                var b = scaled.GetPixel(x, y);
                total += (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) + Math.Abs(a.A - b.A)) / (4 * 255.0);
                samples++;
            }
        return (float)(total / samples);
    }

    private sealed class MissingAssets : Wanderveil.UI.Components.IBookAssets
    {
        public bool TryGet(Wanderveil.UI.Components.BookAsset asset, out Wanderveil.UI.Components.BookImage image, float drawWidth = 0f) { image = default; return false; }
    }
}
