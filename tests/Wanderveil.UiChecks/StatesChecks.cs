using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Discover;
using Wanderveil.Features.Echoes;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Studio;
using Wanderveil.UI;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

// Every section of the real shell in the states content can be in: empty, missing artwork, long text, and
// Japanese text. Each must keep the host still, wrap inside its paper, and say plainly what is missing.
internal static class StatesChecks
{
    private const string LongWord = "Donaudampfschifffahrtsgesellschaftskapitänswitwe";

    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        var empty = new BookShellContent([], null, StudioTemplate.Catalog.Where(template => !template.Available).ToArray());
        Visit(check, artifacts, "empty", new Vector2(1200, 800), PassportPreview.Sample, empty, missingArt: false, japanese: false, shell =>
        {
            check(!shell.Studio.Choose(StudioTemplateKind.Postcard) && shell.Studio.Draft == null, "An empty Studio catalog still starts a draft.");
        });

        Visit(check, artifacts, "missing-art", new Vector2(1200, 800), PassportPreview.Sample, BookShellContent.Sample, missingArt: true, japanese: false);
        MissingArtMotion(check, artifacts);

        var longProfile = new PassportPreview(
            "Ari of the " + LongWord + " and the Many Winding Roads",
            "An unusually long sample home-world label for " + LongWord,
            "Bard / Botanist / " + LongWord,
            string.Concat(Enumerable.Repeat("Ein langer Weg über stille Pfade, alte Karten und den Blick hinter den Horizont. ", 6)),
            LongWord + LongWord);
        var longDiscovery = DiscoveryPreview.Postcard with
        {
            Title = "Der " + LongWord + " Aussichtspunkt am Ende des langen Weges",
            Creator = LongWord + " / Demo creator",
            Location = "Ein erdachter Aussichtspunkt " + LongWord,
            Teaser = string.Concat(Enumerable.Repeat("Ein stiller Pfad und eine Brise vom Meer. ", 4)),
            Caption = string.Concat(Enumerable.Repeat(LongWord + " am Abend über dem Meer. ", 8)),
            StampName = LongWord + " / Demo stamp",
        };
        var longDossier = EchoPreview.Sample with
        {
            Title = "Der Brief des " + LongWord,
            Byline = LongWord + " / Wanderveil demo",
            Premise = string.Concat(Enumerable.Repeat("Ein Brief in einem Gasthaus im Wald bittet den Finder, die Laternen ein letztes Mal zu entzünden. ", 3)),
            Chapters = [EchoPreview.Sample.Chapters[0] with { Title = LongWord, Objectives = [new(LongWord + " " + LongWord, LongWord)] }, .. EchoPreview.Sample.Chapters.Skip(1)],
        };
        var longContent = new BookShellContent([longDiscovery, DiscoveryPreview.StampSpot], longDossier, StudioTemplate.Catalog);
        foreach (var (name, size) in new[] { ("long", new Vector2(1200, 800)), ("long-narrow", new Vector2(452, 760)) })
            Visit(check, artifacts, name, size, longProfile, longContent, missingArt: false, japanese: false, shell =>
            {
                shell.Studio.Choose(StudioTemplateKind.Postcard);
                foreach (var field in StudioTemplate.Postcard.Fields)
                    shell.Studio.Draft!.Set(field.Id, string.Concat(Enumerable.Repeat(LongWord + " ", 10)));
                check(shell.Studio.Draft!.Template.Fields.All(field => StudioDraft.Length(shell.Studio.Draft[field.Id]) == field.MaxLength), "Long Studio text is not held at each field's limit.");
            });

