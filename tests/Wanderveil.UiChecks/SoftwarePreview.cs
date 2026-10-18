using System.Numerics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;

namespace Wanderveil.UiChecks;

// Test-only rasterizer for the real ImGui draw lists. No game host, GPU, or production renderer dependency.
internal static unsafe class SoftwarePreview
{
    internal static void Save(string path, HeadlessUi host)
    {
        var data = ImGui.GetDrawData();
        var width = (int)data.DisplaySize.X;
        var height = (int)data.DisplaySize.Y;
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 46;
            pixels[index + 1] = 36;
            pixels[index + 2] = 25;
            pixels[index + 3] = 255;
        }
        for (var listIndex = 0; listIndex < data.CmdListsCount; listIndex++)
        {
            var list = new ImDrawListPtr(data.CmdLists[listIndex]);
            for (var commandIndex = 0; commandIndex < list.CmdBuffer.Size; commandIndex++)
            {
                var command = list.CmdBuffer[commandIndex];
                if (command.UserCallback != null)
                    throw new InvalidOperationException("Preview does not support native draw callbacks.");
                var texture = command.TextureId.Handle == 1 ? null : host.Textures?.Get(command.TextureId.Handle)
                    ?? throw new InvalidOperationException("Unregistered preview texture.");
                for (var index = 0u; index < command.ElemCount; index += 3)
                {
                    var a = list.VtxBuffer[(int)(command.VtxOffset + list.IdxBuffer[(int)(command.IdxOffset + index)])];
                    var b = list.VtxBuffer[(int)(command.VtxOffset + list.IdxBuffer[(int)(command.IdxOffset + index + 1)])];
                    var c = list.VtxBuffer[(int)(command.VtxOffset + list.IdxBuffer[(int)(command.IdxOffset + index + 2)])];
                    Triangle(a, b, c, command.ClipRect, pixels, width, height, host, texture);
                }
            }
        }
        if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var locked = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(pixels, 0, locked.Scan0, pixels.Length); }
            finally { bitmap.UnlockBits(locked); }
            bitmap.Save(path, ImageFormat.Png);
            return;
        }
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0x4D42);
        writer.Write(54 + pixels.Length);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(-height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(pixels.Length);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(pixels);
    }

    private static void Triangle(ImDrawVert a, ImDrawVert b, ImDrawVert c, Vector4 clip, byte[] pixels,
        int width, int height, HeadlessUi host, PreviewTextures.Texture? texture)
    {
        static float Edge(Vector2 start, Vector2 end, Vector2 point) =>
            ((point.X - start.X) * (end.Y - start.Y)) - ((point.Y - start.Y) * (end.X - start.X));
        var area = Edge(a.Pos, b.Pos, c.Pos);
        if (MathF.Abs(area) < 0.0001f)
            return;
        var minX = Math.Max(0, (int)MathF.Max(clip.X, MathF.Floor(MathF.Min(a.Pos.X, MathF.Min(b.Pos.X, c.Pos.X)))));
        var minY = Math.Max(0, (int)MathF.Max(clip.Y, MathF.Floor(MathF.Min(a.Pos.Y, MathF.Min(b.Pos.Y, c.Pos.Y)))));
        var maxX = Math.Min(width, (int)MathF.Min(clip.Z, MathF.Ceiling(MathF.Max(a.Pos.X, MathF.Max(b.Pos.X, c.Pos.X)))));
        var maxY = Math.Min(height, (int)MathF.Min(clip.W, MathF.Ceiling(MathF.Max(a.Pos.Y, MathF.Max(b.Pos.Y, c.Pos.Y)))));
        for (var y = minY; y < maxY; y++)
            for (var x = minX; x < maxX; x++)
            {
                var point = new Vector2(x + 0.5f, y + 0.5f);
                var wa = Edge(b.Pos, c.Pos, point) / area;
                var wb = Edge(c.Pos, a.Pos, point) / area;
                var wc = 1f - wa - wb;
                if (wa < 0f || wb < 0f || wc < 0f)
                    continue;
                var uv = (a.Uv * wa) + (b.Uv * wb) + (c.Uv * wc);
                var textureWidth = texture?.Width ?? host.AtlasWidth;
                var textureHeight = texture?.Height ?? host.AtlasHeight;
                var tx = Math.Clamp((int)(uv.X * textureWidth), 0, textureWidth - 1);
                var ty = Math.Clamp((int)(uv.Y * textureHeight), 0, textureHeight - 1);
                var sampleIndex = ((ty * textureWidth) + tx) * 4;
                var sampleAlpha = texture == null ? host.AtlasPixels[sampleIndex + 3] : texture.Pixels[sampleIndex + 3];
                var alpha = (((a.Col >> 24) * wa) + ((b.Col >> 24) * wb) + ((c.Col >> 24) * wc)) / 255f * sampleAlpha / 255f;
                var target = ((y * width) + x) * 4;
                for (var channel = 0; channel < 3; channel++)
                {
                    var shift = channel * 8;
                    var color = (((a.Col >> shift) & 255) * wa) + (((b.Col >> shift) & 255) * wb) + (((c.Col >> shift) & 255) * wc);
                    var destination = target + (2 - channel);
                    var sample = texture == null ? host.AtlasPixels[sampleIndex + channel] : texture.Pixels[sampleIndex + channel];
                    pixels[destination] = (byte)((color * sample / 255f * alpha) + (pixels[destination] * (1f - alpha)));
                }
            }
    }
}
