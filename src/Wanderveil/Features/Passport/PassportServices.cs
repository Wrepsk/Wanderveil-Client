using System.Threading.Tasks;
using Wanderveil.UI.Components;

namespace Wanderveil.Features.Passport;

// None: the holder has not chosen a picture. Loading: a chosen or saved picture is being read. Shown: it is
// ready to draw. Failed: it could not be used; Problem says why, and the placeholder shows meanwhile.
internal enum PortraitState { None, Loading, Shown, Failed }

// The holder's own picture, as the pages need it: the host picks, copies, and loads it off the draw path.
// Where it sits in its frame is part of the identity (PassportIdentity.Portrait).
internal interface IPassportPortrait
{
    PortraitState State { get; }
    string? Problem { get; }
    bool TryGet(out BookImage image);
    // Opens the host's file picker; a chosen picture arrives later and is centred in the frame.
    void Choose();
    // Takes the picture out of the passport (and its copy off this PC); the placeholder returns.
    void Remove();
}

// Name, home world, and data center as the game shows them for the logged-in character: copied once into the
// passport when its holder asks, then theirs to change. Never treated as verified ownership.
internal sealed record CharacterDetails(string Name, string HomeWorld, string DataCenter);

internal interface ICharacterSource
{
    // Completes with null when no character is logged in.
    Task<CharacterDetails?> Read();
}

internal static class IdentityStorageText
{
    // What the reader should know about their saved passport identity, or null when there is nothing to say.
    internal static string? Notice(ISavedState? storage) => storage?.Loaded switch
    {
        LoadOutcome.Recovered => "Your passport details could not be read, so the previous save was restored. The unreadable file was kept beside it.",
        LoadOutcome.Unreadable => "Your passport details could not be read and no earlier save was usable. Those files were kept; this passport was issued fresh.",
        LoadOutcome.Newer => "Your passport details were saved by a newer Wanderveil. This version shows a fresh passport and will not change them.",
        LoadOutcome.Inaccessible => "Your passport details could not be opened, so they were left untouched. Changes made now are not saved.",
        _ => storage?.State == SaveState.Failed ? "Your passport details could not be saved." : null,
    };
}
