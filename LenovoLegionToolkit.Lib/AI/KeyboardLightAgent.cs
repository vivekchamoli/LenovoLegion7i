using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Keyboard Light Agent - Intelligent keyboard backlight management
/// Automatically dims or turns off keyboard backlight to save battery
/// Priority: Medium (5-8% battery impact)
/// </summary>
public class KeyboardLightAgent : IOptimizationAgent
{
    private readonly RGBKeyboardBacklightController? _keyboardController;
    private bool? _previousState;
    private int? _previousBrightness;

    // Keyboard backlight parameters
    private const int LOW_BATTERY_BRIGHTNESS = 0;   // Off on low battery
    private const int NORMAL_BATTERY_BRIGHTNESS = 30; // Dim on battery
    private const int AC_BRIGHTNESS = 100;          // Full on AC

    public string AgentName => "KeyboardLightAgent";
    public AgentPriority Priority => AgentPriority.Medium;

    public KeyboardLightAgent(RGBKeyboardBacklightController? keyboardController)
    {
        _keyboardController = keyboardController;
    }

    public async Task<AgentProposal> ProposeActionsAsync(SystemContext context)
    {
        var proposal = new AgentProposal
        {
            Agent = AgentName,
            Priority = Priority
        };

        // Skip if RGB keyboard not supported
        if (_keyboardController == null || !await _keyboardController.IsSupportedAsync().ConfigureAwait(false))
            return proposal;

        // Determine optimal keyboard backlight state
        var (targetState, targetBrightness) = DetermineOptimalKeyboardState(context);

        // Propose state change if needed
        if (ShouldProposeChange(context, targetState, targetBrightness))
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = GetActionType(context),
                Target = "KEYBOARD_RGB_STATE",
                Value = targetState,
                Reason = GetStateChangeReason(context, targetState)
            });

            if (targetState)
            {
                proposal.Actions.Add(new ResourceAction
                {
                    Type = GetActionType(context),
                    Target = "KEYBOARD_BRIGHTNESS",
                    Value = targetBrightness,
                    Reason = $"Setting brightness to {targetBrightness}%"
                });
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Keyboard backlight: {(targetState ? $"ON ({targetBrightness}%)" : "OFF")}");
        }

        return proposal;
    }

    public Task OnActionsExecutedAsync(ExecutionResult result)
    {
        // Track keyboard backlight changes
        if (result.Success)
        {
            foreach (var action in result.ExecutedActions)
            {
                if (action.Target == "KEYBOARD_RGB_STATE" && action.Value is bool state)
                {
                    _previousState = state;
                }
                else if (action.Target == "KEYBOARD_BRIGHTNESS" && action.Value is int brightness)
                {
                    _previousBrightness = brightness;
                }
            }

            if (Log.Instance.IsTraceEnabled && (_previousState.HasValue || _previousBrightness.HasValue))
            {
                Log.Instance.Trace($"Keyboard state updated: Enabled={_previousState}, Brightness={_previousBrightness}");
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Determine optimal keyboard backlight state and brightness
    /// </summary>
    private (bool enabled, int brightness) DetermineOptimalKeyboardState(SystemContext context)
    {
        // On AC power: Always on with full brightness
        if (!context.BatteryState.IsOnBattery)
        {
            return (true, AC_BRIGHTNESS);
        }

        // Critical battery (<15%): Always off
        if (context.BatteryState.ChargePercent < 15)
        {
            return (false, LOW_BATTERY_BRIGHTNESS);
        }

        // Low battery (<30%): Off unless gaming/productivity
        if (context.BatteryState.ChargePercent < 30)
        {
            var needsKeyboard = context.UserIntent switch
            {
                UserIntent.Gaming => true,
                UserIntent.Productivity => true,
                UserIntent.MaxPerformance => true,
                _ => false
            };

            return needsKeyboard
                ? (true, NORMAL_BATTERY_BRIGHTNESS)
                : (false, LOW_BATTERY_BRIGHTNESS);
        }

        // Normal battery: Dim backlight based on workload
        return context.UserIntent switch
        {
            UserIntent.Gaming => (true, 60),                // Gaming: moderate brightness
            UserIntent.MaxPerformance => (true, 70),        // Max performance: higher brightness
            UserIntent.Productivity => (true, 50),          // Productivity: moderate brightness
            UserIntent.Balanced => (true, 40),              // Balanced: lower brightness
            UserIntent.BatterySaving => (false, 0),         // Battery saving: off
            UserIntent.Quiet => (true, 30),                 // Quiet: minimal
            _ => (true, NORMAL_BATTERY_BRIGHTNESS)
        };
    }

    /// <summary>
    /// Check if we should propose a change (avoid too frequent changes)
    /// </summary>
    private bool ShouldProposeChange(SystemContext context, bool targetState, int targetBrightness)
    {
        // Always propose if state changed
        if (_previousState.HasValue && _previousState.Value != targetState)
            return true;

        // Propose if brightness changed significantly (>20%)
        if (_previousBrightness.HasValue && Math.Abs(_previousBrightness.Value - targetBrightness) > 20)
            return true;

        // Propose on first run
        if (!_previousState.HasValue)
            return true;

        return false;
    }

    private ActionType GetActionType(SystemContext context)
    {
        // Critical battery: turn off immediately
        if (context.BatteryState.IsOnBattery && context.BatteryState.ChargePercent < 15)
            return ActionType.Critical;

        // Low battery: proactive dimming
        if (context.BatteryState.IsOnBattery && context.BatteryState.ChargePercent < 30)
            return ActionType.Proactive;

        // Otherwise opportunistic
        return ActionType.Opportunistic;
    }

    private string GetStateChangeReason(SystemContext context, bool targetState)
    {
        if (!context.BatteryState.IsOnBattery)
            return "AC power - enabling keyboard backlight";

        if (context.BatteryState.ChargePercent < 15)
            return $"Critical battery ({context.BatteryState.ChargePercent}%) - disabling keyboard backlight";

        if (context.BatteryState.ChargePercent < 30)
        {
            if (targetState)
                return $"Low battery ({context.BatteryState.ChargePercent}%) - minimal keyboard backlight";
            else
                return $"Low battery ({context.BatteryState.ChargePercent}%) - disabling keyboard backlight";
        }

        if (targetState)
            return $"Optimizing keyboard backlight for {context.UserIntent}";
        else
            return $"Disabling keyboard backlight during {context.UserIntent}";
    }
}
