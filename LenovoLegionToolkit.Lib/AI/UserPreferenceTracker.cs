using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// User Preference Tracker - Learns from user manual overrides
/// Adapts agent behavior to respect user preferences
/// </summary>
public class UserPreferenceTracker
{
    private readonly List<UserOverrideEvent> _overrideHistory = new();
    private readonly Dictionary<string, PreferenceLearning> _learnedPreferences = new();
    private const int MaxHistorySize = 1000;
    private readonly object _lock = new();

    /// <summary>
    /// Record a user override event
    /// </summary>
    public void RecordOverride(string control, object agentValue, object userValue, SystemContext context)
    {
        lock (_lock)
        {
            var overrideEvent = new UserOverrideEvent
            {
                Timestamp = DateTime.Now,
                Control = control,
                AgentSuggestion = agentValue,
                UserPreference = userValue,
                BatteryPercent = context.BatteryState.ChargePercent,
                IsOnBattery = context.BatteryState.IsOnBattery,
                UserIntent = context.UserIntent,
                WorkloadType = context.CurrentWorkload.Type
            };

            _overrideHistory.Add(overrideEvent);

            // Trim history
            while (_overrideHistory.Count > MaxHistorySize)
                _overrideHistory.RemoveAt(0);

            // Update learned preferences
            UpdateLearnedPreferences(control, context);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"User override recorded: {control} - Agent: {agentValue}, User: {userValue}");
        }
    }

    /// <summary>
    /// Check if user has a learned preference for a control in this context
    /// </summary>
    public bool HasPreference(string control, SystemContext context, out object preferredValue)
    {
        lock (_lock)
        {
            preferredValue = null!;

            if (!_learnedPreferences.TryGetValue(control, out var learning))
                return false;

            // Check if we have enough confidence
            if (learning.Confidence < 0.6)
                return false;

            // Check if context matches
            var contextKey = GetContextKey(context);
            if (learning.PreferencesByContext.TryGetValue(contextKey, out var preference))
            {
                if (preference.Occurrences >= 3) // Minimum 3 overrides to establish preference
                {
                    preferredValue = preference.Value;
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Get override frequency for a control (to avoid annoying user)
    /// </summary>
    public double GetOverrideFrequency(string control)
    {
        lock (_lock)
        {
            var recentOverrides = _overrideHistory
                .Where(o => o.Control == control)
                .Where(o => (DateTime.Now - o.Timestamp).TotalHours < 24)
                .Count();

            // Return overrides per hour
            return recentOverrides / 24.0;
        }
    }

    /// <summary>
    /// Check if we should avoid proposing this action (user keeps overriding it)
    /// </summary>
    public bool ShouldAvoidProposal(string control, object proposedValue, SystemContext context)
    {
        lock (_lock)
        {
            // If user overrides this more than once per hour, stop proposing
            if (GetOverrideFrequency(control) > 1.0)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Avoiding {control} proposal - user override frequency too high");
                return true;
            }

            // Check if user has consistently rejected this specific value
            var recentSimilarOverrides = _overrideHistory
                .Where(o => o.Control == control)
                .Where(o => (DateTime.Now - o.Timestamp).TotalDays < 7)
                .Where(o => o.AgentSuggestion?.ToString() == proposedValue?.ToString())
                .ToList();

            if (recentSimilarOverrides.Count >= 3)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Avoiding {control} = {proposedValue} - user rejected this 3+ times");
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Update learned preferences based on override history
    /// </summary>
    private void UpdateLearnedPreferences(string control, SystemContext context)
    {
        if (!_learnedPreferences.ContainsKey(control))
        {
            _learnedPreferences[control] = new PreferenceLearning
            {
                Control = control,
                PreferencesByContext = new Dictionary<string, ContextualPreference>()
            };
        }

        var learning = _learnedPreferences[control];
        var contextKey = GetContextKey(context);

        if (!learning.PreferencesByContext.ContainsKey(contextKey))
        {
            learning.PreferencesByContext[contextKey] = new ContextualPreference
            {
                Context = contextKey,
                Occurrences = 0
            };
        }

        var preference = learning.PreferencesByContext[contextKey];
        preference.Occurrences++;
        preference.LastSeen = DateTime.Now;

        // Get most recent user preference in this context
        var recentOverride = _overrideHistory
            .Where(o => o.Control == control)
            .Where(o => GetContextKey(new SystemContext
            {
                BatteryState = new BatteryState { IsOnBattery = o.IsOnBattery, ChargePercent = o.BatteryPercent },
                UserIntent = o.UserIntent,
                CurrentWorkload = new WorkloadProfile { Type = o.WorkloadType },
                ThermalState = new ThermalState { Trend = new ThermalTrend() },
                PowerState = new PowerState(),
                GpuState = new GpuSystemState()
            }) == contextKey)
            .OrderByDescending(o => o.Timestamp)
            .FirstOrDefault();

        if (recentOverride != null)
        {
            preference.Value = recentOverride.UserPreference;
        }

        // Calculate confidence based on consistency
        var contextOverrides = _overrideHistory
            .Where(o => o.Control == control)
            .Where(o => (DateTime.Now - o.Timestamp).TotalDays < 30)
            .ToList();

        if (contextOverrides.Count >= 3)
        {
            var consistency = contextOverrides.GroupBy(o => o.UserPreference?.ToString())
                .Max(g => g.Count()) / (double)contextOverrides.Count;
            learning.Confidence = consistency;
        }
    }

    /// <summary>
    /// Get context key for grouping similar contexts
    /// </summary>
    private string GetContextKey(SystemContext context)
    {
        var batteryCategory = context.BatteryState.IsOnBattery
            ? (context.BatteryState.ChargePercent < 30 ? "low_battery" : "battery")
            : "ac";

        return $"{batteryCategory}_{context.UserIntent}_{context.CurrentWorkload.Type}";
    }

    /// <summary>
    /// Get statistics for diagnostics
    /// </summary>
    public string GetStatistics()
    {
        lock (_lock)
        {
            if (_overrideHistory.Count == 0)
                return "No user overrides recorded";

            var controlGroups = _overrideHistory
                .GroupBy(o => o.Control)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .ToList();

            var learnedCount = _learnedPreferences.Count(p => p.Value.Confidence >= 0.6);

            return $"""
                User Preference Statistics:
                - Total overrides: {_overrideHistory.Count}
                - Learned preferences: {learnedCount}

                Most overridden controls:
                {string.Join("\n", controlGroups.Select((g, i) => $"  {i + 1}. {g.Key}: {g.Count()} overrides"))}
                """;
        }
    }

    /// <summary>
    /// Export preferences data for persistence
    /// </summary>
    public UserPreferencesData ExportData()
    {
        lock (_lock)
        {
            return new UserPreferencesData
            {
                OverrideHistory = new List<UserOverrideEvent>(_overrideHistory),
                LearnedPreferences = new Dictionary<string, PreferenceLearning>(_learnedPreferences)
            };
        }
    }

    /// <summary>
    /// Import preferences data from persistence
    /// </summary>
    public void ImportData(UserPreferencesData data)
    {
        lock (_lock)
        {
            _overrideHistory.Clear();
            _overrideHistory.AddRange(data.OverrideHistory.Take(MaxHistorySize));

            _learnedPreferences.Clear();
            foreach (var kvp in data.LearnedPreferences)
            {
                _learnedPreferences[kvp.Key] = kvp.Value;
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Loaded {_overrideHistory.Count} overrides and {_learnedPreferences.Count} learned preferences from persistence");
        }
    }

    /// <summary>
    /// Get number of recorded overrides
    /// </summary>
    public int GetOverrideCount()
    {
        lock (_lock)
        {
            return _overrideHistory.Count;
        }
    }

    /// <summary>
    /// Get number of learned preferences
    /// </summary>
    public int GetLearnedPreferenceCount()
    {
        lock (_lock)
        {
            return _learnedPreferences.Count(p => p.Value.Confidence >= 0.6);
        }
    }
}

/// <summary>
/// User override event
/// </summary>
public class UserOverrideEvent
{
    public DateTime Timestamp { get; set; }
    public string Control { get; set; } = string.Empty;
    public object AgentSuggestion { get; set; } = null!;
    public object UserPreference { get; set; } = null!;
    public int BatteryPercent { get; set; }
    public bool IsOnBattery { get; set; }
    public UserIntent UserIntent { get; set; }
    public WorkloadType WorkloadType { get; set; }
}

/// <summary>
/// Learned preference for a control
/// </summary>
public class PreferenceLearning
{
    public string Control { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public Dictionary<string, ContextualPreference> PreferencesByContext { get; set; } = new();
}

/// <summary>
/// Preference in a specific context
/// </summary>
public class ContextualPreference
{
    public string Context { get; set; } = string.Empty;
    public object Value { get; set; } = null!;
    public int Occurrences { get; set; }
    public DateTime LastSeen { get; set; }
}
