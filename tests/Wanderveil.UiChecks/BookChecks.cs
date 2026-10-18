using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

internal static class BookChecks
{
    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        foreach (var invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            try { BookSurface.Height(invalid, true, 100); throw new InvalidOperationException("Invalid book geometry accepted."); }
            catch (ArgumentOutOfRangeException) { check(true, "Invalid book geometry rejected."); }
        }
        foreach (var width in new[] { 280f, 400f, 820f, 1200f })
            foreach (var spread in new[] { true, false })
                foreach (var contentHeight in new[] { 100f, 800f, 2400f })
                {
                    var height = BookSurface.Height(width, spread, contentHeight);
                    var book = new UiRect(new Vector2(24, 30), new Vector2(width, height));
                    foreach (var face in spread ? new[] { BookFace.Spread } : new[] { BookFace.Left, BookFace.Right })
                    {
                        var content = BookSurface.Content(book, face);
                        check(content.Position.X >= book.Position.X && content.Max.X <= book.Max.X, "Book content escapes the paper horizontally.");
                        check(content.Size.Y + 0.01f >= contentHeight && content.Max.Y <= book.Max.Y, "Book cannot contain measured text.");
                    }
                    if (spread) check(BookSurface.RightContent(book).Max.X <= book.Max.X, "Right page escapes the cover.");
                }

        foreach (var target in new[] { "cover", "identity", "collection" })
        {
            using var host = new HeadlessUi(new Vector2(target == "cover" ? 760 : 1140, 960), materials: true);
            void Draw()
            {
                var context = new UiContext(1, ImGui.GetFontSize()) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont };
                var font = ImGui.GetFont();
                PassportTargets.Draw(target, host.Textures!, context);
                check(ImGui.GetFont() == font, "Passport typography leaked a font stack entry.");
                check(ImGui.GetScrollMaxX() == 0, "Passport target overflows horizontally.");
            }
            for (var frame = 0; frame < 4; frame++) host.Frame(Draw);
            check(ImGui.GetDrawData().CmdListsCount > 0, "Passport target has no draw data.");
            if (artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "passport-target-" + target + ".png"), host);
            Console.WriteLine("PASS passport-target-" + target);
        }

        foreach (var scenario in new[] { (Name: "wide", Width: 1140f, Zoom: 1f, Scale: 1f), (Name: "narrow", Width: 420f, Zoom: 1f, Scale: 1f), (Name: "large-text", Width: 820f, Zoom: 1.75f, Scale: 1f), (Name: "host-scale", Width: 820f, Zoom: 1f, Scale: 2f) })
            foreach (var missing in new[] { false, true })
            {
                using var host = new HeadlessUi(new Vector2(scenario.Width, 960), scale: scenario.Scale, textScale: scenario.Zoom, materials: true);
                var view = new PassportView(PassportPreview.Sample, missing ? new MissingAssets() : host.Textures);
                void Draw()
                {
                    view.Draw(new UiContext(scenario.Scale, ImGui.GetFontSize()) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont });
                    check(ImGui.GetScrollMaxX() == 0, "Asset-backed passport has horizontal overflow.");
                }
                for (var frame = 0; frame < 4; frame++) host.Frame(Draw);
                if (artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "passport-material-" + scenario.Name + (missing ? "-fallback" : "") + ".bmp"), host);
            }

        using (var host = new HeadlessUi(new Vector2(420, 400), materials: true))
        {
            var profile = PassportPreview.Sample with { Name = "Ari of the Many Winding Roads and Forgotten Horizons", Bio = string.Concat(Enumerable.Repeat("A long story beyond the next horizon. ", 20)) };
            var view = new PassportView(profile, host.Textures);
            var activated = 0;
            void Draw()
            {
                var context = new UiContext(1, ImGui.GetFontSize()) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont };
                if (view.Draw(context)) activated++;
                check(ImGui.GetScrollMaxX() == 0, "Long book text overflows horizontally.");
            }
            host.Frame(Draw);
            host.Frame(Draw);
            host.Frame(() => { ImGui.SetKeyboardFocusHere(); Draw(); });
            ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, true);
            host.Frame(Draw);
            ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, false);
            host.Frame(Draw);
            host.Frame(Draw);
            check(activated == 1, "Asset-backed passport lost its keyboard Explore action.");
            host.Frame(() => { Draw(); check(ImGui.GetScrollY() > 0f, "Keyboard focus did not reveal the scrolled book action."); });
            if (artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "passport-material-long-end.bmp"), host);
            host.Frame(() =>
            {
                var context = new UiContext(1, ImGui.GetFontSize());
                var native = UiTypography.Measure("名前", TextRole.Title, context, 240);
                var serif = UiTypography.Measure("名前", TextRole.Title, context with { TitleFont = host.TitleFont }, 240);
                check(native == serif, "Unsupported decorative glyphs did not fall back to the host font.");
            });
        }
    }

    private sealed class MissingAssets : IBookAssets
    {
        public bool TryGet(BookAsset asset, out BookImage image) { image = default; return false; }
    }
}
