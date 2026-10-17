using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Wanderveil.UiChecks;

internal sealed unsafe class HeadlessUi : IDisposable
{
    private readonly ImGuiContextPtr context;
    private readonly Vector2 displaySize;
    internal byte* AtlasPixels { get; }
    internal int AtlasWidth { get; }
    internal int AtlasHeight { get; }

    internal HeadlessUi(Vector2 size, float scale = 1f, float textScale = 1f)
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
        io.Fonts.AddFontDefault();
        byte* pixels;
        int width, height;
        io.Fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height);
        io.Fonts.SetTexID(0, new ImTextureID(1));
        AtlasPixels = pixels;
        AtlasWidth = width;
        AtlasHeight = height;
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
