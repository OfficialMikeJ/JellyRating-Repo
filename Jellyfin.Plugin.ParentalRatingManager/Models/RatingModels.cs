using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ParentalRatingManager.Models;

/// <summary>
/// Which kind of media entity a rating assignment targets.
/// </summary>
public enum RatingAssignmentKind
{
    /// <summary>A movie or other single video leaf item (including documentaries).</summary>
    Movie = 0,

    /// <summary>An entire TV series.</summary>
    Series = 1,

    /// <summary>A single season inside a series.</summary>
    Season = 2
}

/// <summary>
/// Where the effective rating currently comes from.
/// </summary>
public enum EffectiveRatingSource
{
    /// <summary>No rating could be determined.</summary>
    Unrated = 0,

    /// <summary>Rating comes from Jellyfin/media metadata (own or inherited).</summary>
    Metadata = 1,

    /// <summary>Rating was manually assigned to this item through the plugin.</summary>
    PluginOverride = 2,

    /// <summary>Rating is inherited from a parent series (or season) plugin override.</summary>
    InheritedOverride = 3
}

/// <summary>
/// A normalized rating definition from the plugin catalogue.
/// </summary>
public sealed class RatingDefinition
{
    /// <summary>Unique id such as "us-r" or "ca-18a".</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Human readable label, e.g. "R" or "18A".</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Rating system: "us", "ca" or "common".</summary>
    [JsonPropertyName("system")]
    public string System { get; set; } = "us";

    /// <summary>Jellyfin parental rating score. Null marks explicit unrated entries.</summary>
    [JsonPropertyName("score")]
    public int? Score { get; set; }

    /// <summary>Jellyfin parental rating sub score.</summary>
    [JsonPropertyName("subScore")]
    public int? SubScore { get; set; }

    /// <summary>Value written to <c>BaseItem.CustomRating</c> so Jellyfin resolves the same level.</summary>
    [JsonPropertyName("jellyfinValue")]
    public string JellyfinValue { get; set; } = string.Empty;

    /// <summary>True when the entry represents explicit unrated content.</summary>
    [JsonPropertyName("isUnrated")]
    public bool IsUnrated { get; set; }

    /// <summary>Input strings that normalize to this definition.</summary>
    [JsonPropertyName("aliases")]
    public string[] Aliases { get; set; } = [];

    /// <summary>Compares this level against another: returns -1/0/1 by (score, subScore).</summary>
    public int CompareLevelTo(RatingDefinition other)
    {
        ArgumentNullException.ThrowIfNull(other);
        int cmp = Nullable.Compare(Score, other.Score);
        return cmp != 0 ? cmp : Nullable.Compare(SubScore, other.SubScore);
    }
}

/// <summary>
/// A stored plugin rating assignment.
/// </summary>
public sealed class RatingAssignment
{
    /// <summary>Jellyfin item id.</summary>
    [JsonPropertyName("itemId")]
    public Guid ItemId { get; set; }

    /// <summary>Top level library (collection folder) the item belongs to.</summary>
    [JsonPropertyName("libraryId")]
    public Guid LibraryId { get; set; }

    /// <summary>Normalized rating id from the plugin catalogue.</summary>
    [JsonPropertyName("ratingId")]
    public string RatingId { get; set; } = string.Empty;

    /// <summary>Assignment target kind.</summary>
    [JsonPropertyName("kind")]
    public RatingAssignmentKind Kind { get; set; }

    /// <summary>Snapshot of the item name for audit/display.</summary>
    [JsonPropertyName("itemName")]
    public string? ItemName { get; set; }

    /// <summary>The value actually written to <c>BaseItem.CustomRating</c> (may differ from the definition's preferred value when a fail-safe substitute was needed).</summary>
    [JsonPropertyName("projectedValue")]
    public string? ProjectedValue { get; set; }

    /// <summary>UTC time the assignment was last changed.</summary>
    [JsonPropertyName("lastModifiedUtc")]
    public DateTime LastModifiedUtc { get; set; }
}

/// <summary>
/// A stored per-user maximum-rating restriction.
/// </summary>
public sealed class UserRestrictionRecord
{
    /// <summary>Jellyfin user id.</summary>
    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }

    /// <summary>Snapshot of the username.</summary>
    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    /// <summary>Normalized rating id for the maximum allowed level. Null = unrestricted.</summary>
    [JsonPropertyName("maxRatingId")]
    public string? MaxRatingId { get; set; }

    /// <summary>UTC time the policy was last changed.</summary>
    [JsonPropertyName("lastModifiedUtc")]
    public DateTime LastModifiedUtc { get; set; }
}

/// <summary>
/// Result of resolving the effective rating for an item.
/// </summary>
/// <param name="RatingId">Normalized rating id when known.</param>
/// <param name="DisplayName">Display label for the effective rating.</param>
/// <param name="Source">Where the rating came from.</param>
/// <param name="RawValue">Raw metadata string, when the source is metadata.</param>
/// <param name="InheritedFromItemId">Item the override was inherited from, if any.</param>
/// <param name="InheritedFromName">Name of the item the override was inherited from, if any.</param>
public sealed record EffectiveRating(
    string? RatingId,
    string? DisplayName,
    EffectiveRatingSource Source,
    string? RawValue,
    Guid? InheritedFromItemId,
    string? InheritedFromName);
