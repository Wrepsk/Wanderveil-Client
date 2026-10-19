namespace Wanderveil.UI;

// The settings the passport's own Settings pages edit. The plugin backs it with its saved configuration.
internal interface IBookShellSettings
{
    bool ReducedMotion { get; set; }

    // Persists the current values; false when saving failed.
    bool Save();
}
