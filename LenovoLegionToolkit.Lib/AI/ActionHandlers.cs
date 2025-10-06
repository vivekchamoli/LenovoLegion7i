using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// CPU Power Limit Handler - Placeholder for future Gen9ECController integration
/// </summary>
public class CPUPowerLimitHandler : IActionHandler
{
    public string[] SupportedTargets => new[] { "CPU_PL1", "CPU_PL2", "CPU_PL4" };

    public Task ExecuteAsync(ResourceAction action)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[PLACEHOLDER] Would execute: {action.Target} = {action.Value}W");

        return Task.CompletedTask;
    }

    public Task RollbackAsync(ResourceAction action)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[PLACEHOLDER] Would rollback: {action.Target}");

        return Task.CompletedTask;
    }
}

/// <summary>
/// GPU Control Handler - Placeholder for future implementation
/// </summary>
public class GPUControlHandler : IActionHandler
{
    public string[] SupportedTargets => new[] { "GPU_TGP", "GPU_OVERCLOCK", "GPU_POWER_STATE", "GPU_PROCESS_PRIORITY" };

    public Task ExecuteAsync(ResourceAction action)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[PLACEHOLDER] Would execute: {action.Target} = {action.Value}");

        return Task.CompletedTask;
    }

    public Task RollbackAsync(ResourceAction action)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[PLACEHOLDER] Would rollback: {action.Target}");

        return Task.CompletedTask;
    }
}

/// <summary>
/// Fan Control Handler - Controls fan profiles and speeds
/// Integrates with ThermalOptimizer, Gen9ECController, and AcousticOptimizer for comprehensive thermal management
/// </summary>
public class FanControlHandler : IActionHandler
{
    private readonly Gen9ECController _ecController;
    private readonly ThermalOptimizer _thermalOptimizer;
    private readonly AcousticOptimizer? _acousticOptimizer;
    private readonly Dictionary<string, object> _previousValues = new();
    private byte _lastCpuFanSpeed = 0;
    private byte _lastGpuFanSpeed = 0;

    public string[] SupportedTargets => new[]
    {
        "FAN_PROFILE",
        "FAN_SPEED_CPU",
        "FAN_SPEED_GPU",
        "FAN_CURVE_APPLY",
        "FAN_FULL_SPEED",
        "VAPOR_CHAMBER_MODE"
    };

    public FanControlHandler(
        Gen9ECController ecController,
        ThermalOptimizer thermalOptimizer,
        AcousticOptimizer? acousticOptimizer = null)
    {
        _ecController = ecController ?? throw new ArgumentNullException(nameof(ecController));
        _thermalOptimizer = thermalOptimizer ?? throw new ArgumentNullException(nameof(thermalOptimizer));
        _acousticOptimizer = acousticOptimizer;
    }

