using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Safety Validator - Prevents dangerous or unwanted agent actions
/// Enforces hardware limits, thermal safety, and user overrides
/// </summary>
public class SafetyValidator
{
    private readonly UserOverrideManager _overrideManager;
    private readonly AgentCoordinator? _agentCoordinator;

    // Legion 7i Gen 9 hardware safety limits
    private const int MAX_CPU_TEMP = 100;
    private const int MAX_GPU_TEMP = 90;
    private const int MAX_VRM_TEMP = 100;
    private const int MIN_CPU_PL1 = 15;
    private const int MAX_CPU_PL1 = 65;
    private const int MIN_CPU_PL2 = 55;
    private const int MAX_CPU_PL2 = 140;
    private const int MIN_GPU_TGP = 60;
    private const int MAX_GPU_TGP = 140;
    private const int MIN_DISPLAY_BRIGHTNESS = 10; // Never go completely dark
    private const int MAX_DISPLAY_BRIGHTNESS = 100;

    public SafetyValidator(UserOverrideManager overrideManager, AgentCoordinator? agentCoordinator = null)
    {
        _overrideManager = overrideManager ?? throw new ArgumentNullException(nameof(overrideManager));
        _agentCoordinator = agentCoordinator;
    }

    /// <summary>
    /// Validate if an action is safe to execute
    /// </summary>
    public ValidationResult ValidateAction(ResourceAction action, SystemContext context)
    {
        // Check user overrides first
        if (_overrideManager.IsOverrideActive(action.Target))
        {
            // FIX #3: Broadcast coordination signal so all agents know user has overridden this control
            if (_agentCoordinator != null)
            {
                _agentCoordinator.BroadcastSignal(new CoordinationSignal
                {
                    Type = CoordinationType.UserOverride,
                    SourceAgent = "SafetyValidator",
                    Timestamp = DateTime.Now,
                    Context = context,
                    Data = new Dictionary<string, object>
                    {
                        ["OverriddenTarget"] = action.Target,
                        ["AttemptedAction"] = action.Type.ToString(),
                        ["AttemptedValue"] = action.Value?.ToString() ?? "null",
                        ["Reason"] = "User manual control active"
                    }
                });

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"User override detected for {action.Target} - broadcasting to all agents");
            }

            return ValidationResult.Reject($"User has manually controlled {action.Target} - respecting user preference");
        }

        // Validate based on action target
        return action.Target switch
        {
            "CPU_PL1" => ValidateCPUPL1(action, context),
            "CPU_PL2" => ValidateCPUPL2(action, context),
            "GPU_TGP" => ValidateGPUTGP(action, context),
            "GPU_OVERCLOCK" => ValidateGPUOverclock(action, context),
            "FAN_PROFILE" => ValidateFanProfile(action, context),
            "POWER_MODE" => ValidatePowerMode(action, context),
            "GPU_MODE" => ValidateGPUMode(action, context),
            "DISPLAY_BRIGHTNESS" => ValidateDisplayBrightness(action, context),
            "DISPLAY_REFRESH_RATE" => ValidateDisplayRefreshRate(action, context),
            "KEYBOARD_RGB_STATE" => ValidationResult.Allow(), // Always safe
            "KEYBOARD_BRIGHTNESS" => ValidationResult.Allow(), // Always safe
            "BATTERY_CHARGE_LIMIT" => ValidateBatteryChargeLimit(action, context),
            "BATTERY_CONSERVATION_MODE" => ValidationResult.Allow(), // Always safe
            _ => ValidationResult.Reject($"Unknown action target: {action.Target}")
        };
    }

    private ValidationResult ValidateCPUPL1(ResourceAction action, SystemContext context)
    {
        var value = Convert.ToInt32(action.Value);

        if (value < MIN_CPU_PL1)
            return ValidationResult.Reject($"CPU PL1 {value}W below minimum safe limit ({MIN_CPU_PL1}W)");

        if (value > MAX_CPU_PL1)
            return ValidationResult.Reject($"CPU PL1 {value}W exceeds maximum limit ({MAX_CPU_PL1}W)");

        // Thermal safety check
        if (context.ThermalState.CpuTemp > MAX_CPU_TEMP - 5 && value > context.PowerState.CurrentPL1)
            return ValidationResult.Reject($"CPU too hot ({context.ThermalState.CpuTemp}°C) for power increase");

        return ValidationResult.Allow();
    }

    private ValidationResult ValidateCPUPL2(ResourceAction action, SystemContext context)
    {
        var value = Convert.ToInt32(action.Value);

        if (value < MIN_CPU_PL2)
            return ValidationResult.Reject($"CPU PL2 {value}W below minimum safe limit ({MIN_CPU_PL2}W)");

        if (value > MAX_CPU_PL2)
            return ValidationResult.Reject($"CPU PL2 {value}W exceeds maximum limit ({MAX_CPU_PL2}W)");

        // Thermal safety check
        if (context.ThermalState.CpuTemp > MAX_CPU_TEMP - 5 && value > context.PowerState.CurrentPL2)
            return ValidationResult.Reject($"CPU too hot ({context.ThermalState.CpuTemp}°C) for power increase");

        // VRM safety check
        if (context.ThermalState.VrmTemp > MAX_VRM_TEMP - 10 && value > context.PowerState.CurrentPL2)
            return ValidationResult.Reject($"VRM too hot ({context.ThermalState.VrmTemp}°C) for power increase");

        return ValidationResult.Allow();
    }

    private ValidationResult ValidateGPUTGP(ResourceAction action, SystemContext context)
    {
        var value = Convert.ToInt32(action.Value);

        if (value < MIN_GPU_TGP)
            return ValidationResult.Reject($"GPU TGP {value}W below minimum limit ({MIN_GPU_TGP}W)");

        if (value > MAX_GPU_TGP)
            return ValidationResult.Reject($"GPU TGP {value}W exceeds maximum limit ({MAX_GPU_TGP}W)");

        // Thermal safety check
        if (context.ThermalState.GpuTemp > MAX_GPU_TEMP - 5 && value > context.PowerState.GpuTGP)
            return ValidationResult.Reject($"GPU too hot ({context.ThermalState.GpuTemp}°C) for power increase");

        // Battery safety - limit high power on low battery
        if (context.BatteryState.IsOnBattery && context.BatteryState.ChargePercent < 15 && value > 80)
            return ValidationResult.Reject($"Battery critical ({context.BatteryState.ChargePercent}%) - limiting GPU power");

        return ValidationResult.Allow();
    }

    private ValidationResult ValidateGPUOverclock(ResourceAction action, SystemContext context)
    {
        // Only allow overclocking on AC power
        if (context.BatteryState.IsOnBattery)
            return ValidationResult.Reject("GPU overclocking not allowed on battery power");

        // Only if thermal headroom available
        if (context.ThermalState.GpuTemp > 75)
            return ValidationResult.Reject($"GPU too hot ({context.ThermalState.GpuTemp}°C) for overclocking");

        return ValidationResult.Allow();
    }

    private ValidationResult ValidateFanProfile(ResourceAction action, SystemContext context)
    {
        // Fan control is always safe - hardware has its own safety limits
        return ValidationResult.Allow();
    }

    private ValidationResult ValidatePowerMode(ResourceAction action, SystemContext context)
    {
        // Power mode changes are always safe
        return ValidationResult.Allow();
    }

    private ValidationResult ValidateGPUMode(ResourceAction action, SystemContext context)
    {
        // GPU mode switching is safe but requires restart
        // Note: Actual switching should warn user about restart requirement
        return ValidationResult.Allow();
    }

    private ValidationResult ValidateDisplayBrightness(ResourceAction action, SystemContext context)
    {
        var value = Convert.ToInt32(action.Value);

        if (value < MIN_DISPLAY_BRIGHTNESS)
            return ValidationResult.Reject($"Display brightness {value}% too low - minimum {MIN_DISPLAY_BRIGHTNESS}%");

        if (value > MAX_DISPLAY_BRIGHTNESS)
            return ValidationResult.Reject($"Display brightness {value}% exceeds maximum {MAX_DISPLAY_BRIGHTNESS}%");

        return ValidationResult.Allow();
    }

    private ValidationResult ValidateDisplayRefreshRate(ResourceAction action, SystemContext context)
    {
        // Display refresh rate changes are always safe
        return ValidationResult.Allow();
    }

    private ValidationResult ValidateBatteryChargeLimit(ResourceAction action, SystemContext context)
    {
        var value = Convert.ToInt32(action.Value);

        if (value < 50 || value > 100)
            return ValidationResult.Reject($"Battery charge limit {value}% out of safe range (50-100%)");

        return ValidationResult.Allow();
    }
}

