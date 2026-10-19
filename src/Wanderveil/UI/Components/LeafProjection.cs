using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI.Components;

// A leaf (the cover, or a page) rotating about a vertical hinge, drawn with perspective.
// Source is the leaf's flat rectangle; its hinge-side edge stays on Hinge. Angle 0 lies flat on the
// Direction side (+1: extends right of the hinge); pi lies flat on the other side, showing its back.
// Bend curls a paper leaf: the surface direction changes by Bend radians from hinge to outer edge
// (negative lags behind a turn that increases the angle). A stiff cover uses 0.
// Depth is the camera distance in leaf heights; a curled page needs more so its bulge does not balloon.
// Everything is drawn as textured quads, so artwork and lettering turn together.
internal readonly record struct LeafProjection(UiRect Source, Vector2 Hinge, float Width, float Height, float Angle, int Direction, float Bend = 0f, float Depth = 3.6f)
{
    internal bool ShowsBack => Angle > MathF.PI / 2;

    // Light falls off as the leaf turns edge-on, on either face.
    internal float Light => LightAt(0.5f);

    // On a curled leaf each part faces the light differently.
    internal float LightAt(float u) => 0.58f + 0.42f * MathF.Abs(MathF.Cos(Angle + Bend * u));

    // u: 0 at the hinge to 1 at the outer edge; v: 0 top to 1 bottom.
    internal Vector2 Project(float u, float v)
    {
        // Camera distance in leaf heights: far enough that the lifted leaf grows only a little.
        var depth = Height * Depth;
        float x, z;
        if (MathF.Abs(Bend) < 1e-4f)
        {
            x = u * Width * MathF.Cos(Angle);
            z = u * Width * MathF.Sin(Angle);
        }
        else
        {
            // The surface direction turns linearly along the leaf, so the leaf is a circular arc.
            x = Width * (MathF.Sin(Angle + Bend * u) - MathF.Sin(Angle)) / Bend;
            z = Width * (MathF.Cos(Angle) - MathF.Cos(Angle + Bend * u)) / Bend;
        }
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
    internal void Leaf(ImDrawListPtr list, ImTextureID texture, Vector2 uvHinge, Vector2 uvOuter, Vector4 color, int strips = 24)
    {
        for (var index = 0; index < strips; index++)
        {
            var u0 = index / (float)strips;
            var u1 = (index + 1) / (float)strips;
            var x0 = uvHinge.X + (uvOuter.X - uvHinge.X) * u0;
            var x1 = uvHinge.X + (uvOuter.X - uvHinge.X) * u1;
            list.AddImageQuad(texture, Project(u0, 0), Project(u1, 0), Project(u1, 1), Project(u0, 1),
                new Vector2(x0, uvHinge.Y), new Vector2(x1, uvHinge.Y), new Vector2(x1, uvOuter.Y), new Vector2(x0, uvOuter.Y), Tint(color, LightAt((u0 + u1) / 2)));
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

    // Moves vertices already drawn flat (from vtxStart on) onto the turning leaf and dims them with its light.
    // flat is the face's rest rectangle; its hinge is the left edge for the front, the right edge for the back.
    internal void Warp(ImDrawListPtr list, int vtxStart, UiRect flat, bool back, float alpha = 1f)
    {
        ref var vertices = ref list.VtxBuffer;
        for (var index = vtxStart; index < vertices.Size; index++)
        {
            ref var vertex = ref vertices.Ref(index);
            var u = back ? (flat.Max.X - vertex.Pos.X) / flat.Size.X : (vertex.Pos.X - flat.Position.X) / flat.Size.X;
            vertex.Pos = Project(u, (vertex.Pos.Y - flat.Position.Y) / flat.Size.Y);
            vertex.Col = Shade(vertex.Col, LightAt(Math.Clamp(u, 0f, 1f)), alpha);
        }
    }

    // Cuts what was drawn flat (vertices from vtxStart, triangles from idxStart) down to rect, as the page's
    // clip rectangle would have before the warp widened it: otherwise a page's scrolled-away content shows
    // on its turning leaf. A vertex outside moves onto rect's edge and its texture coordinate follows it
    // across its triangle, so rectangles, text, and images (ImGui's quads) are cut exactly rather than
    // squashed; other shapes are cut closely enough at a page edge. Fully hidden shapes collapse to nothing.
    internal static void Crop(ImDrawListPtr list, int vtxStart, int idxStart, UiRect rect)
    {
        ref var vertices = ref list.VtxBuffer;
        var count = vertices.Size - vtxStart;
        if (count <= 0) return;
        var positions = new Vector2[count];
        var uvs = new Vector2[count];
        var done = new bool[count];
        for (var index = 0; index < count; index++)
        {
            ref var vertex = ref vertices.Ref(vtxStart + index);
            positions[index] = vertex.Pos;
            uvs[index] = vertex.Uv;
            done[index] = vertex.Pos == Clamp(vertex.Pos, rect);
        }
        ref var indices = ref list.IdxBuffer;
        ref var commands = ref list.CmdBuffer;
        for (var command = 0; command < commands.Size; command++)
        {
            ref var cmd = ref commands.Ref(command);
            // Triangles are whole within a command, and idxStart falls between triangles.
            var end = Math.Min((int)(cmd.IdxOffset + cmd.ElemCount), indices.Size);
            for (var at = (int)cmd.IdxOffset; at + 2 < end; at += 3)
            {
                if (at < idxStart) continue;
                var a = indices.Ref(at) + (int)cmd.VtxOffset - vtxStart;
                var b = indices.Ref(at + 1) + (int)cmd.VtxOffset - vtxStart;
                var c = indices.Ref(at + 2) + (int)cmd.VtxOffset - vtxStart;
                if ((uint)a >= count || (uint)b >= count || (uint)c >= count || (done[a] && done[b] && done[c])) continue;
                var e1 = positions[b] - positions[a];
                var e2 = positions[c] - positions[a];
                var det = e1.X * e2.Y - e1.Y * e2.X;
                foreach (var corner in new[] { a, b, c })
                {
                    if (done[corner]) continue;
                    var moved = Clamp(positions[corner], rect);
                    ref var vertex = ref vertices.Ref(vtxStart + corner);
                    vertex.Pos = moved;
                    if (MathF.Abs(det) > 1e-6f)
                    {
                        var d = moved - positions[a];
                        var s = (d.X * e2.Y - d.Y * e2.X) / det;
                        var t = (e1.X * d.Y - e1.Y * d.X) / det;
                        vertex.Uv = uvs[a] + s * (uvs[b] - uvs[a]) + t * (uvs[c] - uvs[a]);
                    }
                    done[corner] = true;
                }
            }
        }
        // Anything left (only in degenerate triangles) just keeps to the rectangle.
        for (var index = 0; index < count; index++)
            if (!done[index]) vertices.Ref(vtxStart + index).Pos = Clamp(positions[index], rect);
    }

    private static Vector2 Clamp(Vector2 point, UiRect rect) => Vector2.Clamp(point, rect.Position, rect.Max);

    // Widens every clip rectangle in a draw list, for geometry warped outside the window it was drawn in.
    internal static void Unclip(ImDrawListPtr list, Vector4 rect)
    {
        ref var commands = ref list.CmdBuffer;
        for (var index = 0; index < commands.Size; index++) commands.Ref(index).ClipRect = rect;
    }

    // Packed ImGui colors are 0xAABBGGRR.
    private static uint Shade(uint color, float light, float alpha)
    {
        var r = (uint)MathF.Round((color & 0xFF) * light);
        var g = (uint)MathF.Round(((color >> 8) & 0xFF) * light);
        var b = (uint)MathF.Round(((color >> 16) & 0xFF) * light);
        var a = (uint)MathF.Round((color >> 24) * Math.Clamp(alpha, 0f, 1f));
        return (a << 24) | (Math.Min(b, 255u) << 16) | (Math.Min(g, 255u) << 8) | Math.Min(r, 255u);
    }

    internal static uint Tint(Vector4 color, float light) => ImGui.GetColorU32(new Vector4(color.X * light, color.Y * light, color.Z * light, color.W));
}
