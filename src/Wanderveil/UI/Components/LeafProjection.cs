using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI.Components;

// A flat leaf (the cover, or a page) rotating about a vertical hinge, drawn with perspective.
// Source is the leaf's flat rectangle; its hinge-side edge stays on Hinge. Angle 0 lies flat on the
// Direction side (+1: extends right of the hinge); pi lies flat on the other side, showing its back.
// Everything is drawn as textured quads, so artwork and lettering turn together.
internal readonly record struct LeafProjection(UiRect Source, Vector2 Hinge, float Width, float Height, float Angle, int Direction)
{
    internal bool ShowsBack => Angle > MathF.PI / 2;

    // Light falls off as the leaf turns edge-on, on either face.
    internal float Light => 0.58f + 0.42f * MathF.Abs(MathF.Cos(Angle));

    // u: 0 at the hinge to 1 at the outer edge; v: 0 top to 1 bottom.
    internal Vector2 Project(float u, float v)
    {
        // Camera distance in leaf heights: far enough that the lifted leaf grows only a little.
        var depth = Height * 3.6f;
        var x = u * Width * MathF.Cos(Angle);
        var z = u * Width * MathF.Sin(Angle);
        var scale = depth / MathF.Max(depth * 0.2f, depth - z);
        return new Vector2(Hinge.X + Direction * x * scale, Hinge.Y + (v - 0.5f) * Height * scale);
    }

    // A point given in Source coordinates. On the back face, u is still measured from the hinge.
    internal Vector2 Map(Vector2 flat)
    {
        var u = (flat.X - Source.Position.X) / Source.Size.X;
        if (Direction < 0) u = 1 - u;
        return Project(u, (flat.Y - Source.Position.Y) / Source.Size.Y);
    }

    // Draws texture region uvHinge..uvOuter across the whole leaf in vertical strips (affine quads
    // bend perspective, so strips keep the distortion invisible).
    internal void Leaf(ImDrawListPtr list, ImTextureID texture, Vector2 uvHinge, Vector2 uvOuter, uint color, int strips = 16)
    {
        for (var index = 0; index < strips; index++)
        {
            var u0 = index / (float)strips;
            var u1 = (index + 1) / (float)strips;
            var x0 = uvHinge.X + (uvOuter.X - uvHinge.X) * u0;
            var x1 = uvHinge.X + (uvOuter.X - uvHinge.X) * u1;
            list.AddImageQuad(texture, Project(u0, 0), Project(u1, 0), Project(u1, 1), Project(u0, 1),
                new Vector2(x0, uvHinge.Y), new Vector2(x1, uvHinge.Y), new Vector2(x1, uvOuter.Y), new Vector2(x0, uvOuter.Y), color);
        }
    }

    // Draws an image placed at a flat rectangle on the leaf's front.
    internal void Image(ImDrawListPtr list, ImTextureID texture, UiRect flat, uint color, int strips = 6)
    {
        for (var index = 0; index < strips; index++)
        {
            var a = index / (float)strips;
            var b = (index + 1) / (float)strips;
            var left = flat.Position.X + flat.Size.X * a;
            var right = flat.Position.X + flat.Size.X * b;
            list.AddImageQuad(texture, Map(new Vector2(left, flat.Position.Y)), Map(new Vector2(right, flat.Position.Y)),
                Map(new Vector2(right, flat.Max.Y)), Map(new Vector2(left, flat.Max.Y)),
                new Vector2(a, 0), new Vector2(b, 0), new Vector2(b, 1), new Vector2(a, 1), color);
        }
    }

    // Single-line lettering laid out flat at position with extra tracking, drawn glyph by glyph.
    internal unsafe void Text(ImDrawListPtr list, ImFontPtr font, float size, Vector2 position, string text, float tracking, uint color)
    {
        if (font.IsNull) return;
        var scale = size / font.FontSize;
        var x = position.X;
        var atlas = font.ContainerAtlas;
        foreach (var character in text)
        {
            var raw = font.FindGlyph(character);
            if (raw == null) continue;
            var glyph = new ImFontGlyphPtr(raw);
            if (glyph.Visible != 0)
            {
                var texture = atlas.Textures[(int)glyph.TextureIndex].TexID;
                var p0 = new Vector2(x + glyph.X0 * scale, position.Y + glyph.Y0 * scale);
                var p1 = new Vector2(x + glyph.X1 * scale, position.Y + glyph.Y1 * scale);
                list.AddImageQuad(texture, Map(p0), Map(new Vector2(p1.X, p0.Y)), Map(p1), Map(new Vector2(p0.X, p1.Y)),
                    new Vector2(glyph.U0, glyph.V0), new Vector2(glyph.U1, glyph.V0), new Vector2(glyph.U1, glyph.V1), new Vector2(glyph.U0, glyph.V1), color);
            }
            x += glyph.AdvanceX * scale + tracking;
        }
    }

    internal static uint Tint(Vector4 color, float light) => ImGui.GetColorU32(new Vector4(color.X * light, color.Y * light, color.Z * light, color.W));
}
