using System;
using System.Linq;
using LenovoLegionToolkit.Lib.AI;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Services;

/// <summary>
/// Intercepts feature state changes from UI and records cooling periods
/// Prevents agents from immediately overriding user manual changes
/// GUARDRAIL: Optional service - system works without it (graceful degradation)
/// v6.20.0: Completes cooling period system integration
/// </summary>
public class FeatureChangeInterceptor
{
    private readonly CoolingPeriodManager? _coolingPeriodManager;
    private readonly SystemContextStore? _contextStore;
    private readonly UserPreferenceTracker? _preferenceTracker;

    /// <summary>
    /// Constructor with optional dependencies for graceful degradation
    /// GUARDRAIL: If dependencies are null, service becomes a no-op (zero breaking changes)
    /// </summary>
    public FeatureChangeInterceptor(
        CoolingPeriodManager? coolingPeriodManager = null,
        SystemContextStore? contextStore = null,
        UserPreferenceTracker? preferenceTracker = null)
    {
        _coolingPeriodManager = coolingPeriodManager;
        _contextStore = contextStore;
        _preferenceTracker = preferenceTracker;

        // GUARDRAIL: Log initialization status (helps debugging)
        if (Log.Instance.IsTraceEnabled)
        {
            if (_coolingPeriodManager != null && _contextStore != null)
            {
                Log.Instance.Trace($"[FeatureChangeInterceptor] Initialized with full cooling period support and preference tracking");
            }
            else
            {
                Log.Instance.Trace($"[FeatureChangeInterceptor] Initialized in no-op mode (missing dependencies: CoolingPeriodManager={_coolingPeriodManager != null}, SystemContextStore={_contextStore != null}, PreferenceTracker={_preferenceTracker != null})");
            }
        }
    }

    /// <summary>
    /// Record a user-initiated feature state change
    /// GUARDRAIL: Null-safe implementation - no-op if dependencies missing
    /// </summary>
    /// <param name="controlName">Control name (e.g., "DISPLAY_REFRESH_RATE")</param>
    /// <param name="newValue">New value set by user</param>
    /// <param name="isUserInitiated">True if user manually changed, false if agent-initiated</param>
    public void RecordUserChange(string controlName, object? newValue, bool isUserInitiated = true)
    {
        // GUARDRAIL: Validate inputs
        if (string.IsNullOrEmpty(controlName))
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[FeatureChangeInterceptor] Invalid control name - ignoring");
            return;
        }

