using System;
using System.IO;
using System.Text.Json;
using Jellyfin.Plugin.ParentalRatingManager.Models;
using Jellyfin.Plugin.ParentalRatingManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ParentalRatingManager.Tests;

public class PluginDataStoreTests : IDisposable
{
    private readonly string _dir;

    public PluginDataStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "prm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // best effort cleanup
        }
    }

    private PluginDataStore CreateStore()
        => new(_dir, NullLogger<PluginDataStore>.Instance);

    [Fact]
    public void Assignment_RoundTripsAcrossInstances()
    {
        var itemId = Guid.NewGuid();
        var store = CreateStore();
        store.SetAssignment(new RatingAssignment
        {
            ItemId = itemId,
            LibraryId = Guid.NewGuid(),
            RatingId = "us-r",
            Kind = RatingAssignmentKind.Movie,
            ItemName = "Test Movie",
            LastModifiedUtc = DateTime.UtcNow
        });

        // New instance = simulates a Jellyfin restart.
        var reloaded = CreateStore();
        Assert.True(reloaded.TryGetAssignment(itemId, out var assignment));
        Assert.Equal("us-r", assignment!.RatingId);
        Assert.Equal("Test Movie", assignment.ItemName);
    }

    [Fact]
    public void RemoveAssignment_DeletesRecord()
    {
        var itemId = Guid.NewGuid();
        var store = CreateStore();
        store.SetAssignment(new RatingAssignment { ItemId = itemId, RatingId = "us-pg" });

        Assert.True(store.RemoveAssignment(itemId));
        Assert.False(store.TryGetAssignment(itemId, out _));
        Assert.False(store.RemoveAssignment(itemId)); // removing twice is a no-op
    }

    [Fact]
    public void UserRestriction_RoundTrips()
    {
        var userId = Guid.NewGuid();
        var store = CreateStore();
        store.SetUserRestriction(new UserRestrictionRecord
        {
            UserId = userId,
            UserName = "kid",
            MaxRatingId = "ca-pg",
            LastModifiedUtc = DateTime.UtcNow
        });

        var reloaded = CreateStore();
        Assert.True(reloaded.TryGetUserRestriction(userId, out var record));
        Assert.Equal("ca-pg", record!.MaxRatingId);
    }

    [Fact]
    public void Prune_RemovesStaleItemsAndUsers()
    {
        var existing = Guid.NewGuid();
        var deletedItem = Guid.NewGuid();
        var existingUser = Guid.NewGuid();
        var deletedUser = Guid.NewGuid();

        var store = CreateStore();
        store.SetAssignment(new RatingAssignment { ItemId = existing, RatingId = "us-g" });
        store.SetAssignment(new RatingAssignment { ItemId = deletedItem, RatingId = "us-g" });
        store.SetUserRestriction(new UserRestrictionRecord { UserId = existingUser });
        store.SetUserRestriction(new UserRestrictionRecord { UserId = deletedUser });

        var removed = store.Prune(
            itemExists: id => id == existing,
            userExists: id => id == existingUser);

        Assert.Equal(2, removed);
        Assert.True(store.TryGetAssignment(existing, out _));
        Assert.False(store.TryGetAssignment(deletedItem, out _));
        Assert.True(store.TryGetUserRestriction(existingUser, out _));
        Assert.False(store.TryGetUserRestriction(deletedUser, out _));
    }

    [Fact]
    public void CorruptDataFile_StartsEmptyInsteadOfCrashing()
    {
        File.WriteAllText(Path.Combine(_dir, "data.json"), "{ this is not valid json !!!");
        var store = CreateStore();
        Assert.Empty(store.GetAllAssignments());
    }

    [Fact]
    public void DataFile_HasSchemaVersion()
    {
        var store = CreateStore();
        store.SetAssignment(new RatingAssignment { ItemId = Guid.NewGuid(), RatingId = "us-g" });

        var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "data.json")));
        Assert.Equal(PluginDataStore.CurrentSchemaVersion, json.RootElement.GetProperty("SchemaVersion").GetInt32());
    }

    [Fact]
    public void Save_DoesNotLeaveTempFile()
    {
        var store = CreateStore();
        store.SetAssignment(new RatingAssignment { ItemId = Guid.NewGuid(), RatingId = "us-g" });
        Assert.False(File.Exists(Path.Combine(_dir, "data.json.tmp")));
    }
}
