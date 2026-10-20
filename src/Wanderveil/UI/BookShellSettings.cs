using System;
using System.Collections.Generic;

namespace Wanderveil.UI;

// The settings the passport's own Settings pages edit. The plugin backs it with its saved configuration.
internal interface IBookShellSettings
{
    bool ReducedMotion { get; set; }

    // Persists the current values; false when saving failed.
    bool Save();
}

// The signed-in account, as the Settings pages show it: whose passport this is, how that was proved, whether it is
// in step with the server, and signing out.
internal interface IBookShellAccount
{
    string Name { get; }
    string World { get; }
    // How the character was verified ("lodestone" or "xivauth") and when.
    string VerifiedBy { get; }
    DateTimeOffset VerifiedAt { get; }
    // Whether this passport is in step with the server, in words.
    string Sync { get; }
    // Something the holder should know about syncing, or null.
    string? Notice { get; }
    void SignOut();
    // Deletes the online account (asked twice first); the answer arrives later.
    void DeleteAccount();
    // Whether a deletion is waiting for the server, and why the last one did not happen, if it did not.
    bool Deleting { get; }
    string? Problem { get; }
}

// A player on the sharing page: who they are (a verified character) and since when (approved or blocked).
internal sealed record SharingPerson(Guid AccountId, string Name, string World, string VerifiedBy, DateTimeOffset Since);

// A request to view a passport: incoming ones name their verified sender; outgoing ones name the character asked for,
// pending until approved (a decline looks the same to its sender).
internal sealed record SharingRequest(Guid Id, Guid? From, string Name, string World, bool Approved, DateTimeOffset ExpiresAt);

// The holder's sharing, as the Settings pages show and change it (ADR-0005). Reads are the latest known state; every
// action is carried out in the background, after which the state is read again.
internal interface IBookShellSharing
{
    bool Loaded { get; }
    bool Busy { get; }
    string? Problem { get; }
    string? Message { get; }
    bool Anyone { get; }
    IReadOnlyList<SharingRequest> Incoming { get; }
    IReadOnlyList<SharingRequest> Outgoing { get; }
    IReadOnlyList<SharingPerson> Viewers { get; }
    IReadOnlyList<SharingPerson> Blocked { get; }
    // The player the holder has targeted in game, if any.
    (string Name, string World)? Target { get; }
    // Called while the page shows: reads again when what it shows is old.
    void Refresh();
    void SetAnyone(bool anyone);
    void Approve(Guid request);
    void Decline(Guid request);
    void Withdraw(Guid request);
    void RemoveViewer(Guid player);
    void Block(Guid player);
    void Unblock(Guid player);
    void Ask(string name, string world);
    // Opens a passport this holder may see, by its character, read-only in a book of its own.
    void Visit(string name, string world);
    // Whether a passport is being opened.
    bool Opening { get; }
}

// Someone else's passport, opened to look at (ADR-0005): whose it is, and reporting or blocking them. The book shows
// it read-only, with no settings, editing, stamps to place, drawing, or other sections.
internal interface IBookShellVisit
{
    string Name { get; }
    string World { get; }
    string VerifiedBy { get; }
    // What the last report or block did, or why not.
    string? Message { get; }
    // part: identity, picture, marks, stamps, or other; reason: harassment, hateful, sexual, spam, impersonation, or other.
    void Report(string part, string reason);
    void Block();
}
