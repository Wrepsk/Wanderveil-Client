using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Wanderveil.Features.Passport;
using Wanderveil.Infrastructure.Media;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

internal sealed class MainWindow : Window
{
    private readonly Action openSettings;
    private readonly Func<bool> reducedMotion;
    private readonly PassportAssets assets;
    private readonly BookShell shell;
    // Where the book rests (bottom centre on summon, or where it was dragged); the slide offsets from it.
    private Vector2? rest;
    private Vector2 lastSize;

    internal MainWindow(Action openSettings, Func<bool> reducedMotion, PassportAssets assets) : base("Wanderveil###Wanderveil.Main", BookShell.WindowFlags)
    {
        this.openSettings = openSettings;
        this.reducedMotion = reducedMotion;
        this.assets = assets;
        shell = new BookShell(assets, PassportPreview.Sample);
        SizeCondition = ImGuiCond.Always;
        // Escape is handled in Draw so it plays the dismissal sequence instead of hiding at once.
        RespectCloseHotkey = false;
        ForceMainWindow = true;
    }

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

    public override void PreDraw()
    {
        shell.ReducedMotion = reducedMotion();
        shell.Advance(ImGui.GetIO().DeltaTime);
        var context = new UiContext(ShellStyle.Scale, ImGui.GetFontSize());
        var viewport = ImGui.GetMainViewport();
        var room = Vector2.Max(Vector2.One, viewport.WorkSize - new Vector2(16 * context.Scale));
        var size = shell.FitSize(room, context);
        // WindowSystem applies host UI scaling to Size; layout above uses actual pixels.
        Size = size / context.Scale;
        var lowest = viewport.WorkPos + Vector2.Max(Vector2.Zero, viewport.WorkSize - size);
        if (rest is { } position)
        {
            // Keep the bottom centre anchored when the book changes between closed and open sizes.
            position += new Vector2((lastSize.X - size.X) / 2, lastSize.Y - size.Y);
            rest = Vector2.Clamp(position, viewport.WorkPos, lowest);
        }
        else rest = viewport.WorkPos + new Vector2((viewport.WorkSize.X - size.X) / 2, MathF.Max(0, viewport.WorkSize.Y - size.Y - 8 * context.Scale));
        // Below the screen at lift 0, at rest at lift 1.
        var travel = viewport.WorkPos.Y + viewport.WorkSize.Y + 24 * context.Scale - rest.Value.Y;
        Position = rest.Value + new Vector2(0, (1 - shell.Lift) * travel);
        PositionCondition = ImGuiCond.Always;
        lastSize = size;
        BookShell.PrepareWindow(size);
    }

    public override void PostDraw() => BookShell.PopWindowStyle();

    public override void OnOpen() { shell.BeginSession(); rest = null; }

    public override void Draw()
    {
        using var fonts = assets.LockFonts();
        var context = new UiContext(ShellStyle.Scale, ImGui.GetFontSize()) { TitleFont = fonts.Title, HeadingFont = fonts.Heading, BodyFont = fonts.Body, DisplayFont = fonts.Display, MonoFont = fonts.Mono, SignatureFont = fonts.Signature, Fonts = fonts };
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyPressed(ImGuiKey.Escape, false)) shell.RequestDismiss();
        var intent = shell.Draw(new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize()), context);
        if (intent.Drag != Vector2.Zero && rest is { } position) rest = position + intent.Drag;
        if (intent.Settings) openSettings();
        if (intent.Close) IsOpen = false;
    }
}
