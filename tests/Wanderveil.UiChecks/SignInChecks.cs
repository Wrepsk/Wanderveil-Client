using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.Infrastructure.Online;
using Wanderveil.UI;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

// The sign-in sheet in each state, driven through an account against the in-memory server, and the account in the
// passport's General settings with its two-step sign-out.
internal static class SignInChecks
{
    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        var directory = Path.Combine(Path.GetTempPath(), "wanderveil-signin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Sheet(check, artifacts, directory);
            Settings(check, artifacts, directory);
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("PASS sign-in");
    }

    private static void Sheet(Action<bool, string> check, string? artifacts, string directory)
    {
        var server = new OnlineChecks.FakeServer();
        server.Add(1001, "Wander Veil", "Ravana");
        var address = "";
        using var store = new SessionStore(directory);
        using var account = new OnlineAccount(store, server.Client(), "0.0.0.1", () => address);
        using var host = new HeadlessUi(new Vector2(520, 660), materials: true);
        var sheet = new SignInSheet();
        var action = SignInAction.None;
        void Frame() => host.Frame(() =>
        {
            var context = new UiContext(1, 18) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont, SignatureFont = host.SignatureFont };
            var view = new SignInView(account.State, account.Step, account.CharacterName, account.CharacterWorld, account.Challenge, account.Problem, account.Api?.Server.Authority, account.Flow, account.XivAuthOffered);
            var asked = sheet.Draw(new UiRect(ImGui.GetWindowPos() + new Vector2(10), new Vector2(500, 620)), view, context);
            if (asked != SignInAction.None) action = asked;
            check(ImGui.GetScrollMaxX() == 0f, "The sign-in sheet overflows sideways.");
        });
        void Frames(int count) { for (var index = 0; index < count; index++) Frame(); }
        void Click(string control)
        {
            check(sheet.Controls.ContainsKey(control), $"The sign-in sheet has no '{control}'.");
            var point = sheet.Controls[control];
            ImGui.GetIO().AddMousePosEvent(point.X, point.Y); Frame();
            ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
            ImGui.GetIO().AddMouseButtonEvent(0, false); Frames(2);
        }
        void Save(string name)
        {
            ImGui.GetIO().AddMousePosEvent(-100, -100);
            Frames(2);
            if (artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, name), host);
        }
        SignInAction Take() { var taken = action; action = SignInAction.None; return taken; }

        Frames(3);
        check(!sheet.Controls.ContainsKey("Get my code"), "Without a character the sheet offers a code.");
        Save("signin-no-character.bmp");

        // No server: the address form; a remote plain-HTTP address is refused, a local one taken.
        account.SetCharacter(OnlineChecks.Content, "Wander Veil", "Ravana");
        Frames(2);
        check(sheet.Controls.ContainsKey("Use this server") && sheet.Controls.ContainsKey("Server address"), "Without a server the sheet does not ask for one.");
        sheet.EditServer("http://wanderveil.example");
        Frames(1);
        Click("Use this server");
        check(Take() == SignInAction.None, "A plain-HTTP address to another machine was taken.");
        Save("signin-server.bmp");
        sheet.EditServer(OnlineChecks.FakeServer.Address);
        Frames(1);
        Click("Use this server");
        check(Take() == SignInAction.SaveServer && WanderveilApi.ParseServer(sheet.ServerText) != null, "A usable server address was not taken.");
        address = sheet.ServerText;
        Frames(2);

        // The application: ask for a code; it shows with the steps.
        check(account.State == AccountState.SignedOut && sheet.Controls.ContainsKey("Get my code") && sheet.Controls.ContainsKey("Sign in with XIVAuth"), "A signed-out character is not offered both ways to sign in.");
        Save("signin-ready.bmp");

        // XIVAuth: waiting for approval, with the code and a way back to its page; cancelling returns to the start.
        Click("Sign in with XIVAuth");
        check(Take() == SignInAction.StartXivAuth, "Sign in with XIVAuth did not ask to start it.");
        account.StartXivAuth();
        check(OnlineChecks.Until(() => account.Step == SignInStep.Approving), "No XIVAuth sign-in started.");
        check(sheet.Height > 0 && sheet.Height < 620, $"The sheet did not report the height it needed ({sheet.Height}).");
        Frames(2);
        check(sheet.Controls.ContainsKey("Open XIVAuth again") && sheet.Controls.ContainsKey("Cancel") && !sheet.Controls.ContainsKey("Get my code"), "Waiting for XIVAuth does not show its step.");
        Save("signin-xivauth.bmp");
        Click("Open XIVAuth again");
        check(Take() == SignInAction.OpenXivAuth, "The XIVAuth link did not ask to open it.");
        Click("Cancel");
        check(Take() == SignInAction.Restart, "Cancel did not ask to restart.");
        account.Restart();
        Frames(2);

        // A server without XIVAuth: the reason shows once, and only the Lodestone code is offered.
        server.XivAuthEnabled = false;
        account.StartXivAuth();
        check(OnlineChecks.Until(() => !account.XivAuthOffered), "A server without XIVAuth was not noticed.");
        Frames(2);
        check(!sheet.Controls.ContainsKey("Sign in with XIVAuth") && sheet.Controls.ContainsKey("Get my code"), "XIVAuth is still offered by a server without it.");
        Save("signin-no-xivauth.bmp");
        server.XivAuthEnabled = true;
        account.Restart();
        Frames(2);
        Click("Get my code");
        check(Take() == SignInAction.RequestCode, "Get my code did not ask for a code.");
        account.RequestCode();
        check(OnlineChecks.Until(() => account.Step == SignInStep.Code), "No code arrived.");
        Frames(2);
        check(sheet.Controls.ContainsKey("Copy") && sheet.Controls.ContainsKey("Open my Lodestone profile settings") && sheet.Controls.ContainsKey("Check my profile"), "The code is shown without its steps.");
        Click("Copy");
        check(Take() == SignInAction.CopyCode, "Copy did not ask to copy the code.");
        Click("Open my Lodestone profile settings");
        check(Take() == SignInAction.OpenProfile, "The Lodestone link did not ask to open it.");
        Save("signin-code.bmp");

        // Checked too early: the reason shows and the code stays.
        Click("Check my profile");
        check(Take() == SignInAction.CheckCode, "Check my profile did not ask to check.");
        account.CheckCode();
        check(OnlineChecks.Until(() => account.Problem != null), "Checking too early gave no reason.");
        Frames(2);
        check(sheet.Controls.ContainsKey("Check my profile") && account.Challenge != null, "Checking too early lost the code.");
        Save("signin-problem.bmp");

        // Start again; change the server; close.
        Click("Start again");
        check(Take() == SignInAction.Restart, "Start again did not ask to restart.");
        Click("Server: wanderveil.test (change)");
        check(Take() == SignInAction.ChangeServer, "The server line did not offer a change.");
        sheet.EditServer(address);
        Frames(2);
        Click("Keep wanderveil.test");
        Frames(2);
        check(sheet.Controls.ContainsKey("Check my profile") || sheet.Controls.ContainsKey("Get my code"), "Keeping the server did not return to the application.");
        Click("Close");
        check(Take() == SignInAction.Close, "The close mark did not ask to close.");

        // Signed in: the code on the profile, checked.
        server.Introduce(1001, account.Challenge!.Code);
        account.CheckCode();
        check(OnlineChecks.Until(() => account.State == AccountState.SignedIn), "The sheet's account did not sign in.");
    }

    private static void Settings(Action<bool, string> check, string? artifacts, string directory)
    {
        var account = new StubAccount();
        using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: new PassportProfile(PassportIdentity.Sample), account: account);
        book.Shell.ShowSettings();
        book.Frames(30);
        check(book.Shell.AccountControls.ContainsKey("Sign out"), "General settings do not show the account.");
        book.Save(artifacts, "settings-account.bmp");
        book.Click(book.Shell.AccountControls["Sign out"]);
        check(!account.SignedOut && book.Shell.AccountControls.ContainsKey("Confirm sign out"), "Sign out did not ask first.");
        book.Click(book.Shell.AccountControls["Stay signed in"]);
        check(!account.SignedOut && book.Shell.AccountControls.ContainsKey("Sign out"), "Staying signed in signed out.");
        book.Click(book.Shell.AccountControls["Sign out"]);
        book.Click(book.Shell.AccountControls["Confirm sign out"]);
        check(account.SignedOut, "Confirming sign-out did not sign out.");
    }

    private sealed class StubAccount : IBookShellAccount
    {
        internal bool SignedOut { get; private set; }
        public string Name => "Wander Veil";
        public string World => "Ravana";
        public string VerifiedBy => "lodestone";
        public DateTimeOffset VerifiedAt => new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        public string Sync => "Your passport details are saved online too. Stamps, drawing, and your picture stay on this PC for now.";
        public string? Notice => null;
        public void SignOut() => SignedOut = true;
    }
}
