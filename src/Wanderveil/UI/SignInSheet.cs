using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Infrastructure.Online;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

// What the holder asked for on the sign-in sheet.
internal enum SignInAction { None, RequestCode, CopyCode, OpenProfile, CheckCode, Restart, ChangeServer, SaveServer, Close }

// What the sheet shows: the character's account state and sign-in step, as OnlineAccount has them.
internal sealed record SignInView(AccountState State, SignInStep Step, string Name, string World, LodestoneChallenge? Challenge, string? Problem, string? Server);

// The sign-in, as an application form on passport paper: who is applying, the code to put on the Lodestone profile,
// and the check. Drawn in its own window before the passport opens; returns what the holder asked for. Holds only
// the server address being typed.
internal sealed class SignInSheet
{
    private string serverText = "";
    private bool editingServer;
    private bool serverInvalid;

    // The server address the holder entered, after SaveServer.
    internal string ServerText => serverText.Trim();

    // For checks: where each control was drawn last, by its label.
    internal System.Collections.Generic.Dictionary<string, Vector2> Controls { get; } = new(StringComparer.Ordinal);

    internal void EditServer(string? current)
    {
        serverText = current ?? "";
        editingServer = true;
        serverInvalid = false;
    }

    internal SignInAction Draw(UiRect bounds, SignInView view, UiContext context)
    {
        Controls.Clear();
        var list = ImGui.GetWindowDrawList();
        var s = context.Scale;
        var radius = 6 * s;
        // The paper, with a cloth band across its head and a printed rule under it.
        list.AddRectFilled(bounds.Position + new Vector2(0, 3 * s), bounds.Max + new Vector2(0, 3 * s), ImGui.GetColorU32(UiTheme.Shadow), radius);
        list.AddRectFilled(bounds.Position, bounds.Max, ImGui.GetColorU32(UiTheme.Paper), radius);
        var band = new UiRect(bounds.Position, new Vector2(bounds.Size.X, MathF.Round(46 * s)));
        list.AddRectFilled(band.Position, band.Max, ImGui.GetColorU32(UiTheme.Edition), radius, ImDrawFlags.RoundCornersTop);
        const string Office = "WANDERVEIL  ·  PASSPORT OFFICE";
        var tracking = 2.2f * s;
        var officeWidth = UiTypography.MeasureTracked(Office, TextRole.Caption, context, tracking);
        UiTypography.DrawTracked(list, band.Position + new Vector2((band.Size.X - officeWidth) / 2, (band.Size.Y - UiTypography.Size(TextRole.Caption, context)) / 2), Office, TextRole.Caption, context, tracking, UiTheme.Foil);
        list.AddRect(bounds.Position, bounds.Max, ImGui.GetColorU32(UiTheme.MutedInk with { W = 0.35f }), radius, ImDrawFlags.None, s);

        var pad = MathF.Round(26 * s);
        var inner = new UiRect(new Vector2(bounds.Position.X + pad, band.Max.Y + 20 * s), new Vector2(bounds.Size.X - pad * 2, bounds.Max.Y - band.Max.Y - 20 * s - pad));
        var action = SignInAction.None;
        // Its own child window, so printed text wraps at the form's edge.
        ImGui.SetCursorScreenPos(inner.Position);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild("Sign-in form", inner.Size, false, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings);
        try
        {
            if (editingServer || view.State == AccountState.NoServer) action = ServerForm(view, inner, context);
            else action = view.State switch
            {
                AccountState.NoCharacter => NoCharacter(context),
                AccountState.SignedIn => SignedIn(view, inner, context),
                _ => Application(view, inner, context),
            };
        }
        finally
        {
            ImGui.EndChild();
            ImGui.PopStyleVar();
        }
        // Close, in the band's corner.
        var close = new UiRect(new Vector2(band.Max.X - 34 * s, band.Position.Y + 8 * s), new Vector2(28 * s, band.Size.Y - 16 * s));
        if (BookControl.Draw("Close sign-in", "", close, context, BookControlStyle.Foil, hint: "Close")) action = SignInAction.Close;
        // A drawn cross: the foil lettering has no multiplication sign.
        var middle = close.Position + close.Size / 2;
        var arm = 5 * s;
        var cross = ImGui.GetColorU32(ImGui.IsItemHovered() ? UiTheme.FoilBright : UiTheme.Foil);
        list.AddLine(middle - new Vector2(arm), middle + new Vector2(arm), cross, 1.5f * s);
        list.AddLine(middle + new Vector2(-arm, arm), middle + new Vector2(arm, -arm), cross, 1.5f * s);
        Controls["Close"] = close.Position + close.Size / 2;
        return action;
    }

    private SignInAction NoCharacter(UiContext context)
    {
        Heading("Log in to a character", context);
        Body("Wanderveil keeps a passport for each of your characters. Log in to the character whose passport this is, and this page will ask it to sign in.", context);
        return SignInAction.None;
    }

