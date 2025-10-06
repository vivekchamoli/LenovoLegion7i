using System;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Services;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Display Agent - Intelligent display brightness and refresh rate management
/// Optimizes battery by reducing brightness and refresh rate when appropriate
/// Priority: High (30-40% battery impact)
/// ENHANCED: Content-aware refresh rate optimization (24fps → 48Hz, 30fps → 60Hz)
/// </summary>
public class DisplayAgent : IOptimizationAgent
{
    private readonly DisplayBrightnessController? _brightnessController;
    private readonly RefreshRateFeature _refreshRateFeature;
    private readonly SystemContextStore _contextStore;
    private readonly UserOverrideManager _overrideManager;
    private readonly ContentFramerateDetector _framerateDetector;

    // Display optimization parameters
    private const int MIN_BRIGHTNESS = 15;  // Never go completely dark
    private const int MAX_BRIGHTNESS = 100;
    private const int BATTERY_SAVER_BRIGHTNESS = 40;    // Low battery brightness
    private const int NORMAL_BATTERY_BRIGHTNESS = 60;   // Normal battery brightness
    private const int AC_BRIGHTNESS = 80;               // AC power brightness

    // State tracking to avoid redundant proposals
    private int _lastProposedBrightness = -1;
    private bool? _lastWasOnBattery = null;
    private UserIntent? _lastUserIntent = null;
    private int _lastBatteryPercent = -1;

    public string AgentName => "DisplayAgent";
    public AgentPriority Priority => AgentPriority.High;

    public DisplayAgent(
        DisplayBrightnessController? brightnessController,
        RefreshRateFeature refreshRateFeature,
        SystemContextStore contextStore,
        UserOverrideManager overrideManager,
        ContentFramerateDetector? framerateDetector = null)
    {
        _brightnessController = brightnessController;
        _refreshRateFeature = refreshRateFeature ?? throw new ArgumentNullException(nameof(refreshRateFeature));
        _contextStore = contextStore ?? throw new ArgumentNullException(nameof(contextStore));
        _overrideManager = overrideManager ?? throw new ArgumentNullException(nameof(overrideManager));
        _framerateDetector = framerateDetector ?? new ContentFramerateDetector();
    }

    public async Task<AgentProposal> ProposeActionsAsync(SystemContext context)
    {
        var proposal = new AgentProposal
        {
            Agent = AgentName,
            Priority = Priority
        };

        // Propose brightness adjustments
        await ProposeBrightnessOptimizationAsync(proposal, context).ConfigureAwait(false);

        // Propose refresh rate adjustments
        await ProposeRefreshRateOptimizationAsync(proposal, context).ConfigureAwait(false);

        return proposal;
    }

