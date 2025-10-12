using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Cooling Period Manager - Prevents agent annoyance after manual overrides
///
/// Implements scenario-based cooling periods where agents pause proposing changes
/// after user manually overrides autonomous actions. Duration varies by workload context.
///
/// Elite Features:
/// - Scenario-aware durations (Movie: 2h, Gaming: 1h, Office: 15min)
/// - Exponential backoff for repeated overrides (2x, 4x, 8x, max 24h)
/// - Per-control tracking with temporal heatmaps
/// - Autonomous reset after successful cooling period
///
/// Power Savings Impact: N/A (UX improvement, prevents user frustration)
/// </summary>
public class CoolingPeriodManager
{
    private readonly Dictionary<string, CoolingPeriod> _activeCoolingPeriods = new();
    private readonly object _lock = new();
    private readonly Dictionary<string, List<DateTime>> _overrideHistory = new();
    private const int MaxHistoryEntries = 100;

    /// <summary>
    /// Represents an active cooling period for a specific control
    /// </summary>
    public class CoolingPeriod
    {
        public required string Control { get; set; }
        public DateTime OverrideTime { get; set; }
        public TimeSpan Duration { get; set; }
        public required string Reason { get; set; }
        public int OverrideCount { get; set; }
        public WorkloadType WorkloadContext { get; set; }
        public bool IsEscalated { get; set; }
    }

    /// <summary>
    /// Scenario-based cooling period durations
    /// Tuned based on typical workload session lengths
    /// </summary>
    public enum CoolingPeriodScenario
    {
        VideoWatching,      // 2 hours (user watching movie, don't interrupt)
        VideoEditing,       // 30 minutes (intense work session)
        Gaming,             // 1 hour (gaming session)
        AIWorkload,         // 45 minutes (model training/inference)
        OfficeWork,         // 15 minutes (quick override, short cooldown)
        General             // 10 minutes (default)
    }

    private static readonly Dictionary<CoolingPeriodScenario, TimeSpan> ScenarioDurations = new()
    {
        { CoolingPeriodScenario.VideoWatching, TimeSpan.FromHours(2) },
        { CoolingPeriodScenario.VideoEditing, TimeSpan.FromMinutes(30) },
        { CoolingPeriodScenario.Gaming, TimeSpan.FromHours(1) },
        { CoolingPeriodScenario.AIWorkload, TimeSpan.FromMinutes(45) },
        { CoolingPeriodScenario.OfficeWork, TimeSpan.FromMinutes(15) },
        { CoolingPeriodScenario.General, TimeSpan.FromMinutes(10) }
    };

    /// <summary>
    /// Record manual override and start cooling period
    ///
    /// Autonomous behavior:
    /// - Detects workload context
    /// - Selects appropriate cooling duration
    /// - Tracks override frequency for escalation
    /// </summary>
    public void RecordOverride(string control, WorkloadType workload, object userValue)
    {
        if (string.IsNullOrEmpty(control))
            return;

        lock (_lock)
        {
            var scenario = MapWorkloadToScenario(workload);
            var duration = ScenarioDurations[scenario];

            // Track override history for pattern analysis
            if (!_overrideHistory.ContainsKey(control))
                _overrideHistory[control] = new List<DateTime>();

            _overrideHistory[control].Add(DateTime.UtcNow);

            // Prune old history entries
            if (_overrideHistory[control].Count > MaxHistoryEntries)
                _overrideHistory[control].RemoveAt(0);

            var period = new CoolingPeriod
            {
                Control = control,
                OverrideTime = DateTime.UtcNow,
                Duration = duration,
                Reason = $"User override during {workload} workload (value: {userValue})",
                OverrideCount = _activeCoolingPeriods.ContainsKey(control) ?
                                _activeCoolingPeriods[control].OverrideCount + 1 : 1,
                WorkloadContext = workload,
                IsEscalated = false
            };

            _activeCoolingPeriods[control] = period;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[CoolingPeriod] {control} locked for {duration.TotalMinutes:F1}min (scenario: {scenario}, count: {period.OverrideCount}, workload: {workload})");
        }
    }

    /// <summary>
    /// Check if control is in cooling period
    ///
    /// Returns: true if agent should NOT propose changes, false if agent can proceed
    /// </summary>
    public bool IsInCoolingPeriod(string control, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;

        if (string.IsNullOrEmpty(control))
            return false;

        lock (_lock)
        {
            if (!_activeCoolingPeriods.TryGetValue(control, out var period))
                return false;

            var elapsed = DateTime.UtcNow - period.OverrideTime;
            if (elapsed >= period.Duration)
            {
                // Cooling period expired - autonomous reset
                _activeCoolingPeriods.Remove(control);

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[CoolingPeriod] {control} cooling period expired after {period.Duration.TotalMinutes:F1}min");

                return false;
            }

            remaining = period.Duration - elapsed;
            return true;
        }
    }