    private SignInAction SignedIn(SignInView view, UiRect inner, UiContext context)
    {
        Heading("Welcome, " + view.Name, context);
        Body("You are signed in. Your passport is ready.", context);
        return SignInAction.None;
    }

    private SignInAction Application(SignInView view, UiRect inner, UiContext context)
    {
        var action = SignInAction.None;
        UiTypography.Text("APPLICANT", TextRole.Caption, context, UiTheme.MutedInk);
        Heading(view.Name, context, gap: 2);
        UiTypography.Text(view.World, TextRole.Body, context, UiTheme.MutedInk);
        Gap(14, context);
        Rule(inner, context);
        Gap(14, context);

        if (view.Challenge is not { } challenge)
        {
            Body("Your passport is kept for this character, so Wanderveil needs to know it is yours. You prove it the way many FFXIV tools do: put a short code on the character's Lodestone profile, and the server reads it there.", context);
            Gap(16, context);
            var asking = view.Step == SignInStep.Requesting;
            if (Stamp(asking ? "Asking for a code" : "Get my code", inner, context, enabled: !asking)) action = SignInAction.RequestCode;
            Problem(view, inner, context);
            // The small print, before anything is sent.
            Gap(18, context);
            UiTypography.Text("Signing in sends this character's name and world to the Wanderveil server, which reads its public Lodestone profile. Wanderveil never asks for your game password.", TextRole.Caption, context, UiTheme.MutedInk);
        }
        else
        {
            Step("1", "Copy this code.", context);
            var code = context with { BodyFont = context.MonoFont, FontSize = MathF.Round(context.FontSize * 1.7f) };
            var codeSize = UiTypography.Measure(challenge.Code, TextRole.Body, code, inner.Size.X, false);
            var at = ImGui.GetCursorScreenPos() + new Vector2(18 * context.Scale, 4 * context.Scale);
            var box = new UiRect(at - new Vector2(10, 6) * context.Scale, codeSize + new Vector2(20, 12) * context.Scale);
            var list = ImGui.GetWindowDrawList();
            list.AddRectFilled(box.Position, box.Max, ImGui.GetColorU32(UiTheme.PaperInset), 3 * context.Scale);
            list.AddRect(box.Position, box.Max, ImGui.GetColorU32(UiTheme.EditionInk with { W = 0.6f }), 3 * context.Scale, ImDrawFlags.None, context.Scale);
            UiTypography.Draw(list, at, challenge.Code, TextRole.Body, code, inner.Size.X, UiTheme.EditionInk, false);
            ImGui.SetCursorScreenPos(new Vector2(box.Max.X + 14 * context.Scale, at.Y + (codeSize.Y - UiTypography.Size(TextRole.Caption, context)) / 2));
            if (Link("Copy", context)) action = SignInAction.CopyCode;
            ImGui.SetCursorScreenPos(new Vector2(inner.Position.X, box.Max.Y + 12 * context.Scale));
            Step("2", "Paste it anywhere in your Character Profile on the Lodestone, and save.", context);
            ImGui.SetCursorScreenPos(ImGui.GetCursorScreenPos() + new Vector2(18 * context.Scale, 0));
            if (Link("Open my Lodestone profile settings", context)) action = SignInAction.OpenProfile;
            Gap(10, context);
            Step("3", "Come back and check.", context);
            Gap(6, context);
            var checking = view.Step == SignInStep.Checking;
            if (Stamp(checking ? "Checking the Lodestone" : "Check my profile", inner, context, enabled: !checking)) action = SignInAction.CheckCode;
            Problem(view, inner, context);
            Gap(10, context);
            UiTypography.Text("The code works until " + challenge.ExpiresAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) + ". Once you are signed in you can remove it from your profile.", TextRole.Caption, context, UiTheme.MutedInk);
            Gap(4, context);
            if (Link("Start again", context)) action = SignInAction.Restart;
        }

