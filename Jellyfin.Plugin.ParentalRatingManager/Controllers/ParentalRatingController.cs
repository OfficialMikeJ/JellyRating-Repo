using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Plugin.ParentalRatingManager.Configuration;
using Jellyfin.Plugin.ParentalRatingManager.Models;
using Jellyfin.Plugin.ParentalRatingManager.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PermissionKind = Jellyfin.Database.Implementations.Enums.PermissionKind;

namespace Jellyfin.Plugin.ParentalRatingManager.Controllers;

/// <summary>
/// Administration API for the Parental Rating Manager plugin.
/// Every endpoint requires an authenticated Jellyfin administrator
/// (server-side authorization – never trust client-supplied identity).
/// </summary>
[ApiController]
[Route("ParentalRatingManager")]
[Authorize(Policy = Policies.RequiresElevation)]
[Produces(MediaTypeNames.Application.Json)]
public class ParentalRatingController : ControllerBase
{
    private readonly LibraryContentService _library;
    private readonly RatingAssignmentService _assignments;
    private readonly UserRestrictionService _restrictions;
    private readonly EffectiveRatingService _effective;
    private readonly RatingCatalog _catalog;
    private readonly PluginDataStore _store;
    private readonly IUserManager _userManager;
    private readonly IAuthorizationContext _authContext;
    private readonly ILogger<ParentalRatingController> _logger;

    public ParentalRatingController(
        LibraryContentService library,
        RatingAssignmentService assignments,
        UserRestrictionService restrictions,
        EffectiveRatingService effective,
        RatingCatalog catalog,
        PluginDataStore store,
        IUserManager userManager,
        IAuthorizationContext authContext,
        ILogger<ParentalRatingController> logger)
    {
        _library = library;
        _assignments = assignments;
        _restrictions = restrictions;
        _effective = effective;
        _catalog = catalog;
        _store = store;
        _userManager = userManager;
        _authContext = authContext;
        _logger = logger;
    }

    private async Task<string?> AdminNameAsync()
    {
        var info = await _authContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
        return info?.User?.Username;
    }

    /// <summary>List all Jellyfin libraries.</summary>
    [HttpGet("libraries")]
    public ActionResult<IReadOnlyList<LibraryDto>> GetLibraries()
        => Ok(_library.GetLibraries());

