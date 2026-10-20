using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

// Spike (Phase 2, drawing on pages): how a grainy pencil brush can look in ImGui, and what it costs. Three approaches
// draw the same synthetic handwriting on paper-coloured pages:
//   A. Dabs: every sample of a stroke stamps a few tiny jittered discs (pure draw-list geometry, no texture).
//   B. Grain strip: each stroke is a band of quads whose texture coordinates come from the page, sampling a
//      paper-tooth grain texture fixed to the paper, so the paper shows through the same way under every stroke.
//   C. Raster cache: strokes are painted with the same grain into a page-sized bitmap on the CPU, drawn as one image
//      (the plugin would rebuild it off the draw thread when a stroke ends, and draw only the live stroke as B).
// It writes a side-by-side preview and INFO lines with vertices and milliseconds; nothing here is product code.
internal static class BrushSpike
{
    private const int GrainSize = 512;
    private static readonly Vector4 Graphite = new(0.16f, 0.17f, 0.2f, 1f);
    private static readonly Vector4 Paper = new(0.847f, 0.773f, 0.639f, 1f);

    private readonly record struct Sample(Vector2 Point, float Pressure);

    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        var panel = new Vector2(380, 520);
        using var host = new HeadlessUi(new Vector2(panel.X * 3 + 64, panel.Y + 32), materials: true);
        ImGui.GetIO().BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        var grain = Grain(GrainSize, 7);
        host.Textures!.Register(9100, GrainSize, GrainSize, grain);
        var grainTexture = new ImTextureID(9100UL);

        var typical = Strokes(panel, 40, 1);
        var heavy = Strokes(panel, 300, 2);
        var points = typical.Sum(stroke => stroke.Length);
        var heavyPoints = heavy.Sum(stroke => stroke.Length);

        // C's bitmap, painted on the CPU (timed as the plugin's background rebuild would be).
        var timer = Stopwatch.StartNew();
        var raster = Raster(typical, panel, grain);
        var rasterTypical = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        Raster(heavy, panel, grain);
        var rasterHeavy = timer.Elapsed.TotalMilliseconds;
        host.Textures.Register(9101, (int)panel.X, (int)panel.Y, raster);

        UiRect Panel(int index) => new(new Vector2(16 + index * (panel.X + 16), 16), panel);
        void DrawPanel(int index, IReadOnlyList<Sample[]> strokes)
        {
            var list = ImGui.GetWindowDrawList();
            var page = Panel(index);
            list.AddRectFilled(page.Position, page.Max, ImGui.GetColorU32(Paper));
            switch (index)
            {
                case 0: Dabs(list, strokes, page.Position); break;
                case 1: Strip(list, strokes, page, grainTexture); break;
                default: list.AddImage(new ImTextureID(9101UL), page.Position, page.Max); break;
            }
        }