        Gap(8, context);
        if (Link("Server: " + (view.Server ?? "none") + " (change)", context)) action = SignInAction.ChangeServer;
        return action;
    }

    private SignInAction ServerForm(SignInView view, UiRect inner, UiContext context)
    {
        var action = SignInAction.None;
        Heading("Choose a Wanderveil server", context);
        Body("There is no public Wanderveil server yet. If you run one (on this PC, for testing), enter its address. Wanderveil talks to servers over HTTPS, or plain HTTP only on this PC.", context);
        Gap(14, context);
        if (BookControl.Field("Server address", "Server address", "https://wanderveil.example", ref serverText, 200, context)) serverInvalid = false;
        Controls["Server address"] = ImGui.GetItemRectMin() + (ImGui.GetItemRectMax() - ImGui.GetItemRectMin()) / 2;
        Gap(14, context);
        if (Stamp("Use this server", inner, context, enabled: serverText.Trim().Length > 0))
        {
            serverInvalid = WanderveilApi.ParseServer(serverText) == null;
            if (!serverInvalid) { editingServer = false; action = SignInAction.SaveServer; }
        }
        if (serverInvalid)
        {
            Gap(12, context);
            Notice("That is not an address Wanderveil can use: it needs HTTPS, or plain HTTP to this PC (such as http://127.0.0.1:5080).", inner, context);
        }
        if (view.State != AccountState.NoServer && editingServer)
        {
            Gap(10, context);
            if (Link("Keep " + (view.Server ?? "the current server"), context)) editingServer = false;
        }
        return action;
    }

    // Why the last step did not work, under the action that tried it.
    private static void Problem(SignInView view, UiRect inner, UiContext context)
    {
        if (view.Problem is not { } problem) return;
        Gap(12, context);
        Notice(problem, inner, context);
    }

    // A primary action: an inked frame, filled faintly when hovered, around a printed label.
    private bool Stamp(string label, UiRect inner, UiContext context, bool enabled = true)
    {
        var s = context.Scale;
        var size = new Vector2(MathF.Min(inner.Size.X, UiTypography.Measure(label, TextRole.Caption, context, inner.Size.X, false).X + 44 * s), MathF.Round(34 * s));
        var bounds = new UiRect(ImGui.GetCursorScreenPos(), size);
        var list = ImGui.GetWindowDrawList();
        var hovered = enabled && ImGui.IsMouseHoveringRect(bounds.Position, bounds.Max);
        if (hovered) list.AddRectFilled(bounds.Position, bounds.Max, ImGui.GetColorU32(UiTheme.EditionInk with { W = 0.1f }), 3 * s);
        list.AddRect(bounds.Position, bounds.Max, ImGui.GetColorU32(UiTheme.EditionInk with { W = enabled ? 0.9f : 0.35f }), 3 * s, ImDrawFlags.None, 1.5f * s);
        if (!enabled) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
        var pressed = BookControl.Draw(label, label, bounds, context, BookControlStyle.Ink) && enabled;
        if (!enabled) ImGui.PopStyleVar();
        Controls[label] = bounds.Position + bounds.Size / 2;
        ImGui.SetCursorScreenPos(new Vector2(bounds.Position.X, bounds.Max.Y));
        ImGui.Dummy(new Vector2(0, 0));
        return pressed;
    }

    private bool Link(string label, UiContext context)
    {
        var pressed = BookControl.Link(label, context);
        Controls[label] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        return pressed;
    }

    // Something the holder should read: a ruled margin note in the edition's ink.
    private static void Notice(string text, UiRect inner, UiContext context)
    {
        var s = context.Scale;
        var start = ImGui.GetCursorScreenPos();
        var indent = 12 * s;
        ImGui.SetCursorScreenPos(start + new Vector2(indent, 0));
        UiTypography.Text(text, TextRole.Body, context, UiTheme.EditionInk);
        var end = ImGui.GetItemRectMax().Y;
        ImGui.GetWindowDrawList().AddLine(start + new Vector2(2 * s, 2 * s), new Vector2(start.X + 2 * s, end - 2 * s), ImGui.GetColorU32(UiTheme.EditionInk), 2 * s);
        ImGui.SetCursorScreenPos(new Vector2(start.X, end));
        ImGui.Dummy(Vector2.Zero);
    }

    private static void Step(string number, string text, UiContext context)
    {
        var start = ImGui.GetCursorScreenPos();
        UiTypography.Text(number, TextRole.Heading, context with { BodyFont = context.MonoFont }, UiTheme.EditionInk);
        ImGui.SetCursorScreenPos(start + new Vector2(18 * context.Scale, 0));
        UiTypography.Text(text, TextRole.Body, context, UiTheme.Ink);
        Gap(4, context);
    }

    private static void Heading(string text, UiContext context, float gap = 10)
    {
        UiTypography.Text(text, TextRole.Title, context, UiTheme.Ink);
        Gap(gap, context);
    }

    private static void Body(string text, UiContext context) => UiTypography.Text(text, TextRole.Body, context, UiTheme.Ink);

    private static void Rule(UiRect inner, UiContext context)
    {
        var y = ImGui.GetCursorScreenPos().Y;
        ImGui.GetWindowDrawList().AddLine(new Vector2(inner.Position.X, y), new Vector2(inner.Max.X, y), ImGui.GetColorU32(UiTheme.MutedInk with { W = 0.4f }), context.Scale);
        ImGui.Dummy(new Vector2(0, 1));
    }

    private static void Gap(float pixels, UiContext context) => ImGui.Dummy(new Vector2(0, pixels * context.Scale));
}
