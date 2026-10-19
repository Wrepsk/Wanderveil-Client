namespace Wanderveil.Features;

// What happened when a saved file (stamps, identity) was read at startup.
// New: nothing saved yet. Loaded: the saved file was read. Recovered: it could not be read, so the previous
// save (its backup) was used and the unreadable file kept. Unreadable: neither could be read; both were kept
// and the passport starts fresh. Newer: written by a newer Wanderveil, left untouched. Inaccessible: the file
// exists but could not be opened (locked or denied), so it is left untouched.
internal enum LoadOutcome { New, Loaded, Recovered, Unreadable, Newer, Inaccessible }

// Whether what is on screen is on disk. ReadOnly: saving is off so a file this version cannot read
// safely is never overwritten; changes made meanwhile last until the game closes.
internal enum SaveState { Saved, Saving, Failed, ReadOnly }

// What the pages need to know about a saved file, without any file or thread details.
internal interface ISavedState
{
    LoadOutcome Loaded { get; }
    SaveState State { get; }
    void Retry();
}
