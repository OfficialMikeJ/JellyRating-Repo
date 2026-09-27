using System.Linq;
using Jellyfin.Plugin.ParentalRatingManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ParentalRatingManager.Tests;

public class RatingCatalogTests
{
    private static RatingCatalog CreateCatalog()
        => new(NullLogger<RatingCatalog>.Instance);

    [Fact]
    public void Catalog_LoadsEmbeddedDefinitions()
    {
        var catalog = CreateCatalog();
        var all = catalog.GetAll();
        Assert.True(all.Count >= 20);
        Assert.Contains(all, d => d.Id == "us-r");
        Assert.Contains(all, d => d.Id == "ca-18a");
        Assert.Contains(all, d => d.IsUnrated);
    }

    [Fact]
    public void Catalog_IdsAreUnique()
    {
        var catalog = CreateCatalog();
        var duplicates = catalog.GetAll()
            .GroupBy(d => d.Id.ToUpperInvariant())
            .Where(g => g.Count() > 1)
            .ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Catalog_EveryRatedEntryHasJellyfinValue()
    {
        var catalog = CreateCatalog();
        foreach (var def in catalog.GetAll().Where(d => !d.IsUnrated))
        {
            Assert.False(string.IsNullOrWhiteSpace(def.JellyfinValue));
            Assert.NotNull(def.Score);
        }
    }

    [Theory]
    [InlineData("us-r", 17, 0)]
    [InlineData("us-pg13", 13, 0)]
    [InlineData("us-tv-ma", 17, 1)]
    [InlineData("ca-18a", 18, 0)]
    [InlineData("ca-pg", 8, 1)]
    [InlineData("ca-r", 18, 1)]
    [InlineData("ca-a", 1000, 0)]
    public void GetById_ReturnsExpectedLevels(string id, int score, int subScore)
    {
        var catalog = CreateCatalog();
        var def = catalog.GetById(id);
        Assert.NotNull(def);
        Assert.Equal(score, def!.Score);
        Assert.Equal(subScore, def.SubScore);
    }

    [Fact]
    public void GetById_Unknown_ReturnsNull()
    {
        var catalog = CreateCatalog();
        Assert.Null(catalog.GetById("no-such-rating"));
        Assert.Null(catalog.GetById(null));
        Assert.Null(catalog.GetById(""));
    }

    [Fact]
    public void GetBySystem_GroupsUsAndCa()
    {
        var catalog = CreateCatalog();
        Assert.True(catalog.GetBySystem("us").Count >= 10);
        Assert.True(catalog.GetBySystem("ca").Count >= 8);
    }

    [Fact]
    public void Exceeds_HigherLevelBeatsLower()
    {
        var catalog = CreateCatalog();
        var r = catalog.GetById("us-r")!;      // 17/0
        var pg13 = catalog.GetById("us-pg13")!; // 13/0

        Assert.True(RatingCatalog.Exceeds(r, pg13));
        Assert.False(RatingCatalog.Exceeds(pg13, r));
        Assert.False(RatingCatalog.Exceeds(r, r)); // equal is NOT exceeding
    }

    [Fact]
    public void Exceeds_SubScoreBreaksTies()
    {
        var catalog = CreateCatalog();
        var ca14a = catalog.GetById("ca-14a")!;      // 14/0
        var ca14plus = catalog.GetById("ca-14plus")!; // 14/1

        Assert.True(RatingCatalog.Exceeds(ca14plus, ca14a));
        Assert.False(RatingCatalog.Exceeds(ca14a, ca14plus));
    }

    [Fact]
    public void Exceeds_CrossSystemComparesByScoreNotName()
    {
        var catalog = CreateCatalog();
        var caR = catalog.GetById("ca-r")!;   // 18/1
        var usR = catalog.GetById("us-r")!;   // 17/0

        // Same display name "R", different levels per the explicit policy table.
        Assert.True(RatingCatalog.Exceeds(caR, usR));
        Assert.False(RatingCatalog.Exceeds(usR, caR));
    }

    [Theory]
    // user below rating limit: content R(17) > max PG-13(13) → exceeds
    [InlineData("us-r", "us-pg13", true)]
    // user equal to rating limit: content R == max R → allowed
    [InlineData("us-r", "us-r", false)]
    // user above requirement: content PG-13 < max R → allowed
    [InlineData("us-pg13", "us-r", false)]
    public void Exceeds_MatchesUserLimitSemantics(string content, string max, bool expected)
    {
        var catalog = CreateCatalog();
        Assert.Equal(expected, RatingCatalog.Exceeds(catalog.GetById(content)!, catalog.GetById(max)!));
    }
}
