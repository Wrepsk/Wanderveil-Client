using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using Wanderveil.Infrastructure.Media;
using Wanderveil.Infrastructure.Online;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

// The sign-in, in its own window: shown before the passport opens for a character without a session, and when a
// session ends. It carries out what the sheet asks: codes, the Lodestone, the server address. Signing in closes it
// and calls signedIn, which opens the passport.
internal sealed class SignInWindow : Window
{
    private readonly OnlineAccount account;
    private readonly PassportAssets assets;
    private readonly Func<string> server;
    private readonly Action<string> saveServer;
    private readonly Action signedIn;
    private readonly SignInSheet sheet = new();

    internal SignInWindow(OnlineAccount account, PassportAssets assets, Func<string> server, Action<string> saveServer, Action signedIn)
        : base("Wanderveil sign-in###Wanderveil.SignIn", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoDocking)
    {
        this.account = account;
        this.assets = assets;
        this.server = server;
        this.saveServer = saveServer;
        this.signedIn = signedIn;
        Size = new Vector2(500, 620);
        SizeCondition = ImGuiCond.Always;
        PositionCondition = ImGuiCond.Appearing;
    }

    public override void PreDraw()
    {
        var viewport = ImGui.GetMainViewport();
        Position = viewport.WorkPos + (viewport.WorkSize - Size!.Value * ShellStyle.Scale) / 2;
        BookShell.PrepareWindow(Size.Value * ShellStyle.Scale);
    }

    public override void PostDraw() => BookShell.PopWindowStyle();

    public override void Draw()
    {
        var state = account.State;
        if (state == AccountState.SignedIn)
        {
            IsOpen = false;
            signedIn();
            return;
        }
        using var fonts = assets.LockFonts();
        var context = new UiContext(ShellStyle.Scale, ImGui.GetFontSize()) { TitleFont = fonts.Title, HeadingFont = fonts.Heading, BodyFont = fonts.Body, DisplayFont = fonts.Display, MonoFont = fonts.Mono, SignatureFont = fonts.Signature, Fonts = fonts };
        var view = new SignInView(state, account.Step, account.CharacterName, account.CharacterWorld, account.Challenge, account.Problem, account.Api?.Server.Authority);
        switch (sheet.Draw(new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize() - new Vector2(0, 4 * context.Scale)), view, context))
        {
            case SignInAction.RequestCode: account.RequestCode(); break;
            case SignInAction.CheckCode: account.CheckCode(); break;
            case SignInAction.Restart: account.Restart(); break;
            case SignInAction.CopyCode when account.Challenge is { } challenge: ImGui.SetClipboardText(challenge.Code); break;
            case SignInAction.OpenProfile when account.Challenge is { } challenge: Util.OpenLink(challenge.ProfileUrl); break;
            case SignInAction.ChangeServer: sheet.EditServer(server()); break;
            case SignInAction.SaveServer: saveServer(WanderveilApi.ParseServer(sheet.ServerText)!.ToString()); break;
            case SignInAction.Close: IsOpen = false; break;
        }
    }
}
