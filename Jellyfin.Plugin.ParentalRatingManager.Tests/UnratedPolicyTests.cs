using System;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.ParentalRatingManager.Configuration;
using Jellyfin.Plugin.ParentalRatingManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ParentalRatingManager.Tests;

public class UnratedPolicyTests
{
    private static RatingCatalog CreateCatalog()
        => new(NullLogger<RatingCatalog>.Instance);

    [Fact]
    public void Allow_NeverBlocksUnrated()
    {
        var catalog = CreateCatalog();
        var max = catalog.GetById("us-pg")!;
        Assert.Empty(UserRestrictionService.ComputeUnratedBlocks(max, UnratedPolicyKind.Allow, null));
    }

    [Fact]
    public void Block_BlocksAllUnratedKinds()
    {
        var catalog = CreateCatalog();
        var max = catalog.GetById("us-pg")!;
        var blocks = UserRestrictionService.ComputeUnratedBlocks(max, UnratedPolicyKind.Block, null);
        Assert.Equal(Enum.GetValues<UnratedItem>().Length, blocks.Length);
        Assert.Contains(UnratedItem.Movie, blocks);
        Assert.Contains(UnratedItem.Series, blocks);
    }

    [Fact]
    public void TreatAsRating_BlocksWhenEquivalentExceedsMax()
    {
        var catalog = CreateCatalog();
        var userMax = catalog.GetById("us-pg13")!;   // 13
        var equivalent = catalog.GetById("us-r")!;   // 17 > 13

        var blocks = UserRestrictionService.ComputeUnratedBlocks(userMax, UnratedPolicyKind.TreatAsRating, equivalent);
        Assert.NotEmpty(blocks);
    }

    [Fact]
    public void TreatAsRating_AllowsWhenEquivalentWithinMax()
    {
        var catalog = CreateCatalog();
        var userMax = catalog.GetById("us-r")!;      // 17
        var equivalent = catalog.GetById("us-pg")!;  // 10 < 17

        var blocks = UserRestrictionService.ComputeUnratedBlocks(userMax, UnratedPolicyKind.TreatAsRating, equivalent);
        Assert.Empty(blocks);
    }

    [Fact]
    public void TreatAsRating_UnknownEquivalent_FailsSafeToBlock()
    {
        var catalog = CreateCatalog();
        var userMax = catalog.GetById("us-r")!;
        var blocks = UserRestrictionService.ComputeUnratedBlocks(userMax, UnratedPolicyKind.TreatAsRating, null);
        Assert.NotEmpty(blocks);
    }

    [Fact]
    public void TreatAsRating_UnratedEquivalent_FailsSafeToBlock()
    {
        var catalog = CreateCatalog();
        var userMax = catalog.GetById("us-r")!;
        var unratedDef = catalog.GetById(catalog.UnratedId)!;
        var blocks = UserRestrictionService.ComputeUnratedBlocks(userMax, UnratedPolicyKind.TreatAsRating, unratedDef);
        Assert.NotEmpty(blocks);
    }
}