    public Task OnActionsExecutedAsync(ExecutionResult result)
    {
        // Track display optimizations for learning
        if (result.Success && Log.Instance.IsTraceEnabled)
        {
            foreach (var action in result.ExecutedActions)
            {
                if (action.Target.Contains("DISPLAY"))
                {
                    Log.Instance.Trace($"Display action executed: {action.Target} = {action.Value}");
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Propose brightness optimizations based on battery state
    /// FIXED: Only proposes changes when state changes or no user override active
    /// </summary>
    private Task ProposeBrightnessOptimizationAsync(AgentProposal proposal, SystemContext context)
    {
        if (_brightnessController == null)
            return Task.CompletedTask;

        // Check for user override - respect manual brightness adjustments
        if (_overrideManager.IsOverrideActive("DISPLAY_BRIGHTNESS"))
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Brightness optimization skipped - user override active");
            return Task.CompletedTask;
        }

        var targetBrightness = CalculateOptimalBrightness(context);

        // Detect state changes that should trigger brightness adjustment
        bool powerStateChanged = !_lastWasOnBattery.HasValue || context.BatteryState.IsOnBattery != _lastWasOnBattery.Value;
        bool intentChanged = !_lastUserIntent.HasValue || context.UserIntent != _lastUserIntent.Value;
        bool batteryThresholdCrossed = HasCrossedBatteryThreshold(context.BatteryState.ChargePercent, _lastBatteryPercent);
        bool brightnessChanged = targetBrightness != _lastProposedBrightness;

        // Only propose if state changed or first run
        if (powerStateChanged || intentChanged || batteryThresholdCrossed || _lastProposedBrightness == -1)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = GetBrightnessActionType(context),
                Target = "DISPLAY_BRIGHTNESS",
                Value = targetBrightness,
                Reason = GetBrightnessReason(context, targetBrightness),
                Context = context
            });

            // Update state tracking
            _lastProposedBrightness = targetBrightness;
            _lastWasOnBattery = context.BatteryState.IsOnBattery;
            _lastUserIntent = context.UserIntent;
            _lastBatteryPercent = context.BatteryState.ChargePercent;

            if (Log.Instance.IsTraceEnabled)
            {
                var reason = powerStateChanged ? "power state change" :
                            intentChanged ? "user intent change" :
                            batteryThresholdCrossed ? "battery threshold crossed" :
                            "initial run";
                Log.Instance.Trace($"Brightness optimization: target {targetBrightness}% (reason: {reason})");
            }
        }
        else if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Brightness optimization skipped - no state change (target: {targetBrightness}%, last: {_lastProposedBrightness}%)");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Check if battery percentage crossed a significant threshold
    /// </summary>
    private bool HasCrossedBatteryThreshold(int current, int last)
    {
        if (last == -1)
            return false;

        // Check critical thresholds: 15%, 20%, 30%
        int[] thresholds = { 15, 20, 30 };
        foreach (var threshold in thresholds)
        {
            if ((last >= threshold && current < threshold) || (last < threshold && current >= threshold))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Propose refresh rate optimizations for battery saving
    /// </summary>
    private async Task ProposeRefreshRateOptimizationAsync(AgentProposal proposal, SystemContext context)
    {
        if (!await _refreshRateFeature.IsSupportedAsync().ConfigureAwait(false))
            return;

        var currentRate = await _refreshRateFeature.GetStateAsync().ConfigureAwait(false);
        var availableRates = await _refreshRateFeature.GetAllStatesAsync().ConfigureAwait(false);

        if (availableRates.Length == 0)
            return;

        var targetRate = await DetermineOptimalRefreshRateAsync(context, currentRate, availableRates).ConfigureAwait(false);

        if (targetRate.Frequency != currentRate.Frequency)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = GetRefreshRateActionType(context),
                Target = "DISPLAY_REFRESH_RATE",
                Value = targetRate,
                Reason = GetRefreshRateReason(context, currentRate, targetRate)
            });

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Refresh rate optimization: {currentRate.Frequency}Hz → {targetRate.Frequency}Hz");
        }
    }

    /// <summary>
    /// Calculate optimal brightness based on context
    /// </summary>
    private int CalculateOptimalBrightness(SystemContext context)
    {
        // On AC: Use higher brightness
        if (!context.BatteryState.IsOnBattery)
        {
            return AC_BRIGHTNESS;
        }

        // Critical battery: Minimum brightness
        if (context.BatteryState.ChargePercent < 15)
        {
            return MIN_BRIGHTNESS;
        }

        // Low battery: Battery saver brightness
        if (context.BatteryState.ChargePercent < 30)
        {
            return BATTERY_SAVER_BRIGHTNESS;
        }

        // Medium battery: Adjust based on workload
        return context.UserIntent switch
        {
            UserIntent.Gaming => 70,                        // Gaming needs visibility
            UserIntent.MaxPerformance => 75,                // Max performance: higher brightness
            UserIntent.Productivity => 50,                  // Productivity: moderate
            UserIntent.Balanced => NORMAL_BATTERY_BRIGHTNESS,  // Balanced: normal
            UserIntent.BatterySaving => BATTERY_SAVER_BRIGHTNESS, // Battery saving: dim
            UserIntent.Quiet => 50,                         // Quiet mode: moderate
            _ => NORMAL_BATTERY_BRIGHTNESS
        };
    }

