using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ParentalRatingManager.Configuration;

namespace Jellyfin.Plugin.ParentalRatingManager.Models;

/// <summary>A Jellyfin media library exposed to the plugin UI.</summary>
public sealed class LibraryDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? CollectionType { get; set; }

    public int ItemCount { get; set; }
}

/// <summary>A single browsable media row (movie / documentary / series / season).</summary>
public sealed class MediaItemDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public int? ProductionYear { get; set; }

    public string? OfficialRating { get; set; }

    public EffectiveRating Effective { get; set; } = new(null, null, EffectiveRatingSource.Unrated, null, null, null);

    public bool HasManualOverride { get; set; }

    public int SeasonCount { get; set; }
}

/// <summary>Paged browse result.</summary>
public sealed class BrowseResultDto
{
    public List<MediaItemDto> Items { get; set; } = new();

    public int TotalCount { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}

/// <summary>Rating catalogue entry returned to the UI.</summary>
public sealed class RatingDto
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string System { get; set; } = string.Empty;

    public bool IsUnrated { get; set; }
}

/// <summary>Bulk (or single) rating assignment request.</summary>
public sealed class AssignRatingRequest
{
    public Guid[] ItemIds { get; set; } = [];

    /// <summary>Normalized rating id, or null/empty to remove the override.</summary>
    public string? RatingId { get; set; }
}

/// <summary>Result for a single item inside a bulk operation.</summary>
public sealed class ItemOperationResult
{
    public Guid ItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Success { get; set; }

    public string? Error { get; set; }
}

/// <summary>Bulk operation response.</summary>
public sealed class BulkOperationResultDto
{
    public List<ItemOperationResult> Results { get; set; } = new();

    public int Succeeded { get; set; }

    public int Failed { get; set; }
}

/// <summary>A Jellyfin user with the plugin's current policy.</summary>
public sealed class ManagedUserDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsAdministrator { get; set; }

    public bool IsManaged { get; set; }

    public string? MaxRatingId { get; set; }

    public string? MaxRatingDisplayName { get; set; }

    public int? NativeMaxScore { get; set; }

    public int? NativeMaxSubScore { get; set; }
}

/// <summary>Set maximum rating for one or more users.</summary>
public sealed class SetUserPolicyRequest
{
    public Guid[] UserIds { get; set; } = [];

    /// <summary>Normalized rating id, or null/empty for unrestricted.</summary>
    public string? MaxRatingId { get; set; }
}

/// <summary>Serializable plugin configuration sent to the UI.</summary>
public sealed class PluginConfigDto
{
    public UnratedPolicyKind UnratedPolicy { get; set; }

    public string? UnratedEquivalentRatingId { get; set; }

    public bool EnableDebugLogging { get; set; }

    public int BulkConfirmationThreshold { get; set; }

    public List<CustomRatingDefinition> CustomRatings { get; set; } = new();
}

/// <summary>Dashboard statistics.</summary>
public sealed class StatsDto
{
    public int AssignedCount { get; set; }

    public int ManagedUserCount { get; set; }

    public int LibraryCount { get; set; }

    public int RatedItemCount { get; set; }

    public int UnratedItemCount { get; set; }

    public int StaleRecordCount { get; set; }
}
