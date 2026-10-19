using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI;

namespace Wanderveil.UiChecks;

internal sealed unsafe class HeadlessUi : IDisposable
{
    private readonly ImGuiContextPtr context;
    private readonly Vector2 displaySize;
    internal byte* AtlasPixels { get; }
    internal int AtlasWidth { get; }
    internal int AtlasHeight { get; }
    internal PreviewTextures? Textures { get; }
    internal ImFontPtr TitleFont { get; }
    internal ImFontPtr HeadingFont { get; }
    internal ImFontPtr SignatureFont { get; }
    internal ImFontPtr BodyFont { get; }
    internal ImFontPtr DisplayFont { get; }
    internal ImFontPtr MonoFont { get; }

    // japanese: the host font is local Yu Gothic with Japanese glyphs, a surrogate for the game's Japanese-capable
    // default font. Like Segoe UI it is a preview stand-in, never a shipped asset.
    internal HeadlessUi(Vector2 size, float scale = 1f, float textScale = 1f, bool materials = false, bool japanese = false)
    {
        displaySize = size;
        context = ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.IniFilename = null;
        io.DisplaySize = size;
        io.DeltaTime = 1f / 60f;
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.ConfigInputTrickleEventQueue = false;
        io.FontGlobalScale = scale * textScale;
        if (materials)
        {
            // A local Windows UI font is a preview surrogate for the host font, never a shipped asset.
            if (japanese) io.Fonts.AddFontFromFileTTF(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "YuGothM.ttc"), 18f, default, io.Fonts.GetGlyphRangesJapanese());
            else io.Fonts.AddFontFromFileTTF(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf"), 18f);
            TitleFont = io.Fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "Assets", "CormorantGaramond-Bold.ttf"), 30f);
            HeadingFont = io.Fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "Assets", "CormorantGaramond-SemiBold.ttf"), 24f);
            BodyFont = io.Fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "Assets", "CormorantGaramond-Medium.ttf"), 24f);
            DisplayFont = io.Fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "Assets", "Cinzel-Bold.ttf"), 30f);
            MonoFont = io.Fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "Assets", "CourierPrime-Regular.ttf"), 24f);
            SignatureFont = io.Fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "Assets", "Caveat.ttf"), 30f);
            Textures = new PreviewTextures();
        }
        else io.Fonts.AddFontDefault();
        byte* pixels;
        int width, height;
        io.Fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height);
        io.Fonts.SetTexID(0, new ImTextureID(1));
        AtlasPixels = pixels;
        AtlasWidth = width;
        AtlasHeight = height;
        for (var index = 1; index < io.Fonts.Textures.Size; index++)
        {
            io.Fonts.GetTexDataAsRGBA32(index, &pixels, &width, &height);
            var id = 1000UL + (ulong)index;
            io.Fonts.SetTexID(index, new ImTextureID(id));
            var page = new byte[width * height * 4];
            Marshal.Copy((nint)pixels, page, 0, page.Length);
            Textures!.Register(id, width, height, page);
        }
        ImGui.GetStyle().ScaleAllSizes(scale);
    }

    internal void Frame(Action draw)
    {
        ImGui.NewFrame();
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(displaySize);
        ImGui.Begin("Wanderveil UI checks", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
        try
        {
            draw();
        }
        finally
        {
            ImGui.End();
        }
        ImGui.Render();
    }

    internal void BookFrame(Vector2 size, Action draw, bool studyBackdrop = false)
    {
        ImGui.NewFrame();
        // An authored backdrop makes the transparent host and artwork silhouette inspectable.
        var background = ImGui.GetBackgroundDrawList();
        background.AddRectFilledMultiColor(Vector2.Zero, displaySize, 0xff493c29, 0xff493c29, 0xff26382f, 0xff26382f);
        if (!studyBackdrop)
            for (var x = 0; x < displaySize.X; x += 64)
                background.AddLine(new Vector2(x, 0), new Vector2(x, displaySize.Y), 0x183cddbd);
        ImGui.SetNextWindowPos(new Vector2(16), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(size);
        BookShell.PrepareWindow(size);
        ImGui.Begin("Wanderveil book checks", BookShell.WindowFlags | ImGuiWindowFlags.NoSavedSettings);
        try { draw(); }
        finally { ImGui.End(); BookShell.PopWindowStyle(); }
        ImGui.Render();
    }

    public void Dispose() => ImGui.DestroyContext(context);
}
