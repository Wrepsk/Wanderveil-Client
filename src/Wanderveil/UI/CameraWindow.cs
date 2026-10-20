using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Wanderveil.Infrastructure.Media;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

// The camera's guide, over the whole game view, while the book steps aside for Take a picture. A transparent window:
// only the guide's panel, edges, and corners take the mouse, so the game's camera can still be turned, even through
// the guide. The book opens and closes it.
internal sealed class CameraWindow : Window
{
    private readonly BookShell shell;
    private readonly PassportAssets assets;

    internal CameraWindow(BookShell shell, PassportAssets assets) : base("Wanderveil camera###Wanderveil.Camera", Flags0)
    {
        this.shell = shell;
        this.assets = assets;
        SizeCondition = ImGuiCond.Always;
        PositionCondition = ImGuiCond.Always;
        RespectCloseHotkey = false;
    }

    private const ImGuiWindowFlags Flags0 = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing;

    public override void PreDraw()
    {
        var viewport = ImGui.GetMainViewport();
        var scale = ShellStyle.Scale;
        Position = viewport.Pos;
        // WindowSystem applies host UI scaling to Size.
        Size = viewport.Size / scale;
        Flags = Flags0 | (shell.Camera.TakesMouse(ImGui.GetIO().MousePos) ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoMouseInputs);
        BookShell.PrepareWindow(viewport.Size);
    }

    public override void PostDraw() => BookShell.PopWindowStyle();

    public override void Draw()
    {
        using var fonts = assets.LockFonts();
        var context = new UiContext(ShellStyle.Scale, ImGui.GetFontSize()) { TitleFont = fonts.Title, HeadingFont = fonts.Heading, BodyFont = fonts.Body, DisplayFont = fonts.Display, MonoFont = fonts.Mono, SignatureFont = fonts.Signature, Fonts = fonts };
        shell.DrawCamera(new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize()), context);
    }
}
