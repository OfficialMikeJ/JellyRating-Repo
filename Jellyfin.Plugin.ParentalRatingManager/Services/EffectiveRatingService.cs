using System;
using Jellyfin.Plugin.ParentalRatingManager.Models;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.ParentalRatingManager.Services;

/// <summary>
/// Resolves the effective rating for a Jellyfin item following the plugin's
/// source priority:
/// plugin season override → plugin series override → plugin item override →
/// Jellyfin/metadata rating → unrated policy.
/// </summary>
public sealed class EffectiveRatingService
{
    private const int MaxAncestorDepth = 16;

    private readonly PluginDataStore _store;
    private readonly RatingCatalog _catalog;
    private readonly RatingNormalizer _normalizer;

    public EffectiveRatingService(PluginDataStore store, RatingCatalog catalog, RatingNormalizer normalizer)
    {
        _store = store;
        _catalog = catalog;
        _normalizer = normalizer;
    }

    /// <summary>
    /// Resolves the effective rating for an item. For an episode this walks
    /// Episode → Season → Series, mirroring Jellyfin's own inheritance chain,
    /// so season overrides win over the series rating.
    /// </summary>
    public EffectiveRating Resolve(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        // 1. Direct plugin override on the item itself.
        if (_store.TryGetAssignment(item.Id, out var own) && own is not null)
        {
            return new EffectiveRating(own.RatingId, DisplayNameOf(own.RatingId, own.RatingId), EffectiveRatingSource.PluginOverride, null, null, null);
        }

        // 2. Plugin overrides on ancestors (DisplayParent chain == Jellyfin's
        // CustomRatingForComparison chain: Episode→Season→Series).
        var parent = item.DisplayParent;
        var depth = 0;
        while (parent is not null && depth++ < MaxAncestorDepth)
        {
            if (_store.TryGetAssignment(parent.Id, out var inherited) && inherited is not null)
            {
                return new EffectiveRating(
                    inherited.RatingId,
                    DisplayNameOf(inherited.RatingId, inherited.RatingId),
                    EffectiveRatingSource.InheritedOverride,
                    null,
                    parent.Id,
                    parent.Name);
            }

            parent = parent.DisplayParent;
        }

        // 3. Jellyfin metadata rating (own, then inherited from ancestors –
        // same semantics as OfficialRatingForComparison).
        var meta = ResolveMetadataRating(item);
        if (meta is not null)
        {
            return meta;
        }

        return new EffectiveRating(null, null, EffectiveRatingSource.Unrated, null, null, null);
    }

    /// <summary>Whether the item (or an ancestor) carries a plugin assignment.</summary>
    public bool HasPluginAssignmentInHierarchy(BaseItem item)
    {
        if (_store.TryGetAssignment(item.Id, out _))
        {
            return true;
        }

        var parent = item.DisplayParent;
        var depth = 0;
        while (parent is not null && depth++ < MaxAncestorDepth)
        {
            if (_store.TryGetAssignment(parent.Id, out _))
            {
                return true;
            }

            parent = parent.DisplayParent;
        }

        return false;
    }

    /// <summary>Best-effort normalization of a raw metadata rating.</summary>
    public RatingDefinition? NormalizeRaw(string? raw) => _normalizer.Resolve(raw);

    private EffectiveRating? ResolveMetadataRating(BaseItem item)
    {
        BaseItem? current = item;
        var depth = 0;
        while (current is not null && depth++ < MaxAncestorDepth)
        {
            var official = current.OfficialRating;
            if (!string.IsNullOrWhiteSpace(official))
            {
                var def = _normalizer.Resolve(official);
                var isOwn = ReferenceEquals(current, item);
                return new EffectiveRating(
                    def?.Id,
                    def?.DisplayName ?? official.Trim(),
                    EffectiveRatingSource.Metadata,
                    official,
                    isOwn ? null : current.Id,
                    isOwn ? null : current.Name);
            }

            current = current.DisplayParent;
        }

        return null;
    }

    private string DisplayNameOf(string? ratingId, string fallback)
        => _catalog.GetById(ratingId)?.DisplayName ?? fallback;
}
