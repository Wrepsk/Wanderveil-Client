using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Studio;
using Wanderveil.UI;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

// Who gets the mouse and keyboard: the book takes them only where it is drawn, keeps them for a press,
// drag, or edit that began on it, and otherwise lets clicks through its transparent window to the game.
// WantCaptureMouse/WantTextInput are what the host reads to decide whether the game sees input.
internal static class InputChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var room = new Vector2(1200, 800);
        using var host = new HeadlessUi(room, materials: true);
        var shell = new BookShell(host.Textures!, PassportPreview.Sample) { ReducedMotion = true };
        var context = new UiContext(1, 18) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont, SignatureFont = host.SignatureFont };
        UiRect root = default;
        BookShellIntent intent = default;
        // As the plugin window does each frame: flags from where the mouse is, then the book.
        void Frame() => host.BookFrame(shell.FitSize(room - new Vector2(32), context), () =>
        {
            root = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize());
            intent = shell.Draw(root, context);
        }, flags: shell.HostFlags(ImGui.GetIO().MousePos));
        void Frames(int count) { for (var index = 0; index < count; index++) Frame(); }
        void Move(Vector2 point) { ImGui.GetIO().AddMousePosEvent(point.X, point.Y); Frames(3); }
        void Click(Vector2 point)
        {
            Move(point);
            ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
            ImGui.GetIO().AddMouseButtonEvent(0, false); Frames(2);
        }
        bool Captured() => ImGui.GetIO().WantCaptureMouse;

        Frames(3);
        // Closed: the margin around the cover plate passes clicks through; the board takes them.
        var corner = root.Position + new Vector2(3);
        Move(corner);
        check(!shell.OwnsMouse(corner) && !Captured(), "The transparent margin around the closed cover takes the mouse.");
        Click(corner);
        check(!shell.IsBookOpen && intent.Drag == Vector2.Zero, "A click beside the closed cover acted on the book.");
        var open = BookShellLayout.OpenPoint(root);
        Move(open);
        check(shell.OwnsMouse(open) && Captured(), "The closed cover does not take the mouse.");
        Click(open);
        check(shell.IsBookOpen, "The cover no longer opens after pass-through.");

        // Open: beside the tabs and the board passes through; the tabs and pages take the mouse.
        Frames(2);
        var layout = BookShellLayout.Create(root, context, 0);
        var beside = new Vector2(root.Max.X - 3, root.Position.Y + 3);
        check(!BookSurface.Board(layout.Book, BookFace.Spread).Contains(beside), "The probe beside the tabs lies on the board.");
        Move(beside);
        check(!shell.OwnsMouse(beside) && !Captured(), "The margin beside the tabs takes the mouse.");
        var tab = layout.Bookmark(1).Position + layout.Bookmark(1).Size / 2;
        Move(tab);
        check(shell.OwnsMouse(tab) && Captured(), "A tab does not take the mouse.");
        var page = layout.TurnPoint(true, 0);
        Move(page);
        check(shell.OwnsMouse(page) && Captured(), "The page does not take the mouse.");

        // A drag that begins on the book keeps the mouse when it leaves the book, then lets go on release.
        ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
        ImGui.GetIO().AddMousePosEvent(page.X + 20, page.Y + 10); Frame();
        ImGui.GetIO().AddMousePosEvent(beside.X, beside.Y); Frame();
        check(shell.OwnsMouse(beside) && intent.Drag != Vector2.Zero, "A drag stops when the mouse leaves the book.");
        ImGui.GetIO().AddMouseButtonEvent(0, false); Frames(3);
        check(!shell.OwnsMouse(beside) && intent.Drag == Vector2.Zero, "The book keeps the mouse after a drag ends.");

        // Typing in a Studio field tells the host to keep keys from the game; leaving it hands them back.
        layout = BookShellLayout.Create(root, context, shell.VisiblePage);
        Click(layout.Bookmark(3).Position + layout.Bookmark(3).Size / 2);
        shell.Studio.Choose(StudioTemplateKind.Postcard);
        Move(layout.LeftPage.Position + layout.LeftPage.Size / 2);
        Frames(2);
        check(ImGui.GetIO().WantTextInput, "An active Studio field does not ask for text input.");
        ImGui.GetIO().AddInputCharacters("Pier");
        Frames(2);
        check(shell.Studio.Draft![StudioTemplate.Title] == "Pier", "Typing does not reach the active field.");
        // While writing, the field keeps the mouse too, so the first click off the book only leaves the field.
        Move(beside);
        check(shell.OwnsMouse(beside), "An active field lets a click through before it is left.");
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Escape, true); Frame();
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Escape, false); Frames(3);
        check(!ImGui.GetIO().WantTextInput && shell.IsBookOpen && !shell.OwnsMouse(beside), "Leaving the field does not hand input back.");
        Console.WriteLine("PASS input-ownership");
    }
}
