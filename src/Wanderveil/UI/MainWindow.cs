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
    private readonly PassportAssets assets;
    private readonly BookShell shell;
    private Vector2? lastPosition;

    internal MainWindow(Action openSettings, PassportAssets assets) : base("Wanderveil###Wanderveil.Main", BookShell.WindowFlags)
    {
        this.openSettings = openSettings;
        this.assets = assets;
        shell = new BookShell(assets, PassportPreview.Sample);
        SizeCondition = ImGuiCond.Always;
        RespectCloseHotkey = true;
        ForceMainWindow = true;
    }

    public override void PreDraw()
    {
        var context = new UiContext(ShellStyle.Scale, ImGui.GetFontSize());
        var viewport = ImGui.GetMainViewport();
        var room = Vector2.Max(Vector2.One, viewport.WorkSize - new Vector2(16 * context.Scale));
        var size = BookShellLayout.Fit(shell.PreferredWidth, room, context);
        // WindowSystem applies host UI scaling to Size; layout above uses actual pixels.
        Size = size / context.Scale;
        if (lastPosition is { } position)
        {
            Position = Vector2.Clamp(position, viewport.WorkPos, viewport.WorkPos + Vector2.Max(Vector2.Zero, viewport.WorkSize - size));
            PositionCondition = ImGuiCond.Always;
        }
        BookShell.PrepareWindow(size);
    }

    public override void PostDraw() => BookShell.PopWindowStyle();

    public override void Draw()
    {
        using var fonts = assets.LockFonts();
        var context = new UiContext(ShellStyle.Scale, ImGui.GetFontSize()) { TitleFont = fonts.Title, HeadingFont = fonts.Heading };
        var intent = shell.Draw(new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize()), context);
        if (intent.Drag != Vector2.Zero) ImGui.SetWindowPos(ImGui.GetWindowPos() + intent.Drag);
        lastPosition = ImGui.GetWindowPos();
        if (intent.Settings) openSettings();
        if (intent.Close) IsOpen = false;
    }
}
