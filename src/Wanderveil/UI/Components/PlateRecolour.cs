using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Wanderveil.UI.Components;

// Straight-alpha RGBA pixels, row by row from the top.
internal sealed record PlatePixels(int Width, int Height, byte[] Rgba);

// Recolours a passport plate without losing its grain, shading, or embossing. tools/passport-art writes, for each
// recolourable colour of a plate, a grey weight map: how much of that colour reaches each pixel. Compositing is
// linear in each fill colour, so the plate in new colours is the printed plate plus weight x (new - printed) for
// each colour. Pure CPU work on byte arrays: run it off the draw path.
internal static class PlateRecolour
{
    // One colour change: the plate's weight map for it, and how far each channel moves (-255 to 255).
    internal readonly record struct Shift(byte[] Weight, int Red, int Green, int Blue);

    internal static PlatePixels Apply(PlatePixels plate, IReadOnlyList<Shift> shifts)
    {
        var pixels = plate.Width * plate.Height;
        var result = (byte[])plate.Rgba.Clone();
        foreach (var shift in shifts)
            if (shift.Weight.Length != pixels) throw new ArgumentException("A weight map is not the plate's size.", nameof(shifts));
        for (var index = 0; index < pixels; index++)
        {
            int red = 0, green = 0, blue = 0;
            foreach (var shift in shifts)
            {
                var weight = shift.Weight[index];
                if (weight == 0) continue;
                red += weight * shift.Red;
                green += weight * shift.Green;
                blue += weight * shift.Blue;
            }
            if (red == 0 && green == 0 && blue == 0) continue;
            var at = index * 4;
            result[at] = Clamp(result[at] + Round(red));
            result[at + 1] = Clamp(result[at + 1] + Round(green));
            result[at + 2] = Clamp(result[at + 2] + Round(blue));
        }
        return plate with { Rgba = result };
    }

    // The next smaller copy: each pixel the average of a 2 x 2 block, weighted by coverage so edges keep their
    // colour. Odd sizes round up; a missing column or row repeats the last.
    internal static PlatePixels Halve(PlatePixels plate)
    {
        var width = Math.Max(1, (plate.Width + 1) / 2);
        var height = Math.Max(1, (plate.Height + 1) / 2);
        var result = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                int red = 0, green = 0, blue = 0, alpha = 0;
                for (var dy = 0; dy < 2; dy++)
                    for (var dx = 0; dx < 2; dx++)
                    {
                        var sx = Math.Min(plate.Width - 1, x * 2 + dx);
                        var sy = Math.Min(plate.Height - 1, y * 2 + dy);
                        var at = (sy * plate.Width + sx) * 4;
                        var a = plate.Rgba[at + 3];
                        red += plate.Rgba[at] * a;
                        green += plate.Rgba[at + 1] * a;
                        blue += plate.Rgba[at + 2] * a;
                        alpha += a;
                    }
                var to = (y * width + x) * 4;
                if (alpha > 0)
                {
                    result[to] = (byte)((red + alpha / 2) / alpha);
                    result[to + 1] = (byte)((green + alpha / 2) / alpha);
                    result[to + 2] = (byte)((blue + alpha / 2) / alpha);
                }
                result[to + 3] = (byte)((alpha + 2) / 4);
            }
        return new PlatePixels(width, height, result);
    }

    private static int Round(int scaled) => scaled >= 0 ? (scaled + 127) / 255 : -((-scaled + 127) / 255);

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}

