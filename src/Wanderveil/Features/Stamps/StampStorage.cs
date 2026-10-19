namespace Wanderveil.Features.Stamps;

internal static class StampStorageText
{
    // One sentence under a collected stamp about whether it is kept.
    internal static string Kept(ISavedState? storage) => storage?.State switch
    {
        null => "It is kept until the game closes; it is not saved.",
        SaveState.Failed => "It could not be saved, so it is kept only until the game closes.",
        SaveState.ReadOnly => "It is kept until the game closes. Your saved stamps were left untouched, so it is not saved.",
        _ => "It is saved to your passport on this PC.",
    };

    // The collection spread's closing caption.
    internal static string Count(int count, ISavedState? storage)
    {
        if (count == 0) return "NO STAMPS YET";
        var stamps = count == 1 ? "1 LOCAL DEMO STAMP" : $"{count} LOCAL DEMO STAMPS";
        return stamps + (storage?.State is SaveState.Saved or SaveState.Saving ? " / SAVED ON THIS PC" : " / NOT SAVED");
    }

    // A note about the saved stamps that the reader should know, or null when there is nothing to say.
    internal static string? Notice(ISavedState? storage) => storage?.Loaded switch
    {
        LoadOutcome.Recovered => "Your saved stamps could not be read, so the previous save was restored. The unreadable file was kept beside it.",
        LoadOutcome.Unreadable => "Your saved stamps could not be read and no earlier save was usable. Those files were kept; new stamps are saved fresh.",
        LoadOutcome.Newer => "Your stamps were saved by a newer Wanderveil. This version shows none of them and will not change them.",
        LoadOutcome.Inaccessible => "Your saved stamps could not be opened, so they were left untouched. Stamps collected now are not saved.",
        _ => storage?.State == SaveState.Failed ? "Your stamps could not be saved." : null,
    };
}