    /// <summary>Browse a library's rateable items.</summary>
    [HttpGet("libraries/{libraryId}/items")]
    public ActionResult<BrowseResultDto> BrowseLibrary(
        [FromRoute] Guid libraryId,
        [FromQuery] string? search = null,
        [FromQuery] string? filter = null,
        [FromQuery] string? ratingId = null,
        [FromQuery] string? kind = null,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 100)
    {
        if (!Enum.TryParse<LibraryContentService.BrowseFilter>(filter ?? "All", true, out var browseFilter))
        {
            return BadRequest($"Unknown filter '{filter}'.");
        }

        try
        {
            return Ok(_library.Browse(libraryId, search, browseFilter, ratingId, kind, page, pageSize));
        }
        catch (ArgumentException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>Seasons belonging to a series.</summary>
    [HttpGet("items/{itemId}/seasons")]
    public ActionResult<IReadOnlyList<MediaItemDto>> GetSeasons([FromRoute] Guid itemId)
    {
        try
        {
            return Ok(_library.GetSeasons(itemId));
        }
        catch (ArgumentException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>Effective rating for one item (including source detail).</summary>
    [HttpGet("items/{itemId}")]
    public ActionResult<object> GetItem([FromRoute] Guid itemId)
    {
        var item = _library.GetItem(itemId);
        if (item is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            item.Id,
            item.Name,
            OfficialRating = item.OfficialRating,
            CustomRating = item.CustomRating,
            Effective = _effective.Resolve(item),
            ManualOverride = _store.TryGetAssignment(itemId, out var a) ? a : null
        });
    }

    /// <summary>Assign a rating to one or more items (or remove overrides).</summary>
    [HttpPost("items/assign")]
    public async Task<ActionResult<BulkOperationResultDto>> Assign([FromBody] AssignRatingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ItemIds is null || request.ItemIds.Length == 0)
        {
            return BadRequest("At least one item id is required.");
        }

        if (request.ItemIds.Length > 5000)
        {
            return BadRequest("Too many items in a single operation.");
        }

        if (!string.IsNullOrWhiteSpace(request.RatingId) && !_catalog.IsKnownId(request.RatingId))
        {
            return BadRequest($"Unknown rating id '{request.RatingId}'.");
        }

        var admin = await AdminNameAsync().ConfigureAwait(false);
        var result = await _assignments.AssignAsync(request.ItemIds, request.RatingId, admin).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>All Jellyfin users with their current plugin policy.</summary>
    [HttpGet("users")]
    public ActionResult<IReadOnlyList<ManagedUserDto>> GetUsers()
    {
        var users = _userManager.GetUsers()
            .Select(u =>
            {
                _store.TryGetUserRestriction(u.Id, out var record);
                var def = _catalog.GetById(record?.MaxRatingId);
                return new ManagedUserDto
                {
                    Id = u.Id,
                    Name = u.Username,
                    IsAdministrator = u.HasPermission(PermissionKind.IsAdministrator),
                    IsManaged = record is not null,
                    MaxRatingId = def?.Id,
                    MaxRatingDisplayName = def?.DisplayName,
                    NativeMaxScore = u.MaxParentalRatingScore,
                    NativeMaxSubScore = u.MaxParentalRatingSubScore
                };
            })
            .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Ok(users);
    }

    /// <summary>Set the maximum permitted rating for one or more users.</summary>
    [HttpPost("users/policy")]
    public async Task<ActionResult<BulkOperationResultDto>> SetUserPolicy([FromBody] SetUserPolicyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.UserIds is null || request.UserIds.Length == 0)
        {
            return BadRequest("At least one user id is required.");
        }

        if (!string.IsNullOrWhiteSpace(request.MaxRatingId) && !_catalog.IsKnownId(request.MaxRatingId))
        {
            return BadRequest($"Unknown rating id '{request.MaxRatingId}'.");
        }

        var admin = await AdminNameAsync().ConfigureAwait(false);
        var result = await _restrictions.ApplyPolicyAsync(request.UserIds, request.MaxRatingId, admin).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>The full normalized rating catalogue (US, CA and custom).</summary>
    [HttpGet("ratings")]
    public ActionResult<IReadOnlyList<RatingDto>> GetRatings()
        => Ok(_catalog.GetAll()
            .Select(d => new RatingDto { Id = d.Id, DisplayName = d.DisplayName, System = d.System, IsUnrated = d.IsUnrated })
            .ToList());

    /// <summary>Current plugin configuration.</summary>
    [HttpGet("config")]
    public ActionResult<PluginConfigDto> GetConfig()
    {
        var c = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return Ok(new PluginConfigDto
        {
            UnratedPolicy = c.UnratedPolicy,
            UnratedEquivalentRatingId = c.UnratedEquivalentRatingId,
            EnableDebugLogging = c.EnableDebugLogging,
            BulkConfirmationThreshold = c.BulkConfirmationThreshold,
            CustomRatings = c.CustomRatings
        });
    }

    /// <summary>Update plugin configuration.</summary>
    [HttpPut("config")]
    public ActionResult UpdateConfig([FromBody] PluginConfigDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Plugin instance unavailable.");
        }

        if (!Enum.IsDefined(dto.UnratedPolicy))
        {
            return BadRequest("Invalid unrated policy.");
        }

        var duplicate = dto.CustomRatings
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1 || string.IsNullOrWhiteSpace(g.Key));
        if (duplicate is not null)
        {
            return BadRequest("Custom rating ids must be non-empty and unique.");
        }

        var config = plugin.Configuration;
        config.UnratedPolicy = dto.UnratedPolicy;
        config.UnratedEquivalentRatingId = dto.UnratedEquivalentRatingId ?? "us-r";
        config.EnableDebugLogging = dto.EnableDebugLogging;
        config.BulkConfirmationThreshold = Math.Clamp(dto.BulkConfirmationThreshold, 1, 5000);
        config.CustomRatings = dto.CustomRatings ?? new List<CustomRatingDefinition>();
        plugin.SaveConfiguration();
        _logger.LogInformation("Parental Rating Manager configuration updated");
        return NoContent();
    }

    /// <summary>Dashboard statistics.</summary>
    [HttpGet("stats")]
    public ActionResult<StatsDto> GetStats()
        => Ok(_library.GetStats());

    /// <summary>Drop store records whose items/users no longer exist.</summary>
    [HttpPost("maintenance/prune")]
    public ActionResult<object> Prune()
    {
        var removed = _library.PruneStaleRecords(id => _userManager.GetUserById(id) is not null);
        return Ok(new { removed });
    }
}
