using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.ParentalRatingManager.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ParentalRatingManager.Services;

/// <summary>
/// Media discovery against Jellyfin's library database. Jellyfin remains the
/// single source for libraries, items, hierarchy and metadata ratings – the
/// plugin never scans the filesystem itself.
/// </summary>
public sealed class LibraryContentService
{
    private static readonly BaseItemKind[] _leafKinds =
    {
        BaseItemKind.Movie, BaseItemKind.Video, BaseItemKind.MusicVideo, BaseItemKind.Trailer
    };

    private static readonly BaseItemKind[] _allBrowsableKinds =
    {
        BaseItemKind.Movie, BaseItemKind.Video, BaseItemKind.MusicVideo, BaseItemKind.Trailer, BaseItemKind.Series
    };

    private readonly ILibraryManager _libraryManager;
    private readonly EffectiveRatingService _effectiveRating;
    private readonly PluginDataStore _store;
    private readonly ILogger<LibraryContentService> _logger;

    public LibraryContentService(
        ILibraryManager libraryManager,
        EffectiveRatingService effectiveRating,
        PluginDataStore store,
        ILogger<LibraryContentService> logger)
    {
        _libraryManager = libraryManager;
        _effectiveRating = effectiveRating;
        _store = store;
        _logger = logger;
    }