// A minimal PNG reader for the plates tools/passport-art writes: 8-bit grey, grey and alpha, RGB, or RGBA,
// not interlaced. Anything else is refused rather than misread.
internal static class PlatePng
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    internal static PlatePixels ReadRgba(Stream stream)
    {
        var (width, height, channels, raw) = Read(stream);
        var rgba = new byte[width * height * 4];
        for (var index = 0; index < width * height; index++)
        {
            var from = index * channels;
            var to = index * 4;
            switch (channels)
            {
                case 1: rgba[to] = rgba[to + 1] = rgba[to + 2] = raw[from]; rgba[to + 3] = 255; break;
                case 2: rgba[to] = rgba[to + 1] = rgba[to + 2] = raw[from]; rgba[to + 3] = raw[from + 1]; break;
                case 3: rgba[to] = raw[from]; rgba[to + 1] = raw[from + 1]; rgba[to + 2] = raw[from + 2]; rgba[to + 3] = 255; break;
                default: Buffer.BlockCopy(raw, from, rgba, to, 4); break;
            }
        }
        return new PlatePixels(width, height, rgba);
    }

    // A grey map (a weight): one byte per pixel; colour images give their first channel.
    internal static (int Width, int Height, byte[] Grey) ReadGrey(Stream stream)
    {
        var (width, height, channels, raw) = Read(stream);
        if (channels == 1) return (width, height, raw);
        var grey = new byte[width * height];
        for (var index = 0; index < grey.Length; index++) grey[index] = raw[index * channels];
        return (width, height, grey);
    }

    private static (int Width, int Height, int Channels, byte[] Pixels) Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        if (!reader.ReadBytes(8).AsSpan().SequenceEqual(Signature)) throw new InvalidDataException("Not a PNG.");
        int width = 0, height = 0, channels = 0;
        using var compressed = new MemoryStream();
        while (true)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
            var type = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
            var data = reader.ReadBytes(length);
            reader.ReadBytes(4); // CRC: the plates are the plugin's own embedded resources.
            if (data.Length != length) throw new InvalidDataException("A PNG ends early.");
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4));
                if (data[8] != 8 || data[10] != 0 || data[11] != 0 || data[12] != 0) throw new InvalidDataException("Only 8-bit, non-interlaced PNGs are read.");
                channels = data[9] switch { 0 => 1, 4 => 2, 2 => 3, 6 => 4, _ => throw new InvalidDataException("Palette PNGs are not read.") };
            }
            else if (type == "IDAT") compressed.Write(data);
            else if (type == "IEND") break;
        }
        if (width <= 0 || height <= 0) throw new InvalidDataException("A PNG has no size.");
        compressed.Position = 0;
        var stride = width * channels;
        var filtered = new byte[(stride + 1) * height];
        using (var inflate = new ZLibStream(compressed, CompressionMode.Decompress)) inflate.ReadExactly(filtered);
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var filter = filtered[y * (stride + 1)];
            var line = filtered.AsSpan(y * (stride + 1) + 1, stride);
            var row = pixels.AsSpan(y * stride, stride);
            var above = y > 0 ? pixels.AsSpan((y - 1) * stride, stride) : Span<byte>.Empty;
            for (var x = 0; x < stride; x++)
            {
                int left = x >= channels ? row[x - channels] : 0;
                int up = y > 0 ? above[x] : 0;
                int corner = x >= channels && y > 0 ? above[x - channels] : 0;
                row[x] = (byte)(line[x] + filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, corner),
                    _ => throw new InvalidDataException("A PNG row has an unknown filter."),
                });
            }
        }
        return (width, height, channels, pixels);
    }

    private static int Paeth(int left, int up, int corner)
    {
        var estimate = left + up - corner;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toCorner = Math.Abs(estimate - corner);
        return toLeft <= toUp && toLeft <= toCorner ? left : toUp <= toCorner ? up : corner;
    }
}

// An edition's plates as printed and as the holder recoloured them (cloth, foil, paper; 0 to 1 per channel).
internal readonly record struct PlateColours(System.Numerics.Vector3 Cloth, System.Numerics.Vector3 Foil, System.Numerics.Vector3 Paper);

internal readonly record struct PlateTint(PlateColours Printed, PlateColours Wanted)
{
    internal bool Changes => Printed != Wanted;
}

// Recolours an edition's own plates and makes their reduced copies, from files named as tools/passport-art writes
// them: <plate>.png and <plate>.w-<colour>.png (colour cover, foil, or paper; a plate without a map for a colour
// does not carry it).
internal static class EditionRecolour
{
    internal static readonly (BookAsset Asset, string Plate)[] Plates =
        [(BookAsset.Open, "book-open"), (BookAsset.EditionCover, "cover"), (BookAsset.NationBanner, "banner"), (BookAsset.NationMap, "map-overhead"), (BookAsset.EditionCrest, "crest")];

    // The plate at full size, then each half size down to about 96 pixels, as the generator's copies go.
    internal static List<PlatePixels> Levels(System.Func<string, Stream?> open, string plate, PlateTint tint)
    {
        using var source = open(plate + ".png") ?? throw new FileNotFoundException("A plate is missing.", plate);
        var pixels = PlatePng.ReadRgba(source);
        var shifts = new List<PlateRecolour.Shift>();
        Add("cover", tint.Printed.Cloth, tint.Wanted.Cloth);
        Add("foil", tint.Printed.Foil, tint.Wanted.Foil);
        Add("paper", tint.Printed.Paper, tint.Wanted.Paper);
        if (shifts.Count > 0) pixels = PlateRecolour.Apply(pixels, shifts);
        var levels = new List<PlatePixels> { pixels };
        while (System.Math.Min(levels[^1].Width, levels[^1].Height) / 2 >= 96 && levels.Count < 4) levels.Add(PlateRecolour.Halve(levels[^1]));
        return levels;

        void Add(string colour, System.Numerics.Vector3 from, System.Numerics.Vector3 to)
        {
            if (from == to) return;
            using var map = open($"{plate}.w-{colour}.png");
            if (map == null) return;
            var (width, height, weight) = PlatePng.ReadGrey(map);
            if (width != pixels.Width || height != pixels.Height) throw new InvalidDataException("A weight map is not its plate's size.");
            shifts.Add(new PlateRecolour.Shift(weight, Delta(from.X, to.X), Delta(from.Y, to.Y), Delta(from.Z, to.Z)));
        }

        static int Delta(float from, float to) => (int)System.MathF.Round((to - from) * 255f);
    }
}
