using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

internal static class GridaniaChecks
{
    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        foreach (var scenario in new[] {
            (Target: "cover", Single: false, Info: false, TextScale: 1f, Long: false),
            (Target: "cover-plum", Single: false, Info: false, TextScale: 1f, Long: false),
            (Target: "nation", Single: false, Info: false, TextScale: 1f, Long: false),
            (Target: "identity", Single: false, Info: false, TextScale: 1f, Long: false),
            (Target: "collection", Single: false, Info: false, TextScale: 1f, Long: false),
            (Target: "identity", Single: true, Info: false, TextScale: 1f, Long: false),
            (Target: "identity", Single: true, Info: true, TextScale: 1f, Long: false),
            (Target: "identity", Single: true, Info: true, TextScale: 1.75f, Long: true) })
        {
            var cover = scenario.Target.StartsWith("cover", StringComparison.Ordinal);
            var width = cover ? 560 : scenario.Single ? 420 : 1160;
            var height = cover ? 840 : MathF.Ceiling(BookSurface.NaturalHeight(width, !scenario.Single) * 0.925f);
            var size = new Vector2(width, height);
            using var host = new HeadlessUi(size + new Vector2(32), textScale: scenario.TextScale, materials: true, signature: true);
            foreach (var studyName in new[] { "cover", "cover-plum-cutout", "crest", "banner", "map-overhead" }) host.Textures!.LoadStudy(studyName);
            if (cover)
            {
                var image = host.Textures!.Study(scenario.Target == "cover" ? "cover" : "cover-plum-cutout");
                var texture = host.Textures.Get(image.Texture.Handle);
                var topCenter = ((20 * texture.Width) + texture.Width / 2) * 4 + 3;
                check(texture.Pixels[3] == 0 && texture.Pixels[topCenter] == 0 && texture.Pixels[^1] == 0, "Cover artwork has a rectangular opaque canvas outside the book silhouette.");
            }
            GridaniaTargets.Reading reading = default;
            void Draw()
            {
                var context = new UiContext(1, ImGui.GetFontSize()) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont };
                var font = ImGui.GetFont();
                if (cover)
                {
                    var image = host.Textures!.Study("banner");
                    var region = new UiRect(new Vector2(-2000), new Vector2(150, 200));
                    var fitted = BookSurface.Illustration(region, image);
                    check(fitted.Position.X >= region.Position.X && fitted.Position.Y >= region.Position.Y && fitted.Max.X <= region.Max.X && fitted.Max.Y <= region.Max.Y, "Illustration escapes its reserved bounds.");
                    check(MathF.Abs(fitted.Size.X / fitted.Size.Y - image.Width / (float)image.Height) < 0.001f, "Heraldic artwork is stretched.");
                }
                reading = GridaniaTargets.Draw(scenario.Target, new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize()), host.Textures!, context, host.SignatureFont, scenario.Single, scenario.Info, scenario.Long);
                check(ImGui.GetScrollMaxX() == 0 && ImGui.GetScrollMaxY() == 0, "Gridania target scrolls the physical book host.");
                check(ImGui.GetFont() == font, "Gridania typography leaks font selection.");
            }
            for (var frame = 0; frame < 4; frame++) host.BookFrame(size, Draw, studyBackdrop: true);
            if (!cover && !scenario.Single)
                check(MathF.Max(reading.LeftHeight, reading.RightHeight) <= reading.PageHeight, "Default Gridania composition requires scrolling.");
            if (scenario.Long)
                check(reading.Scroll > 0, "Long/enlarged identity cannot scroll inside its page.");
            var name = "passport-gridania-" + scenario.Target + (scenario.Single ? scenario.Info ? "-information" : "-portrait" : "") + (scenario.Long ? "-long" : "");
            if (artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, name + ".png"), host);
            Console.WriteLine("PASS " + name);
        }
    }
}