    public async Task ExecuteAsync(ResourceAction action)
    {
        switch (action.Target)
        {
            case "FAN_PROFILE":
                if (action.Value is FanProfile profile)
                {
                    // Store previous profile for rollback
                    if (!_previousValues.ContainsKey(action.Target))
                        _previousValues[action.Target] = profile;

                    // Apply fan profile through ThermalOptimizer
                    await _thermalOptimizer.ApplyFanProfileAsync(profile).ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Fan profile set to {profile}");
                }
                break;

            case "FAN_SPEED_CPU":
                if (action.Value is int cpuSpeed)
                {
                    var targetSpeedByte = (byte)Math.Clamp(cpuSpeed, 0, 255);

                    // Apply acoustic optimization if available
                    byte optimizedSpeed = targetSpeedByte;
                    if (_acousticOptimizer != null && action.Context?.UserIntent != null)
                    {
                        var currentPercent = Gen9ECController.FanSpeedToPercentage(_lastCpuFanSpeed);
                        var targetPercent = Gen9ECController.FanSpeedToPercentage(targetSpeedByte);

                        var recommendation = _acousticOptimizer.OptimizeForAcoustics(
                            currentPercent,
                            targetPercent,
                            action.Context.UserIntent
                        );

                        optimizedSpeed = Gen9ECController.PercentageToFanSpeed(recommendation.RecommendedPercent);

                        if (recommendation.RateLimited && Log.Instance.IsTraceEnabled)
                        {
                            Log.Instance.Trace($"Acoustic optimization: {recommendation.Reason}");
                        }
                    }

                    // Store previous value
                    if (!_previousValues.ContainsKey(action.Target))
                        _previousValues[action.Target] = _lastCpuFanSpeed;

                    await _ecController.WriteRegisterAsync(0xB2, optimizedSpeed).ConfigureAwait(false); // FAN1_TARGET
                    _lastCpuFanSpeed = optimizedSpeed;

                    if (Log.Instance.IsTraceEnabled)
                    {
                        var percent = Gen9ECController.FanSpeedToPercentage(optimizedSpeed);
                        var rpm = Gen9ECController.FanSpeedToRPM(optimizedSpeed);
                        Log.Instance.Trace($"CPU fan speed set to {optimizedSpeed}/255 ({percent}%, ~{rpm} RPM)");
                    }
                }
                break;

            case "FAN_SPEED_GPU":
                if (action.Value is int gpuSpeed)
                {
                    var targetSpeedByte = (byte)Math.Clamp(gpuSpeed, 0, 255);

                    // Apply acoustic optimization if available
                    byte optimizedSpeed = targetSpeedByte;
                    if (_acousticOptimizer != null && action.Context?.UserIntent != null)
                    {
                        var currentPercent = Gen9ECController.FanSpeedToPercentage(_lastGpuFanSpeed);
                        var targetPercent = Gen9ECController.FanSpeedToPercentage(targetSpeedByte);

                        var recommendation = _acousticOptimizer.OptimizeForAcoustics(
                            currentPercent,
                            targetPercent,
                            action.Context.UserIntent
                        );

                        optimizedSpeed = Gen9ECController.PercentageToFanSpeed(recommendation.RecommendedPercent);

                        if (recommendation.RateLimited && Log.Instance.IsTraceEnabled)
                        {
                            Log.Instance.Trace($"Acoustic optimization: {recommendation.Reason}");
                        }
                    }

                    // Store previous value
                    if (!_previousValues.ContainsKey(action.Target))
                        _previousValues[action.Target] = _lastGpuFanSpeed;

                    await _ecController.WriteRegisterAsync(0xB3, optimizedSpeed).ConfigureAwait(false); // FAN2_TARGET
                    _lastGpuFanSpeed = optimizedSpeed;

                    if (Log.Instance.IsTraceEnabled)
                    {
                        var percent = Gen9ECController.FanSpeedToPercentage(optimizedSpeed);
                        var rpm = Gen9ECController.FanSpeedToRPM(optimizedSpeed);
                        Log.Instance.Trace($"GPU fan speed set to {optimizedSpeed}/255 ({percent}%, ~{rpm} RPM)");
                    }
                }
                break;

            case "FAN_FULL_SPEED":
                if (action.Value is bool fullSpeed)
                {
                    // Store previous value
                    if (!_previousValues.ContainsKey(action.Target))
                        _previousValues[action.Target] = fullSpeed;

                    // Set both fans to full speed (255) or return to normal
                    if (fullSpeed)
                    {
                        await _ecController.WriteRegisterAsync(0xB2, 0xFF).ConfigureAwait(false); // CPU fan max
                        await _ecController.WriteRegisterAsync(0xB3, 0xFF).ConfigureAwait(false); // GPU fan max
                    }
                    else
                    {
                        // Return to curve-based control
                        await _ecController.FixFanCurveAsync().ConfigureAwait(false);
                    }

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Fan full speed mode: {(fullSpeed ? "ENABLED" : "DISABLED")}");
                }
                break;

            case "VAPOR_CHAMBER_MODE":
                if (action.Value is VaporChamberMode vaporMode)
                {
                    // Store previous mode for rollback
                    if (!_previousValues.ContainsKey(action.Target))
                        _previousValues[action.Target] = vaporMode;

                    await _ecController.SetVaporChamberModeAsync(vaporMode).ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Vapor chamber mode set to {vaporMode}");
                }
                break;

            default:
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Unknown fan action target: {action.Target}");
                break;
        }
    }

