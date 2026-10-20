using System;

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
