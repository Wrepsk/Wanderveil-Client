using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Wanderveil.Features;
using Wanderveil.Features.Drawing;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.Infrastructure.Media;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

internal sealed class MainWindow : Window
{
    private readonly Func<bool> reducedMotion;
    private readonly PassportAssets assets;
    private readonly BookShell shell;
    // Where the book rests: the screen centre on the first summon, then wherever it was dragged or
    // dismissed, remembered for the rest of this plugin session. The slide offsets from it.
    private Vector2? rest;
    private Vector2 lastSize;
    // The host's settings button can arrive while the window is closed; the book opens to Settings once shown.
    private bool settingsRequested;

    internal MainWindow(Func<bool> reducedMotion, IBookShellSettings settings, PassportAssets assets, StampCollection stamps, ISavedState storage, PassportServices passport, PageMarks? marks = null, ISavedState? marksStorage = null) : base("Wanderveil###Wanderveil.Main", BookShell.WindowFlags)
    {
        this.reducedMotion = reducedMotion;
        this.assets = assets;
        shell = new BookShell(assets, PassportIdentity.Sample, settings: settings, stamps: stamps, storage: storage, identity: passport.Profile, portrait: passport.Portrait, character: passport.Character, identityStorage: passport.Storage, marks: marks, marksStorage: marksStorage) { ReadsEscape = false };
        SizeCondition = ImGuiCond.Always;
        // The shell handles Escape so it plays the dismissal sequence instead of hiding at once.
        RespectCloseHotkey = false;
        ForceMainWindow = true;
    }

    // Whether the book has a use for Escape now; the plugin then takes the key from the game and hands it over.
    internal bool WantsEscape => IsOpen && shell.WantsEscape;

    internal void RequestEscape() => shell.RequestEscape();

    // The command and main-UI button: summon (or reverse a dismissal in progress), else dismiss.
    internal void ToggleAnimated()
    {
        if (!IsOpen) IsOpen = true;
        else if (shell.Phase == BookPhase.Exiting) shell.Summon();
        else shell.RequestDismiss();
    }

    internal void Summon()
    {
        if (!IsOpen) IsOpen = true;
        else shell.Summon();
    }

    // The host's settings button and /wanderveil settings: the passport opens onto its Settings pages.
    internal void OpenSettings()
    {
        if (!IsOpen) { settingsRequested = true; IsOpen = true; return; }
        shell.Summon();
        shell.ShowSettings();
    }

    public override void PreDraw()
    {
        shell.ReducedMotion = reducedMotion();
        shell.Advance(ImGui.GetIO().DeltaTime);
        // Off the book itself the window lets the mouse through, so clicks around the passport reach the game.
        Flags = shell.HostFlags(ImGui.GetIO().MousePos);
        var context = new UiContext(ShellStyle.Scale, ImGui.GetFontSize());
        var viewport = ImGui.GetMainViewport();
        var room = Vector2.Max(Vector2.One, viewport.WorkSize - new Vector2(16 * context.Scale));
        var size = shell.FitSize(room, context);
        // WindowSystem applies host UI scaling to Size; layout above uses actual pixels.
        Size = size / context.Scale;
        rest = BookShellPlacement.Rest(rest, lastSize, size, viewport.WorkPos, viewport.WorkSize);
        Position = BookShellPlacement.Slide(rest.Value, shell.Lift, viewport.WorkPos, viewport.WorkSize, 24 * context.Scale);
        PositionCondition = ImGuiCond.Always;
        lastSize = size;
        BookShell.PrepareWindow(size);
    }

    public override void PostDraw() => BookShell.PopWindowStyle();

    // A recalled passport returns to where it was dismissed; only the session's first summon centres it.
    public override void OnOpen()
    {
        shell.BeginSession();
        if (settingsRequested) shell.ShowSettings();
        settingsRequested = false;
    }

    public override void Draw()
    {
        using var fonts = assets.LockFonts();
        var context = new UiContext(ShellStyle.Scale, ImGui.GetFontSize()) { TitleFont = fonts.Title, HeadingFont = fonts.Heading, BodyFont = fonts.Body, DisplayFont = fonts.Display, MonoFont = fonts.Mono, SignatureFont = fonts.Signature, Fonts = fonts };
        var intent = shell.Draw(new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize()), context);
        if (intent.Drag != Vector2.Zero && rest is { } position) rest = position + intent.Drag;
        if (intent.Close) IsOpen = false;
    }
}