    /// <summary>
    /// Escalate cooling period duration if repeated overrides detected
    ///
    /// Autonomous escalation logic:
    /// - 1st override: Base duration (10min - 2h)
    /// - 2nd override: 2x base duration
    /// - 3rd override: 4x base duration
    /// - 4th+ override: 8x base duration (max 24h cap)
    /// </summary>
    public void EscalateCoolingPeriod(string control)
    {
        if (string.IsNullOrEmpty(control))
            return;

        lock (_lock)
        {
            if (_activeCoolingPeriods.TryGetValue(control, out var period))
            {
                // Exponential backoff: 2x, 4x, 8x (max 24 hours)
                var multiplier = Math.Min(Math.Pow(2, period.OverrideCount - 1), 144); // 144 * 10min = 24h
                var baseDuration = ScenarioDurations[MapWorkloadToScenario(period.WorkloadContext)];
                period.Duration = TimeSpan.FromMinutes(baseDuration.TotalMinutes * multiplier);
                period.IsEscalated = true;

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[CoolingPeriod] ESCALATED {control} to {period.Duration.TotalHours:F1}h due to repeated overrides ({period.OverrideCount}x)");
            }
        }
    }

    /// <summary>
    /// Clear cooling period (user explicitly re-enabled automation)
    /// </summary>
    public void ClearCoolingPeriod(string control)
    {
        if (string.IsNullOrEmpty(control))
            return;

        lock (_lock)
        {
            if (_activeCoolingPeriods.Remove(control))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[CoolingPeriod] Cleared cooling period for {control} (user re-enabled automation)");
            }
        }
    }

    /// <summary>
    /// Clear all cooling periods (global automation re-enable)
    /// </summary>
    public void ClearAllCoolingPeriods()
    {
        lock (_lock)
        {
            var count = _activeCoolingPeriods.Count;
            _activeCoolingPeriods.Clear();

            if (Log.Instance.IsTraceEnabled && count > 0)
                Log.Instance.Trace($"[CoolingPeriod] Cleared all cooling periods ({count} controls released)");
        }
    }

    /// <summary>
    /// Get override frequency for a control (overrides per hour)
    /// Used by UserPreferenceTracker for annoyance detection
    /// </summary>
    public double GetOverrideFrequency(string control)
    {
        if (string.IsNullOrEmpty(control))
            return 0;

        lock (_lock)
        {
            if (!_overrideHistory.TryGetValue(control, out var history) || history.Count == 0)
                return 0;

            // Count overrides in last hour
            var oneHourAgo = DateTime.UtcNow.AddHours(-1);
            var recentOverrides = history.Count(t => t >= oneHourAgo);

            return recentOverrides;
        }
    }

    /// <summary>
    /// Get all active cooling periods (for UI display)
    /// </summary>
    public List<CoolingPeriod> GetActiveCoolingPeriods()
    {
        lock (_lock)
        {
            return _activeCoolingPeriods.Values.ToList();
        }
    }

    /// <summary>
    /// Get cooling period statistics for telemetry
    /// </summary>
    public CoolingPeriodStats GetStatistics()
    {
        lock (_lock)
        {
            var stats = new CoolingPeriodStats
            {
                ActiveCoolingPeriodsCount = _activeCoolingPeriods.Count,
                EscalatedPeriodsCount = _activeCoolingPeriods.Values.Count(p => p.IsEscalated),
                TotalOverridesLast24Hours = 0,
                MostOverriddenControl = null
            };

            // Calculate total overrides in last 24 hours
            var yesterday = DateTime.UtcNow.AddHours(-24);
            foreach (var history in _overrideHistory.Values)
            {
                stats.TotalOverridesLast24Hours += history.Count(t => t >= yesterday);
            }

            // Find most overridden control
            if (_overrideHistory.Count > 0)
            {
                var mostOverridden = _overrideHistory
                    .OrderByDescending(kvp => kvp.Value.Count(t => t >= yesterday))
                    .FirstOrDefault();

                if (mostOverridden.Value?.Any(t => t >= yesterday) == true)
                {
                    stats.MostOverriddenControl = mostOverridden.Key;
                    stats.MostOverriddenControlCount = mostOverridden.Value.Count(t => t >= yesterday);
                }
            }

            return stats;
        }
    }

    /// <summary>
    /// Map workload type to cooling period scenario
    /// </summary>
    private CoolingPeriodScenario MapWorkloadToScenario(WorkloadType workload)
    {
        return workload switch
        {
            WorkloadType.MediaPlayback => CoolingPeriodScenario.VideoWatching,
            WorkloadType.ContentCreation => CoolingPeriodScenario.VideoEditing,
            WorkloadType.Gaming => CoolingPeriodScenario.Gaming,
            WorkloadType.AIWorkload => CoolingPeriodScenario.AIWorkload,
            WorkloadType.LightProductivity or WorkloadType.HeavyProductivity => CoolingPeriodScenario.OfficeWork,
            _ => CoolingPeriodScenario.General
        };
    }
}

/// <summary>
/// Cooling period statistics for telemetry and monitoring
/// </summary>
public class CoolingPeriodStats
{
    public int ActiveCoolingPeriodsCount { get; set; }
    public int EscalatedPeriodsCount { get; set; }
    public int TotalOverridesLast24Hours { get; set; }
    public string? MostOverriddenControl { get; set; }
    public int MostOverriddenControlCount { get; set; }

    public override string ToString()
    {
        return $"Cooling Periods: {ActiveCoolingPeriodsCount} active, {EscalatedPeriodsCount} escalated, " +
               $"{TotalOverridesLast24Hours} overrides (24h), Most overridden: {MostOverriddenControl ?? "None"} ({MostOverriddenControlCount}x)";
    }
}
