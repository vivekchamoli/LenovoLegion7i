using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Display Agent - Intelligent display brightness and refresh rate management
/// Optimizes battery by reducing brightness and refresh rate when appropriate
/// Priority: High (30-40% battery impact)
/// </summary>
public class DisplayAgent : IOptimizationAgent
{
    private readonly DisplayBrightnessController? _brightnessController;
    private readonly RefreshRateFeature _refreshRateFeature;
    private readonly SystemContextStore _contextStore;

    // Display optimization parameters
    private const int MIN_BRIGHTNESS = 15;  // Never go completely dark
    private const int MAX_BRIGHTNESS = 100;
    private const int BATTERY_SAVER_BRIGHTNESS = 40;    // Low battery brightness
    private const int NORMAL_BATTERY_BRIGHTNESS = 60;   // Normal battery brightness
    private const int AC_BRIGHTNESS = 80;               // AC power brightness

    public string AgentName => "DisplayAgent";
    public AgentPriority Priority => AgentPriority.High;

    public DisplayAgent(
        DisplayBrightnessController? brightnessController,
        RefreshRateFeature refreshRateFeature,
        SystemContextStore contextStore)
    {
        _brightnessController = brightnessController;
        _refreshRateFeature = refreshRateFeature ?? throw new ArgumentNullException(nameof(refreshRateFeature));
        _contextStore = contextStore ?? throw new ArgumentNullException(nameof(contextStore));
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
    /// </summary>
    private Task ProposeBrightnessOptimizationAsync(AgentProposal proposal, SystemContext context)
    {
        if (_brightnessController == null)
            return Task.CompletedTask;

        var targetBrightness = CalculateOptimalBrightness(context);

        // Always propose brightness changes (executor will handle rate limiting)
        proposal.Actions.Add(new ResourceAction
        {
            Type = GetBrightnessActionType(context),
            Target = "DISPLAY_BRIGHTNESS",
            Value = targetBrightness,
            Reason = GetBrightnessReason(context, targetBrightness)
        });

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Brightness optimization: target {targetBrightness}%");

        return Task.CompletedTask;
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

        var targetRate = DetermineOptimalRefreshRate(context, currentRate, availableRates);

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
    /// Determine optimal refresh rate based on workload and battery
    /// CHANGED: Respects user manual settings - only intervenes on critically low battery
    /// </summary>
    private RefreshRate DetermineOptimalRefreshRate(
        SystemContext context,
        RefreshRate current,
        RefreshRate[] available)
    {
        // Get min and max available rates
        var minRate = available[0];
        var maxRate = available[^1];

        // On AC: Respect user's manual choice, don't interfere
        // User has full control when plugged in
        if (!context.BatteryState.IsOnBattery)
        {
            // Return current rate (no change) - respect user's manual setting
            return current;
        }

        // On battery: Only intervene if battery is critically low (< 20%)
        if (context.BatteryState.ChargePercent < 20)
        {
            // Critical battery: force minimum refresh rate to extend battery life
            return minRate;
        }

        // Battery above 20%: Respect user's manual choice
        // User can manage their own refresh rate when battery is not critical
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
