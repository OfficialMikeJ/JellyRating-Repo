using System.Collections.Generic;
using Jellyfin.Plugin.ParentalRatingManager.Configuration;

namespace Jellyfin.Plugin.ParentalRatingManager.Services;

/// <summary>
/// Supplies administrator-defined custom rating definitions. Kept behind an
/// interface so the rating catalogue/normalizer stay free of Jellyfin
/// dependencies and remain unit-testable.
/// </summary>
public interface ICustomRatingsSource
{
    /// <summary>Returns the configured custom rating definitions (empty when none).</summary>
    IReadOnlyList<CustomRatingDefinition> GetCustomRatings();
}

/// <summary>Reads custom rating definitions from the live plugin configuration.</summary>
public sealed class PluginCustomRatingsSource : ICustomRatingsSource
{
    /// <inheritdoc />
    public IReadOnlyList<CustomRatingDefinition> GetCustomRatings()
        => Plugin.Instance?.Configuration.CustomRatings ?? (IReadOnlyList<CustomRatingDefinition>)[];
}
