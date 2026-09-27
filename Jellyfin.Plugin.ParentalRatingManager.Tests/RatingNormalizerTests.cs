using Jellyfin.Plugin.ParentalRatingManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ParentalRatingManager.Tests;

public class RatingNormalizerTests
{
    private static RatingNormalizer CreateNormalizer()
        => new(new RatingCatalog(NullLogger<RatingCatalog>.Instance));

    [Theory]
    [InlineData("PG", "us-pg")]
    [InlineData("PG ", "us-pg")]
    [InlineData("  PG  ", "us-pg")]
    [InlineData("pg", "us-pg")]
    [InlineData("Rated PG", "us-pg")]
    [InlineData("Parental Guidance", "us-pg")]
    [InlineData("Rated R", "us-r")]
    [InlineData("R", "us-r")]
    [InlineData("r", "us-r")]
    [InlineData("Rated: R", "us-r")]
    [InlineData("PG-13", "us-pg13")]
    [InlineData("PG13", "us-pg13")]
    [InlineData("PG 13", "us-pg13")]
    [InlineData("NC-17", "us-nc17")]
    [InlineData("TV-MA", "us-tv-ma")]
    [InlineData("TVMA", "us-tv-ma")]
    [InlineData("TV-14", "us-tv-14")]
    public void Resolve_CommonUsRatings(string input, string expectedId)
    {
        var normalizer = CreateNormalizer();
        Assert.Equal(expectedId, normalizer.Resolve(input)?.Id);
    }

    [Theory]
    [InlineData("18-A", "ca-18a")]
    [InlineData("18A", "ca-18a")]
    [InlineData("18 A", "ca-18a")]
    [InlineData("14-A", "ca-14a")]
    [InlineData("14A", "ca-14a")]
    [InlineData("14+", "ca-14plus")]
    [InlineData("16+", "ca-16plus")]
    [InlineData("18+", "ca-18plus")]
    [InlineData("C8", "ca-c8")]
    [InlineData("C-8", "ca-c8")]
    [InlineData("CA:18A", "ca-18a")]
    [InlineData("CA-14A", "ca-14a")]
    [InlineData("CA:R", "ca-r")]
    [InlineData("Exempt", "ca-e")]
    public void Resolve_CanadianRatings(string input, string expectedId)
    {
        var normalizer = CreateNormalizer();
        Assert.Equal(expectedId, normalizer.Resolve(input)?.Id);
    }

    [Fact]
    public void Resolve_CaScopedR_PicksCanadianNotUs()
    {
        var normalizer = CreateNormalizer();
        var def = normalizer.Resolve("CA:R");
        Assert.NotNull(def);
        Assert.Equal("ca", def!.System);
        Assert.Equal(18, def.Score);
        Assert.Equal(1, def.SubScore);
    }

    [Fact]
    public void Resolve_UsScopedR_PicksUsNotCanadian()
    {
        var normalizer = CreateNormalizer();
        var def = normalizer.Resolve("US:R");
        Assert.Equal("us-r", def?.Id);
    }

    [Theory]
    [InlineData("PG&#x20;")]   // HTML-encoded trailing space
    [InlineData("PG&nbsp;")]   // non-breaking space entity
    [InlineData("PG&#32;")]
    public void Resolve_HtmlEntityWhitespace(string input)
    {
        var normalizer = CreateNormalizer();
        Assert.Equal("us-pg", normalizer.Resolve(input)?.Id);
    }

    [Theory]
    [InlineData("Unrated")]
    [InlineData("Not Rated")]
    [InlineData("NR")]
    [InlineData("UR")]
    [InlineData("N/A")]
    public void Resolve_UnratedMarkers(string input)
    {
        var normalizer = CreateNormalizer();
        var def = normalizer.Resolve(input);
        Assert.NotNull(def);
        Assert.True(def!.IsUnrated);
        Assert.Null(def.Score);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("DefinitelyNotARating123")]
    [InlineData("18-XYZ")]
    public void Resolve_UnknownOrEmpty_ReturnsNull(string? input)
    {
        var normalizer = CreateNormalizer();
        Assert.Null(normalizer.Resolve(input));
    }

    [Theory]
    [InlineData("Rated R", "R")]
    [InlineData("  Rated  PG-13 ", "PG-13")]
    [InlineData("18-A ", "18-A")]
    public void Clean_StripsPrefixAndWhitespace(string input, string expected)
    {
        Assert.Equal(expected, RatingNormalizer.Clean(input));
    }

    [Theory]
    [InlineData("18-A", "18A")]
    [InlineData("tv pg", "TVPG")]
    [InlineData("PG 13", "PG13")]
    public void ComparisonKey_StripsSeparators(string input, string expected)
    {
        Assert.Equal(expected, RatingNormalizer.ComparisonKey(input));
    }
}