        var japaneseProfile = new PassportPreview("アリ・ヴェイル", "デモワールド", "吟遊詩人 / 園芸師",
            "小さな物語と静かな小道、そして遠回りする価値のある景色を集める冒険者。", "寄り道をしよう。物語はいつもその先で待っている。");
        var japaneseDiscovery = DiscoveryPreview.Postcard with
        {
            Title = "遠回りの道",
            Creator = "アリ・ヴェイル / デモ作成者",
            Location = "想像上の海辺の展望台",
            Teaser = "静かな小道と潮風、そして寄り道する価値のある水平線。",
            Caption = "長い道を選んで帰ったら、ひと休みできる場所を見つけた。小道の下では、海がその日最後の光を抱いていた。",
            StampName = "遠回りの道 / デモスタンプ",
        };
        var japaneseDossier = EchoPreview.Sample with
        {
            Title = "灯守りの手紙",
            Byline = "三章からなるサンプルの謎 / Wanderveil デモ",
            Premise = "森の宿に残された手紙が、見つけた人にもう一度だけ灯りをともしてほしいと頼んでいる。",
            Chapters = [EchoPreview.Sample.Chapters[0] with { Title = "手紙", Summary = "次の旅人へのメモが残されていた。", Objectives = [new("宿に残された手紙を読む。"), new("夕空を向いたベンチを探す。", "灯りが途切れる場所を見て。")] }, .. EchoPreview.Sample.Chapters.Skip(1)],
        };
        Visit(check, artifacts, "japanese", new Vector2(1200, 800), japaneseProfile, new BookShellContent([japaneseDiscovery], japaneseDossier, StudioTemplate.Catalog), missingArt: false, japanese: true, shell =>
        {
            shell.Studio.Choose(StudioTemplateKind.Postcard);
            shell.Studio.Draft!.Set(StudioTemplate.Title, "月夜の桟橋");
            shell.Studio.Draft.Set(StudioTemplate.Caption, "水面に灯籠が揺れていた。");
            check(StudioDraft.Length(shell.Studio.Draft[StudioTemplate.Title]) == 5, "Japanese text is not counted by character.");
        });
    }

    private static void Visit(Action<bool, string> check, string? artifacts, string name, Vector2 room, PassportPreview profile, BookShellContent content, bool missingArt, bool japanese, Action<BookShell>? studio = null)
    {
        using var host = new HeadlessUi(room, 1f, 1f, materials: true, japanese: japanese);
        IBookAssets assets = missingArt ? new MissingAssets() : host.Textures!;
        var shell = new BookShell(assets, profile, content: content) { ReducedMotion = true };
        var context = new UiContext(1, 18) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont, SignatureFont = host.SignatureFont };
        UiRect root = default;
        var glyphsChecked = false;
        void Frame() => host.BookFrame(shell.FitSize(room - new Vector2(32), context), () =>
        {
            root = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize());
            shell.Draw(root, context);
            check(ImGui.GetScrollMaxX() == 0 && ImGui.GetScrollMaxY() == 0, name + ": the book host scrolls.");
            check(shell.PageOverflowX == 0, name + ": a page overflows sideways in " + shell.Section + ".");
            if (japanese && !glyphsChecked)
            {
                // Decorative faces lack Japanese; text falls back to a host font that can draw every character.
                foreach (var text in new[] { profile.Name, profile.Bio, "灯守りの手紙" })
                    foreach (var role in new[] { TextRole.Title, TextRole.Body, TextRole.Caption })
                    {
                        var font = UiTypography.ResolveFont(text, role, context);
                        check(text.All(character => char.IsWhiteSpace(character) || HasGlyph(font, character)), name + ": " + role + " text has no glyph for part of '" + text + "'.");
                    }
                glyphsChecked = true;
            }
        });
        void Frames(int count) { for (var index = 0; index < count; index++) Frame(); }
        void Click(Vector2 position)
        {
            ImGui.GetIO().AddMousePosEvent(position.X, position.Y); Frame();
            ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
            ImGui.GetIO().AddMouseButtonEvent(0, false); Frames(2);
        }
        void Save(string section)
        {
            if (artifacts == null) return;
            ImGui.GetIO().AddMousePosEvent(-100, -100); Frames(2);
            SoftwarePreview.Save(Path.Combine(artifacts, "states-" + name + "-" + section + ".png"), host);
        }

        Frames(3);
        Save("cover");
        Click(BookShellLayout.OpenPoint(root));
        Frames(2);
        check(shell.IsBookOpen, name + ": the cover does not open.");
        // Every passport spread, then every section.
        var layout = BookShellLayout.Create(root, context, 0);
        for (var spread = 0; spread < shell.PageCount; spread += layout.IsSpread ? 2 : 1)
        {
            if (spread == 2) Save("identity");
            var before = shell.VisiblePage;
            Click(layout.TurnPoint(true, shell.VisiblePage));
            Frames(2);
            if (shell.VisiblePage == before) break;
        }
        foreach (var (section, tab) in new[] { (BookSection.Discover, 1), (BookSection.Echoes, 2), (BookSection.Studio, 3) })
        {
            Click(layout.Bookmark(tab).Position + layout.Bookmark(tab).Size / 2);
            Frames(2);
            check(shell.Section == section, name + ": cannot reach " + section + ".");
            check(ImGui.GetDrawData().TotalVtxCount > 400, name + ": " + section + " draws nothing.");
            Save(section.ToString().ToLowerInvariant());
            if (section == BookSection.Studio && studio != null)
            {
                studio(shell);
                Frames(3);
                Save("studio-draft");
            }
        }
        Console.WriteLine("PASS states-" + name);
    }

    // Pages still turn on plain paper, and the cover still swings, when no plate has loaded.
    private static void MissingArtMotion(Action<bool, string> check, string? artifacts)
    {
        var room = new Vector2(1200, 800);
        using var host = new HeadlessUi(room, materials: true);
        var shell = new BookShell(new MissingAssets(), PassportPreview.Sample);
        var context = new UiContext(1, 18) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont, SignatureFont = host.SignatureFont };
        UiRect root = default;
        void Frame()
        {
            shell.Advance(1f / 60f);
            host.BookFrame(shell.FitSize(room - new Vector2(32), context), () =>
            {
                root = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize());
                shell.Draw(root, context);
            });
        }
        for (var frame = 0; frame < 60 && shell.Phase != BookPhase.Closed; frame++) Frame();
        var open = BookShellLayout.OpenPoint(root);
        ImGui.GetIO().AddMousePosEvent(open.X, open.Y); Frame();
        ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
        ImGui.GetIO().AddMouseButtonEvent(0, false);
        for (var frame = 0; frame < 12; frame++) Frame();
        if (artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "states-missing-art-opening.png"), host);
        for (var frame = 0; frame < 90 && shell.Phase != BookPhase.Open; frame++) Frame();
        check(shell.Phase == BookPhase.Open, "Without artwork the cover does not finish opening.");
        var layout = BookShellLayout.Create(root, context, 0);
        var turn = layout.TurnPoint(true, 0);
        ImGui.GetIO().AddMousePosEvent(turn.X, turn.Y); Frame();
        ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
        ImGui.GetIO().AddMouseButtonEvent(0, false);
        for (var frame = 0; frame < 14; frame++) Frame();
        check(shell.IsTurning, "Without artwork the page does not turn.");
        if (artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "states-missing-art-turn.png"), host);
        for (var frame = 0; frame < 60 && shell.IsTurning; frame++) Frame();
        check(!shell.IsTurning && shell.VisiblePage == 2, "Without artwork the page turn does not land.");
        Console.WriteLine("PASS states-missing-art-motion");
    }

    private static unsafe bool HasGlyph(ImFontPtr font, char character) => font.FindGlyphNoFallback(character) != null;

    private sealed class MissingAssets : IBookAssets
    {
        public bool TryGet(BookAsset asset, out BookImage image, float drawWidth = 0f) { image = default; return false; }
    }
}
