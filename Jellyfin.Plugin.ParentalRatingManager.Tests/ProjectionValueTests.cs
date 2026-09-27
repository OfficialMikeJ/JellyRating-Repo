using Jellyfin.Plugin.ParentalRatingManager.Models;
using Jellyfin.Plugin.ParentalRatingManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ParentalRatingManager.Tests;

/// <summary>
/// Tests for the CustomRating projection selector: the value written to
/// Jellyfin must resolve (via Jellyfin's own rating engine) to exactly the
/// intended score/sub-score, or be replaced by a guaranteed-parseable
/// fail-safe substitute — an unresolvable value would silently degrade the
/// item to "unrated", which is fail-UNSAFE for parental controls.
/// </summary>
public class ProjectionValueTests
{
    private static RatingCatalog CreateCatalog()
        => new(NullLogger<RatingCatalog>.Instance);

    private static RatingDefinition Def(string id, int? score, int? subScore, string jv, bool unrated = false)
        => new()
        {
            Id = id,
            DisplayName = id,
            System = "custom",
            Score = score,
            SubScore = subScore,
            JellyfinValue = jv,
            IsUnrated = unrated
        };

    [Fact]
    public void ExactResolvingValue_IsUsedVerbatim()
    {
        var catalog = CreateCatalog();
        var def = catalog.GetById("us-r")!; // jellyfinValue "US:R", intended 17/0

        var chosen = RatingAssignmentService.SelectJellyfinValue(
            def, catalog.GetAll(), v => v == "US:R" ? (17, 0) : null);

        Assert.Equal("US:R", chosen);
    }

    [Fact]
    public void UnresolvableValue_FallsBackToNumericScore()
    {
        var catalog = CreateCatalog();
        var def = Def("custom-x", 25, 0, "TotallyFake");

        var chosen = RatingAssignmentService.SelectJellyfinValue(
            def, catalog.GetAll(), _ => null);

        // "25" is always parseable by Jellyfin (numeric score) -> exact level.
        Assert.Equal("25", chosen);
    }

    [Fact]
    public void UnresolvableValue_WithSubScore_FailsSafeOneHigher()
    {
        var catalog = CreateCatalog();
        var def = Def("custom-x", 18, 1, "TotallyFake");

        var chosen = RatingAssignmentService.SelectJellyfinValue(
            def, catalog.GetAll(), _ => null);

        // (18,1) can't be expressed numerically -> "19" is strictly stricter.
        Assert.Equal("19", chosen);
    }

    [Fact]
    public void ValueResolvingToLowerLevel_IsReplaced()
    {
        var catalog = CreateCatalog();
        // Admin pointed the value at PG (10) but the intended level is 18.
        var def = Def("custom-x", 18, 0, "US:PG");

        var chosen = RatingAssignmentService.SelectJellyfinValue(
            def, catalog.GetAll(), v => v == "US:PG" ? (10, 0) : v == "18" ? (18, 0) : null);

        Assert.Equal("18", chosen);
    }

    [Fact]
    public void SameLevelCatalogueValue_PreferredOverNumericFallback()
    {
        var catalog = CreateCatalog();
        // Intended 18/0 with a broken jellyfinValue; ca-18a (18/0) resolves correctly.
        var def = Def("custom-x", 18, 0, "Bogus");

        var chosen = RatingAssignmentService.SelectJellyfinValue(
            def,
            catalog.GetAll(),
            v => v == "CA:18A" ? (18, 0) : v == "18" ? (18, 0) : null);

        Assert.Equal("CA:18A", chosen);
    }

    [Fact]
    public void ExplicitUnrated_UsesMarkerValue()
    {
        var catalog = CreateCatalog();
        var def = catalog.GetById(catalog.UnratedId)!;

        var chosen = RatingAssignmentService.SelectJellyfinValue(
            def, catalog.GetAll(), _ => null);

        Assert.Equal("NR", chosen); // Jellyfin's own unrated marker
    }

    [Fact]
    public void AllBuiltinCatalogValues_MustRoundTrip()
    {
        // Safety net: every built-in rating's jellyfinValue must resolve to its
        // intended level in a faithful mini-replica of Jellyfin's resolution
        // (prefix-stripped dictionary lookup over the built-in us/ca tables).
        var catalog = CreateCatalog();
        var us = new System.Collections.Generic.Dictionary<string, (int, int)>
        {
            ["G"] = (0, 0), ["PG"] = (10, 0), ["PG-13"] = (13, 0), ["R"] = (17, 0),
            ["NC-17"] = (17, 1), ["TV-Y"] = (0, 0), ["TV-Y7"] = (7, 0),
            ["TV-Y7-FV"] = (7, 1), ["TV-G"] = (0, 0), ["TV-PG"] = (10, 0),
            ["TV-14"] = (14, 0), ["TV-MA"] = (17, 1)
        };
        var ca = new System.Collections.Generic.Dictionary<string, (int, int)>
        {
            ["E"] = (0, 0), ["G"] = (0, 0), ["C"] = (0, 0), ["C8"] = (8, 0),
            ["PG"] = (8, 1), ["14A"] = (14, 0), ["14+"] = (14, 1), ["16+"] = (16, 0),
            ["18A"] = (18, 0), ["18+"] = (18, 1), ["R"] = (18, 1), ["A"] = (1000, 0)
        };

        (int, int)? Resolve(string v)
        {
            if (v.Equals("NR", System.StringComparison.OrdinalIgnoreCase)) return null;
            var sep = v.IndexOf(':');
            if (sep > 0)
            {
                var sys = v[..sep].ToUpperInvariant();
                var inner = v[(sep + 1)..].Trim();
                var dict = sys == "US" ? us : sys == "CA" ? ca : null;
                if (dict is not null && dict.TryGetValue(inner, out var s)) return s;
                return null;
            }
            if (us.TryGetValue(v, out var usv)) return usv;
            if (ca.TryGetValue(v, out var cav)) return cav;
            return null;
        }

        foreach (var def in catalog.GetAll())
        {
            var chosen = RatingAssignmentService.SelectJellyfinValue(def, catalog.GetAll(), Resolve);
            if (def.IsUnrated)
            {
                Assert.Null(Resolve(chosen));
            }
            else
            {
                var level = Resolve(chosen);
                Assert.NotNull(level);
                // Resolved level must never be weaker than intended.
                Assert.True(level!.Value.Item1 >= def.Score,
                    $"{def.Id} projected '{chosen}' resolves to {level}, below intended {def.Score}/{def.SubScore}");
            }
        }
    }
}
