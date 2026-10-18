using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.UI;
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
        foreach (var asset in new[] { (Resource: "gridania-cover.png", File: "Studies/Gridania/cover.png"), (Resource: "gridania-banner.png", File: "Studies/Gridania/banner.png"), (Resource: "gridania-map.png", File: "Studies/Gridania/map-overhead.png"), (Resource: "gridania-crest.png", File: "Studies/Gridania/crest.png"), (Resource: "Caveat.ttf", File: "Caveat.ttf"), (Resource: "CormorantGaramond-Bold.ttf", File: "CormorantGaramond-Bold.ttf"), (Resource: "CormorantGaramond-SemiBold.ttf", File: "CormorantGaramond-SemiBold.ttf"), (Resource: "CormorantGaramond-Medium.ttf", File: "CormorantGaramond-Medium.ttf"), (Resource: "Cinzel-Bold.ttf", File: "Cinzel-Bold.ttf"), (Resource: "CourierPrime-Regular.ttf", File: "CourierPrime-Regular.ttf") })
        {
            using var stream = plugin.GetManifestResourceStream("Wanderveil.Assets." + asset.Resource);
            check(stream != null, "Installed-client resource missing: " + asset.Resource);
            using var original = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", asset.File));
            check(SHA256.HashData(stream!).SequenceEqual(SHA256.HashData(original)), "Runtime and preview asset differ: " + asset.Resource);
        }
        check(!plugin.GetManifestResourceNames().Any(name => name.Contains("plum", StringComparison.OrdinalIgnoreCase)), "Unused colorway study entered the runtime assembly.");
        check(!plugin.GetManifestResourceNames().Any(name => name.Contains("SourceSerif4", StringComparison.Ordinal)), "Replaced Source Serif 4 font entered the runtime assembly.");

        foreach (var single in new[] { false, true })
        {
            var viewport = single ? new Vector2(452, 760) : new Vector2(1200, 900);
            using var host = new HeadlessUi(viewport, materials: true);
            var shell = new BookShell(host.Textures!, PassportPreview.Sample);
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
            Vector2 PagerPoint(float fraction)
            {
                var layout = BookShellLayout.Create(bounds, context, shell.VisiblePage);
                var footer = layout.Footer;
                var small = MathF.Max(28, context.FontSize + 10);
                var end = layout.StackControls ? footer.Max.X : footer.Max.X - 3 * small - 15;
                var settingsWidth = MathF.Min(80, (end - footer.Position.X) * 0.45f);
                var start = footer.Position.X + settingsWidth + 5;
                return new Vector2(start + (end - start) * fraction, footer.Position.Y + layout.ControlHeight / 2);
            }
            Frame(); Frame();
            Click(BookShellLayout.OpenButton(bounds, context).Position + BookShellLayout.OpenButton(bounds, context).Size / 2);
            check(shell.IsBookOpen && shell.VisiblePage == 0, "Runtime cover does not open onto the nation spread.");
            if (!single && artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "passport-runtime-nation.png"), host);
            var step = single ? 1 : 2;
            for (var page = 0; page < 2; page += step) Click(PagerPoint(0.98f));
            check(shell.VisiblePage == 2, "Identity does not follow the nation introduction.");
            ImGui.GetIO().AddMousePosEvent(-100, -100); Frame();
            if (!single && artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "passport-runtime-identity.png"), host);
            for (var page = 0; page < 2; page += step) Click(PagerPoint(0.98f));
            check(shell.VisiblePage == 4, "Stamp pages do not follow identity.");
            if (!single && artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "passport-runtime-collection.png"), host);
            if (single) Click(PagerPoint(0.98f));
            Click(PagerPoint(0.98f));
            check(shell.VisiblePage == (single ? 5 : 4), "Disabled Next escapes the final page.");
            Click(PagerPoint(0.02f));
            check(shell.VisiblePage == (single ? 4 : 2), "Previous cannot navigate backward.");
            Click(PagerPoint(0.5f));
            check(!shell.IsBookOpen, "Cover control does not close the book.");
            ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, true); Frame();
            ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, false); Frame();
            check(shell.IsBookOpen && shell.VisiblePage == 0, "Returning to cover loses keyboard Open or the first spread.");
            shell.BeginSession();
            check(!shell.IsBookOpen && shell.VisiblePage == 0 && shell.Section == BookSection.Passport, "Resummoning does not reset to the closed cover.");
        }

        var mutable = PassportDocumentView.SamplePages.ToList();
        for (var index = 6; index < 17; index++) mutable.Add(new PassportPage("extra-" + index, PassportPageKind.Blank));
        var expanded = new BookShell(new MissingAssets(), PassportPreview.Sample, mutable);
        mutable.Clear();
        check(expanded.PageCount == 17, "Logical page catalog is fixed to the demo or aliases mutable input.");
        foreach (var invalid in new[] { Array.Empty<PassportPage>(), new[] { new PassportPage("duplicate", PassportPageKind.Blank), new PassportPage("duplicate", PassportPageKind.Blank) } })
        {
            try { _ = new BookShell(new MissingAssets(), PassportPreview.Sample, invalid); check(false, "Invalid logical page identifiers accepted."); }
            catch (ArgumentException) { check(true, "Invalid logical page catalog rejected."); }
        }
    }

    private sealed class MissingAssets : Wanderveil.UI.Components.IBookAssets
    {
        public bool TryGet(Wanderveil.UI.Components.BookAsset asset, out Wanderveil.UI.Components.BookImage image) { image = default; return false; }
    }
}
