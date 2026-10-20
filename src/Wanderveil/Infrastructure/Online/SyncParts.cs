using System;
using System.Buffers.Binary;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wanderveil.Features.Drawing;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.Infrastructure.Storage;

namespace Wanderveil.Infrastructure.Online;

// What this PC has of a part, ready to compare and send: a hash of it, and the bytes to send (a document's JSON, or a
// picture already within the server's limits) made only when sending.
internal sealed record LocalPart(string Hash, Func<CancellationToken, Task<(byte[] Bytes, string ContentType)>> Payload);

// One part of the passport kept in step with the server (ADR-0005): the identity, the stamps, the drawing and
// writing, or the picture. Capture runs on the UI thread and must be cheap; Encode, Prepare, and the network run on
// the sync's worker; Apply runs on the UI thread and must not count as a change here.
internal abstract class SyncPart
{
    // Its name online, and in the agreement file.
    internal abstract string Name { get; }
    // A part new on this PC takes the server's copy when there is one.
    internal abstract bool Fresh { get; }
    internal abstract bool IsPicture { get; }
    // Raised (from any thread) when the holder changed the part here.
    internal event Action? Changed;
    protected void Raise() => Changed?.Invoke();

    // UI thread: a snapshot of the part, cheap to take.
    internal abstract object? Capture();
    // Worker: what this PC has, or null when it has none (only a picture can be absent).
    internal abstract LocalPart? Encode(object? snapshot);
    // Worker: the server's copy (a document's JSON, or picture bytes) made ready to apply; throws NotSupportedException
    // for a newer version this one cannot read, FormatException for something unreadable.
    internal abstract object Prepare(byte[] server);
    // UI thread: becomes the server's copy (or none, for a picture removed online).
    internal abstract void Apply(object? prepared);

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

// The identity and appearance (`passport`).
internal sealed class IdentityPart : SyncPart
{
    private readonly PassportProfile profile;
    private bool applying;

    internal IdentityPart(PassportProfile profile, bool fresh)
    {
        this.profile = profile;
        Fresh = fresh;
        profile.Changed += () => { if (!applying) Raise(); };
    }

    internal override string Name => "passport";
    internal override bool Fresh { get; }
    internal override bool IsPicture => false;
    internal override object? Capture() => profile.Identity;
    internal override LocalPart? Encode(object? snapshot) => Document(PassportStore.Serialize((PassportIdentity)snapshot!));
    internal override object Prepare(byte[] server) => PassportStore.Parse(Encoding.UTF8.GetString(server));

    internal override void Apply(object? prepared)
    {
        applying = true;
        try { profile.Update((PassportIdentity)prepared!); }
        finally { applying = false; }
    }

    internal static LocalPart Document(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return new LocalPart(Hash(bytes), _ => Task.FromResult((bytes, "application/json")));
    }
}

// The stamps and their places (`stamps`).
internal sealed class StampsPart : SyncPart
{
    private readonly StampCollection collection;
    private bool applying;

    internal StampsPart(StampCollection collection, bool fresh)
    {
        this.collection = collection;
        Fresh = fresh;
        collection.Changed += () => { if (!applying) Raise(); };
    }

    internal override string Name => "stamps";
    internal override bool Fresh { get; }
    internal override bool IsPicture => false;
    internal override object? Capture() => StampBook.Of(collection);
    internal override LocalPart? Encode(object? snapshot) => IdentityPart.Document(StampStore.Serialize((StampBook)snapshot!));
    internal override object Prepare(byte[] server) => StampStore.Parse(Encoding.UTF8.GetString(server));

    internal override void Apply(object? prepared)
    {
        var book = (StampBook)prepared!;
        applying = true;
        try { collection.Replace(book.Stamps, book.Placements); }
        finally { applying = false; }
    }
}

// The drawing and writing on stamp pages (`marks`), sent compressed.
internal sealed class MarksPart : SyncPart
{
    private readonly PageMarks marks;
    private bool applying;

