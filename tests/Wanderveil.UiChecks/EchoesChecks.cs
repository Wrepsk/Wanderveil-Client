using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Echoes;
using Wanderveil.Features.Passport;
using Wanderveil.UI;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

internal static class EchoesChecks
{
    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        var sample = EchoPreview.Validate(EchoPreview.Sample);
        check(!sample.Chapters[0].Sealed && sample.Chapters[0].Objectives.Count > 0, "The sample dossier does not open on a readable first chapter.");
        check(sample.Chapters.Skip(1).All(chapter => chapter.Sealed && chapter.Objectives.Count == 0), "Later sample chapters reveal their objectives.");
        foreach (var invalid in new[]
        {
            sample with { Chapters = [] },
            sample with { Title = " " },
            sample with { Chapters = [sample.Chapters[1]] },
            sample with { Chapters = [sample.Chapters[0], sample.Chapters[1] with { Objectives = sample.Chapters[0].Objectives }] },
        })
        {
            try { EchoPreview.Validate(invalid); check(false, "An invalid dossier was accepted."); }
            catch (ArgumentException) { check(true, "Invalid dossier refused."); }
        }

        foreach (var scenario in new[] {
            (Name: "wide", Size: new Vector2(1200, 800), Text: 1f),
            (Name: "narrow", Size: new Vector2(452, 760), Text: 1f),
            (Name: "large-text", Size: new Vector2(1000, 900), Text: 1.75f) })
            Shell(check, artifacts, scenario.Name, scenario.Size, scenario.Text);

        // Without a dossier the pages still explain Echoes and say nothing was fetched.
        using (var host = new HeadlessUi(new Vector2(420, 600)))
        {
            var view = new EchoesView(null);
            var drawn = 0f;
            void Draw()
            {
                var context = new UiContext(1, ImGui.GetFontSize());
                view.DrawLeft(context);
                var left = ImGui.GetCursorPosY();
                view.DrawRight(context);
                drawn = ImGui.GetCursorPosY();
                check(drawn - left > 2 * ImGui.GetFontSize(), "The empty Echoes chapters page is blank.");
                check(ImGui.GetScrollMaxX() == 0, "The empty Echoes pages overflow sideways.");
            }
            host.Frame(Draw);
            host.Frame(Draw);
            check(drawn > 100 && ImGui.GetDrawData().TotalVtxCount > 100, "The empty Echoes pages draw nothing.");
            if (artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "echoes-empty.bmp"), host);
        }
    }

    private static void Shell(Action<bool, string> check, string? artifacts, string name, Vector2 room, float text)
    {
        using var host = new HeadlessUi(room, 1f, text, materials: true);
        var shell = new BookShell(host.Textures!, PassportPreview.Sample) { ReducedMotion = true };
        var context = new UiContext(1, 18 * text) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont, SignatureFont = host.SignatureFont };
        UiRect root = default;
        void Frame() => host.BookFrame(shell.FitSize(room - new Vector2(32), context), () =>
        {
            root = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize());
            shell.Draw(root, context);
            check(ImGui.GetScrollMaxX() == 0 && ImGui.GetScrollMaxY() == 0, "Echoes makes the book host scroll.");
        });
        void Frames(int count) { for (var index = 0; index < count; index++) Frame(); }
        void Click(Vector2 position)
        {
            ImGui.GetIO().AddMousePosEvent(position.X, position.Y); Frame();
            ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
            ImGui.GetIO().AddMouseButtonEvent(0, false); Frames(2);
        }

        Frames(3);
        Click(BookShellLayout.OpenPoint(root));
        Frames(2);
        var layout = BookShellLayout.Create(root, context, 0);
        Click(layout.Bookmark(2).Position + layout.Bookmark(2).Size / 2);
        Frames(2);
        check(shell.Section == BookSection.Echoes && !shell.IsRiffling, name + ": the Echoes tab does not open Echoes.");
        // A single page holds both sides and may scroll; a spread fits its paper.
        if (text <= 1f && layout.IsSpread) check(shell.PageOverflow == 0, name + ": the Echoes pages overflow their paper.");
        if (artifacts != null)
        {
            ImGui.GetIO().AddMousePosEvent(-100, -100); Frame();
            SoftwarePreview.Save(Path.Combine(artifacts, "echoes-" + name + ".png"), host);
        }

        // The dossier is printed matter: clicking anywhere on its pages starts, ticks, or turns nothing.
        foreach (var page in layout.IsSpread ? new[] { layout.LeftPage, layout.RightPage } : [layout.LeftPage])
            for (var y = 30f; y < page.Size.Y; y += 60)
                Click(page.Position + new Vector2(page.Size.X * 0.3f, y));
        check(shell.Section == BookSection.Echoes && shell.IsBookOpen && !shell.IsTurning && !shell.IsRiffling && shell.VisiblePage == 0, name + ": clicking the Echoes pages acted on something.");
        Console.WriteLine("PASS echoes-" + name);
    }
}
