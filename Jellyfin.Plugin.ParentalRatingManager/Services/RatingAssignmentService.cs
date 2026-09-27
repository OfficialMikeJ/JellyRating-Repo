using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ParentalRatingManager.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ParentalRatingManager.Services;

/// <summary>
/// Applies (or removes) plugin rating overrides. Overrides are projected into
/// Jellyfin's <see cref="BaseItem.CustomRating"/> field so the server's native
/// parental-control engine enforces them for every client and API path, while
/// the original metadata rating (<see cref="BaseItem.OfficialRating"/>) is left
/// untouched.
/// </summary>
public sealed class RatingAssignmentService
{
    private readonly ILibraryManager _libraryManager;
    private readonly PluginDataStore _store;
    private readonly RatingCatalog _catalog;
    private readonly ILocalizationManager _localization;
    private readonly ILogger<RatingAssignmentService> _logger;

    public RatingAssignmentService(
        ILibraryManager libraryManager,
        PluginDataStore store,
        RatingCatalog catalog,
        ILocalizationManager localization,
        ILogger<RatingAssignmentService> logger)
    {
        _libraryManager = libraryManager;
        _store = store;
        _catalog = catalog;
        _localization = localization;
        _logger = logger;
    }

    /// <summary>Whether an item is a valid rating target (movie/leaf video, series or season).</summary>
    public static RatingAssignmentKind? Classify(BaseItem item)
    {
        if (item is Season)
        {
            return RatingAssignmentKind.Season;
        }

        if (item is Series)
        {
            return RatingAssignmentKind.Series;
        }

        // Episodes and folders are never manually rateable (episodes inherit).
        if (item is Episode || item.IsFolder)
        {
            return null;
        }

        // Any non-folder video leaf (Movie, Video, MusicVideo, documentary) is rateable.
        if (item is MediaBrowser.Controller.Entities.Video)
        {
            return RatingAssignmentKind.Movie;
        }

        return null;
    }

    /// <summary>
    /// Chooses the string written to <see cref="BaseItem.CustomRating"/> so the
    /// level Jellyfin's engine computes matches the intended level exactly.
    /// A value Jellyfin cannot resolve (or resolves to a different level) would
    /// silently degrade to "unrated" — fail-unsafe — so we substitute:
    /// another catalogue definition with the same level whose value resolves
    /// correctly, or finally a plain integer string which Jellyfin always
    /// parses to a numeric score (with +1 when the intended level carries a
    /// sub-score, keeping enforcement strictly >= intended).
    /// </summary>
    internal static string SelectJellyfinValue(
        RatingDefinition definition,
        IReadOnlyList<RatingDefinition> catalog,
        Func<string, (int Score, int SubScore)?> resolve)
    {
        // Explicit unrated entries deliberately resolve to nothing.
        if (definition.IsUnrated || definition.Score is null)
        {
            return definition.JellyfinValue;
        }

        var intended = (definition.Score.Value, definition.SubScore ?? 0);

        if (resolve(definition.JellyfinValue) is { } direct && direct == intended)
        {
            return definition.JellyfinValue;
        }

        // Another catalogue entry at the same level may resolve correctly.
        foreach (var candidate in catalog)
        {
            if (candidate.Id == definition.Id || candidate.Score != definition.Score)
            {
                continue;
            }

            var candidateLevel = (candidate.Score.Value, candidate.SubScore ?? 0);
            if (candidateLevel != intended)
            {
                continue;
            }

            if (resolve(candidate.JellyfinValue) is { } alt && alt == candidateLevel)
            {
                return candidate.JellyfinValue;
            }
        }

        // Guaranteed-parseable numeric fallback: strict upgrade for sub-scores.
        var bump = intended.Item2 > 0 ? 1 : 0;
        return (intended.Item1 + bump).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Assigns a normalized rating to the given items, or removes the override
    /// when <paramref name="ratingId"/> is null/empty. Recomputes the stored
    /// parental-rating value for the item and all descendants so Jellyfin's
    /// query-level filtering picks the change up immediately.
    /// </summary>
    public async Task<BulkOperationResultDto> AssignAsync(Guid[] itemIds, string? ratingId, string? adminName)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        RatingDefinition? definition = null;
        if (!string.IsNullOrWhiteSpace(ratingId))
        {
            definition = _catalog.GetById(ratingId);
            if (definition is null)
            {
                throw new ArgumentException($"Unknown rating id '{ratingId}'", nameof(ratingId));
            }
        }

        var result = new BulkOperationResultDto();
        foreach (var id in itemIds)
        {
            try
            {
                await AssignOneAsync(id, definition, adminName).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply rating to item {ItemId}", id);
                result.Results.Add(new ItemOperationResult { ItemId = id, Success = false, Error = ex.Message });
            }
        }

        result.Succeeded = result.Results.Count(r => r.Success);
        result.Failed = result.Results.Count(r => !r.Success);
        _logger.LogInformation(
            "Bulk rating operation by {Admin}: rating={Rating} items={Count} succeeded={Succeeded} failed={Failed}",
            adminName ?? "unknown",
            ratingId ?? "(remove override)",
            itemIds.Length,
            result.Succeeded,
            result.Failed);
        return result;
    }