    internal MarksPart(PageMarks marks, bool fresh)
    {
        this.marks = marks;
        Fresh = fresh;
        marks.Changed += () => { if (!applying) Raise(); };
    }

    internal override string Name => "marks";
    internal override bool Fresh { get; }
    internal override bool IsPicture => false;
    internal override object? Capture() => MarkBook.Of(marks);
    internal override LocalPart? Encode(object? snapshot) => IdentityPart.Document(MarksStore.Serialize((MarkBook)snapshot!));
    internal override object Prepare(byte[] server) => MarksStore.Parse(Encoding.UTF8.GetString(server));

    internal override void Apply(object? prepared)
    {
        var book = (MarkBook)prepared!;
        applying = true;
        try { marks.Replace(book.Strokes, book.Texts); }
        finally { applying = false; }
    }
}

// Where the holder's picture lives on this PC, as the sync needs it. The plugin's PortraitPicture implements it with
// the game's texture tools; the checks with a stand-in.
internal interface IPictureHost
{
    // The picture as saved on this PC, or null when there is none. Worker thread.
    byte[]? ReadStored();
    // A copy within the server's limits (PNG or JPEG, at most 4 MiB and 2048 px): the stored file when it already
    // is, else re-encoded. Worker thread.
    Task<(byte[] Bytes, string ContentType)> ForUpload(byte[] stored, CancellationToken cancellationToken);
    // UI thread: shows and keeps the server's picture (or none), without counting as a change here.
    void Replace(byte[]? bytes);
    // Raised when the holder chose, took, or removed a picture here.
    event Action? Changed;
}

// The holder's picture (`picture`).
internal sealed class PicturePart : SyncPart
{
    private readonly IPictureHost host;

    internal PicturePart(IPictureHost host, bool fresh)
    {
        this.host = host;
        Fresh = fresh;
        host.Changed += Raise;
    }

    internal override string Name => "picture";
    internal override bool Fresh { get; }
    internal override bool IsPicture => true;
    internal override object? Capture() => null;

    internal override LocalPart? Encode(object? snapshot)
    {
        var stored = host.ReadStored();
        return stored == null ? null : new LocalPart(Hash(stored), token => host.ForUpload(stored, token));
    }

    internal override object Prepare(byte[] server) => PictureCheck.Inspect(server) != null ? server : throw new FormatException("Not a PNG or JPEG.");
    internal override void Apply(object? prepared) => host.Replace((byte[]?)prepared);
}

// A PNG's or JPEG's type and size from its own first bytes, as the server reads them.
internal static class PictureCheck
{
    internal const int MaximumBytes = 4 * 1024 * 1024;
    internal const int MaximumSide = 2048;

    internal static (string ContentType, int Width, int Height)? Inspect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 24 && bytes[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) && bytes[12..16].SequenceEqual("IHDR"u8))
            return ("image/png", (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]), int.MaxValue), (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]), int.MaxValue));
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8) return null;
        var at = 2;
        while (at + 4 <= bytes.Length)
        {
            if (bytes[at] != 0xFF) return null;
            var marker = bytes[at + 1];
            if (marker == 0xFF) { at++; continue; }
            if (marker is 0x01 or >= 0xD0 and <= 0xD7) { at += 2; continue; }
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 2)..]);
            if (length < 2) return null;
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                return at + 9 <= bytes.Length ? ("image/jpeg", BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 5)..])) : null;
            if (marker is 0xD9 or 0xDA) return null;
            at += 2 + length;
        }
        return null;
    }

    // Whether the server takes these bytes as they are.
    internal static bool Fits(byte[] bytes) =>
        bytes.Length <= MaximumBytes && Inspect(bytes) is { } image && image.Width is >= 1 and <= MaximumSide && image.Height is >= 1 and <= MaximumSide;
}