    public async Task RollbackAsync(ResourceAction action)
    {
        if (!_previousValues.TryGetValue(action.Target, out var previousValue))
            return;

        switch (action.Target)
        {
            case "FAN_PROFILE":
                if (previousValue is FanProfile profile)
                {
                    await _thermalOptimizer.ApplyFanProfileAsync(profile).ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Rolled back fan profile to {profile}");
                }
                break;

            case "FAN_SPEED_CPU":
                if (previousValue is byte cpuSpeed)
                {
                    await _ecController.WriteRegisterAsync(0xB2, cpuSpeed).ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Rolled back CPU fan speed to {cpuSpeed}");
                }
                break;

            case "FAN_SPEED_GPU":
                if (previousValue is byte gpuSpeed)
                {
                    await _ecController.WriteRegisterAsync(0xB3, gpuSpeed).ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Rolled back GPU fan speed to {gpuSpeed}");
                }
                break;

            case "FAN_FULL_SPEED":
                if (previousValue is bool fullSpeed)
                {
                    if (fullSpeed)
                    {
                        await _ecController.WriteRegisterAsync(0xB2, 0xFF).ConfigureAwait(false);
                        await _ecController.WriteRegisterAsync(0xB3, 0xFF).ConfigureAwait(false);
                    }
                    else
                    {
                        await _ecController.FixFanCurveAsync().ConfigureAwait(false);
                    }

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Rolled back fan full speed mode to {(fullSpeed ? "ENABLED" : "DISABLED")}");
                }
                break;

            case "VAPOR_CHAMBER_MODE":
                if (previousValue is VaporChamberMode vaporMode)
                {
                    await _ecController.SetVaporChamberModeAsync(vaporMode).ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Rolled back vapor chamber mode to {vaporMode}");
                }
                break;
        }
    }
}

/// <summary>
/// Power Mode Handler - Controls system power modes
/// </summary>
public class PowerModeHandler : IActionHandler
{
    private readonly PowerModeFeature _powerModeFeature;
    private PowerModeState? _previousMode;

    public string[] SupportedTargets => new[] { "POWER_MODE" };

    public PowerModeHandler(PowerModeFeature powerModeFeature)
    {
        _powerModeFeature = powerModeFeature ?? throw new ArgumentNullException(nameof(powerModeFeature));
    }

    public async Task ExecuteAsync(ResourceAction action)
    {
        if (action.Value is not PowerModeState mode)
            return;

        // Store previous mode
        if (_previousMode == null)
            _previousMode = await _powerModeFeature.GetStateAsync().ConfigureAwait(false);

        await _powerModeFeature.SetStateAsync(mode).ConfigureAwait(false);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Power mode set to {mode}");
    }

    public async Task RollbackAsync(ResourceAction action)
    {
        if (_previousMode != null)
        {
            await _powerModeFeature.SetStateAsync(_previousMode.Value).ConfigureAwait(false);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Rolled back power mode to {_previousMode}");
        }
    }
}

/// <summary>
/// Battery Control Handler - Placeholder for future implementation
/// </summary>
public class BatteryControlHandler : IActionHandler
{
    public string[] SupportedTargets => new[] { "BATTERY_CHARGE_LIMIT", "BATTERY_CONSERVATION_MODE", "BATTERY_PAUSE_CHARGING" };

    public Task ExecuteAsync(ResourceAction action)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[PLACEHOLDER] Would execute: {action.Target} = {action.Value}");

        return Task.CompletedTask;
    }

    public Task RollbackAsync(ResourceAction action)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[PLACEHOLDER] Would rollback: {action.Target}");

        return Task.CompletedTask;
    }
}

/// <summary>
/// Hybrid Mode Handler - Controls GPU mode switching (iGPU vs dGPU)
/// Phase 1 Fix: Uses GPUTransitionManager for thread-safe execution
/// </summary>
public class HybridModeHandler : IActionHandler
{
    private readonly HybridModeFeature _hybridModeFeature;
    private readonly Services.GPUTransitionManager _transitionManager;
    private HybridModeState? _previousMode;

    public string[] SupportedTargets => new[] { "GPU_HYBRID_MODE" };

    public HybridModeHandler(
        HybridModeFeature hybridModeFeature,
        Services.GPUTransitionManager transitionManager)
    {
        _hybridModeFeature = hybridModeFeature ?? throw new ArgumentNullException(nameof(hybridModeFeature));
        _transitionManager = transitionManager ?? throw new ArgumentNullException(nameof(transitionManager));
    }