    /// <summary>All Jellyfin collection folders (libraries), discovered dynamically.</summary>
    public IReadOnlyList<LibraryDto> GetLibraries()
    {
        var root = _libraryManager.GetUserRootFolder();
        return root.Children.OfType<Folder>()
            .Where(f => f is not MediaBrowser.Controller.Entities.AggregateFolder)
            .Select(f => new LibraryDto
            {
                Id = f.Id,
                Name = f.Name ?? string.Empty,
                CollectionType = (f as IHasCollectionType)?.CollectionType?.ToString()
            })
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Filter values supported by the browse endpoint.</summary>
    public enum BrowseFilter
    {
        All,
        Rated,
        Unrated,
        ManuallyRated
    }

    /// <summary>
    /// Browse a library's movies/documentaries/series (server-side search and
    /// paging; rating filters applied against the resolved effective rating).
    /// </summary>
    public BrowseResultDto Browse(
        Guid libraryId,
        string? search,
        BrowseFilter filter,
        string? ratingId,
        string? kind,
        int page,
        int pageSize)
    {
        var root = _libraryManager.GetItemById(libraryId) as Folder
            ?? throw new ArgumentException("Library not found", nameof(libraryId));

        var kinds = ResolveKinds(kind);
        var query = new InternalItemsQuery
        {
            Parent = root,
            Recursive = true,
            IncludeItemTypes = kinds,
            OrderBy = [(ItemSortBy.Name, SortOrder.Ascending)],
            DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            query.SearchTerm = search.Trim();
        }

        var items = _libraryManager.GetItemList(query);

        var filtered = items
            .Where(i => MatchesRatingFilter(i, filter, ratingId))
            .Select(i => ToDto(i, root.Name))
            .ToList();

        var pageItems = filtered
            .Skip(Math.Max(0, page) * pageSize)
            .Take(Math.Clamp(pageSize, 1, 500))
            .ToList();

        return new BrowseResultDto
        {
            Items = pageItems,
            TotalCount = filtered.Count,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Browse every collection folder at once – the default media view so the
    /// admin sees all movies/documentaries/series without picking a library.
    /// </summary>
    public BrowseResultDto BrowseAll(
        string? search,
        BrowseFilter filter,
        string? ratingId,
        string? kind,
        int page,
        int pageSize)
    {
        var kinds = ResolveKinds(kind);
        var items = new List<(BaseItem Item, string Library)>();

        foreach (var lib in GetLibraries())
        {
            var root = _libraryManager.GetItemById(lib.Id) as Folder;
            if (root is null)
            {
                continue;
            }

            var query = new InternalItemsQuery
            {
                Parent = root,
                Recursive = true,
                IncludeItemTypes = kinds,
                OrderBy = [(ItemSortBy.Name, SortOrder.Ascending)],
                DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
            };

            if (!string.IsNullOrWhiteSpace(search))
            {
                query.SearchTerm = search.Trim();
            }

            items.AddRange(_libraryManager.GetItemList(query).Select(i => (i, lib.Name)));
        }

        var filtered = items
            .Where(t => MatchesRatingFilter(t.Item, filter, ratingId))
            .OrderBy(t => t.Item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pageItems = filtered
            .Skip(Math.Max(0, page) * pageSize)
            .Take(Math.Clamp(pageSize, 1, 500))
            .Select(t => ToDto(t.Item, t.Library))
            .ToList();

        return new BrowseResultDto
        {
            Items = pageItems,
            TotalCount = filtered.Count,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>Fetch a single item by id (or null).</summary>
    public BaseItem? GetItem(Guid id) => _libraryManager.GetItemById(id);

    /// <summary>Seasons of a series, each with its effective rating.</summary>
    public IReadOnlyList<MediaItemDto> GetSeasons(Guid seriesId)
    {
        var series = _libraryManager.GetItemById(seriesId) as Series
            ?? throw new ArgumentException("Series not found", nameof(seriesId));

        var query = new InternalItemsQuery
        {
            Parent = series,
            IncludeItemTypes = [BaseItemKind.Season],
            OrderBy = [(ItemSortBy.IndexNumber, SortOrder.Ascending)],
            DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
        };

        return _libraryManager.GetItemList(query).Select(i => ToDto(i)).ToList();
    }

    /// <summary>Dashboard statistics (kept cheap – counts only).</summary>
    public StatsDto GetStats()
    {
        var libraries = GetLibraries();
        var assignments = _store.GetAllAssignments();
        var users = _store.GetAllUserRestrictions();

        int rated = 0, unrated = 0;
        foreach (var lib in libraries)
        {
            var root = _libraryManager.GetItemById(lib.Id) as Folder;
            if (root is null)
            {
                continue;
            }

            rated += CountItems(root, true);
            unrated += CountItems(root, false);
        }

        var stale = assignments.Count(a => _libraryManager.GetItemById(a.ItemId) is null);

        return new StatsDto
        {
            AssignedCount = assignments.Count,
            ManagedUserCount = users.Count,
            LibraryCount = libraries.Count,
            RatedItemCount = rated,
            UnratedItemCount = unrated,
            StaleRecordCount = stale
        };
    }

    private int CountItems(Folder root, bool rated)
    {
        var query = new InternalItemsQuery
        {
            Parent = root,
            Recursive = true,
            IncludeItemTypes = _allBrowsableKinds,
            HasParentalRating = rated,
            DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
        };
        return _libraryManager.GetItemList(query).Count;
    }

    /// <summary>Drops store records pointing at deleted items/users.</summary>
    public int PruneStaleRecords(Func<Guid, bool> userExists)
        => _store.Prune(id => _libraryManager.GetItemById(id) is not null, userExists);

    private MediaItemDto ToDto(BaseItem item, string? libraryName = null)
    {
        var effective = _effectiveRating.Resolve(item);
        var kind = RatingAssignmentService.Classify(item) ?? RatingAssignmentKind.Movie;
        var dto = new MediaItemDto
        {
            Id = item.Id,
            Name = item.Name ?? string.Empty,
            Kind = kind.ToString(),
            ProductionYear = item.ProductionYear,
            LibraryName = libraryName,
            OfficialRating = item.OfficialRating,
            Effective = effective,
            HasManualOverride = _store.TryGetAssignment(item.Id, out _)
        };

        if (item is Series series)
        {
            var seasons = new InternalItemsQuery
            {
                Parent = series,
                IncludeItemTypes = [BaseItemKind.Season],
                DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
            };
            dto.SeasonCount = _libraryManager.GetItemList(seasons).Count;
        }

        return dto;
    }

    private bool MatchesRatingFilter(BaseItem item, BrowseFilter filter, string? ratingId)
    {
        if (!string.IsNullOrWhiteSpace(ratingId))
        {
            return string.Equals(_effectiveRating.Resolve(item).RatingId, ratingId, StringComparison.OrdinalIgnoreCase);
        }

        return filter switch
        {
            BrowseFilter.All => true,
            BrowseFilter.ManuallyRated => _effectiveRating.HasPluginAssignmentInHierarchy(item),
            BrowseFilter.Rated => _effectiveRating.Resolve(item).Source != EffectiveRatingSource.Unrated,
            BrowseFilter.Unrated => _effectiveRating.Resolve(item).Source == EffectiveRatingSource.Unrated,
            _ => true
        };
    }

    private BaseItemKind[] ResolveKinds(string? kind)
    {
        return (kind ?? string.Empty).ToLowerInvariant() switch
        {
            "movies" or "movie" => _leafKinds,
            "series" => [BaseItemKind.Series],
            _ => _allBrowsableKinds
        };
    }
}