    /// <summary>
    /// Determine optimal refresh rate based on workload, content, and battery
    /// ENHANCED: Content-aware (24fps → 48Hz, 30fps → 60Hz) for perfect cadence
    /// Work Mode: Force 60Hz on battery for maximum power savings (2-3W)
    /// Media Mode: Match content framerate for judder-free playback
    /// </summary>
    private async Task<RefreshRate> DetermineOptimalRefreshRateAsync(
        SystemContext context,
        RefreshRate current,
        RefreshRate[] available)
    {
        // Get min and max available rates
        var minRate = available[0];
        var maxRate = available[^1];

        // PRIORITY 1: Media playback - detect content framerate and match
        if (context.CurrentWorkload.Type == WorkloadType.MediaPlayback)
        {
            var contentFPS = await _framerateDetector.DetectFramerateAsync().ConfigureAwait(false);
            if (contentFPS > 0)
            {
                var availableFrequencies = available.Select(r => r.Frequency).ToArray();
                var optimalHz = _framerateDetector.GetOptimalRefreshRateForContent(contentFPS, availableFrequencies);

                if (optimalHz > 0)
                {
                    var targetRate = available.FirstOrDefault(r => r.Frequency == optimalHz);
                    if (targetRate.Frequency > 0)
                    {
                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Media Mode: Content {contentFPS}fps → {optimalHz}Hz (judder-free cadence)");
                        return targetRate;
                    }
                }
            }

            // Fallback: Use 60Hz for media if content FPS unknown
            var rate60Hz = available.FirstOrDefault(r => r.Frequency == 60);
            if (rate60Hz.Frequency > 0)
                return rate60Hz;
        }

        // PRIORITY 2: WORK MODE (PRODUCTIVITY): Force 60Hz on battery for maximum battery life
        // Savings: 2-3W display power (165Hz → 60Hz)
        if (FeatureFlags.UseProductivityMode && context.BatteryState.IsOnBattery)
        {
            // Find 60Hz rate (typical middle rate for productivity), fallback to minimum
            var rate60Hz = available.FirstOrDefault(r => r.Frequency == 60);
            var targetRate = rate60Hz.Frequency > 0 ? rate60Hz : minRate;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Work Mode: Forcing {targetRate.Frequency}Hz on battery for power savings (2-3W)");

            return targetRate;
        }

        // PRIORITY 3: Battery-based optimization
        if (context.BatteryState.IsOnBattery)
        {
            // Critical battery (< 15%): minimum refresh rate
            if (context.BatteryState.ChargePercent < 15)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Critical battery: Forcing {minRate.Frequency}Hz");
                return minRate;
            }

            // Low battery (< 30%): prefer 60Hz or 90Hz max
            if (context.BatteryState.ChargePercent < 30)
            {
                var rate60Hz = available.FirstOrDefault(r => r.Frequency == 60);
                if (rate60Hz.Frequency > 0)
                    return rate60Hz;

                var rate90Hz = available.FirstOrDefault(r => r.Frequency == 90);
                if (rate90Hz.Frequency > 0)
                    return rate90Hz;

                return minRate;
            }

            // Medium battery (< 50%): cap at 90Hz for balanced performance
            if (context.BatteryState.ChargePercent < 50)
            {
                var rate90Hz = available.FirstOrDefault(r => r.Frequency == 90);
                if (rate90Hz.Frequency > 0 && current.Frequency > 90)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Medium battery: Capping at 90Hz (balanced mode)");
                    return rate90Hz;
                }
            }
        }

        // PRIORITY 4: On AC or good battery - respect user's choice
        return current;
    }

    /// <summary>
    /// Get middle refresh rate (typically 60Hz) for video playback
    /// </summary>
    private RefreshRate GetMidRefreshRate(RefreshRate[] rates)
    {
        // Try to find 60Hz
        foreach (var rate in rates)
        {
            if (rate.Frequency >= 60 && rate.Frequency <= 75)
                return rate;
        }

        // Fallback to middle option
        return rates.Length > 1 ? rates[rates.Length / 2] : rates[0];
    }

    private ActionType GetBrightnessActionType(SystemContext context)
    {
        if (context.BatteryState.IsOnBattery && context.BatteryState.ChargePercent < 15)
            return ActionType.Critical;

        if (context.BatteryState.IsOnBattery && context.BatteryState.ChargePercent < 30)
            return ActionType.Proactive;

        return ActionType.Opportunistic;
    }

    private ActionType GetRefreshRateActionType(SystemContext context)
    {
        if (context.BatteryState.IsOnBattery && context.BatteryState.ChargePercent < 20)
            return ActionType.Proactive;

        return ActionType.Opportunistic;
    }

    private string GetBrightnessReason(SystemContext context, int targetBrightness)
    {
        if (!context.BatteryState.IsOnBattery)
            return $"AC power - setting brightness to {targetBrightness}%";

        if (context.BatteryState.ChargePercent < 15)
            return $"Critical battery ({context.BatteryState.ChargePercent}%) - reducing to minimum brightness";

        if (context.BatteryState.ChargePercent < 30)
            return $"Low battery ({context.BatteryState.ChargePercent}%) - reducing brightness to {targetBrightness}%";

        return $"Optimizing brightness for {context.UserIntent} on battery";
    }

    private string GetRefreshRateReason(SystemContext context, RefreshRate from, RefreshRate to)
    {
        if (!context.BatteryState.IsOnBattery)
            return $"AC power - setting {to.Frequency}Hz for {context.UserIntent}";

        if (context.BatteryState.ChargePercent < 30)
            return $"Low battery ({context.BatteryState.ChargePercent}%) - reducing to {to.Frequency}Hz";

        return $"Optimizing refresh rate for battery life: {from.Frequency}Hz → {to.Frequency}Hz";
    }
}
