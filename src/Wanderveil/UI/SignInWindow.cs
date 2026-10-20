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
    // The sheet's type, a size up from the host's: it is read at a distance, not on a page held close.
    private const float Type = 1.25f;
    private const float Width = 600;
    // The XIVAuth sign-in whose page was opened, so each opens the browser once by itself.
    private string? opened;

    internal SignInWindow(OnlineAccount account, PassportAssets assets, Func<string> server, Action<string> saveServer, Action signedIn)
        : base("Wanderveil sign-in###Wanderveil.SignIn", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoDocking)
    {
        this.account = account;
        this.assets = assets;
        this.server = server;
        this.saveServer = saveServer;
        this.signedIn = signedIn;
        Size = new Vector2(Width, 640);
        SizeCondition = ImGuiCond.Always;
        PositionCondition = ImGuiCond.Appearing;
    }

    public override void PreDraw()
    {
        var viewport = ImGui.GetMainViewport();
        // As tall as the sheet needed last frame, within the screen; WindowSystem applies host scaling to Size.
        if (sheet.Height > 0)
            Size = new Vector2(Width, Math.Clamp(sheet.Height / ShellStyle.Scale + 6, 240, MathF.Max(240, (viewport.WorkSize.Y - 40) / ShellStyle.Scale)));
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
        var context = new UiContext(ShellStyle.Scale, MathF.Round(ImGui.GetFontSize() * Type)) { TitleFont = fonts.Title, HeadingFont = fonts.Heading, BodyFont = fonts.Body, DisplayFont = fonts.Display, MonoFont = fonts.Mono, SignatureFont = fonts.Signature, Fonts = fonts };
        if (account.Flow is { } flow && flow.FlowId != opened) { opened = flow.FlowId; Util.OpenLink(flow.Page.ToString()); }
        var view = new SignInView(state, account.Step, account.CharacterName, account.CharacterWorld, account.Challenge, account.Problem, account.Api?.Server.Authority, account.Flow, account.XivAuthOffered);
        switch (sheet.Draw(new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize() - new Vector2(0, 4 * context.Scale)), view, context))
        {
            case SignInAction.RequestCode: account.RequestCode(); break;
            case SignInAction.CheckCode: account.CheckCode(); break;
            case SignInAction.StartXivAuth: account.StartXivAuth(); break;
            case SignInAction.OpenXivAuth when account.Flow is { } current: Util.OpenLink(current.Page.ToString()); break;
            case SignInAction.Restart: account.Restart(); break;
            case SignInAction.CopyCode when account.Challenge is { } challenge: ImGui.SetClipboardText(challenge.Code); break;
            case SignInAction.OpenProfile when account.Challenge is { } challenge: Util.OpenLink(challenge.ProfileUrl); break;
            case SignInAction.ChangeServer: sheet.EditServer(server()); break;
            case SignInAction.SaveServer: saveServer(WanderveilApi.ParseServer(sheet.ServerText)!.ToString()); break;
            case SignInAction.Close: IsOpen = false; break;
        }
    }
}