        // GUARDRAIL: Only record manual user changes, not agent-initiated changes
        if (!isUserInitiated)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[FeatureChangeInterceptor] {controlName} change was agent-initiated - not recording cooling period");
            return;
        }

        // GUARDRAIL: Graceful degradation - no-op if CoolingPeriodManager or SystemContextStore not available
        if (_coolingPeriodManager == null || _contextStore == null)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[FeatureChangeInterceptor] {controlName} changed by user, but cooling period system not available (graceful degradation)");
            return;
        }

        try
        {
            // Get last gathered context to determine scenario (avoid expensive async operation on UI thread)
            // GUARDRAIL: Use GetLastContext() which is synchronous and returns cached context
            var context = _contextStore.GetLastContext();
            if (context == null)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[FeatureChangeInterceptor] No context available - using GeneralUse scenario as fallback");

                // Fallback to GeneralUse scenario (30 minutes)
                _coolingPeriodManager.RecordOverride(controlName, AI.UserScenario.GeneralUse, newValue ?? "(null)");
                return;
            }

            // Map current workload to cooling period scenario
            var scenario = MapWorkloadToScenario(context.CurrentWorkload.Type);

            // Record cooling period
            _coolingPeriodManager.RecordOverride(controlName, scenario, newValue ?? "(null)");

            // Record user preference for learning (if preference tracker available)
            if (_preferenceTracker != null)
            {
                try
                {
                    // Record the user override for preference learning
                    // Note: We use "(agent_suggested)" as placeholder since we don't track the previous agent value
                    // The important part is recording the user's chosen value and context
                    _preferenceTracker.RecordOverride(controlName, "(agent_suggested)", newValue ?? "(null)", context);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"[FeatureChangeInterceptor] Recorded user preference for {controlName} = {newValue}");
                }
                catch (Exception prefEx)
                {
                    // GUARDRAIL: Never crash - preference tracking is optional
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"[FeatureChangeInterceptor] Failed to record preference for {controlName}", prefEx);
                }
            }

            if (Log.Instance.IsTraceEnabled)
            {
                var duration = GetScenarioDuration(scenario);
                Log.Instance.Trace($"[FeatureChangeInterceptor] User changed {controlName} to {newValue}, cooling period activated for {scenario} ({duration.TotalMinutes:F0} minutes)");
            }
        }
        catch (Exception ex)
        {
            // GUARDRAIL: Never crash UI thread - log and continue
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[FeatureChangeInterceptor] Failed to record cooling period for {controlName}", ex);
        }
    }

    /// <summary>
    /// Map workload type to cooling period scenario
    /// GUARDRAIL: Provides sensible defaults for all workload types
    /// </summary>
    private AI.UserScenario MapWorkloadToScenario(AI.WorkloadType workload)
    {
        return workload switch
        {
            AI.WorkloadType.MediaPlayback => AI.UserScenario.VideoWatching,        // 120 minutes - movie length
            AI.WorkloadType.Gaming => AI.UserScenario.GamingSession,               // 90 minutes - typical gaming session
            AI.WorkloadType.HeavyProductivity => AI.UserScenario.DevelopmentSession, // 60 minutes - coding/compilation session
            AI.WorkloadType.LightProductivity => AI.UserScenario.OfficeWork,       // 15 minutes - quick office task
            AI.WorkloadType.VideoConferencing => AI.UserScenario.DevelopmentSession, // 60 minutes - typical meeting length
            AI.WorkloadType.AIWorkload => AI.UserScenario.DevelopmentSession,      // 60 minutes - ML training session
            AI.WorkloadType.Idle => AI.UserScenario.OfficeWork,                    // 15 minutes - short idle period
            _ => AI.UserScenario.GeneralUse                                         // 30 minutes - safe default
        };
    }

    /// <summary>
    /// Get duration for scenario (for logging purposes)
    /// </summary>
    private TimeSpan GetScenarioDuration(AI.UserScenario scenario)
    {
        return scenario switch
        {
            AI.UserScenario.VideoWatching => TimeSpan.FromMinutes(120),
            AI.UserScenario.GamingSession => TimeSpan.FromMinutes(90),
            AI.UserScenario.DevelopmentSession => TimeSpan.FromMinutes(60),
            AI.UserScenario.OfficeWork => TimeSpan.FromMinutes(15),
            AI.UserScenario.GeneralUse => TimeSpan.FromMinutes(30),
            _ => TimeSpan.FromMinutes(30)
        };
    }

    /// <summary>
    /// Check if a control is currently in a cooling period (for UI indicators)
    /// GUARDRAIL: Null-safe, returns false if system not available
    /// </summary>
    public bool IsInCoolingPeriod(string controlName, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;

        // GUARDRAIL: Graceful degradation
        if (_coolingPeriodManager == null || string.IsNullOrEmpty(controlName))
            return false;

        try
        {
            return _coolingPeriodManager.IsInCoolingPeriod(controlName, out remaining);
        }
        catch (Exception ex)
        {
            // GUARDRAIL: Never crash - log and return false
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[FeatureChangeInterceptor] Failed to check cooling period for {controlName}", ex);
            return false;
        }
    }

    /// <summary>
    /// Manually clear cooling period for a control (for UI "resume AI control" button)
    /// GUARDRAIL: Null-safe, no-op if system not available
    /// </summary>
    public void ClearCoolingPeriod(string controlName)
    {
        // GUARDRAIL: Validate input
        if (string.IsNullOrEmpty(controlName))
            return;

        // GUARDRAIL: Graceful degradation
        if (_coolingPeriodManager == null)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[FeatureChangeInterceptor] Cannot clear cooling period for {controlName} - CoolingPeriodManager not available");
            return;
        }

        try
        {
            _coolingPeriodManager.ClearCoolingPeriod(controlName);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[FeatureChangeInterceptor] User manually cleared cooling period for {controlName} - AI optimization resumed");
        }
        catch (Exception ex)
        {
            // GUARDRAIL: Never crash - log and continue
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[FeatureChangeInterceptor] Failed to clear cooling period for {controlName}", ex);
        }
    }

    /// <summary>
    /// Get all active cooling periods (for diagnostics/debugging)
    /// GUARDRAIL: Returns empty array if system not available
    /// </summary>
    public AI.CoolingPeriod[] GetActiveCoolingPeriods()
    {
        // GUARDRAIL: Graceful degradation
        if (_coolingPeriodManager == null)
            return Array.Empty<AI.CoolingPeriod>();

        try
        {
            var periods = _coolingPeriodManager.GetActiveCoolingPeriods();
            return periods.ToArray(); // Convert List to Array
        }
        catch (Exception ex)
        {
            // GUARDRAIL: Never crash - log and return empty array
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[FeatureChangeInterceptor] Failed to get active cooling periods", ex);
            return Array.Empty<AI.CoolingPeriod>();
        }
    }
}
