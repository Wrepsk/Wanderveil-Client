using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.UI;
using Wanderveil.UI.Components;

namespace Wanderveil.UiChecks;

// Take a picture: the editor's link, the book stepping aside (still open), the guide and its panel, the countdown,
// Escape, and the book coming back as it was. The capture itself needs the game; a stand-in records where it was asked.
internal static class CameraChecks
{
    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        foreach (var motion in new[] { false, true })
        {
            var profile = new PassportProfile(PassportIdentity.Sample);
            FakeCamera? camera = null;
            using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: profile, motion: motion,
                portrait: textures => camera = new FakeCamera(profile, textures.Study("crest")));
            book.Open();
            book.Key(ImGuiKey.PageDown);
            for (var frame = 0; frame < 120 && book.Shell.IsTurning; frame++) book.Frame();
            book.Click(book.StampControl());
            book.Frames(2);
            check(book.Shell.EditingIdentity && book.Shell.Editor.PictureLinkCentres.ContainsKey("Take a picture"), "Editing the identity pages does not offer Take a picture.");
            var page = book.Shell.VisiblePage;
            book.Click(book.Shell.Editor.PictureLinkCentres["Take a picture"]);
            for (var frame = 0; frame < 120 && book.Shell.Phase != BookPhase.Hidden; frame++) book.Frame();
            check(book.Shell.CameraOpen && book.Shell.Phase == BookPhase.Hidden, "Take a picture does not put the book aside.");
            book.Frames(2);
            var guide = book.Shell.Camera.Guide;
            check(MathF.Abs(guide.Size.X / guide.Size.Y - PassportDocumentView.PortraitAspect) < 0.01f && book.Shell.Camera.Controls.ContainsKey("Take"), "The camera's guide is not a 3:4 frame with its panel.");
            if (!motion) book.Save(artifacts, "camera-guide.bmp");

            // The guide's edges move it and its corners resize it, staying 3:4; only they and the panel take the mouse.
            var camera0 = book.Shell.Camera;
            check(camera0.TakesMouse(camera0.Controls["Guide edge"]) && !camera0.TakesMouse(guide.Position + guide.Size / 2) && !camera0.TakesMouse(guide.Position - new Vector2(60)) && camera0.TakesMouse(camera0.Controls["Take"]),
                "The guide's window takes the mouse inside the guide or around it, or not on its edges and panel.");
            book.Drag(camera0.Controls["Guide edge"], camera0.Controls["Guide edge"] + new Vector2(-150, 60));
            var moved = camera0.Guide;
            check(MathF.Abs(moved.Position.X - (guide.Position.X - 150)) < 3 && MathF.Abs(moved.Position.Y - (guide.Position.Y + 60)) < 3 && moved.Size == guide.Size, $"Dragging the guide's edge does not move it ({guide.Position} to {moved.Position}).");
            // Its bottom right corner, dragged in, makes it smaller from its top left.
            book.Drag(camera0.Controls["Guide corner 3"], camera0.Controls["Guide corner 3"] - new Vector2(90, 120));
            var resized = camera0.Guide;
            check(resized.Size.Y < moved.Size.Y - 100 && MathF.Abs(resized.Size.X / resized.Size.Y - PassportDocumentView.PortraitAspect) < 0.01f && Vector2.Distance(resized.Position, moved.Position) < 2, $"Dragging the guide's corner does not resize it at 3:4 from the opposite corner ({resized}).");
            check(camera0.Panel.Position.Y > resized.Max.Y, "The panel does not sit under the guide.");
            // Dragged to the screen's foot, the panel goes above it.
            book.Drag(camera0.Controls["Guide edge"], camera0.Controls["Guide edge"] + new Vector2(0, 600));
            check(camera0.Panel.Max.Y < camera0.Guide.Position.Y, "With the guide at the screen's foot, the panel does not go above it.");
            if (!motion) book.Save(artifacts, "camera-guide-moved.bmp");
            guide = camera0.Guide;

            // A countdown: Take starts it; Escape cancels it, taking nothing; the book comes back still editing.
            book.Click(book.Shell.Camera.Controls["Countdown 2"]);
            book.Click(book.Shell.Camera.Controls["Take"]);
            check(book.Shell.Camera.Remaining is > 4f and <= 5f && camera!.Taken == null, "Take with a countdown takes at once, or does not count.");
            if (!motion) book.Save(artifacts, "camera-countdown.bmp");
            book.Key(ImGuiKey.Escape);
            for (var frame = 0; frame < 120 && (book.Shell.Phase != BookPhase.Open || book.Shell.CameraOpen); frame++) book.Frame();
            check(camera!.Taken == null && !book.Shell.CameraOpen && book.Shell.Phase == BookPhase.Open && book.Shell.EditingIdentity && book.Shell.VisiblePage == page,
                "Escape on the camera takes a picture, or the book does not come back open, editing, on its page.");

            // No countdown: Take captures what is inside the guide, and the book comes back.
            book.Click(book.Shell.Editor.PictureLinkCentres["Take a picture"]);
            for (var frame = 0; frame < 120 && book.Shell.Phase != BookPhase.Hidden; frame++) book.Frame();
            book.Frames(2);
            book.Click(book.Shell.Camera.Controls["Countdown 0"]);
            book.Click(book.Shell.Camera.Controls["Take"]);
            for (var frame = 0; frame < 120 && (book.Shell.Phase != BookPhase.Open || book.Shell.CameraOpen); frame++) book.Frame();
            var now = book.Shell.Camera.Guide;
            check(camera.Taken is { } taken && Vector2.Distance(taken.Min, now.Position) < 1 && Vector2.Distance(taken.Max, now.Max) < 1 && Vector2.Distance(now.Position, guide.Position) < 1 && book.Shell.EditingIdentity && profile.Identity.Portrait != null,
                $"Take does not capture the guide's area, or the book does not come back ({camera.Taken}).");
            // The picture's frame choices make the page taller; it settles to fit its paper again.
            book.Frames(12);
            check(book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, $"The identity page does not fit after a picture is taken ({book.Shell.PageOverflow}).");
            if (!motion) book.Save(artifacts, "camera-taken.bmp");
        }

        // Without a camera (a host that cannot capture), the link is not offered.
        var plain = new PassportProfile(PassportIdentity.Sample);
        using (var none = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: plain, portrait: textures => new FakeCamera(plain, textures.Study("crest"), canTake: false)))
        {
            none.Open();
            none.Key(ImGuiKey.PageDown);
            none.Click(none.StampControl());
            none.Frames(2);
            check(none.Shell.EditingIdentity && !none.Shell.Editor.PictureLinkCentres.ContainsKey("Take a picture"), "Take a picture is offered without a camera.");
        }
        Console.WriteLine("PASS camera");
    }

    private sealed class FakeCamera(PassportProfile profile, BookImage image, bool canTake = true) : IPassportPortrait
    {
        internal (Vector2 Min, Vector2 Max)? Taken { get; private set; }
        public PortraitState State => profile.Identity.Portrait != null ? PortraitState.Shown : PortraitState.None;
        public string? Problem => null;
        public bool CanTake => canTake;

        public bool TryGet(out BookImage shown)
        {
            shown = image;
            return profile.Identity.Portrait != null;
        }

        public void Choose() { }

        public void Remove() => profile.Update(profile.Identity with { Portrait = null });

        // As if the capture arrived at once.
        public void Take(Vector2 min, Vector2 max)
        {
            Taken = (min, max);
            profile.Update(profile.Identity with { Portrait = PortraitCrop.Centered });
        }
    }
}
