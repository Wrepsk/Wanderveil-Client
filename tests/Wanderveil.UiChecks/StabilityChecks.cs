using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Studio;
using Wanderveil.UI;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

// A page whose content sits right at its paper's height must settle: a scrollbar that appears must not narrow
// the content enough to make it fit again (and vanish, and overflow again) every frame. The user saw this as
// a twitching Studio preview at one passport size once a place note was entered.
internal static class StabilityChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var unstable = new List<string>();
        for (var width = 640f; width <= 1600f; width += 6f)
        {
            var room = new Vector2(width, width * 0.72f);
            using var host = new HeadlessUi(room, materials: true);
            var shell = new BookShell(host.Textures!, PassportPreview.Sample) { ReducedMotion = true };
            var context = new UiContext(1, 18) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont, SignatureFont = host.SignatureFont };
            UiRect root = default;
            var overflow = new List<float>();
            void Frame() => host.BookFrame(shell.FitSize(room - new Vector2(32), context), () =>
            {
                root = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize());
                shell.Draw(root, context);
                overflow.Add(shell.PageOverflow);
            });
            void Click(Vector2 point)
            {
                ImGui.GetIO().AddMousePosEvent(point.X, point.Y); Frame();
                ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
                ImGui.GetIO().AddMouseButtonEvent(0, false); Frame(); Frame();
            }
            Frame(); Frame();
            Click(BookShellLayout.OpenPoint(root));
            var layout = BookShellLayout.Create(root, context, 0);
            Click(layout.Bookmark(3).Position + layout.Bookmark(3).Size / 2);
            shell.Studio.Choose(StudioTemplateKind.Postcard);
            shell.Studio.Draft!.Set(StudioTemplate.Title, "Moonlit pier");
            shell.Studio.Draft.Set(StudioTemplate.Caption, "Lanterns on the water");
            shell.Studio.Draft.Set(StudioTemplate.Place, "An imagined harbour at dusk");
            shell.Studio.Draft.Set(StudioTemplate.Stamp, "Moonlit pier");
            Settle("postcard");
            shell.Studio.Choose(StudioTemplateKind.StampSpot);
            shell.Studio.Draft!.Set(StudioTemplate.Title, "Where the lanterns wait");
            shell.Studio.Draft.Set(StudioTemplate.Place, "A bend in the woodland path");
            shell.Studio.Draft.Set(StudioTemplate.Stamp, "Lantern path");
            shell.Studio.Draft.Set(StudioTemplate.Caption, "Stand still a moment and look up.");
            Settle("stamp spot");
            Click(layout.Bookmark(2).Position + layout.Bookmark(2).Size / 2);
            Settle("echoes");
            Click(layout.Bookmark(1).Position + layout.Bookmark(1).Size / 2);
            Settle("discover");

            void Settle(string what)
            {
                ImGui.GetIO().AddMousePosEvent(-100, -100);
                for (var frame = 0; frame < 6; frame++) Frame();
                overflow.Clear();
                for (var frame = 0; frame < 6; frame++) Frame();
                if (overflow.Distinct().Count() > 1) unstable.Add($"{what} at {width}: {string.Join(", ", overflow)}");
            }
        }
        check(unstable.Count == 0, "Pages never settle at some sizes (section at room width: overflow per frame): " + string.Join(" | ", unstable.Take(4)));
        Console.WriteLine("PASS layout-stability");
    }
}
