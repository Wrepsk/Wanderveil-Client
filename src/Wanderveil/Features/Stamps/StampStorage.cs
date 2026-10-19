namespace Wanderveil.Features.Stamps;

// What happened when a passport's saved stamps were read at startup.
// New: nothing saved yet. Loaded: the saved file was read. Recovered: it could not be read, so the previous
// save (its backup) was used and the unreadable file kept. Unreadable: neither could be read; both were kept
// and the passport starts empty. Newer: written by a newer Wanderveil, left untouched. Inaccessible: the file
// exists but could not be opened (locked or denied), so it is left untouched.
internal enum StampLoadOutcome { New, Loaded, Recovered, Unreadable, Newer, Inaccessible }

// Whether the stamps on screen are on disk. ReadOnly: saving is off so a file this version cannot read
// safely is never overwritten; stamps collected meanwhile last until the game closes.
internal enum StampSaveState { Saved, Saving, Failed, ReadOnly }

// What the pages need to know about storage, without any file or thread details.
internal interface IStampStorage
{
    StampLoadOutcome Loaded { get; }
    StampSaveState State { get; }
    void Retry();
}

internal static class StampStorageText
{
    // One sentence under a collected stamp about whether it is kept.
    internal static string Kept(IStampStorage? storage) => storage?.State switch
    {
        null => "It is kept until the game closes; it is not saved.",
        StampSaveState.Failed => "It could not be saved, so it is kept only until the game closes.",
        StampSaveState.ReadOnly => "It is kept until the game closes. Your saved stamps were left untouched, so it is not saved.",
        _ => "It is saved to your passport on this PC.",
    };

    // The collection spread's closing caption.
    internal static string Count(int count, IStampStorage? storage)
    {
        if (count == 0) return "NO STAMPS YET";
        var stamps = count == 1 ? "1 LOCAL DEMO STAMP" : $"{count} LOCAL DEMO STAMPS";
        return stamps + (storage?.State is StampSaveState.Saved or StampSaveState.Saving ? " / SAVED ON THIS PC" : " / NOT SAVED");
    }

    // A note about the saved stamps that the reader should know, or null when there is nothing to say.
    internal static string? Notice(IStampStorage? storage) => storage?.Loaded switch
    {
        StampLoadOutcome.Recovered => "Your saved stamps could not be read, so the previous save was restored. The unreadable file was kept beside it.",
        StampLoadOutcome.Unreadable => "Your saved stamps could not be read and no earlier save was usable. Those files were kept; new stamps are saved fresh.",
        StampLoadOutcome.Newer => "Your stamps were saved by a newer Wanderveil. This version shows none of them and will not change them.",
        StampLoadOutcome.Inaccessible => "Your saved stamps could not be opened, so they were left untouched. Stamps collected now are not saved.",
        _ => storage?.State == StampSaveState.Failed ? "Your stamps could not be saved." : null,
    };
}
