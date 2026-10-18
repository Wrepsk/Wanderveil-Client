using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;

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

    internal HeadlessUi(Vector2 size, float scale = 1f, float textScale = 1f, bool materials = false)
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
            io.Fonts.AddFontFromFileTTF(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf"), 18f);
            TitleFont = io.Fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "Assets", "SourceSerif4-Semibold.ttf"), 30f);
            HeadingFont = io.Fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "Assets", "SourceSerif4-Regular.ttf"), 24f);
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

    public void Dispose() => ImGui.DestroyContext(context);
}
