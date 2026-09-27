using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.ParentalRatingManager.Configuration;
using Jellyfin.Plugin.ParentalRatingManager.Models;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using PreferenceKind = Jellyfin.Database.Implementations.Enums.PreferenceKind;

namespace Jellyfin.Plugin.ParentalRatingManager.Services;

/// <summary>
/// Applies per-user maximum-rating policies by writing Jellyfin's native
/// restriction fields (<c>MaxParentalRatingScore</c>,
/// <c>MaxParentalRatingSubScore</c> and the BlockUnratedItems preference),
/// which Jellyfin enforces at the database query layer and in its in-memory
/// authorization checks for every client/API path.
/// </summary>
public sealed class UserRestrictionService
{
    /// <summary>All unrated item kinds blocked when the unrated policy requires it.</summary>
    private static readonly UnratedItem[] _allUnratedKinds = Enum.GetValues<UnratedItem>();

    private readonly IUserManager _userManager;
    private readonly PluginDataStore _store;
    private readonly RatingCatalog _catalog;
    private readonly ILogger<UserRestrictionService> _logger;

    public UserRestrictionService(
        IUserManager userManager,
        PluginDataStore store,
        RatingCatalog catalog,
        ILogger<UserRestrictionService> logger)
    {
        _userManager = userManager;
        _store = store;
        _catalog = catalog;
        _logger = logger;
    }

    /// <summary>
    /// Applies a maximum rating to each given user. Null/empty
    /// <paramref name="maxRatingId"/> removes the restriction (unrestricted).
    /// </summary>
    public async Task<BulkOperationResultDto> ApplyPolicyAsync(Guid[] userIds, string? maxRatingId, string? adminName)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        RatingDefinition? definition = null;
        if (!string.IsNullOrWhiteSpace(maxRatingId))
        {
            definition = _catalog.GetById(maxRatingId);
            if (definition is null)
            {
                throw new ArgumentException($"Unknown rating id '{maxRatingId}'", nameof(maxRatingId));
            }
        }

        var result = new BulkOperationResultDto();
        foreach (var userId in userIds)
        {
            try
            {
                await ApplyToUserAsync(userId, definition, adminName).ConfigureAwait(false);
                result.Results.Add(new ItemOperationResult { ItemId = userId, Success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply parental policy to user {UserId}", userId);
                result.Results.Add(new ItemOperationResult { ItemId = userId, Success = false, Error = ex.Message });
            }
        }

        result.Succeeded = result.Results.Count(r => r.Success);
        result.Failed = result.Results.Count(r => !r.Success);
        _logger.LogInformation(
            "User parental policy changed by {Admin}: max={Max} users={Count} succeeded={Succeeded} failed={Failed}",
            adminName ?? "unknown",
            maxRatingId ?? "(unrestricted)",
            userIds.Length,
            result.Succeeded,
            result.Failed);
        return result;
    }

    private async Task ApplyToUserAsync(Guid userId, RatingDefinition? definition, string? adminName)
    {
        var user = _userManager.GetUserById(userId)
            ?? throw new InvalidOperationException("User no longer exists.");

        if (definition is null)
        {
            user.MaxParentalRatingScore = null;
            user.MaxParentalRatingSubScore = null;
            user.SetPreference<UnratedItem>(PreferenceKind.BlockUnratedItems, []);
            _store.RemoveUserRestriction(userId);
        }
        else
        {
            user.MaxParentalRatingScore = definition.Score;
            user.MaxParentalRatingSubScore = definition.SubScore;
            user.SetPreference(PreferenceKind.BlockUnratedItems, ComputeUnratedBlocks(definition));
            _store.SetUserRestriction(new UserRestrictionRecord
            {
                UserId = userId,
                UserName = user.Username,
                MaxRatingId = definition.Id,
                LastModifiedUtc = DateTime.UtcNow
            });
        }

        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
        _logger.LogInformation(
            "Admin {Admin} set max parental rating for user {User} to {Rating}",
            adminName,
            user.Username,
            definition?.DisplayName ?? "unrestricted");
    }

    /// <summary>
    /// Computes which unrated item kinds must be blocked for a user with the
    /// given maximum, honouring the configured unrated policy.
    /// </summary>
    public UnratedItem[] ComputeUnratedBlocks(RatingDefinition userMax)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return ComputeUnratedBlocks(userMax, config.UnratedPolicy, _catalog.GetById(config.UnratedEquivalentRatingId));
    }

    /// <summary>Pure policy evaluation – explicit parameters, fully unit-testable.</summary>
    public static UnratedItem[] ComputeUnratedBlocks(RatingDefinition userMax, UnratedPolicyKind policy, RatingDefinition? equivalent)
    {
        switch (policy)
        {
            case UnratedPolicyKind.Allow:
                return [];

            case UnratedPolicyKind.Block:
                return _allUnratedKinds;

            case UnratedPolicyKind.TreatAsRating:
                // Fail safe: unknown/misconfigured equivalent rating → block.
                if (equivalent is null || equivalent.IsUnrated)
                {
                    return _allUnratedKinds;
                }

                // Unrated content counts as 'equivalent': block when it exceeds the user's max.
                return RatingCatalog.Exceeds(equivalent, userMax) ? _allUnratedKinds : [];

            default:
                return _allUnratedKinds;
        }
    }
}
