using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ParentalRatingManager.Configuration;

/// <summary>
/// How media without a determinable rating is treated for restricted users.
/// </summary>
public enum UnratedPolicyKind
{
    /// <summary>Unrated content is visible to users regardless of their maximum rating.</summary>
    Allow = 0,

    /// <summary>Unrated content is hidden from every user that has a maximum rating configured.</summary>
    Block = 1,

    /// <summary>Unrated content is treated as if it carried a configured rating.</summary>
    TreatAsRating = 2
}

/// <summary>
/// A custom rating definition an administrator can add on top of the built-in catalogue.
/// </summary>
public class CustomRatingDefinition
{
    /// <summary>Gets or sets the unique identifier (e.g. "custom-family").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name shown in the administration UI.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the rating system the entry belongs to ("us", "ca" or "custom").</summary>
    public string System { get; set; } = "custom";

    /// <summary>Gets or sets the Jellyfin parental rating score (higher = more restrictive).</summary>
    public int Score { get; set; }

    /// <summary>Gets or sets the Jellyfin parental rating sub score.</summary>
    public int SubScore { get; set; }

    /// <summary>Gets or sets the value written to Jellyfin's CustomRating field.</summary>
    public string JellyfinValue { get; set; } = string.Empty;

    /// <summary>Gets or sets extra input strings that normalize to this rating.</summary>
    public List<string> Aliases { get; set; } = new();
}

/// <summary>
/// Plugin configuration persisted by Jellyfin as XML.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets how unrated content is treated for restricted users.</summary>
    public UnratedPolicyKind UnratedPolicy { get; set; } = UnratedPolicyKind.Allow;

    /// <summary>Gets or sets the normalized rating id used when <see cref="UnratedPolicy"/> is TreatAsRating.</summary>
    public string UnratedEquivalentRatingId { get; set; } = "us-r";

    /// <summary>Gets or sets a value indicating whether verbose debug logging is enabled.</summary>
    public bool EnableDebugLogging { get; set; }

    /// <summary>
    /// Gets or sets the number of selected items above which the UI asks for confirmation
    /// before applying a bulk rating.
    /// </summary>
    public int BulkConfirmationThreshold { get; set; } = 25;

    /// <summary>Gets or sets administrator supplied custom rating definitions.</summary>
    public List<CustomRatingDefinition> CustomRatings { get; set; } = new();
}
