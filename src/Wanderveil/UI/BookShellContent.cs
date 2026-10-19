using System.Collections.Generic;
using Wanderveil.Features.Discover;
using Wanderveil.Features.Echoes;
using Wanderveil.Features.Studio;

namespace Wanderveil.UI;

// What the sections show besides the passport itself: synthetic fixtures today, real content later.
// A null dossier or empty list is a valid state that each section explains on its own pages.
internal sealed record BookShellContent(IReadOnlyList<DiscoveryPreview> Discoveries, EchoPreview? Dossier, IReadOnlyList<StudioTemplate> Templates)
{
    internal static readonly BookShellContent Sample = new(DiscoveryPreview.Samples, EchoPreview.Sample, StudioTemplate.Catalog);
}
