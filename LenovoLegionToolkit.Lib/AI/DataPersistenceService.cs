using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Data Persistence Service - Saves and loads learning data to disk
/// Ensures learned patterns and preferences survive app restarts
/// </summary>
public class DataPersistenceService
{
    private readonly string _dataDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    // File paths
    private string BehaviorHistoryPath => Path.Combine(_dataDirectory, "behavior_history.json");
    private string UserPreferencesPath => Path.Combine(_dataDirectory, "user_preferences.json");
    private string StatisticsPath => Path.Combine(_dataDirectory, "orchestrator_stats.json");
    private string BatteryHistoryPath => Path.Combine(_dataDirectory, "battery_history.json");

    public DataPersistenceService(string? customDataDirectory = null)
    {
        // Default to AppData\Local\LenovoLegionToolkit\AI
        _dataDirectory = customDataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LenovoLegionToolkit",
            "AI"
        );

        // Ensure directory exists
        Directory.CreateDirectory(_dataDirectory);

        // Configure JSON serialization
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Data persistence initialized: {_dataDirectory}");
    }

    #region Behavior History

    /// <summary>
    /// Save behavior history to disk
    /// </summary>
    public async Task SaveBehaviorHistoryAsync(IEnumerable<BehaviorDataPoint> history)
    {
        try
        {
            var json = JsonSerializer.Serialize(history, _jsonOptions);
            await File.WriteAllTextAsync(BehaviorHistoryPath, json).ConfigureAwait(false);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Saved behavior history to {BehaviorHistoryPath}");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to save behavior history", ex);
        }
    }

    /// <summary>
    /// Load behavior history from disk
    /// </summary>
    public async Task<List<BehaviorDataPoint>> LoadBehaviorHistoryAsync()
    {
        try
        {
            if (!File.Exists(BehaviorHistoryPath))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"No behavior history file found");
                return new List<BehaviorDataPoint>();
            }

            var json = await File.ReadAllTextAsync(BehaviorHistoryPath).ConfigureAwait(false);
            var history = JsonSerializer.Deserialize<List<BehaviorDataPoint>>(json, _jsonOptions);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Loaded {history?.Count ?? 0} behavior data points");

            return history ?? new List<BehaviorDataPoint>();
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to load behavior history", ex);
            return new List<BehaviorDataPoint>();
        }
    }

    #endregion

    #region User Preferences

    /// <summary>
    /// Save user preferences to disk
    /// </summary>
    public async Task SaveUserPreferencesAsync(UserPreferencesData preferences)
    {
        try
        {
            var json = JsonSerializer.Serialize(preferences, _jsonOptions);
            await File.WriteAllTextAsync(UserPreferencesPath, json).ConfigureAwait(false);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Saved user preferences to {UserPreferencesPath}");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to save user preferences", ex);
        }
    }

    /// <summary>
    /// Load user preferences from disk
    /// </summary>
    public async Task<UserPreferencesData?> LoadUserPreferencesAsync()
    {
        try
        {
            if (!File.Exists(UserPreferencesPath))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"No user preferences file found");
                return null;
            }

            var json = await File.ReadAllTextAsync(UserPreferencesPath).ConfigureAwait(false);
            var preferences = JsonSerializer.Deserialize<UserPreferencesData>(json, _jsonOptions);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Loaded user preferences: {preferences?.LearnedPreferences?.Count ?? 0} controls");

            return preferences;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to load user preferences", ex);
            return null;
        }
    }

    #endregion

    #region Statistics

    /// <summary>
    /// Save orchestrator statistics to disk
    /// </summary>
    public async Task SaveStatisticsAsync(PersistentStatistics stats)
    {
        try
        {
            var json = JsonSerializer.Serialize(stats, _jsonOptions);
            await File.WriteAllTextAsync(StatisticsPath, json).ConfigureAwait(false);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Saved statistics to {StatisticsPath}");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to save statistics", ex);
        }
    }

    /// <summary>
    /// Load orchestrator statistics from disk
    /// </summary>
    public async Task<PersistentStatistics?> LoadStatisticsAsync()
    {
        try
        {
            if (!File.Exists(StatisticsPath))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"No statistics file found");
                return null;
            }

            var json = await File.ReadAllTextAsync(StatisticsPath).ConfigureAwait(false);
            var stats = JsonSerializer.Deserialize<PersistentStatistics>(json, _jsonOptions);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Loaded statistics: {stats?.TotalCycles ?? 0} cycles");

            return stats;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to load statistics", ex);
            return null;
        }
    }

    #endregion

    #region Battery History

    /// <summary>
    /// Save battery history to disk
    /// </summary>
    public async Task SaveBatteryHistoryAsync(IEnumerable<BatteryStateSnapshot> history)
    {
        try
        {
            var json = JsonSerializer.Serialize(history, _jsonOptions);
            await File.WriteAllTextAsync(BatteryHistoryPath, json).ConfigureAwait(false);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Saved battery history to {BatteryHistoryPath}");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to save battery history", ex);
        }
    }

    /// <summary>
    /// Load battery history from disk
    /// </summary>
    public async Task<List<BatteryStateSnapshot>> LoadBatteryHistoryAsync()
    {
        try
        {
            if (!File.Exists(BatteryHistoryPath))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"No battery history file found");
                return new List<BatteryStateSnapshot>();
            }

            var json = await File.ReadAllTextAsync(BatteryHistoryPath).ConfigureAwait(false);
            var history = JsonSerializer.Deserialize<List<BatteryStateSnapshot>>(json, _jsonOptions);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Loaded {history?.Count ?? 0} battery snapshots");

            return history ?? new List<BatteryStateSnapshot>();
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to load battery history", ex);
            return new List<BatteryStateSnapshot>();
        }
    }

    #endregion

    #region Auto-save Timer

    /// <summary>
    /// Start periodic auto-save (every 5 minutes)
    /// </summary>
    public void StartAutoSave(
        Func<IEnumerable<BehaviorDataPoint>> getBehaviorHistory,
        Func<UserPreferencesData> getUserPreferences,
        Func<PersistentStatistics> getStatistics,
        Func<IEnumerable<BatteryStateSnapshot>> getBatteryHistory)
    {
        var timer = new global::System.Threading.Timer(async _ =>
        {
            try
            {
                // Save all data
                await SaveBehaviorHistoryAsync(getBehaviorHistory()).ConfigureAwait(false);
                await SaveUserPreferencesAsync(getUserPreferences()).ConfigureAwait(false);
                await SaveStatisticsAsync(getStatistics()).ConfigureAwait(false);
                await SaveBatteryHistoryAsync(getBatteryHistory()).ConfigureAwait(false);

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Auto-save completed");
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Auto-save failed", ex);
            }
        }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    #endregion

    #region Data Reset

    /// <summary>
    /// Clear all persisted data (for reset/debugging)
    /// </summary>
    public async Task ClearAllDataAsync()
    {
        try
        {
            if (File.Exists(BehaviorHistoryPath))
                File.Delete(BehaviorHistoryPath);

            if (File.Exists(UserPreferencesPath))
                File.Delete(UserPreferencesPath);

            if (File.Exists(StatisticsPath))
                File.Delete(StatisticsPath);

            if (File.Exists(BatteryHistoryPath))
                File.Delete(BatteryHistoryPath);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"All persisted data cleared");

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to clear data", ex);
        }
    }

    /// <summary>
    /// Get data directory path
    /// </summary>
    public string GetDataDirectory() => _dataDirectory;

    /// <summary>
    /// Get total size of persisted data
    /// </summary>
    public long GetDataSizeBytes()
    {
        long totalSize = 0;

        try
        {
            if (File.Exists(BehaviorHistoryPath))
                totalSize += new FileInfo(BehaviorHistoryPath).Length;

            if (File.Exists(UserPreferencesPath))
                totalSize += new FileInfo(UserPreferencesPath).Length;

            if (File.Exists(StatisticsPath))
                totalSize += new FileInfo(StatisticsPath).Length;

            if (File.Exists(BatteryHistoryPath))
                totalSize += new FileInfo(BatteryHistoryPath).Length;
        }
        catch
        {
            // Ignore errors
        }

        return totalSize;
    }

    #endregion
}

/// <summary>
/// User preferences data for serialization
/// </summary>
public class UserPreferencesData
{
    public List<UserOverrideEvent> OverrideHistory { get; set; } = new();
    public Dictionary<string, PreferenceLearning> LearnedPreferences { get; set; } = new();
}

/// <summary>
/// Persistent statistics data
/// </summary>
public class PersistentStatistics
{
    public long TotalCycles { get; set; }
    public long TotalActions { get; set; }
    public long TotalConflicts { get; set; }
    public DateTime FirstStart { get; set; }
    public DateTime LastUpdate { get; set; }
    public TimeSpan TotalUptime { get; set; }
}