        // The look, side by side, for the typical page.
        host.Frame(() => { for (var index = 0; index < 3; index++) DrawPanel(index, typical); });
        host.Frame(() => { for (var index = 0; index < 3; index++) DrawPanel(index, typical); });
        if (artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "brush-spike.bmp"), host);

        // Cost: each approach alone, building its draw list for 30 frames; vertices from the last.
        foreach (var (name, strokes, count) in new[] { ("typical", typical, points), ("heavy", heavy, heavyPoints) })
            for (var index = 0; index < 3; index++)
            {
                if (index == 2 && name == "heavy") { host.Textures.Register(9102, (int)panel.X, (int)panel.Y, Raster(heavy, panel, grain)); }
                var approach = index;
                host.Frame(() => DrawPanel(approach, strokes));
                timer.Restart();
                for (var frame = 0; frame < 30; frame++) host.Frame(() => DrawPanel(approach, strokes));
                var milliseconds = timer.Elapsed.TotalMilliseconds / 30;
                var vertices = Vertices();
                Console.WriteLine($"INFO brush-spike {"ABC"[approach]} {name} ({strokes.Count} strokes, {count} samples): {vertices} vertices, {milliseconds:0.00} ms per frame (headless CPU, whole frame)");
                check(vertices > 0, "A brush approach draws nothing.");
            }
        Console.WriteLine($"INFO brush-spike C raster: typical {rasterTypical:0.0} ms, heavy {rasterHeavy:0.0} ms to paint the page bitmap once ({panel.X} x {panel.Y})");
        Console.WriteLine("PASS brush-spike");
    }

    private static unsafe int Vertices()
    {
        var data = ImGui.GetDrawData();
        var total = 0;
        for (var index = 0; index < data.CmdListsCount; index++) total += new ImDrawListPtr(data.CmdLists[index]).VtxBuffer.Size;
        return total;
    }

    // Deterministic handwriting: rows of looping, slanted scribbles with pressure that swells and tapers at the ends,
    // a sample every 2 pixels, as a mouse or pen would give after smoothing.
    private static List<Sample[]> Strokes(Vector2 panel, int count, int seed)
    {
        var random = new Random(seed);
        var strokes = new List<Sample[]>();
        for (var stroke = 0; stroke < count; stroke++)
        {
            var row = stroke % 16;
            var start = new Vector2(20 + (float)random.NextDouble() * (panel.X - 160), 30 + row * 30 + (float)random.NextDouble() * 6);
            var length = 50 + (float)random.NextDouble() * 90;
            var loops = 2 + random.Next(4);
            var height = 7 + (float)random.NextDouble() * 6;
            var samples = new List<Sample>();
            var previous = start;
            samples.Add(new Sample(start, 0.2f));
            for (var t = 0f; t <= 1f; t += 0.002f)
            {
                var angle = t * loops * MathF.Tau;
                var point = start + new Vector2(t * length + MathF.Sin(angle) * height * 0.45f + MathF.Cos(angle) * 2f, -MathF.Cos(angle) * height + t * 4f);
                if (Vector2.Distance(point, previous) < 2f) continue;
                var pressure = MathF.Min(1f, MathF.Min(t, 1f - t) * 8f) * (0.75f + 0.25f * MathF.Sin(angle * 0.5f));
                samples.Add(new Sample(point, MathF.Max(0.15f, pressure)));
                previous = point;
            }
            strokes.Add(samples.ToArray());
        }
        return strokes;
    }

    // Paper tooth: fine speckle over a soft, coarser mottle, thresholded so some of the paper always shows through.
    private static byte[] Grain(int size, int seed)
    {
        var random = new Random(seed);
        var coarse = new float[(size / 8 + 1) * (size / 8 + 1)];
        for (var index = 0; index < coarse.Length; index++) coarse[index] = (float)random.NextDouble();
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // Bilinear value noise for the mottle.
                float gx = x / 8f, gy = y / 8f;
                int x0 = (int)gx, y0 = (int)gy;
                float fx = gx - x0, fy = gy - y0;
                var row = size / 8 + 1;
                float At(int cx, int cy) => coarse[Math.Min(cy, row - 1) * row + Math.Min(cx, row - 1)];
                var mottle = At(x0, y0) * (1 - fx) * (1 - fy) + At(x0 + 1, y0) * fx * (1 - fy) + At(x0, y0 + 1) * (1 - fx) * fy + At(x0 + 1, y0 + 1) * fx * fy;
                var tooth = 0.55f * (float)random.NextDouble() + 0.45f * mottle;
                var alpha = Math.Clamp((tooth - 0.28f) / 0.42f, 0f, 1f);
                var at = (y * size + x) * 4;
                pixels[at] = pixels[at + 1] = pixels[at + 2] = 255;
                pixels[at + 3] = (byte)(alpha * 255);
            }
        return pixels;
    }

    // A: dabs. Three jittered discs per sample, each faint, so overlaps build up density like graphite.
    private static void Dabs(ImDrawListPtr list, IReadOnlyList<Sample[]> strokes, Vector2 origin)
    {
        var random = new Random(3);
        foreach (var stroke in strokes)
            foreach (var sample in stroke)
                for (var dab = 0; dab < 3; dab++)
                {
                    var jitter = new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 2.2f * sample.Pressure;
                    list.AddCircleFilled(origin + sample.Point + jitter, 0.7f + 0.9f * sample.Pressure, ImGui.GetColorU32(Graphite with { W = 0.28f }), 6);
                }
    }

    // B: a strip of quads per stroke, joined by averaged normals, as wide as the pressure, sampling the grain by page
    // position; a second, wider and fainter strip softens the edge.
    private static void Strip(ImDrawListPtr list, IReadOnlyList<Sample[]> strokes, UiRect page, ImTextureID grain)
    {
        var solid = ImGui.GetColorU32(Graphite with { W = 0.85f });
        var halo = ImGui.GetColorU32(Graphite with { W = 0.25f });
        foreach (var stroke in strokes)
        {
            if (stroke.Length < 2) continue;
            for (var index = 1; index < stroke.Length; index++)
            {
                var a = stroke[index - 1];
                var b = stroke[index];
                var na = Normal(stroke, index - 1);
                var nb = Normal(stroke, index);
                foreach (var (scale, colour) in new[] { (1.8f, halo), (1f, solid) })
                {
                    var wa = (0.6f + 1.4f * a.Pressure) * scale;
                    var wb = (0.6f + 1.4f * b.Pressure) * scale;
                    Vector2 P(Vector2 point) => page.Position + point;
                    Vector2 Uv(Vector2 point) => point / GrainSize;
                    var p1 = a.Point + na * wa; var p2 = b.Point + nb * wb; var p3 = b.Point - nb * wb; var p4 = a.Point - na * wa;
                    list.AddImageQuad(grain, P(p1), P(p2), P(p3), P(p4), Uv(p1), Uv(p2), Uv(p3), Uv(p4), colour);
                }
            }
        }

        static Vector2 Normal(Sample[] stroke, int index)
        {
            var before = stroke[Math.Max(0, index - 1)].Point;
            var after = stroke[Math.Min(stroke.Length - 1, index + 1)].Point;
            var direction = after - before;
            if (direction.LengthSquared() < 1e-6f) return Vector2.UnitY;
            direction = Vector2.Normalize(direction);
            return new Vector2(-direction.Y, direction.X);
        }
    }

    // C: the strokes painted into an RGBA page bitmap: each sample a soft disc, its coverage multiplied by the grain,
    // composited over (straight alpha). Transparent where nothing is drawn.
    private static byte[] Raster(IReadOnlyList<Sample[]> strokes, Vector2 size, byte[] grain)
    {
        int width = (int)size.X, height = (int)size.Y;
        var coverage = new float[width * height];
        foreach (var stroke in strokes)
            for (var index = 1; index < stroke.Length; index++)
            {
                var a = stroke[index - 1];
                var b = stroke[index];
                // A dab every pixel along the segment.
                var steps = Math.Max(1, (int)Vector2.Distance(a.Point, b.Point));
                for (var step = 0; step < steps; step++)
                {
                    var t = step / (float)steps;
                    var centre = Vector2.Lerp(a.Point, b.Point, t);
                    var radius = 0.7f + 1.5f * (a.Pressure + (b.Pressure - a.Pressure) * t);
                    int x0 = (int)(centre.X - radius - 1), x1 = (int)(centre.X + radius + 1), y0 = (int)(centre.Y - radius - 1), y1 = (int)(centre.Y + radius + 1);
                    for (var y = Math.Max(0, y0); y <= Math.Min(height - 1, y1); y++)
                        for (var x = Math.Max(0, x0); x <= Math.Min(width - 1, x1); x++)
                        {
                            var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre);
                            var dab = Math.Clamp(radius - distance + 0.5f, 0f, 1f) * 0.35f;
                            if (dab <= 0) continue;
                            var tooth = grain[((y % GrainSize) * GrainSize + x % GrainSize) * 4 + 3] / 255f;
                            ref var c = ref coverage[y * width + x];
                            c = c + dab * tooth * (1 - c);
                        }
                }
            }
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < coverage.Length; index++)
        {
            pixels[index * 4] = (byte)(Graphite.X * 255);
            pixels[index * 4 + 1] = (byte)(Graphite.Y * 255);
            pixels[index * 4 + 2] = (byte)(Graphite.Z * 255);
            pixels[index * 4 + 3] = (byte)(Math.Min(1f, coverage[index] * 1.1f) * 255);
        }
        return pixels;
    }
}
