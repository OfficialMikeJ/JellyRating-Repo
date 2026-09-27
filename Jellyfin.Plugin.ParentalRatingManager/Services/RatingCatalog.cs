using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.ParentalRatingManager.Configuration;
using Jellyfin.Plugin.ParentalRatingManager.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ParentalRatingManager.Services;

/// <summary>
/// The centralized rating policy catalogue: normalized ids, display names,
/// Jellyfin score/sub-score levels and aliases. Data-driven so additional
/// ratings can be added without touching application logic.
/// </summary>
public sealed class RatingCatalog
{
    private const string EmbeddedResource = "Jellyfin.Plugin.ParentalRatingManager.Ratings.ratings.json";

    private static readonly Lazy<IReadOnlyList<RatingDefinition>> _builtin = new(LoadEmbedded);

    private readonly ILogger<RatingCatalog> _logger;
    private readonly ICustomRatingsSource? _customRatings;

    public RatingCatalog(ILogger<RatingCatalog> logger, ICustomRatingsSource? customRatings = null)
    {
        _logger = logger;
        _customRatings = customRatings;
    }

    /// <summary>Gets the id of the synthetic explicit "unrated" definition.</summary>
    public string UnratedId => "unrated";

    /// <summary>Returns all definitions (built-in plus administrator-defined custom ratings).</summary>
    public IReadOnlyList<RatingDefinition> GetAll()
    {
        var list = new List<RatingDefinition>(_builtin.Value);
        foreach (var custom in _customRatings?.GetCustomRatings() ?? Enumerable.Empty<CustomRatingDefinition>())
        {
            if (string.IsNullOrWhiteSpace(custom.Id))
            {
                continue;
            }

            list.Add(new RatingDefinition
            {
                Id = custom.Id,
                DisplayName = string.IsNullOrWhiteSpace(custom.DisplayName) ? custom.Id : custom.DisplayName,
                System = string.IsNullOrWhiteSpace(custom.System) ? "custom" : custom.System,
                Score = custom.Score,
                SubScore = custom.SubScore,
                JellyfinValue = string.IsNullOrWhiteSpace(custom.JellyfinValue) ? custom.DisplayName : custom.JellyfinValue,
                IsUnrated = false,
                Aliases = custom.Aliases.ToArray()
            });
        }

        return list;
    }

    /// <summary>Looks up a definition by normalized id.</summary>
    public RatingDefinition? GetById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return GetAll().FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns true when the id is a known rating definition.</summary>
    public bool IsKnownId(string? id) => GetById(id) is not null;

    /// <summary>All definitions belonging to a rating system ("us", "ca", ...).</summary>
    public IReadOnlyList<RatingDefinition> GetBySystem(string system)
        => GetAll().Where(d => string.Equals(d.System, system, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Compares two rating levels: returns a negative value when <paramref name="a"/>
    /// is a lower level than <paramref name="b"/>. Null scores sort below everything.
    /// </summary>
    public static int CompareLevels(RatingDefinition a, RatingDefinition b) => a.CompareLevelTo(b);

    /// <summary>Whether <paramref name="candidate"/> exceeds the <paramref name="max"/> level.</summary>
    public static bool Exceeds(RatingDefinition candidate, RatingDefinition max)
        => candidate.CompareLevelTo(max) > 0;

    private static IReadOnlyList<RatingDefinition> LoadEmbedded()
    {
        var assembly = typeof(RatingCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResource)
            ?? throw new InvalidOperationException($"Embedded rating catalogue '{EmbeddedResource}' is missing");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement.GetProperty("ratings");
        var defs = JsonSerializer.Deserialize<List<RatingDefinition>>(root.GetRawText()) ?? new List<RatingDefinition>();
        return defs;
    }
}
