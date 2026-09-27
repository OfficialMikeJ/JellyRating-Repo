using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.ParentalRatingManager.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ParentalRatingManager.Services;

/// <summary>
/// Versioned JSON data store for plugin-managed rating assignments and user
/// restrictions. Stored inside the plugin data folder so it survives Jellyfin
/// restarts, library rescans and plugin upgrades. Never touches media files.
/// </summary>
public sealed class PluginDataStore
{
    /// <summary>Current on-disk schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly ILogger<PluginDataStore> _logger;
    private readonly string _filePath;
    private readonly object _lock = new();
    private PluginData? _data;

    public PluginDataStore(IApplicationPaths applicationPaths, ILogger<PluginDataStore> logger)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
        _logger = logger;
        var dir = Path.Combine(applicationPaths.PluginsPath, "Jellyfin.Plugin.ParentalRatingManager");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "data.json");
    }

    /// <summary>Constructor with an explicit data directory (used by unit tests).</summary>
    public PluginDataStore(string dataDirectory, ILogger<PluginDataStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _logger = logger;
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "data.json");
    }

    /// <summary>Path of the backing file (shown on the settings page).</summary>
    public string FilePath => _filePath;

    private PluginData Data
    {
        get
        {
            lock (_lock)
            {
                _data ??= Load();
                return _data;
            }
        }
    }

    /// <summary>All rating assignments.</summary>
    public IReadOnlyList<RatingAssignment> GetAllAssignments()
    {
        lock (_lock)
        {
            return Data.Assignments.Values.ToList();
        }
    }

    /// <summary>All user restriction records.</summary>
    public IReadOnlyList<UserRestrictionRecord> GetAllUserRestrictions()
    {
        lock (_lock)
        {
            return Data.UserRestrictions.Values.ToList();
        }
    }

    public bool TryGetAssignment(Guid itemId, out RatingAssignment? assignment)
    {
        lock (_lock)
        {
            return Data.Assignments.TryGetValue(itemId, out assignment);
        }
    }

    public void SetAssignment(RatingAssignment assignment)
    {
        lock (_lock)
        {
            Data.Assignments[assignment.ItemId] = assignment;
            Save();
        }
    }

    public bool RemoveAssignment(Guid itemId)
    {
        lock (_lock)
        {
            var removed = Data.Assignments.Remove(itemId);
            if (removed)
            {
                Save();
            }

            return removed;
        }
    }

    public bool TryGetUserRestriction(Guid userId, out UserRestrictionRecord? record)
    {
        lock (_lock)
        {
            return Data.UserRestrictions.TryGetValue(userId, out record);
        }
    }

    public void SetUserRestriction(UserRestrictionRecord record)
    {
        lock (_lock)
        {
            Data.UserRestrictions[record.UserId] = record;
            Save();
        }
    }

    public bool RemoveUserRestriction(Guid userId)
    {
        lock (_lock)
        {
            var removed = Data.UserRestrictions.Remove(userId);
            if (removed)
            {
                Save();
            }

            return removed;
        }
    }

    /// <summary>
    /// Drops assignments whose items no longer exist and restriction records
    /// whose users no longer exist.
    /// </summary>
    public int Prune(Func<Guid, bool> itemExists, Func<Guid, bool> userExists)
    {
        lock (_lock)
        {
            var removedItems = Data.Assignments.Keys.Where(id => !itemExists(id)).ToList();
            var removedUsers = Data.UserRestrictions.Keys.Where(id => !userExists(id)).ToList();
            foreach (var id in removedItems)
            {
                Data.Assignments.Remove(id);
            }

            foreach (var id in removedUsers)
            {
                Data.UserRestrictions.Remove(id);
            }

            if (removedItems.Count + removedUsers.Count > 0)
            {
                Save();
                _logger.LogInformation(
                    "Pruned {ItemCount} stale rating assignments and {UserCount} stale user restrictions",
                    removedItems.Count,
                    removedUsers.Count);
            }

            return removedItems.Count + removedUsers.Count;
        }
    }

    private PluginData Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var data = JsonSerializer.Deserialize<PluginData>(File.ReadAllText(_filePath));
                if (data is not null)
                {
                    if (data.SchemaVersion > CurrentSchemaVersion)
                    {
                        _logger.LogWarning(
                            "Plugin data file schema version {FileVersion} is newer than supported {Supported}; continuing best-effort",
                            data.SchemaVersion,
                            CurrentSchemaVersion);
                    }

                    data.SchemaVersion = CurrentSchemaVersion;
                    return data;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read plugin data store at {Path}; starting with an empty store", _filePath);
        }

        return new PluginData { SchemaVersion = CurrentSchemaVersion };
    }

    private void Save()
    {
        try
        {
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Data, _jsonOptions));
            File.Move(tmp, _filePath, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist plugin data store at {Path}", _filePath);
            throw;
        }
    }

    /// <summary>On-disk root object.</summary>
    private sealed class PluginData
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public Dictionary<Guid, RatingAssignment> Assignments { get; set; } = new();

        public Dictionary<Guid, UserRestrictionRecord> UserRestrictions { get; set; } = new();
    }
}