/// <summary>
/// Manages user manual overrides of automatic controls
/// </summary>
public class UserOverrideManager
{
    private readonly Dictionary<string, UserOverride> _overrides = new();
    private readonly object _lock = new();

    /// <summary>
    /// Set a user override for a specific control
    /// </summary>
    public void SetOverride(string control, object value, TimeSpan duration)
    {
        lock (_lock)
        {
            _overrides[control] = new UserOverride
            {
                Control = control,
                Value = value,
                SetAt = DateTime.Now,
                ExpiresAt = DateTime.Now + duration
            };

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"User override set: {control} = {value} for {duration.TotalMinutes:F0} minutes");
        }
    }

    /// <summary>
    /// Check if a user override is active for a control
    /// </summary>
    public bool IsOverrideActive(string control)
    {
        lock (_lock)
        {
            if (_overrides.TryGetValue(control, out var userOverride))
            {
                if (DateTime.Now < userOverride.ExpiresAt)
                    return true;

                // Override expired - remove it
                _overrides.Remove(control);
            }
            return false;
        }
    }

    /// <summary>
    /// Clear a specific override
    /// </summary>
    public void ClearOverride(string control)
    {
        lock (_lock)
        {
            if (_overrides.Remove(control))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"User override cleared: {control}");
            }
        }
    }

    /// <summary>
    /// Clear all overrides
    /// </summary>
    public void ClearAllOverrides()
    {
        lock (_lock)
        {
            var count = _overrides.Count;
            _overrides.Clear();

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"All user overrides cleared ({count} overrides)");
        }
    }

    /// <summary>
    /// Get all active overrides
    /// </summary>
    public List<UserOverride> GetActiveOverrides()
    {
        lock (_lock)
        {
            var now = DateTime.Now;
            return _overrides.Values
                .Where(o => now < o.ExpiresAt)
                .ToList();
        }
    }
}

/// <summary>
/// User manual override record
/// </summary>
public class UserOverride
{
    public string Control { get; set; } = string.Empty;
    public object Value { get; set; } = null!;
    public DateTime SetAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Validation result
/// </summary>
public class ValidationResult
{
    public bool IsAllowed { get; set; }
    public string Reason { get; set; } = string.Empty;

    public static ValidationResult Allow() => new() { IsAllowed = true };
    public static ValidationResult Reject(string reason) => new() { IsAllowed = false, Reason = reason };
}