    public async Task ExecuteAsync(ResourceAction action)
    {
        // PHASE 1 FIX: Handle GPUTransitionProposal from HybridModeAgent
        if (action.Value is Services.GPUTransitionProposal proposal)
        {
            // Store previous mode for rollback
            if (_previousMode == null)
                _previousMode = proposal.CurrentMode;

            // Execute through GPUTransitionManager (thread-safe, atomic)
            var success = await _transitionManager.ExecuteTransitionAsync(proposal).ConfigureAwait(false);

            if (!success && Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"GPU transition execution failed or was cancelled");

            return;
        }

        // Legacy path: Direct HybridModeState (for backward compatibility)
        if (action.Value is HybridModeState mode)
        {
            // Store previous mode for rollback
            if (_previousMode == null)
                _previousMode = await _hybridModeFeature.GetStateAsync().ConfigureAwait(false);

            await _hybridModeFeature.SetStateAsync(mode).ConfigureAwait(false);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"GPU hybrid mode set to {mode} (legacy path)");
        }
    }

    public async Task RollbackAsync(ResourceAction action)
    {
        if (_previousMode != null)
        {
            // Use GPUTransitionManager for rollback (force critical priority)
            await _transitionManager.ForceTransitionAsync(
                _previousMode.Value,
                "Rollback from failed action").ConfigureAwait(false);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Rolled back GPU hybrid mode to {_previousMode}");
        }
    }
}

/// <summary>
/// Display Control Handler - Controls brightness and refresh rate
/// </summary>
public class DisplayControlHandler : IActionHandler
{
    private readonly DisplayBrightnessController? _brightnessController;
    private readonly RefreshRateFeature _refreshRateFeature;
    private readonly Dictionary<string, object> _previousValues = new();

    public string[] SupportedTargets => new[] { "DISPLAY_BRIGHTNESS", "DISPLAY_REFRESH_RATE" };

    public DisplayControlHandler(
        DisplayBrightnessController? brightnessController,
        RefreshRateFeature refreshRateFeature)
    {
        _brightnessController = brightnessController;
        _refreshRateFeature = refreshRateFeature ?? throw new ArgumentNullException(nameof(refreshRateFeature));
    }

    public async Task ExecuteAsync(ResourceAction action)
    {
        switch (action.Target)
        {
            case "DISPLAY_BRIGHTNESS":
                if (_brightnessController != null && action.Value is int brightness)
                {
                    // Store previous value (we can't read it, so just store the new value as reference)
                    if (!_previousValues.ContainsKey(action.Target))
                        _previousValues[action.Target] = brightness;

                    await _brightnessController.SetBrightnessAsync(brightness).ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Display brightness set to {brightness}%");
                }
                break;

            case "DISPLAY_REFRESH_RATE":
                if (action.Value is RefreshRate refreshRate)
                {
                    // Store previous value
                    if (!_previousValues.ContainsKey(action.Target))
                        _previousValues[action.Target] = await _refreshRateFeature.GetStateAsync().ConfigureAwait(false);

                    await _refreshRateFeature.SetStateAsync(refreshRate).ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Display refresh rate set to {refreshRate.Frequency}Hz");
                }
                break;
        }
    }

    public async Task RollbackAsync(ResourceAction action)
    {
        if (!_previousValues.TryGetValue(action.Target, out var previousValue))
            return;

        switch (action.Target)
        {
            case "DISPLAY_BRIGHTNESS":
                if (_brightnessController != null && previousValue is int brightness)
                {
                    await _brightnessController.SetBrightnessAsync(brightness).ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Rolled back display brightness to {brightness}%");
                }
                break;

            case "DISPLAY_REFRESH_RATE":
                if (previousValue is RefreshRate refreshRate)
                {
                    await _refreshRateFeature.SetStateAsync(refreshRate).ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Rolled back display refresh rate to {refreshRate.Frequency}Hz");
                }
                break;
        }
    }
}

/// <summary>
/// Keyboard Backlight Handler - Controls RGB keyboard backlight
/// </summary>
public class KeyboardBacklightHandler : IActionHandler
{
    private readonly RGBKeyboardBacklightController? _keyboardController;
    private readonly Dictionary<string, object> _previousValues = new();

    public string[] SupportedTargets => new[] { "KEYBOARD_RGB_STATE", "KEYBOARD_BRIGHTNESS" };

    public KeyboardBacklightHandler(RGBKeyboardBacklightController? keyboardController)
    {
        _keyboardController = keyboardController;
    }

