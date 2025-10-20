using System;
using System.Collections.Generic;
using System.Linq;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Manages cooling periods for user-initiated changes to prevent AI from overriding user preferences
/// </summary>
public class CoolingPeriodManager
{
    private readonly Dictionary<string, CoolingPeriod> _activeCoolingPeriods = new();
    private readonly object _lock = new();

    /// <summary>
    /// Check if a feature is currently in a cooling period
    /// </summary>
    /// <param name="featureKey">The feature identifier (e.g., "DISPLAY_REFRESH_RATE")</param>
    /// <param name="remaining">Time remaining in the cooling period</param>
    /// <returns>True if in cooling period, false otherwise</returns>
    public bool IsInCoolingPeriod(string featureKey, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;

        lock (_lock)
        {
            if (_activeCoolingPeriods.TryGetValue(featureKey, out var period))
            {
                if (period.IsActive)
                {
                    remaining = period.Remaining;
                    return true;
                }
                else
                {
                    // Expired - remove it
                    _activeCoolingPeriods.Remove(featureKey);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Record a user override with a cooling period based on scenario
    /// </summary>
    /// <param name="featureKey">The feature identifier</param>
    /// <param name="scenario">The user scenario determining cooling period duration</param>
    /// <param name="userValue">The value set by the user</param>
    public void RecordOverride(string featureKey, UserScenario scenario, object? userValue)
    {
        var duration = GetScenarioDuration(scenario);
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            _activeCoolingPeriods[featureKey] = new CoolingPeriod
            {
                FeatureKey = featureKey,
                Scenario = scenario,
                UserValue = userValue,
                StartTime = now,
                ExpiryTime = now.Add(duration)
            };
        }
    }

    /// <summary>
    /// Clear cooling period for a feature (user manually resumes AI control)
    /// </summary>
    /// <param name="featureKey">The feature identifier</param>
    public void ClearCoolingPeriod(string featureKey)
    {
        lock (_lock)
        {
            _activeCoolingPeriods.Remove(featureKey);
        }
    }

    /// <summary>
    /// Get all active cooling periods
    /// </summary>
    /// <returns>List of active cooling periods</returns>
    public List<CoolingPeriod> GetActiveCoolingPeriods()
    {
        lock (_lock)
        {
            // Remove expired periods and return active ones
            var expiredKeys = _activeCoolingPeriods
                .Where(kvp => !kvp.Value.IsActive)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _activeCoolingPeriods.Remove(key);
            }

            return _activeCoolingPeriods.Values.ToList();
        }
    }

    /// <summary>
    /// Get duration for a scenario
    /// </summary>
    private TimeSpan GetScenarioDuration(UserScenario scenario)
    {
        return scenario switch
        {
            UserScenario.VideoWatching => TimeSpan.FromMinutes(120),
            UserScenario.GamingSession => TimeSpan.FromMinutes(90),
            UserScenario.DevelopmentSession => TimeSpan.FromMinutes(60),
            UserScenario.OfficeWork => TimeSpan.FromMinutes(15),
            UserScenario.GeneralUse => TimeSpan.FromMinutes(30),
            _ => TimeSpan.FromMinutes(30)
        };
    }
}
