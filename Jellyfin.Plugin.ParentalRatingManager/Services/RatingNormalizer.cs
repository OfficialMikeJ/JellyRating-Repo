using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.ParentalRatingManager.Models;

namespace Jellyfin.Plugin.ParentalRatingManager.Services;

/// <summary>
/// Translates raw metadata rating strings ("Rated R", "18-A ", "PG&#x20;")
/// into normalized catalogue rating ids before any comparison happens.
/// Pure logic – no Jellyfin dependencies – so it is fully unit-testable.
/// </summary>
public sealed class RatingNormalizer
{
    private static readonly Regex _multiSpace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex _ratedPrefix = new(@"^rated\s*[:：\-–]?\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] _unratedMarkers =
    {
        "unrated", "not rated", "nr", "ur", "n/a", "na", "notrated", "notratedorunrated"
    };

    private static readonly string[] _countryPrefixes = { "US", "CA", "USA", "UK", "GB" };

    private readonly RatingCatalog _catalog;

    public RatingNormalizer(RatingCatalog catalog)
    {
        _catalog = catalog;
    }

    /// <summary>
    /// Cleans a raw value: HTML-entity decode, whitespace collapse, trim,
    /// "Rated " prefix removal. Never returns null.
    /// </summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var value = WebUtility.HtmlDecode(raw).Replace(' ', ' ');
        value = _multiSpace.Replace(value, " ").Trim();
        value = _ratedPrefix.Replace(value, string.Empty).Trim();
        return value;
    }

    /// <summary>
    /// Aggressive key used for alias comparison: upper-cased with all
    /// spaces, dashes, underscores and dots removed ("18-A " -&gt; "18A",
    /// "tv pg" -&gt; "TVPG").
    /// </summary>
    public static string ComparisonKey(string? raw)
    {
        var clean = Clean(raw);
        var sb = new StringBuilder(clean.Length);
        foreach (var c in clean)
        {
            if (char.IsWhiteSpace(c) || c is '-' or '_' or '.' or ' ')
            {
                continue;
            }

            sb.Append(char.ToUpperInvariant(c));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Resolves a raw metadata string to a normalized rating definition.
    /// Returns the explicit "unrated" definition for known unrated markers,
    /// or null when the string cannot be recognized at all.
    /// </summary>
    public RatingDefinition? Resolve(string? raw)
    {
        var clean = Clean(raw);
        if (clean.Length == 0)
        {
            return null;
        }

        if (_unratedMarkers.Contains(clean, StringComparer.OrdinalIgnoreCase)
            || _unratedMarkers.Contains(ComparisonKey(clean), StringComparer.OrdinalIgnoreCase))
        {
            return _catalog.GetById(_catalog.UnratedId);
        }

        var defs = _catalog.GetAll();

        // 1. Exact (case-insensitive) match against id/display/aliases.
        var hit = defs.FirstOrDefault(d =>
            string.Equals(d.Id, clean, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.DisplayName, clean, StringComparison.OrdinalIgnoreCase)
            || d.Aliases.Any(a => string.Equals(a, clean, StringComparison.OrdinalIgnoreCase)));
        if (hit is not null)
        {
            return hit;
        }

        // 2. Stripped comparison ("18-A" vs alias "18A", "tv pg" vs "TVPG").
        var key = ComparisonKey(clean);
        if (key.Length == 0)
        {
            return null;
        }

        hit = defs.FirstOrDefault(d =>
            string.Equals(ComparisonKey(d.Id), key, StringComparison.Ordinal)
            || string.Equals(ComparisonKey(d.DisplayName), key, StringComparison.Ordinal)
            || d.Aliases.Any(a => string.Equals(ComparisonKey(a), key, StringComparison.Ordinal)));
        if (hit is not null)
        {
            return hit;
        }

        // 3. Country-prefixed values such as "US:PG-13", "CA-18A", "CA:R".
        // Resolve the inner part scoped to that rating system so e.g. "CA:R"
        // picks the Canadian R (18/1) instead of the US R (17/0).
        foreach (var prefix in _countryPrefixes)
        {
            foreach (var sep in new[] { ':', '-', ' ' })
            {
                var head = prefix + sep;
                if (clean.StartsWith(head, StringComparison.OrdinalIgnoreCase) && clean.Length > head.Length)
                {
                    var scoped = ResolveScoped(clean[head.Length..], prefix, defs);
                    if (scoped is not null)
                    {
                        return scoped;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Resolves an inner rating part restricted to a rating system.</summary>
    private RatingDefinition? ResolveScoped(string inner, string prefix, IReadOnlyList<RatingDefinition> defs)
    {
        var clean = Clean(inner);
        var key = ComparisonKey(inner);
        if (clean.Length == 0 || key.Length == 0)
        {
            return null;
        }

        return defs.FirstOrDefault(d => IsSystemMatch(d, prefix)
            && (string.Equals(d.Id, clean, StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.DisplayName, clean, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ComparisonKey(d.Id), key, StringComparison.Ordinal)
                || string.Equals(ComparisonKey(d.DisplayName), key, StringComparison.Ordinal)
                || d.Aliases.Any(a => string.Equals(a, clean, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(ComparisonKey(a), key, StringComparison.Ordinal))));
    }

    /// <summary>Resolves a raw string to a normalized rating id (or null).</summary>
    public string? ResolveId(string? raw) => Resolve(raw)?.Id;

    private static bool IsSystemMatch(RatingDefinition def, string countryPrefix)
    {
        if (def.IsUnrated)
        {
            return true;
        }

        return countryPrefix.ToUpperInvariant() switch
        {
            "US" or "USA" => string.Equals(def.System, "us", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(def.System, "common", StringComparison.OrdinalIgnoreCase),
            "CA" => string.Equals(def.System, "ca", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(def.System, "common", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }
}