    private async Task AssignOneAsync(Guid itemId, RatingDefinition? definition, string? adminName)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            _store.RemoveAssignment(itemId);
            throw new InvalidOperationException("Item no longer exists in the Jellyfin library (stale record removed).");
        }

        var kind = Classify(item);
        if (kind is null)
        {
            throw new InvalidOperationException($"Item '{item.Name}' ({item.GetType().Name}) is not a valid rating target.");
        }

        if (definition is null)
        {
            item.CustomRating = string.Empty;
            _store.RemoveAssignment(itemId);
        }
        else
        {
            var projected = SelectJellyfinValue(definition, _catalog.GetAll(), ResolveScore);
            if (!string.Equals(projected, definition.JellyfinValue, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Rating '{Id}' value '{JellyfinValue}' does not resolve to the intended level in Jellyfin; projecting '{Projected}' instead",
                    definition.Id,
                    definition.JellyfinValue,
                    projected);
            }

            item.CustomRating = projected;
            _store.SetAssignment(new RatingAssignment
            {
                ItemId = itemId,
                LibraryId = item.GetTopParent()?.Id ?? Guid.Empty,
                RatingId = definition.Id,
                Kind = kind.Value,
                ItemName = item.Name,
                ProjectedValue = projected,
                LastModifiedUtc = DateTime.UtcNow
            });
        }

        // Persist + recompute the stored inherited rating values used by
        // Jellyfin's database-level parental filters (OnMetadataChanged
        // re-evaluates GetParentalRatingScore, which walks the DisplayParent
        // chain and therefore sees our new CustomRating).
        item.OnMetadataChanged();
        await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);

        // Cascade: descendants must recompute their stored value so season and
        // episode rows inherit the new series/season rating at the query layer.
        if (item is Folder folder)
        {
            foreach (var child in folder.GetRecursiveChildren())
            {
                child.OnMetadataChanged();
                await child.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
            }
        }

        if (definition is null)
        {
            _logger.LogInformation("Admin {Admin} removed rating override on {Item} ({Id})", adminName, item.Name, itemId);
        }
        else
        {
            _logger.LogInformation(
                "Admin {Admin} assigned rating {Rating} to {Item} ({Id})",
                adminName,
                definition.DisplayName,
                item.Name,
                itemId);
        }
    }

    private (int Score, int SubScore)? ResolveScore(string jellyfinValue)
    {
        try
        {
            var score = _localization.GetRatingScore(jellyfinValue, null);
            return score is null ? null : (score.Score, score.SubScore ?? 0);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
