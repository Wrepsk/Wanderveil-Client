using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.UI;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

internal static class BookShellChecks
{
    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        foreach (var room in new[] { new Vector2(640, 480), new Vector2(420, 720), new Vector2(1920, 1080), new Vector2(1600, 480) })
            foreach (var preferred in new[] { 420f, 740f, 1040f, 1400f })
                foreach (var font in new[] { 13f, 18f, 31.5f, 36f })
                {
                    var context = new UiContext(1, font);
                    var size = BookShellLayout.Fit(preferred, room, context);
                    var layout = BookShellLayout.Create(new UiRect(Vector2.Zero, size), context, 0);
                    check(size.X <= room.X && size.Y <= room.Y, "Fitted book escapes the work area.");
                    check(layout.Book.Position.Y + layout.Book.Size.Y * 0.955f <= size.Y + 1, "Rounded spread threshold clips the book silhouette.");
                }
        foreach (var scenario in new[] {
            (Name: "wide", Size: new Vector2(1200, 800), Scale: 1f, Text: 1f),
            (Name: "narrow", Size: new Vector2(452, 760), Scale: 1f, Text: 1f),
            (Name: "compact-text", Size: new Vector2(352, 520), Scale: 1f, Text: 1.75f),
            (Name: "large-text", Size: new Vector2(1000, 900), Scale: 1f, Text: 1.75f),
            (Name: "host-scale", Size: new Vector2(1280, 1000), Scale: 2f, Text: 1f) })
        {
            using var host = new HeadlessUi(scenario.Size, scenario.Scale, scenario.Text, materials: true);
            var shell = new BookShell(host.Textures!, PassportPreview.Sample) { ReducedMotion = true };
            var context = new UiContext(scenario.Scale, 18 * scenario.Scale * scenario.Text) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont, SignatureFont = host.SignatureFont };
            var size = BookShellLayout.Fit(shell.PreferredWidth, scenario.Size - new Vector2(32), context);
            UiRect root = default;
            BookShellIntent intent = default;
            var style = ImGui.GetStyle().WindowPadding;
            void Draw()
            {
                root = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize());
                intent = shell.Draw(root, context);
                check(ImGui.GetScrollMaxX() == 0 && ImGui.GetScrollMaxY() == 0, "Book host scrolls instead of its live pages.");
                check(ImGui.GetWindowDrawList().CmdBuffer.Size > 0, "Book has no draw commands.");
            }
            void Frame()
            {
                size = shell.FitSize(scenario.Size - new Vector2(32), context);
                host.BookFrame(size, Draw);
            }
            for (var frame = 0; frame < 4; frame++) Frame();
            check(!shell.IsBookOpen, "Summoning skips the closed passport cover.");
            if (artifacts != null && scenario.Name == "wide") SoftwarePreview.Save(Path.Combine(artifacts, "passport-runtime-cover.png"), host);
            Click(BookShellLayout.OpenPoint(root));
            check(shell.IsBookOpen, "Clicking the right side of the cover does not open the passport.");
            for (var frame = 0; frame < 3; frame++) Frame();
            var layout = BookShellLayout.Create(root, context, shell.VisiblePage);
            check(size.X <= scenario.Size.X - 32.01f + 0.02f && size.Y <= scenario.Size.Y - 32 + 0.02f, "Book does not fit the viewport.");
            check(layout.LeftPage.Size.Y > 60 && layout.LeftPage.Max.Y < layout.Footer.Position.Y, "Page content collides with book controls.");
            check(layout.RightPage.Max.X <= root.Max.X, "Right content escapes the host.");
            var ribbon = BookControl.RibbonBounds(layout.Bookmark(0), context);
            check(ribbon.Max.Y > layout.Bookmark(0).Max.Y && ribbon.Max.Y < layout.LeftPage.Position.Y && ribbon.Max.X <= layout.Bookmark(0).Max.X, "Selected-tab ribbon does not hang just past its tab without covering page content.");
            check(ImGui.GetStyle().WindowPadding == style, "Book host style leaks into other windows.");
            if (artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "passport-shell-" + scenario.Name + ".png"), host);

            void Click(Vector2 position)
            {
                ImGui.GetIO().AddMousePosEvent(position.X, position.Y);
                ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
                ImGui.GetIO().AddMouseButtonEvent(0, false); Frame(); Frame();
            }
            Vector2 Center(UiRect rect) => rect.Position + rect.Size / 2;
            if (!layout.IsSpread)
            {
                Click(layout.TurnPoint(true, shell.VisiblePage));
                check(shell.VisiblePage == 1, "Clicking the right side of the page does not reach the next logical page.");
                ImGui.GetIO().AddMousePosEvent(-100, -100); Frame();
                if (artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "passport-shell-" + scenario.Name + "-notes.png"), host);
            }
            Click(Center(layout.Bookmark(1)));
            check(shell.Section == BookSection.Discover, "Book bookmark lost native activation.");
            if (artifacts != null && scenario.Name == "wide") SoftwarePreview.Save(Path.Combine(artifacts, "passport-shell-discover.png"), host);
            if (scenario.Name != "compact-text")
            {
                var discoveries = layout.IsSpread ? layout.RightPage : layout.LeftPage;
                // Card position follows the page scale; probe down the column (blank paper in Discover never turns pages).
                for (var y = 40f; shell.SelectedDiscovery == null && y < discoveries.Size.Y; y += 24 * context.Scale)
                    Click(discoveries.Position + new Vector2(40 * context.Scale, y));
                check(shell.SelectedDiscovery != null, "Discovery card cannot open inside the book page.");
                ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, true); Frame();
                ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, false); Frame(); Frame();
                check(shell.SelectedDiscovery == null, "Detail Back lost keyboard focus inside the book page.");
            }
            Click(Center(layout.Bookmark(0)));
            check(shell.Section == BookSection.Passport, "Passport bookmark cannot return to identity.");

            // A cancelled native button press must not switch sections.
            var discover = Center(layout.Bookmark(1));
            ImGui.GetIO().AddMousePosEvent(discover.X, discover.Y);
            ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
            ImGui.GetIO().AddMousePosEvent(-100, -100);
            ImGui.GetIO().AddMouseButtonEvent(0, false); Frame();
            check(shell.Section == BookSection.Passport, "Bookmark activated after release outside its bounds.");

            // Dragging from the page itself moves the book, and a drag never also turns the page.
            var pageBefore = shell.VisiblePage;
            var grip = layout.TurnPoint(true, shell.VisiblePage);
            ImGui.GetIO().AddMousePosEvent(grip.X, grip.Y); Frame();
            ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
            ImGui.GetIO().AddMousePosEvent(grip.X - 12, grip.Y + 8); Frame();
            check(intent.Drag != Vector2.Zero, "Dragging the passport does not move it.");
            ImGui.GetIO().AddMouseButtonEvent(0, false); Frame(); Frame();
            check(intent.Drag == Vector2.Zero && shell.VisiblePage == pageBefore && shell.IsBookOpen, "Releasing a drag turned the page or kept moving the book.");
            layout = BookShellLayout.Create(root, context, shell.VisiblePage);

            // The printed Settings mark turns to the Settings pages; Back returns to the page the reader left.
            var readingPage = shell.VisiblePage;
            Click(Center(layout.Settings(context)));
            check(shell.Section == BookSection.Settings && shell.ReturnSection == BookSection.Passport, "The Settings mark does not open the Settings pages.");
            if (artifacts != null && scenario.Name == "wide") SoftwarePreview.Save(Path.Combine(artifacts, "passport-shell-settings.png"), host);
            shell.GoBack(); Frame(); Frame();
            check(shell.Section == BookSection.Passport && shell.VisiblePage == readingPage, "Back does not return from Settings to the same passport page.");
            var close = Center(layout.Tool(2, context));
            ImGui.GetIO().AddMousePosEvent(close.X, close.Y); Frame();
            ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
            ImGui.GetIO().AddMouseButtonEvent(0, false); Frame();
            check(intent.Close, "Borderless book has no working close control.");
            check(shell.Phase == BookPhase.Hidden, "Close does not dismiss the passport.");
            // Summon again and reopen for the size controls.
            shell.BeginSession(); Frame(); Frame();
            var reopen = BookShellLayout.OpenPoint(root);
            ImGui.GetIO().AddMousePosEvent(reopen.X, reopen.Y); Frame();
            Click(reopen);
            check(shell.IsBookOpen, "A resummoned passport cannot reopen.");
            layout = BookShellLayout.Create(root, context, shell.VisiblePage);
            var beforeZoom = shell.PreferredWidth;
            Click(Center(layout.Tool(0, context)));
            check(shell.PreferredWidth < beforeZoom, "Smaller control does not change preferred book size.");
            layout = BookShellLayout.Create(root, context, shell.VisiblePage);
            Click(Center(layout.Tool(1, context)));
            check(MathF.Abs(shell.PreferredWidth - beforeZoom) < 0.01f, "Larger control does not restore preferred book size.");
            Console.WriteLine("PASS passport-shell-" + scenario.Name);
        }

        using (var host = new HeadlessUi(new Vector2(1200, 900), materials: true))
        {
            var profile = PassportPreview.Sample with { Bio = string.Concat(Enumerable.Repeat("A story about the long way around. ", 50)) };
            var shell = new BookShell(host.Textures!, profile) { ReducedMotion = true };
            var context = new UiContext(1, 18) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont };
            var size = BookShellLayout.Fit(1040, new Vector2(1168, 868), context);
            // Opening focuses the selected bookmark; native directional navigation reaches its peer.
            void Frame() => host.BookFrame(shell.FitSize(new Vector2(1168, 868), context), () => shell.Draw(new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize()), context));
            Frame(); Frame();
            ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, true); Frame();
            ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, false); Frame();
            check(shell.IsBookOpen, "Opening the cover lost keyboard activation.");
            var reached = false;
            for (var step = 0; step < 20 && !reached; step++)
            {
                ImGui.GetIO().AddKeyEvent(ImGuiKey.RightArrow, true); Frame();
                ImGui.GetIO().AddKeyEvent(ImGuiKey.RightArrow, false); Frame();
                ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, true); Frame();
                ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, false); Frame();
                reached = shell.Section == BookSection.Discover;
            }
            check(reached, "Keyboard cannot navigate the book shell.");
        }

        using (var host = new HeadlessUi(new Vector2(700, 700), materials: true))
        {
            var profile = PassportPreview.Sample with { Bio = string.Concat(Enumerable.Repeat("A story about the long way around. ", 50)) };
            var page = new PassportView(profile, host.Textures!);
            var context = new UiContext(1, 18) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont };
            var activated = 0;
            var childScroll = 0f;
            var focus = false;
            void Frame() => host.BookFrame(new Vector2(500, 650), () =>
            {
                ImGui.SetCursorScreenPos(new Vector2(60, 80));
                ImGui.BeginChild("Long live page", new Vector2(320, 380), false, ImGuiWindowFlags.NoBackground);
                if (focus) { ImGui.SetKeyboardFocusHere(); focus = false; }
                if (page.DrawPageContent(1, context)) activated++;
                childScroll = ImGui.GetScrollY();
                ImGui.EndChild();
                check(ImGui.GetScrollY() == 0, "Focusing page content scrolls the book host.");
            });
            Frame(); Frame(); focus = true; Frame();
            ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, true); Frame();
            ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, false); Frame(); Frame();
            check(activated == 1 && childScroll > 0, "Long live page cannot reveal and activate Explore through native keyboard focus.");
        }
        using (var host = new HeadlessUi(new Vector2(1200, 800), materials: true))
        {
            var shell = new BookShell(new MissingAssets(), PassportPreview.Sample) { ReducedMotion = true };
            var context = new UiContext(1, 18);
            var size = BookShellLayout.Fit(1040, new Vector2(1168, 768), context);
            for (var frame = 0; frame < 4; frame++) host.BookFrame(size, () =>
            {
                shell.Draw(new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize()), context);
                check(ImGui.GetScrollMaxX() == 0 && ImGui.GetScrollMaxY() == 0, "Unavailable materials break the book host.");
            });
            if (artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "passport-shell-fallback.png"), host);
        }
    }

    private sealed class MissingAssets : IBookAssets
    {
        public bool TryGet(BookAsset asset, out BookImage image, float drawWidth = 0f) { image = default; return false; }
    }
}