    public async Task ExecuteAsync(ResourceAction action)
    {
        if (_keyboardController == null)
            return;

        switch (action.Target)
        {
            case "KEYBOARD_RGB_STATE":
                if (action.Value is bool enabled)
                {
                    // Store previous state (simplified - actual implementation would query current state)
                    if (!_previousValues.ContainsKey(action.Target))
                        _previousValues[action.Target] = enabled;

                    await _keyboardController.SetLightControlOwnerAsync(enabled).ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Keyboard RGB state set to {(enabled ? "ON" : "OFF")}");
                }
                break;

            case "KEYBOARD_BRIGHTNESS":
                if (action.Value is int brightness)
                {
                    // Store previous brightness
                    if (!_previousValues.ContainsKey(action.Target))
                        _previousValues[action.Target] = brightness;

                    // Note: Actual brightness control would require accessing the preset and modifying it
                    // This is a simplified placeholder
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Keyboard brightness would be set to {brightness}%");
                }
                break;
        }
    }

    public async Task RollbackAsync(ResourceAction action)
    {
        if (_keyboardController == null || !_previousValues.TryGetValue(action.Target, out var previousValue))
            return;

        switch (action.Target)
        {
            case "KEYBOARD_RGB_STATE":
                if (previousValue is bool enabled)
                {
                    await _keyboardController.SetLightControlOwnerAsync(enabled).ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Rolled back keyboard RGB state to {(enabled ? "ON" : "OFF")}");
                }
                break;

            case "KEYBOARD_BRIGHTNESS":
                if (previousValue is int brightness)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Rolled back keyboard brightness to {brightness}%");
                }
                break;
        }
    }
}

/// <summary>
/// Coordination Handler - Handles coordinated multi-agent actions
/// </summary>
public class CoordinationHandler : IActionHandler
{
    public string[] SupportedTargets => new[]
    {
        "COORDINATE_EMERGENCY_MODE",
        "COORDINATE_LOW_BATTERY_MODE",
        "COORDINATE_HIGH_POWER_CONSUMPTION",
        "SYSTEM_HIBERNATE_WARNING"
    };

    public Task ExecuteAsync(ResourceAction action)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Coordination signal: {action.Target}");

        return Task.CompletedTask;
    }

    public Task RollbackAsync(ResourceAction action)
    {
        // Coordination signals don't need rollback
        return Task.CompletedTask;
    }
}

/// <summary>
/// Elite Profile Handler - Handles elite hardware control profiles
/// Coordinates: MSR Access, NVAPI, PCIe ASPM, Process Priority, Windows Power
/// </summary>
public class EliteProfileHandler : IActionHandler
{
    private ElitePowerProfile? _previousProfile;

    public string[] SupportedTargets => new[] { "ELITE_PROFILE" };

    public async Task ExecuteAsync(ResourceAction action)
    {
        if (action.Value is not ElitePowerProfile profile)
            return;

        // Extract EliteFeaturesManager from action parameters
        if (!action.Parameters.TryGetValue("EliteFeaturesManager", out var managerObj) ||
            managerObj is not EliteFeaturesManager manager)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Elite profile action missing EliteFeaturesManager in parameters");
            return;
        }

        // Store previous profile for rollback
        if (_previousProfile == null)
        {
            var status = manager.GetCurrentProfileStatus();
            _previousProfile = status.ActiveProfile;
        }

        // Apply the elite profile with power-source awareness
        // This automatically adapts based on AC vs Battery power
        // For example: Gaming on battery -> Balanced profile (saves 55W)
        await manager.ApplyPowerSourceOptimizationsAsync(profile).ConfigureAwait(false);

        // Log the result
        if (Log.Instance.IsTraceEnabled)
        {
            var status = manager.GetCurrentProfileStatus();
            Log.Instance.Trace($"Elite profile applied: {status.GetStatusSummary()}");

            var availability = manager.GetFeatureAvailability();
            Log.Instance.Trace($"Elite features: {availability.GetAvailabilitySummary()}");
        }
    }

    public async Task RollbackAsync(ResourceAction action)
    {
        if (_previousProfile == null)
            return;

        // Extract EliteFeaturesManager from action parameters
        if (!action.Parameters.TryGetValue("EliteFeaturesManager", out var managerObj) ||
            managerObj is not EliteFeaturesManager manager)
            return;

        // Restore previous profile with power-source awareness
        await manager.ApplyPowerSourceOptimizationsAsync(_previousProfile.Value).ConfigureAwait(false);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Rolled back elite profile to {_previousProfile}");
    }
}
